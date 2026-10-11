namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: the raw S1API PursuitLevel values the forced-Wanted trigger compares, pinned
/// against the installed S1API by the boundary tests. Kept as ints so the seam interfaces
/// stay S1API free and fakes stay trivial.
/// </summary>
public static class Chapter3PursuitLevels
{
    public const int None = 0;
    public const int Arresting = 2;
}

/// <summary>
/// OC-74: the trigger inputs gathered from the world, as plain data for the pure
/// <see cref="Release1ChiefLedger.ShouldFireForcedWanted"/> predicate.
/// </summary>
public sealed record Chapter3ForcedWantedTrigger(
    bool IsHost,
    bool CurfewActive,
    bool Outdoors,
    int? PursuitLevel,
    int Night,
    bool? Arrested);

/// <summary>OC-74: reads whether the game's curfew is currently active. Total: false when unreadable.</summary>
public interface IChapter3CurfewReader
{
    bool TryReadActive(out bool active);
}

/// <summary>
/// OC-74: sets the local player to Arresting and reads the level back. The interface exposes
/// no level parameter, so OC can never set anything above Arresting by construction.
/// </summary>
public interface IChapter3WantedWriter
{
    bool TrySetArresting(out int readBackLevel);
}

/// <summary>
/// OC-74: sends one two-officer vehicle dispatch for a forced-Wanted fire through the OC-41
/// adapter path. False when the request cannot be built or sent; the result carries the
/// adapter's own outcome (Accepted, UnavailableCapacity, ...).
/// </summary>
public interface IChapter3ForcedWantedDispatch
{
    bool TryDispatch(string playerId, Guid sessionEpoch, long loadEpoch, string correlationId, out NativeLawResponseResult? result);
}

/// <summary>
/// OC-74 amendment: clamps the local player's pursuit timer while an armed lockdown holds,
/// so Wanted never times out. Reads the S1API crime data; the write drops to the native
/// interop because S1API 3.2.1 exposes TimeSinceSighted get-only (pinned by the boundary
/// tests). Both members are total and never throw: false on any failure, and the hold
/// leaves the game alone.
/// </summary>
public interface IChapter3PursuitHold
{
    bool TryRead(out int pursuitLevel, out float timeSinceSighted, out float searchTime);
    bool TryWriteTimeSinceSighted(float value);
}
