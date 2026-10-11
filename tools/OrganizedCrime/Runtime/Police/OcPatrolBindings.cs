namespace OrganizedCrime.Runtime.Police;

// Engine-free resource state. Ordinary failed cleanup retains ownership for retry.
// Scene teardown always drops entries after best-effort native cleanup.
internal sealed class OcPatrolBindings : IOcPatrolWorld
{
    private readonly IOcPatrolNative _native;
    private readonly Dictionary<OcOfficerCloneRef, Binding> _bindings = new();

    internal OcPatrolBindings(IOcPatrolNative native) => _native = native;

    public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) =>
        TryBind(clone, duty, target, out detail);

    internal OcOfficerWorldStatus TryBind(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail)
    {
        detail = "patrol unavailable";
        var created = false;
        try
        {
            if (!_native.IsHost) return OcOfficerWorldStatus.NotHost;
            if (clone.Pointer == 0 || clone.Native is null) return OcOfficerWorldStatus.InvalidHandle;
            if (!target.IsFinite || !Enum.IsDefined(typeof(OcPatrolDuty), duty)) return OcOfficerWorldStatus.Rejected;
            var route = OcPatrolRouteSelector.Closest(_native.ReadRoutes(duty), target);
            if (route is null) { detail = "no live native route"; return OcOfficerWorldStatus.NotReady; }
            if (_bindings.TryGetValue(clone, out var previous))
            {
                if (previous.Bound && previous.Duty == duty && StringComparer.Ordinal.Equals(previous.Route.Key, route.Key))
                {
                    detail = $"already bound to {previous.Route.Key}";
                    return OcOfficerWorldStatus.Ok;
                }
                var unbind = TryUnbind(clone, out detail);
                if (unbind != OcOfficerWorldStatus.Ok) return unbind;
            }
            var prepared = _native.PrepareForPatrol(clone, out detail);
            if (prepared != OcOfficerWorldStatus.Ok) return prepared;
            var binding = new Binding(clone, duty, route);
            _bindings.Add(clone, binding);
            created = true;
            if (duty == OcPatrolDuty.Foot)
            {
                binding.Group = _native.CreateFootGroup(route);
                _native.StartFoot(clone, binding.Group);
            }
            else
            {
                binding.Car = _native.LeaseVehicle(target);
                if (binding.Car is null)
                {
                    _bindings.Remove(clone);
                    detail = "no patrol car free (one reserved for forced Wanted), or deployment blocked";
                    return OcOfficerWorldStatus.NotReady;
                }
                _native.StartVehicle(clone, route, binding.Car);
            }
            binding.Bound = true;
            detail = $"{duty} patrol bound to {route.Key}; car leased={binding.Car is not null}";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            var failure = $"patrol bind failed ({exception.GetType().Name})";
            detail = failure;
            if (created)
            {
                var cleanup = TryUnbind(clone, out var cleanupDetail);
                detail += $"; cleanup {cleanup}: {cleanupDetail}";
            }
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => TryUnbind(clone, out detail);

    internal OcOfficerWorldStatus TryUnbind(OcOfficerCloneRef clone, out string detail)
    {
        detail = "no OC patrol duty";
        try
        {
            if (!_native.IsHost) return OcOfficerWorldStatus.NotHost;
            if (!_bindings.TryGetValue(clone, out var binding)) return OcOfficerWorldStatus.Ok;
            binding.Bound = false;
            if (!binding.Stopped)
            {
                _native.Stop(clone, binding.Duty, binding.Group, binding.Car);
                binding.Stopped = true;
            }
            if (binding.Car is not null)
            {
                _native.ReturnVehicle(binding.Car);
                binding.Car = null;
            }
            _bindings.Remove(clone);
            detail = "patrol stopped; group released; leased car returned";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            detail = $"patrol cleanup pending ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    public void ClearPatrols()
    {
        try
        {
            // Losing host authority permits bookkeeping teardown, never client writes.
            if (!_native.IsHost) return;
            foreach (var binding in _bindings.Values)
            {
                if (!binding.Stopped)
                {
                    try { _native.Stop(binding.Clone, binding.Duty, binding.Group, binding.Car); }
                    catch { }
                }
                // Still attempt the car return if stopping a dying clone/group failed.
                if (binding.Car is not null)
                {
                    try { _native.ReturnVehicleOnClear(binding.Car); }
                    catch { }
                }
            }
        }
        catch { }
        finally { _bindings.Clear(); }
    }

    // OC-owned references only; zero does not prove native group collection.
    public int PatrolGroupCount() => _bindings.Values.Count(b => b.Group is not null);
    public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Snapshot();
    internal IReadOnlyList<OcPatrolSnapshot> Snapshot() => _bindings.Values.Select(b =>
        new OcPatrolSnapshot(b.Clone, b.Duty, b.Route.Key, b.Car is not null, b.Bound ? "bound" : "cleanup pending")).ToArray();

    private sealed class Binding
    {
        internal Binding(OcOfficerCloneRef clone, OcPatrolDuty duty, OcPatrolRoute route)
        { Clone = clone; Duty = duty; Route = route; }
        internal readonly OcOfficerCloneRef Clone;
        internal readonly OcPatrolDuty Duty;
        internal readonly OcPatrolRoute Route;
        internal object? Group;
        internal object? Car;
        internal bool Bound;
        internal bool Stopped;
    }
}
