using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PresentationPlannerTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Null_story_with_intro_hidden_yields_empty_plan()
    {
        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(null, false, false, null, null));

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Null_story_with_intro_visible_yields_no_message_and_a_decision_carrying_the_offer_as_its_prompt()
    {
        // The offer text is sent once, as the decision's prompt (S1API sends the prompt as a message
        // on the boundary's behalf); it is not also queued as a separate passive message, or the
        // offer would show twice.
        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(null, true, false, null, null));

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-intro", plan.Decision!.Id);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal(2, plan.Decision.Options.Count);
        Assert.Equal("Accept", plan.Decision.Options[0].Label);
        Assert.Equal(Release1PresentationCommand.IntroAccept, plan.Decision.Options[0].Command);
        Assert.Equal("Not now", plan.Decision.Options[1].Label);
        Assert.Equal(Release1PresentationCommand.IntroDefer, plan.Decision.Options[1].Command);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Unstarted_story_with_intro_visible_yields_the_same_offer_as_null_story()
    {
        var story = UnstartedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, true, false, null, null));

        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-intro", plan.Decision!.Id);
        AssertPlayerCopy(plan.Decision.Prompt);
    }

    [Fact]
    public void Unstarted_story_with_intro_hidden_yields_no_decision()
    {
        var story = UnstartedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, null, null));

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Unstarted_relationship_with_eligibility_pending_and_intro_hidden_yields_no_decision_and_marks_undetermined()
    {
        // While intro eligibility is still being observed after a reload, the prompt is not yet
        // known to be visible or hidden for good: the planner must emit no decision, but flag the
        // plan as undetermined so the projector holds off on clearing anything native restored.
        var story = UnstartedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, true, null, null));

        Assert.Empty(plan.Messages);
        Assert.Null(plan.Decision);
        Assert.True(plan.DecisionUndetermined);
    }

    [Fact]
    public void Unstarted_relationship_with_intro_visible_ignores_pending_and_yields_a_determined_decision()
    {
        var story = UnstartedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, true, true, null, null));

        Assert.NotNull(plan.Decision);
        Assert.False(plan.DecisionUndetermined);
    }

    [Fact]
    public void Deferred_relationship_with_intro_hidden_yields_deferred_acknowledgement_with_no_decision()
    {
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroDeferred, "test-defer-receipt").Value;
        var command = new Release1StoryCommand(
            Guid.NewGuid(), 1, PlayerId, Release1MissionCatalog.IntroScopeKey, 0,
            Release1TransitionKind.IntroDeferred, "test-defer-receipt", correlation);
        var story = Release1StoryTransitions.Apply(null, command).State!;

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, null, null));

        var message = Assert.Single(plan.Messages);
        AssertPlayerCopy(message.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1MissionCatalog.IntroScopeKey, parsed.MissionKey);
        Assert.Equal(0, parsed.Attempt);
        Assert.Equal(Release1TransitionKind.IntroDeferred, parsed.TransitionKind);
        Assert.Null(plan.Decision);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Deferred_relationship_with_eligibility_pending_and_intro_hidden_still_emits_acknowledgement_and_marks_undetermined()
    {
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroDeferred, "test-defer-receipt").Value;
        var command = new Release1StoryCommand(
            Guid.NewGuid(), 1, PlayerId, Release1MissionCatalog.IntroScopeKey, 0,
            Release1TransitionKind.IntroDeferred, "test-defer-receipt", correlation);
        var story = Release1StoryTransitions.Apply(null, command).State!;

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, true, null, null));

        var message = Assert.Single(plan.Messages);
        AssertPlayerCopy(message.Text);
        Assert.Null(plan.Decision);
        Assert.True(plan.DecisionUndetermined);
    }

    [Fact]
    public void Deferred_relationship_with_intro_visible_yields_deferred_acknowledgement_and_a_nudge_decision()
    {
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroDeferred, "test-defer-receipt").Value;
        var command = new Release1StoryCommand(
            Guid.NewGuid(), 1, PlayerId, Release1MissionCatalog.IntroScopeKey, 0,
            Release1TransitionKind.IntroDeferred, "test-defer-receipt", correlation);
        var story = Release1StoryTransitions.Apply(null, command).State!;

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, true, false, null, null));

        var message = Assert.Single(plan.Messages);
        AssertPlayerCopy(message.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1MissionCatalog.IntroScopeKey, parsed.MissionKey);
        Assert.Equal(0, parsed.Attempt);
        Assert.Equal(Release1TransitionKind.IntroDeferred, parsed.TransitionKind);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-intro-deferred", plan.Decision!.Id);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal(2, plan.Decision.Options.Count);
        Assert.Equal("Accept", plan.Decision.Options[0].Label);
        Assert.Equal(Release1PresentationCommand.IntroAccept, plan.Decision.Options[0].Command);
        Assert.Equal("Not now", plan.Decision.Options[1].Label);
        Assert.Equal(Release1PresentationCommand.IntroDefer, plan.Decision.Options[1].Command);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Accepted_relationship_yields_accepted_acknowledgement()
    {
        var story = AcceptedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, null, null));

        var message = Assert.Single(plan.Messages);
        AssertPlayerCopy(message.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1MissionCatalog.IntroScopeKey, parsed.MissionKey);
        Assert.Equal(0, parsed.Attempt);
        Assert.Equal(Release1TransitionKind.IntroAccepted, parsed.TransitionKind);
        Assert.Null(plan.Decision);
    }

    [Fact]
    public void Accepted_relationship_with_eligibility_pending_still_yields_a_determined_plan()
    {
        // Once the relationship is Accepted, the intro decision no longer applies at all, so a
        // still-pending eligibility observation must not mark the plan undetermined.
        var story = AcceptedStory(PlayerId);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, true, null, null));

        Assert.False(plan.DecisionUndetermined);
    }

    [Fact]
    public void Small_courtesy_can_review_yields_only_the_intro_ack_message_and_a_review_decision_carrying_the_offer_as_its_prompt()
    {
        // The offer body is sent once, as the review decision's prompt; it is not also queued as a
        // separate passive message, or the offer would show twice. Only the (unrelated) intro-accepted
        // acknowledgement message remains.
        var story = AcceptedStory(PlayerId);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.Offer, "Small Courtesy", "Terms are ready.", true, false, true);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, null));

        var message = Assert.Single(plan.Messages);
        AssertPlayerCopy(message.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1MissionCatalog.IntroScopeKey, parsed.MissionKey);
        Assert.Equal(Release1TransitionKind.IntroAccepted, parsed.TransitionKind);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-sc-review", plan.Decision!.Id);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal(Release1PlayerCopy.Normalize("Terms are ready."), plan.Decision.Prompt);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal("Review terms", option.Label);
        Assert.Equal(Release1PresentationCommand.SmallCourtesyReview, option.Command);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void Small_courtesy_can_accept_yields_decision_with_accept_and_defer_and_a_prompt_carrying_the_accept_body_and_question()
    {
        var story = AcceptedStory(PlayerId);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.Review, "Small Courtesy", "Review complete.\nAre you in?", false, true, true);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, null));

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-sc-decision", plan.Decision!.Id);
        AssertPlayerCopy(plan.Decision.Prompt);
        // The accept prompt carries the reviewed terms forward verbatim: Release1SmallCourtesyPresentation
        // already ends the body with the question, so the planner appends nothing further.
        Assert.Contains(Release1PlayerCopy.Normalize("Review complete."), plan.Decision.Prompt, StringComparison.Ordinal);
        Assert.EndsWith("Are you in?", plan.Decision.Prompt, StringComparison.Ordinal);
        Assert.Equal(2, plan.Decision.Options.Count);
        Assert.Equal("Accept", plan.Decision.Options[0].Label);
        Assert.Equal(Release1PresentationCommand.SmallCourtesyAccept, plan.Decision.Options[0].Command);
        Assert.Equal("Not now", plan.Decision.Options[1].Label);
        Assert.Equal(Release1PresentationCommand.SmallCourtesyDefer, plan.Decision.Options[1].Command);
    }

    [Fact]
    public void Mission_accepted_with_assignment_yields_quest_with_active_delivery_and_inactive_payment_entries()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Accepted);
        var assignment = MakeAssignment(PlayerId, 1);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, assignment));

        var quest = Assert.Single(plan.Quests);
        Assert.Equal(Release1MissionCatalog.SmallCourtesy, quest.Key);
        Assert.Equal("Small Courtesy", quest.Title);
        Assert.Equal(2, quest.Entries.Count);

        var delivery = quest.Entries[0];
        Assert.Equal(Release1DesiredEntryState.Active, delivery.State);
        Assert.Equal($"Deliver one {assignment.PackagingName} of {assignment.ProductName} to {assignment.DeadDropName}", delivery.Text);
        Assert.NotNull(delivery.Marker);
        Assert.Equal((float)assignment.DeadDropX, delivery.Marker!.X);
        Assert.Equal((float)assignment.DeadDropY, delivery.Marker.Y);
        Assert.Equal((float)assignment.DeadDropZ, delivery.Marker.Z);
        AssertPlayerCopy(delivery.Text);

        var payment = quest.Entries[1];
        Assert.Equal(Release1DesiredEntryState.Inactive, payment.State);
        Assert.Equal("Wait for payment", payment.Text);
        Assert.Null(payment.Marker);

        Assert.Equal(2, plan.Messages.Count);
        Assert.Contains(plan.Messages, message => message.CorrelationId.Contains("MissionAccepted", StringComparison.Ordinal));
    }

    [Fact]
    public void Assignment_null_with_mission_accepted_emits_no_quest()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Accepted);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, null));

        Assert.Empty(plan.Quests);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyCardStage.AwaitingFirstSave)]
    [InlineData(Release1SmallCourtesyCardStage.AwaitingSecondSave)]
    [InlineData(Release1SmallCourtesyCardStage.AwaitingFinalSave)]
    public void Awaiting_save_stages_map_delivery_complete_and_payment_active(Release1SmallCourtesyCardStage stage)
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Active);
        var assignment = MakeAssignment(PlayerId, 1);
        var view = new Release1SmallCourtesyViewModel(true, stage, "Small Courtesy", "Working.", false, false, false);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, assignment));

        var quest = Assert.Single(plan.Quests);
        var delivery = quest.Entries[0];
        Assert.Equal(Release1DesiredEntryState.Complete, delivery.State);
        Assert.Null(delivery.Marker);

        var payment = quest.Entries[1];
        Assert.Equal(Release1DesiredEntryState.Active, payment.State);
        Assert.Equal("Payment is on its way", payment.Text);
        Assert.Null(payment.Marker);
    }

    [Fact]
    public void Ambiguous_stage_maps_delivery_on_hold_with_no_marker()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Active);
        var assignment = MakeAssignment(PlayerId, 1);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.Ambiguous, "Small Courtesy", "Blocked.", false, false, false);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, assignment));

        var quest = Assert.Single(plan.Quests);
        var delivery = quest.Entries[0];
        Assert.Equal("Delivery on hold", delivery.Text);
        Assert.Null(delivery.Marker);
        AssertPlayerCopy(delivery.Text);

        var payment = quest.Entries[1];
        Assert.Equal("Wait for payment", payment.Text);
    }

    [Fact]
    public void Satisfied_mission_yields_both_entries_complete_and_the_complete_message_at_current_attempt()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Satisfied, attempt: 2);
        var assignment = MakeAssignment(PlayerId, 2);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.Completed, "Small Courtesy", "Done.", false, false, false);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, view, assignment));

        var quest = Assert.Single(plan.Quests);
        Assert.All(quest.Entries, entry => Assert.Equal(Release1DesiredEntryState.Complete, entry.State));

        Assert.Equal(2, plan.Messages.Count);
        var message = Assert.Single(plan.Messages, candidate => candidate.CorrelationId.Contains("MissionCompleted", StringComparison.Ordinal));
        AssertPlayerCopy(message.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1MissionCatalog.SmallCourtesy, parsed.MissionKey);
        Assert.Equal(2, parsed.Attempt);
        Assert.Equal(Release1TransitionKind.MissionCompleted, parsed.TransitionKind);
    }

    [Fact]
    public void Null_small_courtesy_view_emits_no_small_courtesy_content_even_when_mission_is_satisfied()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Satisfied);
        var assignment = MakeAssignment(PlayerId, 1);

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, null, assignment));

        Assert.Empty(plan.Quests);
        var message = Assert.Single(plan.Messages);
        Assert.True(Release1LogicalCorrelation.TryParse(message.CorrelationId, out var parsed));
        Assert.Equal(Release1TransitionKind.IntroAccepted, parsed.TransitionKind);
    }

    [Fact]
    public void Plan_is_deterministic_for_equal_inputs()
    {
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Accepted);
        var assignment = MakeAssignment(PlayerId, 1);
        var view = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        var inputs = new Release1PresentationInputs(story, true, false, view, assignment);

        var first = Release1PresentationPlanner.Build(inputs);
        var second = Release1PresentationPlanner.Build(inputs);

        AssertPlansEqual(first, second);
    }

    [Fact]
    public void Presentation_receipts_do_not_change_the_plan()
    {
        var story = AcceptedStory(PlayerId);
        var withoutReceipt = Release1PresentationPlanner.Build(new Release1PresentationInputs(story, false, false, null, null));

        var introAccepted = Assert.Single(withoutReceipt.Messages);
        var storyWithReceipt = story with
        {
            PresentationReceipts = new[] { new Release1PresentationReceipt(introAccepted.CorrelationId, story.Revision) }
        };

        var withReceipt = Release1PresentationPlanner.Build(new Release1PresentationInputs(storyWithReceipt, false, false, null, null));

        AssertPlansEqual(withoutReceipt, withReceipt);
        Assert.Single(withReceipt.Messages);
    }

    [Fact]
    public void A_reviewable_wrong_address_offer_becomes_the_review_decision_prompt()
    {
        var inputs = WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.Offer,
            "One of ours went to the wrong address. I need it collected and put where it belongs.",
            canReview: true));

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-wa-review", plan.Decision!.Id);
        Assert.Equal(
            "One of ours went to the wrong address. I need it collected and put where it belongs.",
            plan.Decision.Prompt);
        var option = Assert.Single(plan.Decision.Options);
        Assert.Equal("Review terms", option.Label);
        Assert.Equal(Release1PresentationCommand.WrongAddressReview, option.Command);
        AssertPlayerCopy(plan.Decision.Prompt);
    }

    [Fact]
    public void The_refusal_stays_on_the_review_decision_so_the_player_can_try_again()
    {
        var inputs = WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.InsufficientDrops,
            "I need two clear drops before I can point you at one. Try me again when the town has room.",
            canReview: true));

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.Equal("release1-wa-review", plan.Decision!.Id);
        Assert.Contains("two clear drops", plan.Decision.Prompt, StringComparison.Ordinal);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Empty(plan.Quests);
    }

    [Fact]
    public void The_reviewed_terms_become_the_accept_decision_with_two_options()
    {
        var inputs = WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.Review, TermsBody(), canAccept: true, canDefer: true));

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.Equal("release1-wa-decision", plan.Decision!.Id);
        Assert.EndsWith("Are you taking it?", plan.Decision.Prompt, StringComparison.Ordinal);
        Assert.Equal(new[] { "Accept", "Not now" }, plan.Decision.Options.Select(option => option.Label));
        Assert.Equal(Release1PresentationCommand.WrongAddressAccept, plan.Decision.Options[0].Command);
        Assert.Equal(Release1PresentationCommand.WrongAddressDefer, plan.Decision.Options[1].Command);
        foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
    }

    [Fact]
    public void An_active_stage_projects_two_entries_with_the_source_marker()
    {
        var plan = Release1PresentationPlanner.Build(ActiveWrongAddressInputs(custody: false));

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.Equal("Wrong Address", quest.Title);
        Assert.Equal(2, quest.Entries.Count);
        Assert.Equal("Fix the fuckup and get the package from Drop A", quest.Entries[0].Text);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
        Assert.Equal(new Release1WorldPoint(1f, 2f, 3f), quest.Entries[0].Marker);
        Assert.Equal("Return it to Drop B", quest.Entries[1].Text);
        Assert.Equal(Release1DesiredEntryState.Inactive, quest.Entries[1].State);
        Assert.Null(quest.Entries[1].Marker);
        foreach (var entry in quest.Entries) AssertPlayerCopy(entry.Text);
    }

    [Fact]
    public void Custody_moves_the_marker_to_the_handoff_and_sends_the_custody_message_once()
    {
        var plan = Release1PresentationPlanner.Build(ActiveWrongAddressInputs(custody: true));

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.Equal(Release1DesiredEntryState.Complete, quest.Entries[0].State);
        Assert.Null(quest.Entries[0].Marker);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[1].State);
        Assert.Equal(new Release1WorldPoint(4f, 5f, 6f), quest.Entries[1].Marker);

        var custody = Assert.Single(plan.Messages, message => message.Text.StartsWith("We have eyes everywhere", StringComparison.Ordinal));
        Assert.Equal("We have eyes everywhere, we know you have the package. Take it to Drop B NOW.", custody.Text);
        Assert.True(Release1LogicalCorrelation.TryParse(custody.CorrelationId, out var correlation));
        Assert.Equal(Release1MissionCatalog.WrongAddress, correlation.MissionKey);
        Assert.Equal("presentation-nell-wa-custody-v1", correlation.ReceiptId);
        AssertPlayerCopy(custody.Text);
    }

    [Fact]
    public void A_make_good_active_stage_projects_two_entries_and_sends_the_accepted_message()
    {
        var plan = Release1PresentationPlanner.Build(MakeGoodActiveWrongAddressInputs(custody: false));

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.Equal(2, quest.Entries.Count);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
        Assert.Equal(Release1DesiredEntryState.Inactive, quest.Entries[1].State);
        var accepted = Assert.Single(plan.Messages, message => message.Text.StartsWith("The package is at", StringComparison.Ordinal));
        AssertPlayerCopy(accepted.Text);
    }

    [Fact]
    public void A_make_good_active_stage_with_custody_moves_the_marker_and_sends_the_custody_message()
    {
        var plan = Release1PresentationPlanner.Build(MakeGoodActiveWrongAddressInputs(custody: true));

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.Equal(Release1DesiredEntryState.Complete, quest.Entries[0].State);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[1].State);
        Assert.Equal(new Release1WorldPoint(4f, 5f, 6f), quest.Entries[1].Marker);
        var custody = Assert.Single(plan.Messages, message => message.Text.StartsWith("We have eyes everywhere", StringComparison.Ordinal));
        AssertPlayerCopy(custody.Text);
    }

    [Fact]
    public void A_satisfied_stage_completes_both_entries_and_sends_the_delivery_message()
    {
        var plan = Release1PresentationPlanner.Build(SatisfiedWrongAddressInputs());

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.All(quest.Entries, entry => Assert.Equal(Release1DesiredEntryState.Complete, entry.State));
        Assert.All(quest.Entries, entry => Assert.Null(entry.Marker));
        var delivered = Assert.Single(plan.Messages, message => message.Text.StartsWith("Received and paid", StringComparison.Ordinal));
        Assert.Equal("Received and paid. Mr. Selby noticed.", delivered.Text);
        AssertPlayerCopy(delivered.Text);
    }

    [Fact]
    public void A_blocked_stage_holds_the_second_entry_without_a_marker()
    {
        var plan = Release1PresentationPlanner.Build(BlockedWrongAddressInputs());

        var quest = Assert.Single(plan.Quests, q => q.Key == Release1MissionCatalog.WrongAddress);
        Assert.Equal("Delivery on hold", quest.Entries[1].Text);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[1].State);
        Assert.Null(quest.Entries[1].Marker);
    }

    [Fact]
    public void The_plan_is_deterministic_and_never_shows_two_decisions()
    {
        var inputs = ActiveWrongAddressInputs(custody: false);
        AssertPlansEqual(Release1PresentationPlanner.Build(inputs), Release1PresentationPlanner.Build(inputs));
        Assert.Null(Release1PresentationPlanner.Build(inputs).Decision);
    }

    [Fact]
    public void A_wrong_address_decision_never_coexists_with_a_small_courtesy_decision()
    {
        // Contrived input (both view models report a decision at once, which never happens for real
        // since Wrong Address only offers once Small Courtesy is Satisfied): the plan's Decision is a
        // single nullable field, so at most one can ever survive, and BuildWrongAddress runs after
        // BuildSmallCourtesy, so Wrong Address wins the assignment.
        var story = WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Offered);
        var smallCourtesyView = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Offer, "Small Courtesy", "Terms are ready.", true, false, true);
        var wrongAddressView = WrongAddressView(Release1WrongAddressCardStage.Offer, "One of ours went to the wrong address.", canReview: true);
        var inputs = new Release1PresentationInputs(story, false, false, smallCourtesyView, null, wrongAddressView, null, null);

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-wa-review", plan.Decision!.Id);
    }

    [Fact]
    public void Small_courtesy_satisfied_with_wrong_address_offered_yields_only_the_wrong_address_review_decision()
    {
        // The realistic combined state once Small Courtesy is done and Wrong Address has just been
        // offered: Small Courtesy is Satisfied (so it never contributes a decision, only its own
        // complete quest and message) and Wrong Address is Offered with its review prompt visible.
        // Only the Wrong Address review decision should come out, no Small Courtesy decision, and no
        // Wrong Address quest since its mission has not been Accepted yet.
        var story = WithWrongAddressState(
            WithSmallCourtesyState(AcceptedStory(PlayerId), Release1MissionState.Satisfied),
            Release1MissionState.Offered);
        var smallCourtesyAssignment = MakeAssignment(PlayerId, 1);
        var smallCourtesyView = new Release1SmallCourtesyViewModel(
            true, Release1SmallCourtesyCardStage.Completed, "Small Courtesy", "Done.", false, false, false);
        var wrongAddressView = WrongAddressView(
            Release1WrongAddressCardStage.Offer,
            "One of ours went to the wrong address. I need it collected and put where it belongs.",
            canReview: true);
        var inputs = new Release1PresentationInputs(
            story, false, false, smallCourtesyView, smallCourtesyAssignment, wrongAddressView, null, null);

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-wa-review", plan.Decision!.Id);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.WrongAddress);
    }

    [Fact]
    public void Every_wrong_address_string_in_every_stage_passes_player_copy()
    {
        foreach (var inputs in AllWrongAddressInputs())
        {
            var plan = Release1PresentationPlanner.Build(inputs);
            if (plan.Decision is not null)
            {
                foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
                foreach (var option in plan.Decision.Options) AssertPlayerCopy(option.Label);
            }
            foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
            foreach (var quest in plan.Quests)
            {
                AssertPlayerCopy(quest.Title);
                foreach (var entry in quest.Entries) AssertPlayerCopy(entry.Text);
            }
        }
    }

    [Fact]
    public void The_room_offer_is_the_review_decision_prompt_and_is_never_also_a_message()
    {
        var plan = Release1PresentationPlanner.Build(RoomInputs(RoomOfferView()));

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-rn-review", plan.Decision!.Id);
        Assert.Equal(Release1RoomWithNoNamePresentation.OfferText, plan.Decision.Prompt);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal("Review terms", Assert.Single(plan.Decision.Options).Label);
        Assert.Equal(Release1PresentationCommand.RoomWithNoNameReview, plan.Decision.Options[0].Command);
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1RoomWithNoNamePresentation.OfferText);
    }

    [Fact]
    public void Wrong_address_satisfied_with_room_offered_yields_only_the_room_review_decision()
    {
        // The realistic combined state once Wrong Address is done and Room With No Name has just
        // been offered: Wrong Address is Satisfied (so it never contributes a decision, only its own
        // complete quest and message) and Room With No Name is Offered with its review prompt
        // visible. Only the Room With No Name review decision should come out, no Wrong Address
        // decision.
        var story = WithRoomWithNoNameState(
            WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Satisfied),
            Release1MissionState.Offered);
        var wrongAddressAssignment = MakeWrongAddressAssignment(PlayerId, 1);
        var wrongAddressView = WrongAddressView(Release1WrongAddressCardStage.Completed, "Status line.");
        var roomView = RoomOfferView();
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            wrongAddressView, wrongAddressAssignment, null,
            roomView, null, null, DefaultHoldRoomMarker);

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-rn-review", plan.Decision!.Id);
        Assert.NotEqual("release1-wa-review", plan.Decision.Id);
    }

    [Fact]
    public void The_room_terms_decision_offers_accept_and_not_now()
    {
        var plan = Release1PresentationPlanner.Build(RoomInputs(RoomTermsView()));

        Assert.Equal("release1-rn-decision", plan.Decision!.Id);
        Assert.Collection(plan.Decision.Options,
            option => { Assert.Equal("Accept", option.Label); Assert.Equal(Release1PresentationCommand.RoomWithNoNameAccept, option.Command); },
            option => { Assert.Equal("Not now", option.Label); Assert.Equal(Release1PresentationCommand.RoomWithNoNameDefer, option.Command); });
        foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
    }

    [Fact]
    public void The_room_quest_has_three_entries_and_moves_its_marker_with_the_stage()
    {
        var accepted = RoomQuest(custody: false, holdSatisfied: false, Release1MissionState.Active);
        Assert.Equal(3, accepted.Entries.Count);
        Assert.Equal(Release1DesiredEntryState.Active, accepted.Entries[0].State);
        Assert.NotNull(accepted.Entries[0].Marker);
        Assert.Null(accepted.Entries[1].Marker);

        var holding = RoomQuest(custody: true, holdSatisfied: false, Release1MissionState.Active);
        Assert.Equal(Release1DesiredEntryState.Complete, holding.Entries[0].State);
        Assert.Equal(Release1DesiredEntryState.Active, holding.Entries[1].State);
        Assert.Equal(new Release1WorldPoint(11f, 12f, 13f), holding.Entries[1].Marker);

        var released = RoomQuest(custody: true, holdSatisfied: true, Release1MissionState.Active);
        Assert.Equal(Release1DesiredEntryState.Complete, released.Entries[1].State);
        Assert.Equal(Release1DesiredEntryState.Active, released.Entries[2].State);
        Assert.NotNull(released.Entries[2].Marker);

        var satisfied = RoomQuest(custody: true, holdSatisfied: true, Release1MissionState.Satisfied);
        Assert.All(satisfied.Entries, entry => Assert.Equal(Release1DesiredEntryState.Complete, entry.State));
        Assert.All(satisfied.Entries, entry => Assert.Null(entry.Marker));
        foreach (var entry in satisfied.Entries) AssertPlayerCopy(entry.Text);
    }

    [Fact]
    public void An_unresolved_hq_door_leaves_the_hold_entry_with_no_marker()
    {
        var quest = RoomQuest(custody: true, holdSatisfied: false, Release1MissionState.Active, holdRoomMarker: null);

        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[1].State);
        Assert.Null(quest.Entries[1].Marker);
    }

    [Fact]
    public void The_four_nell_milestones_are_emitted_once_each_with_their_own_correlations()
    {
        var plan = Release1PresentationPlanner.Build(RoomInputs(RoomActiveView(), custody: true, stowed: true, holdSatisfied: true, missionState: Release1MissionState.Satisfied));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("You have it. Take it to the room and leave it there.", texts);
        Assert.Contains("Good. It stays in the room until I call. One day. Fuck off until then.", texts);
        Assert.Contains("That is long enough. Take it to Drop D now.", texts);
        Assert.Contains("Received and paid. We now know we can trust HQ.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Fact]
    public void A_null_room_view_model_produces_no_room_surface_at_all()
    {
        var plan = Release1PresentationPlanner.Build(RoomInputs(null));

        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.RoomWithNoName);
        Assert.DoesNotContain(plan.Messages, message => message.CorrelationId.Contains("/release1.room-with-no-name/", StringComparison.Ordinal));
        Assert.True(plan.Decision is null || !plan.Decision.Id.StartsWith("release1-rn-", StringComparison.Ordinal));
    }

    // ---- Short Notice (Task 7) ----------------------------------------------------------------

    [Fact]
    public void The_short_notice_offer_is_the_review_decision_prompt_and_is_never_also_a_message()
    {
        var plan = Release1PresentationPlanner.Build(ShortNoticeInputs(ShortNoticeOfferView()));

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-sn-review", plan.Decision!.Id);
        Assert.Equal(Release1ShortNoticePresentation.OfferText, plan.Decision.Prompt);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal("Review terms", Assert.Single(plan.Decision.Options).Label);
        Assert.Equal(Release1PresentationCommand.ShortNoticeReview, plan.Decision.Options[0].Command);
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1ShortNoticePresentation.OfferText);
    }

    [Fact]
    public void Room_with_no_name_satisfied_with_short_notice_offered_yields_only_the_short_notice_review_decision()
    {
        // The realistic combined state once Room With No Name is done and Short Notice has just been
        // offered: Room With No Name is Satisfied (so it never contributes a decision, only its own
        // complete quest and message) and Short Notice is Offered with its review prompt visible.
        // Only the Short Notice review decision should come out, no Room With No Name decision.
        var story = WithShortNoticeState(
            WithRoomWithNoNameState(AcceptedStory(PlayerId), Release1MissionState.Satisfied),
            Release1MissionState.Offered);
        var roomAssignment = MakeRoomWithNoNameAssignment(PlayerId, 1);
        var roomView = new Release1RoomWithNoNameViewModel(
            true, Release1RoomWithNoNameCardStage.Completed, Release1RoomWithNoNamePresentation.Title,
            Release1PlayerCopy.Normalize("Status: Complete. Delivered and paid."), false, false, false);
        var shortNoticeView = ShortNoticeOfferView();
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            RoomWithNoName: roomView, RoomWithNoNameAssignment: roomAssignment, HoldRoomMarker: DefaultHoldRoomMarker,
            ShortNotice: shortNoticeView);

        var plan = Release1PresentationPlanner.Build(inputs);

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-sn-review", plan.Decision!.Id);
        Assert.NotEqual("release1-rn-review", plan.Decision.Id);
    }

    [Fact]
    public void The_short_notice_terms_decision_offers_accept_and_not_now()
    {
        var plan = Release1PresentationPlanner.Build(ShortNoticeInputs(ShortNoticeTermsView()));

        Assert.Equal("release1-sn-decision", plan.Decision!.Id);
        Assert.Collection(plan.Decision.Options,
            option => { Assert.Equal("Accept", option.Label); Assert.Equal(Release1PresentationCommand.ShortNoticeAccept, option.Command); },
            option => { Assert.Equal("Not now", option.Label); Assert.Equal(Release1PresentationCommand.ShortNoticeDefer, option.Command); });
        foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
    }

    [Fact]
    public void The_short_notice_quest_has_one_entry_and_the_marker_follows_active_completion_and_blocked_states()
    {
        var active = ShortNoticeQuest(Release1MissionState.Active);
        Assert.Single(active.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, active.Entries[0].State);
        Assert.NotNull(active.Entries[0].Marker);

        var satisfied = ShortNoticeQuest(Release1MissionState.Satisfied);
        Assert.Single(satisfied.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, satisfied.Entries[0].State);
        Assert.Null(satisfied.Entries[0].Marker);
        foreach (var entry in satisfied.Entries) AssertPlayerCopy(entry.Text);

        var blocked = ShortNoticeQuest(Release1MissionState.Active, blocked: true);
        Assert.Single(blocked.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, blocked.Entries[0].State);
        Assert.Null(blocked.Entries[0].Marker);
    }

    [Fact]
    public void A_shortfall_entry_still_carries_the_marker_and_names_what_remains()
    {
        var quest = ShortNoticeQuest(Release1MissionState.Active, shortfallRemaining: 2);

        Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
        Assert.NotNull(quest.Entries[0].Marker);
        Assert.Contains("2 still needed", quest.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_short_notice_accepted_and_shortfall_messages_are_emitted_with_their_own_correlations()
    {
        var plan = Release1PresentationPlanner.Build(ShortNoticeInputs(
            ShortNoticeActiveView(), spreadNoticed: false, shortfallRemaining: 2, missionState: Release1MissionState.Active));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("The drop is Drop D. Leave the whole order in one slot.", texts);
        Assert.Contains("That is not the whole order. 2 more of the same, in the same drop, in one slot. Get it together, you fuck.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Fact]
    public void The_short_notice_spread_and_delivered_messages_are_emitted_once_each_when_satisfied()
    {
        // The accepted message's own guard excludes Satisfied (mirroring BuildRoomWithNoName), so
        // only the spread and delivered messages are expected here.
        var plan = Release1PresentationPlanner.Build(ShortNoticeInputs(
            ShortNoticeActiveView(), spreadNoticed: true, shortfallRemaining: null, missionState: Release1MissionState.Satisfied));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("Put the whole order in one slot. I am not counting two piles.", texts);
        Assert.Contains("Received and paid. That is what short notice is worth.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Fact]
    public void The_shortfall_message_correlation_depends_on_the_remaining_count_so_a_repeat_is_deduped_by_receipt()
    {
        // The shortfall receipt id embeds the remaining count (see
        // Release1PresentationPlanner.ShortNoticeShortfallReceipt), so a repeated observation of the
        // same shortfall reuses the same correlation, which the projector's receipt store already
        // dedupes on; a genuinely different remaining count gets a distinct correlation and so is
        // sent again, "once per distinct shortfall" falling straight out of that receipt store.
        var first = Release1PresentationPlanner.Build(ShortNoticeInputs(
            ShortNoticeActiveView(), spreadNoticed: false, shortfallRemaining: 2, missionState: Release1MissionState.Active));
        var repeat = Release1PresentationPlanner.Build(ShortNoticeInputs(
            ShortNoticeActiveView(), spreadNoticed: false, shortfallRemaining: 2, missionState: Release1MissionState.Active));
        var different = Release1PresentationPlanner.Build(ShortNoticeInputs(
            ShortNoticeActiveView(), spreadNoticed: false, shortfallRemaining: 1, missionState: Release1MissionState.Active));

        var firstShortfall = first.Messages.Single(message => message.Text.Contains("more of the same", StringComparison.Ordinal));
        var repeatShortfall = repeat.Messages.Single(message => message.Text.Contains("more of the same", StringComparison.Ordinal));
        var differentShortfall = different.Messages.Single(message => message.Text.Contains("more of the same", StringComparison.Ordinal));

        Assert.Equal(firstShortfall.CorrelationId, repeatShortfall.CorrelationId);
        Assert.NotEqual(firstShortfall.CorrelationId, differentShortfall.CorrelationId);
    }

    [Fact]
    public void A_null_short_notice_view_model_produces_no_short_notice_surface_at_all()
    {
        var plan = Release1PresentationPlanner.Build(ShortNoticeInputs(null));

        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.ShortNotice);
        Assert.DoesNotContain(plan.Messages, message => message.CorrelationId.Contains("/release1.short-notice/", StringComparison.Ordinal));
        Assert.True(plan.Decision is null || !plan.Decision.Id.StartsWith("release1-sn-", StringComparison.Ordinal));
    }

    // ---- Keep the Lights Off (Task 6) ----------------------------------------------------------

    [Fact]
    public void The_keep_the_lights_off_offer_is_the_review_decision_prompt_and_is_never_also_a_message()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(KeepTheLightsOffOfferView()));

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-ktlo-review", plan.Decision!.Id);
        Assert.Equal(Release1KeepTheLightsOffPresentation.OfferText, plan.Decision.Prompt);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal("Review terms", Assert.Single(plan.Decision.Options).Label);
        Assert.Equal(Release1PresentationCommand.KeepTheLightsOffReview, plan.Decision.Options[0].Command);
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1KeepTheLightsOffPresentation.OfferText);
    }

    [Fact]
    public void The_keep_the_lights_off_terms_decision_offers_accept_and_not_now()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(KeepTheLightsOffTermsView()));

        Assert.Equal("release1-ktlo-decision", plan.Decision!.Id);
        Assert.Collection(plan.Decision.Options,
            option => { Assert.Equal("Accept", option.Label); Assert.Equal(Release1PresentationCommand.KeepTheLightsOffAccept, option.Command); },
            option => { Assert.Equal("Not now", option.Label); Assert.Equal(Release1PresentationCommand.KeepTheLightsOffDefer, option.Command); });
        foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
    }

    [Fact]
    public void The_keep_the_lights_off_quest_has_one_entry_with_no_marker_at_every_state()
    {
        var active = KeepTheLightsOffQuest(Release1MissionState.Active);
        Assert.Single(active.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, active.Entries[0].State);
        Assert.Null(active.Entries[0].Marker);
        Assert.Contains("No production activities from you or employees for 24 hours.", active.Entries[0].Text, StringComparison.Ordinal);

        var holding = KeepTheLightsOffQuest(Release1MissionState.Active, clearConfirmedAtGameMinutes: 5_000d);
        Assert.Single(holding.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, holding.Entries[0].State);
        Assert.Null(holding.Entries[0].Marker);
        Assert.Contains("Hold quiet. Nothing moves for one day", holding.Entries[0].Text, StringComparison.Ordinal);

        var satisfied = KeepTheLightsOffQuest(Release1MissionState.Satisfied);
        Assert.Single(satisfied.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, satisfied.Entries[0].State);
        Assert.Null(satisfied.Entries[0].Marker);
        foreach (var entry in satisfied.Entries) AssertPlayerCopy(entry.Text);

        var blocked = KeepTheLightsOffQuest(Release1MissionState.Active, blocked: true);
        Assert.Single(blocked.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, blocked.Entries[0].State);
        Assert.Null(blocked.Entries[0].Marker);
        Assert.Contains("Job on hold", blocked.Entries[0].Text, StringComparison.Ordinal);

        var makeGoodActive = KeepTheLightsOffQuest(Release1MissionState.MakeGoodActive);
        Assert.Single(makeGoodActive.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, makeGoodActive.Entries[0].State);
        Assert.Null(makeGoodActive.Entries[0].Marker);
        Assert.Contains("No production activities from you or employees for 24 hours.", makeGoodActive.Entries[0].Text, StringComparison.Ordinal);

        var recoveryActive = KeepTheLightsOffQuest(Release1MissionState.RecoveryActive);
        Assert.Single(recoveryActive.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, recoveryActive.Entries[0].State);
        Assert.Null(recoveryActive.Entries[0].Marker);
        Assert.Contains("No production activities from you or employees for 24 hours.", recoveryActive.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keep_the_lights_off_mission_produces_no_quest_or_message_in_non_projecting_states()
    {
        // Deferred (the review was declined) and MakeGoodOffered (the state a RequiredFailure
        // transition lands the mission in; see Release1StoryTransitions' RequiredFailure arm) are both
        // absent from BuildKeepTheLightsOff's projecting-state allowlist (Accepted, Active,
        // MakeGoodActive, RecoveryActive, Satisfied), so neither should surface a quest or a message.
        var deferred = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.Deferred));

        Assert.DoesNotContain(deferred.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
        Assert.DoesNotContain(deferred.Messages, message => message.CorrelationId.Contains("/release1.keep-the-lights-off/", StringComparison.Ordinal));

        var requiredFailure = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.MakeGoodOffered));

        Assert.DoesNotContain(requiredFailure.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
        Assert.DoesNotContain(requiredFailure.Messages, message => message.CorrelationId.Contains("/release1.keep-the-lights-off/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_breach_quest_entry_reuses_the_card_constant()
    {
        var story = WithKeepTheLightsOffState(AcceptedStory(PlayerId), Release1MissionState.Active, attempt: 1);
        var assignment = MakeKeepTheLightsOffAssignment(PlayerId, 1);
        var progress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 5_000d, 6_000d);
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            KeepTheLightsOff: KeepTheLightsOffActiveView(), KeepTheLightsOffAssignment: assignment, KeepTheLightsOffProgress: progress);

        var plan = Release1PresentationPlanner.Build(inputs);

        var quest = plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
        Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
        Assert.Null(quest.Entries[0].Marker);
        Assert.Equal(Release1KeepTheLightsOffPresentation.BreachStatusText, quest.Entries[0].Text);
        AssertPlayerCopy(quest.Entries[0].Text);
    }

    [Fact]
    public void The_keep_the_lights_off_clear_confirmed_messages_correlation_id_is_stable_across_repeated_passes_while_still_holding()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.Active, clearConfirmedAtGameMinutes: 5_000d));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("All stopped. Keep it this way for 24 hours.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);

        // The planner is a pure function of its inputs: it does not itself track "already sent" state
        // (that dedupe happens downstream, against the projector's receipt store), so this only proves
        // the correlation id is deterministic and stable across repeated builds of the same inputs.
        var repeat = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.Active, clearConfirmedAtGameMinutes: 5_000d));

        var first = plan.Messages.Single(message => message.Text.Contains("All stopped", StringComparison.Ordinal));
        var second = repeat.Messages.Single(message => message.Text.Contains("All stopped", StringComparison.Ordinal));
        Assert.Equal(first.CorrelationId, second.CorrelationId);
    }

    [Fact]
    public void The_keep_the_lights_off_completed_message_is_emitted_once_when_satisfied_and_the_clear_confirmed_message_is_not_repeated()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.Satisfied, clearConfirmedAtGameMinutes: 5_000d));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("One quiet day. Thank god, you dodged a bullet there.", texts);
        Assert.DoesNotContain("All stopped. Keep it this way for 24 hours.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Fact]
    public void A_null_keep_the_lights_off_view_model_produces_no_keep_the_lights_off_surface_at_all()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(null));

        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
        Assert.DoesNotContain(plan.Messages, message => message.CorrelationId.Contains("/release1.keep-the-lights-off/", StringComparison.Ordinal));
        Assert.True(plan.Decision is null || !plan.Decision.Id.StartsWith("release1-ktlo-", StringComparison.Ordinal));
    }

    // ---- Task 5: the shutdown copy, and the assignment free Completed stage ------------------

    [Fact]
    public void The_open_quest_entry_is_a_plain_string_with_no_format_placeholder()
    {
        const string openText = "No production activities from you or employees for 24 hours.";
        Assert.DoesNotContain("{0}", openText, StringComparison.Ordinal);

        var quest = KeepTheLightsOffQuest(Release1MissionState.Active);

        Assert.Equal(Release1PlayerCopy.Normalize(openText), quest.Entries[0].Text);
    }

    [Fact]
    public void A_satisfied_attempt_builds_its_completed_quest_and_message_with_an_empty_assignment_collection()
    {
        var story = WithKeepTheLightsOffState(AcceptedStory(PlayerId), Release1MissionState.Satisfied, attempt: 1);
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            KeepTheLightsOff: KeepTheLightsOffActiveView(), KeepTheLightsOffAssignment: null, KeepTheLightsOffProgress: null);

        var plan = Release1PresentationPlanner.Build(inputs);

        var quest = Assert.Single(plan.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
        var entry = Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, entry.State);
        Assert.Null(entry.Marker);
        Assert.Equal("No production activities from you or employees for 24 hours.", entry.Text);

        var message = Assert.Single(plan.Messages,
            message => message.CorrelationId.Contains("/release1.keep-the-lights-off/", StringComparison.Ordinal));
        Assert.Equal("One quiet day. Thank god, you dodged a bullet there.", message.Text);
    }

    [Fact]
    public void The_confirmed_message_is_published_once_per_attempt()
    {
        var plan = Release1PresentationPlanner.Build(KeepTheLightsOffInputs(
            KeepTheLightsOffActiveView(), missionState: Release1MissionState.Active, clearConfirmedAtGameMinutes: 5_000d, attempt: 1));

        var activatedCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.KeepTheLightsOff, 1, Release1TransitionKind.MissionActivated,
            "presentation-nell-ktlo-clear-v1").Value;

        var matches = plan.Messages.Where(message => message.CorrelationId == activatedCorrelation).ToArray();
        var match = Assert.Single(matches);
        Assert.Equal("All stopped. Keep it this way for 24 hours.", match.Text);
    }

    [Fact]
    public void No_quest_entry_at_any_stage_carries_a_marker()
    {
        var accepted = KeepTheLightsOffQuest(Release1MissionState.Accepted);
        Assert.All(accepted.Entries, entry => Assert.Null(entry.Marker));

        var running = KeepTheLightsOffQuest(Release1MissionState.Active);
        Assert.All(running.Entries, entry => Assert.Null(entry.Marker));

        var holding = KeepTheLightsOffQuest(Release1MissionState.Active, clearConfirmedAtGameMinutes: 5_000d);
        Assert.All(holding.Entries, entry => Assert.Null(entry.Marker));

        var breach = KeepTheLightsOffQuest(Release1MissionState.Active, breachSincePassGameMinutes: 6_000d);
        Assert.All(breach.Entries, entry => Assert.Null(entry.Marker));

        var satisfied = KeepTheLightsOffQuest(Release1MissionState.Satisfied);
        Assert.All(satisfied.Entries, entry => Assert.Null(entry.Marker));
    }

    // ---- The Envelope (Task 6) ----------------------------------------------------------------

    [Fact]
    public void The_envelope_offer_is_the_review_decision_prompt_and_is_never_also_a_message()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(TheEnvelopeOfferView()));

        Assert.NotNull(plan.Decision);
        Assert.Equal("release1-te-review", plan.Decision!.Id);
        Assert.Equal(Release1TheEnvelopePresentation.OfferText, plan.Decision.Prompt);
        AssertPlayerCopy(plan.Decision.Prompt);
        Assert.Equal("Review terms", Assert.Single(plan.Decision.Options).Label);
        Assert.Equal(Release1PresentationCommand.TheEnvelopeReview, plan.Decision.Options[0].Command);
        Assert.DoesNotContain(plan.Messages, message => message.Text == Release1TheEnvelopePresentation.OfferText);
    }

    [Fact]
    public void The_envelope_terms_decision_offers_accept_and_not_now()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(TheEnvelopeTermsView()));

        Assert.Equal("release1-te-decision", plan.Decision!.Id);
        Assert.Collection(plan.Decision.Options,
            option => { Assert.Equal("Accept", option.Label); Assert.Equal(Release1PresentationCommand.TheEnvelopeAccept, option.Command); },
            option => { Assert.Equal("Not now", option.Label); Assert.Equal(Release1PresentationCommand.TheEnvelopeDefer, option.Command); });
        foreach (var line in plan.Decision.Prompt.Split('\n')) AssertPlayerCopy(line);
    }

    [Fact]
    public void The_envelope_quest_has_one_entry_and_the_marker_follows_active_completion_and_blocked_states()
    {
        var active = TheEnvelopeQuest(Release1MissionState.Active);
        Assert.Single(active.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, active.Entries[0].State);
        Assert.NotNull(active.Entries[0].Marker);

        var satisfied = TheEnvelopeQuest(Release1MissionState.Satisfied);
        Assert.Single(satisfied.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, satisfied.Entries[0].State);
        Assert.Null(satisfied.Entries[0].Marker);
        foreach (var entry in satisfied.Entries) AssertPlayerCopy(entry.Text);

        var blocked = TheEnvelopeQuest(Release1MissionState.Active, blocked: true);
        Assert.Single(blocked.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, blocked.Entries[0].State);
        Assert.Null(blocked.Entries[0].Marker);
    }

    [Fact]
    public void A_shortfall_envelope_entry_still_carries_the_marker_and_names_what_remains()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Active, shortfallRemaining: 500d);

        Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
        Assert.NotNull(quest.Entries[0].Marker);
        Assert.Contains("$500 still needed", quest.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_envelope_accepted_message_and_a_large_shortfall_use_the_many_format()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: 1500d, missionState: Release1MissionState.Active));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("The closet is in the HQ. 20 stacks of cash, all in one closet.", texts);
        Assert.Contains("Are you trying to scam us? You really want to try and test the Selby's?", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Theory]
    [InlineData(Release1TheEnvelopeAssignmentMode.Primary, Release1MissionState.Active, 20)]
    [InlineData(Release1TheEnvelopeAssignmentMode.MakeGood, Release1MissionState.MakeGoodActive, 10)]
    [InlineData(Release1TheEnvelopeAssignmentMode.Recovery, Release1MissionState.RecoveryActive, 5)]
    public void The_envelope_accepted_message_states_the_stack_count_for_the_assignment_s_own_tier(
        Release1TheEnvelopeAssignmentMode mode, Release1MissionState missionState, int expectedStacks)
    {
        var story = WithTheEnvelopeState(AcceptedStory(PlayerId), missionState, attempt: 1);
        var assignment = MakeTheEnvelopeAssignment(PlayerId, attempt: 1, mode);
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            TheEnvelope: TheEnvelopeActiveView(), TheEnvelopeAssignment: assignment, TheEnvelopeProgress: null,
            HoldRoomMarker: DefaultHoldRoomMarker);

        var plan = Release1PresentationPlanner.Build(inputs);

        var expectedText = $"The closet is in the HQ. {expectedStacks} stacks of cash, all in one closet.";
        Assert.Contains(plan.Messages, message => message.Text == expectedText);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
    }

    [Fact]
    public void A_small_envelope_shortfall_uses_the_small_format()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: 80d, missionState: Release1MissionState.Active));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("We know how to count. You are missing 80 dollars more. Put it in the same storage", texts);
    }

    [Fact]
    public void The_envelope_spread_message_is_emitted_once_when_noticed()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: true, shortfallRemaining: null, missionState: Release1MissionState.Active));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains("Stop fucking around. I told you, all in one storage.", texts);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
    }

    [Fact]
    public void The_envelope_deposit_confirmed_and_recognition_messages_fire_once_each_on_completion_with_distinct_correlations()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Satisfied));

        var texts = plan.Messages.Select(message => message.Text).ToArray();
        Assert.Contains(Release1TheEnvelopePresentation.Title, plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope).Title);
        Assert.Contains("Received. Well done.", texts);
        Assert.Contains(
            $"Welcome to {Release1PlayerCopy.FamilyName}. You are part of the family now. Don't get cocky though, you will earn the respect for increased operations. For now, the HQ room is yours. Use it as you please, but there are times the family may need to use it, and you don't have a fucking choice. I'll be in touch.",
            texts);
        // Deposit-confirmed keys off The Envelope's own mission scope; recognition keys off the intro
        // scope instead (a whole-story fact), so the two correlations never collide.
        var deposit = plan.Messages.Single(message => message.Text == "Received. Well done.");
        var recognition = plan.Messages.Single(message => message.Text.StartsWith("Welcome to", StringComparison.Ordinal));
        Assert.NotEqual(deposit.CorrelationId, recognition.CorrelationId);
        Assert.Contains("/release1.the-envelope/", deposit.CorrelationId, StringComparison.Ordinal);
        Assert.Contains($"/{Release1MissionCatalog.IntroScopeKey}/", recognition.CorrelationId, StringComparison.Ordinal);
        Assert.Equal(plan.Messages.Select(message => message.CorrelationId).Distinct().Count(), plan.Messages.Count);
        foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);

        // The shortfall entry disappears once the mission is Satisfied: one Complete entry, no
        // remaining-amount text.
        var quest = plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, quest.Entries[0].State);
        Assert.DoesNotContain("still needed", quest.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_envelope_shortfall_message_correlation_depends_on_the_remaining_amount_so_a_repeat_is_deduped_by_receipt()
    {
        // The shortfall receipt id embeds the whole-dollar remaining amount (see
        // Release1PresentationPlanner.TheEnvelopeShortfallReceipt), so a repeated observation of the
        // same shortfall reuses the same correlation, which the projector's receipt store already
        // dedupes on; a genuinely different remaining amount gets a distinct correlation and so is
        // sent again, "once per distinct shortfall, never re-sent once already receipted" falling
        // straight out of that receipt store.
        var first = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: 500d, missionState: Release1MissionState.Active));
        var repeat = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: 500d, missionState: Release1MissionState.Active));
        var different = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: 250d, missionState: Release1MissionState.Active));

        var firstShortfall = first.Messages.Single(message => message.CorrelationId.Contains("shortfall", StringComparison.Ordinal));
        var repeatShortfall = repeat.Messages.Single(message => message.CorrelationId.Contains("shortfall", StringComparison.Ordinal));
        var differentShortfall = different.Messages.Single(message => message.CorrelationId.Contains("shortfall", StringComparison.Ordinal));

        Assert.Equal(firstShortfall.CorrelationId, repeatShortfall.CorrelationId);
        Assert.NotEqual(firstShortfall.CorrelationId, differentShortfall.CorrelationId);
    }

    [Fact]
    public void Every_the_envelope_mission_state_maps_to_exactly_one_planner_stage()
    {
        // Accepted (awaiting activation): quest present, no assignment-driven message beyond
        // "accepted" itself, entry Active with marker, no shortfall/spread/deposit text.
        var accepted = TheEnvelopeQuest(Release1MissionState.Accepted);
        Assert.Equal(Release1DesiredEntryState.Active, Assert.Single(accepted.Entries).State);

        // Active: same entry shape as Accepted (this planner does not distinguish awaiting-activation
        // from active in the quest entry, only Nell's card does), no shortfall.
        var active = TheEnvelopeQuest(Release1MissionState.Active);
        Assert.Equal(Release1DesiredEntryState.Active, Assert.Single(active.Entries).State);

        // MakeGoodActive and RecoveryActive both still produce the accepted message and an active,
        // marker-carrying entry (the planner does not distinguish attempt mode in the entry shape).
        var makeGoodPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.MakeGoodActive));
        Assert.Contains(makeGoodPlan.Messages, message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.");
        var makeGoodQuest = makeGoodPlan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.Equal(Release1DesiredEntryState.Active, Assert.Single(makeGoodQuest.Entries).State);
        Assert.NotNull(makeGoodQuest.Entries[0].Marker);

        var recoveryPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.RecoveryActive));
        Assert.Contains(recoveryPlan.Messages, message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.");
        var recoveryQuest = recoveryPlan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.Equal(Release1DesiredEntryState.Active, Assert.Single(recoveryQuest.Entries).State);
        Assert.NotNull(recoveryQuest.Entries[0].Marker);

        // Satisfied: Complete entry, deposit-confirmed and recognition messages, no accepted message.
        var satisfiedPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Satisfied));
        Assert.DoesNotContain(satisfiedPlan.Messages, message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.");
        Assert.Equal(Release1DesiredEntryState.Complete, satisfiedPlan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope).Entries[0].State);

        // Blocked (Ambiguous on the card, but the mission state driving the quest here is still
        // Active): the quest entry becomes the hold text, no marker.
        var blocked = TheEnvelopeQuest(Release1MissionState.Active, blocked: true);
        Assert.Equal("Delivery on hold", Assert.Single(blocked.Entries).Text);
        Assert.Null(blocked.Entries[0].Marker);

        // Deferred and MakeGoodOffered are non-projecting states: an assignment can already exist
        // (a prior attempt was deferred, or a make-good offer is pending acceptance), so
        // BuildTheEnvelope's `story is null || mission is null || assignment is null` short-circuit
        // never fires here; it is the separate state guard right after it
        // (`mission.State is not (Accepted or Active or MakeGoodActive or RecoveryActive or
        // Satisfied)`) that must suppress the quest and the accepted message. The offer/review
        // decision is built earlier and does not depend on mission state at all (same as every other
        // mission's planner branch), so it can still appear; that is exercised here too rather than
        // assumed.
        var deferredPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeOfferView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Deferred));
        Assert.NotNull(deferredPlan.Decision);
        Assert.Equal("release1-te-review", deferredPlan.Decision!.Id);
        Assert.DoesNotContain(deferredPlan.Quests, quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.DoesNotContain(deferredPlan.Messages, message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.");

        var makeGoodOfferedPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeOfferView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.MakeGoodOffered));
        Assert.NotNull(makeGoodOfferedPlan.Decision);
        Assert.Equal("release1-te-review", makeGoodOfferedPlan.Decision!.Id);
        Assert.DoesNotContain(makeGoodOfferedPlan.Quests, quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.DoesNotContain(makeGoodOfferedPlan.Messages, message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.");
    }

    [Fact]
    public void A_null_the_envelope_view_model_produces_no_the_envelope_surface_at_all()
    {
        // Mirrors what happens under ImguiFallback presentation mode: the production composition
        // passes a null TheEnvelope view model (and no assignment/progress) rather than the real
        // presenter's view, so the planner must contribute nothing at all for the mission.
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(null));

        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.TheEnvelope);
        Assert.DoesNotContain(plan.Messages, message => message.CorrelationId.Contains("/release1.the-envelope/", StringComparison.Ordinal));
        Assert.True(plan.Decision is null || !plan.Decision.Id.StartsWith("release1-te-", StringComparison.Ordinal));
    }

    // ---- OC-61 Task 5 Step 2: the closet copy table and the hq door marker --------------------

    [Fact]
    public void The_envelope_quest_has_exactly_one_entry_carrying_the_hq_door_marker_when_it_resolves()
    {
        var marker = new Release1WorldPoint(21f, 22f, 23f);

        var quest = TheEnvelopeQuest(Release1MissionState.Active, spreadNoticed: false, shortfallRemaining: null, blocked: false, attempt: 1, holdRoomMarker: marker);

        Assert.Single(quest.Entries);
        Assert.Equal(marker, quest.Entries[0].Marker);
    }

    [Fact]
    public void The_envelope_quest_entry_carries_no_marker_when_the_hq_door_has_not_resolved()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Active, spreadNoticed: false, shortfallRemaining: null, blocked: false, attempt: 1, holdRoomMarker: null);

        Assert.Single(quest.Entries);
        Assert.Null(quest.Entries[0].Marker);
        Assert.Equal(Release1DesiredEntryState.Active, quest.Entries[0].State);
    }

    [Fact]
    public void The_open_entry_names_the_amount_and_the_closet_at_the_syndicate_hq()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Active);

        var entry = Assert.Single(quest.Entries);
        Assert.Equal("Leave $20000 in cash in the closet at the Syndicate HQ", entry.Text);
        AssertPlayerCopy(entry.Text);
    }

    [Fact]
    public void The_shortfall_entry_names_the_amount_and_the_remaining_amount()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Active, shortfallRemaining: 500d);

        var entry = Assert.Single(quest.Entries);
        Assert.Equal("Leave $20000 in cash in the closet at the Syndicate HQ, $500 still needed", entry.Text);
        Assert.Equal(Release1DesiredEntryState.Active, entry.State);
        Assert.NotNull(entry.Marker);
        AssertPlayerCopy(entry.Text);
    }

    [Fact]
    public void The_blocked_entry_is_delivery_on_hold_with_no_marker()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Active, blocked: true);

        var entry = Assert.Single(quest.Entries);
        Assert.Equal("Delivery on hold", entry.Text);
        Assert.Equal(Release1DesiredEntryState.Active, entry.State);
        Assert.Null(entry.Marker);
    }

    [Fact]
    public void The_satisfied_entry_completes_and_carries_no_marker()
    {
        var quest = TheEnvelopeQuest(Release1MissionState.Satisfied);

        var entry = Assert.Single(quest.Entries);
        Assert.Equal(Release1DesiredEntryState.Complete, entry.State);
        Assert.Null(entry.Marker);
    }

    [Fact]
    public void The_accepted_message_is_sent_once_per_attempt_and_takes_no_format_argument()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Active));

        var accepted = plan.Messages.Where(message => message.Text == "The closet is in the HQ. 20 stacks of cash, all in one closet.").ToArray();
        Assert.Single(accepted);
        Assert.DoesNotContain("{0}", accepted[0].Text, StringComparison.Ordinal);
        AssertPlayerCopy(accepted[0].Text);
    }

    [Fact]
    public void The_spread_message_is_sent_once_per_attempt()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: true, shortfallRemaining: null, missionState: Release1MissionState.Active));

        var spread = plan.Messages.Where(message => message.Text == "Stop fucking around. I told you, all in one storage.").ToArray();
        Assert.Single(spread);
        AssertPlayerCopy(spread[0].Text);
    }

    [Theory]
    [InlineData(4000d, "Are you trying to scam us? You really want to try and test the Selby's?")]
    [InlineData(100d, "We know how to count. You are missing 100 dollars more. Put it in the same storage")]
    [InlineData(50d, "We know how to count. You are missing 50 dollars more. Put it in the same storage")]
    public void The_shortfall_message_is_receipted_by_the_remaining_amount_and_uses_the_many_wording_above_one_hundred_and_the_small_wording_at_or_below_it(
        double remaining, string expectedText)
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: remaining, missionState: Release1MissionState.Active));

        var shortfall = Assert.Single(plan.Messages, message =>
            message.CorrelationId.Contains(Release1TheEnvelopePresentation.Dollars(remaining), StringComparison.Ordinal));
        Assert.Equal(expectedText, shortfall.Text);
        AssertPlayerCopy(shortfall.Text);
    }

    [Fact]
    public void Completion_sends_the_delivered_message_then_the_recognition_message_exactly_once()
    {
        var plan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Satisfied));

        var deposit = Assert.Single(plan.Messages, message => message.Text == "Received. Well done.");
        var recognition = Assert.Single(plan.Messages, message => message.Text.StartsWith("Welcome to", StringComparison.Ordinal));
        var depositIndex = plan.Messages.ToList().IndexOf(deposit);
        var recognitionIndex = plan.Messages.ToList().IndexOf(recognition);
        Assert.True(depositIndex < recognitionIndex, "Expected the deposit-confirmed message before the recognition message.");
        AssertPlayerCopy(deposit.Text);
        AssertPlayerCopy(recognition.Text);
    }

    [Fact]
    public void No_envelope_string_in_the_built_plan_contains_a_dash_character()
    {
        var offerPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(TheEnvelopeOfferView()));
        var termsPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(TheEnvelopeTermsView()));
        var activePlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: true, shortfallRemaining: 1500d, missionState: Release1MissionState.Active));
        var satisfiedPlan = Release1PresentationPlanner.Build(TheEnvelopeInputs(
            TheEnvelopeActiveView(), spreadNoticed: false, shortfallRemaining: null, missionState: Release1MissionState.Satisfied));

        foreach (var plan in new[] { offerPlan, termsPlan, activePlan, satisfiedPlan })
        {
            if (plan.Decision is not null) AssertPlayerCopy(plan.Decision.Prompt);
            foreach (var message in plan.Messages) AssertPlayerCopy(message.Text);
            foreach (var quest in plan.Quests.Where(quest => quest.Key == Release1MissionCatalog.TheEnvelope))
                foreach (var entry in quest.Entries) AssertPlayerCopy(entry.Text);
        }
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

    private static void AssertPlansEqual(Release1PresentationPlan a, Release1PresentationPlan b)
    {
        Assert.True(a.Messages.SequenceEqual(b.Messages));
        Assert.Equal(a.Decision is null, b.Decision is null);
        if (a.Decision is not null && b.Decision is not null)
        {
            Assert.Equal(a.Decision.Id, b.Decision.Id);
            Assert.Equal(a.Decision.Prompt, b.Decision.Prompt);
            Assert.True(a.Decision.Options.SequenceEqual(b.Decision.Options));
        }
        Assert.Equal(a.Quests.Count, b.Quests.Count);
        for (var i = 0; i < a.Quests.Count; i++)
        {
            Assert.Equal(a.Quests[i].Key, b.Quests[i].Key);
            Assert.Equal(a.Quests[i].Title, b.Quests[i].Title);
            Assert.True(a.Quests[i].Entries.SequenceEqual(b.Quests[i].Entries));
        }
    }

    private static Release1StoryState UnstartedStory(string playerId) => new(
        playerId, 0, Release1RelationshipState.Unstarted, false,
        Array.Empty<string>(), Array.Empty<string>(),
        Release1MissionCatalog.All.Select(definition => NewMission(definition.MissionKey, Release1MissionState.Locked)).ToArray(),
        Array.Empty<Release1NativeEffectJournalEntry>(), 0);

    private static Release1StoryState AcceptedStory(string playerId)
    {
        var introCorrelation = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "test-intro-receipt").Value;
        return Release1StoryState.CreateAccepted(playerId, introCorrelation);
    }

    private static Release1StoryState WithSmallCourtesyState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.SmallCourtesy
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1SmallCourtesyAssignment MakeAssignment(string playerId, int attempt)
    {
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.SmallCourtesy, attempt, Release1TransitionKind.MissionAccepted, "test-sc-authorization").Value;
        return new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            Release1SmallCourtesyAssignmentMode.Primary,
            authorization,
            "cocaine",
            "Cocaine",
            500d,
            "brick",
            "Brick",
            "drop-guid-1",
            "Drainage Ditch Drop",
            "Behind the drainage ditch off Dyer Street.",
            120.5d,
            10.25d,
            -40.75d,
            1.25d);
    }

    private static Release1MissionRecord NewMission(string key, Release1MissionState state) => new(
        key, state, 1, null, null, null, Release1MissionOutcome.None, 0,
        Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0);

    // ---- Wrong Address fixtures --------------------------------------------------------------

    private static Release1PresentationInputs WrongAddressInputs(Release1WrongAddressViewModel view) =>
        new(AcceptedStory(PlayerId), false, false, null, null, view, null, null);

    private static Release1WrongAddressViewModel WrongAddressView(
        Release1WrongAddressCardStage stage, string body, bool canReview = false, bool canAccept = false, bool canDefer = false) =>
        new(true, stage, "Wrong Address", Release1PlayerCopy.Normalize(body), canReview, canAccept, canDefer);

    private static string TermsBody() => string.Join("\n",
        "Recovery run, attempt 1",
        "Package: 1 Brick of Cocaine",
        "Wrong address: Drop A",
        "Behind the drainage ditch off Dyer Street.",
        "Right address: Drop B",
        "Beneath the loading dock on Kettleman.",
        "Deadline: 24 in game hours",
        "Payment: 125 percent of the package value, paid the moment you hand it over.",
        "The package will be waiting at the wrong address.",
        "Are you taking it?");

    private static Release1StoryState WithWrongAddressState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "wrong-address-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-wa-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1WrongAddressAssignment MakeWrongAddressAssignment(
        string playerId, int attempt, Release1WrongAddressAssignmentMode mode = Release1WrongAddressAssignmentMode.Primary)
    {
        var transitionKind = mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.WrongAddress, attempt, transitionKind, "test-wa-authorization").Value;
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

    private static Release1NativeEffectJournalEntry BlockedWrongAddressEffect(int attempt) => new(
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

    private static Release1PresentationInputs ActiveWrongAddressInputs(bool custody, int attempt = 1)
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Active, attempt);
        var assignment = MakeWrongAddressAssignment(PlayerId, attempt);
        var progress = custody ? new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, attempt, true, true) : null;
        var view = WrongAddressView(
            custody ? Release1WrongAddressCardStage.InCustody : Release1WrongAddressCardStage.Active,
            "Status line.");
        return new Release1PresentationInputs(story, false, false, null, null, view, assignment, progress);
    }

    private static Release1PresentationInputs MakeGoodActiveWrongAddressInputs(bool custody, int attempt = 2)
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.MakeGoodActive, attempt);
        var assignment = MakeWrongAddressAssignment(PlayerId, attempt, Release1WrongAddressAssignmentMode.MakeGood);
        var progress = custody ? new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, attempt, true, true) : null;
        var view = WrongAddressView(
            custody ? Release1WrongAddressCardStage.InCustody : Release1WrongAddressCardStage.Active,
            "Status line.");
        return new Release1PresentationInputs(story, false, false, null, null, view, assignment, progress);
    }

    private static Release1PresentationInputs SatisfiedWrongAddressInputs(int attempt = 1)
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Satisfied, attempt);
        var assignment = MakeWrongAddressAssignment(PlayerId, attempt);
        var view = WrongAddressView(Release1WrongAddressCardStage.Completed, "Status line.");
        return new Release1PresentationInputs(story, false, false, null, null, view, assignment, null);
    }

    private static Release1PresentationInputs BlockedWrongAddressInputs(int attempt = 1)
    {
        var story = WithWrongAddressState(AcceptedStory(PlayerId), Release1MissionState.Active, attempt);
        story = story with { NativeEffects = new[] { BlockedWrongAddressEffect(attempt) } };
        var assignment = MakeWrongAddressAssignment(PlayerId, attempt);
        var view = WrongAddressView(Release1WrongAddressCardStage.Ambiguous, "Status line.");
        return new Release1PresentationInputs(story, false, false, null, null, view, assignment, null);
    }

    private static IEnumerable<Release1PresentationInputs> AllWrongAddressInputs()
    {
        yield return WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.Offer,
            "One of ours went to the wrong address. I need it collected and put where it belongs.",
            canReview: true));
        yield return WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.InsufficientDrops,
            "I need two clear drops before I can point you at one. Try me again when the town has room.",
            canReview: true));
        yield return WrongAddressInputs(WrongAddressView(
            Release1WrongAddressCardStage.Review, TermsBody(), canAccept: true, canDefer: true));
        yield return ActiveWrongAddressInputs(custody: false);
        yield return ActiveWrongAddressInputs(custody: true);
        yield return MakeGoodActiveWrongAddressInputs(custody: false);
        yield return MakeGoodActiveWrongAddressInputs(custody: true);
        yield return SatisfiedWrongAddressInputs();
        yield return BlockedWrongAddressInputs();
    }

    // ---- Room With No Name fixtures ----------------------------------------------------------

    private static readonly Release1WorldPoint DefaultHoldRoomMarker = new(11f, 12f, 13f);

    private static Release1PresentationInputs RoomInputs(Release1RoomWithNoNameViewModel? view) =>
        new(AcceptedStory(PlayerId), false, false, null, null, null, null, null, view, null, null, DefaultHoldRoomMarker);

    private static Release1PresentationInputs RoomInputs(
        Release1RoomWithNoNameViewModel view, bool custody, bool stowed, bool holdSatisfied, Release1MissionState missionState, int attempt = 1)
    {
        var story = WithRoomWithNoNameState(AcceptedStory(PlayerId), missionState, attempt);
        var assignment = MakeRoomWithNoNameAssignment(PlayerId, attempt);
        Release1RoomWithNoNameProgress? progress = custody || stowed || holdSatisfied
            ? new Release1RoomWithNoNameProgress(
                Release1MissionCatalog.RoomWithNoName, attempt, true, custody, stowed, holdSatisfied,
                stowed ? 100d : null, stowed ? "closet-guid" : null, null)
            : null;
        return new Release1PresentationInputs(story, false, false, null, null, null, null, null, view, assignment, progress, DefaultHoldRoomMarker);
    }

    private static Release1RoomWithNoNameViewModel RoomOfferView() =>
        new(true, Release1RoomWithNoNameCardStage.Offer, Release1RoomWithNoNamePresentation.Title,
            Release1PlayerCopy.Normalize(Release1RoomWithNoNamePresentation.OfferText), true, false, false);

    private static Release1RoomWithNoNameViewModel RoomTermsView() =>
        new(true, Release1RoomWithNoNameCardStage.Review, Release1RoomWithNoNamePresentation.Title,
            Release1PlayerCopy.Normalize(RoomTermsBody()), false, true, true);

    private static Release1RoomWithNoNameViewModel RoomActiveView() =>
        new(true, Release1RoomWithNoNameCardStage.Active, Release1RoomWithNoNamePresentation.Title,
            Release1PlayerCopy.Normalize("Status line."), false, false, false);

    private static string RoomTermsBody() => string.Join("\n",
        "Hold run, attempt 1",
        "Consignment: 1 Brick of Cocaine",
        "Pick up: Drop A",
        "Behind the drainage ditch off Dyer Street.",
        "Hold: the storage room at the Syndicate HQ, one full day",
        "Hand off: Drop D",
        "Beneath the loading dock on Kettleman.",
        "Deadline: 72 in game hours",
        "Payment: 150 percent of the consignment value, paid the moment you hand it over.",
        "The consignment will be waiting at the pick up.",
        "Are you taking it?");

    private static Release1DesiredQuest RoomQuest(
        bool custody, bool holdSatisfied, Release1MissionState missionState) =>
        RoomQuest(custody, holdSatisfied, missionState, DefaultHoldRoomMarker);

    private static Release1DesiredQuest RoomQuest(
        bool custody, bool holdSatisfied, Release1MissionState missionState, Release1WorldPoint? holdRoomMarker)
    {
        const int attempt = 1;
        var story = WithRoomWithNoNameState(AcceptedStory(PlayerId), missionState, attempt);
        var assignment = MakeRoomWithNoNameAssignment(PlayerId, attempt);
        Release1RoomWithNoNameProgress? progress = custody || holdSatisfied
            ? new Release1RoomWithNoNameProgress(
                Release1MissionCatalog.RoomWithNoName, attempt, true, custody, holdSatisfied, holdSatisfied,
                holdSatisfied ? 100d : null, holdSatisfied ? "closet-guid" : null, null)
            : null;
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null, null, null, null, RoomActiveView(), assignment, progress, holdRoomMarker);
        var plan = Release1PresentationPlanner.Build(inputs);
        return plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.RoomWithNoName);
    }

    private static Release1StoryState WithRoomWithNoNameState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.RoomWithNoName
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "room-with-no-name-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-rn-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1RoomWithNoNameAssignment MakeRoomWithNoNameAssignment(
        string playerId, int attempt, Release1RoomWithNoNameAssignmentMode mode = Release1RoomWithNoNameAssignmentMode.Primary)
    {
        var transitionKind = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.RoomWithNoName, attempt, transitionKind, "test-rn-authorization").Value;
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
            "drop-d-guid",
            "Drop D",
            "Beneath the loading dock on Kettleman.",
            4d, 5d, 6d,
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            Release1RoomWithNoNameAssignment.OneInGameDayMinutes,
            Release1RoomWithNoNameAssignment.RoomRewardMultiplier);
    }

    // ---- Short Notice fixtures ----------------------------------------------------------------

    private static Release1PresentationInputs ShortNoticeInputs(Release1ShortNoticeViewModel? view) =>
        new(AcceptedStory(PlayerId), false, false, null, null, ShortNotice: view);

    private static Release1PresentationInputs ShortNoticeInputs(
        Release1ShortNoticeViewModel view, bool spreadNoticed, int? shortfallRemaining, Release1MissionState missionState, int attempt = 1)
    {
        var story = WithShortNoticeState(AcceptedStory(PlayerId), missionState, attempt);
        var assignment = MakeShortNoticeAssignment(PlayerId, attempt);
        Release1ShortNoticeProgress? progress = spreadNoticed || shortfallRemaining is not null
            ? new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, attempt, 0, shortfallRemaining, spreadNoticed)
            : null;
        return new(story, false, false, null, null, ShortNotice: view, ShortNoticeAssignment: assignment, ShortNoticeProgress: progress);
    }

    private static Release1ShortNoticeViewModel ShortNoticeOfferView() =>
        new(true, Release1ShortNoticeCardStage.Offer, Release1ShortNoticePresentation.Title,
            Release1PlayerCopy.Normalize(Release1ShortNoticePresentation.OfferText), true, false, false);

    private static Release1ShortNoticeViewModel ShortNoticeTermsView() =>
        new(true, Release1ShortNoticeCardStage.Review, Release1ShortNoticePresentation.Title,
            Release1PlayerCopy.Normalize(ShortNoticeTermsBody()), false, true, true);

    private static Release1ShortNoticeViewModel ShortNoticeActiveView() =>
        new(true, Release1ShortNoticeCardStage.Active, Release1ShortNoticePresentation.Title,
            Release1PlayerCopy.Normalize("Status line."), false, false, false);

    private static string ShortNoticeTermsBody() => string.Join("\n",
        "Supply run, attempt 1",
        "Manifest: 3 Bricks of Cocaine",
        "Drop: Drop D",
        "Beneath the loading dock on Kettleman.",
        "The whole order goes in one slot.",
        "Deadline: 12 in game hours",
        "Payment: 175 percent of the value of what you leave, paid the moment it lands.",
        "The product comes out of your own stock. I am not sending you any.",
        "Are you taking it?");

    private static Release1DesiredQuest ShortNoticeQuest(
        Release1MissionState missionState, bool spreadNoticed = false, int? shortfallRemaining = null, bool blocked = false, int attempt = 1)
    {
        var story = WithShortNoticeState(AcceptedStory(PlayerId), missionState, attempt);
        if (blocked)
            story = story with { NativeEffects = new[] { BlockedShortNoticeEffect(attempt) } };
        var assignment = MakeShortNoticeAssignment(PlayerId, attempt);
        Release1ShortNoticeProgress? progress = spreadNoticed || shortfallRemaining is not null
            ? new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, attempt, 0, shortfallRemaining, spreadNoticed)
            : null;
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            ShortNotice: ShortNoticeActiveView(), ShortNoticeAssignment: assignment, ShortNoticeProgress: progress);
        var plan = Release1PresentationPlanner.Build(inputs);
        return plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.ShortNotice);
    }

    private static Release1NativeEffectJournalEntry BlockedShortNoticeEffect(int attempt) => new(
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

    private static Release1StoryState WithShortNoticeState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.ShortNotice
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "short-notice-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-sn-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1ShortNoticeAssignment MakeShortNoticeAssignment(
        string playerId, int attempt, Release1ShortNoticeAssignmentMode mode = Release1ShortNoticeAssignmentMode.Primary)
    {
        var transitionKind = mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.ShortNotice, attempt, transitionKind, "test-sn-authorization").Value;
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

    // ---- Keep the Lights Off fixtures ----------------------------------------------------------

    private static Release1PresentationInputs KeepTheLightsOffInputs(Release1KeepTheLightsOffViewModel? view) =>
        new(AcceptedStory(PlayerId), false, false, null, null, KeepTheLightsOff: view);

    private static Release1PresentationInputs KeepTheLightsOffInputs(
        Release1KeepTheLightsOffViewModel view, Release1MissionState missionState, double? clearConfirmedAtGameMinutes = null, int attempt = 1)
    {
        var story = WithKeepTheLightsOffState(AcceptedStory(PlayerId), missionState, attempt);
        var assignment = MakeKeepTheLightsOffAssignment(PlayerId, attempt);
        Release1KeepTheLightsOffProgress? progress = clearConfirmedAtGameMinutes is not null
            ? new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, attempt, clearConfirmedAtGameMinutes, null)
            : null;
        return new(story, false, false, null, null,
            KeepTheLightsOff: view, KeepTheLightsOffAssignment: assignment, KeepTheLightsOffProgress: progress);
    }

    private static Release1KeepTheLightsOffViewModel KeepTheLightsOffOfferView() =>
        new(true, Release1KeepTheLightsOffCardStage.Offer, Release1KeepTheLightsOffPresentation.Title,
            Release1PlayerCopy.Normalize(Release1KeepTheLightsOffPresentation.OfferText), true, false, false);

    private static Release1KeepTheLightsOffViewModel KeepTheLightsOffTermsView() =>
        new(true, Release1KeepTheLightsOffCardStage.Review, Release1KeepTheLightsOffPresentation.Title,
            Release1PlayerCopy.Normalize(KeepTheLightsOffTermsBody()), false, true, true);

    private static Release1KeepTheLightsOffViewModel KeepTheLightsOffActiveView() =>
        new(true, Release1KeepTheLightsOffCardStage.Running, Release1KeepTheLightsOffPresentation.Title,
            Release1PlayerCopy.Normalize("Status line."), false, false, false);

    private static string KeepTheLightsOffTermsBody() => string.Join("\n",
        "Shutdown run, attempt 1",
        "Clear every unit of Cocaine from every dead drop and every HQ closet.",
        "Then hold it. Nothing moves for one full day once every container reads clear.",
        "Deadline: 72 in game hours",
        "Payment: 200 percent of the asking price, paid the moment the day is up.",
        "I am not touching any of it. What you do with it after it is out is your business.",
        "Are you taking it?");

    private static Release1DesiredQuest KeepTheLightsOffQuest(
        Release1MissionState missionState, double? clearConfirmedAtGameMinutes = null,
        double? breachSincePassGameMinutes = null, bool blocked = false, int attempt = 1)
    {
        var story = WithKeepTheLightsOffState(AcceptedStory(PlayerId), missionState, attempt);
        if (blocked)
            story = story with { NativeEffects = new[] { BlockedKeepTheLightsOffEffect(attempt) } };
        var assignment = MakeKeepTheLightsOffAssignment(PlayerId, attempt);
        Release1KeepTheLightsOffProgress? progress = clearConfirmedAtGameMinutes is not null || breachSincePassGameMinutes is not null
            ? new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, attempt, clearConfirmedAtGameMinutes, breachSincePassGameMinutes)
            : null;
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            KeepTheLightsOff: KeepTheLightsOffActiveView(), KeepTheLightsOffAssignment: assignment, KeepTheLightsOffProgress: progress);
        var plan = Release1PresentationPlanner.Build(inputs);
        return plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
    }

    private static Release1NativeEffectJournalEntry BlockedKeepTheLightsOffEffect(int attempt) => new(
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

    private static Release1StoryState WithKeepTheLightsOffState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.KeepTheLightsOff
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "keep-the-lights-off-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-ktlo-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1KeepTheLightsOffAssignment MakeKeepTheLightsOffAssignment(
        string playerId, int attempt, Release1KeepTheLightsOffAssignmentMode mode = Release1KeepTheLightsOffAssignmentMode.Primary)
    {
        var transitionKind = mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.KeepTheLightsOff, attempt, transitionKind, "test-ktlo-authorization").Value;
        return new Release1KeepTheLightsOffAssignment(
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            mode,
            authorization,
            3,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);
    }

    // ---- The Envelope fixtures -----------------------------------------------------------------

    private static Release1PresentationInputs TheEnvelopeInputs(Release1TheEnvelopeViewModel? view) =>
        new(AcceptedStory(PlayerId), false, false, null, null, TheEnvelope: view);

    private static Release1PresentationInputs TheEnvelopeInputs(
        Release1TheEnvelopeViewModel view, bool spreadNoticed, double? shortfallRemaining, Release1MissionState missionState, int attempt = 1)
    {
        var story = WithTheEnvelopeState(AcceptedStory(PlayerId), missionState, attempt);
        var assignment = MakeTheEnvelopeAssignment(PlayerId, attempt);
        Release1TheEnvelopeProgress? progress = spreadNoticed || shortfallRemaining is not null
            ? new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, attempt, 0, shortfallRemaining, spreadNoticed)
            : null;
        return new(story, false, false, null, null, TheEnvelope: view, TheEnvelopeAssignment: assignment, TheEnvelopeProgress: progress,
            HoldRoomMarker: DefaultHoldRoomMarker);
    }

    private static Release1TheEnvelopeViewModel TheEnvelopeOfferView() =>
        new(true, Release1TheEnvelopeCardStage.Offer, Release1TheEnvelopePresentation.Title,
            Release1PlayerCopy.Normalize(Release1TheEnvelopePresentation.OfferText), true, false, false);

    private static Release1TheEnvelopeViewModel TheEnvelopeTermsView() =>
        new(true, Release1TheEnvelopeCardStage.Review, Release1TheEnvelopePresentation.Title,
            Release1PlayerCopy.Normalize(TheEnvelopeTermsBody()), false, true, true);

    private static Release1TheEnvelopeViewModel TheEnvelopeActiveView() =>
        new(true, Release1TheEnvelopeCardStage.Active, Release1TheEnvelopePresentation.Title,
            Release1PlayerCopy.Normalize("Status line."), false, false, false);

    private static string TheEnvelopeTermsBody() => string.Join("\n",
        "Amount: $20000 in cash, 20 stacks.",
        "Closet: one closet in the storage room at the Syndicate HQ. All 20 stacks in it.",
        "Deadline: 24 in game hours",
        "No payment. I take the number and nothing else; this one buys standing, not dollars.",
        "The cash is your own. I am not fronting you anything.",
        "Are you taking it?");

    private static Release1DesiredQuest TheEnvelopeQuest(
        Release1MissionState missionState, bool spreadNoticed = false, double? shortfallRemaining = null, bool blocked = false, int attempt = 1) =>
        TheEnvelopeQuest(missionState, spreadNoticed, shortfallRemaining, blocked, attempt, DefaultHoldRoomMarker);

    private static Release1DesiredQuest TheEnvelopeQuest(
        Release1MissionState missionState, bool spreadNoticed, double? shortfallRemaining, bool blocked, int attempt,
        Release1WorldPoint? holdRoomMarker)
    {
        var story = WithTheEnvelopeState(AcceptedStory(PlayerId), missionState, attempt);
        if (blocked)
            story = story with { NativeEffects = new[] { BlockedTheEnvelopeEffect(attempt) } };
        var assignment = MakeTheEnvelopeAssignment(PlayerId, attempt);
        Release1TheEnvelopeProgress? progress = spreadNoticed || shortfallRemaining is not null
            ? new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, attempt, 0, shortfallRemaining, spreadNoticed)
            : null;
        var inputs = new Release1PresentationInputs(
            story, false, false, null, null,
            TheEnvelope: TheEnvelopeActiveView(), TheEnvelopeAssignment: assignment, TheEnvelopeProgress: progress,
            HoldRoomMarker: holdRoomMarker);
        var plan = Release1PresentationPlanner.Build(inputs);
        return plan.Quests.Single(quest => quest.Key == Release1MissionCatalog.TheEnvelope);
    }

    private static Release1NativeEffectJournalEntry BlockedTheEnvelopeEffect(int attempt) => new(
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

    private static Release1StoryState WithTheEnvelopeState(Release1StoryState story, Release1MissionState state, int attempt = 1) =>
        story with
        {
            Missions = story.Missions.Select(mission => mission.MissionKey == Release1MissionCatalog.TheEnvelope
                ? mission with
                {
                    State = state,
                    Attempt = attempt,
                    TermsVersion = "the-envelope-v1",
                    LastOutcome = state == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None,
                    RewardAuthorizationReceiptId = state == Release1MissionState.Satisfied ? "test-te-reward-receipt" : null
                }
                : mission).ToArray()
        };

    private static Release1TheEnvelopeAssignment MakeTheEnvelopeAssignment(
        string playerId, int attempt, Release1TheEnvelopeAssignmentMode mode = Release1TheEnvelopeAssignmentMode.Primary)
    {
        var transitionKind = mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.TheEnvelope, attempt, transitionKind, "test-te-authorization").Value;
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
}
