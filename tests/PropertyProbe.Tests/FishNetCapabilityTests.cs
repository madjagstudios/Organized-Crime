using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetCapabilityTests
{
    [Fact]
    public void Capability_report_separates_prefab_registration_from_scene_identity()
    {
        var snapshot = FishNetCapabilitySnapshot.Test(
            serverManagerPresent: true,
            spawnablePrefabMemberFound: true,
            s1MApiLoaded: false);

        var text = FishNetCapabilityFormatter.FormatText(snapshot);

        Assert.Contains("SERVER_MANAGER_PRESENT: True", text);
        Assert.Contains("SPAWNABLE_PREFAB_MEMBER_FOUND: True", text);
        Assert.Contains("S1MAPI_LOADED: False", text);
        Assert.Contains("MUTATION_ATTEMPTED: False", text);
        Assert.Contains("SCENE_IDENTITY_REUSED: False", text);
    }
}
