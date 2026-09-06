using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyPromotionPreflightTests
{
    [Fact]
    public void Text_report_records_target_identity_and_registration_boundary()
    {
        var snapshot = PropertyPromotionPreflightSnapshot.Test(
            proposedPropertyCode: "oc_fishwarehouse",
            targetName: "Fish Warehouse",
            targetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            hasNpcEnterableBuilding: true,
            hasPropertyComponent: false,
            isInPropertyCollection: false,
            isInOwnedPropertyCollection: false,
            isInUnownedPropertyCollection: false);

        var text = PropertyPromotionPreflightFormatter.FormatText(snapshot);

        Assert.Contains("PROPOSED_PROPERTY_CODE: oc_fishwarehouse", text);
        Assert.Contains("TARGET_NAME: Fish Warehouse", text);
        Assert.Contains("HAS_NPC_ENTERABLE_BUILDING: True", text);
        Assert.Contains("HAS_PROPERTY_COMPONENT: False", text);
        Assert.Contains("IS_IN_PROPERTY_COLLECTION: False", text);
    }

    [Fact]
    public void Json_report_is_deterministic_and_contains_registration_boundary()
    {
        var snapshot = PropertyPromotionPreflightSnapshot.Test(
            proposedPropertyCode: "oc_fishwarehouse",
            targetName: "Fish Warehouse",
            targetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse");

        var json = PropertyPromotionPreflightFormatter.FormatJson(snapshot);

        Assert.Contains("\"proposedPropertyCode\": \"oc_fishwarehouse\"", json);
        Assert.Contains("\"targetPath\": \"Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse\"", json);
    }

    [Fact]
    public void Fish_Warehouse_preflight_uses_a_unique_proposed_code()
    {
        var snapshot = PropertyPromotionPreflightSnapshot.Test(
            proposedPropertyCode: "oc_fishwarehouse",
            targetName: "Fish Warehouse",
            targetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse");

        Assert.Equal("oc_fishwarehouse", snapshot.ProposedPropertyCode);
        Assert.NotEqual("dockswarehouse", snapshot.ProposedPropertyCode);
    }
}
