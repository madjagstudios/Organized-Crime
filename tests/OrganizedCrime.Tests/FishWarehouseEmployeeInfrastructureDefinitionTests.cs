using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeInfrastructureDefinitionTests
{
    [Fact]
    public void Runtime_attachment_contract_accepts_the_twenty_point_layout()
    {
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.HasValidLayout());
    }

    [Fact]
    public void Defined_points_are_twenty_with_stable_ordered_names_and_indices()
    {
        Assert.Equal(20, FishWarehouseEmployeeInfrastructureDefinition.DefinedIdlePointCount);
        Assert.Equal(20, FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count);
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(0));
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(19));
        Assert.False(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(-1));
        Assert.False(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(20));

        Assert.Equal(
            Enumerable.Range(0, 20)
                .Select(index => $"OC_FishWarehouse_EmployeeIdle_{index}"),
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Select(point => point.Name));
    }

    [Fact]
    public void All_twenty_points_are_the_owner_placed_block_outside_east_of_the_garage()
    {
        // OC-117 owner runs 2 and 3 (2026-10-04): one tight block, outside,
        // off the sidewalk pedestrians use, placed live with
        // `oc_ch3 capacity here`.
        var expected = new[]
        {
            (2.07f, 0f, 10.93f),
            (2.16f, 0f, 11.72f),
            (2.24f, 0f, 12.52f),
            (2.33f, 0f, 13.31f),
            (2.41f, 0f, 14.11f),
            (1.28f, 0f, 11.01f),
            (1.36f, 0f, 11.81f),
            (1.45f, 0f, 12.60f),
            (1.53f, 0f, 13.40f),
            (1.62f, 0f, 14.19f),
            (0.48f, 0f, 11.10f),
            (0.57f, 0f, 11.89f),
            (0.65f, 0f, 12.69f),
            (0.74f, 0f, 13.48f),
            (0.82f, 0f, 14.28f),
            (-0.31f, 0f, 11.18f),
            (-0.23f, 0f, 11.98f),
            (-0.14f, 0f, 12.77f),
            (-0.06f, 0f, 13.57f),
            (0.02f, 0f, 14.36f)
        };

        Assert.Equal(
            expected,
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
                .Select(point => (point.X, point.Y, point.Z)));
        Assert.All(
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints,
            point => Assert.True(
                point.Z > FishWarehouseInteriorRoomDefinition.HalfDepth + 1f,
                $"Idle point {point.Name} is not outside the building."));
    }

    [Fact]
    public void The_block_stays_clear_of_the_loading_dock_frontage()
    {
        Assert.All(
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints,
            point => Assert.True(
                point.X < FishWarehouseLoadingDockDefinition.FrontageMinX - 1f,
                $"Idle point {point.Name} is within a metre of the dock frontage."));
    }
}
