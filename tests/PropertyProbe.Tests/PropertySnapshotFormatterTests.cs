using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertySnapshotFormatterTests
{
    [Fact]
    public void Text_report_contains_stable_searchable_fields()
    {
        var property = PropertySnapshot.Test(
            code: "warehouse_candidate",
            employeeCapacity: 10,
            gridCount: 2,
            loadingDockCount: 2,
            hasContainer: true,
            employeeIdlePointCount: 10,
            configurableCount: 6) with
        {
            PropertyName = "Warehouse Candidate"
        };

        var score = PropertyCandidateRanker.Score(property);
        var text = PropertySnapshotFormatter.FormatText(new[] { property });

        Assert.Contains("PROPERTY: Warehouse Candidate", text);
        Assert.Contains("CODE: warehouse_candidate", text);
        Assert.Contains("TYPE: ScheduleOne.Property.Property", text);
        Assert.Contains("OWNED: false", text);
        Assert.Contains("EMPLOYEES: 0 / 10", text);
        Assert.Contains("GRIDS: 2", text);
        Assert.Contains("LOADING_DOCKS: 2", text);
        Assert.Contains($"WAREHOUSE_SCORE: {score.Warehouse}", text);
    }

    [Fact]
    public void Json_report_includes_property_and_loading_dock_identity()
    {
        var property = PropertySnapshot.Test(code: "dock_candidate", loadingDockCount: 1);

        var json = PropertySnapshotFormatter.FormatJson(new[] { property });

        Assert.Contains("\"propertyCode\": \"dock_candidate\"", json);
        Assert.Contains("\"transformPath\": \"World/dock_candidate\"", json);
        Assert.Contains("\"loadingDocks\"", json);
        Assert.Contains("\"guid\": \"dock-0\"", json);
        Assert.Contains("\"dock\"", json);
        Assert.Contains("\"warehouse\"", json);
    }

    [Fact]
    public void Reports_sort_by_property_code_for_repeatable_diffs()
    {
        var b = PropertySnapshot.Test(code: "b");
        var a = PropertySnapshot.Test(code: "a");

        var text = PropertySnapshotFormatter.FormatText(new[] { b, a });

        Assert.True(text.IndexOf("CODE: a", StringComparison.Ordinal) < text.IndexOf("CODE: b", StringComparison.Ordinal));
    }
}
