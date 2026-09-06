using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class RuntimePropertyVerticalSliceTests
{
    [Fact]
    public void Report_distinguishes_property_runtime_readiness_from_ownership()
    {
        var snapshot = RuntimePropertyVerticalSliceSnapshot.Test(
            propertyRegistered: true,
            networkInitializePassed: true,
            spawnPassed: true,
            cleanupPassed: true);

        var text = RuntimePropertyVerticalSliceFormatter.FormatText(snapshot);

        Assert.Contains("PROPERTY_REGISTERED: True", text);
        Assert.Contains("NETWORK_INITIALIZE_PASSED: True", text);
        Assert.Contains("SPAWN_PASSED: True", text);
        Assert.Contains("CLEANUP_PASSED: True", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PERSISTENCE_ATTEMPTED: False", text);
    }
}
