using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefRecordTests
{
    private static Release1LogicalCorrelation Correlation(int round, Release1TransitionKind kind = Release1TransitionKind.ChiefPaymentAccepted) =>
        Release1LogicalCorrelation.Create("player-1", Release1MissionCatalog.ChiefCampbell, round, kind, $"chief-pay-r{round}");

    private static Release1ChiefRecord Open(int round) =>
        Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
        {
            State = Release1ChiefState.DemandOpen,
            DemandRound = round,
            Revision = 1
        };

    private static Release1ChiefRecord Paying(int round) => Open(round) with
    {
        State = Release1ChiefState.Paying,
        AcceptedLogicalCorrelations = new[] { Correlation(round).Value },
        NativeEffectIds = new[] { $"chief-campbell-cash-v1-r{round}" }
    };

    [Fact]
    public void Adopt_sets_the_observed_tier_and_flag_at_round_zero()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Watched, true);

        Assert.True(record.Adopted);
        Assert.Equal(LocalPressureTier.Watched, record.AdoptedTier);
        Assert.True(record.AdoptedKnownOffender);
        Assert.Equal(0, record.DemandRound);
        Assert.Equal(Release1ChiefState.Adopted, record.State);
        Assert.False(record.LockdownEngaged);
        Assert.Equal(0, record.WatchLinesSent);
        Assert.Equal(0, record.LockdownAnnouncements);
        Assert.Equal(0, record.PaidLifts);
        Assert.Equal(0, record.CooledLifts);
        Assert.Equal(0, record.DeclineRepliesSent);
        Assert.Equal(0, record.PaymentBlockedNotices);
        Assert.Empty(record.AcceptedLogicalCorrelations);
        Assert.Empty(record.NativeEffectIds);
        Assert.Equal(0, record.Revision);
    }

    [Theory]
    [InlineData(1, 15000)]
    [InlineData(2, 20000)]
    [InlineData(3, 25000)]
    public void Demand_reads_the_ladder(int round, int expected)
    {
        Assert.Equal(expected, Open(round).Demand);
    }

    [Fact]
    public void The_scope_key_is_widened_but_never_a_mission()
    {
        Assert.Equal(6, Release1MissionCatalog.All.Count);
        Assert.False(Release1MissionCatalog.IsMissionKey(Release1MissionCatalog.ChiefCampbell));
        Assert.True(Release1MissionCatalog.IsEffectScopeKey(Release1MissionCatalog.ChiefCampbell));
        Assert.True(Release1MissionCatalog.AllowsRevertTolerantEffects(Release1MissionCatalog.ChiefCampbell));

        var correlation = Correlation(1);
        Assert.Equal(Release1MissionCatalog.ChiefCampbell, correlation.MissionKey);
        Assert.Equal(Release1TransitionKind.ChiefPaymentAccepted, correlation.TransitionKind);

        Assert.False(Release1LogicalCorrelation.TryParse(
            $"oc10/v1/player-1/{Release1MissionCatalog.ChiefCampbell}/0/{Release1TransitionKind.ChiefPaymentAccepted}/chief-pay-r0",
            out _));
    }

    [Fact]
    public void Round_zero_is_exactly_the_adopted_state_in_both_directions()
    {
        Assert.Throws<ArgumentException>(() => (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { State = Release1ChiefState.DemandOpen }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { State = Release1ChiefState.Adopted }).Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Demand_round_outside_the_ladder_throws(int round)
    {
        Assert.Throws<ArgumentException>(() => (Open(1) with { DemandRound = round }).Validate());
    }

    [Fact]
    public void Paying_requires_its_authorization_and_its_effect()
    {
        var paying = Paying(2);
        paying.Validate();

        Assert.Throws<ArgumentException>(() => (Open(2) with { State = Release1ChiefState.Paying }).Validate());
    }

    [Fact]
    public void Paying_correlations_must_be_canonical_for_the_current_round()
    {
        Assert.Throws<ArgumentException>(() => (Paying(1) with { AcceptedLogicalCorrelations = new[] { Correlation(3).Value } }).Validate());
        Assert.Throws<ArgumentException>(() => (Paying(1) with { AcceptedLogicalCorrelations = new[] { Correlation(1, Release1TransitionKind.MissionAccepted).Value } }).Validate());
    }

    [Fact]
    public void Lockdown_engaged_requires_an_announcement_first()
    {
        Assert.Throws<ArgumentException>(() => (Open(1) with { LockdownEngaged = true }).Validate());
        (Open(1) with { LockdownAnnouncements = 1, LockdownEngaged = true }).Validate();
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_bad_shortfall_throws(double shortfall)
    {
        Assert.Throws<ArgumentException>(() => (Open(1) with { LastShortfallNoticed = shortfall }).Validate());
    }

    [Fact]
    public void Negative_counters_throw()
    {
        Assert.Throws<ArgumentException>(() => (Open(1) with { WatchLinesSent = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { LockdownAnnouncements = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { PaidLifts = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { CooledLifts = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { DeclineRepliesSent = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { PaymentBlockedNotices = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Open(1) with { Revision = -1 }).Validate());
    }

    [Fact]
    public void Value_equality_compares_every_field_including_both_collections()
    {
        var left = Paying(2) with { WatchLinesSent = 1 };
        var right = Paying(2) with { WatchLinesSent = 1 };
        var different = Paying(2) with { WatchLinesSent = 2 };

        Assert.True(left.ValueEquals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.ValueEquals(different));
    }
}
