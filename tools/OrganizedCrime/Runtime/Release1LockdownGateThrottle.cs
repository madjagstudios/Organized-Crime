using OrganizedCrime.Core.Scheduling;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 lockdown gate re-assert log throttle. The Harmony postfix that re-asserts curfew active
/// fires every time the native per-minute recompute runs, but the owner protocol only wants one
/// receipt line per distinct game minute, not one per native call. This keys on the game's own HHMM
/// clock int (<c>TimeManager.CurrentTime</c>) and reports true the first time a given key is seen,
/// false on every repeat of that same key, until a different key arrives. No Unity, S1API, Il2Cpp, or
/// MelonLoader reference, so it is fully unit testable. <see cref="Release1LockdownGatePatch"/> is the
/// only production caller. A thin <see cref="GameHourGate"/> wrapper with a constant revision:
/// every integer key survives the floor exactly, including the -1 unknown-clock sentinel.
/// </summary>
public sealed class Release1LockdownGateThrottle
{
    private readonly GameHourGate _gate = new();

    public bool TryMark(int minuteKey) => _gate.TryPass(minuteKey, 0L);

    public void Reset() => _gate.Clear();
}
