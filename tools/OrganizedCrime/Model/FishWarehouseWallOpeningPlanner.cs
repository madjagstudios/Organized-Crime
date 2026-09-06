namespace OrganizedCrime.Model;

public sealed record FishWarehouseWallOpening(
    FishWarehouseInteriorDoorSide Wall,
    float HorizontalMinimum,
    float HorizontalMaximum,
    float Bottom,
    float Top,
    bool IsCornerTerminatedAtMinimum = false,
    bool IsCornerTerminatedAtMaximum = false,
    string? SemanticId = null)
{
    public float Width => HorizontalMaximum - HorizontalMinimum;
    public float Height => Top - Bottom;
    public float Center => (HorizontalMinimum + HorizontalMaximum) / 2f;
}

public sealed record FishWarehouseWallSegment(
    FishWarehouseInteriorDoorSide Wall,
    float HorizontalMinimum,
    float HorizontalMaximum,
    float Bottom,
    float Top)
{
    public float Width => HorizontalMaximum - HorizontalMinimum;
    public float Height => Top - Bottom;
}

public sealed record FishWarehouseWallPlan(
    FishWarehouseInteriorDoorSide Wall,
    IReadOnlyList<FishWarehouseWallSegment> Segments);

public static class FishWarehouseWallOpeningPlanner
{
    public const float Epsilon = 0.001f;
    private const float MinimumResidualWallMargin = 0.50f;
    private const float MinimumClippedPersonnelWidth = 1.20f;

    public static bool TryCreatePersonnelOpening(
        float localX,
        float localZ,
        out FishWarehouseWallOpening? opening)
    {
        opening = null;
        if (!float.IsFinite(localX) || !float.IsFinite(localZ))
            return false;

        FishWarehouseInteriorDoorSide wall = FishWarehouseInteriorRoomDefinition.ResolveDoorSide(localX, localZ);
        float center = wall is FishWarehouseInteriorDoorSide.West or FishWarehouseInteriorDoorSide.East
            ? localZ
            : localX;
        (float wallMinimum, float wallMaximum) = GetHorizontalBounds(wall);
        float horizontalMinimum = MathF.Max(
            wallMinimum,
            center - FishWarehouseInteriorRoomDefinition.DoorWidth / 2f);
        float horizontalMaximum = MathF.Min(
            wallMaximum,
            center + FishWarehouseInteriorRoomDefinition.DoorWidth / 2f);
        if (horizontalMaximum - horizontalMinimum < MinimumClippedPersonnelWidth)
            return false;

        opening = new FishWarehouseWallOpening(
            wall,
            horizontalMinimum,
            horizontalMaximum,
            FishWarehouseInteriorRoomDefinition.FloorY,
            FishWarehouseInteriorRoomDefinition.DoorHeight,
            IsCornerTerminatedAtMinimum: MathF.Abs(horizontalMinimum - wallMinimum) <= Epsilon,
            IsCornerTerminatedAtMaximum: MathF.Abs(horizontalMaximum - wallMaximum) <= Epsilon,
            SemanticId: "personnel");
        return true;
    }

    public static bool TryPlan(
        IEnumerable<FishWarehouseWallOpening>? openings,
        out FishWarehouseWallPlan? plan)
    {
        return TryPlan(openings, out plan, null);
    }

    public static bool TryPlan(
        IEnumerable<FishWarehouseWallOpening>? openings,
        out FishWarehouseWallPlan? plan,
        FishWarehouseInteriorDoorSide wall)
    {
        return TryPlan(openings, out plan, (FishWarehouseInteriorDoorSide?)wall);
    }

    private static bool TryPlan(
        IEnumerable<FishWarehouseWallOpening>? openings,
        out FishWarehouseWallPlan? plan,
        FishWarehouseInteriorDoorSide? requestedWall)
    {
        plan = null;
        if (openings is null)
            return false;

        FishWarehouseWallOpening[] source = openings.ToArray();
        FishWarehouseInteriorDoorSide wall = requestedWall ??
            (source.Length > 0 ? source[0].Wall : FishWarehouseInteriorDoorSide.North);
        if (source.Any(opening => opening.Wall != wall))
            return false;

        (float minimum, float maximum) = GetHorizontalBounds(wall);
        FishWarehouseWallOpening[] normalized = new FishWarehouseWallOpening[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            FishWarehouseWallOpening opening = source[index];
            if (!TryNormalizeOpening(opening, minimum, maximum, out FishWarehouseWallOpening normalizedOpening))
                return false;

            normalized[index] = normalizedOpening;
        }

        Array.Sort(normalized, static (left, right) =>
        {
            int minimumComparison = left.HorizontalMinimum.CompareTo(right.HorizontalMinimum);
            return minimumComparison != 0
                ? minimumComparison
                : left.HorizontalMaximum.CompareTo(right.HorizontalMaximum);
        });

        for (int index = 1; index < normalized.Length; index++)
        {
            float residualWidth = normalized[index].HorizontalMinimum -
                normalized[index - 1].HorizontalMaximum;
            if (residualWidth < 0f ||
                residualWidth > 0f && residualWidth < MinimumResidualWallMargin)
            {
                return false;
            }
        }

        var segments = new List<FishWarehouseWallSegment>();
        if (normalized.Length == 0)
        {
            segments.Add(new FishWarehouseWallSegment(
                wall,
                minimum,
                maximum,
                FishWarehouseInteriorRoomDefinition.FloorY,
                FishWarehouseInteriorRoomDefinition.CeilingY));
        }
        else
        {
            float cursor = minimum;
            foreach (FishWarehouseWallOpening opening in normalized)
            {
                AddSegmentIfMaterial(
                    segments,
                    wall,
                    cursor,
                    opening.HorizontalMinimum,
                    FishWarehouseInteriorRoomDefinition.FloorY,
                    FishWarehouseInteriorRoomDefinition.CeilingY);
                AddSegmentIfMaterial(
                    segments,
                    wall,
                    opening.HorizontalMinimum,
                    opening.HorizontalMaximum,
                    FishWarehouseInteriorRoomDefinition.FloorY,
                    opening.Bottom);
                AddSegmentIfMaterial(
                    segments,
                    wall,
                    opening.HorizontalMinimum,
                    opening.HorizontalMaximum,
                    opening.Top,
                    FishWarehouseInteriorRoomDefinition.CeilingY);
                cursor = opening.HorizontalMaximum;
            }

            AddSegmentIfMaterial(
                segments,
                wall,
                cursor,
                maximum,
                FishWarehouseInteriorRoomDefinition.FloorY,
                FishWarehouseInteriorRoomDefinition.CeilingY);
        }

        if (segments.Any(segment => segment.Width <= Epsilon || segment.Height <= Epsilon))
            return false;

        plan = new FishWarehouseWallPlan(wall, segments.AsReadOnly());
        return true;
    }

    private static bool TryNormalizeOpening(
        FishWarehouseWallOpening opening,
        float minimum,
        float maximum,
        out FishWarehouseWallOpening normalized)
    {
        normalized = null!;
        if (!float.IsFinite(opening.HorizontalMinimum) ||
            !float.IsFinite(opening.HorizontalMaximum) ||
            !float.IsFinite(opening.Bottom) ||
            !float.IsFinite(opening.Top))
        {
            return false;
        }

        float normalizedMinimum = SnapToEdge(opening.HorizontalMinimum, minimum);
        float normalizedMaximum = SnapToEdge(opening.HorizontalMaximum, maximum);
        if (normalizedMinimum < minimum || normalizedMaximum > maximum ||
            normalizedMinimum >= normalizedMaximum ||
            normalizedMaximum - normalizedMinimum <= Epsilon ||
            opening.Bottom < FishWarehouseInteriorRoomDefinition.FloorY ||
            opening.Top > FishWarehouseInteriorRoomDefinition.CeilingY ||
            opening.Bottom >= opening.Top ||
            opening.Top - opening.Bottom <= Epsilon)
        {
            return false;
        }

        float leftMargin = normalizedMinimum - minimum;
        float rightMargin = maximum - normalizedMaximum;
        bool atMinimum = MathF.Abs(normalizedMinimum - minimum) <= Epsilon;
        bool atMaximum = MathF.Abs(normalizedMaximum - maximum) <= Epsilon;
        if (atMinimum && !opening.IsCornerTerminatedAtMinimum ||
            atMaximum && !opening.IsCornerTerminatedAtMaximum)
        {
            return false;
        }

        if (opening.IsCornerTerminatedAtMinimum && !atMinimum ||
            opening.IsCornerTerminatedAtMaximum && !atMaximum)
        {
            return false;
        }

        if (atMinimum)
        {
            if (rightMargin < MinimumResidualWallMargin)
                return false;
        }
        else if (atMaximum)
        {
            if (leftMargin < MinimumResidualWallMargin)
                return false;
        }
        else if (leftMargin < MinimumResidualWallMargin || rightMargin < MinimumResidualWallMargin)
        {
            return false;
        }

        normalized = opening with
        {
            HorizontalMinimum = normalizedMinimum,
            HorizontalMaximum = normalizedMaximum,
            IsCornerTerminatedAtMinimum = atMinimum,
            IsCornerTerminatedAtMaximum = atMaximum
        };
        return true;
    }

    private static void AddSegmentIfMaterial(
        ICollection<FishWarehouseWallSegment> segments,
        FishWarehouseInteriorDoorSide wall,
        float horizontalMinimum,
        float horizontalMaximum,
        float bottom,
        float top)
    {
        if (horizontalMaximum - horizontalMinimum <= Epsilon || top - bottom <= Epsilon)
            return;

        segments.Add(new FishWarehouseWallSegment(
            wall,
            horizontalMinimum,
            horizontalMaximum,
            bottom,
            top));
    }

    private static float SnapToEdge(float value, float edge) =>
        MathF.Abs(value - edge) <= Epsilon ? edge : value;

    private static (float Minimum, float Maximum) GetHorizontalBounds(
        FishWarehouseInteriorDoorSide wall) =>
        wall is FishWarehouseInteriorDoorSide.West or FishWarehouseInteriorDoorSide.East
            ? (-FishWarehouseInteriorRoomDefinition.HalfDepth, FishWarehouseInteriorRoomDefinition.HalfDepth)
            : (-FishWarehouseInteriorRoomDefinition.HalfWidth, FishWarehouseInteriorRoomDefinition.HalfWidth);
}
