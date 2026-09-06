namespace OrganizedCrime.Runtime;

public sealed record NativeDispatchStationSnapshot(
    string StationIdentity,
    IReadOnlyList<string> OfficerIdentities,
    int AvailableVehicleCount);

public static class NativeDispatchOutcomeEvaluator
{
    public static NativeLawResponseResult Evaluate(
        NativeDispatchStationSnapshot? before,
        NativeDispatchStationSnapshot? after,
        NativeLawResponseProfile profile,
        string correlationId)
    {
        if (!TryValidateSnapshot(before) || !TryValidateSnapshot(after))
            return Result(NativeLawResponseResultState.InconclusiveOutcome, correlationId, "station observation was unavailable");

        var diagnostics = Diagnostics(
            before!.StationIdentity,
            before.OfficerIdentities.Count,
            OfficersConsumed(before, after!),
            before.AvailableVehicleCount,
            before.AvailableVehicleCount - after!.AvailableVehicleCount);

        if (before.OfficerIdentities.Count < profile.RequestedOfficerCount ||
            before.AvailableVehicleCount < 1)
        {
            return Result(
                NativeLawResponseResultState.UnavailableCapacity,
                correlationId,
                "station preflight capacity was unavailable",
                diagnostics);
        }

        if (!string.Equals(before.StationIdentity, after.StationIdentity, StringComparison.Ordinal) ||
            diagnostics.OfficersConsumed != profile.RequestedOfficerCount ||
            diagnostics.VehiclesConsumed != 1)
        {
            return Result(
                NativeLawResponseResultState.InconclusiveOutcome,
                correlationId,
                "native dispatch outcome did not match the expected station capacity delta",
                diagnostics);
        }

        return Result(
            NativeLawResponseResultState.Accepted,
            correlationId,
            "native dispatch consumed the expected station capacity",
            diagnostics);
    }

    private static bool TryValidateSnapshot(NativeDispatchStationSnapshot? snapshot)
    {
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.StationIdentity) ||
            snapshot.OfficerIdentities is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in snapshot.OfficerIdentities)
        {
            if (string.IsNullOrWhiteSpace(identity) || !seen.Add(identity))
                return false;
        }

        return true;
    }

    private static int OfficersConsumed(
        NativeDispatchStationSnapshot before,
        NativeDispatchStationSnapshot after) =>
        before.OfficerIdentities.Except(after.OfficerIdentities).Count();

    private static NativeLawResponseDiagnostics Diagnostics(
        string stationIdentity,
        int officersBefore,
        int officersConsumed,
        int vehiclesBefore,
        int vehiclesConsumed) =>
        new(stationIdentity, officersBefore, officersConsumed, vehiclesBefore, vehiclesConsumed);

    private static NativeLawResponseResult Result(
        NativeLawResponseResultState state,
        string correlationId,
        string reason,
        NativeLawResponseDiagnostics? diagnostics = null) =>
        new(state, correlationId, reason, diagnostics);
}
