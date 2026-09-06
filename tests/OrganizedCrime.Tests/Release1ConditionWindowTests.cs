using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ConditionWindowTests
{
    private static readonly Release1WindowProfile Heal =
        new(1_440d, 60d, Release1WindowHealPolicy.HealBeforeElapse);

    private static readonly Release1WindowProfile Elapse =
        new(1_440d, 60d, Release1WindowHealPolicy.ElapseBeforeHeal);

    private static Release1WindowProgress Progress(double? openedAt, double? breachSince, bool satisfied = false) =>
        new(openedAt, breachSince, satisfied);

    // Rule 1.
    [Theory]
    [InlineData(double.NaN, 1_440d, 60d)]
    [InlineData(double.PositiveInfinity, 1_440d, 60d)]
    [InlineData(double.NegativeInfinity, 1_440d, 60d)]
    [InlineData(-1d, 1_440d, 60d)]
    [InlineData(500d, 0d, 60d)]
    [InlineData(500d, -1_440d, 60d)]
    [InlineData(500d, double.NaN, 60d)]
    [InlineData(500d, double.PositiveInfinity, 60d)]
    [InlineData(500d, 1_440d, 0d)]
    [InlineData(500d, 1_440d, -60d)]
    [InlineData(500d, 1_440d, double.NaN)]
    [InlineData(500d, 1_440d, double.PositiveInfinity)]
    public void Rule_one_holds_on_a_bad_clock_a_bad_window_or_a_bad_grace(double now, double window, double grace)
    {
        var profile = new Release1WindowProfile(window, grace, Release1WindowHealPolicy.HealBeforeElapse);

        // Every observation and every progress shape reaches the same guard.
        Assert.Equal(Release1WindowDecision.Hold,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(null, null), now, profile));
        Assert.Equal(Release1WindowDecision.Hold,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, Progress(100d, 500d), now, profile));
    }

    // Rule 2.
    [Fact]
    public void Rule_two_holds_on_an_unreadable_observation_whatever_the_progress()
    {
        Assert.Equal(Release1WindowDecision.Hold,
            Release1ConditionWindow.Decide(Release1WindowObservation.Unreadable, Progress(null, null), 500d, Heal));
        Assert.Equal(Release1WindowDecision.Hold,
            Release1ConditionWindow.Decide(Release1WindowObservation.Unreadable, Progress(100d, 200d, true), 100_000d, Elapse));
    }

    // Rule 3.
    [Fact]
    public void Rule_three_opens_the_window_on_the_first_clean_pass()
    {
        Assert.Equal(Release1WindowDecision.OpenWindow,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(null, null), 500d, Heal));
        Assert.Equal(Release1WindowDecision.OpenWindow,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(null, null), 0d, Elapse));
    }

    // Rule 4 against rule 5: the heal ordering collision, the highest risk row in the lift.
    [Fact]
    public void Rules_four_and_five_split_the_heal_ordering_collision_by_policy()
    {
        var staleBreachOnAnElapsedWindow = Progress(100d, 500d);

        Assert.Equal(Release1WindowDecision.ClearBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, staleBreachOnAnElapsedWindow, 5_000d, Heal));
        Assert.Equal(Release1WindowDecision.Elapsed,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, staleBreachOnAnElapsedWindow, 5_000d, Elapse));
    }

    // Rule 5, and the window boundary from a zero and a non zero origin.
    [Theory]
    [InlineData(0d, 1_439.99d, Release1WindowDecision.None)]
    [InlineData(0d, 1_440d, Release1WindowDecision.Elapsed)]
    [InlineData(0d, 1_440.01d, Release1WindowDecision.Elapsed)]
    [InlineData(100d, 1_539.99d, Release1WindowDecision.None)]
    [InlineData(100d, 1_540d, Release1WindowDecision.Elapsed)]
    [InlineData(100d, 100_000d, Release1WindowDecision.Elapsed)]
    public void Rule_five_and_rule_seven_pin_the_window_boundary_on_both_policies(
        double openedAt, double now, Release1WindowDecision expected)
    {
        var open = Progress(openedAt, null);

        Assert.Equal(expected, Release1ConditionWindow.Decide(Release1WindowObservation.Clean, open, now, Heal));
        Assert.Equal(expected, Release1ConditionWindow.Decide(Release1WindowObservation.Clean, open, now, Elapse));
    }

    // Rule 6.
    [Fact]
    public void Rule_six_heals_a_stale_breach_under_elapse_before_heal_while_the_window_still_runs()
    {
        var staleBreach = Progress(100d, 500d);

        Assert.Equal(Release1WindowDecision.ClearBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, staleBreach, 520d, Elapse));
    }

    // Rule 7.
    [Fact]
    public void Rule_seven_does_nothing_on_a_clean_pass_inside_an_open_unbreached_window()
    {
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(100d, null), 1_000d, Heal));
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(100d, null), 1_000d, Elapse));
    }

    // Rule 8.
    [Fact]
    public void Rule_eight_ignores_a_breach_before_the_window_ever_opened()
    {
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, Progress(null, null), 5_000d, Heal));
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, Progress(null, null), 5_000d, Elapse));
    }

    // Rule 9.
    [Fact]
    public void Rule_nine_ignores_a_breach_once_the_condition_is_satisfied()
    {
        var satisfied = Progress(100d, null, satisfied: true);
        var satisfiedAndMarked = Progress(100d, 500d, satisfied: true);

        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, satisfied, 100_000d, Elapse));
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, satisfiedAndMarked, 100_000d, Elapse));
    }

    // Rule 10.
    [Fact]
    public void Rule_ten_records_the_first_breach_pass_and_never_fails_on_it()
    {
        Assert.Equal(Release1WindowDecision.RecordBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, Progress(100d, null), 500d, Heal));
        Assert.Equal(Release1WindowDecision.RecordBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, Progress(100d, null), 500d, Elapse));
    }

    // Rules 11 and 12, and the grace boundary from a zero and a non zero mark.
    [Theory]
    [InlineData(0d, 59.99d, Release1WindowDecision.Hold)]
    [InlineData(0d, 60d, Release1WindowDecision.Fail)]
    [InlineData(0d, 60.01d, Release1WindowDecision.Fail)]
    [InlineData(500d, 559.99d, Release1WindowDecision.Hold)]
    [InlineData(500d, 560d, Release1WindowDecision.Fail)]
    [InlineData(500d, 100_000d, Release1WindowDecision.Fail)]
    public void Rules_eleven_and_twelve_pin_the_grace_boundary_on_both_policies(
        double breachSince, double now, Release1WindowDecision expected)
    {
        var marked = Progress(0d, breachSince);

        Assert.Equal(expected, Release1ConditionWindow.Decide(Release1WindowObservation.Breached, marked, now, Heal));
        Assert.Equal(expected, Release1ConditionWindow.Decide(Release1WindowObservation.Breached, marked, now, Elapse));
    }

    [Fact]
    public void Satisfied_suppresses_elapsing_and_failing_but_still_permits_a_heal()
    {
        var satisfiedPastTheWindow = Progress(100d, null, satisfied: true);
        var satisfiedPastTheGrace = Progress(100d, 500d, satisfied: true);

        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, satisfiedPastTheWindow, 100_000d, Elapse));
        Assert.Equal(Release1WindowDecision.None,
            Release1ConditionWindow.Decide(Release1WindowObservation.Breached, satisfiedPastTheGrace, 100_000d, Elapse));
        Assert.Equal(Release1WindowDecision.ClearBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, satisfiedPastTheGrace, 100_000d, Elapse));
        Assert.Equal(Release1WindowDecision.ClearBreach,
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, satisfiedPastTheGrace, 100_000d, Heal));
    }

    [Fact]
    public void A_pass_repeated_inside_the_same_game_minute_reaches_the_same_decision()
    {
        var open = Progress(100d, null);

        var first = Release1ConditionWindow.Decide(Release1WindowObservation.Clean, open, 1_539.2d, Heal);
        var second = Release1ConditionWindow.Decide(Release1WindowObservation.Clean, open, 1_539.9d, Heal);

        Assert.Equal(Release1WindowDecision.None, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void A_null_progress_or_a_null_profile_throws_rather_than_deciding()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, null!, 500d, Heal));
        Assert.Throws<ArgumentNullException>(() =>
            Release1ConditionWindow.Decide(Release1WindowObservation.Clean, Progress(null, null), 500d, null!));
    }
}
