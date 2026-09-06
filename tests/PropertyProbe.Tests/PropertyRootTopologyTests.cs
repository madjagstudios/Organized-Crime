using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyRootTopologyTests
{
    [Fact]
    public void Topology_report_distinguishes_property_root_from_map_building()
    {
        var snapshot = PropertyRootTopologySnapshot.Test();

        var text = PropertyRootTopologyFormatter.FormatText(snapshot);

        Assert.Contains("PROPERTY_PATH: @Properties/DocksWarehouse", text);
        Assert.Contains("LOCAL_NETWORK_OBJECT_PRESENT: True", text);
        Assert.Contains("FISH_WAREHOUSE_LOCAL_NETWORK_OBJECT_PRESENT: False", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
        Assert.Contains("MUTATION_ATTEMPTED: False", text);
    }
}
