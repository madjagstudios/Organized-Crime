namespace OrganizedCrime.Model;

/// <summary>
/// The first functional warehouse fixture. The item id is a vanilla employee
/// locker, not a synthesized EmployeeHome component.
/// </summary>
public sealed record FishWarehouseEmployeeHomeDefinition(
    string ItemId,
    int GridX,
    int GridY,
    int Rotation,
    string PlacementGuid)
{
    public static FishWarehouseEmployeeHomeDefinition Default { get; } = new(
        ItemId: "locker",
        GridX: 0,
        GridY: 0,
        Rotation: 0,
        PlacementGuid: "4ce5b96a-39f5-4ac3-9a05-9fca7a6e8c51");
}
