using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticePresentationTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_is_hidden()
    {
        var view = Release1ShortNoticePresentation.Build(null, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.Hidden, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_empty_dead_drop_status_shows_the_refusal_regardless_of_story()
    {
        var view = Release1ShortNoticePresentation.Build(null, Release1ShortNoticeOfferStatus.NoEmptyDeadDrop, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.NoDrop, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1ShortNoticePresentation.RefusalText, view.Body);
        Assert.Contains("one clear drop", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Not_accepted_relationship_is_hidden_even_when_offered()
    {
        var story = UnstartedStory();

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Available, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Offer_is_shown_when_available_and_not_yet_assigned()
    {
        var story = AcceptedStory();

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1ShortNoticePresentation.OfferText, view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Unavailable_offer_with_no_assignment_is_hidden()
    {
        var story = AcceptedStory();

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Ineligible, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.Hidden, view.Stage);
    }

    [Theory]
    [InlineData(Release1ShortNoticeAssignmentMode.Primary, null, 12d, "12 hours", 3, "3 Bricks")]
    [InlineData(Release1ShortNoticeAssignmentMode.MakeGood, "Make good, attempt 1", 12d, "12 hours", 2, "2 Bricks")]
    [InlineData(Release1ShortNoticeAssignmentMode.Recovery, "Recovery, attempt 1", null, "none", 1, "1 Brick")]
    public void Reviewed_quote_shows_full_terms_ending_in_the_question(
        Release1ShortNoticeAssignmentMode mode, string? headerLine, double? deadlineHours, string deadlineText, int quantity, string units)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, mode);
        var quote = new Release1ShortNoticeQuote(assignment, deadlineHours);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Available, quote, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.Review, view.Stage);
        Assert.False(view.CanReview);
        Assert.True(view.CanAccept);
        Assert.True(view.CanDefer);
        Assert.Equal(quantity, assignment.RequiredQuantity);
        // Verified for every mode: no header on primary means at most six lines total; a header on
        // make-good or recovery is an extra first line beyond the six fact lines (section 2.1).
        Release1CopyAssertions.AssertAttemptHeaderAndFactLineCap(view.Body, headerLine);
        Assert.Contains($"{units} of {assignment.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Drop: {assignment.HandoffDropName}. All of it in one slot.", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.HandoffDropDescription, view.Body, StringComparison.Ordinal);
        Assert.Contains($"Deadline: {deadlineText}", view.Body, StringComparison.Ordinal);
        Assert.Contains("I pay 175 percent of what you leave, the moment it lands.", view.Body, StringComparison.Ordinal);
        Assert.Contains("It comes out of your own stock. I am not sending you any.", view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Are you taking it?", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Accepted_but_not_yet_active_shows_awaiting_activation()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Accepted);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1ShortNoticeCardStage.AwaitingActivation, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Contains("Status: Leave the whole order in one slot at the drop before the window closes.", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Active_without_progress_shows_the_default_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1ShortNoticeCardStage.Active, view.Stage);
        Assert.Contains("Status: Leave the whole order in one slot at the drop before the window closes.", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Shortfall_of_two_shows_the_many_format()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, assignment.Attempt, 1, 2, false);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1ShortNoticeCardStage.Shortfall, view.Stage);
        Assert.Contains("Status: Part of the order is there. 2 more still needed.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Shortfall_of_one_shows_the_singular_line()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, assignment.Attempt, 2, 1, false);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1ShortNoticeCardStage.Shortfall, view.Stage);
        Assert.Contains("Status: Part of the order is there. One more still needed.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Spread_shows_the_put_it_together_status()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, assignment.Attempt, 0, null, true);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1ShortNoticeCardStage.Spread, view.Stage);
        Assert.Contains("Status: The order is in more than one slot. Put it together.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Blocked_native_effect_is_shown_as_ambiguous_not_retryable()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };

        var view = Release1ShortNoticePresentation.Build(blocked, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1ShortNoticeCardStage.Ambiguous, view.Stage);
        Assert.Contains("Stopped on conflicting evidence. It will not retry.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Satisfied_mission_shows_completed()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);

        var view = Release1ShortNoticePresentation.Build(story, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1ShortNoticeCardStage.Completed, view.Stage);
        Assert.Contains("Status: Complete. Delivered and paid.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_deadline_recorded_reports_none()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var noDeadline = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.ShortNotice
                ? mission with { DeadlineGameTimeHours = null }
                : mission).ToArray()
        };

        var view = Release1ShortNoticePresentation.Build(noDeadline, Release1ShortNoticeOfferStatus.Inactive, null, null);

        Assert.Contains("Deadline: none", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_defaults_to_persistence_deferred_when_no_stage_is_given()
    {
        var view = Release1ShortNoticePresentation.Build(
            null, Release1ShortNoticeOfferStatus.Inactive, null, null, "Acceptance was not persisted; no assignment was started.");

        Assert.True(view.Visible);
        Assert.Equal(Release1ShortNoticeCardStage.PersistenceDeferred, view.Stage);
        Assert.Equal("Acceptance was not persisted; no assignment was started.", view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_honors_an_explicit_stage()
    {
        var view = Release1ShortNoticePresentation.Build(
            null, Release1ShortNoticeOfferStatus.Inactive, null, null, "The decision was rejected.", Release1ShortNoticeCardStage.DecisionRejected);

        Assert.Equal(Release1ShortNoticeCardStage.DecisionRejected, view.Stage);
        Assert.Equal("The decision was rejected.", view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Whitespace_only_feedback_is_ignored()
    {
        var view = Release1ShortNoticePresentation.Build(null, Release1ShortNoticeOfferStatus.Inactive, null, null, "   ");

        Assert.Equal(Release1ShortNoticeCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Units_pluralizes_by_count_only()
    {
        Assert.Equal("1 Brick", Release1ShortNoticePresentation.Units(1, "Brick"));
        Assert.Equal("2 Bricks", Release1ShortNoticePresentation.Units(2, "Brick"));
        Assert.Equal("3 Bricks", Release1ShortNoticePresentation.Units(3, "Brick"));
    }

    [Fact]
    public void Presenter_forwards_review_accept_and_defer_to_the_real_service_only()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        var presenter = new Release1ShortNoticePresenter(harness.Service, harness.Story);

        var reviewResult = presenter.TryReview();
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, reviewResult.Status);
        Assert.Equal(Release1ShortNoticeCardStage.Review, presenter.View.Stage);

        var deferResult = presenter.TryDefer();
        Assert.Equal(Release1ShortNoticeDecisionStatus.Deferred, deferResult.Status);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Presenter_accept_activates_the_mission()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var presenter = new Release1ShortNoticePresenter(harness.Service, harness.Story);
        presenter.TryReview();

        var acceptResult = presenter.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, acceptResult.Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void ClearFeedback_resets_a_stale_persistence_deferred_message()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        var presenter = new Release1ShortNoticePresenter(harness.Service, harness.Story);
        presenter.TryReview();
        harness.Service.OnSaveStart();

        var decision = presenter.TryAccept();
        Assert.Equal(Release1ShortNoticeDecisionStatus.PersistenceDeferred, decision.Status);
        Assert.Equal(Release1ShortNoticeCardStage.PersistenceDeferred, presenter.View.Stage);

        presenter.ClearFeedback();

        Assert.NotEqual(Release1ShortNoticeCardStage.PersistenceDeferred, presenter.View.Stage);
    }

    [Fact]
    public void Disposed_presenter_reports_hidden()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        var presenter = new Release1ShortNoticePresenter(harness.Service, harness.Story);

        presenter.Dispose();

        Assert.False(presenter.View.Visible);
    }

    // ---- Step 1: every card stage carries the shared Active-shape invariants -----------------

    [Theory]
    [MemberData(nameof(ActiveShapedViews))]
    public void Every_active_shaped_stage_has_the_status_line_and_no_decision_options(Release1ShortNoticeViewModel view)
    {
        Assert.Equal(Release1ShortNoticePresentation.Title, view.Title);
        Assert.StartsWith("Status: ", view.Body, StringComparison.Ordinal);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    public static IEnumerable<object[]> ActiveShapedViews()
    {
        var (awaitingStory, _) = StoryWithAssignment(Release1MissionState.Accepted);
        yield return new object[] { Release1ShortNoticePresentation.Build(awaitingStory, Release1ShortNoticeOfferStatus.Inactive, null, null) };

        var (activeStory, _) = StoryWithAssignment(Release1MissionState.Active);
        yield return new object[] { Release1ShortNoticePresentation.Build(activeStory, Release1ShortNoticeOfferStatus.Inactive, null, null) };

        var (shortfallStory, shortfallAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var shortfallProgress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, shortfallAssignment.Attempt, 1, 2, false);
        yield return new object[] { Release1ShortNoticePresentation.Build(shortfallStory, Release1ShortNoticeOfferStatus.Inactive, null, shortfallProgress) };

        var (spreadStory, spreadAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var spreadProgress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, spreadAssignment.Attempt, 0, null, true);
        yield return new object[] { Release1ShortNoticePresentation.Build(spreadStory, Release1ShortNoticeOfferStatus.Inactive, null, spreadProgress) };

        var (satisfiedStory, _) = StoryWithAssignment(Release1MissionState.Satisfied);
        yield return new object[] { Release1ShortNoticePresentation.Build(satisfiedStory, Release1ShortNoticeOfferStatus.Inactive, null, null) };

        var (ambiguousStory, ambiguousAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = ambiguousStory with { NativeEffects = new[] { BlockedEffect(ambiguousAssignment.Attempt) } };
        yield return new object[] { Release1ShortNoticePresentation.Build(blocked, Release1ShortNoticeOfferStatus.Inactive, null, null) };
    }

    private static void AssertPlayerCopy(Release1ShortNoticeViewModel view)
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
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
    }

    private static (Release1StoryState Story, Release1ShortNoticeAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, int attempt = 1)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(attempt, Release1ShortNoticeAssignmentMode.Primary);

        story = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.ShortNotice
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "short-notice-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = 112d,
                    LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "test-sn-reward-receipt" : null
                }
                : mission).ToArray(),
            ShortNoticeAssignments = new[] { assignment }
        };

        return (story, assignment);
    }

    private static Release1ShortNoticeAssignment MakeAssignment(int attempt, Release1ShortNoticeAssignmentMode mode)
    {
        var transitionKind = mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.ShortNotice, attempt, transitionKind, "test-sn-authorization").Value;
        return new Release1ShortNoticeAssignment(
            Release1MissionCatalog.ShortNotice,
            attempt,
            mode,
            authorization,
            "cocaine",
            "Cocaine",
            "brick",
            "Brick",
            Release1ShortNoticeAssignment.QuantityFor(mode),
            "drop-d-guid",
            "Drop D",
            "Beneath the loading dock on Kettleman.",
            4d, 5d, 6d,
            mode == Release1ShortNoticeAssignmentMode.Recovery ? null : Release1ShortNoticeAssignment.TimedStageGameMinutes,
            Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
            Release1ShortNoticeValueConvention.PerUnit);
    }

    private static Release1NativeEffectJournalEntry BlockedEffect(int attempt) => new(
        "sn-effect-blocked-v1",
        Release1MissionCatalog.ShortNotice,
        attempt,
        "CargoTransfer",
        "handoff-drop",
        "oc-short-notice",
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
