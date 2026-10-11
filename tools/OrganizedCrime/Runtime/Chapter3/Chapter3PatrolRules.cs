using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

internal sealed record Chapter3PatrolProfile(string Name, int Foot, int Vehicle, bool ReplacesOrdinary = false)
{
    internal int Total => Foot + Vehicle;
}

internal static class Chapter3PatrolRules
{
    internal const int OrdinaryCap = OcOfficerRoster.MaxOfficers;
    internal const int LockdownCap = OcOfficerRoster.MaximumCapacity;
    internal const int WatchedFoot = 1;
    internal const int CriticalFoot = 1;
    internal const int CriticalVehicle = 1;
    internal const int LockdownFoot = 3;
    internal const int LockdownVehicle = 1;
    internal const int WatchedCheckpoints = Chapter3CheckpointService.OfficersPerCheckpoint;
    // D3 ActiveSet selects three OC sites at Critical, each with this staffing.
    internal const int CriticalCheckpoints = 3 * Chapter3CheckpointService.OfficersPerCheckpoint;
    internal const double WindowStartMinutes = 1080d;
    internal const double WindowEndMinutes = 1380d;
    internal const double DayMinutes = 1440d;
    internal const int RetryPolls = 5;
    internal static readonly Chapter3PatrolProfile None = new("none", 0, 0);

    internal static Chapter3PatrolProfile Select(LocalPressureTier? tier, bool isProtected,
        bool lockdown, double? minutes, Chapter3PatrolProfile? surge = null)
    {
        if (minutes is not { } now || !double.IsFinite(now) || now < 0d) return None;
        var selected = None;
        if (!isProtected)
        {
            if (lockdown) selected = new("lockdown", LockdownFoot, LockdownVehicle);
            else if (now % DayMinutes >= WindowStartMinutes && now % DayMinutes < WindowEndMinutes)
                selected = tier switch
                {
                    LocalPressureTier.Watched => new("watched", WatchedFoot, 0),
                    LocalPressureTier.Critical => new("critical", CriticalFoot, CriticalVehicle),
                    _ => None
                };
        }
        // D6 owns eligibility/expiry/protection of its supplied profile.
        // Select a whole profile; never sum counts from concurrent requests.
        if (surge is { Foot: >= 0, Vehicle: >= 0 } &&
            (surge.Total > selected.Total || (surge.ReplacesOrdinary && (!lockdown || isProtected))))
            selected = surge;
        return selected;
    }
}
