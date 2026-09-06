using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyNetworkInitializationTests
{
    [Fact]
    public void Network_report_records_initialized_state_without_claiming_spawn()
    {
        var snapshot = PropertyNetworkInitializationSnapshot.Test(
            targetNetworkObjectPresentAfter: true,
            targetPropertyNetworkObjectResolvedAfter: true,
            targetPropertyNetworkInitializedAfter: true,
            targetPropertyClientInitializedAfter: false,
            targetPropertyServerInitializedAfter: false,
            targetNetworkObjectSpawnedAfter: false);

        var text = PropertyNetworkInitializationFormatter.FormatText(snapshot);

        Assert.Contains("TARGET_NETWORK_OBJECT_PRESENT_AFTER: True", text);
        Assert.Contains("TARGET_PROPERTY_NETWORK_INITIALIZED_AFTER: True", text);
        Assert.Contains("TARGET_NETWORK_OBJECT_SPAWNED_AFTER: False", text);
    }
}
