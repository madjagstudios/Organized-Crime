using Xunit;

namespace OrganizedCrime.Tests;

// OC-147 characterization: a mission-service hold reports once per attempt and
// message, then re-arms at the load boundaries. Pinned before OnceLog replaces
// the five ReportHoldOnce copies; passes unchanged after.
public sealed class MissionHoldWarnOnceTests
{
    [Fact]
    public void Short_notice_hold_logs_once_then_rearms_after_a_load_cycle()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        var expected = $"Short Notice attempt {assignment.Attempt} is holding: " +
            "the handoff drop reported no slots at all; it has not finished initializing.";
        int Count() => harness.Logs.Count(line => line == expected);
        Assert.Equal(0, Count());

        harness.World.ClearDropSlots(assignment.HandoffDropGuid);

        harness.Service.ReconcileObservation();
        Assert.Equal(1, Count());

        harness.Service.ReconcileObservation();
        Assert.Equal(1, Count());

        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();
        harness.Service.ReconcileObservation();
        harness.Service.ReconcileObservation();
        Assert.Equal(2, Count());
    }
}
