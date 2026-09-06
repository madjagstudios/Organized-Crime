using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopePresentationTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_is_hidden()
    {
        var view = Release1TheEnvelopePresentation.Build(null, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.Hidden, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Not_accepted_relationship_is_hidden_even_when_offered()
    {
        var story = UnstartedStory();

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.Hidden, view.Stage);
    }

    [Fact]
    public void Offer_is_shown_when_available_and_not_yet_assigned()
    {
        var story = AcceptedStory();

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, null, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.Offer, view.Stage);
        Assert.True(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Equal(Release1TheEnvelopePresentation.OfferText, view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Unavailable_offer_with_no_assignment_is_hidden()
    {
        var story = AcceptedStory();

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Ineligible, null, null);

        Assert.False(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.Hidden, view.Stage);
    }

    [Theory]
    [InlineData(Release1TheEnvelopeAssignmentMode.Primary, null, 24d, "one day", 20000d)]
    [InlineData(Release1TheEnvelopeAssignmentMode.MakeGood, "Make good, attempt 1", 24d, "one day", 10000d)]
    [InlineData(Release1TheEnvelopeAssignmentMode.Recovery, "Recovery, attempt 1", null, "none", 5000d)]
    public void Reviewed_quote_shows_full_terms_ending_in_the_question(
        Release1TheEnvelopeAssignmentMode mode, string? headerLine, double? deadlineHours, string deadlineText, double amount)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, mode);
        var quote = new Release1TheEnvelopeQuote(assignment, deadlineHours);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, quote, null);

        Assert.True(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.Review, view.Stage);
        Assert.False(view.CanReview);
        Assert.True(view.CanAccept);
        Assert.True(view.CanDefer);
        Assert.Equal(amount, assignment.AmountWholeDollars);
        var lines = view.Body.Split('\n');
        if (headerLine is null)
            Assert.DoesNotContain("attempt", lines[0], StringComparison.Ordinal);
        else
            Assert.Equal(headerLine, lines[0]);
        // The stack count is derived from the tier's amount at 1000 per stack: 20, 10, 5.
        var stackCount = Release1TheEnvelopePresentation.StackCount(amount);
        Assert.Contains($"${Release1TheEnvelopePresentation.Dollars(amount)} in cash, {stackCount} stacks.", view.Body, StringComparison.Ordinal);
        Assert.Contains(Release1TheEnvelopePresentation.ClosetTermsText(amount), view.Body, StringComparison.Ordinal);
        Assert.Contains($"Deadline: {deadlineText}", view.Body, StringComparison.Ordinal);
        Assert.Contains(Release1TheEnvelopePresentation.PaymentText, view.Body, StringComparison.Ordinal);
        Assert.Contains(Release1TheEnvelopePresentation.SourceText, view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Are you taking it?", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Drop:", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("slot", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.HoldRoomKey, view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Accepted_but_not_yet_active_shows_awaiting_activation()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Accepted);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.AwaitingActivation, view.Stage);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        Assert.Contains("Status: Leave the whole amount in the closet before the window closes.", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Active_without_progress_shows_the_default_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Active, view.Stage);
        Assert.Contains("Status: Leave the whole amount in the closet before the window closes.", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Shortfall_of_fifteen_hundred_shows_the_many_format()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 500, 1500, false);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1TheEnvelopeCardStage.Shortfall, view.Stage);
        Assert.Contains("Status: Part of the envelope is there. $1500 more still needed.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Shortfall_of_eighty_shows_the_small_format()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 1920, 80, false);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1TheEnvelopeCardStage.Shortfall, view.Stage);
        Assert.Contains("Status: Part of the envelope is there. $80 more still needed.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Spread_shows_the_put_it_together_status()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 0, null, true);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1TheEnvelopeCardStage.Spread, view.Stage);
        Assert.Contains("Status: The amount is spread across more than one closet. Put it together.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Blocked_native_effect_is_shown_as_ambiguous_not_retryable()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = story with { NativeEffects = new[] { BlockedEffect(assignment.Attempt) } };

        var view = Release1TheEnvelopePresentation.Build(blocked, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Ambiguous, view.Stage);
        Assert.Contains("Stopped on conflicting evidence. It will not retry.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Satisfied_mission_shows_completed()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Satisfied);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Completed, view.Stage);
        Assert.Contains("Status: Complete. The envelope is delivered.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void No_deadline_recorded_reports_none()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var noDeadline = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.TheEnvelope
                ? mission with { DeadlineGameTimeHours = null }
                : mission).ToArray()
        };

        var view = Release1TheEnvelopePresentation.Build(noDeadline, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Contains("Deadline: none", view.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(24d, "one day")]
    [InlineData(48d, "48 hours")]
    public void Deadline_formats_the_actual_duration_instead_of_a_fixed_label(double durationHours, string expectedDeadlineText)
    {
        // StoryWithAssignment's default fixture happens to produce a 24 hour duration
        // (AcceptedGameTimeHours 100, DeadlineGameTimeHours 124), which is exactly why the old
        // hardcoded "24 in game hours" literal passed every prior test without anyone noticing it
        // never actually read the duration. The 48 hour case is what pins the real fix: it fails
        // against the old literal and passes only once Deadline formats the value it is given. A
        // 24 hour window now reads "one day" per the shared Release1PlayerCopy.Deadline convention.
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);
        var withDuration = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.TheEnvelope
                ? mission with { DeadlineGameTimeHours = mission.AcceptedGameTimeHours!.Value + durationHours }
                : mission).ToArray()
        };

        var view = Release1TheEnvelopePresentation.Build(withDuration, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Contains($"Deadline: {expectedDeadlineText}", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_defaults_to_persistence_deferred_when_no_stage_is_given()
    {
        var view = Release1TheEnvelopePresentation.Build(
            null, Release1TheEnvelopeOfferStatus.Inactive, null, null, "Acceptance was not persisted; no assignment was started.");

        Assert.True(view.Visible);
        Assert.Equal(Release1TheEnvelopeCardStage.PersistenceDeferred, view.Stage);
        Assert.Equal("Acceptance was not persisted; no assignment was started.", view.Body);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Feedback_honors_an_explicit_stage()
    {
        var view = Release1TheEnvelopePresentation.Build(
            null, Release1TheEnvelopeOfferStatus.Inactive, null, null, "The decision was rejected.", Release1TheEnvelopeCardStage.DecisionRejected);

        Assert.Equal(Release1TheEnvelopeCardStage.DecisionRejected, view.Stage);
        Assert.Equal("The decision was rejected.", view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void Whitespace_only_feedback_is_ignored()
    {
        var view = Release1TheEnvelopePresentation.Build(null, Release1TheEnvelopeOfferStatus.Inactive, null, null, "   ");

        Assert.Equal(Release1TheEnvelopeCardStage.Hidden, view.Stage);
    }

    // ---- OC-61 Task 5 Step 1: the closet copy table -------------------------------------------

    [Fact]
    public void Every_envelope_string_passes_player_copy_normalization_unchanged()
    {
        var fields = typeof(Release1TheEnvelopePresentation)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToArray();

        Assert.True(fields.Length > 0, "expected at least one player-facing string constant");
        foreach (var field in fields) AssertPlayerCopy((string)field.GetRawConstantValue()!);
    }

    [Fact]
    public void The_offer_card_shows_the_closet_offer_text()
    {
        var story = AcceptedStory();

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Offer, view.Stage);
        Assert.Equal(
            "One last thing before the family meets to discuss your future. Payment. You think we just allow anyone off the street in our family for free?",
            view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void The_terms_card_shows_the_amount_the_closet_the_deadline_the_payment_the_source_and_the_question_and_no_drop_line()
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, Release1TheEnvelopeAssignmentMode.Primary);
        var quote = new Release1TheEnvelopeQuote(assignment, 24d);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, quote, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Review, view.Stage);
        var lines = view.Body.Split('\n');
        Assert.Equal("$20000 in cash, 20 stacks.", lines[0]);
        Assert.Contains("Closet: one closet in the storage room at HQ. All 20 stacks in it.", view.Body, StringComparison.Ordinal);
        Assert.Contains("Deadline: one day", view.Body, StringComparison.Ordinal);
        Assert.Contains("We don't pay you. You pay us. You want in? This is the cost.", view.Body, StringComparison.Ordinal);
        Assert.Contains("Your own fucking cash. You have to prove to us you are worth the effort.", view.Body, StringComparison.Ordinal);
        Assert.EndsWith("Are you taking it?", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Drop:", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("slot", view.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(assignment.HoldRoomKey, view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void A_make_good_terms_card_begins_with_the_shared_attempt_header()
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(1, Release1TheEnvelopeAssignmentMode.MakeGood);
        var quote = new Release1TheEnvelopeQuote(assignment, 24d);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Available, quote, null);

        Assert.Equal("Make good, attempt 1", view.Body.Split('\n')[0]);
        Release1CopyAssertions.AssertAttemptHeaderAndFactLineCap(view.Body, "Make good, attempt 1");
        AssertPlayerCopy(view);
    }

    [Fact]
    public void The_active_card_shows_the_closet_line_and_the_active_status()
    {
        var (story, _) = StoryWithAssignment(Release1MissionState.Active);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, null);

        Assert.Equal(Release1TheEnvelopeCardStage.Active, view.Stage);
        Assert.Contains("Status: Leave the whole amount in the closet before the window closes.", view.Body, StringComparison.Ordinal);
        Assert.Contains("Closet: any one storage at HQ", view.Body, StringComparison.Ordinal);
        Release1CopyAssertions.AssertAtMostSixLines(view.Body);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void The_shortfall_card_shows_the_remaining_amount()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 500, 1500, false);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1TheEnvelopeCardStage.Shortfall, view.Stage);
        Assert.Contains("Status: Part of the envelope is there. $1500 more still needed.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void The_spread_card_shows_the_closet_spread_status()
    {
        var (story, assignment) = StoryWithAssignment(Release1MissionState.Active);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 0, null, true);

        var view = Release1TheEnvelopePresentation.Build(story, Release1TheEnvelopeOfferStatus.Inactive, null, progress);

        Assert.Equal(Release1TheEnvelopeCardStage.Spread, view.Stage);
        Assert.Contains("Status: The amount is spread across more than one closet. Put it together.", view.Body, StringComparison.Ordinal);
        AssertPlayerCopy(view);
    }

    [Fact]
    public void The_blocked_card_shows_the_ambiguous_status_and_the_completed_card_shows_completed()
    {
        var (blockedStory, blockedAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = blockedStory with { NativeEffects = new[] { BlockedEffect(blockedAssignment.Attempt) } };
        var blockedView = Release1TheEnvelopePresentation.Build(blocked, Release1TheEnvelopeOfferStatus.Inactive, null, null);
        Assert.Equal(Release1TheEnvelopeCardStage.Ambiguous, blockedView.Stage);
        Assert.Contains("Status: Stopped on conflicting evidence. It will not retry.", blockedView.Body, StringComparison.Ordinal);
        AssertPlayerCopy(blockedView);

        var (satisfiedStory, _) = StoryWithAssignment(Release1MissionState.Satisfied);
        var satisfiedView = Release1TheEnvelopePresentation.Build(satisfiedStory, Release1TheEnvelopeOfferStatus.Inactive, null, null);
        Assert.Equal(Release1TheEnvelopeCardStage.Completed, satisfiedView.Stage);
        Assert.Contains("Status: Complete. The envelope is delivered.", satisfiedView.Body, StringComparison.Ordinal);
        AssertPlayerCopy(satisfiedView);
    }

    [Fact]
    public void There_is_no_no_drop_stage_and_no_refusal_text()
    {
        Assert.DoesNotContain("NoDrop", Enum.GetNames(typeof(Release1TheEnvelopeCardStage)));
        Assert.Null(typeof(Release1TheEnvelopePresentation).GetField("RefusalText"));
    }

    [Fact]
    public void Dollars_renders_a_whole_number_with_no_decimal()
    {
        Assert.Equal("2000", Release1TheEnvelopePresentation.Dollars(2000d));
        Assert.Equal("80", Release1TheEnvelopePresentation.Dollars(80d));
        Assert.Equal("1500", Release1TheEnvelopePresentation.Dollars(1500d));
    }

    [Fact]
    public void Presenter_forwards_review_accept_and_defer_to_the_real_service_only()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        var presenter = new Release1TheEnvelopePresenter(harness.Service, harness.Story);

        var reviewResult = presenter.TryReview();
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, reviewResult.Status);
        Assert.Equal(Release1TheEnvelopeCardStage.Review, presenter.View.Stage);

        var deferResult = presenter.TryDefer();
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Deferred, deferResult.Status);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Presenter_accept_activates_the_mission()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var presenter = new Release1TheEnvelopePresenter(harness.Service, harness.Story);
        presenter.TryReview();

        var acceptResult = presenter.TryAccept();

        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, acceptResult.Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void ClearFeedback_resets_a_stale_persistence_deferred_message()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        var presenter = new Release1TheEnvelopePresenter(harness.Service, harness.Story);
        presenter.TryReview();
        harness.Service.OnSaveStart();

        var decision = presenter.TryAccept();
        Assert.Equal(Release1TheEnvelopeDecisionStatus.PersistenceDeferred, decision.Status);
        Assert.Equal(Release1TheEnvelopeCardStage.PersistenceDeferred, presenter.View.Stage);

        presenter.ClearFeedback();

        Assert.NotEqual(Release1TheEnvelopeCardStage.PersistenceDeferred, presenter.View.Stage);
    }

    [Fact]
    public void Disposed_presenter_reports_hidden()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        var presenter = new Release1TheEnvelopePresenter(harness.Service, harness.Story);

        presenter.Dispose();

        Assert.False(presenter.View.Visible);
    }

    // ---- Step 1: every card stage carries the shared Active-shape invariants -----------------

    [Theory]
    [MemberData(nameof(ActiveShapedViews))]
    public void Every_active_shaped_stage_has_the_status_line_and_no_decision_options(Release1TheEnvelopeViewModel view)
    {
        Assert.Equal(Release1TheEnvelopePresentation.Title, view.Title);
        Assert.StartsWith("Status: ", view.Body, StringComparison.Ordinal);
        Assert.False(view.CanReview);
        Assert.False(view.CanAccept);
        Assert.False(view.CanDefer);
        AssertPlayerCopy(view);
    }

    public static IEnumerable<object[]> ActiveShapedViews()
    {
        var (awaitingStory, _) = StoryWithAssignment(Release1MissionState.Accepted);
        yield return new object[] { Release1TheEnvelopePresentation.Build(awaitingStory, Release1TheEnvelopeOfferStatus.Inactive, null, null) };

        var (activeStory, _) = StoryWithAssignment(Release1MissionState.Active);
        yield return new object[] { Release1TheEnvelopePresentation.Build(activeStory, Release1TheEnvelopeOfferStatus.Inactive, null, null) };

        var (shortfallStory, shortfallAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var shortfallProgress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, shortfallAssignment.Attempt, 500, 1500, false);
        yield return new object[] { Release1TheEnvelopePresentation.Build(shortfallStory, Release1TheEnvelopeOfferStatus.Inactive, null, shortfallProgress) };

        var (spreadStory, spreadAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var spreadProgress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, spreadAssignment.Attempt, 0, null, true);
        yield return new object[] { Release1TheEnvelopePresentation.Build(spreadStory, Release1TheEnvelopeOfferStatus.Inactive, null, spreadProgress) };

        var (satisfiedStory, _) = StoryWithAssignment(Release1MissionState.Satisfied);
        yield return new object[] { Release1TheEnvelopePresentation.Build(satisfiedStory, Release1TheEnvelopeOfferStatus.Inactive, null, null) };

        var (ambiguousStory, ambiguousAssignment) = StoryWithAssignment(Release1MissionState.Active);
        var blocked = ambiguousStory with { NativeEffects = new[] { BlockedEffect(ambiguousAssignment.Attempt) } };
        yield return new object[] { Release1TheEnvelopePresentation.Build(blocked, Release1TheEnvelopeOfferStatus.Inactive, null, null) };
    }

    private static void AssertPlayerCopy(Release1TheEnvelopeViewModel view)
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

    private static (Release1StoryState Story, Release1TheEnvelopeAssignment Assignment) StoryWithAssignment(
        Release1MissionState missionState, int attempt = 1)
    {
        var story = AcceptedStory();
        var assignment = MakeAssignment(attempt, Release1TheEnvelopeAssignmentMode.Primary);

        story = story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.TheEnvelope
                ? mission with
                {
                    State = missionState,
                    Attempt = attempt,
                    TermsVersion = "the-envelope-v1",
                    AcceptedGameTimeHours = 100d,
                    DeadlineGameTimeHours = 124d,
                    LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "test-te-reward-receipt" : null
                }
                : mission).ToArray(),
            TheEnvelopeAssignments = new[] { assignment }
        };

        return (story, assignment);
    }

    private static Release1TheEnvelopeAssignment MakeAssignment(int attempt, Release1TheEnvelopeAssignmentMode mode)
    {
        var transitionKind = mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.TheEnvelope, attempt, transitionKind, "test-te-authorization").Value;
        return new Release1TheEnvelopeAssignment(
            Release1MissionCatalog.TheEnvelope,
            attempt,
            mode,
            authorization,
            Release1TheEnvelopeAssignment.AmountFor(mode),
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : Release1TheEnvelopeAssignment.TimedStageGameMinutes);
    }

    private static Release1NativeEffectJournalEntry BlockedEffect(int attempt) => new(
        "te-effect-blocked-v1",
        Release1MissionCatalog.TheEnvelope,
        attempt,
        "CashTransfer",
        "handoff-drop",
        "oc-the-envelope",
        "cash-identity",
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
