using System.Text.Json;

namespace OrganizedCrime.PoliceDispatchProof;

public static class DispatchProofPins
{
    public const string BuildId = "1f1e5669033c6029fb0f64ad335c39dc5125c773a051c48e540627991040990c";
    public const string DispatchSignature = "ScheduleOne.Map.PoliceStation::Dispatch(System.Int32,ScheduleOne.PlayerScripts.Player,ScheduleOne.Map.PoliceStation+EDispatchType,System.Boolean):System.Void";
    public const int RequestedOfficerCount = 2;
    public const bool BeginAsSighted = false;
    public const string DispatchType = "UseVehicle";
}

public enum DispatchProofPhase
{
    Detached,
    AwaitingLoadAuthorityIdentity,
    BaselineReady,
    Response1Armed,
    Response1Invoked,
    ObservingRecovery1,
    Response2Eligible,
    Response2Invoked,
    ObservingRecovery2,
    TeardownPending,
    Complete,
    Inconclusive,
    Stop
}

public enum DispatchProofTrigger
{
    Response1,
    Response2
}

public enum DispatchProofDecision
{
    PassOptionA,
    NeedsOptionB,
    Inconclusive,
    Stop
}

public readonly record struct ProofObservation<T>(bool IsAvailable, T Value, string? UnavailabilityReason)
{
    public static ProofObservation<T> Available(T value) => new(true, value, null);

    public static ProofObservation<T> Unavailable(string reason) =>
        new(false, default!, string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason);
}

public sealed record DispatchProofContext(
    bool IsAuthoritativeHost,
    bool IsSinglePlayer,
    string? CanonicalTargetIdentity,
    string? ActualTargetIdentity,
    string BuildPinProvenance,
    string DispatchSignatureProvenance,
    bool MorePatrolsPresent);

public sealed record DispatchProofAdmissionResult(bool Accepted, string Reason, int AcceptedCallCount);

public sealed record DispatchProofLogRow(string Key, string Event);

public sealed record SequencedDispatchProofLogRow(long Sequence, string Key, string Event);

public sealed record StationCapacityDelta(
    string StationIdentity,
    ProofObservation<int> OfficerPoolCountDelta,
    ProofObservation<int> ConsumedOfficerCount,
    ProofObservation<int> AvailableVehicleCountDelta,
    ProofObservation<int> PoliceVehicleCountDelta,
    ProofObservation<int> DeployedVehicleCountDelta)
{
    public static StationCapacityDelta From(StationSnapshot before, StationSnapshot after)
    {
        var poolDelta = CalculateCountDelta(before.OfficerPoolIdentities, after.OfficerPoolIdentities);
        var consumed = CalculateConsumed(before.OfficerPoolIdentities, after.OfficerPoolIdentities);
        var vehicleDelta = CalculateCountDelta(before.PoliceVehicleIdentities, after.PoliceVehicleIdentities);
        var deployedDelta = CalculateCountDelta(before.DeployedVehicleIdentities, after.DeployedVehicleIdentities);

        return new(
            before.StationIdentity,
            poolDelta,
            consumed,
            CalculateCountDelta(before.AvailableVehicleCount, after.AvailableVehicleCount),
            vehicleDelta,
            deployedDelta);
    }

    private static ProofObservation<int> CalculateCountDelta<T>(
        ProofObservation<IReadOnlyList<T>> before,
        ProofObservation<IReadOnlyList<T>> after)
    {
        if (!before.IsAvailable || !after.IsAvailable)
            return ProofObservation<int>.Unavailable("station identity list unavailable");

        return ProofObservation<int>.Available(after.Value.Count - before.Value.Count);
    }

    private static ProofObservation<int> CalculateConsumed<T>(
        ProofObservation<IReadOnlyList<T>> before,
        ProofObservation<IReadOnlyList<T>> after)
    {
        if (!before.IsAvailable || !after.IsAvailable)
            return ProofObservation<int>.Unavailable("station officer identities unavailable");

        var remaining = new HashSet<T>(after.Value);
        return ProofObservation<int>.Available(before.Value.Count(identity => !remaining.Contains(identity)));
    }

    private static ProofObservation<int> CalculateCountDelta(
        ProofObservation<int> before,
        ProofObservation<int> after)
    {
        if (!before.IsAvailable || !after.IsAvailable)
            return ProofObservation<int>.Unavailable("numeric observation unavailable");

        return ProofObservation<int>.Available(after.Value - before.Value);
    }
}

public sealed record StationSnapshot(
    string StationIdentity,
    ProofObservation<IReadOnlyList<string>> OfficerPoolIdentities,
    ProofObservation<int> AvailableVehicleCount,
    ProofObservation<IReadOnlyList<string>> PoliceVehicleIdentities,
    ProofObservation<IReadOnlyList<string>> DeployedVehicleIdentities,
    ProofObservation<float> TimeSinceLastDispatch);

public sealed record OfficerSnapshot(
    string StableIdentity,
    ProofObservation<bool> Active,
    ProofObservation<bool> Alive,
    ProofObservation<string?> TargetIdentity);

public sealed record PlayerSnapshot(
    string? StableIdentity,
    ProofObservation<bool> IsSupportedServerInitialized);

public sealed record DispatchResponseEvidence(
    int ResponseNumber,
    string? ExpectedTargetIdentity,
    string? ObservedTargetIdentity,
    bool DispatchCallCompleted,
    bool TargetAttributionAvailable,
    bool VehicleResponseObserved,
    bool RecoveryObservationAvailable,
    bool RecoveryAdequate,
    bool DeathObservationAvailable,
    bool NaturalDeathObserved,
    bool CapacityShortfallObserved,
    bool DuplicateInvocationObserved,
    bool UnrelatedPlayerEffectObserved,
    bool PersistentExceptionObserved,
    IReadOnlyList<StationSnapshot> BeforeStations,
    IReadOnlyList<StationSnapshot> AfterStations,
    IReadOnlyList<OfficerSnapshot> BeforeResponders,
    IReadOnlyList<OfficerSnapshot> AfterResponders,
    IReadOnlyList<PlayerSnapshot> BeforePlayers,
    IReadOnlyList<PlayerSnapshot> AfterPlayers)
{
    public string? DispatchedStationIdentity { get; init; }

    // This is deliberately separate from AfterStations: AfterStations is the
    // immutable immediate post-dispatch observation used for consumption.
    public IReadOnlyList<StationSnapshot> RecoveryStations { get; init; } = Array.Empty<StationSnapshot>();

    public IReadOnlyList<OfficerSnapshot> RecoveryResponders { get; init; } = Array.Empty<OfficerSnapshot>();

    public IReadOnlyList<PlayerSnapshot> RecoveryPlayers { get; init; } = Array.Empty<PlayerSnapshot>();

    public ProofObservation<IReadOnlyList<string>> ConsumedOfficerIdentities
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DispatchedStationIdentity))
                return ProofObservation<IReadOnlyList<string>>.Unavailable("dispatched station identity unavailable");

            var before = BeforeStations.FirstOrDefault(station =>
                string.Equals(station.StationIdentity, DispatchedStationIdentity, StringComparison.Ordinal));
            var immediate = AfterStations.FirstOrDefault(station =>
                string.Equals(station.StationIdentity, DispatchedStationIdentity, StringComparison.Ordinal));
            if (before is null || immediate is null || !before.OfficerPoolIdentities.IsAvailable ||
                !immediate.OfficerPoolIdentities.IsAvailable)
                return ProofObservation<IReadOnlyList<string>>.Unavailable("dispatched station immediate officer lists unavailable");

            var immediateIdentities = new HashSet<string>(immediate.OfficerPoolIdentities.Value, StringComparer.Ordinal);
            return ProofObservation<IReadOnlyList<string>>.Available(
                before.OfficerPoolIdentities.Value
                    .Where(identity => !immediateIdentities.Contains(identity))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public IReadOnlyList<StationCapacityDelta> StationDeltas =>
        BeforeStations
            .Join(
                AfterStations,
                before => before.StationIdentity,
                after => after.StationIdentity,
                (before, after) => StationCapacityDelta.From(before, after))
            .ToArray();

    public ProofObservation<int> ConsumedOfficerCount =>
        ConsumedOfficerIdentities.IsAvailable
            ? ProofObservation<int>.Available(ConsumedOfficerIdentities.Value.Count)
            : ProofObservation<int>.Unavailable(ConsumedOfficerIdentities.UnavailabilityReason ?? "consumed officer identities unavailable");
}

public sealed record DispatchProofRun(
    string? CanonicalTargetIdentity,
    IReadOnlyList<DispatchResponseEvidence> Responses,
    bool MorePatrolsPresent = false,
    bool UnsupportedPlayerObserved = false,
    bool DuplicateOrUnboundedInvocationObserved = false,
    bool PoolOrPatrolCorruptionObserved = false,
    bool SessionOrSaveCorruptionObserved = false);

public sealed record DispatchProofClassification(
    DispatchProofDecision Decision,
    IReadOnlyList<string> Reasons);
