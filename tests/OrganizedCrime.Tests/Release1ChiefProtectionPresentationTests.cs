using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefProtectionPresentationTests
{
    [Fact]
    public void Offered_record_shows_the_offer_and_the_buy_button()
    {
        var state = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionOffered = true });

        var plan = Release1ChiefPresentation.BuildPlan(state, today: 5);

        Assert.Contains(plan.Messages, m => m.Text == Release1ChiefCampbellCopy.ProtectionOfferText());
        var decision = plan.Decision;
        Assert.NotNull(decision);
        Assert.Equal("chief-campbell-protection", decision!.Id);
        Assert.Equal(Release1ChiefCampbellCopy.ProtectionOfferText(), decision.Prompt);
        var option = Assert.Single(decision.Options);
        Assert.Equal(Release1ChiefCampbellCopy.BuyProtectionLabel(), option.Label);
        Assert.Equal(Release1PresentationCommand.ChiefCampbellBuyProtection, option.Command);
    }

    [Fact]
    public void Covered_record_shows_the_confirmation_and_the_extend_button()
    {
        var state = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 12 });

        var plan = Release1ChiefPresentation.BuildPlan(state, today: 5);

        Assert.Contains(plan.Messages, m => m.Text == Release1ChiefCampbellCopy.ProtectionCoveredText(12));
        var decision = plan.Decision;
        Assert.NotNull(decision);
        Assert.Equal(Release1ChiefCampbellCopy.ProtectionCoveredText(12), decision!.Prompt);
        var option = Assert.Single(decision.Options);
        Assert.Equal(Release1ChiefCampbellCopy.ExtendProtectionLabel(), option.Label);
        Assert.Equal(Release1PresentationCommand.ChiefCampbellBuyProtection, option.Command);
    }

    [Fact]
    public void Button_hides_at_the_banking_cap_but_the_confirmation_stays()
    {
        var state = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 19 });

        var plan = Release1ChiefPresentation.BuildPlan(state, today: 5);

        Assert.Null(plan.Decision);
        Assert.Contains(plan.Messages, m => m.Text == Release1ChiefCampbellCopy.ProtectionCoveredText(19));
    }

    [Fact]
    public void Unpaid_record_withholds_the_button_for_the_demand()
    {
        var record = Release1ChiefLedger.Observe(
            Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionOffered = true },
            Release1ChiefObservedEvent.ArrestObserved).Next;
        var state = StoryWith(record);

        var plan = Release1ChiefPresentation.BuildPlan(state, today: 5);

        Assert.NotNull(plan.Decision);
        Assert.StartsWith("chief-campbell-demand-", plan.Decision!.Id);
        Assert.DoesNotContain(plan.Messages, m => m.Text == Release1ChiefCampbellCopy.ProtectionCoveredText(12));
    }

    [Fact]
    public void Warning_lapse_and_refusal_lines_show_once_per_key()
    {
        var warned = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 12, ProtectionWarnedForUntilDay = 12 });
        Assert.Contains(
            Release1ChiefPresentation.BuildPlan(warned, today: 11).Messages,
            m => m.Text == Release1ChiefCampbellCopy.ProtectionWarningText());

        var staleWarning = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 19, ProtectionWarnedForUntilDay = 12 });
        Assert.DoesNotContain(
            Release1ChiefPresentation.BuildPlan(staleWarning, today: 12).Messages,
            m => m.Text == Release1ChiefCampbellCopy.ProtectionWarningText());

        var lapsed = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectionLapseNotices = 2 });
        Assert.Contains(
            Release1ChiefPresentation.BuildPlan(lapsed, today: 30).Messages,
            m => m.Text == Release1ChiefCampbellCopy.ProtectionLapseText());

        var refused = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionSettleFirstRefusals = 1 });
        Assert.Contains(
            Release1ChiefPresentation.BuildPlan(refused, today: 5).Messages,
            m => m.Text == Release1ChiefCampbellCopy.ProtectionSettleFirstText());

        var blocked = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionBlockedNotices = 1 });
        Assert.Contains(
            Release1ChiefPresentation.BuildPlan(blocked, today: 5).Messages,
            m => m.Text == Release1ChiefCampbellCopy.ProtectionBlockedText);
    }

    [Fact]
    public void Protection_correlations_are_stable_across_rounds_and_revisions()
    {
        var first = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 12, ProtectionLapseNotices = 1, ProtectionSettleFirstRefusals = 1, Revision = 7 });
        var second = StoryWith(Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectionOffered = true, ProtectedUntilDay = 12, ProtectionLapseNotices = 1, ProtectionSettleFirstRefusals = 1, DemandRound = 2, Revision = 41 });

        var firstPlan = Release1ChiefPresentation.BuildPlan(first, today: 5);
        var secondPlan = Release1ChiefPresentation.BuildPlan(second, today: 5);

        foreach (var receipt in new[] { "chief-protection-offer", "chief-protection-covered-12", "chief-protection-lapsed-1", "chief-protection-refused-1" })
        {
            var a = Assert.Single(firstPlan.Messages, m => m.CorrelationId.EndsWith(receipt, StringComparison.Ordinal));
            var b = Assert.Single(secondPlan.Messages, m => m.CorrelationId.EndsWith(receipt, StringComparison.Ordinal));
            Assert.Equal(a.CorrelationId, b.CorrelationId);
        }
    }

    [Fact]
    public void Protection_copy_passes_the_player_copy_bar()
    {
        AssertPlayerCopy(Release1ChiefCampbellCopy.ProtectionOfferText());
        AssertPlayerCopy(Release1ChiefCampbellCopy.BuyProtectionLabel());
        AssertPlayerCopy(Release1ChiefCampbellCopy.ExtendProtectionLabel());
        AssertPlayerCopy(Release1ChiefCampbellCopy.ProtectionCoveredText(12));
        AssertPlayerCopy(Release1ChiefCampbellCopy.ProtectionWarningText());
        AssertPlayerCopy(Release1ChiefCampbellCopy.ProtectionLapseText());
        AssertPlayerCopy(Release1ChiefCampbellCopy.ProtectionSettleFirstText());
        Assert.Equal("You're making noise. $30,000 a week and my people look the other way. Offer stands.", Release1ChiefCampbellCopy.ProtectionOfferText());
        Assert.Equal("Buy protection ($30,000)", Release1ChiefCampbellCopy.BuyProtectionLabel());
        Assert.Equal("Extend protection ($30,000)", Release1ChiefCampbellCopy.ExtendProtectionLabel());
        Assert.Equal("You're covered until day 12.", Release1ChiefCampbellCopy.ProtectionCoveredText(12));
        Assert.Equal("Your cover ends tomorrow.", Release1ChiefCampbellCopy.ProtectionWarningText());
        Assert.Equal("You're on your own now.", Release1ChiefCampbellCopy.ProtectionLapseText());
        Assert.Equal("Settle your file first.", Release1ChiefCampbellCopy.ProtectionSettleFirstText());
    }

    private static void AssertPlayerCopy(string value)
    {
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
        Assert.DoesNotContain('\u2014', value); // U+2014 em dash
        Assert.DoesNotContain('\u2013', value); // U+2013 en dash
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2019', value); // U+2019 right single quote
        Assert.DoesNotContain('%', value);
    }

    private static Release1StoryState StoryWith(Release1ChiefRecord record)
    {
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        const string receipt = "chief-protection-presentation-seed";
        var correlation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
        Assert.True(story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);
        return story.State! with { ChiefRecord = record };
    }
}
