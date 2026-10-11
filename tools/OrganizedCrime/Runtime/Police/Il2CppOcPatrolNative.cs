using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.Vehicles;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;
using System.Reflection;
using ApiLaw = S1API.Law.LawManager;

namespace OrganizedCrime.Runtime.Police;

internal sealed class Il2CppOcPatrolNative : IOcPatrolNative
{
    private readonly OcOfficerWorldContext _context;
    private bool _deploymentWarned;
    private readonly OcOfficerPoolSweep _poolSweep;
    internal Action<long>? PoolRelease { get; set; }
    internal Il2CppOcPatrolNative(OcOfficerWorldContext context)
    {
        _context = context;
        _poolSweep = new OcOfficerPoolSweep(context._warn);
    }

    internal void ResetOfficerPoolWarnings() => _poolSweep.ResetWarnings();

    internal void PollOfficerPools()
    {
        if (!IsHost) return;
        var released = new HashSet<long>();
        try
        {
            var stations = PoliceStation.PoliceStations;
            if (stations is null) return;
            foreach (var item in stations)
            {
                try
                {
                    var station = item?.TryCast<PoliceStation>();
                    if (!UnityLife.IsAlive(station)) continue;
                    var pool = station!.OfficerPool;
                    if (pool is null) continue;
                    var snapshot = new List<PoliceOfficer?>();
                    for (var i = 0; i < pool.Count; i++)
                    {
                        try { snapshot.Add(pool[i]?.TryCast<PoliceOfficer>()); }
                        catch { snapshot.Add(null); } // Preserve indices around a bad entry.
                    }
                    // Pointer identity also permits removing a dead owned wrapper;
                    // Minted IDs avoid native getters on dying or unreadable clones.
                    _poolSweep.RemoveOwned(IsHost, station.name, snapshot,
                        officer => officer is not null && _context._ownPointers.Contains(officer.Pointer.ToInt64()),
                        officer => _context._ownCloneIds.TryGetValue(officer!.Pointer.ToInt64(), out var id)
                            ? id : $"pointer:{officer.Pointer.ToInt64():x16}",
                        pool.RemoveAt,
                        officer => released.Add(officer!.Pointer.ToInt64()));
                }
                catch { } // A dying station must not prevent cleanup at other stations.
            }
        }
        catch { }
        // Keep ownership intact until every station/duplicate has been swept.
        // The roster owns normal stop-duty, return-car and despawn ordering.
        foreach (var pointer in released)
        {
            try { PoolRelease?.Invoke(pointer); }
            catch { }
        }
    }
    public bool IsHost => _context.IsHost;

    internal OcOfficerWorldStatus TryReadState(OcOfficerCloneRef clone, out OcPatrolCloneState state)
    {
        state = new(false, false);
        if (!IsHost) return OcOfficerWorldStatus.NotHost;
        if (!_context._ownPointers.Contains(clone.Pointer)) return OcOfficerWorldStatus.Rejected;
        try
        {
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || !UnityLife.IsAlive(officer))
                return OcOfficerWorldStatus.InvalidHandle;
            var health = officer!.Health;
            if (!UnityLife.IsAlive(health)) return OcOfficerWorldStatus.NotReady;
            if (health.IsKnockedOut || health.IsDead)
            {
                state = new(true, false);
                return OcOfficerWorldStatus.Ok;
            }
            var stack = officer.Behaviour;
            if (!UnityLife.IsAlive(stack)) return OcOfficerWorldStatus.NotReady;
            var active = stack.activeBehaviour;
            // No name-string or managed `is` test on an Il2Cpp behavior wrapper.
            // Any live active duty above patrol is native-owned. Schedule/Sentry
            // are excluded so their erroneous takeover remains visible in D5a status.
            var busy = UnityLife.IsAlive(active) && active!.Active &&
                !Same(active, officer.FootPatrolBehaviour) &&
                !Same(active, officer.VehiclePatrolBehaviour) &&
                !Same(active, officer.SentryBehaviour) &&
                !UnityLife.IsAlive(active!.TryCast<ScheduleBehaviour>());
            state = new(false, busy);
            return OcOfficerWorldStatus.Ok;
        }
        catch { return OcOfficerWorldStatus.Failed; }
    }

    private static bool Same(UnityEngine.Object? left, UnityEngine.Object? right) =>
        UnityLife.IsAlive(left) && UnityLife.IsAlive(right) && left!.Pointer == right!.Pointer;

    internal IReadOnlyList<string> ReadPoolStatus()
    {
        var lines = new List<string>();
        if (!IsHost) return new[] { "station pools: not host." };
        try
        {
            foreach (var item in PoliceStation.PoliceStations)
            {
                try
                {
                    var station = item?.TryCast<PoliceStation>();
                    if (!UnityLife.IsAlive(station)) continue;
                    var pool = station!.OfficerPool;
                    if (pool is null) { lines.Add($"station {station.name}: pool unknown."); continue; }
                    var owned = OcOfficerPoolSweep.CountOwned(pool.Count,
                        i => pool[i]?.TryCast<PoliceOfficer>(),
                        officer => officer is not null && _context._ownPointers.Contains(officer.Pointer.ToInt64()));
                    lines.Add($"station {station.name}: pool={pool.Count}, OC clones in pool={owned}, free cars={station.AvailableVehicleCount}, deployed cars={station.deployedVehicleCount}, deployed list={station.deployedVehicles?.Count.ToString() ?? "unknown"}, patrol reserve=1.");
                }
                catch (Exception ex) { lines.Add($"station pool: unknown ({ex.GetType().Name})."); }
            }
            return lines.Count == 0 ? new[] { "station pools: unavailable." } : lines;
        }
        catch (Exception ex) { lines.Add($"station pools: unknown ({ex.GetType().Name})."); return lines; }
    }

    public IReadOnlyList<OcPatrolRoute> ReadRoutes(OcPatrolDuty duty)
    {
        var routes = new List<OcPatrolRoute>();
        if (duty == OcPatrolDuty.Foot)
        {
            foreach (var route in ApiLaw.GetAllFootPatrolRoutes())
            {
                var native = route?.GetType().GetField("S1Route", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(route) as FootPatrolRoute;
                if (route is null || !UnityLife.IsAlive(native)) continue;
                AddRoute(routes, route.RouteName, native!, native!.Waypoints,
                    route.WaypointCount, route.GetWaypointPosition);
            }
        }
        else
        {
            foreach (var route in ApiLaw.GetAllVehiclePatrolRoutes())
            {
                var native = route?.GetType().GetField("S1Route", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(route) as VehiclePatrolRoute;
                if (route is null || !UnityLife.IsAlive(native)) continue;
                AddRoute(routes, route.RouteName, native!, native!.Waypoints,
                    route.WaypointCount, route.GetWaypointPosition);
            }
        }
        return routes;
    }

    private static void AddRoute(List<OcPatrolRoute> routes, string name, Component native,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform>? transforms,
        int count, Func<int, Vector3> read)
    {
        // Snapshot live world positions once. No scene fixture mutation or retained transforms.
        if (count <= 0 || transforms is null || transforms.Length != count) return;
        var points = new List<OcOfficerPosition>(count);
        for (var i = 0; i < count; i++)
        {
            if (!UnityLife.IsAlive(transforms[i])) return;
            var p = read(i);
            var position = new OcOfficerPosition(p.x, p.y, p.z);
            if (!position.IsFinite) return;
            points.Add(position);
        }
        routes.Add(new OcPatrolRoute($"{name} [{native.Pointer.ToInt64():x16}]", native, points));
    }

    public object CreateFootGroup(OcPatrolRoute route) => new PatrolGroup((FootPatrolRoute)route.Native);

    public object? LeaseVehicle(OcOfficerPosition target)
    {
        var station = PoliceStation.GetClosestPoliceStation(new Vector3(target.X, target.Y, target.Z));
        if (!UnityLife.IsAlive(station) || !OcPatrolCarReservation.CanLease(station.AvailableVehicleCount)) return null;
        var countBefore = station.deployedVehicleCount;
        var deployedBefore = new HashSet<long>();
        // Without the pre-deploy list a failed deployment can't be told apart from existing cars.
        if (station.deployedVehicles is null) return null;
        foreach (var deployed in station.deployedVehicles)
            if (UnityLife.IsAlive(deployed)) deployedBefore.Add(deployed.Pointer.ToInt64());
        LandVehicle? car = null;
        try
        {
            car = station.DeployVehicle();
            return UnityLife.IsAlive(car) ? new CarLease(station, car!) : null;
        }
        finally
        {
            if (!UnityLife.IsAlive(car)) RecoverFailedDeployment(station, deployedBefore, countBefore);
        }
    }

    private void RecoverFailedDeployment(PoliceStation station, HashSet<long> deployedBefore, int countBefore)
    {
        var countAfter = "unknown";
        var countReturned = "unknown";
        try
        {
            if (!UnityLife.IsAlive(station)) return;
            countAfter = station.deployedVehicleCount.ToString();
            var added = new List<LandVehicle>();
            if (station.deployedVehicles is null) return;
            foreach (var deployed in station.deployedVehicles)
                if (UnityLife.IsAlive(deployed)) added.Add(deployed);
            // Synchronous main-thread call: only newly observed vehicles are candidates.
            OcPatrolDeploymentRecovery.ReturnNew(added, deployedBefore,
                vehicle => vehicle.Pointer.ToInt64(),
                vehicle => { if (UnityLife.IsAlive(vehicle)) station.ReturnVehicle(vehicle); });
            countReturned = station.deployedVehicleCount.ToString();
        }
        catch { }
        finally
        {
            if (!_deploymentWarned)
            {
                _deploymentWarned = true;
                try { _context._warn($"OC patrol DeployVehicle returned no live car or threw: deployed before={countBefore}, after={countAfter}, after recovery={countReturned}; return is unproven, check patrol status."); }
                catch { }
            }
        }
    }

    public OcOfficerWorldStatus PrepareForPatrol(OcOfficerCloneRef clone, out string detail) =>
        OcOfficerDutyBinder.PrepareForPatrol(OwnedOfficer(clone), out detail);

    public void StartFoot(OcOfficerCloneRef clone, object group)
    {
        var officer = OwnedOfficer(clone);
        OcOfficerDutyBinder.SuppressForPatrol(officer);
        // OC-174: retain clone-lifetime recycling suppression when starting duty.
        officer.AutoDeactivate = false;
        // UNKNOWN: what the bool controls; owner QA must observe startup with false.
        officer.StartFootPatrol((PatrolGroup)group, false);
        var foot = officer.FootPatrolBehaviour;
        if (!UnityLife.IsAlive(foot) || !foot.Enabled || foot.Group is null ||
            foot.Group.Pointer != ((PatrolGroup)group).Pointer)
            throw new InvalidOperationException("Foot patrol did not read back on the clone.");
    }

    public void StartVehicle(OcOfficerCloneRef clone, OcPatrolRoute route, object car)
    {
        var officer = OwnedOfficer(clone);
        var lease = (CarLease)car;
        OcOfficerDutyBinder.SuppressForPatrol(officer);
        officer.AutoDeactivate = false;
        officer.StartVehiclePatrol((VehiclePatrolRoute)route.Native, lease.Car);
        var vehicle = officer.VehiclePatrolBehaviour;
        if (!UnityLife.IsAlive(vehicle) || !vehicle.Enabled || !UnityLife.IsAlive(vehicle.Vehicle) ||
            vehicle.Vehicle.Pointer != lease.Car.Pointer || !UnityLife.IsAlive(vehicle.Route) ||
            vehicle.Route.Pointer != ((VehiclePatrolRoute)route.Native).Pointer)
            throw new InvalidOperationException("Vehicle patrol did not read back on the clone.");
    }

    public void Stop(OcOfficerCloneRef clone, OcPatrolDuty duty, object? group, object? car)
    {
        if (!_context._ownPointers.Contains(clone.Pointer))
            throw new InvalidOperationException("Patrol cleanup requires an OC-owned clone.");
        if (OcOfficerSpawner.TryGetOfficer(clone, out var officer) && UnityLife.IsAlive(officer))
        {
            OcOfficerDutyBinder.StopPatrolDuty(officer!, duty);
            if (duty == OcPatrolDuty.Foot)
            {
                var foot = officer!.FootPatrolBehaviour;
                if (UnityLife.IsAlive(foot)) foot._Group_k__BackingField = null;
            }
            else if (car is CarLease lease && UnityLife.IsAlive(lease.Car))
            {
                var current = officer!.CurrentVehicle;
                if (UnityLife.IsAlive(current) && current.Pointer == lease.Car.Pointer)
                {
                    officer.ExitVehicle();
                    current = officer.CurrentVehicle;
                    if (UnityLife.IsAlive(current) && current.Pointer == lease.Car.Pointer)
                        throw new InvalidOperationException("Clone has not exited its leased car.");
                }
                var assigned = officer.AssignedVehicle;
                if (UnityLife.IsAlive(assigned) && assigned.Pointer == lease.Car.Pointer)
                    officer._AssignedVehicle_k__BackingField = null;
                var patrol = officer.VehiclePatrolBehaviour;
                if (UnityLife.IsAlive(patrol)) patrol.Vehicle = null;
                if (UnityLife.IsAlive(lease.Car)) lease.Car.RemoveNPCOccupant(officer);
            }
        }
        if (group is PatrolGroup ownedGroup && UnityLife.IsAlive(ownedGroup.Route))
        {
            // Each OC group contains only its one OC clone, never a station officer.
            // Native tracking/collection of this empty non-Unity group is UNKNOWN.
            ownedGroup.Members.Clear();
        }
        if (car is CarLease vehicleLease && UnityLife.IsAlive(vehicleLease.Car) && UnityLife.IsAlive(vehicleLease.Car.Agent))
            vehicleLease.Car.Agent.StopNavigating();
    }

    public void ReturnVehicleOnClear(object car)
    {
        if (car is CarLease lease && UnityLife.IsAlive(lease.Station) && UnityLife.IsAlive(lease.Car))
            ReturnVehicle(car);
    }

    public void ReturnVehicle(object car)
    {
        var lease = (CarLease)car;
        if (!UnityLife.IsAlive(lease.Station) || !UnityLife.IsAlive(lease.Car))
            throw new InvalidOperationException("Leased station/car is unavailable; return is unproven.");
        // A readback makes retry safe if the native return took effect before throwing.
        if (lease.Station.deployedVehicles.Contains(lease.Car)) lease.Station.ReturnVehicle(lease.Car);
        if (lease.Station.deployedVehicles.Contains(lease.Car))
            throw new InvalidOperationException("Leased car is still deployed after return.");
    }

    private PoliceOfficer OwnedOfficer(OcOfficerCloneRef clone)
    {
        if (!IsHost || !_context._ownPointers.Contains(clone.Pointer) ||
            !OcOfficerSpawner.TryGetOfficer(clone, out var officer) || !UnityLife.IsAlive(officer))
            throw new InvalidOperationException("Patrol starter requires a live OC-owned host clone.");
        return officer!;
    }

    private sealed record CarLease(PoliceStation Station, LandVehicle Car);
}
