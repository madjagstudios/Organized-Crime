namespace OrganizedCrime.QuestLifecycleProof;

public enum QuestLifecyclePhase
{
    Created,
    Begun,
    ObjectiveEntry,
    ObjectiveUpdate,
    Terminal,
    Save,
    FullReload
}

public enum QuestProofActor
{
    Host,
    Client,
    Ui,
    Unknown
}

public enum QuestNativeIdentity
{
    Opaque,
    Stable,
    Empty
}

public enum QuestTerminalOutcome
{
    None,
    Completed,
    Failed
}

public enum QuestLifecycleProofDecision
{
    Pass,
    Inconclusive,
    Stop
}

public enum QuestRuntimeAvailability
{
    Available,
    MissingS1Api,
    MissingNativeState
}

public enum QuestReloadClassification
{
    None,
    ActiveReconstruction,
    CompletedQuestAbsentNoReplay
}

public sealed record QuestLifecycleObservation(
    string MissionKey,
    int Attempt,
    string LoadEpoch,
    string? NativeReference,
    QuestNativeIdentity NativeIdentity,
    QuestLifecyclePhase Phase,
    QuestProofActor Actor,
    QuestTerminalOutcome TerminalOutcome,
    string? StandingReceipt,
    string? RewardReceipt,
    bool IsReconstruction,
    bool ClientOrUiMutation,
    QuestRuntimeAvailability S1ApiAvailability = QuestRuntimeAvailability.Available,
    QuestRuntimeAvailability NativeStateAvailability = QuestRuntimeAvailability.Available,
    string? NativeTitle = null,
    string? NativeQuestState = null,
    string? NativeObjectiveState = null,
    bool? ManagerMembership = null,
    QuestReloadClassification ReloadClassification = QuestReloadClassification.None,
    string? RunChainId = null,
    string? ProcessSessionId = null,
    bool LoadBoundaryObserved = false);

public sealed record QuestGuidExperimentObservation(
    string RequestedGuid,
    string? InitialStaticGuid,
    string? ReloadStaticGuid,
    bool SurvivedFullReload,
    bool GetQuestByGuidMatchedAfterReload,
    QuestRuntimeAvailability Availability,
    string Note);

public readonly record struct QuestProofHostContext(
    bool IsAuthoritativeHost,
    bool IsSinglePlayer);

public sealed record QuestLifecycleActionRequest(
    string MissionKey,
    int Attempt,
    string LoadEpoch,
    QuestLifecyclePhase Phase,
    QuestProofActor Actor,
    QuestTerminalOutcome TerminalOutcome = QuestTerminalOutcome.None,
    bool IsReconstruction = false,
    bool ClientOrUiMutation = false,
    QuestReloadClassification ReloadClassification = QuestReloadClassification.None);

public sealed record QuestLifecycleActionAdmission(
    bool Accepted,
    bool Duplicate,
    string Reason,
    string? StandingReceipt = null,
    string? RewardReceipt = null);

public sealed class QuestLifecycleProofSession : IDisposable
{
    private readonly string _missionKey;
    private readonly int _attempt;
    private readonly string _initialLoadEpoch;
    private string _currentLoadEpoch;
    private readonly HashSet<QuestLifecyclePhase> _phases = new();
    private bool _disposed;
    private bool _saveSeenInCurrentEpoch;
    private bool _activeReconstructionSeen;
    private bool _completedQuestAbsenceSeen;
    private QuestTerminalOutcome _terminalOutcome;
    private int _lastPhaseRank = -1;

    public QuestLifecycleProofSession(string missionKey, int attempt, string initialLoadEpoch)
    {
        _missionKey = missionKey;
        _attempt = attempt;
        _initialLoadEpoch = initialLoadEpoch;
        _currentLoadEpoch = initialLoadEpoch;
    }

    public bool IsDisposed => _disposed;

    public QuestLifecycleActionAdmission TryAdmit(
        QuestLifecycleActionRequest request,
        QuestProofHostContext context)
    {
        if (_disposed)
            return Reject("proof session is disposed");

        if (!context.IsAuthoritativeHost || !context.IsSinglePlayer)
            return Reject("authoritative single-player host context is required");

        if (request.Actor != QuestProofActor.Host || request.ClientOrUiMutation)
            return Reject("client/UI actions cannot drive the proof");

        if (!string.Equals(request.MissionKey, _missionKey, StringComparison.Ordinal))
            return Reject("wrong OC mission key");

        if (request.Attempt != _attempt)
            return Reject("stale mission attempt");

        if (request.Phase == QuestLifecyclePhase.FullReload)
            return AdmitFullReload(request);

        if (!string.Equals(request.LoadEpoch, _currentLoadEpoch, StringComparison.Ordinal))
            return Reject("stale load epoch");

        if (_phases.Contains(request.Phase))
            return Reject($"duplicate {request.Phase} action in one load epoch");

        if (request.Phase == QuestLifecyclePhase.Created &&
            !string.Equals(_currentLoadEpoch, _initialLoadEpoch, StringComparison.Ordinal))
            return Reject("native Quest creation is not allowed during reconstruction");

        var rank = PhaseRank(request.Phase);
        if (rank < _lastPhaseRank)
            return Reject("lifecycle action arrived out of order");

        if (request.Phase == QuestLifecyclePhase.Terminal && !_activeReconstructionSeen)
            return Reject("terminal requires the active Quest reconstruction");

        if (request.Phase == QuestLifecyclePhase.Terminal && request.TerminalOutcome == QuestTerminalOutcome.None)
            return Reject("terminal outcome is missing");

        if (request.Phase == QuestLifecyclePhase.Save &&
            string.Equals(_currentLoadEpoch, _initialLoadEpoch, StringComparison.Ordinal) &&
            _terminalOutcome != QuestTerminalOutcome.None)
            return Reject("terminal save must follow reconstruction");

        _phases.Add(request.Phase);
        _lastPhaseRank = rank;
        _saveSeenInCurrentEpoch |= request.Phase == QuestLifecyclePhase.Save;

        string? standingReceipt = null;
        string? rewardReceipt = null;
        if (request.Phase == QuestLifecyclePhase.Terminal)
        {
            _terminalOutcome = request.TerminalOutcome;
            standingReceipt = $"proof-standing:{_missionKey}:{_attempt}:{request.TerminalOutcome}";
            rewardReceipt = $"proof-reward:{_missionKey}:{_attempt}:{request.TerminalOutcome}";
        }

        return new QuestLifecycleActionAdmission(
            Accepted: true,
            Duplicate: false,
            Reason: $"{request.Phase} admitted for host proof",
            StandingReceipt: standingReceipt,
            RewardReceipt: rewardReceipt);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _phases.Clear();
    }

    private QuestLifecycleActionAdmission AdmitFullReload(QuestLifecycleActionRequest request)
    {
        var classification = request.ReloadClassification switch
        {
            QuestReloadClassification.None when request.IsReconstruction => QuestReloadClassification.ActiveReconstruction,
            QuestReloadClassification.None => QuestReloadClassification.CompletedQuestAbsentNoReplay,
            _ => request.ReloadClassification
        };

        if (string.Equals(request.LoadEpoch, _currentLoadEpoch, StringComparison.Ordinal))
        {
            if (classification == QuestReloadClassification.ActiveReconstruction && _activeReconstructionSeen)
                return Reject("duplicate active Quest reconstruction was observed");
            if (classification == QuestReloadClassification.CompletedQuestAbsentNoReplay && _completedQuestAbsenceSeen)
                return Reject("duplicate completed Quest no-replay observation was observed");
            return Reject("full reload reused the pre-reload epoch");
        }

        if (!_saveSeenInCurrentEpoch)
            return Reject("full reload was observed before normal save");

        if (classification == QuestReloadClassification.ActiveReconstruction)
        {
            if (!request.IsReconstruction)
                return Reject("active Quest reconstruction classification is missing reconstruction evidence");
            if (_activeReconstructionSeen)
                return Reject("duplicate active Quest reconstruction was observed");
        }
        else if (classification == QuestReloadClassification.CompletedQuestAbsentNoReplay)
        {
            if (request.IsReconstruction)
                return Reject("completed Quest no-replay classification cannot be a reconstruction");
            if (!_activeReconstructionSeen || _terminalOutcome == QuestTerminalOutcome.None)
                return Reject("completed Quest absence requires the reconstructed terminal save");
            if (_completedQuestAbsenceSeen)
                return Reject("duplicate completed Quest no-replay observation was observed");
        }
        else
        {
            return Reject("full reload classification is missing");
        }

        _currentLoadEpoch = request.LoadEpoch;
        _phases.Clear();
        _lastPhaseRank = -1;
        _saveSeenInCurrentEpoch = false;
        _activeReconstructionSeen |= classification == QuestReloadClassification.ActiveReconstruction;
        _completedQuestAbsenceSeen |= classification == QuestReloadClassification.CompletedQuestAbsentNoReplay;
        return new QuestLifecycleActionAdmission(
            true,
            false,
            classification == QuestReloadClassification.ActiveReconstruction
                ? "active Quest reconstruction admitted for host proof"
                : "completed Quest absence admitted as positive no-replay evidence");
    }

    private static int PhaseRank(QuestLifecyclePhase phase) => phase switch
    {
        QuestLifecyclePhase.Created => 0,
        QuestLifecyclePhase.Begun => 1,
        QuestLifecyclePhase.ObjectiveEntry => 2,
        QuestLifecyclePhase.ObjectiveUpdate => 3,
        QuestLifecyclePhase.Terminal => 4,
        QuestLifecyclePhase.Save => 5,
        QuestLifecyclePhase.FullReload => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
    };

    private static QuestLifecycleActionAdmission Reject(string reason) =>
        new(false, false, reason);
}

public sealed class QuestProofSubscriptionLedger : IDisposable
{
    private readonly List<Action> _detachments = new();
    private bool _disposed;

    public bool IsDisposed => _disposed;

    public void Track(Action detachment)
    {
        ArgumentNullException.ThrowIfNull(detachment);
        if (_disposed)
        {
            detachment();
            return;
        }

        _detachments.Add(detachment);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var detachment in _detachments.AsEnumerable().Reverse())
            detachment();
        _detachments.Clear();
    }
}

public sealed record QuestLifecycleProofResult(
    QuestLifecycleProofDecision Decision,
    IReadOnlyList<string> Reasons);

public static class QuestLifecycleProofEvaluator
{
    private const string RequiredActiveState = "Active";

    private static readonly QuestLifecyclePhase[] ExpectedPhases =
    [
        QuestLifecyclePhase.Created,
        QuestLifecyclePhase.Begun,
        QuestLifecyclePhase.ObjectiveEntry,
        QuestLifecyclePhase.ObjectiveUpdate,
        QuestLifecyclePhase.Save,
        QuestLifecyclePhase.FullReload,
        QuestLifecyclePhase.Terminal,
        QuestLifecyclePhase.Save,
        QuestLifecyclePhase.FullReload
    ];

    public static QuestLifecycleProofResult Evaluate(
        IReadOnlyList<QuestLifecycleObservation> observations,
        QuestGuidExperimentObservation? guidExperiment = null,
        QuestProofEvaluationContext? evaluationContext = null)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0)
            return Inconclusive("no lifecycle observations were supplied");

        if (observations.Any(x => x.ClientOrUiMutation))
            return Stop("client/UI mutation was observed");

        if (observations.Any(x => x.Actor == QuestProofActor.Unknown))
            return Stop("authority is unknown for at least one observation");

        if (observations.Any(x => x.Actor != QuestProofActor.Host))
            return Stop("authority drifted away from the host");

        if (observations.Any(x => x.Actor != QuestProofActor.Host &&
            (x.StandingReceipt is not null || x.RewardReceipt is not null)))
            return Stop("Standing or reward authorization was emitted outside the host");

        var missionKeys = observations.Select(x => x.MissionKey).Distinct(StringComparer.Ordinal).ToArray();
        if (missionKeys.Length != 1)
            return Stop("multiple OC mission keys were observed for one proof run");

        var attempts = observations.Select(x => x.Attempt).Distinct().ToArray();
        if (attempts.Length != 1 || attempts[0] < 1)
            return Stop("mission attempt identity drifted");

        if (observations.Any(x => x.S1ApiAvailability != QuestRuntimeAvailability.Available))
            return Inconclusive("S1API state is unavailable; native proof cannot be classified");

        if (observations.Any(x => x.NativeStateAvailability != QuestRuntimeAvailability.Available))
        {
            var missingActiveReconstruction = observations.Any(x =>
                x.Phase == QuestLifecyclePhase.FullReload &&
                x.ReloadClassification == QuestReloadClassification.ActiveReconstruction);
            return Inconclusive(missingActiveReconstruction
                ? "native state is unavailable; active Quest reconstruction cannot be classified"
                : "native state is unavailable; native proof cannot be classified");
        }

        var activeReloads = observations.Where(x =>
            x.Phase == QuestLifecyclePhase.FullReload &&
            x.ReloadClassification == QuestReloadClassification.ActiveReconstruction).ToArray();
        var noReplayReloads = observations.Where(x =>
            x.Phase == QuestLifecyclePhase.FullReload &&
            x.ReloadClassification == QuestReloadClassification.CompletedQuestAbsentNoReplay).ToArray();
        var unclassifiedReload = observations.Any(x =>
            x.Phase == QuestLifecyclePhase.FullReload &&
            x.ReloadClassification == QuestReloadClassification.None);

        if (unclassifiedReload)
            return Inconclusive("full reload classification is missing");
        if (activeReloads.Length == 0)
            return Inconclusive("active Quest reconstruction was not observed");
        if (activeReloads.Length > 1)
            return Stop("duplicate active Quest reconstruction was observed");
        if (noReplayReloads.Length == 0)
            return Inconclusive("completed Quest no-replay confirmation was not observed");
        if (noReplayReloads.Length > 1)
            return Stop("duplicate completed Quest no-replay observation was observed");

        var hostObservations = observations.Where(x => x.Actor == QuestProofActor.Host).ToArray();
        if (hostObservations.Length != ExpectedPhases.Length)
        {
            if (hostObservations.Length > ExpectedPhases.Length)
                return Stop("duplicate lifecycle observation was observed");
            return Inconclusive("required three-launch lifecycle evidence is missing");
        }

        for (var index = 0; index < ExpectedPhases.Length; index++)
        {
            if (hostObservations[index].Phase != ExpectedPhases[index])
                return Stop("host lifecycle ordering is inconsistent");
        }

        var firstCreate = hostObservations[0];
        var launchASave = hostObservations[4];
        var activeReload = activeReloads[0];
        var noReplayReload = noReplayReloads[0];
        if (string.IsNullOrWhiteSpace(firstCreate.LoadEpoch) ||
            hostObservations.Take(5).Any(x => !string.Equals(x.LoadEpoch, firstCreate.LoadEpoch, StringComparison.Ordinal)))
            return Stop("Launch A used inconsistent load epochs");
        if (string.Equals(activeReload.LoadEpoch, firstCreate.LoadEpoch, StringComparison.Ordinal) ||
            !string.Equals(hostObservations[6].LoadEpoch, activeReload.LoadEpoch, StringComparison.Ordinal) ||
            !string.Equals(hostObservations[7].LoadEpoch, activeReload.LoadEpoch, StringComparison.Ordinal) ||
            string.Equals(noReplayReload.LoadEpoch, activeReload.LoadEpoch, StringComparison.Ordinal) ||
            string.Equals(noReplayReload.LoadEpoch, firstCreate.LoadEpoch, StringComparison.Ordinal))
            return Stop("full reload reused or mixed load epochs");

        if (hostObservations.Any(x => string.IsNullOrWhiteSpace(x.RunChainId)))
            return Stop("run-chain identity is missing; proof-local evidence cleanup is required");
        var runChainIds = hostObservations.Select(x => x.RunChainId!).Distinct(StringComparer.Ordinal).ToArray();
        if (runChainIds.Length != 1)
            return Stop("run-chain identity drifted across the three-launch proof");
        if (hostObservations.Any(x => string.IsNullOrWhiteSpace(x.ProcessSessionId)))
            return Stop("process-session identity is missing; proof-local evidence cleanup is required");
        if (activeReload.LoadBoundaryObserved is false || noReplayReload.LoadBoundaryObserved is false)
            return Stop("full reload evidence was not emitted at an actual process load boundary");

        var launchAProcess = firstCreate.ProcessSessionId!;
        var launchBProcess = activeReload.ProcessSessionId!;
        var launchCProcess = noReplayReload.ProcessSessionId!;
        if (hostObservations.Take(5).Any(x => !string.Equals(x.ProcessSessionId, launchAProcess, StringComparison.Ordinal)) ||
            hostObservations.Skip(5).Take(3).Any(x => !string.Equals(x.ProcessSessionId, launchBProcess, StringComparison.Ordinal)) ||
            string.Equals(launchAProcess, launchBProcess, StringComparison.Ordinal) ||
            string.Equals(launchAProcess, launchCProcess, StringComparison.Ordinal) ||
            string.Equals(launchBProcess, launchCProcess, StringComparison.Ordinal))
            return Stop("three distinct process sessions did not own Launches A, B, and C");

        if (string.IsNullOrWhiteSpace(launchASave.NativeQuestState) ||
            string.IsNullOrWhiteSpace(activeReload.NativeQuestState))
            return Inconclusive("native Quest state is unavailable at Launch A Save or Launch B active reconstruction");
        if (!string.Equals(launchASave.NativeQuestState, RequiredActiveState, StringComparison.Ordinal) ||
            !string.Equals(activeReload.NativeQuestState, RequiredActiveState, StringComparison.Ordinal))
            return Stop("native Quest state was not Active at Launch A Save and Launch B reconstruction");

        if (string.IsNullOrWhiteSpace(launchASave.NativeObjectiveState) ||
            string.IsNullOrWhiteSpace(activeReload.NativeObjectiveState))
            return Inconclusive("native objective state is unavailable at Launch A Save or Launch B active reconstruction");
        if (!string.Equals(launchASave.NativeObjectiveState, RequiredActiveState, StringComparison.Ordinal) ||
            !string.Equals(activeReload.NativeObjectiveState, RequiredActiveState, StringComparison.Ordinal))
            return Stop("native objective was not Active at Launch A Save and Launch B reconstruction");

        if (activeReload.IsReconstruction is false ||
            string.IsNullOrWhiteSpace(activeReload.NativeReference) ||
            activeReload.NativeIdentity == QuestNativeIdentity.Empty ||
            string.IsNullOrWhiteSpace(activeReload.NativeTitle) ||
            string.IsNullOrWhiteSpace(activeReload.NativeObjectiveState) ||
            activeReload.ManagerMembership is not true)
            return Inconclusive("active Quest reconstruction did not restore sufficient native presentation state");

        var nativePresentationObservations = hostObservations.Where(x => x != noReplayReload);
        if (nativePresentationObservations.Any(x =>
            x.NativeIdentity == QuestNativeIdentity.Empty ||
            string.IsNullOrWhiteSpace(x.NativeReference)))
            return Inconclusive("native identity is empty or unavailable for a presentation observation");

        if (!string.IsNullOrWhiteSpace(firstCreate.NativeTitle) &&
            !string.Equals(firstCreate.NativeTitle, activeReload.NativeTitle, StringComparison.Ordinal))
            return Stop("reconstructed Quest presentation did not correlate to Launch A");

        if (noReplayReload.IsReconstruction ||
            noReplayReload.NativeReference is not null ||
            noReplayReload.NativeIdentity != QuestNativeIdentity.Empty ||
            noReplayReload.ManagerMembership is not false)
            return Stop("completed Quest reappeared after terminal save; replay was observed");

        if (firstCreate.NativeIdentity == QuestNativeIdentity.Stable &&
            !string.Equals(firstCreate.NativeReference, activeReload.NativeReference, StringComparison.Ordinal))
            return Stop("a claimed stable native identity drifted across reconstruction");

        var terminalObservation = hostObservations.Single(x => x.Phase == QuestLifecyclePhase.Terminal);
        if (terminalObservation.TerminalOutcome == QuestTerminalOutcome.None)
            return Inconclusive("terminal outcome was not observed exactly once");

        var terminalOutcomes = hostObservations
            .Where(x => x.Phase == QuestLifecyclePhase.Terminal)
            .Select(x => x.TerminalOutcome)
            .Where(x => x != QuestTerminalOutcome.None)
            .Distinct()
            .ToArray();
        if (terminalOutcomes.Length != 1)
            return Inconclusive("terminal outcome was not observed exactly once");

        var standingReceipts = hostObservations.Where(x => x.StandingReceipt is not null).ToArray();
        var rewardReceipts = hostObservations.Where(x => x.RewardReceipt is not null).ToArray();
        if (standingReceipts.Any(x => x.Phase != QuestLifecyclePhase.Terminal) ||
            rewardReceipts.Any(x => x.Phase != QuestLifecyclePhase.Terminal) ||
            standingReceipts.Length > 1 ||
            rewardReceipts.Length > 1)
            return Stop("exactly-one proof-local Standing and reward receipt must occur only on Terminal");
        if (standingReceipts.Length != 1 || rewardReceipts.Length != 1 ||
            terminalObservation.StandingReceipt is null || terminalObservation.RewardReceipt is null)
            return Inconclusive("exactly-one Standing and reward-authorization receipts were not observed");

        if (evaluationContext is null || evaluationContext.CurrentProcessLoadBoundaryObserved is false)
            return Inconclusive("the current process did not participate in a load boundary");
        if (evaluationContext.CurrentProcessLoadBoundaryValid is false)
            return Stop("duplicate or invalid OnLoadComplete participation was observed in the current process");
        if (!string.Equals(evaluationContext.RunChainId, runChainIds[0], StringComparison.Ordinal))
            return Stop("the current process run-chain identity does not match persisted evidence");
        if (!string.Equals(evaluationContext.ProcessSessionId, launchCProcess, StringComparison.Ordinal))
            return Stop("completed evidence was not produced by the current process load boundary");

        var reason = "PASS: Launch A active save reconstructed once in Launch B; host-owned terminal and exactly-once receipts were followed by Launch C no replay";
        if (guidExperiment is not null)
            reason += "; optional SetGUID observation was recorded without making native GUID stability a PASS gate";
        return new QuestLifecycleProofResult(QuestLifecycleProofDecision.Pass, [reason]);
    }

    private static QuestLifecycleProofResult Inconclusive(string reason) =>
        new(QuestLifecycleProofDecision.Inconclusive, ["INCONCLUSIVE: " + reason]);

    private static QuestLifecycleProofResult Stop(string reason) =>
        new(QuestLifecycleProofDecision.Stop, ["STOP: " + reason]);
}
