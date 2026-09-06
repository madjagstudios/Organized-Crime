namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 lockdown gate re-assert log throttle. The Harmony postfix that re-asserts curfew active
/// fires every time the native per-minute recompute runs, but the owner protocol only wants one
/// receipt line per distinct game minute, not one per native call. This keys on the game's own HHMM
/// clock int (<c>TimeManager.CurrentTime</c>) and reports true the first time a given key is seen,
/// false on every repeat of that same key, until a different key arrives. No Unity, S1API, Il2Cpp, or
/// MelonLoader reference, so it is fully unit testable. <see cref="Release1LockdownGatePatch"/> is the
/// only production caller.
/// </summary>
public sealed class Release1LockdownGateThrottle
{
    private int? _lastLoggedMinuteKey;

    public bool TryMark(int minuteKey)
    {
        if (_lastLoggedMinuteKey.HasValue && _lastLoggedMinuteKey.Value == minuteKey) return false;
        _lastLoggedMinuteKey = minuteKey;
        return true;
    }

    public void Reset() => _lastLoggedMinuteKey = null;
}
