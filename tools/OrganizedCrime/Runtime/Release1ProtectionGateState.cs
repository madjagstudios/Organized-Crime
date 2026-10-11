namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-118. The counters the protection QA status reports: civilian calls dropped, patrol starts
/// skipped and run per kind, sentry starts skipped, and checkpoint re-enables blocked.
/// </summary>
public sealed record Release1ProtectionGateCounters(
    long CallsDropped,
    long FootPatrolsSkipped,
    long FootPatrolsRun,
    long VehiclePatrolsSkipped,
    long VehiclePatrolsRun,
    long SentriesSkipped,
    long CheckpointsBlocked);

/// <summary>
/// OC-118. The decision core behind the protection law patches. The Harmony prefixes themselves are
/// thin (a provider read, a decision call, a boolean); everything about when a gate is active lives
/// here, behind settable providers, so the behavior tests fake cover, host and day without ever
/// loading native code. Every decision fails open: a missing or throwing provider reads as
/// unprotected, off-host, or dateless, and the native call runs. Counters are monotonic per load;
/// Reset clears them for tests and for mod unload.
/// </summary>
public static class Release1ProtectionGateState
{
    private static readonly object Gate = new();

    /// <summary>Whether the Chief's protection covers the local player now (production: the Chief service's own read).</summary>
    public static Func<bool>? IsCovered { get; set; }

    /// <summary>Whether this process is the authoritative host (production: the mod shell's own read).</summary>
    public static Func<bool>? IsHost { get; set; }

    /// <summary>The whole game day the patrol coin flips on (production: the late-fee clock's whole days).</summary>
    public static Func<int?>? Today { get; set; }

    public static long CallsDropped { get; private set; }
    public static long FootPatrolsSkipped { get; private set; }
    public static long FootPatrolsRun { get; private set; }
    public static long VehiclePatrolsSkipped { get; private set; }
    public static long VehiclePatrolsRun { get; private set; }
    public static long SentriesSkipped { get; private set; }
    public static long CheckpointsBlocked { get; private set; }

    public static void Reset()
    {
        lock (Gate)
        {
            IsCovered = null;
            IsHost = null;
            Today = null;
            CallsDropped = 0;
            FootPatrolsSkipped = 0;
            FootPatrolsRun = 0;
            VehiclePatrolsSkipped = 0;
            VehiclePatrolsRun = 0;
            SentriesSkipped = 0;
            CheckpointsBlocked = 0;
        }
    }

    public static Release1ProtectionGateCounters Snapshot()
    {
        lock (Gate)
        {
            return new Release1ProtectionGateCounters(
                CallsDropped, FootPatrolsSkipped, FootPatrolsRun, VehiclePatrolsSkipped, VehiclePatrolsRun,
                SentriesSkipped, CheckpointsBlocked);
        }
    }

    /// <summary>Drops a civilian police report, and only one: covered, host, and reported on the local player.</summary>
    public static bool ShouldDropPoliceCall(bool reportedPlayerIsLocal)
    {
        if (!reportedPlayerIsLocal) return false;
        if (!Active()) return false;
        lock (Gate) CallsDropped++;
        return true;
    }

    /// <summary>
    /// OC-118 owner run: a civilian's own phone call (CallPoliceBehaviour) sets the reported player
    /// to Investigating and adds the crime before it ever reaches LawManager.PoliceCalled (f9 native
    /// recovery of RpcLogic___FinalizeCall: IsTargetValid, RecordLastKnownPosition, SetPursuitLevel,
    /// AddCrime, then PoliceCalled on the server). Covered, host, and targeting the local player
    /// makes that call's target invalid, so the behaviour backs out through its own path. Not
    /// counted: IsTargetValid runs every behaviour update, and the counter is for whole calls.
    /// </summary>
    public static bool ShouldVoidCivilianCallTarget(bool targetIsLocal) =>
        targetIsLocal && Active();

    /// <summary>
    /// The instance-level foot gate (<see cref="ShouldSkipFootPatrol"/> is the funnel). Same coin,
    /// but counts only the blocks: a run stays silent here because the funnel counts the run when
    /// the instance start funnels into it, so every spawn is counted exactly once. Skipping at
    /// entry leaves no half-started state: nothing in the instance body runs at all.
    /// </summary>
    public static bool ShouldSkipFootPatrolInstance(string? routeKey, int? day)
    {
        if (!Active()) return false;
        if (routeKey is null || day is null) return false;
        if (!SkipCoin(routeKey, day.Value)) return false;
        lock (Gate) FootPatrolsSkipped++;
        return true;
    }

    /// <summary>
    /// The funnel-level foot gate on LawManager.StartFootpatrol: the single spawn point every foot
    /// patrol passes through, so this is where runs are counted. Shares its coin and route key
    /// with <see cref="ShouldSkipFootPatrolInstance"/>, so both levels always agree.
    /// </summary>
    public static bool ShouldSkipFootPatrol(string? routeKey, int? day)
    {
        if (!Active()) return false;
        if (routeKey is null || day is null) { lock (Gate) FootPatrolsRun++; return false; }
        if (!SkipCoin(routeKey, day.Value)) { lock (Gate) FootPatrolsRun++; return false; }
        lock (Gate) FootPatrolsSkipped++;
        return true;
    }

    /// <summary>
    /// The instance-level vehicle gate (<see cref="ShouldSkipVehiclePatrol"/> is the funnel). Same
    /// coin, but counts only the blocks; runs stay silent for the funnel to count. Required in
    /// both directions: the schedule's Evaluate calls the funnel directly, bypassing the instance
    /// start, and direct instance starts must not half-run their reads before a funnel block.
    /// </summary>
    public static bool ShouldSkipVehiclePatrolInstance(string? routeKey, int? day)
    {
        if (!Active()) return false;
        if (routeKey is null || day is null) return false;
        if (!SkipCoin(routeKey, day.Value)) return false;
        lock (Gate) VehiclePatrolsSkipped++;
        return true;
    }

    /// <summary>
    /// The funnel-level vehicle gate on LawManager.StartVehiclePatrol: the single spawn point
    /// every vehicle patrol passes through, so this is where runs are counted. Shares its coin
    /// and route key with <see cref="ShouldSkipVehiclePatrolInstance"/>, so both levels agree.
    /// </summary>
    public static bool ShouldSkipVehiclePatrol(string? routeKey, int? day)
    {
        if (!Active()) return false;
        if (routeKey is null || day is null) { lock (Gate) VehiclePatrolsRun++; return false; }
        if (!SkipCoin(routeKey, day.Value)) { lock (Gate) VehiclePatrolsRun++; return false; }
        lock (Gate) VehiclePatrolsSkipped++;
        return true;
    }

    /// <summary>Skips every sentry start while the gates are active.</summary>
    public static bool ShouldSkipSentry()
    {
        if (!Active()) return false;
        lock (Gate) SentriesSkipped++;
        return true;
    }

    /// <summary>Blocks checkpoint re-enables while the gates are active; disables always pass through.</summary>
    public static bool ShouldBlockCheckpointEnable(bool enabling)
    {
        if (!enabling) return false;
        if (!Active()) return false;
        lock (Gate) CheckpointsBlocked++;
        return true;
    }

    // Covered on the host, through providers that may be missing or throwing: any failure reads as
    // inactive, so the native call runs exactly as if unprotected.
    private static bool Active()
    {
        try
        {
            return (IsCovered?.Invoke() ?? false) && (IsHost?.Invoke() ?? false);
        }
        catch
        {
            return false;
        }
    }

    // FNV-1a over "key:day", low bit decides: stable within the build for a given key and day (so a
    // patrol never flickers on and off within a day), evenly split across instances, and a fresh
    // split every day. Deliberately not stable across builds: the split is allowed to move on.
    internal static bool SkipCoin(string key, int day)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            var text = string.Concat(key, ":", day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }
            return (hash & 1) == 1;
        }
    }
}
