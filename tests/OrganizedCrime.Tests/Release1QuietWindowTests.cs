using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1QuietWindowTests
{
    [Fact]
    public void NotReady_always_holds_regardless_of_progress()
    {
        var untouched = Progress(confirmedAt: null, breachSince: null);
        var midway = Progress(confirmedAt: 100d, breachSince: 200d);

        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.NotReady, untouched, 500d, 1_440d));
        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.NotReady, midway, 500d, 1_440d));
    }

    [Fact]
    public void Clear_with_no_confirmation_yet_confirms_the_clear()
    {
        var fresh = Progress(confirmedAt: null, breachSince: null);

        Assert.Equal(Release1QuietDecision.ConfirmClear,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, fresh, 500d, 1_440d));
    }

    [Fact]
    public void Clear_already_confirmed_with_the_window_not_elapsed_does_nothing()
    {
        var confirmed = Progress(confirmedAt: 100d, breachSince: null);

        Assert.Equal(Release1QuietDecision.None,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, 1_000d, 1_440d));
    }

    [Fact]
    public void Clear_already_confirmed_with_a_stale_breach_recorded_clears_the_breach()
    {
        var staleBreach = Progress(confirmedAt: 100d, breachSince: 500d);

        Assert.Equal(Release1QuietDecision.ClearBreach,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, staleBreach, 520d, 1_440d));
    }

    [Fact]
    public void Clear_already_confirmed_just_short_of_the_window_does_nothing()
    {
        var confirmed = Progress(confirmedAt: 100d, breachSince: null);

        Assert.Equal(Release1QuietDecision.None,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, 1_539.99d, 1_440d));
    }

    [Fact]
    public void Clear_already_confirmed_with_the_window_elapsed_completes()
    {
        var confirmed = Progress(confirmedAt: 100d, breachSince: null);

        Assert.Equal(Release1QuietDecision.Complete,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, 1_540d, 1_440d));
    }

    [Fact]
    public void Present_before_any_confirmation_does_nothing()
    {
        var fresh = Progress(confirmedAt: null, breachSince: null);

        Assert.Equal(Release1QuietDecision.None,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Present, fresh, 500d, 1_440d));
    }

    [Fact]
    public void Present_after_confirmation_with_no_breach_recorded_records_the_breach()
    {
        var confirmed = Progress(confirmedAt: 100d, breachSince: null);

        Assert.Equal(Release1QuietDecision.RecordBreach,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Present, confirmed, 500d, 1_440d));
    }

    [Fact]
    public void Present_after_confirmation_within_the_sixty_minute_grace_holds()
    {
        var breached = Progress(confirmedAt: 100d, breachSince: 500d);

        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Present, breached, 559.99d, 1_440d));
    }

    [Theory]
    [InlineData(560d)]
    [InlineData(5_000d)]
    public void Present_after_confirmation_past_the_sixty_minute_grace_fails_the_window(double now)
    {
        var breached = Progress(confirmedAt: 100d, breachSince: 500d);

        Assert.Equal(Release1QuietDecision.FailWindow,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Present, breached, now, 1_440d));
    }

    [Fact]
    public void Non_finite_or_negative_now_and_a_non_positive_window_duration_always_hold()
    {
        var confirmed = Progress(confirmedAt: 100d, breachSince: null);

        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, double.NaN, 1_440d));
        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, double.PositiveInfinity, 1_440d));
        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, -1d, 1_440d));
        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, 1_540d, 0d));
        Assert.Equal(Release1QuietDecision.Hold,
            Release1QuietWindow.Decide(Release1QuietCensusObservation.Clear, confirmed, 1_540d, -1_440d));
    }

    private static Release1KeepTheLightsOffProgress Progress(double? confirmedAt, double? breachSince) =>
        new(Release1MissionCatalog.KeepTheLightsOff, 1, confirmedAt, breachSince);
}
