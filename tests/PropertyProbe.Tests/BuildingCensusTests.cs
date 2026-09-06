using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class BuildingCensusTests
{
    [Fact]
    public void Name_matching_finds_bait_and_tackle_terms_case_insensitively()
    {
        Assert.True(BuildingCensusMatcher.MatchesName("Randy's Bait & Tackle", "randy", "tackle"));
        Assert.True(BuildingCensusMatcher.MatchesName("Brick Warehouse", "brick", "warehouse"));
        Assert.False(BuildingCensusMatcher.MatchesName("DocksWarehouse", "randy", "tackle"));
    }

    [Fact]
    public void Text_report_preserves_candidate_and_component_evidence()
    {
        var snapshot = BuildingCensusSnapshot.Test(
            candidate: "Near Docks Warehouse",
            gameObjectName: "UnnamedShell",
            componentTypes: new[] { "UnityEngine.MeshRenderer", "Il2CppScheduleOne.Property.Property" },
            hasPropertyComponent: true,
            distanceFromReference: 18.5f,
            deliveryLocationName: "BrickWarehouseDocks",
            deliveryLocationDescription: "Brick warehouse at the docks");

        var text = BuildingCensusFormatter.FormatText(new[] { snapshot });

        Assert.Contains("CANDIDATE: Near Docks Warehouse", text);
        Assert.Contains("GAME_OBJECT: UnnamedShell", text);
        Assert.Contains("DISTANCE_FROM_DOCKS_WAREHOUSE: 18.5", text);
        Assert.Contains("Il2CppScheduleOne.Property.Property", text);
        Assert.Contains("HAS_PROPERTY_COMPONENT: True", text);
        Assert.Contains("DELIVERY_LOCATION_NAME: BrickWarehouseDocks", text);
        Assert.Contains("DELIVERY_LOCATION_DESCRIPTION: Brick warehouse at the docks", text);
    }
}
