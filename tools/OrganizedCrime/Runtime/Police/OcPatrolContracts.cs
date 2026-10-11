namespace OrganizedCrime.Runtime.Police;

internal sealed record OcPatrolCloneState(bool Unavailable, bool Busy);

internal enum OcPatrolDuty { Foot, Vehicle }

// Key includes the native route pointer: stable for this scene even when API wrappers are recreated.
internal sealed record OcPatrolRoute(string Key, object Native, IReadOnlyList<OcOfficerPosition> Waypoints);
internal sealed record OcPatrolSnapshot(OcOfficerCloneRef Officer, OcPatrolDuty Duty, string Route, bool CarLeased, string State);

// Separate internal capability: existing officer world implementations and public pins stay intact.
internal interface IOcPatrolWorld
{
    OcOfficerWorldStatus TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state)
    { state = new(false, false); return OcOfficerWorldStatus.NotReady; }
    OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail);
    OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail);
    // Default for adapters without separate teardown state; the production world
    // overrides this to discard entries even when native cleanup fails.
    void ClearPatrols()
    {
        try
        {
            foreach (var binding in PatrolSnapshot())
            {
                try { TryUnbindPatrol(binding.Officer, out _); }
                catch { }
            }
        }
        catch { }
    }
    IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot();
    IReadOnlyList<string> PatrolPoolStatus() => Array.Empty<string>();
    int PatrolGroupCount() => 0;
}

internal interface IOcPatrolNative
{
    bool IsHost { get; }
    OcOfficerWorldStatus PrepareForPatrol(OcOfficerCloneRef clone, out string detail)
    { detail = "no checkpoint duty"; return OcOfficerWorldStatus.Ok; }
    IReadOnlyList<OcPatrolRoute> ReadRoutes(OcPatrolDuty duty);
    object CreateFootGroup(OcPatrolRoute route);
    object? LeaseVehicle(OcOfficerPosition target);
    void StartFoot(OcOfficerCloneRef clone, object group);
    void StartVehicle(OcOfficerCloneRef clone, OcPatrolRoute route, object car);
    void Stop(OcOfficerCloneRef clone, OcPatrolDuty duty, object? group, object? car);
    void ReturnVehicle(object car);
    // Teardown may encounter a destroyed station or car; ordinary unbind retains
    // failed returns for retry, while clear always discards its scene references.
    void ReturnVehicleOnClear(object car) => ReturnVehicle(car);
}

internal static class OcPatrolCarReservation
{
    internal static bool CanLease(int nativeFreeCars) => nativeFreeCars > 1;
}

internal static class OcPatrolRouteSelector
{
    internal static OcPatrolRoute? Closest(IEnumerable<OcPatrolRoute> routes, OcOfficerPosition target)
    {
        if (!target.IsFinite) return null;
        OcPatrolRoute? best = null;
        var distance = double.PositiveInfinity;
        foreach (var route in routes)
        {
            // A missing waypoint can also break native navigation; reject the whole route.
            if (route.Waypoints.Count == 0 || route.Waypoints.Any(p => !p.IsFinite)) continue;
            var nearest = route.Waypoints.Min(p => p.DistanceTo(target));
            if (nearest < distance || (nearest == distance &&
                (best is null || StringComparer.Ordinal.Compare(route.Key, best.Key) < 0)))
            {
                best = route;
                distance = nearest;
            }
        }
        return best;
    }
}

// Do not allocate patrol resources until the existing networked checkpoint
// removal sequence succeeds and its duty/list readback is clear.
internal static class OcPatrolCheckpointTransition
{
    internal static OcOfficerWorldStatus TryUnbind(Func<string> unbind, Func<bool> stillAssigned, out string detail)
    {
        try
        {
            var note = unbind();
            if (!note.StartsWith("checkpoint disabled", StringComparison.Ordinal) || stillAssigned())
            {
                detail = $"checkpoint cleanup pending: {note}; patrol not ready";
                return OcOfficerWorldStatus.NotReady;
            }
            detail = note;
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception ex)
        {
            detail = $"checkpoint cleanup pending ({ex.GetType().Name}); patrol not ready";
            return OcOfficerWorldStatus.NotReady;
        }
    }
}

internal static class OcPatrolDeploymentRecovery
{
    internal static void ReturnNew<T>(IEnumerable<T> deployed, ISet<long> before, Func<T, long> pointer, Action<T> returnVehicle)
    {
        // Snapshot before a native return mutates the source collection.
        foreach (var vehicle in deployed.ToArray())
        {
            try { if (!before.Contains(pointer(vehicle))) returnVehicle(vehicle); }
            catch { }
        }
    }
}
