using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseDockDeliveryPopulationLatchTests
{
    [Fact]
    public void A_successfully_populated_delivery_is_not_repopulated_after_the_dock_clears()
    {
        var latch = new FishWarehouseDockDeliveryPopulationLatch();

        Assert.False(latch.HasPopulated("delivery-a"));

        latch.MarkPopulated("delivery-a");

        Assert.True(latch.HasPopulated("delivery-a"));
    }

    [Fact]
    public void A_new_active_delivery_clears_the_previous_delivery_latch()
    {
        var latch = new FishWarehouseDockDeliveryPopulationLatch();
        latch.MarkPopulated("delivery-a");

        latch.ObserveActiveDelivery("delivery-b");

        Assert.False(latch.HasPopulated("delivery-a"));
        Assert.False(latch.HasPopulated("delivery-b"));
    }

    [Fact]
    public void No_active_delivery_clears_the_previous_delivery_latch()
    {
        var latch = new FishWarehouseDockDeliveryPopulationLatch();
        latch.MarkPopulated("delivery-a");

        latch.ObserveActiveDelivery(null);

        Assert.False(latch.HasPopulated("delivery-a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_delivery_ids_are_not_latched(string? deliveryId)
    {
        var latch = new FishWarehouseDockDeliveryPopulationLatch();

        latch.MarkPopulated(deliveryId!);

        Assert.False(latch.HasPopulated(deliveryId ?? string.Empty));
    }
}
