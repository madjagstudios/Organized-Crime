using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertySpawnExperimentTests
{
    [Fact]
    public void Spawn_report_separates_spawn_success_from_ownership_and_persistence()
    {
        var snapshot = PropertySpawnExperimentSnapshot.Test(
            serverAvailable: true,
            spawnAttempted: true,
            spawnPassed: true,
            targetNetworkObjectSpawnedAfter: true,
            cleanupPassed: true);

        var text = PropertySpawnExperimentFormatter.FormatText(snapshot);

        Assert.Contains("SERVER_AVAILABLE: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: True", text);
        Assert.Contains("SPAWN_PASSED: True", text);
        Assert.Contains("TARGET_NETWORK_OBJECT_SPAWNED_AFTER: True", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PERSISTENCE_ATTEMPTED: False", text);
    }
}
