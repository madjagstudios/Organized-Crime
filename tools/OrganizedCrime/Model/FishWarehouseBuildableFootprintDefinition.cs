namespace OrganizedCrime.Model;

public readonly record struct FishWarehouseFootprintBounds(
    float MinimumX,
    float MaximumX,
    float MinimumZ,
    float MaximumZ)
{
    public bool IsValid =>
        float.IsFinite(MinimumX) &&
        float.IsFinite(MaximumX) &&
        float.IsFinite(MinimumZ) &&
        float.IsFinite(MaximumZ) &&
        MinimumX < MaximumX &&
        MinimumZ < MaximumZ;

    public bool Contains(float x, float z) =>
        IsValid &&
        float.IsFinite(x) &&
        float.IsFinite(z) &&
        x >= MinimumX &&
        x <= MaximumX &&
        z >= MinimumZ &&
        z <= MaximumZ;

    public FishWarehouseFootprintBounds Inset(float distance) =>
        new(
            MinimumX + distance,
            MaximumX - distance,
            MinimumZ + distance,
            MaximumZ - distance);
}

public readonly record struct FishWarehouseGridCellCoordinate(int X, int Y);

/// <summary>
/// The Fish Warehouse floor envelope derived from OC's own interior room walls.
/// Coordinates are local to the runtime Property root, created at the authored
/// Fish Warehouse anchor's world position and rotation.
/// </summary>
public sealed class FishWarehouseBuildableFootprintDefinition
{
    // Keep cells just off the walls so wall-adjacent footprints stay placeable.
    public const float WallAllowance = 0.05f;
    // Fixed Property-local origin; X=0.25 splits the X leftover evenly between the walls.
    public const float GridOriginX = 0.25f;
    public const float GridOriginZ = 0f;

    private FishWarehouseBuildableFootprintDefinition(
        FishWarehouseFootprintBounds measuredBounds,
        float gridCellSize,
        int clearanceCells)
    {
        MeasuredBounds = measuredBounds;
        GridCellSize = gridCellSize;
        ClearanceCells = clearanceCells;
        PlacementBounds = measuredBounds.Inset(gridCellSize * clearanceCells);
    }

    public FishWarehouseFootprintBounds MeasuredBounds { get; }
    public FishWarehouseFootprintBounds PlacementBounds { get; }
    public float GridCellSize { get; }
    public int ClearanceCells { get; }

    public FishWarehousePoint GetPropertyLocalCellCenter(
        FishWarehouseGridCellCoordinate coordinate,
        float planeHeight)
    {
        if (!float.IsFinite(planeHeight))
            throw new ArgumentOutOfRangeException(nameof(planeHeight), "The Grid plane height must be finite.");

        return new FishWarehousePoint(
            GridOriginX + (coordinate.X * GridCellSize),
            planeHeight,
            GridOriginZ + (coordinate.Y * GridCellSize));
    }

    public static FishWarehouseBuildableFootprintDefinition CreateMeasured(float gridCellSize)
    {
        if (!float.IsFinite(gridCellSize) || gridCellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(gridCellSize), "The native grid cell size must be finite and positive.");

        // Bounds follow OC's own interior room so the walls stop rejecting nearby items.
        var measuredBounds = new FishWarehouseFootprintBounds(
            MinimumX: -(FishWarehouseInteriorRoomDefinition.HalfWidth - WallAllowance),
            MaximumX: FishWarehouseInteriorRoomDefinition.HalfWidth - WallAllowance,
            MinimumZ: -(FishWarehouseInteriorRoomDefinition.HalfDepth - WallAllowance),
            MaximumZ: FishWarehouseInteriorRoomDefinition.HalfDepth - WallAllowance);
        return new FishWarehouseBuildableFootprintDefinition(
            measuredBounds,
            gridCellSize,
            clearanceCells: 0);
    }

    public bool ContainsPlacementPoint(FishWarehousePoint point) =>
        PlacementBounds.Contains(point.X, point.Z);

    public bool ContainsPlacementCell(FishWarehousePoint center)
    {
        if (!ContainsPlacementPoint(center))
            return false;

        float halfCell = GridCellSize / 2f;
        return MeasuredBounds.Contains(center.X - halfCell, center.Z - halfCell) &&
               MeasuredBounds.Contains(center.X + halfCell, center.Z + halfCell);
    }

    public static bool TryMapLocalPointToCoordinate(
        FishWarehousePoint point,
        FishWarehousePoint referenceCenter,
        float gridCellSize,
        out FishWarehouseGridCellCoordinate coordinate)
    {
        coordinate = default;
        if (!float.IsFinite(point.X) ||
            !float.IsFinite(point.Z) ||
            !float.IsFinite(referenceCenter.X) ||
            !float.IsFinite(referenceCenter.Z) ||
            !float.IsFinite(gridCellSize) ||
            gridCellSize <= 0f)
        {
            return false;
        }

        coordinate = new FishWarehouseGridCellCoordinate(
            RoundToNearestInt((point.X - referenceCenter.X) / gridCellSize),
            RoundToNearestInt((point.Z - referenceCenter.Z) / gridCellSize));
        return true;
    }

    public static bool ContainsCoordinate(
        IReadOnlySet<FishWarehouseGridCellCoordinate> allowedCoordinates,
        FishWarehouseGridCellCoordinate coordinate) =>
        allowedCoordinates is not null && allowedCoordinates.Contains(coordinate);

    /// <summary>
    /// Returns native integer coordinates whose cell centers are measured from the supplied
    /// reference cell and whose complete cells fit inside the room-derived envelope.
    /// Fish Warehouse layout uses its fixed Property-local origin as the reference cell.
    /// </summary>
    public IReadOnlyList<FishWarehouseGridCellCoordinate> GetAllowedCellCoordinates(
        int referenceCoordinateX,
        int referenceCoordinateY,
        float referenceCenterX,
        float referenceCenterZ)
    {
        if (!float.IsFinite(referenceCenterX) || !float.IsFinite(referenceCenterZ))
            throw new ArgumentOutOfRangeException(nameof(referenceCenterX), "The reference cell center must be finite.");

        var halfCell = GridCellSize / 2f;
        var minimumX = ToCoordinateFloor(MeasuredBounds.MinimumX - halfCell, referenceCenterX, referenceCoordinateX);
        var maximumX = ToCoordinateCeiling(MeasuredBounds.MaximumX + halfCell, referenceCenterX, referenceCoordinateX);
        var minimumY = ToCoordinateFloor(MeasuredBounds.MinimumZ - halfCell, referenceCenterZ, referenceCoordinateY);
        var maximumY = ToCoordinateCeiling(MeasuredBounds.MaximumZ + halfCell, referenceCenterZ, referenceCoordinateY);

        var coordinates = new List<FishWarehouseGridCellCoordinate>();
        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                var center = new FishWarehousePoint(
                    referenceCenterX + ((x - referenceCoordinateX) * GridCellSize),
                    0f,
                    referenceCenterZ + ((y - referenceCoordinateY) * GridCellSize));
                if (ContainsPlacementCell(center))
                    coordinates.Add(new FishWarehouseGridCellCoordinate(x, y));
            }
        }

        return coordinates;
    }

    private int ToCoordinateFloor(float boundary, float referenceCenter, int referenceCoordinate) =>
        checked((int)MathF.Floor(((boundary - referenceCenter) / GridCellSize) + referenceCoordinate));

    private int ToCoordinateCeiling(float boundary, float referenceCenter, int referenceCoordinate) =>
        checked((int)MathF.Ceiling(((boundary - referenceCenter) / GridCellSize) + referenceCoordinate));

    private static int RoundToNearestInt(float value) =>
        checked((int)(value >= 0f
            ? MathF.Floor(value + 0.5f)
            : MathF.Ceiling(value - 0.5f)));
}
