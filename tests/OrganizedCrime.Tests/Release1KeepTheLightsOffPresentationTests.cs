using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1KeepTheLightsOffPresentationTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_is_hidden()
    {
        var view = Release1KeepTheLightsOffPresentation.Build(null, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Hidden, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Not_accepted_relationship_is_hidden_even_when_offered()
    {
        var story = UnstartedStory();

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Available, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Offer_is_shown_when_available_and_not_yet_assigned()
    {
        var story = AcceptedStory();

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1KeepTheLightsOffPresentation.OfferText, view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Unavailable_offer_with_no_assignment_is_hidden()
    {
        var story = AcceptedStory();

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Ineligible, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Hidden, view.Stage);
    }

    [Theory]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.Primary, null, 72d, "72 hours")]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.MakeGood, "Make good, attempt 1", 72d, "72 hours")]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.Recovery, "Recovery, attempt 1", null, "none")]
    public void The_terms_body_carries_no_product_and_stays_inside_the_line_cap(
        Release1KeepTheLightsOffAssignmentMode mode, string? headerLine, double? deadlineHours, string deadlineText)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, mode);
        var quote = new Release1KeepTheLightsOffQuote(assignment, deadlineHours);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Available, quote, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Review, view.Stage);
        Assert.False(view.CanReview);
        Assert.True(view.CanAccept);
        Assert.True(view.CanDefer);

        var factLines = new[]
        {
            Release1KeepTheLightsOffPresentation.ActionText,
            Release1KeepTheLightsOffPresentation.HoldText,
            $"Deadline: {deadlineText}",
            Release1KeepTheLightsOffPresentation.PaymentText,
            Release1KeepTheLightsOffPresentation.OwnershipText,
            Release1KeepTheLightsOffPresentation.QuestionText
        };
        var expectedLines = headerLine is null ? factLines : new[] { headerLine }.Concat(factLines).ToArray();
        Assert.Equal(expectedLines, view.Body.Split('\n'));

        // Verified for every mode: no header on primary means at most six lines total; a header on
        // make-good or recovery is an extra first line beyond the six fact lines (section 2.1).
        Release1CopyAssertions.AssertAttemptHeaderAndFactLineCap(view.Body, headerLine);
        Assert.DoesNotContain("Cocaine", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("{", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Accepted_but_not_yet_active_shows_awaiting_activation()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Accepted);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Equal(Release1KeepTheLightsOffCardStage.AwaitingActivation, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.RunningStatusText}", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Active_without_progress_shows_the_running_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Running, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.RunningStatusText}", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Make_good_active_without_progress_shows_the_running_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.MakeGoodActive, Release1KeepTheLightsOffAssignmentMode.MakeGood);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Running, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.RunningStatusText}", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Recovery_active_without_progress_shows_the_running_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.RecoveryActive, Release1KeepTheLightsOffAssignmentMode.Recovery);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Running, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.RunningStatusText}", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Clear_confirmed_shows_the_holding_status()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, assignment.Attempt, 5_000d, null);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Holding, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.HoldingStatusText}", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Breach_since_the_last_pass_shows_the_breach_status_even_after_a_clear_was_confirmed()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, assignment.Attempt, 5_000d, 6_000d);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Breach, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.BreachStatusText}", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Blocked_native_effect_is_shown_as_ambiguous_not_retryable()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };

        var view = Release1KeepTheLightsOffPresentation.Build(blocked, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Equal(Release1KeepTheLightsOffCardStage.Ambiguous, view.Stage);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.AmbiguousStatusText}", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void A_satisfied_attempt_renders_the_completed_card_with_no_assignment_at_all()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);
        var noAssignments = story with { KeepTheLightsOffAssignments = Array.Empty<Release1KeepTheLightsOffAssignment>() };

        var view = Release1KeepTheLightsOffPresentation.Build(noAssignments, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Completed, view.Stage);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1KeepTheLightsOffPresentation.CompletedText), view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void A_satisfied_attempt_still_renders_the_completed_card_when_an_assignment_is_present()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);

        var view = Release1KeepTheLightsOffPresentation.Build(story, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Completed, view.Stage);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1KeepTheLightsOffPresentation.CompletedText), view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_deadline_recorded_reports_none()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var noDeadline = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.KeepTheLightsOff
                ? mission with { DeadlineGameTimeHours = null }
                : mission).ToArray()
        };

        var view = Release1KeepTheLightsOffPresentation.Build(noDeadline, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);

        Assert.Contains("Deadline: none", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_defaults_to_persistence_deferred_when_no_stage_is_given()
    {
        var view = Release1KeepTheLightsOffPresentation.Build(
            null, Release1KeepTheLightsOffOfferStatus.Inactive, null, null, "Acceptance was not persisted; no assignment was started.");

        Assert.True(view.Visible);
        Assert.Equal(Release1KeepTheLightsOffCardStage.PersistenceDeferred, view.Stage);
        Assert.Equal("Acceptance was not persisted; no assignment was started.", view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_honors_an_explicit_stage()
    {
        var view = Release1KeepTheLightsOffPresentation.Build(
            null, Release1KeepTheLightsOffOfferStatus.Inactive, null, null, "The decision was rejected.", Release1KeepTheLightsOffCardStage.DecisionRejected);

        Assert.Equal(Release1KeepTheLightsOffCardStage.DecisionRejected, view.Stage);
        Assert.Equal("The decision was rejected.", view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Whitespace_only_feedback_is_ignored()
    {
        var view = Release1KeepTheLightsOffPresentation.Build(null, Release1KeepTheLightsOffOfferStatus.Inactive, null, null, "   ");

        Assert.Equal(Release1KeepTheLightsOffCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Presenter_forwards_review_accept_and_defer_to_the_real_service_only()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        var presenter = new Release1KeepTheLightsOffPresenter(harness.Service, harness.Story);

        var reviewResult = presenter.TryReview();
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, reviewResult.Status);
        Assert.Equal(Release1KeepTheLightsOffCardStage.Review, presenter.View.Stage);

        var deferResult = presenter.TryDefer();
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Deferred, deferResult.Status);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Presenter_accept_activates_the_mission()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var presenter = new Release1KeepTheLightsOffPresenter(harness.Service, harness.Story);
        presenter.TryReview();

        var acceptResult = presenter.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, acceptResult.Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void ClearFeedback_resets_a_stale_persistence_deferred_message()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        var presenter = new Release1KeepTheLightsOffPresenter(harness.Service, harness.Story);
        presenter.TryReview();
        harness.Service.OnSaveStart();

        var decision = presenter.TryAccept();
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred, decision.Status);
        Assert.Equal(Release1KeepTheLightsOffCardStage.PersistenceDeferred, presenter.View.Stage);

        presenter.ClearFeedback();

        Assert.NotEqual(Release1KeepTheLightsOffCardStage.PersistenceDeferred, presenter.View.Stage);
    }

    [Fact]
    public void Disposed_presenter_reports_hidden()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        var presenter = new Release1KeepTheLightsOffPresenter(harness.Service, harness.Story);

        presenter.Dispose();

        Assert.False(presenter.View.Visible);
    }

    // ---- Step 1: every card stage carries the shared Active-shape invariants -----------------

    [Theory]
    [MemberData(nameof(ActiveShapedViews))]
    public void Every_active_shaped_stage_has_the_status_line_and_no_decision_options(Release1KeepTheLightsOffViewModel view)
    {
        Assert.Equal(Release1KeepTheLightsOffPresentation.Title, view.Title);
        Assert.StartsWith("Status: ", view.Body, StringComparison.Ordinal);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    public static IEnumerable<object[]> ActiveShapedViews()
    {
        // Satisfied is deliberately absent: decision 14 gives it its own assignment free, one line
        // Completed shape, not this four-line "Status: " shape, so it is covered by its own tests below.
        var (awaitingStory, _) = StoryWithAssignment(Release1MissionState.Accepted);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(awaitingStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null) };

        var (runningStory, _) = StoryWithAssignment(Release1MissionState.Active);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(runningStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null) };

        var (makeGoodStory, _) = StoryWithAssignment(Release1MissionState.MakeGoodActive, Release1KeepTheLightsOffAssignmentMode.MakeGood);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(makeGoodStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null) };

        var (recoveryStory, _) = StoryWithAssignment(Release1MissionState.RecoveryActive, Release1KeepTheLightsOffAssignmentMode.Recovery);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(recoveryStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null) };

        var (holdingStory, holdingAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var holdingProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, holdingAssignment.Attempt, 5_000d, null);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(holdingStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, holdingProgress) };

        var (breachStory, breachAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var breachProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, breachAssignment.Attempt, 5_000d, 6_000d);
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(breachStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, breachProgress) };

        var (ambiguousStory, ambiguousAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = ambiguousStory with { NativeEffects = new[] { BlockedEffect(ambiguousAssignment.Attempt) } };
        yield return new object[] { Release1KeepTheLightsOffPresentation.Build(blocked, Release1KeepTheLightsOffOfferStatus.Inactive, null, null) };
    }

    // ---- Task 5: the shutdown copy, and the assignment free Completed stage ------------------

    [Theory]
    [InlineData(Release1KeepTheLightsOffPresentation.Title)]
    [InlineData(Release1KeepTheLightsOffPresentation.OfferText)]
    [InlineData(Release1KeepTheLightsOffPresentation.QuestionText)]
    [InlineData(Release1KeepTheLightsOffPresentation.ActionText)]
    [InlineData(Release1KeepTheLightsOffPresentation.HoldText)]
    [InlineData(Release1KeepTheLightsOffPresentation.OwnershipText)]
    [InlineData(Release1KeepTheLightsOffPresentation.PaymentText)]
    [InlineData(Release1KeepTheLightsOffPresentation.RunningStatusText)]
    [InlineData(Release1KeepTheLightsOffPresentation.HoldingStatusText)]
    [InlineData(Release1KeepTheLightsOffPresentation.BreachStatusText)]
    [InlineData(Release1KeepTheLightsOffPresentation.CompletedText)]
    [InlineData(Release1KeepTheLightsOffPresentation.AmbiguousStatusText)]
    public void Every_copy_string_is_the_owners_verbatim_and_normalizes_unchanged(string value)
    {
        AssertPlayerCopy(value);
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
    }

    public static IEnumerable<object[]> FourLineActiveViews()
    {
        var (awaitingStory, _) = StoryWithAssignment(Release1MissionState.Accepted);
        yield return new object[]
        {
            Release1KeepTheLightsOffPresentation.Build(awaitingStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null),
            Release1KeepTheLightsOffPresentation.RunningStatusText
        };

        var (runningStory, _) = StoryWithAssignment(Release1MissionState.Active);
        yield return new object[]
        {
            Release1KeepTheLightsOffPresentation.Build(runningStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null),
            Release1KeepTheLightsOffPresentation.RunningStatusText
        };

        var (holdingStory, holdingAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var holdingProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, holdingAssignment.Attempt, 5_000d, null);
        yield return new object[]
        {
            Release1KeepTheLightsOffPresentation.Build(holdingStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, holdingProgress),
            Release1KeepTheLightsOffPresentation.HoldingStatusText
        };

        var (breachStory, breachAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var breachProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, breachAssignment.Attempt, 5_000d, 6_000d);
        yield return new object[]
        {
            Release1KeepTheLightsOffPresentation.Build(breachStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, breachProgress),
            Release1KeepTheLightsOffPresentation.BreachStatusText
        };
    }

    [Theory]
    [MemberData(nameof(FourLineActiveViews))]
    public void The_active_body_carries_the_status_the_action_line_the_deadline_and_the_payment(
        Release1KeepTheLightsOffViewModel view, string expectedStatus)
    {
        var expectedLines = new[]
        {
            $"Status: {expectedStatus}",
            Release1KeepTheLightsOffPresentation.ActionText,
            "Deadline: 72 hours",
            Release1KeepTheLightsOffPresentation.PaymentText
        };
        Assert.Equal(expectedLines, view.Body.Split('\n'));
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Each_active_stage_selects_its_own_status_line()
    {
        var (runningStory, _) = StoryWithAssignment(Release1MissionState.Active);
        var runningView = Release1KeepTheLightsOffPresentation.Build(runningStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.RunningStatusText}", runningView.Body, StringComparison.Ordinal);

        var (holdingStory, holdingAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var holdingProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, holdingAssignment.Attempt, 5_000d, null);
        var holdingView = Release1KeepTheLightsOffPresentation.Build(holdingStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, holdingProgress);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.HoldingStatusText}", holdingView.Body, StringComparison.Ordinal);

        var (breachStory, breachAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var breachProgress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, breachAssignment.Attempt, 5_000d, 6_000d);
        var breachView = Release1KeepTheLightsOffPresentation.Build(breachStory, Release1KeepTheLightsOffOfferStatus.Inactive, null, breachProgress);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.BreachStatusText}", breachView.Body, StringComparison.Ordinal);

        var (ambiguousStory, ambiguousAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = ambiguousStory with { NativeEffects = new[] { BlockedEffect(ambiguousAssignment.Attempt) } };
        var ambiguousView = Release1KeepTheLightsOffPresentation.Build(blocked, Release1KeepTheLightsOffOfferStatus.Inactive, null, null);
        Assert.Contains($"Status: {Release1KeepTheLightsOffPresentation.AmbiguousStatusText}", ambiguousView.Body, StringComparison.Ordinal);
    }

    private static void AssertPlayerCopy(Release1KeepTheLightsOffViewModel view)
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

    private static (Release1StoryState Story, Release1KeepTheLightsOffAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, int attempt = 1) =>
        StoryWithAssignment(missionState, Release1KeepTheLightsOffAssignmentMode.Primary, attempt);

    private static (Release1StoryState Story, Release1KeepTheLightsOffAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, Release1KeepTheLightsOffAssignmentMode mode, int attempt = 1)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(attempt, mode);

        story = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.KeepTheLightsOff
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "keep-the-lights-off-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = 172d,
                    LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "test-ktlo-reward-receipt" : null
                }
                : mission).ToArray(),
            KeepTheLightsOffAssignments = new[] { assignment }
        };

        return (story, assignment);
    }

    private static Release1KeepTheLightsOffAssignment MakeAssignment(int attempt, Release1KeepTheLightsOffAssignmentMode mode)
    {
        var transitionKind = mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.KeepTheLightsOff, attempt, transitionKind, "test-ktlo-authorization").Value;
        return new Release1KeepTheLightsOffAssignment(
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            mode,
            authorization,
            3,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);
    }

    private static Release1NativeEffectJournalEntry BlockedEffect(int attempt) => new(
        "ktlo-effect-blocked-v1",
        Release1MissionCatalog.KeepTheLightsOff,
        attempt,
        "Reward",
        "oc-keep-the-lights-off",
        "player-cash",
        "reward-identity",
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
