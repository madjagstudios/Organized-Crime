namespace OrganizedCrime.Runtime;

/// <summary>What one production pass reads as. Richer than the engine's three values only in that NotReady
/// carries a reason; the mapping method below is the only place this mission speaks the engine's vocabulary.</summary>
public enum Release1ProductionObservation { NotReady, Quiet, Working }

/// <summary>Why a NotReady pass could not decide. A property count mismatch is an ordinary player action,
/// buying or selling mid attempt, that the engine still holds on; this discriminator exists so the mission
/// service and the presentation layer can tell it from a genuine unready read (decision 19).</summary>
public enum Release1ProductionHoldReason { None, WorldNotReady, OwnedPropertyCountMismatch }

/// <summary>One station as the previous pass saw it. Held in memory by the mission service, never persisted.</summary>
public sealed record Release1ProductionStationFingerprint(
    string StationGuid,
    Release1ProductionStationKind Kind,
    bool Running,
    string? OperationIdentity,
    int Progress);

public sealed record Release1ProductionObservationResult(
    Release1ProductionObservation Observation,
    Release1ProductionHoldReason Reason,
    IReadOnlyList<Release1ProductionStationFingerprint> Stations);

/// <summary>
/// Pure. Reads no world, writes no story, holds no state, and has no clock. Returns an observation, a
/// reason, and the fingerprint set to carry into the next pass.
///
/// A baseline pass is the first pass after any lifecycle boundary, including acceptance itself. It can
/// return NotReady or Quiet but never Working on a station alone, because there is no previous pass to
/// compare against and an operation already running when the world loaded is one the player is allowed
/// to let finish. An employee working still reads Working on a baseline pass: that needs no history.
/// </summary>
public static class Release1ProductionActivityClassifier
{
    public static Release1ProductionObservationResult Classify(
        Release1ProductionActivitySnapshot? snapshot,
        Release1SmallCourtesyWorldReadStatus readStatus,
        int expectedOwnedPropertyCount,
        IReadOnlyList<Release1ProductionStationFingerprint>? previousStations,
        bool isBaselinePass)
    {
        if (expectedOwnedPropertyCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedOwnedPropertyCount));
        var carried = previousStations ?? Array.Empty<Release1ProductionStationFingerprint>();

        if (readStatus != Release1SmallCourtesyWorldReadStatus.Ready || snapshot is null)
            return new(Release1ProductionObservation.NotReady, Release1ProductionHoldReason.WorldNotReady, carried);
        if (snapshot.Properties.Count != expectedOwnedPropertyCount)
            return new(Release1ProductionObservation.NotReady, Release1ProductionHoldReason.OwnedPropertyCountMismatch, carried);

        var stations = snapshot.Properties
            .SelectMany(property => property.Stations)
            .Select(station => new Release1ProductionStationFingerprint(
                station.StationGuid, station.Kind, station.Running, station.OperationIdentity, station.Progress))
            .OrderBy(station => station.StationGuid, StringComparer.Ordinal)
            .ToArray();

        // Decision 5: Employee.IsAnyWorkInProgress() is the only decisive working read. A fired employee
        // is never working whatever the native flag says, because a fired employee is not the player's.
        if (snapshot.Properties.Any(property => property.Employees.Any(employee =>
                employee is not null && !employee.Fired && employee.IsAnyWorkInProgress)))
            return new(Release1ProductionObservation.Working, Release1ProductionHoldReason.None, stations);

        if (isBaselinePass) return new(Release1ProductionObservation.Quiet, Release1ProductionHoldReason.None, stations);

        var previous = new Dictionary<string, Release1ProductionStationFingerprint>(StringComparer.Ordinal);
        foreach (var station in carried)
            if (station is not null) previous[station.StationGuid] = station;

        foreach (var station in stations)
            if (StartedSomethingNew(station, previous.TryGetValue(station.StationGuid, out var before) ? before : null))
                return new(Release1ProductionObservation.Working, Release1ProductionHoldReason.None, stations);

        return new(Release1ProductionObservation.Quiet, Release1ProductionHoldReason.None, stations);
    }

    public static Release1WindowObservation ToWindowObservation(Release1ProductionObservation observation) => observation switch
    {
        Release1ProductionObservation.Quiet => Release1WindowObservation.Clean,
        Release1ProductionObservation.Working => Release1WindowObservation.Breached,
        _ => Release1WindowObservation.Unreadable
    };

    /// <summary>
    /// New means: running now and absent before, or running now and stopped before, or running at a
    /// different operation identity, or running at the same identity with the progress counter gone
    /// backwards. A station that stopped, and an operation that completed, are both never new. Every
    /// progress figure the adapter emits is an increasing quantity or a constant zero, so a decrease is
    /// always a genuine restart.
    /// </summary>
    private static bool StartedSomethingNew(
        Release1ProductionStationFingerprint now,
        Release1ProductionStationFingerprint? before)
    {
        if (!now.Running) return false;
        if (before is null || !before.Running) return true;
        if (!string.Equals(now.OperationIdentity, before.OperationIdentity, StringComparison.Ordinal)) return true;
        return now.Progress < before.Progress;
    }
}
