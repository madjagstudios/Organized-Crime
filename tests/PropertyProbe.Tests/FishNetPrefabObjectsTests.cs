using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class FishNetPrefabObjectsTests
{
    [Fact]
    public void Report_records_prefab_objects_surface_without_claiming_mutation()
    {
        var snapshot = FishNetPrefabObjectsSnapshot.Test(
            prefabObjectsPresent: true,
            relevantMembers: new[] { "PrefabObjects::AddPrefab(GameObject)" });

        var text = FishNetPrefabObjectsFormatter.FormatText(snapshot);

        Assert.Contains("PREFAB_OBJECTS_PRESENT: True", text);
        Assert.Contains("PrefabObjects::AddPrefab(GameObject)", text);
        Assert.Contains("MUTATION_ATTEMPTED: False", text);
        Assert.Contains("INVOCATION_ATTEMPTED: False", text);
    }

    [Fact]
    public void Partial_report_keeps_all_mutation_boundaries_false()
    {
        var snapshot = FishNetPrefabObjectsSnapshot.Test();

        var text = FishNetPrefabObjectsFormatter.FormatText(snapshot);

        Assert.Contains("GATE: capability-partial", text);
        Assert.Contains("INVOCATION_ATTEMPTED: False", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PERSISTENCE_ATTEMPTED: False", text);
    }
}
