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
    public void Garage_jamb_reveal_is_planned_on_the_free_edge()
    {
        // The measured garage opening terminates at the west corner (min) and is free at max.
        FishWarehouseGarageOpeningProjection projection = CreateGarageProjection();

        IReadOnlyList<FishWarehouseClosurePiece> jambs =
            FishWarehouseInteriorClosureDefinition.PlanGarageJambs(projection);

        FishWarehouseClosurePiece jamb = Assert.Single(jambs, piece => piece.SemanticId == "garage-jamb-max");
        Assert.Equal(FishWarehouseClosurePlane.WallX, jamb.Plane);
        Assert.Equal(projection.Opening.HorizontalMaximum, jamb.PlaneCoordinate, 5);
        // Spans the transition-prism depth from the authored north wall to the native plane,
        // plus the overlap that tucks the outer edge behind the pillar.
        Assert.Equal(FishWarehouseInteriorRoomDefinition.HalfDepth, jamb.HorizontalMinimum, 5);
        Assert.Equal(
            projection.TransitionPrism.MaximumZ + FishWarehouseInteriorClosureDefinition.GarageJambOverlap,
            jamb.HorizontalMaximum,
            5);
        Assert.Equal(projection.Opening.Bottom, jamb.VerticalMinimum, 5);
        Assert.Equal(FishWarehouseInteriorClosureDefinition.GarageHeaderTopY, jamb.VerticalMaximum, 5);
        Assert.True(jamb.DoubleSided);
        Assert.True(jamb.HasArea);
    }

    [Fact]
    public void Garage_jambs_are_empty_for_a_null_projection()
    {
        Assert.Empty(FishWarehouseInteriorClosureDefinition.PlanGarageJambs(null));
    }

    [Fact]
    public void Garage_jambs_span_both_edges_within_the_opening_planes_when_neither_is_corner_terminated()
    {
        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -7.0f,
            -5.0f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            4.35f,
            SemanticId: "garage");
        var prism = new FishWarehouseTransitionPrism(
            -7.0f, -5.0f, 0f, 4.35f, FishWarehouseInteriorRoomDefinition.HalfDepth, 7.55f);
        var projection = new FishWarehouseGarageOpeningProjection(
            opening, -7.0f, -5.0f, 1.45f, prism, new FishWarehouseNavigationHorizontalBounds(0f, 2f));

        IReadOnlyList<FishWarehouseClosurePiece> jambs =
            FishWarehouseInteriorClosureDefinition.PlanGarageJambs(projection);

        Assert.Equal(2, jambs.Count);
        Assert.All(jambs, jamb =>
        {
            Assert.Equal(FishWarehouseClosurePlane.WallX, jamb.Plane);
            Assert.True(jamb.PlaneCoordinate >= opening.HorizontalMinimum);
            Assert.True(jamb.PlaneCoordinate <= opening.HorizontalMaximum);
            Assert.Equal(FishWarehouseInteriorRoomDefinition.HalfDepth, jamb.HorizontalMinimum, 5);
            Assert.Equal(prism.MaximumZ + FishWarehouseInteriorClosureDefinition.GarageJambOverlap, jamb.HorizontalMaximum, 5);
            Assert.True(jamb.DoubleSided);
            Assert.True(jamb.HasArea);
        });
        Assert.Contains(jambs, jamb => jamb.SemanticId == "garage-jamb-min" && jamb.PlaneCoordinate == opening.HorizontalMinimum);
        Assert.Contains(jambs, jamb => jamb.SemanticId == "garage-jamb-max" && jamb.PlaneCoordinate == opening.HorizontalMaximum);
    }

    [Fact]
    public void Garage_jamb_edges_terminated_at_corners_still_get_a_jamb()
    {
        // OC-110 owner run: the native wall stands ~0.7 m outside the authored west corner,
        // so a corner-terminated edge reveals sky too and needs its jamb.
        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -11.60f,
            -7.64268f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            4.35f,
            IsCornerTerminatedAtMinimum: true,
            IsCornerTerminatedAtMaximum: true,
            SemanticId: "garage");
        var prism = new FishWarehouseTransitionPrism(
            -11.60f, -7.64268f, 0f, 4.35f, FishWarehouseInteriorRoomDefinition.HalfDepth, 7.55f);
        var projection = new FishWarehouseGarageOpeningProjection(
            opening, -11.7f, -7.5f, 1.45f, prism, new FishWarehouseNavigationHorizontalBounds(0f, 3.95732f));

        IReadOnlyList<FishWarehouseClosurePiece> jambs =
            FishWarehouseInteriorClosureDefinition.PlanGarageJambs(projection);
        float capZ = FishWarehouseInteriorClosureDefinition.GarageCornerPillarFrontZ -
            FishWarehouseInteriorClosureDefinition.GarageCornerCapSetback;

        Assert.Equal(3, jambs.Count);
        FishWarehouseClosurePiece max = Assert.Single(jambs, jamb => jamb.SemanticId == "garage-jamb-max");
        Assert.Equal(opening.HorizontalMaximum, max.PlaneCoordinate, 5);
        Assert.Equal(prism.MaximumZ + FishWarehouseInteriorClosureDefinition.GarageJambOverlap, max.HorizontalMaximum, 5);

        // The west (min) corner jamb runs out to the native pillar's front face...
        FishWarehouseClosurePiece min = Assert.Single(jambs, jamb => jamb.SemanticId == "garage-jamb-min");
        Assert.Equal(opening.HorizontalMinimum, min.PlaneCoordinate, 5);
        Assert.Equal(capZ, min.HorizontalMaximum, 5);

        // ...and a front cap closes the slot between that jamb and the pillar, tucked into the pillar.
        FishWarehouseClosurePiece cap = Assert.Single(jambs, jamb => jamb.SemanticId == "garage-corner-cap");
        Assert.Equal(FishWarehouseClosurePlane.WallZ, cap.Plane);
        Assert.Equal(capZ, cap.PlaneCoordinate, 5);
        Assert.True(cap.HorizontalMinimum < FishWarehouseInteriorClosureDefinition.GarageCornerPillarInnerX);
        Assert.Equal(opening.HorizontalMinimum, cap.HorizontalMaximum, 5);
        Assert.Equal(FishWarehouseInteriorClosureDefinition.GarageCornerPillarTopY, cap.VerticalMaximum, 5);
        Assert.True(cap.DoubleSided);
        Assert.True(cap.HasArea);
    }

    [Fact]
    public void Room_collider_mesh_contains_no_jamb_pieces()
    {
        // OC-4 gotcha guard: the room mesh doubles as its MeshCollider, so jamb
        // reveals must never be planned into it. They live in the separate
        // collider-free jamb object instead.
        string roomHost = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "FishWarehouseInteriorRoomHost.cs"));

        Assert.DoesNotContain("PlanGarageJambs", roomHost);
        Assert.DoesNotContain("garage-jamb", roomHost);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
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
