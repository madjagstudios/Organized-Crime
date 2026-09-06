using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNamePresentationTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_is_hidden()
    {
        var view = Release1RoomWithNoNamePresentation.Build(null, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Hidden, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Insufficient_drops_status_shows_the_refusal_regardless_of_story()
    {
        var view = Release1RoomWithNoNamePresentation.Build(null, Release1RoomWithNoNameOfferStatus.InsufficientEmptyDeadDrops, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.InsufficientDrops, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1RoomWithNoNamePresentation.RefusalText, view.Body);
        Assert.Contains("two clear drops", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Not_accepted_relationship_is_hidden_even_when_offered()
    {
        var story = UnstartedStory();

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Available, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Offer_is_shown_when_available_and_not_yet_assigned()
    {
        var story = AcceptedStory();

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1RoomWithNoNamePresentation.OfferText, view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Unavailable_offer_with_no_assignment_is_hidden()
    {
        var story = AcceptedStory();

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Ineligible, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Hidden, view.Stage);
    }

    [Theory]
    [InlineData(Release1RoomWithNoNameAssignmentMode.Primary, null, 72d, "72 hours")]
    [InlineData(Release1RoomWithNoNameAssignmentMode.MakeGood, "Make good, attempt 1", 72d, "72 hours")]
    [InlineData(Release1RoomWithNoNameAssignmentMode.Recovery, "Recovery, attempt 1", null, "none")]
    public void Reviewed_quote_shows_full_terms_ending_in_the_question(
        Release1RoomWithNoNameAssignmentMode mode, string? headerLine, double? deadlineHours, string deadlineText)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, mode);
        var quote = new Release1RoomWithNoNameQuote(assignment, deadlineHours);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Available, quote, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Review, view.Stage);
        Assert.False(view.CanReview);
        Assert.True(view.CanAccept);
        Assert.True(view.CanDefer);
        // Verified for every mode: no header on primary means at most six lines total; a header on
        // make-good or recovery is an extra first line beyond the six fact lines (section 2.1).
        Release1CopyAssertions.AssertAttemptHeaderAndFactLineCap(view.Body, headerLine);
        Assert.Contains($"1 {assignment.PackagingName} of {assignment.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Pick up: {assignment.SourceDropName}. Drop off: {assignment.HandoffDropName}.", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.SourceDropDescription, view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.HandoffDropDescription, view.Body, StringComparison.Ordinal);
        Assert.Contains("Leave it alone in the storage room at HQ for one day.", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Deadline: {deadlineText}", view.Body, StringComparison.Ordinal);
        Assert.Contains("I pay 150 percent of the value the moment you hand it over.", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("waiting at the pick up", view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Are you taking it?", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Accepted_but_not_yet_active_shows_awaiting_activation()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Accepted);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Equal(Release1RoomWithNoNameCardStage.AwaitingActivation, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Contains("Status: Collect it from the pick up.", view.Body, StringComparison.Ordinal);
        Assert.Contains($"1 {assignment.PackagingName} of {assignment.ProductName}", view.Body, StringComparison.Ordinal);
        Assert.Contains("Room: the storage room at HQ", view.Body, StringComparison.Ordinal);
        Assert.Contains($"Drop off: {assignment.HandoffDropName}", view.Body, StringComparison.Ordinal);
        Assert.Contains("I pay 150 percent of the value the moment you hand it over.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Active_without_progress_shows_the_collect_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Equal(Release1RoomWithNoNameCardStage.Active, view.Stage);
        Assert.Contains("Status: Collect it from the pick up.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Custody_shows_take_it_to_the_room()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, assignment.Attempt, true, true, false, false, null, null, null);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1RoomWithNoNameCardStage.InCustody, view.Stage);
        Assert.Contains("Status: Take it to the room and leave it there. No fucking around on the way there.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Stowed_shows_it_stays_in_the_room()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, assignment.Attempt, true, true, true, false, 100d, "closet-a", null);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1RoomWithNoNameCardStage.Stowed, view.Stage);
        Assert.Contains("Status: It stays in the room. One day.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Released_shows_take_it_to_the_hand_off()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, assignment.Attempt, true, true, true, true, 100d, "closet-a", null);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1RoomWithNoNameCardStage.Released, view.Stage);
        Assert.Contains("Status: The hold is done. Take it to the drop off.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Blocked_native_effect_is_shown_as_ambiguous_not_retryable()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };

        var view = Release1RoomWithNoNamePresentation.Build(blocked, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Equal(Release1RoomWithNoNameCardStage.Ambiguous, view.Stage);
        Assert.Contains("Stopped on conflicting evidence. It will not retry.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void Blocked_effect_takes_priority_over_custody()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };
        var progress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, assignment.Attempt, true, true, false, false, null, null, null);

        var view = Release1RoomWithNoNamePresentation.Build(blocked, Release1RoomWithNoNameOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1RoomWithNoNameCardStage.Ambiguous, view.Stage);
    }

    [Fact]
    public void Satisfied_mission_shows_completed()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Equal(Release1RoomWithNoNameCardStage.Completed, view.Stage);
        Assert.Contains("Status: Complete. Delivered and paid.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
        AssertAtMostSixBodyLines(view);
    }

    [Fact]
    public void MakeGoodOffered_with_the_stale_primary_assignment_still_offers_review()
    {
        var primary = MakeDistinctAssignment(1, Release1RoomWithNoNameAssignmentMode.Primary, "PRI");
        var story = StoryWithAssignments(Release1MissionState.MakeGoodOffered, 1, primary);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void RecoveryActive_shows_the_recovery_assignment_not_an_earlier_one()
    {
        var primary = MakeDistinctAssignment(1, Release1RoomWithNoNameAssignmentMode.Primary, "PRI");
        var makeGood = MakeDistinctAssignment(2, Release1RoomWithNoNameAssignmentMode.MakeGood, "MG");
        var recovery = MakeDistinctAssignment(3, Release1RoomWithNoNameAssignmentMode.Recovery, "REC");
        var story = StoryWithAssignments(Release1MissionState.RecoveryActive, 3, primary, makeGood, recovery);

        var view = Release1RoomWithNoNamePresentation.Build(story, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Equal(Release1RoomWithNoNameCardStage.Active, view.Stage);
        Assert.Contains($"Drop off: {recovery.HandoffDropName}", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(primary.HandoffDropName, view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(makeGood.HandoffDropName, view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_deadline_recorded_reports_none()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var noDeadline = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.RoomWithNoName
                ? mission with { DeadlineGameTimeHours = null }
                : mission).ToArray()
        };

        var view = Release1RoomWithNoNamePresentation.Build(noDeadline, Release1RoomWithNoNameOfferStatus.Inactive, null, null);

        Assert.Contains("Deadline: none", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_defaults_to_persistence_deferred_when_no_stage_is_given()
    {
        var view = Release1RoomWithNoNamePresentation.Build(
            null, Release1RoomWithNoNameOfferStatus.Inactive, null, null, "Acceptance was not persisted; no assignment was started.");

        Assert.True(view.Visible);
        Assert.Equal(Release1RoomWithNoNameCardStage.PersistenceDeferred, view.Stage);
        Assert.Equal("Acceptance was not persisted; no assignment was started.", view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_honors_an_explicit_stage()
    {
        var view = Release1RoomWithNoNamePresentation.Build(
            null, Release1RoomWithNoNameOfferStatus.Inactive, null, null, "The decision was rejected.", Release1RoomWithNoNameCardStage.DecisionRejected);

        Assert.Equal(Release1RoomWithNoNameCardStage.DecisionRejected, view.Stage);
        Assert.Equal("The decision was rejected.", view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Whitespace_only_feedback_is_ignored()
    {
        var view = Release1RoomWithNoNamePresentation.Build(null, Release1RoomWithNoNameOfferStatus.Inactive, null, null, "   ");

        Assert.Equal(Release1RoomWithNoNameCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Presenter_forwards_review_accept_and_defer_to_the_real_service_only()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        var presenter = new Release1RoomWithNoNamePresenter(harness.Service, harness.Story);

        var reviewResult = presenter.TryReview();
        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, reviewResult.Status);
        Assert.Equal(Release1RoomWithNoNameCardStage.Review, presenter.View.Stage);

        var deferResult = presenter.TryDefer();
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Deferred, deferResult.Status);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Presenter_accept_activates_the_mission()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var presenter = new Release1RoomWithNoNamePresenter(harness.Service, harness.Story);
        presenter.TryReview();

        var acceptResult = presenter.TryAccept();

        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, acceptResult.Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void Disposed_presenter_reports_hidden()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        var presenter = new Release1RoomWithNoNamePresenter(harness.Service, harness.Story);

        presenter.Dispose();

        Assert.False(presenter.View.Visible);
    }

    // ---- Step 1: every card stage carries the shared Active-shape invariants -----------------

    [Theory]
    [MemberData(nameof(ActiveShapedViews))]
    public void Every_active_shaped_stage_has_the_status_line_and_no_decision_options(Release1RoomWithNoNameViewModel view)
    {
        Assert.Equal(Release1RoomWithNoNamePresentation.Title, view.Title);
        Assert.StartsWith("Status: ", view.Body, StringComparison.Ordinal);
        AssertAtMostSixBodyLines(view);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    public static IEnumerable<object[]> ActiveShapedViews()
    {
        var (awaitingStory, _) = StoryWithAssignment(Release1MissionState.Accepted);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(awaitingStory, Release1RoomWithNoNameOfferStatus.Inactive, null, null) };

        var (activeStory, _) = StoryWithAssignment(Release1MissionState.Active);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(activeStory, Release1RoomWithNoNameOfferStatus.Inactive, null, null) };

        var (custodyStory, custodyAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var custodyProgress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, custodyAssignment.Attempt, true, true, false, false, null, null, null);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(custodyStory, Release1RoomWithNoNameOfferStatus.Inactive, null, custodyProgress) };

        var (stowedStory, stowedAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var stowedProgress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, stowedAssignment.Attempt, true, true, true, false, 100d, "closet-a", null);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(stowedStory, Release1RoomWithNoNameOfferStatus.Inactive, null, stowedProgress) };

        var (releasedStory, releasedAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var releasedProgress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, releasedAssignment.Attempt, true, true, true, true, 100d, "closet-a", null);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(releasedStory, Release1RoomWithNoNameOfferStatus.Inactive, null, releasedProgress) };

        var (satisfiedStory, _) = StoryWithAssignment(Release1MissionState.Satisfied);
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(satisfiedStory, Release1RoomWithNoNameOfferStatus.Inactive, null, null) };

        var (ambiguousStory, ambiguousAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = ambiguousStory with { NativeEffects = new[] { BlockedEffect(ambiguousAssignment.Attempt) } };
        yield return new object[] { Release1RoomWithNoNamePresentation.Build(blocked, Release1RoomWithNoNameOfferStatus.Inactive, null, null) };
    }

    private static void AssertAtMostSixBodyLines(Release1RoomWithNoNameViewModel view) =>
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);

    private static void AssertPlayerCopy(Release1RoomWithNoNameViewModel view)
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

    private static (Release1StoryState Story, Release1RoomWithNoNameAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, int attempt = 1)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(attempt, Release1RoomWithNoNameAssignmentMode.Primary);

        story = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.RoomWithNoName
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "room-with-no-name-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = 172d,
                    LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "test-rn-reward-receipt" : null
                }
                : mission).ToArray(),
            RoomWithNoNameAssignments = new[] { assignment }
        };

        return (story, assignment);
    }

    private static Release1StoryState StoryWithAssignments(
        Release1MissionState missionState, int attempt, params Release1RoomWithNoNameAssignment[] assignments)
    {
        var story = AcceptedStory();
        return story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.RoomWithNoName
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "room-with-no-name-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = missionState is Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive ? null : 172d,
                    LastOutcome = Release1MissionOutcome.None
                }
                : mission).ToArray(),
            RoomWithNoNameAssignments = assignments
        };
    }

    private static Release1RoomWithNoNameAssignment MakeDistinctAssignment(int attempt, Release1RoomWithNoNameAssignmentMode mode, string tag)
    {
        var transitionKind = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.RoomWithNoName, attempt, transitionKind, $"test-rn-authorization-{tag}").Value;
        return new Release1RoomWithNoNameAssignment(
            Release1MissionCatalog.RoomWithNoName,
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
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            Release1RoomWithNoNameAssignment.OneInGameDayMinutes,
            Release1RoomWithNoNameAssignment.RoomRewardMultiplier);
    }

    private static Release1RoomWithNoNameAssignment MakeAssignment(int attempt, Release1RoomWithNoNameAssignmentMode mode)
    {
        var transitionKind = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.RoomWithNoName, attempt, transitionKind, "test-rn-authorization").Value;
        return new Release1RoomWithNoNameAssignment(
            Release1MissionCatalog.RoomWithNoName,
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
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            Release1RoomWithNoNameAssignment.OneInGameDayMinutes,
            Release1RoomWithNoNameAssignment.RoomRewardMultiplier);
    }

    private static Release1NativeEffectJournalEntry BlockedEffect(int attempt) => new(
        "rn-effect-blocked-v1",
        Release1MissionCatalog.RoomWithNoName,
        attempt,
        "CargoTransfer",
        "source-drop",
        "oc-room-with-no-name",
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
