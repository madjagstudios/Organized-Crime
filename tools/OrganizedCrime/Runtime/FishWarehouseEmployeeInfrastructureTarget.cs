using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-117: what the employee infrastructure host writes. The production
/// target is a live Property plus the GameObjects it creates; tests fake
/// this seam, since Il2Cpp objects cannot be constructed off-Unity.
/// </summary>
public interface IFishWarehouseEmployeeInfrastructureTarget<TPoint> where TPoint : class
{
    int EmployeeCapacity { get; set; }
    int HeldEmployeeCount { get; }
    TPoint CreatePoint(string name, float x, float y, float z);
    void SetSpawnPoint(TPoint spawn);
    bool SpawnPointRetained(TPoint spawn);
    void SetIdlePoints(IReadOnlyList<TPoint> points);
    int ReadIdlePointCount();
    string PointName(TPoint point);
    void Teardown();
}

/// <summary>
/// OC-117: the tier capacity the warehouse attach reads, plus the saved
/// employees about to replay. Mod.cs wires the Chapter 3 service and the
/// capture service behind this; the host falls back to tier 1 with no
/// pending replay when it is missing.
/// </summary>
public interface IFishWarehouseEmployeeCapacitySource
{
    int ReadTierCapacity();
    int ReadPendingEmployeeCount();
}

/// <summary>
/// OC-117: the production capacity source over two delegates, so the host
/// stays free of the Chapter 3 and capture services. A faulting read falls
/// back (tier 1, no pending replay) instead of failing the attach.
/// </summary>
public sealed class FishWarehouseEmployeeCapacitySource : IFishWarehouseEmployeeCapacitySource
{
    private readonly Func<int> _readTierCapacity;
    private readonly Func<int> _readPendingEmployeeCount;

    public FishWarehouseEmployeeCapacitySource(Func<int> readTierCapacity, Func<int> readPendingEmployeeCount)
    {
        _readTierCapacity = readTierCapacity ?? throw new ArgumentNullException(nameof(readTierCapacity));
        _readPendingEmployeeCount = readPendingEmployeeCount ?? throw new ArgumentNullException(nameof(readPendingEmployeeCount));
    }

    public int ReadTierCapacity()
    {
        try
        {
            return _readTierCapacity();
        }
        catch
        {
            return Chapter3WarehouseCapacityTiers.Tier1Capacity;
        }
    }

    public int ReadPendingEmployeeCount()
    {
        try
        {
            return Math.Max(0, _readPendingEmployeeCount());
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// OC-117: the production infrastructure target. Creates the spawn point
/// and the 20 idle-point transforms under one staging root parented to
/// the Property, and hands the Property its spawn, capacity, and the
/// first-N idle slice.
/// </summary>
public sealed class FishWarehousePropertyInfrastructureTarget : IFishWarehouseEmployeeInfrastructureTarget<Transform>
{
    private readonly Property _property;
    private GameObject? _staging;

    public FishWarehousePropertyInfrastructureTarget(Property property)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _staging = new GameObject("OC_FishWarehouse_EmployeeInfrastructure");
        _staging.transform.SetParent(property.transform, false);
    }

    public int EmployeeCapacity
    {
        get => _property.EmployeeCapacity;
        set => _property.EmployeeCapacity = value;
    }

    public int HeldEmployeeCount
    {
        get
        {
            try
            {
                return _property.Employees?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public Transform CreatePoint(string name, float x, float y, float z)
    {
        var staging = _staging ?? throw new InvalidOperationException("Fish Warehouse employee infrastructure staging is gone.");
        var point = new GameObject(name).transform;
        point.SetParent(staging.transform, false);
        point.localPosition = new Vector3(x, y, z);
        point.localRotation = Quaternion.identity;
        return point;
    }

    public void SetSpawnPoint(Transform spawn) => _property.NPCSpawnPoint = spawn;

    public bool SpawnPointRetained(Transform spawn)
    {
        try
        {
            return _property.NPCSpawnPoint == spawn;
        }
        catch
        {
            return false;
        }
    }

    public void SetIdlePoints(IReadOnlyList<Transform> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));
        var idle = new Il2CppReferenceArray<Transform>(points.Count);
        for (var index = 0; index < points.Count; index++)
            idle[index] = points[index];
        _property.EmployeeIdlePoints = idle;
    }

    public int ReadIdlePointCount()
    {
        try
        {
            return _property.EmployeeIdlePoints?.Length ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    public string PointName(Transform point)
    {
        try
        {
            return point is null ? "?" : point.name;
        }
        catch
        {
            return "?";
        }
    }

    public void Teardown()
    {
        if (_staging is not null)
            UnityEngine.Object.DestroyImmediate(_staging);
        _staging = null;
    }
}
