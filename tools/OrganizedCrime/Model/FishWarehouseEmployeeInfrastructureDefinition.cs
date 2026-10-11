namespace OrganizedCrime.Model;

public sealed record FishWarehouseEmployeePointDefinition(
    string Name,
    float X,
    float Y,
    float Z);

public static class FishWarehouseEmployeeInfrastructureDefinition
{
    /// <summary>
    /// OC-117: the defined idle points. All 20 transforms are created at
    /// attach; the Property is handed only the first N for the paid tier
    /// (12 / 16 / 20), while navigation probes all 20 from tier 1 on. The
    /// owner placed the block live with `oc_ch3 capacity here` (2026-10-04):
    /// outside, east of the garage, clear of the sidewalk pedestrians use and
    /// of the loading-dock frontage, 0.8 m apart. Native WaitOutside sends
    /// idle employees here.
    /// </summary>
    public const int DefinedIdlePointCount = 20;
    public const float WallMargin = 1.5f;

    public static FishWarehouseEmployeePointDefinition NpcSpawnPoint { get; } =
        new("OC_FishWarehouse_NPCSpawnPoint", -9.4f, FishWarehouseInteriorRoomDefinition.FloorY, 4.8f);

    public static IReadOnlyList<FishWarehouseEmployeePointDefinition> IdlePoints { get; } =
        new[]
        {
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_0", 2.07f, FishWarehouseInteriorRoomDefinition.FloorY, 10.93f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_1", 2.16f, FishWarehouseInteriorRoomDefinition.FloorY, 11.72f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_2", 2.24f, FishWarehouseInteriorRoomDefinition.FloorY, 12.52f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_3", 2.33f, FishWarehouseInteriorRoomDefinition.FloorY, 13.31f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_4", 2.41f, FishWarehouseInteriorRoomDefinition.FloorY, 14.11f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_5", 1.28f, FishWarehouseInteriorRoomDefinition.FloorY, 11.01f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_6", 1.36f, FishWarehouseInteriorRoomDefinition.FloorY, 11.81f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_7", 1.45f, FishWarehouseInteriorRoomDefinition.FloorY, 12.60f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_8", 1.53f, FishWarehouseInteriorRoomDefinition.FloorY, 13.40f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_9", 1.62f, FishWarehouseInteriorRoomDefinition.FloorY, 14.19f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_10", 0.48f, FishWarehouseInteriorRoomDefinition.FloorY, 11.10f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_11", 0.57f, FishWarehouseInteriorRoomDefinition.FloorY, 11.89f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_12", 0.65f, FishWarehouseInteriorRoomDefinition.FloorY, 12.69f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_13", 0.74f, FishWarehouseInteriorRoomDefinition.FloorY, 13.48f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_14", 0.82f, FishWarehouseInteriorRoomDefinition.FloorY, 14.28f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_15", -0.31f, FishWarehouseInteriorRoomDefinition.FloorY, 11.18f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_16", -0.23f, FishWarehouseInteriorRoomDefinition.FloorY, 11.98f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_17", -0.14f, FishWarehouseInteriorRoomDefinition.FloorY, 12.77f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_18", -0.06f, FishWarehouseInteriorRoomDefinition.FloorY, 13.57f),
            new FishWarehouseEmployeePointDefinition("OC_FishWarehouse_EmployeeIdle_19", 0.02f, FishWarehouseInteriorRoomDefinition.FloorY, 14.36f)
        };

    public static bool IsInsideRoom(FishWarehouseEmployeePointDefinition point) =>
        MathF.Abs(point.X) <= FishWarehouseInteriorRoomDefinition.HalfWidth - WallMargin &&
        MathF.Abs(point.Z) <= FishWarehouseInteriorRoomDefinition.HalfDepth - WallMargin;

    public static bool IsValidEmployeeIndex(int index) => index >= 0 && index < DefinedIdlePointCount;

    public static bool HasValidLayout() => HasValidLayout(IdlePoints);

    public static bool HasValidLayout(IEnumerable<FishWarehouseEmployeePointDefinition?>? points) =>
        IsInsideRoom(NpcSpawnPoint) &&
        FishWarehousePropertyBoundsDefinition.Contains(NpcSpawnPoint.X, NpcSpawnPoint.Y, NpcSpawnPoint.Z) &&
        FishWarehouseEmployeePointLayoutValidator.Validate(
                DefinedIdlePointCount,
                points,
                FishWarehouseEmployeePointLayoutRules.Default)
            .Succeeded;
}
