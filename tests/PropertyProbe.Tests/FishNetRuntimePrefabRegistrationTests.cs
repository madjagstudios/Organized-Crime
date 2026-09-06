using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetRuntimePrefabRegistrationTests
{
    [Fact]
    public void Report_separates_registration_from_spawn_and_ownership()
    {
        var snapshot = FishNetRuntimePrefabRegistrationSnapshot.Test(
            addObjectAttempted: true,
            registrationPassed: true,
            cleanupPassed: true);

        var text = FishNetRuntimePrefabRegistrationFormatter.FormatText(snapshot);

        Assert.Contains("ADD_OBJECT_ATTEMPTED: True", text);
        Assert.Contains("REGISTRATION_PASSED: True", text);
        Assert.Contains("CLEANUP_PASSED: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PERSISTENCE_ATTEMPTED: False", text);
    }
}
