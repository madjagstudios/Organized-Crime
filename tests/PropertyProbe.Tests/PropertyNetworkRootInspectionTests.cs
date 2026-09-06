using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyNetworkRootInspectionTests
{
    [Fact]
    public void Root_report_identifies_scene_network_object_without_claiming_mutation()
    {
        var snapshot = PropertyNetworkRootInspectionSnapshot.Test(
            capturePassed: true,
            localNetworkObjectPresent: true,
            sceneObject: true);

        var text = PropertyNetworkRootInspectionFormatter.FormatText(snapshot);

        Assert.Contains("METADATA_CAPTURE_PASSED: True", text);
        Assert.Contains("LOCAL_NETWORK_OBJECT_PRESENT: True", text);
        Assert.Contains("IS_SCENE_OBJECT: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
    }
}
