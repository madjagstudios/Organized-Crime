namespace OrganizedCrime.Model;

public sealed record FishWarehousePlayerSpawnPointDefinition(string Name, float X, float Y, float Z, float YawDegrees);

/// <summary>
/// OC-139: the two room-frame player spawn points the runtime warehouse assigns
/// to the Property's SpawnPoint (exterior) and InteriorSpawnPoint fields. Both
/// were null, so an arrest after visiting the warehouse hung the Continue
/// routine on a black screen. Yaw degrees face +z at 0, like a Unity transform.
/// </summary>
public static class FishWarehousePlayerSpawnDefinition
{
    // Between the building (room edge z 6.9) and the OC-117 idle block (z 10.9
    // and up), facing +z away from the building. The owner moves this live with
    // `oc_ch3 warehouse spawn here` after the run.
    public static FishWarehousePlayerSpawnPointDefinition PlayerExteriorSpawn { get; } =
        new("OC_FishWarehouse_PlayerExteriorSpawn", 1.2f, FishWarehouseInteriorRoomDefinition.FloorY, 9.9f, 0f);

    // A metre east of the employee spawn, facing +x into the room off the west wall.
    public static FishWarehousePlayerSpawnPointDefinition PlayerInteriorSpawn { get; } =
        new("OC_FishWarehouse_PlayerInteriorSpawn", -8.4f, FishWarehouseInteriorRoomDefinition.FloorY, 4.8f, 90f);

    public const float ExteriorIdleClearance = 0.8f;

    public static bool HasValidLayout() =>
        IsInsideRoom(PlayerInteriorSpawn) &&
        !IsInsideRoom(PlayerExteriorSpawn) &&
        ExteriorClearsIdleBlock();

    public static bool IsInsideRoom(FishWarehousePlayerSpawnPointDefinition point) =>
        Math.Abs(point.X) <= FishWarehouseInteriorRoomDefinition.HalfWidth &&
        Math.Abs(point.Z) <= FishWarehouseInteriorRoomDefinition.HalfDepth &&
        point.Y >= FishWarehouseInteriorRoomDefinition.FloorY &&
        point.Y <= FishWarehouseInteriorRoomDefinition.CeilingY;

    public static bool ExteriorClearsIdleBlock() =>
        FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.All(idle =>
            DistanceXZ(PlayerExteriorSpawn.X, PlayerExteriorSpawn.Z, idle.X, idle.Z) >= ExteriorIdleClearance);

    private static float DistanceXZ(float x1, float z1, float x2, float z2)
    {
        var dx = x1 - x2;
        var dz = z1 - z2;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
