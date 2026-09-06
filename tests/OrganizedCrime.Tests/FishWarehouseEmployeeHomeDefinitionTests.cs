using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeHomeDefinitionTests
{
    [Fact]
    public void Default_fixture_uses_the_vanilla_locker_and_a_valid_deterministic_guid()
    {
        var definition = FishWarehouseEmployeeHomeDefinition.Default;

        Assert.Equal("locker", definition.ItemId);
        Assert.Equal(0, definition.GridX);
        Assert.Equal(0, definition.GridY);
        Assert.Equal(0, definition.Rotation);
        Assert.Equal("4ce5b96a-39f5-4ac3-9a05-9fca7a6e8c51", definition.PlacementGuid);
    }

    [Fact]
    public void Default_fixture_is_not_a_property_or_docks_mutation()
    {
        var definition = FishWarehouseEmployeeHomeDefinition.Default;

        Assert.DoesNotContain("dockswarehouse", definition.ItemId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oc_fishwarehouse", definition.ItemId, StringComparison.OrdinalIgnoreCase);
    }
}
