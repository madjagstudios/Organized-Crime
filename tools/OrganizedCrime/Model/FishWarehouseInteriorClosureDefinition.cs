namespace OrganizedCrime.Model;

public enum FishWarehouseClosurePlane
{
    /// <summary>Horizontal plane at a fixed Y; spans X (horizontal) and Z (vertical fields).</summary>
    CeilingXZ,
    /// <summary>Vertical plane at a fixed X; spans Z (horizontal) and Y (vertical fields).</summary>
    WallX,
    /// <summary>Vertical plane at a fixed Z; spans X (horizontal) and Y (vertical fields).</summary>
    WallZ
}

/// <summary>
/// One render-only closure rectangle in Fish Warehouse anchor-local space.
/// Horizontal/Vertical are the in-plane axes for the plane kind (see
/// <see cref="FishWarehouseClosurePlane"/>); PlaneCoordinate is the fixed axis.
/// DoubleSided pieces are emitted with both windings so a native-plane infill
/// reads correctly from inside and outside.
/// </summary>
public sealed record FishWarehouseClosurePiece(
    string SemanticId,
    FishWarehouseClosurePlane Plane,
    float PlaneCoordinate,
    float HorizontalMinimum,
    float HorizontalMaximum,
    float VerticalMinimum,
    float VerticalMaximum,
    bool DoubleSided)
{
    public float Width => HorizontalMaximum - HorizontalMinimum;
    public float Height => VerticalMaximum - VerticalMinimum;
    public bool HasArea => Width > FishWarehouseWallOpeningPlanner.Epsilon &&
        Height > FishWarehouseWallOpeningPlanner.Epsilon;
}

/// <summary>
/// Pure planner for OC-4's additive visual closure layer. It never changes the
/// authored room's defining constants (HalfWidth/HalfDepth/CeilingY/FloorY) —
/// those are load-bearing for the frozen garage aperture, transition prism, and
/// navigation bounds. It only produces extra render-only rectangles that cap the
/// perimeter roof band and the two door headers so no interior sightline reaches
/// the single-sided (culled) native roof underside.
/// </summary>
public static class FishWarehouseInteriorClosureDefinition
{
    /// <summary>
    /// Visual-only ceiling half-extents. Larger than the authored room walls so
    /// the ceiling covers the perimeter band out toward the native walls
    /// (native X face measured at ~±12.3; Z estimated pending the acceptance
    /// pass). Kept just inside the native faces so the slab never pokes outside.
    /// </summary>
    public const float CeilingHalfWidth = 12.2f;
    public const float CeilingHalfDepth = 7.6f;

    /// <summary>
    /// Top of the garage header infill. Reaches above the native roof underside
    /// (~Y6) into the gable so the sky slot above the garage opening is closed
    /// from inside and outside; the header is inset in front of the native gable.
    /// </summary>
    public const float GarageHeaderTopY = 6.8f;

    /// <summary>Deliberate offset from a native wall plane, to avoid coincident-face z-fighting.</summary>
    public const float PlaneInset = 0.05f;

    public static bool TryPlanCeiling(out FishWarehouseClosurePiece? ceiling)
    {
        // The enlarged ceiling must cover further than the authored room walls;
        // this invariant is guaranteed by the constants above and asserted in
        // tests, so at runtime it only needs a positive-area result.
        ceiling = new FishWarehouseClosurePiece(
            SemanticId: "ceiling",
            Plane: FishWarehouseClosurePlane.CeilingXZ,
            PlaneCoordinate: FishWarehouseInteriorRoomDefinition.CeilingY,
            HorizontalMinimum: -CeilingHalfWidth,
            HorizontalMaximum: CeilingHalfWidth,
            VerticalMinimum: -CeilingHalfDepth,
            VerticalMaximum: CeilingHalfDepth,
            DoubleSided: false);
        return ceiling.HasArea;
    }

    /// <summary>
    /// Full brick seal over the personnel door opening at the room wall plane.
    /// The garage is the employee entrance, so the personnel door is not needed
    /// from inside; filling the opening (floor to its top) makes the interior
    /// wall solid with no door and no sky visible through the aperture. The
    /// physical native door and its frozen aperture are unaffected (render-only,
    /// no collider). The panel tiles with the room's own wall segments, so it is
    /// placed exactly on the wall plane rather than inset.
    /// </summary>
    public static bool TryPlanPersonnelSeal(
        FishWarehouseWallOpening? personnelOpening,
        out FishWarehouseClosurePiece? seal)
    {
        seal = null;
        if (personnelOpening is null)
            return false;

        float bottom = personnelOpening.Bottom;
        float top = personnelOpening.Top;
        if (top - bottom <= FishWarehouseWallOpeningPlanner.Epsilon ||
            personnelOpening.Width <= FishWarehouseWallOpeningPlanner.Epsilon)
        {
            return false;
        }

        switch (personnelOpening.Wall)
        {
            case FishWarehouseInteriorDoorSide.West:
                seal = BuildSeal(FishWarehouseClosurePlane.WallX, -FishWarehouseInteriorRoomDefinition.HalfWidth, personnelOpening, bottom, top);
                break;
            case FishWarehouseInteriorDoorSide.East:
                seal = BuildSeal(FishWarehouseClosurePlane.WallX, FishWarehouseInteriorRoomDefinition.HalfWidth, personnelOpening, bottom, top);
                break;
            case FishWarehouseInteriorDoorSide.North:
                seal = BuildSeal(FishWarehouseClosurePlane.WallZ, FishWarehouseInteriorRoomDefinition.HalfDepth, personnelOpening, bottom, top);
                break;
            default:
                seal = BuildSeal(FishWarehouseClosurePlane.WallZ, -FishWarehouseInteriorRoomDefinition.HalfDepth, personnelOpening, bottom, top);
                break;
        }

        return seal.HasArea;
    }

    private static FishWarehouseClosurePiece BuildSeal(
        FishWarehouseClosurePlane plane,
        float planeCoordinate,
        FishWarehouseWallOpening opening,
        float bottom,
        float top) =>
        new(
            "personnel-seal",
            plane,
            planeCoordinate,
            opening.HorizontalMinimum,
            opening.HorizontalMaximum,
            bottom,
            top,
            DoubleSided: true);

    /// <summary>
    /// Header infill above the garage aperture, placed just inside the native
    /// garage panel plane and spanning only above the aperture top.
    /// </summary>
    public static bool TryPlanGarageHeader(
        FishWarehouseGarageOpeningProjection projection,
        out FishWarehouseClosurePiece? header)
    {
        header = null;
        if (projection is null)
            return false;

        float top = GarageHeaderTopY;
        float bottom = projection.Opening.Top;
        if (top - bottom <= FishWarehouseWallOpeningPlanner.Epsilon)
            return false;

        // The garage is on the north wall; the native panel plane is the far Z of the transition prism.
        float nativePlaneZ = projection.TransitionPrism.MaximumZ;
        float planeCoordinate = InsetTowardOrigin(nativePlaneZ);
        header = new FishWarehouseClosurePiece(
            "garage-header",
            FishWarehouseClosurePlane.WallZ,
            planeCoordinate,
            projection.Opening.HorizontalMinimum,
            projection.Opening.HorizontalMaximum,
            bottom,
            top,
            DoubleSided: true);

        return header.HasArea && DoesNotIntrudeIntoGarageAperture(header, projection);
    }

    private static float InsetTowardOrigin(float planeCoordinate) =>
        planeCoordinate > 0f ? planeCoordinate - PlaneInset : planeCoordinate + PlaneInset;

    private static bool DoesNotIntrudeIntoGarageAperture(
        FishWarehouseClosurePiece header,
        FishWarehouseGarageOpeningProjection projection) =>
        header.VerticalMinimum >= projection.Opening.Top - FishWarehouseWallOpeningPlanner.Epsilon;
}
