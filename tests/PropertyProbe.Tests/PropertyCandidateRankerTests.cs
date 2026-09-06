using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyCandidateRankerTests
{
    [Fact]
    public void Warehouse_score_rewards_employee_build_delivery_capabilities()
    {
        var property = PropertySnapshot.Test(
            code: "warehouse_candidate",
            employeeCapacity: 10,
            gridCount: 2,
            loadingDockCount: 2,
            hasContainer: true,
            employeeIdlePointCount: 10,
            configurableCount: 6);

        var score = PropertyCandidateRanker.Score(property);

        Assert.True(score.Warehouse >= 80);
    }

    [Fact]
    public void Dock_score_rewards_loading_dock_infrastructure()
    {
        var withoutDock = PropertySnapshot.Test(code: "a", loadingDockCount: 0);
        var withDock = PropertySnapshot.Test(code: "b", loadingDockCount: 2);

        Assert.True(
            PropertyCandidateRanker.Score(withDock).Dock >
            PropertyCandidateRanker.Score(withoutDock).Dock);
    }

    [Fact]
    public void Scores_are_clamped_to_zero_through_one_hundred()
    {
        var property = PropertySnapshot.Test(
            code: "maxed",
            employeeCapacity: 50,
            gridCount: 20,
            loadingDockCount: 10,
            hasContainer: true,
            employeeIdlePointCount: 50,
            configurableCount: 50);

        var score = PropertyCandidateRanker.Score(property);

        Assert.InRange(score.Safehouse, 0, 100);
        Assert.InRange(score.Warehouse, 0, 100);
        Assert.InRange(score.Dock, 0, 100);
        Assert.InRange(score.Casino, 0, 100);
    }
}
