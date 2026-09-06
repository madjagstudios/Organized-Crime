using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Il2CppFishNet;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using S1API.Lifecycle;
using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace OrganizedCrime.QuestLifecycleProof;

public sealed class QuestLifecycleProofRuntime : IDisposable
{
    private sealed record QuestLookup(Quest? Quest, QuestRuntimeAvailability Availability);

    private static readonly string CurrentProcessSessionId =
        $"{Environment.ProcessId}:{Guid.NewGuid():N}";

    public const string MissionKey = "oc44.typed-quest.diagnostic";
    public const int Attempt = 1;

    private const string InitialLoadEpoch = "load-0";
    private const string DiagnosticGuid = "1f4c4e2b-5af4-4a34-9d5e-5f1a2b7c8d90";
    private const string ObjectiveTitle = "OC-44 typed Quest objective";
    private const string EvidenceFileName = "oc44-typed-quest-proof.jsonl";
    private const string GuidEvidenceFileName = "oc44-typed-quest-proof-guid.json";

    private readonly Action<string> _log;
    private readonly QuestLifecycleProofSession _session = new(MissionKey, Attempt, InitialLoadEpoch);
    private readonly QuestProofSubscriptionLedger _subscriptions = new();
    private readonly List<QuestLifecycleObservation> _observations = new();
    private Quest? _quest;
    private QuestEntry? _entry;
    private QuestGuidExperimentObservation? _guidExperiment;
    private string _loadEpoch = InitialLoadEpoch;
    private bool _guidExperimentEnabled;
    private bool _persistedSessionReplayed;
    private bool _persistedSessionValid = true;
    private bool _disposed;
    private QuestProofProcessSession? _processSession;

    public QuestLifecycleProofRuntime(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
    }

    public IReadOnlyList<QuestLifecycleObservation> Observations => _observations;

    public QuestLifecycleProofResult? LastEvaluation { get; private set; }

    public void Initialize()
    {
        if (_disposed)
            return;

        LoadPersistedObservations();
        _processSession = QuestProofProcessSession.Restore(
            _observations,
            CurrentProcessSessionId,
            Guid.NewGuid().ToString("N"));
        if (!_processSession.PersistedChainValid)
        {
            _persistedSessionValid = false;
            Log("evidence-invalid", $"reason={_processSession.InvalidReason}; cleanup is required before rerun");
        }
        GameLifecycle.OnPreLoad += HandlePreLoad;
        _subscriptions.Track(() => GameLifecycle.OnPreLoad -= HandlePreLoad);
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        _subscriptions.Track(() => GameLifecycle.OnLoadComplete -= HandleLoadComplete);
        GameLifecycle.OnSaveStart += HandleSaveStart;
        _subscriptions.Track(() => GameLifecycle.OnSaveStart -= HandleSaveStart);
        MelonLogger.Msg("[OC-44] initialized; Launch A: F8 create, F9 begin, F10 objective, save, exit; Launch B: reload, F11 terminal, save, exit; Launch C: reload, F12 evaluate. F7 is optional GUID evidence only.");
    }

    public void Update()
    {
        if (_disposed)
            return;

        try
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _guidExperimentEnabled = true;
                Log("guid-experiment-enabled", $"requestedGuid={DiagnosticGuid}");
            }

            if (Input.GetKeyDown(KeyCode.F8))
                TryCreate();
            if (Input.GetKeyDown(KeyCode.F9))
                TryBegin();
            if (Input.GetKeyDown(KeyCode.F10))
                TryObjective();
            if (Input.GetKeyDown(KeyCode.F11))
                TryTerminal();
            if (Input.GetKeyDown(KeyCode.F12))
                EvaluateAndLog("owner-f12");
        }
        catch (Exception exception)
        {
            Log("runtime-stop", $"unexpected diagnostic exception: {exception.GetType().Name}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscriptions.Dispose();
        _session.Dispose();
        _quest = null;
        _entry = null;
        Log("disposed", "lifecycle handlers detached; no production ledger or save was written");
    }

    private void HandlePreLoad() => Log("pre-load", "load boundary observed; waiting for full reload");

    private void HandleLoadComplete()
    {
        ReplayPersistedSession();
        if (_processSession is null)
        {
            _persistedSessionValid = false;
            Log("evidence-invalid", "process-session marker is unavailable; cleanup is required before rerun");
            return;
        }

        var boundary = _processSession.ObserveLoadComplete(_observations);
        if (!boundary.Accepted)
        {
            _persistedSessionValid = false;
            Log("load-boundary-rejected", boundary.Reason);
            return;
        }

        if (boundary.IsDuplicateNoOp)
        {
            Log("load-complete-noop", boundary.Reason);
            return;
        }

        if (boundary.ShouldRecordReload)
        {
            _loadEpoch = boundary.NextLoadEpoch!;
            var lookup = FindQuestByMissionKey();
            _quest = lookup.Quest;
            CaptureReloadGuidObservation(_quest);
            TryRecord(
                QuestLifecyclePhase.FullReload,
                _quest,
                isReconstruction: boundary.IsReconstruction,
                terminalOutcome: QuestTerminalOutcome.None,
                nativeAction: null,
                reloadClassification: boundary.ReloadClassification,
                nativeStateAvailability: lookup.Availability);
            Log(
                "reload-observation",
                boundary.ReloadClassification == QuestReloadClassification.CompletedQuestAbsentNoReplay
                    ? "Launch C no-replay observation recorded; owner F12 evaluation remains required"
                    : "Launch B active reconstruction recorded; owner F11 terminal remains required");
            return;
        }

        Log("load-complete", $"epoch={_loadEpoch}; owner may begin the diagnostic sequence");
    }

    private void HandleSaveStart()
    {
        TryRecord(
            QuestLifecyclePhase.Save,
            _quest,
            isReconstruction: false,
            terminalOutcome: QuestTerminalOutcome.None,
            nativeAction: null);
    }

    private void TryCreate()
    {
        if (!TryAdmit(QuestLifecyclePhase.Created, out var admission))
            return;

        try
        {
            _quest = QuestManager.CreateQuest<DiagnosticQuest>(_guidExperimentEnabled ? DiagnosticGuid : null);
            if (_quest is null)
            {
                Log("native-missing", "QuestManager.CreateQuest returned no DiagnosticQuest");
                return;
            }

            Record(admission, QuestLifecyclePhase.Created, _quest, false, QuestTerminalOutcome.None);
            if (_guidExperimentEnabled)
            {
                _guidExperiment = CaptureGuidExperiment(_quest, DiagnosticGuid, null);
                WriteGuidEvidence();
            }
        }
        catch (Exception exception)
        {
            Log("native-create-failed", $"{exception.GetType().Name}; S1API/native state is unavailable");
        }
    }

    private void TryBegin()
    {
        if (_quest is null)
        {
            Log("action-rejected", "begin requires a created DiagnosticQuest");
            return;
        }

        if (!TryAdmit(QuestLifecyclePhase.Begun, out var admission))
            return;

        try
        {
            _quest.Begin();
            Record(admission, QuestLifecyclePhase.Begun, _quest, false, QuestTerminalOutcome.None);
        }
        catch (Exception exception)
        {
            Log("native-begin-failed", $"{exception.GetType().Name}; S1API/native state is unavailable");
        }
    }

    private void TryObjective()
    {
        if (_quest is null)
        {
            Log("action-rejected", "objective requires a created DiagnosticQuest");
            return;
        }

        if (!TryAdmit(QuestLifecyclePhase.ObjectiveEntry, out var entryAdmission))
            return;

        try
        {
            if (_quest is not DiagnosticQuest diagnosticQuest)
            {
                Log("action-rejected", "objective requires the typed DiagnosticQuest reconstruction");
                return;
            }

            _entry = diagnosticQuest.AddDiagnosticEntry(ObjectiveTitle);
            _entry.Begin();
            Record(entryAdmission, QuestLifecyclePhase.ObjectiveEntry, _quest, false, QuestTerminalOutcome.None);
        }
        catch (Exception exception)
        {
            Log("native-objective-entry-failed", $"{exception.GetType().Name}; S1API/native state is unavailable");
            return;
        }

        if (!TryAdmit(QuestLifecyclePhase.ObjectiveUpdate, out var updateAdmission))
            return;

        try
        {
            _entry.SetState(QuestState.Active);
            Record(updateAdmission, QuestLifecyclePhase.ObjectiveUpdate, _quest, false, QuestTerminalOutcome.None);
        }
        catch (Exception exception)
        {
            Log("native-objective-update-failed", $"{exception.GetType().Name}; S1API/native state is unavailable");
        }
    }

    private void TryTerminal()
    {
        if (_quest is null)
        {
            Log("action-rejected", "terminal requires a created DiagnosticQuest");
            return;
        }

        if (!TryAdmit(
                QuestLifecyclePhase.Terminal,
                out var admission,
                QuestTerminalOutcome.Completed))
            return;

        try
        {
            _quest.Complete();
            Record(admission, QuestLifecyclePhase.Terminal, _quest, false, QuestTerminalOutcome.Completed);
        }
        catch (Exception exception)
        {
            Log("native-terminal-failed", $"{exception.GetType().Name}; S1API/native state is unavailable");
        }
    }

    private bool TryAdmit(
        QuestLifecyclePhase phase,
        out QuestLifecycleActionAdmission admission,
        QuestTerminalOutcome terminalOutcome = QuestTerminalOutcome.None,
        bool isReconstruction = false,
        QuestReloadClassification reloadClassification = QuestReloadClassification.None)
    {
        if (!_persistedSessionValid)
        {
            admission = new QuestLifecycleActionAdmission(false, false, "persisted proof evidence is invalid; cleanup is required before rerun");
            Log("action-rejected", $"phase={phase}; reason={admission.Reason}");
            return false;
        }

        var context = ReadHostContext();
        admission = _session.TryAdmit(
            new QuestLifecycleActionRequest(
                MissionKey,
                Attempt,
                _loadEpoch,
                phase,
                QuestProofActor.Host,
                terminalOutcome,
                isReconstruction,
                false,
                reloadClassification),
            context);
        if (!admission.Accepted)
            Log("action-rejected", $"phase={phase}; reason={admission.Reason}");
        return admission.Accepted;
    }

    private void TryRecord(
        QuestLifecyclePhase phase,
        Quest? quest,
        bool isReconstruction,
        QuestTerminalOutcome terminalOutcome,
        Action? nativeAction,
        QuestReloadClassification reloadClassification = QuestReloadClassification.None,
        QuestRuntimeAvailability nativeStateAvailability = QuestRuntimeAvailability.Available)
    {
        if (!TryAdmit(phase, out var admission, terminalOutcome, isReconstruction, reloadClassification))
            return;

        if (nativeAction is not null)
        {
            try
            {
                nativeAction();
            }
            catch (Exception exception)
            {
                Log("native-action-failed", $"phase={phase}; error={exception.GetType().Name}");
                return;
            }
        }

        Record(admission, phase, quest, isReconstruction, terminalOutcome, reloadClassification, nativeStateAvailability);
    }

    private void Record(
        QuestLifecycleActionAdmission admission,
        QuestLifecyclePhase phase,
        Quest? quest,
        bool isReconstruction,
        QuestTerminalOutcome terminalOutcome,
        QuestReloadClassification reloadClassification = QuestReloadClassification.None,
        QuestRuntimeAvailability nativeStateAvailability = QuestRuntimeAvailability.Available)
    {
        var nativeAvailable = quest is not null;
        var presentation = QuestPresentationObserver.Observe(quest);
        var observation = new QuestLifecycleObservation(
            MissionKey,
            Attempt,
            _loadEpoch,
            nativeAvailable ? NativeReference(quest!) : null,
            nativeAvailable ? QuestNativeIdentity.Opaque : QuestNativeIdentity.Empty,
            phase,
            QuestProofActor.Host,
            terminalOutcome,
            admission.StandingReceipt,
            admission.RewardReceipt,
            isReconstruction,
            false,
            QuestRuntimeAvailability.Available,
            nativeAvailable ? QuestRuntimeAvailability.Available : nativeStateAvailability,
            presentation.Title,
            presentation.QuestState,
            presentation.SoleObjectiveState,
            quest is null && reloadClassification == QuestReloadClassification.CompletedQuestAbsentNoReplay
                ? false
                : ReadManagerMembership(quest),
            reloadClassification,
            _processSession?.RunChainId,
            _processSession?.ProcessSessionId,
            phase == QuestLifecyclePhase.FullReload &&
                _processSession?.CurrentProcessLoadBoundaryObserved == true);
        _observations.Add(observation);
        WriteEvidence(observation);
        Log("observation", $"phase={phase}; epoch={_loadEpoch}; native={(nativeAvailable ? "available" : "missing")}; duplicate={admission.Duplicate}");
    }

    private void ReplayPersistedSession()
    {
        if (_persistedSessionReplayed)
            return;

        _persistedSessionReplayed = true;
        if (_observations.Count == 0)
            return;

        _loadEpoch = _observations[^1].LoadEpoch;
        foreach (var observation in _observations)
        {
            var admission = _session.TryAdmit(
                new QuestLifecycleActionRequest(
                    observation.MissionKey,
                    observation.Attempt,
                    observation.LoadEpoch,
                    observation.Phase,
                    observation.Actor,
                    observation.TerminalOutcome,
                    observation.IsReconstruction,
                    observation.ClientOrUiMutation,
                    observation.ReloadClassification),
                new QuestProofHostContext(true, true));
            if (!admission.Accepted)
            {
                _persistedSessionValid = false;
                Log("evidence-invalid", $"persisted phase={observation.Phase}; reason={admission.Reason}; cleanup is required before rerun");
            }
        }
    }

    private QuestGuidExperimentObservation CaptureGuidExperiment(
        Quest quest,
        string requestedGuid,
        QuestGuidExperimentObservation? previous)
    {
        try
        {
            var native = QuestPresentationObserver.ReadNativeQuest(quest);
            if (native is null)
                return new(requestedGuid, null, previous?.ReloadStaticGuid, false, false,
                    QuestRuntimeAvailability.MissingNativeState, "S1API wrapper has no readable native Quest");

            var setGuid = native.GetType().GetMethod("SetGUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            setGuid?.Invoke(native, [requestedGuid]);
            var staticGuid = ReadStaticGuid(native);
            return new(
                requestedGuid,
                previous?.InitialStaticGuid ?? staticGuid,
                previous?.ReloadStaticGuid,
                previous?.SurvivedFullReload ?? false,
                previous?.GetQuestByGuidMatchedAfterReload ?? false,
                staticGuid is null ? QuestRuntimeAvailability.MissingNativeState : QuestRuntimeAvailability.Available,
                staticGuid is null ? "StaticGUID was unavailable" : "SetGUID observation captured; identity remains non-authoritative");
        }
        catch (Exception exception)
        {
            return new(requestedGuid, previous?.InitialStaticGuid, previous?.ReloadStaticGuid, false, false,
                QuestRuntimeAvailability.MissingNativeState, $"SetGUID observation threw {exception.GetType().Name}");
        }
    }

    private void CaptureReloadGuidObservation(Quest? quest)
    {
        if (!_guidExperimentEnabled || _guidExperiment is null)
            return;

        try
        {
            var byGuid = QuestManager.GetQuestByGuid(DiagnosticGuid);
            var native = QuestPresentationObserver.ReadNativeQuest(quest);
            var reloadStaticGuid = native is null ? null : ReadStaticGuid(native);
            _guidExperiment = _guidExperiment with
            {
                ReloadStaticGuid = reloadStaticGuid,
                SurvivedFullReload = reloadStaticGuid is not null &&
                    string.Equals(_guidExperiment.InitialStaticGuid, reloadStaticGuid, StringComparison.Ordinal),
                GetQuestByGuidMatchedAfterReload = byGuid is not null,
                Availability = native is null ? QuestRuntimeAvailability.MissingNativeState : QuestRuntimeAvailability.Available,
                Note = "full-reload lookup captured; native GUID stability does not gate PASS"
            };
            WriteGuidEvidence();
            Log("guid-observation", JsonSerializer.Serialize(_guidExperiment));
        }
        catch (Exception exception)
        {
            _guidExperiment = _guidExperiment with
            {
                Availability = QuestRuntimeAvailability.MissingNativeState,
                Note = $"full-reload GUID lookup threw {exception.GetType().Name}"
            };
            WriteGuidEvidence();
            Log("guid-observation", JsonSerializer.Serialize(_guidExperiment));
        }
    }

    private static QuestLookup FindQuestByMissionKey()
    {
        try
        {
            return new QuestLookup(QuestManager.GetQuestByName(MissionKey), QuestRuntimeAvailability.Available);
        }
        catch
        {
            return new QuestLookup(null, QuestRuntimeAvailability.MissingNativeState);
        }
    }

    private void EvaluateAndLog(string trigger)
    {
        LastEvaluation = QuestLifecycleProofEvaluator.Evaluate(
            _observations,
            _guidExperiment,
            _processSession?.EvaluationContext);
        Log("evaluation", $"trigger={trigger}; decision={LastEvaluation.Decision}; reasons={string.Join(" | ", LastEvaluation.Reasons)}");
    }

    private void LoadPersistedObservations()
    {
        try
        {
            var path = EvidencePath();
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    var observation = JsonSerializer.Deserialize<QuestLifecycleObservation>(line);
                    if (observation is not null)
                        _observations.Add(observation);
                }
                catch (JsonException)
                {
                    // GUID and diagnostic log lines are intentionally not observations.
                }
            }

            if (_observations.Count > 0)
                Log("evidence-restored", $"observations={_observations.Count}; path={path}");

            var guidPath = GuidEvidencePath();
            if (File.Exists(guidPath))
            {
                _guidExperiment = JsonSerializer.Deserialize<QuestGuidExperimentObservation>(File.ReadAllText(guidPath));
                _guidExperimentEnabled = _guidExperiment is not null;
            }
        }
        catch (Exception exception)
        {
            Log("evidence-unavailable", $"{exception.GetType().Name}; live observations remain in memory only");
        }
    }

    private void WriteEvidence(QuestLifecycleObservation observation)
    {
        try
        {
            File.AppendAllText(EvidencePath(), JsonSerializer.Serialize(observation) + Environment.NewLine);
        }
        catch (Exception exception)
        {
            Log("evidence-write-failed", exception.GetType().Name);
        }
    }

    private void WriteGuidEvidence()
    {
        if (_guidExperiment is null)
            return;

        try
        {
            File.WriteAllText(GuidEvidencePath(), JsonSerializer.Serialize(_guidExperiment));
        }
        catch (Exception exception)
        {
            Log("guid-evidence-write-failed", exception.GetType().Name);
        }
    }

    private string EvidencePath() => Path.Combine(Application.persistentDataPath, EvidenceFileName);

    private string GuidEvidencePath() => Path.Combine(Application.persistentDataPath, GuidEvidenceFileName);

    private void Log(string eventName, string message) => _log($"event={eventName}; {message}");

    private static QuestProofHostContext ReadHostContext()
    {
        try
        {
            var server = InstanceFinder.ServerManager;
            var client = InstanceFinder.ClientManager;
            var isHost = server is not null && client is not null && server.OneServerStarted() && client.Started;
            var serverPlayers = 0;
            foreach (var player in Player.PlayerList ?? new Il2CppSystem.Collections.Generic.List<Player>())
            {
                if (player is not null && player.IsServerInitialized)
                    serverPlayers++;
            }
            return new QuestProofHostContext(isHost, serverPlayers == 1);
        }
        catch
        {
            return new QuestProofHostContext(false, false);
        }
    }

    private static string NativeReference(Quest quest) =>
        $"{quest.GetType().FullName}:{RuntimeHelpers.GetHashCode(quest)}";

    private static string? ReadStaticGuid(object native) =>
        native.GetType().GetProperty("StaticGUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(native) as string;

    private static bool? ReadManagerMembership(Quest? quest)
    {
        if (quest is null)
            return null;

        try
        {
            var field = typeof(QuestManager).GetField("Quests", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is not System.Collections.IEnumerable quests)
                return null;
            return quests.Cast<object>().Any(candidate => ReferenceEquals(candidate, quest));
        }
        catch
        {
            return null;
        }
    }
}
