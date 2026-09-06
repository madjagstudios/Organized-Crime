using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// The mechanical proof that lifting the two shipped windows onto Release1ConditionWindow changed
/// nothing. Each half compares the live shim against a verbatim copy of that window's pre-lift decision
/// table over the cross product of every observation, every progress shape the shipped test files use,
/// and a spread of times around both boundaries. This is what makes it safe to leave
/// Release1QuietWindowTests and Release1HoldWindowTests unedited.
/// </summary>
public sealed class Release1ConditionWindowShimEquivalenceTests
{
    private const double Window = 1_440d;

    private static readonly double[] Times =
    {
        0d, 1d, 100d, 500d, 559.99d, 560d, 560.01d, 1_000d,
        1_439.99d, 1_440d, 1_440.01d, 1_539.99d, 1_540d, 1_540.01d, 9_000d, 100_000d
    };

    // The pre-lift Release1QuietWindow.Decide body, copied verbatim.
    private static Release1QuietDecision ShippedQuietDecide(
        Release1QuietCensusObservation observation,
        Release1KeepTheLightsOffProgress progress,
        double nowGameMinutes,
        double windowDurationGameMinutes)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!double.IsFinite(nowGameMinutes) || nowGameMinutes < 0d ||
            !double.IsFinite(windowDurationGameMinutes) || windowDurationGameMinutes <= 0d)
            return Release1QuietDecision.Hold;

        if (observation == Release1QuietCensusObservation.NotReady) return Release1QuietDecision.Hold;

        if (observation == Release1QuietCensusObservation.Clear)
        {
            if (progress.ClearConfirmedAtGameMinutes is not { } confirmedAt) return Release1QuietDecision.ConfirmClear;
            if (progress.BreachSincePassGameMinutes is not null) return Release1QuietDecision.ClearBreach;
            return nowGameMinutes >= confirmedAt + windowDurationGameMinutes
                ? Release1QuietDecision.Complete
                : Release1QuietDecision.None;
        }

        if (progress.ClearConfirmedAtGameMinutes is null) return Release1QuietDecision.None;
        if (progress.BreachSincePassGameMinutes is not { } breachSince) return Release1QuietDecision.RecordBreach;
        return nowGameMinutes >= breachSince + 60d
            ? Release1QuietDecision.FailWindow
            : Release1QuietDecision.Hold;
    }

    private static IEnumerable<Release1KeepTheLightsOffProgress> QuietShapes()
    {
        // Every shape Release1KeepTheLightsOffProgress.Validate admits: a breach mark cannot exist
        // before a confirmed clear.
        yield return Quiet(null, null);
        yield return Quiet(0d, null);
        yield return Quiet(0d, 0d);
        yield return Quiet(100d, null);
        yield return Quiet(100d, 500d);
        yield return Quiet(500d, 500d);
    }

    private static Release1KeepTheLightsOffProgress Quiet(double? confirmedAt, double? breachSince) =>
        new(Release1MissionCatalog.KeepTheLightsOff, 1, confirmedAt, breachSince);

    [Fact]
    public void The_quiet_window_shim_matches_the_shipped_quiet_decision_table_everywhere()
    {
        var compared = 0;
        foreach (var observation in Enum.GetValues<Release1QuietCensusObservation>())
        foreach (var progress in QuietShapes())
        foreach (var now in Times)
        {
            compared++;
            Assert.Equal(
                ShippedQuietDecide(observation, progress, now, Window),
                Release1QuietWindow.Decide(observation, progress, now, Window));
        }

        Assert.Equal(3 * 6 * 16, compared);
    }

    [Theory]
    [InlineData(double.NaN, 1_440d)]
    [InlineData(double.PositiveInfinity, 1_440d)]
    [InlineData(-1d, 1_440d)]
    [InlineData(1_540d, 0d)]
    [InlineData(1_540d, -1_440d)]
    [InlineData(1_540d, double.NaN)]
    public void The_quiet_window_shim_matches_the_shipped_table_on_every_guard_row(double now, double window)
    {
        foreach (var observation in Enum.GetValues<Release1QuietCensusObservation>())
        foreach (var progress in QuietShapes())
            Assert.Equal(
                ShippedQuietDecide(observation, progress, now, window),
                Release1QuietWindow.Decide(observation, progress, now, window));
    }

    [Fact]
    public void The_quiet_window_shim_still_throws_on_a_null_progress_and_still_publishes_its_grace()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, null!, 500d, Window));
        Assert.Equal(60d, Release1QuietWindow.BreachGraceGameMinutes);
    }

    [Fact]
    public void A_breach_mark_without_a_confirmed_clear_is_not_a_state_the_progress_record_admits()
    {
        // The equivalence sweep above enumerates only shapes Validate admits. This pins that the
        // excluded shape really is excluded, so the sweep's coverage claim rests on the record's own
        // invariant rather than on the sweep's choice of shapes.
        var invalid = Quiet(null, 500d);

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    private const string ClosetA = "8ec9d63b-f0f7-4af9-86fb-f1c73c7af481";

    // The pre-lift Release1HoldWindow.Decide body, copied verbatim.
    private static Release1HoldDecision ShippedHoldDecide(
        Release1HoldObservation observation,
        Release1RoomWithNoNameProgress progress,
        double nowGameMinutes,
        double holdDurationGameMinutes)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!double.IsFinite(nowGameMinutes) || nowGameMinutes < 0d ||
            !double.IsFinite(holdDurationGameMinutes) || holdDurationGameMinutes <= 0d)
            return Release1HoldDecision.Hold;

        if (observation is Release1HoldObservation.RoomNotReady or Release1HoldObservation.Ambiguous)
            return Release1HoldDecision.Hold;

        if (observation == Release1HoldObservation.Held)
        {
            if (!progress.Stowed) return Release1HoldDecision.RecordStow;
            if (!progress.HoldSatisfied &&
                progress.StowedAtGameMinutes is { } stowedAt &&
                nowGameMinutes >= stowedAt + holdDurationGameMinutes)
                return Release1HoldDecision.SatisfyHold;
            return progress.MissingSincePassGameMinutes is not null
                ? Release1HoldDecision.ClearMissing
                : Release1HoldDecision.None;
        }

        if (!progress.Stowed || progress.HoldSatisfied) return Release1HoldDecision.None;
        if (progress.MissingSincePassGameMinutes is not { } missingSince) return Release1HoldDecision.RecordMissing;
        return nowGameMinutes >= missingSince + 60d
            ? Release1HoldDecision.FailHold
            : Release1HoldDecision.Hold;
    }

    private static IEnumerable<Release1RoomWithNoNameProgress> HoldShapes()
    {
        // Every shape Release1RoomWithNoNameProgress.Validate admits for a consignment already in
        // custody: a stow always carries a stow time and a holding closet, stow details never exist
        // before a stow, and the hold is never satisfied before the stow.
        yield return Hold(stowed: false, satisfied: false, stowedAt: null, missingSince: null);
        yield return Hold(stowed: true, satisfied: false, stowedAt: 0d, missingSince: null);
        yield return Hold(stowed: true, satisfied: false, stowedAt: 0d, missingSince: 0d);
        yield return Hold(stowed: true, satisfied: false, stowedAt: 100d, missingSince: null);
        yield return Hold(stowed: true, satisfied: false, stowedAt: 100d, missingSince: 500d);
        yield return Hold(stowed: true, satisfied: true, stowedAt: 100d, missingSince: null);
        yield return Hold(stowed: true, satisfied: true, stowedAt: 100d, missingSince: 500d);
    }

    private static Release1RoomWithNoNameProgress Hold(bool stowed, bool satisfied, double? stowedAt, double? missingSince) =>
        new(Release1MissionCatalog.RoomWithNoName, 1, true, true, stowed, satisfied, stowedAt,
            stowed ? ClosetA : null, missingSince);

    [Fact]
    public void The_hold_window_shim_matches_the_shipped_hold_decision_table_everywhere()
    {
        var compared = 0;
        foreach (var observation in Enum.GetValues<Release1HoldObservation>())
        foreach (var progress in HoldShapes())
        foreach (var now in Times)
        {
            compared++;
            Assert.Equal(
                ShippedHoldDecide(observation, progress, now, Window),
                Release1HoldWindow.Decide(observation, progress, now, Window));
        }

        Assert.Equal(4 * 7 * 16, compared);
    }

    [Theory]
    [InlineData(double.NaN, 1_440d)]
    [InlineData(double.PositiveInfinity, 1_440d)]
    [InlineData(-1d, 1_440d)]
    [InlineData(1_540d, 0d)]
    [InlineData(1_540d, -1_440d)]
    [InlineData(1_540d, double.NaN)]
    public void The_hold_window_shim_matches_the_shipped_table_on_every_guard_row(double now, double hold)
    {
        foreach (var observation in Enum.GetValues<Release1HoldObservation>())
        foreach (var progress in HoldShapes())
            Assert.Equal(
                ShippedHoldDecide(observation, progress, now, hold),
                Release1HoldWindow.Decide(observation, progress, now, hold));
    }

    [Fact]
    public void The_hold_window_shim_still_throws_on_a_null_progress_and_still_publishes_its_grace()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1HoldWindow.Decide(Release1HoldObservation.Held, null!, 500d, Window));
        Assert.Equal(60d, Release1HoldWindow.MissingGraceGameMinutes);
    }

    [Fact]
    public void A_stow_without_a_stow_time_is_not_a_state_the_progress_record_admits()
    {
        // The projection maps OpenedAtGameMinutes to StowedAtGameMinutes when Stowed and null
        // otherwise, so a record claiming Stowed with no stow time would project as a window that never
        // opened and would decide differently from the shipped table. That record cannot exist: Validate
        // rejects it, and the story runtime validates before it persists. This pins the invariant the
        // projection rests on, so the equivalence sweep's choice of shapes is bounded by the record
        // rather than by the test author.
        var stowedWithNoTime = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, null, ClosetA, null);
        var stowedWithNoCloset = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, 100d, null, null);
        var satisfiedWithoutAStow = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, false, true, null, null, null);

        Assert.Throws<ArgumentException>(() => stowedWithNoTime.Validate());
        Assert.Throws<ArgumentException>(() => stowedWithNoCloset.Validate());
        Assert.Throws<ArgumentException>(() => satisfiedWithoutAStow.Validate());
    }

    [Fact]
    public void The_two_shipped_policies_really_do_differ_on_the_heal_ordering_collision()
    {
        // The one genuine divergence the engine's policy dial exists to preserve. No shipped test pins
        // it, which is why it is proved here directly against both shims.
        var quiet = Quiet(confirmedAt: 100d, breachSince: 500d);
        var hold = Hold(stowed: true, satisfied: false, stowedAt: 100d, missingSince: 500d);

        Assert.Equal(Release1QuietDecision.ClearBreach,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, quiet, 5_000d, Window));
        Assert.Equal(Release1HoldDecision.SatisfyHold,
            Release1HoldWindow.Decide(Release1HoldObservation.Held, hold, 5_000d, Window));
    }
}
