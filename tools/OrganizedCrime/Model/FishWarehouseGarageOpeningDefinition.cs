namespace OrganizedCrime.Model;

public readonly record struct FishWarehouseAnchorLocalBounds(
    float MinimumX,
    float MaximumX,
    float MinimumY,
    float MaximumY,
    float MinimumZ,
    float MaximumZ)
{
    public bool IsValid =>
        float.IsFinite(MinimumX) && float.IsFinite(MaximumX) &&
        float.IsFinite(MinimumY) && float.IsFinite(MaximumY) &&
        float.IsFinite(MinimumZ) && float.IsFinite(MaximumZ) &&
        MinimumX <= MaximumX && MinimumY <= MaximumY && MinimumZ <= MaximumZ;

    public bool Intersects(FishWarehouseAnchorLocalBounds other) =>
        IsValid && other.IsValid &&
        MinimumX < other.MaximumX && MaximumX > other.MinimumX &&
        MinimumY < other.MaximumY && MaximumY > other.MinimumY &&
        MinimumZ < other.MaximumZ && MaximumZ > other.MinimumZ;
}

public sealed record FishWarehouseGarageOpeningCandidate(
    string RootPath,
    string PanelIdentity,
    FishWarehouseAnchorLocalBounds PanelBounds,
    FishWarehousePhysicalPanelPlane PhysicalPanelPlane,
    IReadOnlyList<FishWarehouseAnchorLocalBounds> SurvivingNonTargetColliders,
    bool HasFloorSupport);

public sealed record FishWarehouseNavigationHorizontalBounds(float MinimumX, float MaximumX)
{
    public float Width => MaximumX - MinimumX;
}

public sealed record FishWarehouseTransitionPrism(
    float MinimumX,
    float MaximumX,
    float MinimumY,
    float MaximumY,
    float MinimumZ,
    float MaximumZ)
{
    public float Depth => MaximumZ - MinimumZ;
}

public sealed record FishWarehouseGarageOpeningProjection(
    FishWarehouseWallOpening Opening,
    float RawMinimumX,
    float RawMaximumX,
    float Lintel,
    FishWarehouseTransitionPrism TransitionPrism,
    FishWarehouseNavigationHorizontalBounds NavigationBounds)
{
    public float NavigationCenterX =>
        (NavigationBounds.MinimumX + NavigationBounds.MaximumX) / 2f;
}

public static class FishWarehouseGarageOpeningDefinition
{
    public const string ApprovedGarageDoorRootPath =
        "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse/GarageDoor (1)";
    public const string ApprovedAlternateRootPath =
        "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse/GarageDoor_Other";

    private const string ApprovedGarageDoorPanelIdentity = "GarageDoor (1)";
    private const string ApprovedAlternatePanelIdentity = "GarageDoor_Other";
    private const float PanelClearance = 0.10f;
    private const float BoundarySnapTolerance = 0.01f;
    private const float MinimumLintel = 0.30f;
    private const float MinimumResidualWallMargin = 0.50f;

    public static bool TryProject(
        IEnumerable<FishWarehouseGarageOpeningCandidate>? candidates,
        out FishWarehouseGarageOpeningProjection? projection)
    {
        projection = null;
        if (candidates is null)
            return false;

        FishWarehouseGarageOpeningCandidate[] matches = candidates.ToArray();
        if (matches.Length != 2)
            return false;

        FishWarehouseGarageOpeningCandidate? primary = null;
        FishWarehouseGarageOpeningCandidate? alternate = null;
        foreach (FishWarehouseGarageOpeningCandidate candidate in matches)
        {
            if (!candidate.PanelBounds.IsValid ||
                candidate.SurvivingNonTargetColliders is null ||
                !FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(
                    candidate.PanelBounds,
                    candidate.PhysicalPanelPlane) ||
                !candidate.HasFloorSupport)
            {
                return false;
            }

            if (candidate.RootPath == ApprovedGarageDoorRootPath &&
                candidate.PanelIdentity == ApprovedGarageDoorPanelIdentity)
            {
                if (primary is not null)
                    return false;

                primary = candidate;
                continue;
            }

            if (candidate.RootPath == ApprovedAlternateRootPath &&
                candidate.PanelIdentity == ApprovedAlternatePanelIdentity)
            {
                if (alternate is not null)
                    return false;

                alternate = candidate;
                continue;
            }

            return false;
        }

        if (primary is null || alternate is null ||
            !FishWarehouseGarageOpeningGeometryDefinition.AreCoincidentPhysicalFaces(
                primary.PanelBounds,
                primary.PhysicalPanelPlane,
                alternate.PanelBounds,
                alternate.PhysicalPanelPlane))
        return false;

        FishWarehouseAnchorLocalBounds panel = primary.PanelBounds;
        float rawMinimumX = panel.MinimumX - PanelClearance;
        float rawMaximumX = panel.MaximumX + PanelClearance;

        if (!TryClampHorizontalBounds(rawMinimumX, rawMaximumX, out float minimumX, out float maximumX))
            return false;

        float bottom = MathF.Max(FishWarehouseInteriorRoomDefinition.FloorY, panel.MinimumY - PanelClearance);
        float top = panel.MaximumY + PanelClearance;
        float lintel = FishWarehouseInteriorRoomDefinition.CeilingY - top;
        if (bottom > FishWarehouseInteriorRoomDefinition.FloorY ||
            bottom >= top ||
            top > FishWarehouseInteriorRoomDefinition.CeilingY ||
            lintel < MinimumLintel)
        {
            return false;
        }

        float panelPlaneZ = primary.PhysicalPanelPlane.AnchorLocalCenter.Z;
        float transitionDepth = panelPlaneZ - FishWarehouseInteriorRoomDefinition.HalfDepth;
        if (transitionDepth <= 0f || transitionDepth > 1f)
        {
            return false;
        }

        float leftMargin = minimumX + FishWarehouseInteriorRoomDefinition.HalfWidth;
        float rightMargin = FishWarehouseInteriorRoomDefinition.HalfWidth - maximumX;
        bool atWestCorner = MathF.Abs(minimumX + FishWarehouseInteriorRoomDefinition.HalfWidth) <= BoundarySnapTolerance;
        bool atEastCorner = MathF.Abs(maximumX - FishWarehouseInteriorRoomDefinition.HalfWidth) <= BoundarySnapTolerance;
        if (atWestCorner)
        {
            if (rightMargin < MinimumResidualWallMargin)
                return false;
        }
        else if (atEastCorner)
        {
            if (leftMargin < MinimumResidualWallMargin)
                return false;
        }
        else if (leftMargin < MinimumResidualWallMargin || rightMargin < MinimumResidualWallMargin)
        {
            return false;
        }

        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            minimumX,
            maximumX,
            bottom,
            top,
            IsCornerTerminatedAtMinimum: atWestCorner,
            IsCornerTerminatedAtMaximum: atEastCorner,
            SemanticId: "garage");

        var transitionPrism = new FishWarehouseTransitionPrism(
            minimumX,
            maximumX,
            bottom,
            top,
            FishWarehouseInteriorRoomDefinition.HalfDepth,
            panelPlaneZ);
        foreach (FishWarehouseGarageOpeningCandidate candidate in matches)
        {
            if (!FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
                    transitionPrism,
                    candidate.SurvivingNonTargetColliders) ||
                !FishWarehouseGarageOpeningClearanceDefinition.IsFloorBandClear(
                    transitionPrism,
                    candidate.SurvivingNonTargetColliders))
            {
                return false;
            }
        }

        var navigationBounds = new FishWarehouseNavigationHorizontalBounds(
            minimumX + FishWarehouseInteriorRoomDefinition.HalfWidth,
            maximumX + FishWarehouseInteriorRoomDefinition.HalfWidth);

        projection = new FishWarehouseGarageOpeningProjection(
            opening,
            rawMinimumX,
            rawMaximumX,
            lintel,
            transitionPrism,
            navigationBounds);
        return true;
    }

    private static bool TryClampHorizontalBounds(
        float rawMinimumX,
        float rawMaximumX,
        out float minimumX,
        out float maximumX)
    {
        float roomMinimum = -FishWarehouseInteriorRoomDefinition.HalfWidth;
        float roomMaximum = FishWarehouseInteriorRoomDefinition.HalfWidth;
        if (rawMaximumX <= roomMinimum || rawMinimumX >= roomMaximum)
        {
            minimumX = 0f;
            maximumX = 0f;
            return false;
        }

        minimumX = SnapBoundary(MathF.Max(rawMinimumX, roomMinimum), roomMinimum);
        maximumX = SnapBoundary(MathF.Min(rawMaximumX, roomMaximum), roomMaximum);

        if (minimumX < roomMinimum ||
            maximumX > roomMaximum ||
            minimumX >= maximumX)
        {
            return false;
        }

        return true;
    }

    private static float SnapBoundary(float value, float edge)
    {
        return MathF.Abs(value - edge) <= BoundarySnapTolerance ? edge : value;
    }
}
