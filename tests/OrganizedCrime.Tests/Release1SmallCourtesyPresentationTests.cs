using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyPresentationTests
{
    [Theory]
    [InlineData(Release1SmallCourtesyAssignmentMode.Primary, null, "one day")]
    [InlineData(Release1SmallCourtesyAssignmentMode.MakeGood, "Make good, attempt", "one day")]
    [InlineData(Release1SmallCourtesyAssignmentMode.Recovery, "Recovery, attempt", "none")]
    public void Persistent_card_names_the_assignment_without_repeating_review_only_terms(
        Release1SmallCourtesyAssignmentMode mode,
        string? headerPhrase,
        string deadline)
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission(mode: mode);

        var view = Release1SmallCourtesyPresentation.Build(
            harness.Story.State,
            harness.Story.LastPersistedRevision,
            harness.Service.OfferStatus,
            harness.Service.ReviewedQuote);

        Assert.True(view.Visible);
        AssertStatusFirstAndCompact(view);
        Assert.Contains($"Status: Deliver", view.Body, StringComparison.Ordinal);
        Assert.Contains(harness.Assignment.ProductName, view.Body, StringComparison.Ordinal);
        Assert.Contains(harness.Assignment.PackagingName, view.Body, StringComparison.Ordinal);
        Assert.Contains(harness.Assignment.DeadDropName, view.Body, StringComparison.Ordinal);
        Assert.Contains($"Deadline: {deadline}", view.Body, StringComparison.Ordinal);
        // The Active/status card never shows an attempt header, regardless of mode.
        Assert.DoesNotContain("attempt", view.Body, StringComparison.Ordinal);
        if (headerPhrase is not null) Assert.DoesNotContain(headerPhrase, view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Selection reference", view.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("empty when I picked it", view.Body, StringComparison.OrdinalIgnoreCase);
        AssertPlayerCopy(view.Title);
        AssertPlayerCopy(view.Body);
    }

    [Fact]
    public void Review_card_retains_the_exact_drop_and_payment_terms()
    {
        using var harness = OfferedMission();
        var presenter = new Release1SmallCourtesyPresenter(harness.Service, harness.Story);

        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, presenter.TryReview().Status);
        var view = presenter.View;

        Assert.True(view.CanAccept);
        // Primary is the first offer: no attempt header at all (section 3 of the OC-56 spec).
        Assert.DoesNotContain("attempt", view.Body, StringComparison.Ordinal);
        Assert.Contains("empty when I picked it.", view.Body, StringComparison.Ordinal);
        Assert.Contains("I pay 125 percent of what you actually leave, not the asking price.", view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Are you in?", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Selection reference", view.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is empty", view.Body, StringComparison.OrdinalIgnoreCase);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view.Body);
    }

    [Fact]
    public void Card_reports_offer_and_persistence_failures_as_typed_non_success_states()
    {
        var noDrop = Release1SmallCourtesyPresentation.Build(
            null,
            -1,
            Release1SmallCourtesyOfferStatus.NoEmptyDeadDrop,
            null);
        var persistence = Release1SmallCourtesyPresentation.Build(
            null,
            -1,
            Release1SmallCourtesyOfferStatus.Available,
            null,
            "Acceptance was not persisted; no assignment was started.");

        Assert.Equal(Release1SmallCourtesyCardStage.NoEmptyDrop, noDrop.Stage);
        Assert.Contains("Nowhere clean to put it right now.", noDrop.Body, StringComparison.Ordinal);
        Assert.Equal(Release1SmallCourtesyCardStage.PersistenceDeferred, persistence.Stage);
        Assert.Contains("not persisted", persistence.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_distinguishes_activation_and_each_save_boundary()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        var active = Build(harness);
        var currentState = Assert.IsType<Release1StoryState>(harness.Story.State);
        var acceptedState = currentState with
        {
            Missions = currentState.Missions
                .Select(mission => mission.MissionKey == Release1MissionCatalog.SmallCourtesy
                    ? mission with { State = Release1MissionState.Accepted }
                    : mission)
                .ToArray()
        };

        var awaitingActivation = Release1SmallCourtesyPresentation.Build(
            acceptedState,
            harness.Story.LastPersistedRevision,
            harness.Service.OfferStatus,
            null);
        Assert.Equal(Release1SmallCourtesyCardStage.AwaitingActivation, awaitingActivation.Stage);
        AssertStatusFirstAndCompact(awaitingActivation);
        Assert.StartsWith("Status: Accepted", awaitingActivation.Body, StringComparison.Ordinal);
        Assert.Equal(Release1SmallCourtesyCardStage.Active, active.Stage);
        AssertStatusFirstAndCompact(active);
        Assert.StartsWith("Status: Deliver", active.Body, StringComparison.Ordinal);

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        var awaitingFirstSave = Build(harness);
        Assert.Equal(Release1SmallCourtesyCardStage.AwaitingFirstSave, awaitingFirstSave.Stage);
        AssertStatusFirstAndCompact(awaitingFirstSave);
        Assert.StartsWith("Status: Deposit recognized", awaitingFirstSave.Body, StringComparison.Ordinal);

        Release1SmallCourtesyDepositTests.Save(harness);
        var awaitingSecondSave = Build(harness);
        Assert.Equal(Release1SmallCourtesyCardStage.AwaitingSecondSave, awaitingSecondSave.Stage);
        AssertStatusFirstAndCompact(awaitingSecondSave);
        Assert.StartsWith("Status: Item accepted", awaitingSecondSave.Body, StringComparison.Ordinal);

        Release1SmallCourtesyDepositTests.Save(harness);
        var awaitingFinalSave = Build(harness);
        Assert.Equal(Release1SmallCourtesyCardStage.AwaitingFinalSave, awaitingFinalSave.Stage);
        AssertStatusFirstAndCompact(awaitingFinalSave);
        Assert.StartsWith("Status: Payment delivered", awaitingFinalSave.Body, StringComparison.Ordinal);

        Release1SmallCourtesyDepositTests.Save(harness);
        var completed = Build(harness);
        Assert.Equal(Release1SmallCourtesyCardStage.Completed, completed.Stage);
        AssertStatusFirstAndCompact(completed);
        Assert.StartsWith("Status: Complete", completed.Body, StringComparison.Ordinal);
        Assert.Equal(Release1MissionState.Satisfied, harness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void Blocked_native_effect_is_presented_as_ambiguous_not_retryable()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        harness.World.CashBalance += 10f;
        Release1SmallCourtesyDepositTests.Save(harness);

        var view = Build(harness);

        Assert.Equal(Release1SmallCourtesyCardStage.Ambiguous, view.Stage);
        Assert.Contains("stopped", view.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not retry", view.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Presenter_commands_forward_to_the_real_service_only()
    {
        using var reviewHarness = OfferedMission();
        var presenter = new Release1SmallCourtesyPresenter(reviewHarness.Service, reviewHarness.Story);
        var beforeReview = reviewHarness.Story.State;

        var reviewCard = Assert.IsType<Release1DecisionPromptCard>(presenter.DecisionPrompt);
        Assert.Equal("Review terms", reviewCard.PrimaryLabel);
        reviewCard.PrimaryAction();
        Assert.True(beforeReview!.ValueEquals(reviewHarness.Story.State));
        var acceptCard = Assert.IsType<Release1DecisionPromptCard>(presenter.DecisionPrompt);
        Assert.Equal("Accept", acceptCard.PrimaryLabel);
        Assert.Equal("Not now", acceptCard.SecondaryLabel);
        acceptCard.PrimaryAction();
        Assert.Equal(Release1MissionState.Active, reviewHarness.Story.State!.Missions[0].State);

        using var deferHarness = OfferedMission();
        var deferPresenter = new Release1SmallCourtesyPresenter(deferHarness.Service, deferHarness.Story);
        Assert.IsType<Release1DecisionPromptCard>(deferPresenter.DecisionPrompt).PrimaryAction();
        Assert.IsType<Release1DecisionPromptCard>(deferPresenter.DecisionPrompt).SecondaryAction!();
        Assert.Equal(Release1MissionState.Deferred, deferHarness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void All_current_release1_customer_copy_uses_plain_punctuation()
    {
        AssertPlayerCopy(Release1PayphoneBanner.Text);
        AssertPlayerCopy(Release1IntroPromptPresenter.PromptText);

        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        var view = Build(harness);
        AssertPlayerCopy(view.Title);
        AssertPlayerCopy(view.Body);
    }

    private static Release1SmallCourtesyViewModel Build(Release1SmallCourtesyDepositTests.Harness harness) =>
        Release1SmallCourtesyPresentation.Build(
            harness.Story.State,
            harness.Story.LastPersistedRevision,
            harness.Service.OfferStatus,
            harness.Service.ReviewedQuote);

    private static void AssertPlayerCopy(string value)
    {
        Assert.DoesNotContain('\u2014', value);
        Assert.DoesNotContain('\u2013', value);
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2018', value);
        Assert.DoesNotContain('\u2019', value);
        Assert.DoesNotContain('%', value);
    }

    private static void AssertStatusFirstAndCompact(Release1SmallCourtesyViewModel view)
    {
        var lines = view.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.StartsWith("Status: ", lines[0], StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
    }

    private static OfferedHarness OfferedMission()
    {
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var receipt = "intro-presentation";
        Assert.True(story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            receipt,
            Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey,
                0,
                Release1TransitionKind.IntroAccepted,
                receipt).Value)).Accepted);
        var world = new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot);
        var service = new Release1SmallCourtesyMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service);
    }

    private sealed class OfferedHarness : IDisposable
    {
        public OfferedHarness(Release1StoryRuntimeService story, Release1SmallCourtesyMissionService service)
        {
            Story = story;
            Service = service;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyMissionService Service { get; }
        public void Dispose() { Service.Dispose(); Story.Dispose(); }
    }
}
