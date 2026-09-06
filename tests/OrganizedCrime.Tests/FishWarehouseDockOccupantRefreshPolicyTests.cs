using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseDockOccupantRefreshPolicyTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void Suppresses_only_occupant_clear_that_can_remove_an_active_warehouse_source(
        bool isFishWarehouseDock,
        bool hasDynamicOccupant,
        bool hasOutputItems,
        bool expected)
    {
        Assert.Equal(
            expected,
            FishWarehouseDockOccupantRefreshPolicy.ShouldSuppressOccupantClear(
                isFishWarehouseDock,
                hasDynamicOccupant,
                hasOutputItems));
    }
}
