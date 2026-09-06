using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeInfrastructureDefinitionTests
{
    [Fact]
    public void Runtime_attachment_contract_accepts_the_ten_point_layout()
    {
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.HasValidLayout());
    }

    [Fact]
    public void Capacity_is_ten_with_stable_ordered_names_and_indices()
    {
        Assert.Equal(10, FishWarehouseEmployeeInfrastructureDefinition.Capacity);
        Assert.Equal(10, FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count);
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(0));
        Assert.True(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(9));
        Assert.False(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(-1));
        Assert.False(FishWarehouseEmployeeInfrastructureDefinition.IsValidEmployeeIndex(10));

        Assert.Equal(
            Enumerable.Range(0, 10)
                .Select(index => $"OC_FishWarehouse_EmployeeIdle_{index}"),
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Select(point => point.Name));
    }

    [Fact]
    public void Existing_four_idle_coordinates_are_preserved_exactly()
    {
        var expected = new[]
        {
            (-7.8f, 0f, 4.8f),
            (-7.8f, 0f, 2.8f),
            (-7.8f, 0f, 0.8f),
            (-7.8f, 0f, -1.2f)
        };

        Assert.Equal(
            expected,
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
                .Take(4)
                .Select(point => (point.X, point.Y, point.Z)));
    }

    [Fact]
    public void Recorded_additional_candidates_are_preserved_as_outside_waiting_points()
    {
        var expected = new[]
        {
            (-7.30f, 0f, 10.14f),
            (-6.20f, 0f, 10.18f),
            (-5.42f, 0f, 9.05f),
            (-4.37f, 0f, 9.09f),
            (-4.41f, 0f, 10.05f),
            (-4.46f, 0f, 11.45f)
        };

        Assert.Equal(
            expected,
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
                .Skip(4)
                .Select(point => (point.X, point.Y, point.Z)));
        Assert.Contains(
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints,
            point => point.Z > FishWarehouseInteriorRoomDefinition.HalfDepth);
    }
}
