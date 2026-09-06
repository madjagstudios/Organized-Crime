namespace OrganizedCrime.Model;

public sealed record FishWarehouseEmployeePointDefinition(
    string Name,
    float X,
    float Y,
    float Z);

public static class FishWarehouseEmployeeInfrastructureDefinition
{
    public const int Capacity = 10;
    public const float WallMargin = 1.5f;

    public static FishWarehouseEmployeePointDefinition NpcSpawnPoint { get; } =
        new("OC_FishWarehouse_NPCSpawnPoint", -9.4f, FishWarehouseInteriorRoomDefinition.FloorY, 4.8f);

    public static IReadOnlyList<FishWarehouseEmployeePointDefinition> IdlePoints { get; } =
        new[]
        {
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_0", -7.8f, FishWarehouseInteriorRoomDefinition.FloorY, 4.8f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_1", -7.8f, FishWarehouseInteriorRoomDefinition.FloorY, 2.8f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_2", -7.8f, FishWarehouseInteriorRoomDefinition.FloorY, 0.8f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_3", -7.8f, FishWarehouseInteriorRoomDefinition.FloorY, -1.2f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_4", -7.30f, FishWarehouseInteriorRoomDefinition.FloorY, 10.14f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_5", -6.20f, FishWarehouseInteriorRoomDefinition.FloorY, 10.18f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_6", -5.42f, FishWarehouseInteriorRoomDefinition.FloorY, 9.05f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_7", -4.37f, FishWarehouseInteriorRoomDefinition.FloorY, 9.09f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_8", -4.41f, FishWarehouseInteriorRoomDefinition.FloorY, 10.05f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_9", -4.46f, FishWarehouseInteriorRoomDefinition.FloorY, 11.45f)
        };

    public static bool IsInsideRoom(FishWarehouseEmployeePointDefinition point) =>
        MathF.Abs(point.X) <= FishWarehouseInteriorRoomDefinition.HalfWidth - WallMargin &&
        MathF.Abs(point.Z) <= FishWarehouseInteriorRoomDefinition.HalfDepth - WallMargin;

    public static bool IsValidEmployeeIndex(int index) => index >= 0 && index < Capacity;

    public static bool HasValidLayout() => HasValidLayout(IdlePoints);

    public static bool HasValidLayout(IEnumerable<FishWarehouseEmployeePointDefinition?>? points) =>
        IsInsideRoom(NpcSpawnPoint) &&
        FishWarehousePropertyBoundsDefinition.Contains(NpcSpawnPoint.X, NpcSpawnPoint.Y, NpcSpawnPoint.Z) &&
        FishWarehouseEmployeePointLayoutValidator.Validate(
                Capacity,
                points,
                FishWarehouseEmployeePointLayoutRules.Default)
            .Succeeded;
}
