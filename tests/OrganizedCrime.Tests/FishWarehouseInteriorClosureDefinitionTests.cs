using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseInteriorClosureDefinitionTests
{
    [Fact]
    public void Ceiling_covers_further_than_the_authored_room_walls_but_stays_inside_native_faces()
    {
        Assert.True(FishWarehouseInteriorClosureDefinition.CeilingHalfWidth > FishWarehouseInteriorRoomDefinition.HalfWidth);
        Assert.True(FishWarehouseInteriorClosureDefinition.CeilingHalfDepth > FishWarehouseInteriorRoomDefinition.HalfDepth);
        // Measured native X face is ~±12.3; the ceiling must stay inside it so the slab never pokes outside.
        Assert.True(FishWarehouseInteriorClosureDefinition.CeilingHalfWidth < 12.3f);
    }

    [Fact]
    public void Plans_a_ceiling_at_ceiling_height_spanning_the_enlarged_footprint()
    {
        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanCeiling(out FishWarehouseClosurePiece? ceiling));
        Assert.NotNull(ceiling);
        Assert.Equal(FishWarehouseClosurePlane.CeilingXZ, ceiling!.Plane);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.CeilingY, ceiling.PlaneCoordinate, 5);
        Assert.Equal(-FishWarehouseInteriorClosureDefinition.CeilingHalfWidth, ceiling.HorizontalMinimum, 5);
        Assert.Equal(FishWarehouseInteriorClosureDefinition.CeilingHalfDepth, ceiling.VerticalMaximum, 5);
        Assert.False(ceiling.DoubleSided);
        Assert.True(ceiling.HasArea);
    }

    [Fact]
    public void Personnel_seal_fills_the_whole_opening_on_the_room_wall_plane()
    {
        var personnel = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.West,
            5.05f,
            6.90f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            FishWarehouseInteriorRoomDefinition.DoorHeight,
            IsCornerTerminatedAtMaximum: true,
            SemanticId: "personnel");

        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanPersonnelSeal(
            personnel, out FishWarehouseClosurePiece? seal));
        Assert.NotNull(seal);
        Assert.Equal(FishWarehouseClosurePlane.WallX, seal!.Plane);
        // Sits on the west room wall plane (not the native plane) so it tiles with the wall segments.
        Assert.Equal(-FishWarehouseInteriorRoomDefinition.HalfWidth, seal.PlaneCoordinate, 5);
        Assert.Equal(5.05f, seal.HorizontalMinimum, 5);
        Assert.Equal(6.90f, seal.HorizontalMaximum, 5);
        // Fills the full door hole, floor to opening top.
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, seal.VerticalMinimum, 5);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.DoorHeight, seal.VerticalMaximum, 5);
        Assert.True(seal.DoubleSided);
        Assert.True(seal.HasArea);
    }

    [Fact]
    public void Personnel_seal_is_rejected_for_a_missing_or_zero_width_opening()
    {
        Assert.False(FishWarehouseInteriorClosureDefinition.TryPlanPersonnelSeal(null, out _));

        var degenerate = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.West, 6.0f, 6.0f, 0f, FishWarehouseInteriorRoomDefinition.DoorHeight,
            SemanticId: "personnel");
        Assert.False(FishWarehouseInteriorClosureDefinition.TryPlanPersonnelSeal(degenerate, out _));
    }

    [Fact]
    public void Garage_header_caps_above_the_aperture_at_the_native_panel_plane()
    {
        FishWarehouseGarageOpeningProjection projection = CreateGarageProjection();

        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanGarageHeader(
            projection, out FishWarehouseClosurePiece? header));
        Assert.NotNull(header);
        Assert.Equal(FishWarehouseClosurePlane.WallZ, header!.Plane);
        Assert.True(header.DoubleSided);
        // Above the aperture top, up to the roof-level header top — no intrusion into the garage opening.
        Assert.True(header.VerticalMinimum >= projection.Opening.Top - FishWarehouseWallOpeningPlanner.Epsilon);
        Assert.Equal(FishWarehouseInteriorClosureDefinition.GarageHeaderTopY, header.VerticalMaximum, 5);
        Assert.True(header.VerticalMaximum > FishWarehouseInteriorRoomDefinition.CeilingY);
        // Inset toward the interior from the positive native plane Z.
        Assert.Equal(projection.TransitionPrism.MaximumZ - FishWarehouseInteriorClosureDefinition.PlaneInset, header.PlaneCoordinate, 5);
        Assert.Equal(projection.Opening.HorizontalMinimum, header.HorizontalMinimum, 5);
        Assert.Equal(projection.Opening.HorizontalMaximum, header.HorizontalMaximum, 5);
        Assert.True(header.HasArea);
    }

    [Fact]
    public void Garage_header_is_rejected_when_the_aperture_already_reaches_the_header_top()
    {
        FishWarehouseGarageOpeningProjection projection = CreateGarageProjection(
            openingTop: FishWarehouseInteriorClosureDefinition.GarageHeaderTopY);

        Assert.False(FishWarehouseInteriorClosureDefinition.TryPlanGarageHeader(projection, out _));
    }

    [Fact]
    public void Closure_pieces_never_have_zero_area()
    {
        var personnel = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.West, 5.05f, 6.90f,
            FishWarehouseInteriorRoomDefinition.FloorY, FishWarehouseInteriorRoomDefinition.DoorHeight,
            IsCornerTerminatedAtMaximum: true, SemanticId: "personnel");

        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanCeiling(out FishWarehouseClosurePiece? ceiling));
        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanPersonnelSeal(personnel, out FishWarehouseClosurePiece? seal));
        Assert.True(FishWarehouseInteriorClosureDefinition.TryPlanGarageHeader(CreateGarageProjection(), out FishWarehouseClosurePiece? header));

        foreach (FishWarehouseClosurePiece piece in new[] { ceiling!, seal!, header! })
        {
            Assert.True(piece.HasArea);
            Assert.True(piece.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(piece.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        }
    }

    private static FishWarehouseGarageOpeningProjection CreateGarageProjection(float openingTop = 4.35f)
    {
        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -11.60f,
            -7.64268f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            openingTop,
            IsCornerTerminatedAtMinimum: true,
            SemanticId: "garage");
        var prism = new FishWarehouseTransitionPrism(
            -11.60f, -7.64268f, 0f, openingTop, FishWarehouseInteriorRoomDefinition.HalfDepth, 7.55f);
        var navigation = new FishWarehouseNavigationHorizontalBounds(0f, 3.95732f);
        return new FishWarehouseGarageOpeningProjection(opening, -11.7f, -7.5f, 1.45f, prism, navigation);
    }
}
