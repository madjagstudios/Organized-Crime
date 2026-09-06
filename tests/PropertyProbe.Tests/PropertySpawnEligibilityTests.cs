using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertySpawnEligibilityTests
{
    [Fact]
    public void Eligibility_report_does_not_claim_a_spawn_attempt()
    {
        var snapshot = PropertySpawnEligibilitySnapshot.Test(
            capturePassed: true,
            targetNetworked: true,
            spawnAttempted: false,
            cleanupPassed: true);

        var text = PropertySpawnEligibilityFormatter.FormatText(snapshot);

        Assert.Contains("METADATA_CAPTURE_PASSED: True", text);
        Assert.Contains("TARGET_IS_NETWORKED: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
        Assert.Contains("CLEANUP_PASSED: True", text);
    }
}
