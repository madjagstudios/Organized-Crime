using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseInteriorRoomDefinitionTests
{
    [Fact]
    public void Room_is_smaller_than_measured_source_footprint()
    {
        Assert.True(FishWarehouseInteriorRoomDefinition.HalfWidth < 12.2f);
        Assert.True(FishWarehouseInteriorRoomDefinition.HalfDepth < 7.5f);
        Assert.True(FishWarehouseInteriorRoomDefinition.CeilingY > FishWarehouseInteriorRoomDefinition.DoorHeight);
    }

    [Theory]
    [InlineData(-11.8f, 0f, FishWarehouseInteriorDoorSide.West)]
    [InlineData(11.8f, 0f, FishWarehouseInteriorDoorSide.East)]
    [InlineData(0f, -7.4f, FishWarehouseInteriorDoorSide.South)]
    [InlineData(0f, 7.4f, FishWarehouseInteriorDoorSide.North)]
    public void Door_side_uses_nearest_room_boundary(
        float localX,
        float localZ,
        FishWarehouseInteriorDoorSide expected)
    {
        Assert.Equal(expected, FishWarehouseInteriorRoomDefinition.ResolveDoorSide(localX, localZ));
    }

    [Fact]
    public void Room_plan_requires_a_positive_door_clearance()
    {
        Assert.True(FishWarehouseInteriorRoomDefinition.DoorWidth > 1f);
        Assert.True(FishWarehouseInteriorRoomDefinition.DoorHeight > 2f);
        Assert.True(FishWarehouseInteriorRoomDefinition.WallInset > 0f);
    }

    [Fact]
    public void Plans_independent_personnel_and_garage_openings_for_the_authored_room()
    {
        var garage = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -FishWarehouseInteriorRoomDefinition.HalfWidth,
            -7.91795f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            5.35f,
            IsCornerTerminatedAtMinimum: true,
            SemanticId: "garage");

        Assert.True(FishWarehouseInteriorRoomDefinition.TryPlanRoomWalls(
            -11.6f,
            0f,
            garage,
            out var openings,
            out var wallPlans));

        Assert.Collection(
            openings!,
            opening =>
            {
                Assert.Equal("personnel", opening.SemanticId);
                Assert.Equal(FishWarehouseInteriorDoorSide.West, opening.Wall);
                Assert.Equal(FishWarehouseInteriorRoomDefinition.DoorWidth, opening.Width, 5);
                Assert.Equal(FishWarehouseInteriorRoomDefinition.DoorHeight, opening.Height, 5);
            },
            opening => Assert.Equal(garage, opening));
        Assert.Equal(4, wallPlans!.Count);
        Assert.All(wallPlans!.SelectMany(plan => plan.Segments), segment =>
        {
            Assert.True(segment.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(segment.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        });
    }

    [Fact]
    public void Plans_the_measured_adjacent_corner_topology_without_residual_wall_slivers()
    {
        var garage = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -11.60f,
            -7.64268f,
            0f,
            4.35f,
            IsCornerTerminatedAtMinimum: true,
            SemanticId: "garage");

        Assert.True(FishWarehouseInteriorRoomDefinition.TryPlanRoomWalls(
            -12.31f,
            6.15f,
            garage,
            out var openings,
            out var wallPlans));

        FishWarehouseWallOpening personnel = Assert.Single(openings!, opening => opening.SemanticId == "personnel");
        Assert.Equal(FishWarehouseInteriorDoorSide.West, personnel.Wall);
        Assert.Equal(5.05f, personnel.HorizontalMinimum, 4);
        Assert.Equal(6.90f, personnel.HorizontalMaximum, 4);
        Assert.True(personnel.IsCornerTerminatedAtMaximum);

        FishWarehouseWallPlan west = Assert.Single(wallPlans!, plan => plan.Wall == FishWarehouseInteriorDoorSide.West);
        Assert.Collection(
            west.Segments,
            segment => AssertSegment(segment, -6.90f, 5.05f, 0f, 5.80f),
            segment => AssertSegment(segment, 5.05f, 6.90f, 3.00f, 5.80f));

        FishWarehouseWallPlan north = Assert.Single(wallPlans!, plan => plan.Wall == FishWarehouseInteriorDoorSide.North);
        Assert.Collection(
            north.Segments,
            segment => AssertSegment(segment, -11.60f, -7.64268f, 4.35f, 5.80f),
            segment => AssertSegment(segment, -7.64268f, 11.60f, 0f, 5.80f));

        Assert.All(wallPlans!.SelectMany(plan => plan.Segments), segment =>
        {
            Assert.True(segment.Width > FishWarehouseWallOpeningPlanner.Epsilon);
            Assert.True(segment.Height > FishWarehouseWallOpeningPlanner.Epsilon);
        });
    }

    [Fact]
    public void Rejects_overlapping_personnel_and_garage_openings_on_the_same_wall()
    {
        var garage = new FishWarehouseWallOpening(
            FishWarehouseInteriorDoorSide.North,
            -1f,
            1f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            5.35f,
            SemanticId: "garage");

        Assert.False(FishWarehouseInteriorRoomDefinition.TryPlanRoomWalls(
            0f,
            FishWarehouseInteriorRoomDefinition.HalfDepth,
            garage,
            out _,
            out _));
    }

    private static void AssertSegment(
        FishWarehouseWallSegment segment,
        float horizontalMinimum,
        float horizontalMaximum,
        float bottom,
        float top)
    {
        Assert.Equal(horizontalMinimum, segment.HorizontalMinimum, 4);
        Assert.Equal(horizontalMaximum, segment.HorizontalMaximum, 4);
        Assert.Equal(bottom, segment.Bottom, 4);
        Assert.Equal(top, segment.Top, 4);
    }

}
