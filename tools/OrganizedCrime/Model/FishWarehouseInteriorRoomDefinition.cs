namespace OrganizedCrime.Model;

public enum FishWarehouseInteriorDoorSide
{
    West,
    East,
    South,
    North
}

/// <summary>
/// Fixed authored-room dimensions derived from the live Fish Warehouse source
/// census. Coordinates are local to the fishwarehouse anchor.
/// </summary>
public static class FishWarehouseInteriorRoomDefinition
{
    public const float HalfWidth = 11.6f;
    public const float HalfDepth = 6.9f;
    public const float FloorY = 0f;
    public const float CeilingY = 5.8f;
    public const float DoorWidth = 2.2f;
    public const float DoorHeight = 3f;
    public const float WallInset = 0.6f;

    public static FishWarehouseInteriorDoorSide ResolveDoorSide(float localX, float localZ)
    {
        float west = MathF.Abs(localX + HalfWidth);
        float east = MathF.Abs(HalfWidth - localX);
        float south = MathF.Abs(localZ + HalfDepth);
        float north = MathF.Abs(HalfDepth - localZ);

        float nearest = MathF.Min(MathF.Min(west, east), MathF.Min(south, north));
        if (nearest == west)
            return FishWarehouseInteriorDoorSide.West;
        if (nearest == east)
            return FishWarehouseInteriorDoorSide.East;
        if (nearest == south)
            return FishWarehouseInteriorDoorSide.South;

        return FishWarehouseInteriorDoorSide.North;
    }

    public static bool TryPlanRoomWalls(
        float personnelLocalX,
        float personnelLocalZ,
        FishWarehouseWallOpening garageOpening,
        out IReadOnlyList<FishWarehouseWallOpening>? openings,
        out IReadOnlyList<FishWarehouseWallPlan>? wallPlans)
    {
        openings = null;
        wallPlans = null;

        if (!FishWarehouseWallOpeningPlanner.TryCreatePersonnelOpening(
                personnelLocalX,
                personnelLocalZ,
                out FishWarehouseWallOpening? personnelOpening) ||
            personnelOpening is null)
        {
            return false;
        }

        FishWarehouseWallOpening[] allOpenings = { personnelOpening, garageOpening };
        var plans = new List<FishWarehouseWallPlan>();
        foreach (FishWarehouseInteriorDoorSide wall in Enum.GetValues<FishWarehouseInteriorDoorSide>())
        {
            FishWarehouseWallOpening[] openingsForWall = allOpenings
                .Where(opening => opening.Wall == wall)
                .ToArray();
            if (!FishWarehouseWallOpeningPlanner.TryPlan(openingsForWall, out FishWarehouseWallPlan? plan, wall) ||
                plan is null)
            {
                return false;
            }

            plans.Add(plan);
        }

        openings = Array.AsReadOnly(allOpenings);
        wallPlans = plans.AsReadOnly();
        return true;
    }

}
