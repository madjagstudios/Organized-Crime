using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Task 4: completion once the shutdown window has fully elapsed. Nell's offer pays nothing on its
/// own terms, so completion is a single durable transition and nothing else: no cash mutation, no
/// native effect, no reward math. Reuses the offer/accept/census harness from
/// <see cref="Release1KeepTheLightsOffMissionServiceTests"/> and
/// <see cref="Release1KeepTheLightsOffCensusTests"/> so every stage transition and every
/// mission-key/attempt wiring stays identical to the rest of the suite.
/// </summary>
public sealed class Release1KeepTheLightsOffCompletionTests
{
    private const double WindowMinutes = Release1KeepTheLightsOffAssignment.WindowGameMinutes;

    [Fact]
    public void The_window_completes_exactly_once_with_no_cash_and_no_effect()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        var cashBefore = harness.World.CashBalance;
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        harness.World.TotalMinutes = 5_000d + WindowMinutes;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Completed, status);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.OnTime, harness.Mission().LastOutcome);
        Assert.Equal(70, harness.Story.State!.Standing);
        Assert.Empty(harness.Story.State.NativeEffects);
        Assert.Equal(cashBefore, harness.World.CashBalance);
        Assert.Equal(0, harness.World.CashChanges);

        var further = harness.Service.ReconcileCensus();
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NoWork, further);
    }

    public static IEnumerable<object[]> ActiveStageNames()
    {
        yield return new object[] { "Active" };
        yield return new object[] { "MakeGoodActive" };
        yield return new object[] { "RecoveryActive" };
    }

    // Theory data carries a stage name rather than a harness factory delegate: Harness is internal
    // (deliberately, so nothing outside the test assembly depends on its shape), and a public Theory
    // method cannot expose an internal type in its own signature.
    [Theory]
    [MemberData(nameof(ActiveStageNames))]
    public void Completion_fires_from_active_make_good_and_recovery_alike(string stage)
    {
        using var harness = stage switch
        {
            "Active" => Release1KeepTheLightsOffHarness.Active(),
            "MakeGoodActive" => Release1KeepTheLightsOffHarness.MakeGoodActive(),
            "RecoveryActive" => Release1KeepTheLightsOffHarness.RecoveryActive(),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage name.")
        };
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        var start = harness.World.TotalMinutes;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        harness.World.TotalMinutes = start + WindowMinutes;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Completed, status);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Empty(harness.Story.State!.NativeEffects);
    }

    [Fact]
    public void A_completed_attempt_cannot_complete_twice()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        var start = harness.World.TotalMinutes;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        harness.World.TotalMinutes = start + WindowMinutes;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Completed, harness.Service.ReconcileCensus());
        var revisionAfterCompletion = harness.Story.State!.Revision;

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NoWork, harness.Service.ReconcileCensus());
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NoWork, harness.Service.ReconcileCensus());

        Assert.Equal(revisionAfterCompletion, harness.Story.State!.Revision);
    }

    [Fact]
    public void Completion_reaches_Standing_70_and_offers_The_Envelope()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        var start = harness.World.TotalMinutes;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());
        harness.World.TotalMinutes = start + WindowMinutes;

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Completed, harness.Service.ReconcileCensus());

        Assert.Equal(70, harness.Story.State!.Standing);
        Assert.Equal(
            Release1MissionState.Offered,
            harness.Story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)].State);
    }
}
