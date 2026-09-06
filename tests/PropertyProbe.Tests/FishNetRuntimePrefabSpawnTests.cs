using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetRuntimePrefabSpawnTests
{
    [Fact]
    public void Report_separates_spawn_from_property_ownership()
    {
        var snapshot = FishNetRuntimePrefabSpawnSnapshot.Test(
            serverAvailable: true,
            spawnAttempted: true,
            spawnPassed: true,
            cleanupPassed: true);

        var text = FishNetRuntimePrefabSpawnFormatter.FormatText(snapshot);

        Assert.Contains("SERVER_AVAILABLE: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: True", text);
        Assert.Contains("SPAWN_PASSED: True", text);
        Assert.Contains("BUCKET_CLEANUP_PASSED: True", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PROPERTY_MUTATION_ATTEMPTED: False", text);
    }
}
