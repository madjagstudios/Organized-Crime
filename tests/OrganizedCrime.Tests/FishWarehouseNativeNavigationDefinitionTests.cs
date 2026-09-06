using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativeNavigationDefinitionTests
{
    [Fact]
    public void Creates_the_native_floor_and_garage_link_from_the_live_projection()
    {
        FishWarehouseNativeNavigationPlan plan =
            FishWarehouseNativeNavigationDefinition.Create(LiveProjection());

        AssertVector(plan.Surface.Center, 0f, -0.05f, 0.30f);
        AssertVector(plan.Surface.Size, 23.20f, 0.10f, 14.40f);
        AssertVector(plan.DoorwayInterior, -9.62134f, 0f, 6.60f);
        AssertVector(plan.Link.Start, -9.62134f, 0f, 7.20f);
        AssertVector(plan.Link.NominalExteriorQueryPoint, -9.62134f, 0f, 9.70f);
        Assert.Equal(3.25732f, plan.Link.Width, 4);
        Assert.Equal(0.35f, plan.Link.SideInset, 4);
        Assert.True(plan.Link.Bidirectional);
    }

    [Fact]
    public void Uses_full_room_surface_bounds_and_keeps_the_surface_top_on_the_authored_floor()
    {
        FishWarehouseNativeNavigationPlan plan =
            FishWarehouseNativeNavigationDefinition.Create(LiveProjection());

        Assert.Equal(-11.60f, plan.Surface.Center.X - plan.Surface.Size.X / 2f, 4);
        Assert.Equal(11.60f, plan.Surface.Center.X + plan.Surface.Size.X / 2f, 4);
        Assert.Equal(-6.90f, plan.Surface.Center.Z - plan.Surface.Size.Z / 2f, 4);
        Assert.Equal(7.50f, plan.Surface.Center.Z + plan.Surface.Size.Z / 2f, 4);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY,
            plan.Surface.Center.Y + plan.Surface.Size.Y / 2f);
        Assert.Equal(0, plan.Surface.Area);
        Assert.False(plan.Surface.GenerateLinks);
    }

    [Fact]
    public void Keeps_validation_points_centered_on_the_projected_aperture_and_separated_from_the_transition_band()
    {
        FishWarehouseGarageOpeningProjection projection = LiveProjection();
        FishWarehouseNativeNavigationPlan plan =
            FishWarehouseNativeNavigationDefinition.Create(projection);

        Assert.Equal(projection.Opening.Center, plan.DoorwayInterior.X, 4);
        Assert.Equal(projection.Opening.Center, plan.Link.Start.X, 4);
        Assert.Equal(projection.Opening.Center, plan.Link.NominalExteriorQueryPoint.X, 4);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, plan.DoorwayInterior.Y, 4);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, plan.Link.Start.Y, 4);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, plan.Link.NominalExteriorQueryPoint.Y, 4);
        Assert.Equal(projection.TransitionPrism.MinimumZ - 0.30f, plan.DoorwayInterior.Z, 4);
        Assert.Equal(projection.TransitionPrism.MaximumZ - 0.30f, plan.Link.Start.Z, 4);
        Assert.True(plan.Link.NominalExteriorQueryPoint.Z > projection.TransitionPrism.MaximumZ);
        Assert.Equal(1.00f, plan.SampleRadius, 4);
    }

    [Fact]
    public void Insets_the_live_aperture_link_edges_by_the_named_side_margin()
    {
        FishWarehouseNativeNavigationPlan plan =
            FishWarehouseNativeNavigationDefinition.Create(LiveProjection());

        float leftEdge = plan.Link.Start.X - plan.Link.Width / 2f;
        float rightEdge = plan.Link.Start.X + plan.Link.Width / 2f;

        Assert.Equal(-11.25000f, leftEdge, 4);
        Assert.Equal(-7.99268f, rightEdge, 4);
        Assert.Equal(0.35f, leftEdge - (-11.60f), 4);
        Assert.Equal(0.35f, -7.64268f - rightEdge, 4);
    }

    [Fact]
    public void Bake_bounds_enclose_the_surface_and_authored_room_height_with_fixed_padding()
    {
        FishWarehouseNativeNavigationPlan plan =
            FishWarehouseNativeNavigationDefinition.Create(LiveProjection());

        Assert.Equal(-12.10f, plan.BakeBounds.Center.X - plan.BakeBounds.Size.X / 2f, 4);
        Assert.Equal(12.10f, plan.BakeBounds.Center.X + plan.BakeBounds.Size.X / 2f, 4);
        Assert.Equal(-7.40f, plan.BakeBounds.Center.Z - plan.BakeBounds.Size.Z / 2f, 4);
        Assert.Equal(8.00f, plan.BakeBounds.Center.Z + plan.BakeBounds.Size.Z / 2f, 4);
        Assert.Equal(-0.50f, plan.BakeBounds.Center.Y - plan.BakeBounds.Size.Y / 2f, 4);
        Assert.Equal(6.30f, plan.BakeBounds.Center.Y + plan.BakeBounds.Size.Y / 2f, 4);
        Assert.Equal(0.50f, FishWarehouseNativeNavigationDefinition.BakeBoundsPadding, 4);
    }

    [Fact]
    public void Rejects_a_projection_that_is_not_north_finite_and_valid_before_creating_geometry()
    {
        FishWarehouseGarageOpeningProjection nonNorth = LiveProjection() with
        {
            Opening = LiveProjection().Opening with { Wall = FishWarehouseInteriorDoorSide.South }
        };
        FishWarehouseGarageOpeningProjection nonFinite = LiveProjection() with
        {
            NavigationBounds = new(float.NaN, 1f)
        };

        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(nonNorth));
        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(nonFinite));
    }

    [Fact]
    public void Rejects_zero_and_under_minimum_lintel_values()
    {
        FishWarehouseGarageOpeningProjection zeroLintel = ProjectionWithLintel(0f);
        FishWarehouseGarageOpeningProjection underMinimumLintel = ProjectionWithLintel(0.29f);

        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(zeroLintel));
        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(underMinimumLintel));
    }

    [Fact]
    public void Rejects_malformed_projection_relationships_before_creating_geometry()
    {
        FishWarehouseGarageOpeningProjection reversedRawBounds = LiveProjection() with
        {
            RawMinimumX = -7.64268f,
            RawMaximumX = -11.60000f
        };
        FishWarehouseGarageOpeningProjection mismatchedLintel = LiveProjection() with
        {
            Lintel = 1.40f
        };
        FishWarehouseGarageOpeningProjection mismatchedTransitionBounds = LiveProjection() with
        {
            TransitionPrism = LiveProjection().TransitionPrism with
            {
                MinimumX = -11.50f,
                MaximumY = 4.25f
            }
        };
        FishWarehouseGarageOpeningProjection unboundedTransition = LiveProjection() with
        {
            TransitionPrism = LiveProjection().TransitionPrism with { MaximumZ = 100f }
        };

        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(reversedRawBounds));
        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(mismatchedLintel));
        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(mismatchedTransitionBounds));
        Assert.Throws<ArgumentException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(unboundedTransition));
    }

    [Fact]
    public void Rejects_a_garage_link_that_would_be_narrower_than_the_named_minimum()
    {
        FishWarehouseGarageOpeningProjection narrowLinkProjection = LiveProjection() with
        {
            NavigationBounds = new FishWarehouseNavigationHorizontalBounds(0f, 1.89f)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FishWarehouseNativeNavigationDefinition.Create(narrowLinkProjection));
    }

    [Theory]
    [InlineData(false, false, false, FishWarehouseNativeGraphDecision.Fail)]
    [InlineData(true, false, false, FishWarehouseNativeGraphDecision.AddSurfaceAndLink)]
    [InlineData(true, true, false, FishWarehouseNativeGraphDecision.AddLinkOnly)]
    [InlineData(true, true, true, FishWarehouseNativeGraphDecision.ReuseExistingPath)]
    public void Decides_native_graph_mutation_without_manufacturing_exterior_navigation(
        bool exteriorSampled,
        bool interiorSampled,
        bool pathComplete,
        FishWarehouseNativeGraphDecision expected)
    {
        var probe = new FishWarehouseNativeGraphProbe(
            ExteriorSampled: exteriorSampled,
            InteriorSampled: interiorSampled,
            PathComplete: pathComplete);

        Assert.Equal(expected, FishWarehouseNativeGraphDecisionDefinition.Decide(probe));
    }

    private static FishWarehouseGarageOpeningProjection LiveProjection() =>
        new(
            new FishWarehouseWallOpening(
                FishWarehouseInteriorDoorSide.North,
                -11.60000f,
                -7.64268f,
                0.00000f,
                4.35000f,
                IsCornerTerminatedAtMinimum: true,
                SemanticId: "garage"),
            RawMinimumX: -11.60000f,
            RawMaximumX: -7.64268f,
            Lintel: 1.45000f,
            new FishWarehouseTransitionPrism(
                -11.60000f,
                -7.64268f,
                0.00000f,
                4.35000f,
                6.90000f,
                7.50000f),
            new FishWarehouseNavigationHorizontalBounds(0.00000f, 3.95732f));

    private static FishWarehouseGarageOpeningProjection ProjectionWithLintel(float lintel)
    {
        FishWarehouseGarageOpeningProjection projection = LiveProjection();
        float openingTop = FishWarehouseInteriorRoomDefinition.CeilingY - lintel;
        return projection with
        {
            Opening = projection.Opening with { Top = openingTop },
            Lintel = lintel,
            TransitionPrism = projection.TransitionPrism with { MaximumY = openingTop }
        };
    }

    private static void AssertVector(
        FishWarehouseNativeNavigationVector actual,
        float x,
        float y,
        float z)
    {
        Assert.Equal(x, actual.X, 4);
        Assert.Equal(y, actual.Y, 4);
        Assert.Equal(z, actual.Z, 4);
    }
}
