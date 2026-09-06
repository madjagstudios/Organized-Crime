using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseWallOpeningPlannerTests
{
    [Theory]
    [InlineData(-11.6f, 0f, FishWarehouseInteriorDoorSide.West)]
    [InlineData(11.6f, 0f, FishWarehouseInteriorDoorSide.East)]
    [InlineData(0f, -6.9f, FishWarehouseInteriorDoorSide.South)]
    [InlineData(0f, 6.9f, FishWarehouseInteriorDoorSide.North)]
    public void Plans_the_dynamic_personnel_opening_on_every_wall(
        float localX,
        float localZ,
        FishWarehouseInteriorDoorSide expectedWall)
    {
        Assert.True(FishWarehouseWallOpeningPlanner.TryCreatePersonnelOpening(
            localX,
            localZ,
            out var opening));

        Assert.Equal(expectedWall, opening!.Wall);
        Assert.True(FishWarehouseWallOpeningPlanner.TryPlan(new[] { opening }, out var plan));
        Assert.All(plan!.Segments, segment =>
        {
            Assert.True(segment.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(segment.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        });
    }

    [Fact]
    public void Clips_the_measured_near_corner_personnel_opening_and_declares_corner_termination()
    {
        Assert.True(FishWarehouseWallOpeningPlanner.TryCreatePersonnelOpening(
            -12.31f,
            6.15f,
            out var opening));

        FishWarehouseWallOpening result = opening!;
        Assert.Equal(FishWarehouseInteriorDoorSide.West, result.Wall);
        Assert.Equal(5.05f, result.HorizontalMinimum, 4);
        Assert.Equal(6.90f, result.HorizontalMaximum, 4);
        Assert.Equal(1.85f, result.Width, 4);
        Assert.False(result.IsCornerTerminatedAtMinimum);
        Assert.True(result.IsCornerTerminatedAtMaximum);
    }

    [Fact]
    public void Rejects_a_personnel_opening_clipped_below_the_minimum_clear_width()
    {
        Assert.False(FishWarehouseWallOpeningPlanner.TryCreatePersonnelOpening(
            -12.31f,
            6.85f,
            out var opening));
        Assert.Null(opening);
    }

    [Fact]
    public void Corner_termination_emits_no_west_edge_residual_quad()
    {
        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -FishWarehouseInteriorRoomDefinition.HalfWidth,
            -7.91795f,
            0f,
            5.35f,
            IsCornerTerminatedAtMinimum: true,
            SemanticId: "garage");

        Assert.True(FishWarehouseWallOpeningPlanner.TryPlan(new[] { opening }, out var plan));
        Assert.DoesNotContain(plan!.Segments, segment =>
            segment.HorizontalMaximum <= opening.HorizontalMinimum + FishWarehouseWallOpeningPlanner.Epsilon);
        Assert.All(plan.Segments, segment =>
        {
            Assert.True(segment.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(segment.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        });
    }

    [Fact]
    public void Plans_two_non_overlapping_openings_on_one_wall_with_different_heights()
    {
        var openings = new[]
        {
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, -8f, -6f, 0f, 3f, SemanticId: "one"),
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, 2f, 4f, 0f, 4.5f, SemanticId: "two")
        };

        Assert.True(FishWarehouseWallOpeningPlanner.TryPlan(openings, out var plan));
        Assert.Contains(plan!.Segments, segment => segment.HorizontalMinimum == -8f && segment.Bottom == 3f);
        Assert.Contains(plan.Segments, segment => segment.HorizontalMinimum == 2f && segment.Bottom == 4.5f);
        Assert.All(plan.Segments, segment =>
        {
            Assert.True(segment.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(segment.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        });
    }

    [Fact]
    public void Rejects_overlapping_same_wall_semantic_openings_instead_of_merging_them()
    {
        var openings = new[]
        {
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, -2f, 1f, 0f, 3f, SemanticId: "one"),
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, 0.5f, 2f, 0f, 3f, SemanticId: "two")
        };

        Assert.False(FishWarehouseWallOpeningPlanner.TryPlan(openings, out _));
    }

    [Fact]
    public void Rejects_a_positive_inter_opening_wall_remnant_below_half_a_meter()
    {
        var openings = new[]
        {
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, -8f, -6f, 0f, 3f, SemanticId: "one"),
            new FishWarehouseWallOpening(FishWarehouseInteriorDoorSide.North, -5.8f, -3.8f, 0f, 3f, SemanticId: "two")
        };

        Assert.False(FishWarehouseWallOpeningPlanner.TryPlan(openings, out _));
    }

    [Fact]
    public void Rejects_zero_or_sliver_openings_before_emitting_segments()
    {
        var opening = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            1f,
            1f + FishWarehouseWallOpeningPlanner.Epsilon / 2f,
            0f,
            3f);

        Assert.False(FishWarehouseWallOpeningPlanner.TryPlan(new[] { opening }, out _));
    }

    [Fact]
    public void Allows_a_wall_without_openings_to_emit_one_full_wall_rectangle()
    {
        Assert.True(FishWarehouseWallOpeningPlanner.TryPlan(
            Array.Empty<FishWarehouseWallOpening>(),
            out var plan,
            FishWarehouseInteriorDoorSide.West));

        FishWarehouseWallPlan result = plan!;
        Assert.Single(result.Segments);
        Assert.True(result.Segments[0].Width > FishWarehouseWallOpeningPlanner.Epsilon);
        Assert.True(result.Segments[0].Height > FishWarehouseWallOpeningPlanner.Epsilon);
    }
}
