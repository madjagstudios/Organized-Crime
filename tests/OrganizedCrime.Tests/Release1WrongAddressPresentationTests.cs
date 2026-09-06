using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressPresentationTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_is_hidden()
    {
        var view = Release1WrongAddressPresentation.Build(null, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Hidden, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Insufficient_drops_status_shows_the_refusal_regardless_of_story()
    {
        var view = Release1WrongAddressPresentation.Build(null, Release1WrongAddressOfferStatus.InsufficientEmptyDeadDrops, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.InsufficientDrops, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1WrongAddressPresentation.RefusalText, view.Body);
        Assert.Contains("two clear drops", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Not_accepted_relationship_is_hidden_even_when_offered()
    {
        var story = UnstartedStory();

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Offer_is_shown_when_available_and_not_yet_assigned()
    {
        var story = AcceptedStory();

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1WrongAddressPresentation.OfferText, view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Unavailable_offer_with_no_assignment_is_hidden()
    {
        var story = AcceptedStory();

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Ineligible, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Hidden, view.Stage);
    }

    [Theory]
    [InlineData(Release1WrongAddressAssignmentMode.Primary, null, 24d, "one day")]
    [InlineData(Release1WrongAddressAssignmentMode.MakeGood, "Make good, attempt 1", 24d, "one day")]
    [InlineData(Release1WrongAddressAssignmentMode.Recovery, "Recovery, attempt 1", null, "none")]
    public void Reviewed_quote_shows_full_terms_ending_in_the_question(
        Release1WrongAddressAssignmentMode mode, string? headerLine, double? deadlineHours, string deadlineText)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, mode);
        var quote = new Release1WrongAddressQuote(assignment, deadlineHours);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, quote, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Review, view.Stage);
        Assert.False(view.CanReview);
        Assert.True(view.CanAccept);
        Assert.True(view.CanDefer);
        var lines = view.Body.Split('\n');
        if (headerLine is null)
            Assert.DoesNotContain("attempt", lines[0], StringComparison.Ordinal);
        else
            Assert.Equal(headerLine, lines[0]);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        Assert.Contains($"1 {assignment.PackagingName} of {assignment.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Pick up: {assignment.SourceDropName}. Drop off: {assignment.HandoffDropName}.", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.SourceDropDescription, view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.HandoffDropDescription, view.Body, StringComparison.Ordinal);
        Assert.Contains($"Deadline: {deadlineText}", view.Body, StringComparison.Ordinal);
        Assert.Contains("We pay 125 percent of the package value the moment you hand it over.", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("waiting at the wrong address", view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Do you have the balls?", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Accepted_but_not_yet_active_shows_awaiting_activation()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Accepted);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.AwaitingActivation, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Contains("Status: You twat. Collect the package from the wrong address.", view.Body, StringComparison.Ordinal);
        Assert.Contains($"1 {assignment.PackagingName} of {assignment.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Pick up: {assignment.SourceDropName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Drop off: {assignment.HandoffDropName}", view.Body, StringComparison.Ordinal);
        Assert.Contains("We pay 125 percent of the package value the moment you hand it over.", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Active_without_custody_shows_the_collect_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.Active, view.Stage);
        Assert.Contains("Status: You twat. Collect the package from the wrong address.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Custody_shows_take_it_to_the_right_address()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, assignment.Attempt, true, true);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1WrongAddressCardStage.InCustody, view.Stage);
        Assert.Contains("Status: Finish the fucking job and take it to the right address.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Progress_without_custody_still_shows_the_collect_status()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, assignment.Attempt, true, false);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1WrongAddressCardStage.Active, view.Stage);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Blocked_native_effect_is_shown_as_ambiguous_not_retryable()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };

        var view = Release1WrongAddressPresentation.Build(blocked, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.Ambiguous, view.Stage);
        Assert.Contains("Stopped on conflicting evidence. It will not retry.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Blocked_effect_takes_priority_over_custody()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, assignment.Attempt, true, true);

        var view = Release1WrongAddressPresentation.Build(blocked, Release1WrongAddressOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1WrongAddressCardStage.Ambiguous, view.Stage);
    }

    [Fact]
    public void Satisfied_mission_shows_completed()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.Completed, view.Stage);
        Assert.Contains("Status: You found a way to not fuck it up. Delivered and paid.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void MakeGoodOffered_with_the_stale_primary_assignment_still_offers_review()
    {
        var primary = MakeDistinctAssignment(1, Release1WrongAddressAssignmentMode.Primary, "PRI");
        var story = StoryWithAssignments(Release1MissionState.MakeGoodOffered, 1, primary);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void MakeGoodOffered_reviewed_quote_shows_make_good_terms_despite_the_stale_primary_assignment()
    {
        var primary = MakeDistinctAssignment(1, Release1WrongAddressAssignmentMode.Primary, "PRI");
        var makeGoodQuote = MakeDistinctAssignment(2, Release1WrongAddressAssignmentMode.MakeGood, "MG");
        var story = StoryWithAssignments(Release1MissionState.MakeGoodOffered, 1, primary);
        var quote = new Release1WrongAddressQuote(makeGoodQuote, 24d);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, quote, null);

        Assert.Equal(Release1WrongAddressCardStage.Review, view.Stage);
        Assert.True(view.CanAccept);
        Assert.Contains("Make good, attempt 2", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void MakeGoodActive_shows_the_make_good_assignment_not_the_stale_primary_one()
    {
        var primary = MakeDistinctAssignment(1, Release1WrongAddressAssignmentMode.Primary, "PRI");
        var makeGood = MakeDistinctAssignment(2, Release1WrongAddressAssignmentMode.MakeGood, "MG");
        var story = StoryWithAssignments(Release1MissionState.MakeGoodActive, 2, primary, makeGood);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.Active, view.Stage);
        Assert.Contains($"1 {makeGood.PackagingName} of {makeGood.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Pick up: {makeGood.SourceDropName}", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(primary.SourceDropName, view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void RecoveryAvailable_with_earlier_assignments_still_offers_review()
    {
        var primary = MakeDistinctAssignment(1, Release1WrongAddressAssignmentMode.Primary, "PRI");
        var makeGood = MakeDistinctAssignment(2, Release1WrongAddressAssignmentMode.MakeGood, "MG");
        var story = StoryWithAssignments(Release1MissionState.RecoveryAvailable, 2, primary, makeGood);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void RecoveryActive_shows_the_recovery_assignment_not_an_earlier_one()
    {
        var primary = MakeDistinctAssignment(1, Release1WrongAddressAssignmentMode.Primary, "PRI");
        var makeGood = MakeDistinctAssignment(2, Release1WrongAddressAssignmentMode.MakeGood, "MG");
        var recovery = MakeDistinctAssignment(3, Release1WrongAddressAssignmentMode.Recovery, "REC");
        var story = StoryWithAssignments(Release1MissionState.RecoveryActive, 3, primary, makeGood, recovery);

        var view = Release1WrongAddressPresentation.Build(story, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Equal(Release1WrongAddressCardStage.Active, view.Stage);
        Assert.Contains($"Pick up: {recovery.SourceDropName}", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(primary.SourceDropName, view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(makeGood.SourceDropName, view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_deadline_recorded_reports_none()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var noDeadline = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress
                ? mission with { DeadlineGameTimeHours = null }
                : mission).ToArray()
        };

        var view = Release1WrongAddressPresentation.Build(noDeadline, Release1WrongAddressOfferStatus.Inactive, null, null);

        Assert.Contains("Deadline: none", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_defaults_to_persistence_deferred_when_no_stage_is_given()
    {
        var view = Release1WrongAddressPresentation.Build(
            null, Release1WrongAddressOfferStatus.Inactive, null, null, "Acceptance was not persisted; no assignment was started.");

        Assert.True(view.Visible);
        Assert.Equal(Release1WrongAddressCardStage.PersistenceDeferred, view.Stage);
        Assert.Equal("Acceptance was not persisted; no assignment was started.", view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_honors_an_explicit_stage()
    {
        var view = Release1WrongAddressPresentation.Build(
            null, Release1WrongAddressOfferStatus.Inactive, null, null, "The decision was rejected.", Release1WrongAddressCardStage.DecisionRejected);

        Assert.Equal(Release1WrongAddressCardStage.DecisionRejected, view.Stage);
        Assert.Equal("The decision was rejected.", view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Whitespace_only_feedback_is_ignored()
    {
        var view = Release1WrongAddressPresentation.Build(null, Release1WrongAddressOfferStatus.Inactive, null, null, "   ");

        Assert.Equal(Release1WrongAddressCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Presenter_forwards_review_accept_and_defer_to_the_real_service_only()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        var presenter = new Release1WrongAddressPresenter(harness.Service, harness.Story);

        var reviewResult = presenter.TryReview();
        Assert.Equal(Release1WrongAddressReviewStatus.Ready, reviewResult.Status);
        Assert.Equal(Release1WrongAddressCardStage.Review, presenter.View.Stage);

        var deferResult = presenter.TryDefer();
        Assert.Equal(Release1WrongAddressDecisionStatus.Deferred, deferResult.Status);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Presenter_accept_activates_the_mission()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var presenter = new Release1WrongAddressPresenter(harness.Service, harness.Story);
        presenter.TryReview();

        var acceptResult = presenter.TryAccept();

        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, acceptResult.Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void Disposed_presenter_reports_hidden()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        var presenter = new Release1WrongAddressPresenter(harness.Service, harness.Story);

        presenter.Dispose();

        Assert.False(presenter.View.Visible);
    }

    private static void AssertPlayerCopy(Release1WrongAddressViewModel view)
    {
        AssertPlayerCopy(view.Title);
        foreach (var line in view.Body.Split('\n')) AssertPlayerCopy(line);
    }

    private static void AssertPlayerCopy(string value)
    {
        Assert.DoesNotContain('—', value);
        Assert.DoesNotContain('–', value);
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('‘', value);
        Assert.DoesNotContain('’', value);
        Assert.DoesNotContain('%', value);
    }

    private static (Release1StoryState Story, Release1WrongAddressAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, int attempt = 1)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(attempt, Release1WrongAddressAssignmentMode.Primary);

        story = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "wrong-address-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = 124d,
                    LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "test-wa-reward-receipt" : null
                }
                : mission).ToArray(),
            WrongAddressAssignments = new[] { assignment }
        };

        return (story, assignment);
    }

    private static Release1StoryState StoryWithAssignments(
        Release1MissionState missionState, int attempt, params Release1WrongAddressAssignment[] assignments)
    {
        var story = AcceptedStory();
        return story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "wrong-address-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = missionState is Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive ? null : 124d,
                    LastOutcome = Release1MissionOutcome.None
                }
                : mission).ToArray(),
            WrongAddressAssignments = assignments
        };
    }

    private static Release1WrongAddressAssignment MakeDistinctAssignment(int attempt, Release1WrongAddressAssignmentMode mode, string tag)
    {
        var transitionKind = mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, attempt, transitionKind, $"test-wa-authorization-{tag}").Value;
        return new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress,
            attempt,
            mode,
            authorization,
            "cocaine",
            "Cocaine",
            "brick",
            "Brick",
            1,
            $"drop-{tag}-a-guid",
            $"Drop {tag}-A",
            "Behind the drainage ditch off Dyer Street.",
            1d, 2d, 3d,
            $"drop-{tag}-b-guid",
            $"Drop {tag}-B",
            "Beneath the loading dock on Kettleman.",
            4d, 5d, 6d,
            1.25d);
    }

    private static Release1WrongAddressAssignment MakeAssignment(int attempt, Release1WrongAddressAssignmentMode mode)
    {
        var transitionKind = mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, attempt, transitionKind, "test-wa-authorization").Value;
        return new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress,
            attempt,
            mode,
            authorization,
            "cocaine",
            "Cocaine",
            "brick",
            "Brick",
            1,
            "drop-a-guid",
            "Drop A",
            "Behind the drainage ditch off Dyer Street.",
            1d, 2d, 3d,
            "drop-b-guid",
            "Drop B",
            "Beneath the loading dock on Kettleman.",
            4d, 5d, 6d,
            1.25d);
    }

    private static Release1NativeEffectJournalEntry BlockedEffect(int attempt) => new(
        "wa-effect-blocked-v1",
        Release1MissionCatalog.WrongAddress,
        attempt,
        "CargoTransfer",
        "source-drop",
        "oc-wrong-address",
        "cargo-identity",
        Release1NativeEffectPhase.Prepared,
        null,
        0,
        ExecutionBlocked: true);

    private static Release1StoryState UnstartedStory() => new(
        PlayerId, 0, Release1RelationshipState.Unstarted, false,
        Array.Empty<string>(), Array.Empty<string>(),
        Release1MissionCatalog.All.Select(definition => NewMission(definition.MissionKey)).ToArray(),
        Array.Empty<Release1NativeEffectJournalEntry>(), 0);

    private static Release1StoryState AcceptedStory()
    {
        var introCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "test-intro-receipt").Value;
        return Release1StoryState.CreateAccepted(PlayerId, introCorrelation);
    }

    private static Release1MissionRecord NewMission(string key) => new(
        key, Release1MissionState.Locked, 1, null, null, null, Release1MissionOutcome.None, 0,
        Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0);
}
