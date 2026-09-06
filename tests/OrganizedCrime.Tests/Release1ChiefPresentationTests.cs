using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefPresentationTests
{
    private const string PlayerId = "76561190000000001";

    private static Release1StoryState BaseStory() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState With(Release1ChiefRecord record) => BaseStory() with { ChiefRecord = record };

    private static Release1ChiefRecord Open(int round) => Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
    {
        State = Release1ChiefState.DemandOpen,
        DemandRound = round,
        Revision = 1
    };

    private static string Correlation(int round, string receiptId) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.ChiefCampbell, round, Release1TransitionKind.MissionOffered, receiptId).Value;

    [Fact]
    public void No_state_returns_the_empty_plan()
    {
        var plan = Release1ChiefPresentation.BuildPlan(null);

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Adopted_carries_no_message_and_no_decision()
    {
        var plan = Release1ChiefPresentation.BuildPlan(With(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)));

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Demand_open_carries_only_the_pay_or_decline_decision_and_no_passive_message()
    {
        // OC-73 review fix. DemandText used to be queued as a passive message as well as being the
        // decision's own prompt, so the boundary (which sends the prompt as a message on its own
        // behalf when nothing is bound yet) delivered it twice. DemandOpen now follows the same
        // convention the shipped missions already document (Release1PresentationPlan's own
        // BuildSmallCourtesy comment): the prompt is the only place the offer text is sent.
        var record = Open(1);
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        Assert.Empty(plan.Messages);

        Assert.NotNull(plan.Decision);
        Assert.Equal("chief-campbell-demand-r1", plan.Decision!.Id);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText), plan.Decision.Prompt);
        Assert.Equal(2, plan.Decision.Options.Count);
        Assert.Contains(plan.Decision.Options, option => option.Command == Release1PresentationCommand.ChiefCampbellPay);
        Assert.Contains(plan.Decision.Options, option => option.Command == Release1PresentationCommand.ChiefCampbellDecline);
    }

    [Fact]
    public void The_decision_disappears_the_instant_the_state_leaves_demand_open()
    {
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefPaymentAccepted, "chief-pay-r1").Value;
        var paying = Open(1) with
        {
            State = Release1ChiefState.Paying,
            AcceptedLogicalCorrelations = new[] { authorization },
            NativeEffectIds = new[] { "chief-campbell-cash-v1-r1" }
        };

        var plan = Release1ChiefPresentation.BuildPlan(With(paying));

        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Declined_carries_the_decline_reply()
    {
        var record = Open(1) with { State = Release1ChiefState.Declined, DeclineRepliesSent = 1 };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        var message = Assert.Single(plan.Messages);
        // OC-73 review fix (finding 1). Keyed on DeclineRepliesSent, a non-resetting counter (not
        // DemandRound): DemandRound resets to 1 on the Adopted-or-Settled arrest branch
        // (Release1ChiefLedger.Observe), which used to collide a later payoff cycle's decline reply
        // with an earlier cycle's already-delivered one at the same round. See
        // Release1ChiefPresentation.DeclineReceipt's own comment.
        Assert.Equal(Correlation(1, "chief-decline-1"), message.CorrelationId);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText), message.Text);
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void The_decline_reply_correlation_does_not_move_when_a_watch_line_bumps_revision_underneath_it()
    {
        // OC-73 review fix (finding 1). A rising crossing observed while still Declined bumps the
        // record's Revision without leaving Declined (Release1ChiefLedger.Observe's watch and
        // lockdown branches), and DemandRound can also fall back to 1 on a later ladder restart. The
        // reply is keyed on DeclineRepliesSent alone, so it must stay identical here even though
        // Revision differs from the pre-watch-line record.
        var beforeWatchLine = Open(1) with { State = Release1ChiefState.Declined, DeclineRepliesSent = 1, Revision = 1 };
        var afterWatchLine = beforeWatchLine with { WatchLinesSent = 1, Revision = 2 };

        var before = Release1ChiefPresentation.BuildPlan(With(beforeWatchLine)).Messages
            .Single(m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));
        var after = Release1ChiefPresentation.BuildPlan(With(afterWatchLine)).Messages
            .Single(m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));

        Assert.Equal(before.CorrelationId, after.CorrelationId);
    }

    [Fact]
    public void A_second_decline_after_a_ladder_restart_earns_its_own_distinct_correlation()
    {
        // OC-73 review fix (finding 1), the core regression: decline, pay, settle, arrest (restarting
        // the ladder at round 1), decline again. Both declines land at round 1, but DeclineRepliesSent
        // never resets, so the second reply must carry a correlation distinct from the first's rather
        // than colliding with it and being silently dropped.
        var firstCycleDecline = Open(1) with { State = Release1ChiefState.Declined, DeclineRepliesSent = 1 };
        var secondCycleDecline = Open(1) with { State = Release1ChiefState.Declined, DeclineRepliesSent = 2 };

        var first = Release1ChiefPresentation.BuildPlan(With(firstCycleDecline)).Messages
            .Single(m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));
        var second = Release1ChiefPresentation.BuildPlan(With(secondCycleDecline)).Messages
            .Single(m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));

        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
    }

    [Fact]
    public void Settled_carries_the_paid_text()
    {
        var record = Open(1) with { State = Release1ChiefState.Settled };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        var message = Assert.Single(plan.Messages);
        // OC-73 review fix. Same revision fold as the decline receipt, so a payoff that settles at a
        // round already used by an earlier cycle never collides with that earlier cycle's receipt.
        Assert.Equal(Correlation(1, "chief-paid-r1-c1"), message.CorrelationId);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaidText), message.Text);
    }

    [Fact]
    public void A_counter_at_zero_contributes_no_message()
    {
        var plan = Release1ChiefPresentation.BuildPlan(With(Open(1)));

        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.WatchListText));
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText));
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftPaidText));
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaymentBlockedText));
    }

    [Fact]
    public void Watch_and_lockdown_lines_are_carried_together_with_their_own_correlations()
    {
        var record = Open(1) with { WatchLinesSent = 1, LockdownAnnouncements = 2 };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-watch-1") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.WatchListText));
        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-lockdown-2") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText));
    }

    [Fact]
    public void Lift_lines_are_carried_with_their_own_correlations()
    {
        var record = Open(1) with { PaidLifts = 1, CooledLifts = 2 };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-lift-paid-1") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftPaidText));
        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-lift-cooled-2") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));
    }

    [Fact]
    public void A_payment_blocked_notice_is_carried_with_its_own_correlation()
    {
        // OC-73 review fix (finding 2). PaymentBlockedNotices is the third counter-keyed line added
        // by the review: the one Chief message telling the player a permanently blocked payment could
        // not be confirmed and the demand still stands.
        var record = Open(1) with { PaymentBlockedNotices = 1 };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-payment-blocked-1") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaymentBlockedText));
    }

    [Fact]
    public void A_noticed_shortfall_carries_the_short_cash_line_keyed_on_its_own_amount()
    {
        var record = Open(1) with { LastShortfallNoticed = 5000d };
        var plan = Release1ChiefPresentation.BuildPlan(With(record));

        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-short-5000") && m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.ShortCashText(record.Demand)));
    }

    [Fact]
    public void A_message_already_recorded_as_a_receipt_is_still_in_the_plan()
    {
        // OC-73 review fix. DemandOpen (the state this test used to exercise) no longer contributes
        // any passive message at all, so the state that still proves "the projector, not this
        // planner, dedupes" is Declined, whose reply is still a queued message with its own receipt.
        var record = Open(1) with { State = Release1ChiefState.Declined, DeclineRepliesSent = 1 };
        var story = With(record) with { PresentationReceipts = new[] { new Release1PresentationReceipt(Correlation(1, "chief-decline-1"), 1) } };

        var plan = Release1ChiefPresentation.BuildPlan(story);

        Assert.Contains(plan.Messages, m => m.CorrelationId == Correlation(1, "chief-decline-1"));
    }

    [Fact]
    public void Quests_are_always_empty()
    {
        foreach (var record in new[]
                 {
                     Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false),
                     Open(1),
                     Open(1) with { State = Release1ChiefState.Declined },
                     Open(1) with { State = Release1ChiefState.Settled }
                 })
        {
            Assert.Empty(Release1ChiefPresentation.BuildPlan(With(record)).Quests);
        }
    }

    [Fact]
    public void The_lockdown_announcement_correlation_matches_the_planners_own()
    {
        var record = Open(1) with { LockdownAnnouncements = 3 };

        Assert.Equal(Correlation(1, "chief-lockdown-3"), Release1ChiefPresentation.LockdownAnnouncementCorrelation(PlayerId, record));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Counter_keyed_lines_and_the_lockdown_announcement_correlation_do_not_move_when_the_round_advances(int laterRound)
    {
        // OC-73 review fix. Watch, lockdown, lift paid and lift cooled lines are each keyed on their
        // own counter, which never resets, so the round must never be folded into their correlation:
        // an arrest on a standing decline (or the Settled-plus-arrest ladder restart) advances or
        // resets DemandRound while the counters themselves hold still, and used to mint a fresh
        // correlation for an already-delivered line, resending it (and, for the lockdown line,
        // deferring Release1ChiefService's own engage until the duplicate announcement was receipted,
        // since HasAnnouncementReceipt looked for the correlation the projector had already recorded
        // under the earlier round).
        var atFirstRound = Open(1) with
        {
            DeclineRepliesSent = 1, WatchLinesSent = 1, LockdownAnnouncements = 1, PaidLifts = 1,
            CooledLifts = 1, PaymentBlockedNotices = 1
        };
        var atLaterRound = atFirstRound with { DemandRound = laterRound };

        var firstPlan = Release1ChiefPresentation.BuildPlan(With(atFirstRound));
        var laterPlan = Release1ChiefPresentation.BuildPlan(With(atLaterRound));

        foreach (var text in new[]
                 {
                     Release1ChiefCampbellCopy.DeclineReplyText,
                     Release1ChiefCampbellCopy.WatchListText,
                     Release1ChiefCampbellCopy.LockdownAnnouncementText,
                     Release1ChiefCampbellCopy.LiftPaidText,
                     Release1ChiefCampbellCopy.LiftCooledText,
                     Release1ChiefCampbellCopy.PaymentBlockedText
                 })
        {
            var normalized = Release1PlayerCopy.Normalize(text);
            var before = firstPlan.Messages.Single(m => m.Text == normalized).CorrelationId;
            var after = laterPlan.Messages.Single(m => m.Text == normalized).CorrelationId;
            Assert.Equal(before, after);
        }

        Assert.Equal(
            Release1ChiefPresentation.LockdownAnnouncementCorrelation(PlayerId, atFirstRound),
            Release1ChiefPresentation.LockdownAnnouncementCorrelation(PlayerId, atLaterRound));
    }
}
