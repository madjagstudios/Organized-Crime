using System.Collections.ObjectModel;

namespace OrganizedCrime.PropertyProbe.Model;

public enum NightclubProbeAdmissionDecision
{
    Ignore,
    Start,
    Stop
}

public sealed record NightclubProbeAdmission(
    NightclubProbeAdmissionDecision Decision,
    string Reason);

public enum NightclubProbeAuthority
{
    Unknown,
    Host,
    Client
}

public enum NightclubMenuOutcome
{
    Unknown,
    Opened,
    NotObserved,
    OpenedAndClosed
}

public enum NightclubProbeDecision
{
    Pass,
    Inconclusive,
    Stop
}

public enum NightclubProbeEventKind
{
    OwnerTriggered,
    DoorInteractionObserved,
    DuplicateInteractionObserved,
    MenuOpenedObserved,
    MenuClosedObserved,
    WindowElapsed,
    TeardownObserved
}

public sealed record NightclubProbeObservation(
    int Sequence,
    NightclubProbeEventKind EventKind,
    double TimestampSeconds,
    string SubjectPath,
    string Detail)
{
    public static NightclubProbeObservation Test(
        NightclubProbeEventKind eventKind,
        double timestampSeconds,
        string subjectPath = "<test>",
        string detail = "test") =>
        new(0, eventKind, timestampSeconds, subjectPath, detail);
}

public sealed record NightclubDoorFingerprint(
    string HierarchyPath,
    string RuntimeType,
    int InstanceId,
    bool ActiveInHierarchy,
    Vector3Dto Position,
    Vector3Dto Forward,
    string InteractablePath,
    string InteractableType,
    string BuildingPath,
    string BuildingType,
    string BuildingName,
    string BuildingGuid,
    string AccessPointPath,
    Vector3Dto? AccessPointPosition,
    IReadOnlyList<string> ComponentTypes,
    IReadOnlyDictionary<string, string> FieldAvailability,
    string StableIdentity)
{
    public static NightclubDoorFingerprint Test(string path = "Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Front Door") =>
        new(
            HierarchyPath: path,
            RuntimeType: "Il2CppScheduleOne.Doors.StaticDoor",
            InstanceId: 1,
            ActiveInHierarchy: true,
            Position: new Vector3Dto(-43.16f, -2.93f, 157.03f),
            Forward: new Vector3Dto(0f, 0f, 1f),
            InteractablePath: path,
            InteractableType: "Il2CppScheduleOne.Interaction.InteractableObject",
            BuildingPath: "Map/Hyland Point/Region_Northtown/Nightclub/desert town hall",
            BuildingType: "Il2CppScheduleOne.Map.NPCEnterableBuilding",
            BuildingName: "Nightclub",
            BuildingGuid: "<unobserved>",
            AccessPointPath: path + "/AccessPoint",
            AccessPointPosition: new Vector3Dto(-43.16f, -2.93f, 157.03f),
            ComponentTypes: new[] { "Il2CppScheduleOne.Doors.StaticDoor" },
            FieldAvailability: new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IntObj"] = "present",
                ["Building"] = "present",
                ["AccessPoint"] = "present",
                ["CanKnock"] = "present",
                ["Usable"] = "present",
                ["doorIndex"] = "present"
            }),
            StableIdentity: path + "|instance:1");
}

public sealed record NightclubVanillaFingerprint(
    string DoorState,
    string InteractableState,
    string BuildingState,
    string AccessPointState,
    string MenuState,
    string NpcState)
{
    public static NightclubVanillaFingerprint Test() =>
        new(
            DoorState: "Usable=True;CanKnock=True;doorIndex=0",
            InteractableState: "Default;message=Knock",
            BuildingState: "BuildingName=Nightclub;occupants=0",
            AccessPointState: "present;position=(-43.16,-2.93,157.03)",
            MenuState: "closed",
            NpcState: "occupants=[]");
}

public sealed record NightclubProbeEvidence(
    string RunId,
    string SceneName,
    NightclubProbeAuthority Authority,
    IReadOnlyList<NightclubDoorFingerprint> DoorCandidates,
    NightclubDoorFingerprint? SelectedDoor,
    NightclubVanillaFingerprint VanillaBefore,
    NightclubVanillaFingerprint VanillaAfter,
    IReadOnlyList<NightclubProbeObservation> Observations,
    NightclubMenuOutcome MenuOutcome,
    bool MenuSurfaceObserved,
    bool DuplicateInteractionTested,
    bool TeardownObserved,
    bool MutationAttempted,
    bool HarmonyUsed,
    bool SceneChanged,
    bool ExceptionObserved,
    bool ExactShellValidated,
    bool VanillaInvariantsUnchanged,
    double DurationSeconds,
    string? FailureReason = null)
{
    public static NightclubProbeEvidence Test(
        NightclubProbeAuthority authority = NightclubProbeAuthority.Host,
        NightclubMenuOutcome menuOutcome = NightclubMenuOutcome.NotObserved,
        bool duplicateInteractionTested = true,
        bool teardownObserved = true,
        bool mutationAttempted = false,
        bool exactShellValidated = true,
        bool vanillaInvariantsUnchanged = true,
        IReadOnlyList<NightclubProbeObservation>? observations = null) =>
        new(
            RunId: "test-run",
            SceneName: "Main",
            Authority: authority,
            DoorCandidates: new[] { NightclubDoorFingerprint.Test() },
            SelectedDoor: NightclubDoorFingerprint.Test(),
            VanillaBefore: NightclubVanillaFingerprint.Test(),
            VanillaAfter: NightclubVanillaFingerprint.Test(),
            Observations: observations ?? new[]
            {
                NightclubProbeObservation.Test(NightclubProbeEventKind.OwnerTriggered, 0),
                NightclubProbeObservation.Test(NightclubProbeEventKind.DoorInteractionObserved, 1),
                NightclubProbeObservation.Test(NightclubProbeEventKind.DuplicateInteractionObserved, 2),
                NightclubProbeObservation.Test(NightclubProbeEventKind.WindowElapsed, 10),
                NightclubProbeObservation.Test(NightclubProbeEventKind.TeardownObserved, 10.1)
            },
            MenuOutcome: menuOutcome,
            MenuSurfaceObserved: true,
            DuplicateInteractionTested: duplicateInteractionTested,
            TeardownObserved: teardownObserved,
            MutationAttempted: mutationAttempted,
            HarmonyUsed: false,
            SceneChanged: false,
            ExceptionObserved: false,
            ExactShellValidated: exactShellValidated,
            VanillaInvariantsUnchanged: vanillaInvariantsUnchanged,
            DurationSeconds: 10.1);
}

public sealed record NightclubProbeAssessment(
    NightclubProbeDecision Decision,
    IReadOnlyList<string> Reasons);

public static class NightclubProbeContract
{
    public const string ExpectedShellPath = "Map/Hyland Point/Region_Northtown/Nightclub/desert town hall";
    public const string TriggerLabel = "F5";
    public const double MaximumRunSeconds = 12.0;

    public static bool IsExpectedShellPath(string? path) =>
        string.Equals(path, ExpectedShellPath, StringComparison.Ordinal);

    public static bool TryAdmitTrigger(
        bool f5Pressed,
        int hostCount,
        bool serverStarted,
        bool clientStarted,
        bool alreadyRan,
        out NightclubProbeAdmission admission)
    {
        if (!f5Pressed)
        {
            admission = new(NightclubProbeAdmissionDecision.Ignore, "F5 was not pressed.");
            return false;
        }

        if (alreadyRan)
        {
            admission = new(NightclubProbeAdmissionDecision.Stop, "The one-run guard already consumed this process.");
            return false;
        }

        if (!serverStarted || !clientStarted || hostCount != 1)
        {
            admission = new(
                NightclubProbeAdmissionDecision.Stop,
                "Exactly one authoritative single-player host is required.");
            return false;
        }

        admission = new(NightclubProbeAdmissionDecision.Start, "One authoritative host run admitted.");
        return true;
    }

    public static NightclubActiveWindowAssessment EvaluateActiveAuthority(
        int hostCount,
        bool serverStarted,
        bool clientStarted) =>
        serverStarted && clientStarted && hostCount == 1
            ? new(NightclubActiveWindowDecision.Continue, "The authoritative single-player host remains stable.")
            : new(NightclubActiveWindowDecision.Stop, "STOP: authority drifted from the required single-player host.");
}

public static class NightclubProbeEvaluator
{
    public static NightclubProbeAssessment Evaluate(NightclubProbeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var stopReasons = new List<string>();
        if (evidence.Authority != NightclubProbeAuthority.Host)
            stopReasons.Add("STOP: authority was not the single-player host.");
        if (evidence.MutationAttempted)
            stopReasons.Add("STOP: a mutation was attempted.");
        if (evidence.HarmonyUsed)
            stopReasons.Add("STOP: Harmony was used without a separately approved design.");
        if (evidence.SceneChanged)
            stopReasons.Add("STOP: the active scene changed during the bounded run.");
        if (evidence.ExceptionObserved)
            stopReasons.Add("STOP: an exception occurred during observation.");
        if (!evidence.ExactShellValidated)
            stopReasons.Add("STOP: exact Nightclub shell identity could not be validated fail-closed.");
        if (!evidence.VanillaInvariantsUnchanged)
            stopReasons.Add("STOP: vanilla door, building, access-point, or NPC identity invariants changed.");

        if (evidence.DoorCandidates.Any(candidate =>
                !NightclubProbeContract.IsExpectedShellPath(GetShellPath(candidate.HierarchyPath))))
            stopReasons.Add("STOP: a selected door candidate was outside the approved shell.");

        if (evidence.DoorCandidates
                .GroupBy(candidate => candidate.StableIdentity, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            stopReasons.Add("STOP: duplicate door identities were observed.");

        if (stopReasons.Count > 0)
            return new(NightclubProbeDecision.Stop, stopReasons);

        var inconclusiveReasons = new List<string>();
        if (evidence.DoorCandidates.Count == 0 || evidence.SelectedDoor is null)
            inconclusiveReasons.Add("INCONCLUSIVE: the exact Nightclub StaticDoor was not selected.");
        else if (!evidence.DoorCandidates.Any(candidate =>
                     string.Equals(candidate.StableIdentity, evidence.SelectedDoor.StableIdentity, StringComparison.Ordinal)))
            inconclusiveReasons.Add("INCONCLUSIVE: the selected door was not in the captured shell candidates.");

        if (!evidence.Observations.Any(observation => observation.EventKind == NightclubProbeEventKind.OwnerTriggered))
            inconclusiveReasons.Add("INCONCLUSIVE: owner-trigger observation is missing.");
        if (!evidence.Observations.Any(observation => observation.EventKind == NightclubProbeEventKind.DoorInteractionObserved))
            inconclusiveReasons.Add("INCONCLUSIVE: no manual door interaction was observed.");
        if (evidence.MenuOutcome == NightclubMenuOutcome.Unknown)
            inconclusiveReasons.Add("INCONCLUSIVE: the complete NPCSummonMenu observation window is missing.");
        if (!evidence.MenuSurfaceObserved)
            inconclusiveReasons.Add("INCONCLUSIVE: no NPCSummonMenu surface was available to observe.");
        if (evidence.MenuOutcome == NightclubMenuOutcome.Opened)
            inconclusiveReasons.Add("INCONCLUSIVE: NPCSummonMenu remained open at the end of the window.");
        if (!evidence.DuplicateInteractionTested)
            inconclusiveReasons.Add("INCONCLUSIVE: duplicate manual interaction behavior was not tested.");
        if (!evidence.Observations.Any(observation => observation.EventKind == NightclubProbeEventKind.WindowElapsed))
            inconclusiveReasons.Add("INCONCLUSIVE: the bounded timing window did not elapse.");
        if (!evidence.TeardownObserved)
            inconclusiveReasons.Add("INCONCLUSIVE: diagnostic teardown was not observed.");
        if (evidence.DurationSeconds is < 0 or > NightclubProbeContract.MaximumRunSeconds + 0.5)
            inconclusiveReasons.Add("INCONCLUSIVE: bounded-run duration is unavailable or outside the contract.");

        if (inconclusiveReasons.Count > 0)
            return new(NightclubProbeDecision.Inconclusive, inconclusiveReasons);

        return new(
            NightclubProbeDecision.Pass,
            new[]
            {
                "PASS: exact shell/door identity, manual interaction timing, menu outcome, duplicate behavior, and diagnostic teardown were observed on one host run.",
                "INTERPRETATION: callback order remains observationally bounded to the recorded polling edges; no callback ownership is claimed."
            });
    }

    private static string GetShellPath(string doorPath)
    {
        return doorPath.StartsWith(
            NightclubProbeContract.ExpectedShellPath + "/",
            StringComparison.Ordinal)
            ? NightclubProbeContract.ExpectedShellPath
            : doorPath;
    }
}

public enum NightclubEventReceiptDisposition
{
    Accepted,
    DebouncedDuplicate,
    IgnoredUntrackedDoor
}

public sealed record NightclubDoorEventReceipt(
    NightclubEventReceiptDisposition Disposition,
    string DoorStableIdentity,
    string DoorPath,
    float TimestampSeconds,
    int Ordinal,
    int DuplicateCount);

public sealed record NightclubEventReceiptResult(
    NightclubEventReceiptDisposition Disposition,
    NightclubDoorEventReceipt? Receipt);

/// <summary>
/// Pure, per-exact-door receipt ledger for the bounded native event observer.
/// It never invokes Unity or game APIs; the runtime adapter supplies only already-validated identities and monotonic timestamps.
/// </summary>
public sealed class NightclubEventObserverLedger
{
    public const float DebounceSeconds = 0.25f;

    private readonly IReadOnlyDictionary<string, NightclubDoorFingerprint> _candidates;
    private readonly Dictionary<string, LastAcceptedEvent> _lastAcceptedByDoor = new(StringComparer.Ordinal);
    private readonly List<NightclubDoorEventReceipt> _receipts = new();

    public NightclubEventObserverLedger(IEnumerable<NightclubDoorFingerprint> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _candidates = candidates.ToDictionary(candidate => candidate.StableIdentity, StringComparer.Ordinal);
    }

    public int AcceptedInteractionCount { get; private set; }

    public IReadOnlyList<NightclubDoorEventReceipt> Receipts => _receipts;

    public NightclubEventReceiptResult Record(string doorStableIdentity, float timestampSeconds)
    {
        if (!_candidates.TryGetValue(doorStableIdentity, out var candidate))
            return new(NightclubEventReceiptDisposition.IgnoredUntrackedDoor, null);

        if (_lastAcceptedByDoor.TryGetValue(doorStableIdentity, out var previous) &&
            timestampSeconds - previous.TimestampSeconds <= DebounceSeconds)
        {
            var duplicate = new NightclubDoorEventReceipt(
                NightclubEventReceiptDisposition.DebouncedDuplicate,
                candidate.StableIdentity,
                candidate.HierarchyPath,
                timestampSeconds,
                previous.Ordinal,
                previous.DuplicateCount + 1);
            _lastAcceptedByDoor[doorStableIdentity] = previous with { DuplicateCount = duplicate.DuplicateCount };
            _receipts.Add(duplicate);
            return new(duplicate.Disposition, duplicate);
        }

        var accepted = new NightclubDoorEventReceipt(
            NightclubEventReceiptDisposition.Accepted,
            candidate.StableIdentity,
            candidate.HierarchyPath,
            timestampSeconds,
            ++AcceptedInteractionCount,
            DuplicateCount: 0);
        _lastAcceptedByDoor[doorStableIdentity] = new LastAcceptedEvent(
            accepted.TimestampSeconds,
            accepted.Ordinal,
            accepted.DuplicateCount);
        _receipts.Add(accepted);
        return new(accepted.Disposition, accepted);
    }

    private sealed record LastAcceptedEvent(float TimestampSeconds, int Ordinal, int DuplicateCount);
}

public enum NightclubActiveWindowDecision
{
    Continue,
    Stop
}

public sealed record NightclubActiveWindowAssessment(
    NightclubActiveWindowDecision Decision,
    string Reason);

public enum NightclubListenerTermination
{
    NormalCompletion,
    Stop,
    Inconclusive,
    SceneChanged,
    AuthorityDrift,
    Exception,
    Disposal,
    ApplicationQuit,
    PartialSubscriptionFailure
}

public enum NightclubListenerRemovalDisposition
{
    Removed,
    TargetUnavailable,
    Failed
}

public sealed record NightclubListenerRemovalAttempt(
    string DoorStableIdentity,
    NightclubListenerRemovalDisposition Disposition,
    string? Detail);

public interface INightclubListenerRegistration
{
    string DoorStableIdentity { get; }

    NightclubListenerRemovalAttempt TryRemove();
}

public sealed record NightclubListenerCleanupResult(
    NightclubListenerTermination Termination,
    IReadOnlyList<string> RemovedDoorStableIdentities,
    IReadOnlyList<string> PendingDoorStableIdentities,
    IReadOnlyList<NightclubListenerRemovalAttempt> FailedRemovals)
{
    public bool TeardownSucceeded => PendingDoorStableIdentities.Count == 0;
}

/// <summary>
/// Pure lifecycle record for listeners the runtime adapter has actually registered.
/// Successful removals are consumed once; failed removals remain pending for a later terminal retry.
/// </summary>
public sealed class NightclubListenerRegistry
{
    private readonly HashSet<string> _candidateIdentities;
    private readonly Dictionary<string, INightclubListenerRegistration> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = new();

    public NightclubListenerRegistry(IEnumerable<NightclubDoorFingerprint> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _candidateIdentities = candidates
            .Select(candidate => candidate.StableIdentity)
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool MarkRegistered(INightclubListenerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!_candidateIdentities.Contains(registration.DoorStableIdentity) ||
            !_pending.TryAdd(registration.DoorStableIdentity, registration))
            return false;

        _registrationOrder.Add(registration.DoorStableIdentity);
        return true;
    }

    public NightclubListenerCleanupResult RemovePending(NightclubListenerTermination termination)
    {
        var removed = new List<string>();
        var failed = new List<NightclubListenerRemovalAttempt>();
        foreach (var stableIdentity in _registrationOrder)
        {
            if (!_pending.TryGetValue(stableIdentity, out var registration))
                continue;

            NightclubListenerRemovalAttempt attempt;
            try
            {
                attempt = registration.TryRemove();
            }
            catch (Exception ex)
            {
                attempt = new(
                    stableIdentity,
                    NightclubListenerRemovalDisposition.Failed,
                    ex.GetType().Name + ": " + ex.Message);
            }

            if (attempt.Disposition == NightclubListenerRemovalDisposition.Removed)
            {
                _pending.Remove(stableIdentity);
                removed.Add(stableIdentity);
            }
            else
            {
                failed.Add(attempt);
            }
        }

        return new(
            termination,
            removed,
            _registrationOrder.Where(_pending.ContainsKey).ToArray(),
            failed);
    }
}
