using Il2CppScheduleOne.Property;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-139: the Il2Cpp-free seam the player spawn host builds against, so tests
/// fake the points while production parents real transforms to the Property and
/// assigns its SpawnPoint and InteriorSpawnPoint fields.
/// </summary>
public interface IFishWarehousePlayerSpawnTarget<TPoint> where TPoint : class
{
    TPoint CreateSpawnPoint(string name, float x, float y, float z, float yawDegrees);
    bool TryGroundExterior(TPoint point);
    void SetExteriorSpawn(TPoint spawn);
    void SetInteriorSpawn(TPoint spawn);
    bool SpawnsRetained(TPoint exterior, TPoint interior);
    void Teardown();
}

/// <summary>
/// OC-139: builds both player spawn points as children of the Property root in
/// the same room frame the idle points and the NPC spawn use, then assigns the
/// native spawn fields. Grounds the exterior point with a downward raycast so
/// the player never lands below the street.
/// </summary>
public sealed class FishWarehousePropertySpawnTarget : IFishWarehousePlayerSpawnTarget<Transform>
{
    private const float GroundRayStartAbovePoint = 3f;
    private const float GroundRayDistance = 10f;
    private const float GroundRestAboveHit = 0.1f;

    private readonly Property _property;
    private GameObject? _staging;

    public FishWarehousePropertySpawnTarget(Property property)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _staging = new GameObject("OC_FishWarehouse_PlayerSpawns");
        _staging.transform.SetParent(property.transform, false);
    }

    public Transform CreateSpawnPoint(string name, float x, float y, float z, float yawDegrees)
    {
        var staging = _staging ?? throw new InvalidOperationException("Fish Warehouse player spawn staging is gone.");
        var point = new GameObject(name).transform;
        point.SetParent(staging.transform, false);
        point.localPosition = new Vector3(x, y, z);
        point.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        return point;
    }

    public bool TryGroundExterior(Transform point)
    {
        try
        {
            var origin = point.position + Vector3.up * GroundRayStartAbovePoint;
            if (!Physics.Raycast(origin, Vector3.down, out var hit, GroundRayDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                return false;
            point.position = hit.point + Vector3.up * GroundRestAboveHit;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetExteriorSpawn(Transform spawn) => _property.SpawnPoint = spawn;

    public void SetInteriorSpawn(Transform spawn) => _property.InteriorSpawnPoint = spawn;

    public bool SpawnsRetained(Transform exterior, Transform interior)
    {
        try
        {
            return _property.SpawnPoint == exterior && _property.InteriorSpawnPoint == interior;
        }
        catch
        {
            return false;
        }
    }

    public void Teardown()
    {
        if (_staging is not null)
            UnityEngine.Object.DestroyImmediate(_staging);
        _staging = null;
    }
}
