using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePropertyBoundsDefinitionTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Recognizes_whether_staged_bounds_are_usable(
        bool boundsRootActiveSelf,
        bool colliderEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            FishWarehousePropertyBoundsDefinition.IsStagedAttachmentUsable(
                boundsRootActiveSelf,
                colliderEnabled));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(-10.96f, 0f, 6.15f)]
    [InlineData(-9.4f, 0f, 4.8f)]
    [InlineData(-6f, 0f, 3.6f)]
    [InlineData(-2f, 0f, 3.6f)]
    [InlineData(2f, 0f, 3.6f)]
    [InlineData(6f, 0f, 3.6f)]
    [InlineData(11.5f, 5.7f, -6.8f)]
    public void Contains_player_entry_and_employee_workflow_points(float x, float y, float z)
    {
        Assert.True(FishWarehousePropertyBoundsDefinition.Contains(x, y, z));
    }

    [Theory]
    [InlineData(-12.31f, 0f, 6.15f)]
    [InlineData(-13.6f, 0f, 5.2f)]
    [InlineData(-13.6f, 0f, -0.8f)]
    public void Contains_the_west_door_employee_management_apron(float x, float y, float z)
    {
        Assert.True(FishWarehousePropertyBoundsDefinition.Contains(x, y, z));
    }

    [Theory]
    [InlineData(-15.6f, 0f, 0f)]
    [InlineData(0f, -0.6f, 0f)]
    [InlineData(0f, 6.4f, 0f)]
    [InlineData(0f, 0f, 7f)]
    public void Rejects_points_outside_the_gameplay_property_bounds(float x, float y, float z)
    {
        Assert.False(FishWarehousePropertyBoundsDefinition.Contains(x, y, z));
    }
}
