namespace OrganizedCrime.PoliceDispatchProof;

public sealed record DispatchRecoveryEvaluation(
    bool RequiredObservationsAvailable,
    bool RecoveryAdequate,
    IReadOnlyList<string> ConsumedOfficerIdentities,
    string Reason);

public static class DispatchRecoveryEvaluator
{
    public static DispatchRecoveryEvaluation Evaluate(DispatchResponseEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.DispatchedStationIdentity))
            return Unavailable("dispatched station identity unavailable");

        var before = Find(evidence.BeforeStations, evidence.DispatchedStationIdentity);
        var immediate = Find(evidence.AfterStations, evidence.DispatchedStationIdentity);
        var recovery = Find(evidence.RecoveryStations, evidence.DispatchedStationIdentity);
        if (before is null || immediate is null || recovery is null)
            return Unavailable("dispatched station was not present in every required observation phase");

        if (!before.OfficerPoolIdentities.IsAvailable || !immediate.OfficerPoolIdentities.IsAvailable ||
            !recovery.OfficerPoolIdentities.IsAvailable || !before.AvailableVehicleCount.IsAvailable ||
            !immediate.AvailableVehicleCount.IsAvailable || !recovery.AvailableVehicleCount.IsAvailable)
            return Unavailable("dispatched station officer pool or vehicle capacity observation unavailable");

        var consumed = before.OfficerPoolIdentities.Value
            .Where(identity => !immediate.OfficerPoolIdentities.Value.Contains(identity, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (consumed.Length < DispatchProofPins.RequestedOfficerCount)
            return Available(consumed, false, "fewer than two exact officer identities were consumed at dispatch");

        var recoveryPool = new HashSet<string>(recovery.OfficerPoolIdentities.Value, StringComparer.Ordinal);
        var officersReturned = consumed.All(recoveryPool.Contains) &&
            recovery.OfficerPoolIdentities.Value.Count >= before.OfficerPoolIdentities.Value.Count;
        var vehicleCapacityReturned = recovery.AvailableVehicleCount.Value == before.AvailableVehicleCount.Value;
        return Available(
            consumed,
            officersReturned && vehicleCapacityReturned,
            officersReturned && vehicleCapacityReturned
                ? "exact consumed officers returned and available vehicle capacity recovered"
                : "exact consumed officers or available vehicle capacity did not recover");
    }

    private static StationSnapshot? Find(IEnumerable<StationSnapshot> stations, string identity) =>
        stations.FirstOrDefault(station => string.Equals(station.StationIdentity, identity, StringComparison.Ordinal));

    private static DispatchRecoveryEvaluation Unavailable(string reason) =>
        new(false, false, Array.Empty<string>(), reason);

    private static DispatchRecoveryEvaluation Available(IReadOnlyList<string> consumed, bool adequate, string reason) =>
        new(true, adequate, consumed, reason);
}
