namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 lockdown gate pure state machine. Tracks only two facts: whether the mod-owned lockdown is
/// currently engaged, and whether this gate itself flipped the save's curfew IsEnabled switch on when
/// it engaged, so release knows whether it must flip that switch back off. No Unity, S1API, Il2Cpp,
/// or MelonLoader reference anywhere in this file, so it is fully unit testable without the game or a
/// build of Schedule I present. <see cref="Release1LockdownGatePatch"/> is the only production caller.
/// </summary>
public sealed class Release1LockdownGateState
{
    public bool LockdownActive { get; private set; }

    public bool CurfewEnabledByGate { get; private set; }

    /// <summary>
    /// Engages the lockdown. Idempotent: a second call while already engaged changes nothing and
    /// keeps whatever the first call recorded for <see cref="CurfewEnabledByGate"/>, since only the
    /// first press's Enable attempt (or lack of one) is the one that actually happened against the
    /// save.
    /// </summary>
    public void Engage(bool curfewEnabledByThisCall)
    {
        if (LockdownActive) return;
        LockdownActive = true;
        CurfewEnabledByGate = curfewEnabledByThisCall;
    }

    /// <summary>
    /// Releases the lockdown and returns whether this gate had itself enabled curfew on engage, so
    /// the caller knows whether to also call the game's Disable path. Idempotent: releasing while
    /// already inactive returns false and changes nothing.
    /// </summary>
    public bool Release()
    {
        if (!LockdownActive) return false;
        var shouldDisableCurfew = CurfewEnabledByGate;
        LockdownActive = false;
        CurfewEnabledByGate = false;
        return shouldDisableCurfew;
    }
}
