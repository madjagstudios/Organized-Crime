namespace OrganizedCrime.Model;

public readonly record struct FishWarehouseNativeNavigationVector(float X, float Y, float Z);

public readonly record struct FishWarehouseNativeNavigationSurface(
    FishWarehouseNativeNavigationVector Center,
    FishWarehouseNativeNavigationVector Size,
    int Area,
    bool GenerateLinks);

public readonly record struct FishWarehouseNativeNavigationBox(
    FishWarehouseNativeNavigationVector Center,
    FishWarehouseNativeNavigationVector Size);

public readonly record struct FishWarehouseNativeNavigationLink(
    FishWarehouseNativeNavigationVector Start,
    FishWarehouseNativeNavigationVector NominalExteriorQueryPoint,
    float Width,
    float SideInset,
    bool Bidirectional);

public sealed record FishWarehouseNativeNavigationPlan(
    FishWarehouseNativeNavigationSurface Surface,
    FishWarehouseNativeNavigationBox BakeBounds,
    FishWarehouseNativeNavigationLink Link,
    FishWarehouseNativeNavigationVector DoorwayInterior,
    float SampleRadius);

public readonly record struct FishWarehouseNativeGraphProbe(
    bool ExteriorSampled,
    bool InteriorSampled,
    bool PathComplete);

public enum FishWarehouseNativeGraphDecision
{
    Fail,
    AddSurfaceAndLink,
    AddLinkOnly,
    ReuseExistingPath
}

public static class FishWarehouseNativeGraphDecisionDefinition
{
    public static FishWarehouseNativeGraphDecision Decide(FishWarehouseNativeGraphProbe probe) =>
        !probe.ExteriorSampled
            ? FishWarehouseNativeGraphDecision.Fail
            : !probe.InteriorSampled
                ? FishWarehouseNativeGraphDecision.AddSurfaceAndLink
                : !probe.PathComplete
                    ? FishWarehouseNativeGraphDecision.AddLinkOnly
                    : FishWarehouseNativeGraphDecision.ReuseExistingPath;
}

public static class FishWarehouseNativeNavigationDefinition
{
    public const float SurfaceThickness = 0.10f;
    public const float LinkSideInset = 0.35f;
    public const float MinimumLinkWidth = 1.20f;
    public const float BakeBoundsPadding = 0.50f;
    public const float SampleRadius = 1.00f;

    private const float TransitionClearance = 0.30f;
    private const float ExteriorSampleClearance = 2.20f;
    private const float ProjectionTolerance = 0.001f;
    private const float MaximumTransitionDepth = 1.00f;
    // Matches FishWarehouseGarageOpeningDefinition.MinimumLintel; that producer constant is private.
    private const float MinimumProjectedLintel = 0.30f;

    public static FishWarehouseNativeNavigationPlan Create(
        FishWarehouseGarageOpeningProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ValidateProjection(projection);

        float surfaceMinimumZ = -FishWarehouseInteriorRoomDefinition.HalfDepth;
        float surfaceMaximumZ = projection.TransitionPrism.MaximumZ;
        float surfaceSizeZ = surfaceMaximumZ - surfaceMinimumZ;
        float roomWidth = FishWarehouseInteriorRoomDefinition.HalfWidth * 2f;

        var surface = new FishWarehouseNativeNavigationSurface(
            new FishWarehouseNativeNavigationVector(
                0f,
                FishWarehouseInteriorRoomDefinition.FloorY - SurfaceThickness / 2f,
                (surfaceMinimumZ + surfaceMaximumZ) / 2f),
            new FishWarehouseNativeNavigationVector(roomWidth, SurfaceThickness, surfaceSizeZ),
            Area: 0,
            GenerateLinks: false);

        float bakeMinimumY = FishWarehouseInteriorRoomDefinition.FloorY - BakeBoundsPadding;
        float bakeMaximumY = FishWarehouseInteriorRoomDefinition.CeilingY + BakeBoundsPadding;
        var bakeBounds = new FishWarehouseNativeNavigationBox(
            new FishWarehouseNativeNavigationVector(
                0f,
                (bakeMinimumY + bakeMaximumY) / 2f,
                (surfaceMinimumZ + surfaceMaximumZ) / 2f),
            new FishWarehouseNativeNavigationVector(
                roomWidth + 2f * BakeBoundsPadding,
                bakeMaximumY - bakeMinimumY,
                surfaceSizeZ + 2f * BakeBoundsPadding));

        float linkWidth = projection.NavigationBounds.Width - 2f * LinkSideInset;
        if (linkWidth < MinimumLinkWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projection),
                linkWidth,
                $"The inset native garage link must be at least {MinimumLinkWidth:0.00}m wide.");
        }

        float apertureCenterX = projection.Opening.Center;
        var doorwayInterior = new FishWarehouseNativeNavigationVector(
            apertureCenterX,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MinimumZ - TransitionClearance);
        var linkStart = new FishWarehouseNativeNavigationVector(
            apertureCenterX,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MaximumZ - TransitionClearance);
        var nominalExteriorQueryPoint = new FishWarehouseNativeNavigationVector(
            apertureCenterX,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MaximumZ + ExteriorSampleClearance);

        return new FishWarehouseNativeNavigationPlan(
            surface,
            bakeBounds,
            new FishWarehouseNativeNavigationLink(
                linkStart,
                nominalExteriorQueryPoint,
                linkWidth,
                LinkSideInset,
                Bidirectional: true),
            doorwayInterior,
            SampleRadius);
    }

    private static void ValidateProjection(FishWarehouseGarageOpeningProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection.Opening);
        ArgumentNullException.ThrowIfNull(projection.TransitionPrism);
        ArgumentNullException.ThrowIfNull(projection.NavigationBounds);

        if (projection.Opening.Wall != FishWarehouseInteriorDoorSide.North)
            throw new ArgumentException("The native garage opening must be on the north wall.", nameof(projection));

        if (!IsFinite(
                projection.RawMinimumX,
                projection.RawMaximumX,
                projection.Lintel,
                projection.Opening.HorizontalMinimum,
                projection.Opening.HorizontalMaximum,
                projection.Opening.Bottom,
                projection.Opening.Top,
                projection.TransitionPrism.MinimumX,
                projection.TransitionPrism.MaximumX,
                projection.TransitionPrism.MinimumY,
                projection.TransitionPrism.MaximumY,
                projection.TransitionPrism.MinimumZ,
                projection.TransitionPrism.MaximumZ,
                projection.NavigationBounds.MinimumX,
                projection.NavigationBounds.MaximumX))
        {
            throw new ArgumentException("The native garage projection must contain only finite coordinates.", nameof(projection));
        }

        if (projection.Opening.HorizontalMinimum >= projection.Opening.HorizontalMaximum ||
            projection.Opening.Bottom >= projection.Opening.Top ||
            projection.RawMinimumX >= projection.RawMaximumX ||
            projection.RawMinimumX > projection.Opening.HorizontalMinimum + ProjectionTolerance ||
            projection.RawMaximumX < projection.Opening.HorizontalMaximum - ProjectionTolerance ||
            projection.Opening.HorizontalMinimum < -FishWarehouseInteriorRoomDefinition.HalfWidth ||
            projection.Opening.HorizontalMaximum > FishWarehouseInteriorRoomDefinition.HalfWidth ||
            projection.Opening.Bottom < FishWarehouseInteriorRoomDefinition.FloorY ||
            projection.Opening.Top > FishWarehouseInteriorRoomDefinition.CeilingY ||
            projection.Lintel < MinimumProjectedLintel ||
            MathF.Abs(projection.Lintel -
                (FishWarehouseInteriorRoomDefinition.CeilingY - projection.Opening.Top)) > ProjectionTolerance ||
            MathF.Abs(projection.TransitionPrism.MinimumX - projection.Opening.HorizontalMinimum) > ProjectionTolerance ||
            MathF.Abs(projection.TransitionPrism.MaximumX - projection.Opening.HorizontalMaximum) > ProjectionTolerance ||
            MathF.Abs(projection.TransitionPrism.MinimumY - projection.Opening.Bottom) > ProjectionTolerance ||
            MathF.Abs(projection.TransitionPrism.MaximumY - projection.Opening.Top) > ProjectionTolerance ||
            projection.TransitionPrism.MinimumX >= projection.TransitionPrism.MaximumX ||
            projection.TransitionPrism.MinimumY >= projection.TransitionPrism.MaximumY ||
            projection.TransitionPrism.MinimumZ >= projection.TransitionPrism.MaximumZ ||
            MathF.Abs(projection.TransitionPrism.MinimumZ - FishWarehouseInteriorRoomDefinition.HalfDepth) > ProjectionTolerance ||
            projection.TransitionPrism.Depth > MaximumTransitionDepth ||
            projection.NavigationBounds.MinimumX >= projection.NavigationBounds.MaximumX ||
            projection.NavigationBounds.Width < 2f * LinkSideInset ||
            projection.NavigationBounds.Width > projection.Opening.Width)
        {
            throw new ArgumentException("The native garage projection geometry is invalid.", nameof(projection));
        }

        float apertureCenterX = projection.Opening.Center;
        float doorwayInteriorZ = projection.TransitionPrism.MinimumZ - TransitionClearance;
        float linkStartZ = projection.TransitionPrism.MaximumZ - TransitionClearance;
        if (apertureCenterX <= -FishWarehouseInteriorRoomDefinition.HalfWidth ||
            apertureCenterX >= FishWarehouseInteriorRoomDefinition.HalfWidth ||
            doorwayInteriorZ <= -FishWarehouseInteriorRoomDefinition.HalfDepth ||
            doorwayInteriorZ >= FishWarehouseInteriorRoomDefinition.HalfDepth ||
            linkStartZ <= projection.TransitionPrism.MinimumZ ||
            linkStartZ >= projection.TransitionPrism.MaximumZ)
        {
            throw new ArgumentException("The native garage projection cannot produce interior validation points.", nameof(projection));
        }
    }

    private static bool IsFinite(params float[] values) => values.All(float.IsFinite);
}
