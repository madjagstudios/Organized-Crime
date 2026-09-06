using System.Text.Json;
using Il2CppFishNet;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using S1API.Lifecycle;
using UnityEngine;

namespace OrganizedCrime.PoliceDispatchProof;

public sealed class PoliceDispatchProofRuntime : IDisposable
{
    private const int MaxLogRows = 256;
    private const int MaxLogBytes = 64 * 1024;
    private const double MinimumOwnerIntervalSeconds = 10;
    private static readonly TimeSpan PerResponseRecoveryDeadline = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TotalRunDeadline = TimeSpan.FromMinutes(15);

    private readonly Action<string> _log;
    private readonly DispatchProofStateMachine _stateMachine = new();
    private readonly DispatchProofAdmission _admission;
    private readonly DispatchProofSubscriptionLedger _subscriptions = new();
    private readonly BoundedProofLog _proofLog = new(MaxLogRows, MaxLogBytes);
    private readonly PoliceDispatchProofObserver _observer = new();
    private readonly List<DispatchResponseEvidence> _responses = new();
    private readonly List<StationSnapshot> _baselineStations = new();
    private DispatchProofObservation? _baseline;
    private DispatchProofObservation? _responseBefore;
    private DispatchResponseEvidence? _pendingResponse;
    private string? _dispatchedStationIdentity;
    private DateTime _lastInvocationUtc;
    private DateTime _runStartedUtc;
    private bool _loadCompleteSeen;
    private bool _disposed;
    private bool _morePatrolsPresent;
    private bool _unsupportedPlayerObserved = false;
    private bool _duplicateInvocationObserved = false;
    private bool _poolCorruptionObserved = false;
    private bool _sessionCorruptionObserved;

    public PoliceDispatchProofRuntime(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _admission = new DispatchProofAdmission(_stateMachine);
    }

    public DispatchProofPhase Phase => _stateMachine.Phase;

    public void Initialize()
    {
        if (_disposed)
            return;

        _stateMachine.TryTransition(DispatchProofPhase.Detached, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "initialize");
        _runStartedUtc = DateTime.UtcNow;
        GameLifecycle.OnPreLoad += HandlePreLoad;
        _subscriptions.Track(() => GameLifecycle.OnPreLoad -= HandlePreLoad);
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        _subscriptions.Track(() => GameLifecycle.OnLoadComplete -= HandleLoadComplete);
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;
        _subscriptions.Track(() => GameLifecycle.OnPreSceneChange -= HandlePreSceneChange);
        GameLifecycle.OnSaveStart += HandleSaveStart;
        _subscriptions.Track(() => GameLifecycle.OnSaveStart -= HandleSaveStart);
        MelonLogger.Msg("[OC-40] initialized; no Dispatch call is automatic; owner keys F9/F10 are required.");
    }

    public void Update()
    {
        if (_disposed)
            return;

        try
        {
            if (DateTime.UtcNow - _runStartedUtc > TotalRunDeadline)
            {
                Stop("absolute fifteen-minute proof run cap elapsed");
                return;
            }

            if (_stateMachine.Phase == DispatchProofPhase.AwaitingLoadAuthorityIdentity && _loadCompleteSeen)
                TryCaptureBaseline();

            if (_stateMachine.Phase == DispatchProofPhase.ObservingRecovery1 ||
                _stateMachine.Phase == DispatchProofPhase.ObservingRecovery2)
                ObserveRecovery();

            if (Input.GetKeyDown(KeyCode.F9))
                TryOwnerTrigger(DispatchProofTrigger.Response1, "owner-f9");
            if (Input.GetKeyDown(KeyCode.F10))
                TryOwnerTrigger(DispatchProofTrigger.Response2, "owner-f10");
        }
        catch (Exception exception)
        {
            Stop($"unexpected proof runtime exception: {exception.GetType().Name}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscriptions.Dispose();
        if (_stateMachine.Phase is not DispatchProofPhase.Complete and not DispatchProofPhase.Stop)
            _stateMachine.TryTransition(_stateMachine.Phase, DispatchProofPhase.Stop, "dispose");
        _log("disposed; all lifecycle handlers detached");
    }

    private void HandlePreLoad()
    {
        if (DispatchProofLifecycle.IsInitialLoadBoundary(_stateMachine.Phase, _loadCompleteSeen))
            return;

        Stop("save/load boundary observed; proof stopped before mutation");
    }

    private void HandlePreSceneChange() => Stop("scene boundary observed; proof stopped before mutation");

    private void HandleSaveStart() => Stop("save boundary observed; proof stopped before mutation");

    private void HandleLoadComplete() => _loadCompleteSeen = true;

    private void TryCaptureBaseline()
    {
        var observation = _observer.Capture();
        if (!IsProofReady(observation, out var reason))
        {
            Log("readiness-pending", reason);
            return;
        }

        _baseline = observation;
        _baselineStations.Clear();
        _baselineStations.AddRange(observation.Stations);
        _stateMachine.TryTransition(
            DispatchProofPhase.AwaitingLoadAuthorityIdentity,
            DispatchProofPhase.BaselineReady,
            $"baseline:{observation.Sequence}");
        _stateMachine.TryTransition(
            DispatchProofPhase.BaselineReady,
            DispatchProofPhase.Response1Armed,
            $"arm-1:{observation.Sequence}");
        Log("baseline-ready", $"sequence={observation.Sequence}; target={observation.CanonicalTargetIdentity}");
    }

    private void TryOwnerTrigger(DispatchProofTrigger trigger, string triggerKey)
    {
        if (_stateMachine.Phase != (trigger == DispatchProofTrigger.Response1
                ? DispatchProofPhase.Response1Armed
                : DispatchProofPhase.Response2Eligible))
        {
            Log("trigger-rejected", $"key={triggerKey}; phase={_stateMachine.Phase}");
            return;
        }

        var before = _observer.Capture();
        if (!TryGetCanonicalTarget(before, out var target, out var targetIdentity, out var targetReason))
        {
            Stop(targetReason);
            return;
        }

        var context = new DispatchProofContext(
            IsAuthoritativeHost: before.IsAuthoritativeHost,
            IsSinglePlayer: before.IsSinglePlayer,
            CanonicalTargetIdentity: before.CanonicalTargetIdentity,
            ActualTargetIdentity: targetIdentity,
            BuildPinProvenance: "manual S1Atlas build-pin provenance verified before artifact build",
            DispatchSignatureProvenance: "compile-time direct interop boundary verified against S1Atlas",
            MorePatrolsPresent: IsMorePatrolsPresent());
        _morePatrolsPresent |= context.MorePatrolsPresent;

        var admission = _admission.TryAdmit(trigger, triggerKey, context);
        if (!admission.Accepted)
        {
            Log("trigger-rejected", $"key={triggerKey}; reason={admission.Reason}");
            return;
        }

        _responseBefore = before;
        var dispatchCompleted = false;
        var duplicateObserved = false;
        try
        {
            var station = SelectStation(before);
            if (station is null)
            {
                Stop("no native PoliceStation was available for the owner-triggered proof");
                return;
            }

            _dispatchedStationIdentity = station.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
            station.Dispatch(
                DispatchProofPins.RequestedOfficerCount,
                target,
                PoliceStation.EDispatchType.UseVehicle,
                DispatchProofPins.BeginAsSighted);
            dispatchCompleted = true;
        }
        catch (Exception exception)
        {
            Stop($"native PoliceStation.Dispatch threw: {exception.GetType().Name}");
        }

        if (!dispatchCompleted || _stateMachine.Phase == DispatchProofPhase.Stop)
            return;

        var after = _observer.Capture();
        _unsupportedPlayerObserved |= after.Players.Any(player =>
            player.IsSupportedServerInitialized.IsAvailable && !player.IsSupportedServerInitialized.Value);
        _poolCorruptionObserved |= HasDuplicateOfficerIdentity(after.Officers);
        var responseNumber = trigger == DispatchProofTrigger.Response1 ? 1 : 2;
        _pendingResponse = CreateResponseEvidence(responseNumber, targetIdentity, dispatchCompleted, duplicateObserved, _dispatchedStationIdentity, before, after);
        _lastInvocationUtc = DateTime.UtcNow;
        var invokedPhase = trigger == DispatchProofTrigger.Response1
            ? DispatchProofPhase.Response1Invoked
            : DispatchProofPhase.Response2Invoked;
        var observingPhase = trigger == DispatchProofTrigger.Response1
            ? DispatchProofPhase.ObservingRecovery1
            : DispatchProofPhase.ObservingRecovery2;
        _stateMachine.TryTransition(invokedPhase, observingPhase, $"observe:{triggerKey}");
        Log("dispatch-invoked", $"response={responseNumber}; target={targetIdentity}; sequence={after.Sequence}");
    }

    private void ObserveRecovery()
    {
        var pending = _pendingResponse;
        var before = _responseBefore;
        if (pending is null || before is null)
        {
            Stop("recovery observation has no response baseline");
            return;
        }

        var after = _observer.Capture();
        var recoveryEvidence = pending with
        {
            RecoveryStations = after.Stations,
            RecoveryResponders = after.Officers,
            RecoveryPlayers = after.Players
        };
        var evaluation = DispatchRecoveryEvaluator.Evaluate(recoveryEvidence);
        var deadlineReached = DateTime.UtcNow - _lastInvocationUtc > PerResponseRecoveryDeadline;
        var adequate = DateTime.UtcNow - _lastInvocationUtc >= TimeSpan.FromSeconds(MinimumOwnerIntervalSeconds) &&
            evaluation.RequiredObservationsAvailable && evaluation.RecoveryAdequate;
        if (!adequate && !deadlineReached)
            return;

        if (!adequate && !evaluation.RequiredObservationsAvailable && deadlineReached)
        {
            CompleteResponse(
                recoveryEvidence with
                {
                    RecoveryObservationAvailable = false,
                    RecoveryAdequate = false,
                    DeathObservationAvailable = after.Officers.All(officer => officer.Alive.IsAvailable),
                    NaturalDeathObserved = after.Officers.Any(officer => officer.Alive.IsAvailable && !officer.Alive.Value)
                },
                "recovery-timeout-unavailable");
            return;
        }

        if (!adequate && deadlineReached)
        {
            CompleteResponse(
                recoveryEvidence with
                {
                    RecoveryObservationAvailable = true,
                    RecoveryAdequate = false,
                    DeathObservationAvailable = after.Officers.All(officer => officer.Alive.IsAvailable),
                    NaturalDeathObserved = after.Officers.Any(officer => officer.Alive.IsAvailable && !officer.Alive.Value)
                },
                "recovery-timeout-capacity-shortfall");
            return;
        }

        var completed = recoveryEvidence with
        {
            RecoveryObservationAvailable = true,
            RecoveryAdequate = true,
            DeathObservationAvailable = after.Officers.All(officer => officer.Alive.IsAvailable),
            NaturalDeathObserved = after.Officers.Any(officer => officer.Alive.IsAvailable && !officer.Alive.Value)
        };
        CompleteResponse(completed, "response-recovered");
    }

    private void CompleteResponse(DispatchResponseEvidence completed, string eventKey)
    {
        _responses.Add(completed);
        _pendingResponse = null;
        _responseBefore = null;
        _dispatchedStationIdentity = null;

        if (_stateMachine.Phase == DispatchProofPhase.ObservingRecovery1)
        {
            if (completed.RecoveryAdequate)
            {
                _stateMachine.TryTransition(DispatchProofPhase.ObservingRecovery1, DispatchProofPhase.Response2Eligible, $"eligible-2:{completed.ResponseNumber}");
                Log(eventKey, "response=1; response 2 is now owner-eligible after native recovery");
            }
            else
            {
                _stateMachine.TryTransition(DispatchProofPhase.ObservingRecovery1, DispatchProofPhase.TeardownPending, $"teardown:{completed.ResponseNumber}");
                Log(eventKey, "response=1; recovery decision recorded; response 2 is not issued");
                FinishClassification();
            }
        }
        else
        {
            _stateMachine.TryTransition(DispatchProofPhase.ObservingRecovery2, DispatchProofPhase.TeardownPending, $"teardown:{completed.ResponseNumber}");
            FinishClassification();
        }
    }

    private void FinishClassification()
    {
        var classification = DispatchProofClassifier.Classify(new(
            _baseline?.CanonicalTargetIdentity,
            _responses,
            _morePatrolsPresent,
            _unsupportedPlayerObserved,
            _duplicateInvocationObserved,
            _poolCorruptionObserved,
            _sessionCorruptionObserved));
        Log("classification", JsonSerializer.Serialize(classification));
        _stateMachine.TryTransition(DispatchProofPhase.TeardownPending, DispatchProofPhase.Complete, "complete");
    }

    private DispatchResponseEvidence CreateResponseEvidence(
        int responseNumber,
        string targetIdentity,
        bool dispatchCompleted,
        bool duplicateObserved,
        string? dispatchedStationIdentity,
        DispatchProofObservation before,
        DispatchProofObservation after)
    {
        var evidence = new DispatchResponseEvidence(
            ResponseNumber: responseNumber,
            ExpectedTargetIdentity: _baseline?.CanonicalTargetIdentity,
            ObservedTargetIdentity: targetIdentity,
            DispatchCallCompleted: dispatchCompleted,
            TargetAttributionAvailable: string.Equals(_baseline?.CanonicalTargetIdentity, targetIdentity, StringComparison.Ordinal),
            VehicleResponseObserved: HasVehicleResponse(before, after, dispatchedStationIdentity),
            RecoveryObservationAvailable: false,
            RecoveryAdequate: false,
            DeathObservationAvailable: after.Officers.All(officer => officer.Alive.IsAvailable),
            NaturalDeathObserved: after.Officers.Any(officer => officer.Alive.IsAvailable && !officer.Alive.Value),
            CapacityShortfallObserved: false,
            DuplicateInvocationObserved: duplicateObserved,
            UnrelatedPlayerEffectObserved: false,
            PersistentExceptionObserved: false,
            BeforeStations: before.Stations,
            AfterStations: after.Stations,
            BeforeResponders: before.Officers,
            AfterResponders: after.Officers,
            BeforePlayers: before.Players,
            AfterPlayers: after.Players)
        {
            DispatchedStationIdentity = dispatchedStationIdentity
        };

        return evidence with
        {
            CapacityShortfallObserved = evidence.ConsumedOfficerCount.IsAvailable &&
                evidence.ConsumedOfficerCount.Value < DispatchProofPins.RequestedOfficerCount
        };
    }

    private bool IsProofReady(DispatchProofObservation observation, out string reason)
    {
        _morePatrolsPresent = IsMorePatrolsPresent();
        if (_morePatrolsPresent)
        {
            reason = "MorePatrols assembly is present";
            Stop(reason);
            return false;
        }
        if (!observation.IsAuthoritativeHost)
        {
            reason = "not an authoritative host";
            return false;
        }
        if (!observation.IsSinglePlayer)
        {
            reason = "supported player registry is not exactly one host player";
            return false;
        }
        if (string.IsNullOrWhiteSpace(observation.CanonicalTargetIdentity))
        {
            reason = "canonical save-account identity is unavailable";
            return false;
        }
        if (!observation.HostGameMinutes.IsAvailable)
        {
            reason = "host clock is unavailable";
            return false;
        }

        reason = "ready";
        return true;
    }

    private static bool HasVehicleResponse(DispatchProofObservation before, DispatchProofObservation after, string? stationIdentity) =>
        !string.IsNullOrWhiteSpace(stationIdentity) && before.Stations.Join(
                after.Stations,
                station => station.StationIdentity,
                station => station.StationIdentity,
                (previous, current) => previous.AvailableVehicleCount.IsAvailable && current.AvailableVehicleCount.IsAvailable &&
                    string.Equals(previous.StationIdentity, stationIdentity, StringComparison.Ordinal) &&
                    current.AvailableVehicleCount.Value < previous.AvailableVehicleCount.Value)
            .Any(value => value);

    private static bool HasDuplicateOfficerIdentity(IReadOnlyList<OfficerSnapshot> officers) =>
        officers
            .Select(officer => officer.StableIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .GroupBy(identity => identity, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    private static PoliceStation? SelectStation(DispatchProofObservation observation)
    {
        try
        {
            var stations = PoliceStation.PoliceStations;
            if (stations is null)
                return null;

            var candidates = new List<PoliceStation>();
            foreach (var station in stations)
            {
                if (station is not null)
                    candidates.Add(station);
            }

            return candidates
                .OrderByDescending(station => SafeOfficerCount(station))
                .ThenByDescending(station => SafeVehicleCount(station))
                .FirstOrDefault();

        }
        catch
        {
            return null;
        }
    }

    private static int SafeOfficerCount(PoliceStation station)
    {
        try { return station.OfficerPool?.Count ?? -1; } catch { return -1; }
    }

    private static int SafeVehicleCount(PoliceStation station)
    {
        try { return station.AvailableVehicleCount; } catch { return -1; }
    }

    private static bool TryGetCanonicalTarget(
        DispatchProofObservation observation,
        out Player target,
        out string targetIdentity,
        out string reason)
    {
        target = null!;
        targetIdentity = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(observation.CanonicalTargetIdentity))
        {
            reason = "canonical target identity is unavailable at trigger time";
            return false;
        }

        try
        {
            var matches = new List<Player>();
            foreach (var player in Player.PlayerList ?? new Il2CppSystem.Collections.Generic.List<Player>())
            {
                if (player is not null && player.IsServerInitialized &&
                    string.Equals(player.PlayerCode?.Trim(), observation.CanonicalTargetIdentity, StringComparison.Ordinal))
                    matches.Add(player);
            }
            if (matches.Count != 1)
            {
                reason = $"canonical target identity matched {matches.Count} authoritative players";
                return false;
            }

            target = matches[0];
            targetIdentity = target.PlayerCode!.Trim();
            return true;
        }
        catch (Exception exception)
        {
            reason = $"canonical target lookup threw: {exception.GetType().Name}";
            return false;
        }
    }

    private static bool IsMorePatrolsPresent() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            string.Equals(assembly.GetName().Name, "MorePatrols", StringComparison.OrdinalIgnoreCase));

    private void Stop(string reason)
    {
        _sessionCorruptionObserved |= reason.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("scene", StringComparison.OrdinalIgnoreCase);
        if (_stateMachine.Phase is DispatchProofPhase.Complete or DispatchProofPhase.Stop)
            return;

        _stateMachine.TryTransition(_stateMachine.Phase, DispatchProofPhase.Stop, $"stop:{_stateMachine.Phase}:{reason}");
        Log("stop", reason);
    }

    private void Log(string key, string message)
    {
        if (_proofLog.TryAppend(new DispatchProofLogRow(key, message)))
            _log(JsonSerializer.Serialize(new { key, message }));
        else if (_proofLog.Truncated)
            _log("proof log cap reached; further rows suppressed");
    }
}
