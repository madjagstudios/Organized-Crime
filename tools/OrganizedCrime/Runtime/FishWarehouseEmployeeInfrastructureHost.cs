using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseEmployeeInfrastructureHost<TPoint> where TPoint : class
{
    private IFishWarehouseEmployeeInfrastructureTarget<TPoint>? _target;
    private IReadOnlyList<TPoint>? _created;
    private int _appliedCapacity;

    public bool IsReady => _target is not null;

    public int AppliedCapacity => _appliedCapacity;

    /// <summary>
    /// OC-117: all 20 created idle-point handles, in definition order.
    /// Navigation probes every one from tier 1 on; the Property only ever
    /// sees the first <see cref="AppliedCapacity"/>.
    /// </summary>
    public IReadOnlyList<TPoint> CreatedIdlePoints => _created ?? Array.Empty<TPoint>();

    /// <summary>
    /// OC-117: creates all 20 idle points plus the spawn point, then hands
    /// the target the tier slice: capacity N and the first N of the same
    /// handles. One-shot. Never sets capacity below the employees the
    /// target already holds or the saved replay is about to restore (capped
    /// at the defined 20, with a warning naming both numbers): the guard
    /// against a load-order race dropping replayed employees.
    /// </summary>
    public bool TryAttach(
        IFishWarehouseEmployeeInfrastructureTarget<TPoint>? target,
        IFishWarehouseEmployeeCapacitySource? source = null,
        Action<string>? log = null)
    {
        if (IsReady)
            return true;
        if (target is null)
            return false;

        try
        {
            if (!FishWarehouseEmployeeInfrastructureDefinition.HasValidLayout())
            {
                throw new InvalidOperationException(
                    "Fish Warehouse employee point definition failed its bounds/capacity contract.");
            }

            var spawn = target.CreatePoint(
                FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.Name,
                FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.X,
                FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.Y,
                FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.Z);
            var created = new List<TPoint>(FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count);
            foreach (var definition in FishWarehouseEmployeeInfrastructureDefinition.IdlePoints)
                created.Add(target.CreatePoint(definition.Name, definition.X, definition.Y, definition.Z));

            var tierCapacity = ClampTierCapacity(source?.ReadTierCapacity() ?? Chapter3WarehouseCapacityTiers.Tier1Capacity);
            var required = Math.Max(target.HeldEmployeeCount, source?.ReadPendingEmployeeCount() ?? 0);
            var capacity = Math.Min(FishWarehouseEmployeeInfrastructureDefinition.DefinedIdlePointCount, Math.Max(tierCapacity, required));
            if (required > tierCapacity)
            {
                log?.Invoke(
                    $"Fish Warehouse employee capacity lifted for the saved replay: tier allows {tierCapacity} but {required} employees are held or pending; attaching at {capacity}.");
            }

            target.SetSpawnPoint(spawn);
            target.EmployeeCapacity = capacity;
            target.SetIdlePoints(created.Take(capacity).ToArray());

            if (target.EmployeeCapacity != capacity ||
                !target.SpawnPointRetained(spawn) ||
                target.ReadIdlePointCount() != capacity)
            {
                throw new InvalidOperationException(
                    "Fish Warehouse Property did not retain employee infrastructure references.");
            }

            _target = target;
            _created = created.AsReadOnly();
            _appliedCapacity = capacity;
            OrganizedCrimeLog.Diagnostic(
                $"Fish Warehouse employee infrastructure enabled (capacity={target.EmployeeCapacity}, idlePoints={target.ReadIdlePointCount()}, npcSpawn={target.PointName(spawn)}).", log);
            return true;
        }
        catch (Exception ex)
        {
            target.Teardown();
            log?.Invoke($"Fish Warehouse employee infrastructure failed safely: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// OC-117: the live tier raise. Replaces the idle slice with the
    /// longer (or QA-shortened) prefix of the same handles, so indices
    /// below the old capacity keep their objects, and reads both values
    /// back. Native hiring and idle assignment read the live fields per
    /// call, so no refresh is needed. Never drops below the held
    /// employees.
    /// </summary>
    public bool TryRaiseCapacity(int tierCapacity, Action<string>? log = null)
    {
        var target = _target;
        var created = _created;
        if (target is null || created is null)
            return false;

        try
        {
            var tier = ClampTierCapacity(tierCapacity);
            var held = Math.Max(0, target.HeldEmployeeCount);
            var capacity = Math.Min(created.Count, Math.Max(tier, held));
            if (capacity == _appliedCapacity)
            {
                log?.Invoke(
                    $"Fish Warehouse employee capacity already at {capacity}; no raise applied.");
                return true;
            }
            if (held > tier)
            {
                log?.Invoke(
                    $"Fish Warehouse employee capacity lifted for the held employees: tier allows {tier} but {held} are held; raising to {capacity}.");
            }

            target.EmployeeCapacity = capacity;
            target.SetIdlePoints(created.Take(capacity).ToArray());

            if (target.EmployeeCapacity != capacity || target.ReadIdlePointCount() != capacity)
            {
                throw new InvalidOperationException(
                    "Fish Warehouse Property did not retain the raised employee capacity.");
            }

            log?.Invoke(
                $"Fish Warehouse employee capacity raised {_appliedCapacity} -> {capacity} (idlePoints={target.ReadIdlePointCount()}).");
            _appliedCapacity = capacity;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse employee capacity raise failed safely: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _target?.Teardown();
        _target = null;
        _created = null;
        _appliedCapacity = 0;
    }

    private static int ClampTierCapacity(int tierCapacity)
    {
        if (tierCapacity < Chapter3WarehouseCapacityTiers.Tier1Capacity)
            return Chapter3WarehouseCapacityTiers.Tier1Capacity;
        if (tierCapacity > FishWarehouseEmployeeInfrastructureDefinition.DefinedIdlePointCount)
            return FishWarehouseEmployeeInfrastructureDefinition.DefinedIdlePointCount;
        return tierCapacity;
    }
}
