using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PresentationProjectorTests
{
    private const string PlayerId = "76561190000000001";

    // ---- Step 1: gate tests -------------------------------------------------------------

    [Fact]
    public void Inactive_story_produces_zero_native_calls()
    {
        var harness = new Harness(); // story never reaches Active: no OnLoadComplete
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, false, false, null, null));

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);
    }

    [Fact]
    public void Quarantined_story_produces_zero_native_calls()
    {
        var harness = new Harness();
        harness.Repository.FailLoad = true;
        harness.Story.OnPreLoad();
        harness.Story.OnLoadComplete(); // quarantines because the sidecar load fails
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, false, false, null, null));

        projector.Reconcile();

        Assert.Equal(Release1StoryRuntimePhase.Quarantined, harness.Story.Phase);
        Assert.Empty(harness.Native.Events);
    }

    [Fact]
    public void Saving_phase_produces_zero_native_calls()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        harness.Story.OnSaveStart();
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);
    }

    [Fact]
    public void Failed_TryGetActiveContext_produces_zero_native_calls()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        harness.Context.Snapshot = harness.Context.Snapshot with { PlayerId = "76561190000000002" }; // identity now mismatches
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.Empty(harness.Native.Events);
    }

    // ---- Step 2: message tests -----------------------------------------------------------

    [Fact]
    public void Desired_message_with_no_receipt_sends_once_and_records_a_receipt()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        var sendEvent = Assert.Single(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        var text = sendEvent["SendMessage:".Length..];
        Assert.NotEmpty(harness.Story.State!.PresentationReceipts);
        var receiptId = harness.Story.State!.PresentationReceipts[0].CorrelationId;
        Assert.True(Release1LogicalCorrelation.TryParse(receiptId, out var parsed));
        Assert.Equal(Release1TransitionKind.IntroAccepted, parsed.TransitionKind);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void Second_reconcile_sends_nothing()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));
        projector.Reconcile();
        var sendCountAfterFirst = harness.Native.Events.Count(e => e.StartsWith("SendMessage:", StringComparison.Ordinal));

        projector.Reconcile();

        var sendCountAfterSecond = harness.Native.Events.Count(e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        Assert.Equal(1, sendCountAfterFirst);
        Assert.Equal(1, sendCountAfterSecond);
    }

    [Fact]
    public void Existing_receipt_in_state_suppresses_the_send()
    {
        var story = AcceptedStory();
        var expectedCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "presentation-intro-accepted-v1").Value;
        var withReceipt = story with { PresentationReceipts = new[] { new Release1PresentationReceipt(expectedCorrelation, story.Revision) } };
        var harness = new Harness();
        harness.ActivateStory(withReceipt);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
    }

    [Fact]
    public void Receipt_present_suppresses_the_send_even_when_native_reports_the_text_already_sent()
    {
        // A receipt is now the sole dedupe key; native history is never consulted. Seeding native
        // with the exact desired text (as if it had somehow already shown up there) must have no
        // effect on its own, and the receipt already on the story must still be what suppresses it.
        var story = AcceptedStory();
        var expectedCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "presentation-intro-accepted-v1").Value;
        var withReceipt = story with { PresentationReceipts = new[] { new Release1PresentationReceipt(expectedCorrelation, story.Revision) } };
        var harness = new Harness();
        harness.ActivateStory(withReceipt);
        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(harness.Story.State, false, false, null, null));
        var text = Assert.Single(plan.Messages).Text;
        harness.Native.SentMessages.Add(text);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        Assert.Single(harness.Story.State!.PresentationReceipts);
    }

    [Fact]
    public void Text_present_in_observed_history_but_no_receipt_sends_once_and_records_the_receipt()
    {
        // The text guard is gone: native already showing the exact text is no longer a reason to
        // skip a send. Only a matching receipt does that, and none exists here.
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(harness.Story.State, false, false, null, null));
        var text = Assert.Single(plan.Messages).Text;
        harness.Native.SentMessages.Add(text);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == $"SendMessage:{text}");
        Assert.NotEmpty(harness.Story.State!.PresentationReceipts);
    }

    [Fact]
    public void Identical_milestone_text_on_a_later_attempt_with_a_new_correlation_is_sent_again()
    {
        // A make-good or recovery attempt reaches Satisfied again with the exact same delivered
        // text as an earlier attempt. The receipt from that earlier attempt is still on record (it
        // is never cleared by an attempt change), but its correlation embeds the old attempt number,
        // so it does not cover the new attempt's correlation: the milestone message must be sent
        // again rather than swallowed for matching an old, unrelated receipt's text.
        var attempt1Correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, 1, Release1TransitionKind.MissionCompleted, "presentation-nell-wa-delivered-v1").Value;
        var story = WithWrongAddressState(AcceptedStory(), Release1MissionState.Satisfied, attempt: 2)
            with
        { PresentationReceipts = new[] { new Release1PresentationReceipt(attempt1Correlation, 1) } };
        var harness = new Harness();
        harness.ActivateStory(story);
        var assignment = MakeWrongAddressAssignment(2);
        var view = WrongAddressView(Release1WrongAddressCardStage.Completed, "Status line.");
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null, view, assignment, null));

        var plan = Release1PresentationPlanner.Build(new Release1PresentationInputs(harness.Story.State, false, false, null, null, view, assignment, null));
        var deliveredText = Assert.Single(plan.Messages, m => m.Text.StartsWith("Received and paid", StringComparison.Ordinal)).Text;

        projector.Reconcile();

        // The intro and custody messages also send on this pass (Satisfied implies custody), so more
        // than one new receipt is expected; what matters here is that the delivered text was sent
        // again and that a distinct, newer receipt now covers it alongside the untouched old one.
        Assert.Contains(harness.Native.Events, e => e == $"SendMessage:{deliveredText}");
        Assert.Contains(harness.Story.State!.PresentationReceipts, r => r.CorrelationId == attempt1Correlation);
        Assert.Contains(harness.Story.State!.PresentationReceipts,
            r => r.CorrelationId != attempt1Correlation && r.CorrelationId.Contains("MissionCompleted", StringComparison.Ordinal));
    }

    [Fact]
    public void Null_story_state_sends_the_intro_offer_once_through_the_decision_and_never_records_a_receipt()
    {
        // The intro offer is no longer queued as a separate passive message: it is sent exactly once,
        // as the intro decision's prompt (TrySendMessage is never called for it).
        var harness = new Harness();
        harness.ActivateStory(null); // Active phase with no story yet, matching a fresh save
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        var text = Release1PresentationPlanner.Build(new Release1PresentationInputs(null, true, false, null, null)).Decision!.Prompt;
        Assert.Equal(1, harness.Native.SentMessages.Count(sent => sent == text));
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        Assert.Null(harness.Story.State);

        projector.Reconcile(); // second pass: decision already matches natively, nothing new is sent

        Assert.Equal(1, harness.Native.SentMessages.Count(sent => sent == text));
        Assert.Null(harness.Story.State);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void Small_courtesy_review_replaced_by_accept_sends_the_body_once_and_adds_exactly_one_new_distinct_message()
    {
        // Small Courtesy's offer body is sent once, as the review decision's prompt. When the review
        // decision is replaced by the accept/defer decision, only the new, distinct accept prompt is
        // sent; the body is never repeated.
        var harness = new Harness();
        var story = AcceptedStory();
        harness.ActivateStory(story);
        const string body = "Terms are on the table.";
        // The review prompt is the short offer teaser; the accept prompt is the full reviewed terms,
        // ending in the question, per Release1SmallCourtesyPresentation.Terms. They must be distinct
        // strings for this test to prove the accept prompt is sent as a new message, not a repeat.
        const string acceptBody = "Terms are on the table.\nAre you in?";
        var reviewView = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Offer, "Small Courtesy", body, true, false, true);
        var acceptView = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Review, "Small Courtesy", acceptBody, false, true, true);
        var currentView = reviewView;
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, currentView, null));

        projector.Reconcile();
        var normalizedBody = Release1PlayerCopy.Normalize(body);
        Assert.Equal(1, harness.Native.SentMessages.Count(sent => sent == normalizedBody));

        var acceptPrompt = Release1PresentationPlanner.Build(new Release1PresentationInputs(harness.Story.State, false, false, acceptView, null)).Decision!.Prompt;
        currentView = acceptView;
        projector.Reconcile();

        Assert.Equal(1, harness.Native.SentMessages.Count(sent => sent == normalizedBody));
        Assert.Equal(1, harness.Native.SentMessages.Count(sent => sent == acceptPrompt));
        Assert.NotEqual(normalizedBody, acceptPrompt);
    }

    // Failed_TryReadSentMessages_ends_the_pass_before_any_send removed: Reconcile no longer calls
    // TryReadSentMessages (see the receipt-only dedupe change above), so a failure from it can no
    // longer affect a reconcile pass. The method stays on FakeNative only because it stays on the
    // interface for the boundary and its own tests.

    [Fact]
    public void Unavailable_from_send_records_no_receipt_and_retries_on_the_next_pass()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        harness.Native.SendStatus = Release1NativePresentationStatus.Unavailable;
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();
        Assert.Empty(harness.Story.State!.PresentationReceipts);
        Assert.Empty(harness.Native.SentMessages);

        harness.Native.SendStatus = Release1NativePresentationStatus.Succeeded;
        projector.Reconcile();

        Assert.NotEmpty(harness.Story.State!.PresentationReceipts);
        Assert.Single(harness.Native.SentMessages);
        Assert.Equal(2, harness.Native.Events.Count(e => e.StartsWith("SendMessage:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Receipt_in_memory_disappears_after_preload_without_a_save_and_the_message_is_sent_again()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();
        var sendEvent = Assert.Single(harness.Native.Events, e => e.StartsWith("SendMessage:", StringComparison.Ordinal));
        var text = sendEvent["SendMessage:".Length..];
        Assert.NotEmpty(harness.Story.State!.PresentationReceipts);

        // Unsaved reload: story reverts to the last persisted (receipt-less) snapshot, and the
        // native conversation history reverts with the same save.
        projector.OnPreLoad();
        harness.Story.OnPreLoad();
        harness.Story.OnLoadComplete();
        harness.Native.SentMessages.Clear();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"SendMessage:{text}");
    }

    // ---- Step 3: decision tests -----------------------------------------------------------

    [Fact]
    public void Desired_decision_absent_natively_is_set_once()
    {
        var harness = new Harness();
        harness.ActivateStory(null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == "SetDecision:release1-intro");
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    [Fact]
    public void Identical_observed_decision_makes_no_decision_calls()
    {
        var harness = new Harness();
        harness.ActivateStory(null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));
        projector.Reconcile();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision" || e.StartsWith("SetDecision:", StringComparison.Ordinal));
        Assert.Contains(harness.Native.Events, e => e == "ReadDecision");
    }

    [Fact]
    public void Changed_labels_clears_then_sets()
    {
        var harness = new Harness();
        harness.ActivateStory(null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));
        projector.Reconcile();
        harness.Native.ObservedDecision = new Release1ObservedDecision("release1-intro", new[] { "Accept" }, null); // labels differ
        harness.Native.Events.Clear();

        projector.Reconcile();

        var clearIndex = harness.Native.Events.IndexOf("ClearDecision");
        var setIndex = harness.Native.Events.IndexOf("SetDecision:release1-intro");
        Assert.True(clearIndex >= 0 && setIndex >= 0 && clearIndex < setIndex);
    }

    [Fact]
    public void Observed_decision_with_unknown_id_but_matching_labels_rebinds_via_set_decision_without_clearing()
    {
        // A boundary that just re-adopted its contact after a reload cannot recover which logical
        // decision id produced the native responses it sees; it reports Id = null. The labels match
        // the desired decision, but nothing in this session has bound a callback onto those restored
        // responses yet (that only happens inside TrySetDecision's bind-existing branch), so the
        // projector must still call TrySetDecision, without clearing first, since that would blow
        // away the responses the player is looking at.
        var harness = new Harness();
        harness.ActivateStory(null);
        var expectedPrompt = Release1PresentationPlanner.Build(new Release1PresentationInputs(null, true, false, null, null)).Decision!.Prompt;
        harness.Native.ObservedDecision = new Release1ObservedDecision(null, new[] { "Accept", "Not now" }, expectedPrompt);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == "SetDecision:release1-intro");
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    [Fact]
    public void Observed_decision_with_restored_prompt_matching_whitespace_insensitively_rebinds_without_clearing()
    {
        // When a prompt is restored across a reload with line breaks replaced by spaces and with
        // trailing whitespace, the projector should still recognize it as matching the desired prompt
        // and bind to the existing responses without clearing.
        var harness = new Harness();
        harness.ActivateStory(null);
        var expectedPrompt = Release1PresentationPlanner.Build(new Release1PresentationInputs(null, true, false, null, null)).Decision!.Prompt;
        var restoredPrompt = expectedPrompt.Replace("\n", " ").Replace("\r", " ") + "\r\n";
        harness.Native.ObservedDecision = new Release1ObservedDecision(null, new[] { "Accept", "Not now" }, restoredPrompt);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == "SetDecision:release1-intro");
        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
    }

    // Identical_observed_decision_makes_no_decision_calls (below) already covers the non-null matching
    // id case: no bind and no send on a subsequent pass once this session has set the decision itself.

    [Fact]
    public void Observed_decision_with_unknown_id_and_different_labels_clears_then_sets()
    {
        var harness = new Harness();
        harness.ActivateStory(null);
        harness.Native.ObservedDecision = new Release1ObservedDecision(null, new[] { "Something else" }, null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        var clearIndex = harness.Native.Events.IndexOf("ClearDecision");
        var setIndex = harness.Native.Events.IndexOf("SetDecision:release1-intro");
        Assert.True(clearIndex >= 0 && setIndex >= 0 && clearIndex < setIndex);
    }

    [Fact]
    public void Observed_decision_with_unknown_id_matching_labels_but_different_prompt_clears_then_sets()
    {
        // Guards against two different decisions sharing the same label set (the intro decision and
        // the Small Courtesy accept decision both offer "Accept"/"Not now"): after a reload, when the
        // boundary cannot recover the id, label-only matching would wrongly treat them as already
        // showing. The prompt must also match before the projector treats the decision as settled.
        var harness = new Harness();
        harness.ActivateStory(null);
        harness.Native.ObservedDecision = new Release1ObservedDecision(null, new[] { "Accept", "Not now" }, "Some other prompt entirely.");
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));

        projector.Reconcile();

        var clearIndex = harness.Native.Events.IndexOf("ClearDecision");
        var setIndex = harness.Native.Events.IndexOf("SetDecision:release1-intro");
        Assert.True(clearIndex >= 0 && setIndex >= 0 && clearIndex < setIndex);
    }

    [Fact]
    public void Plan_without_decision_but_observed_one_clears()
    {
        var harness = new Harness();
        var deferred = DeferredStory();
        harness.ActivateStory(deferred);
        harness.Native.ObservedDecision = new Release1ObservedDecision("stale-decision", new[] { "Whatever" }, null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == "ClearDecision");
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetDecision:", StringComparison.Ordinal));
    }

    [Fact]
    public void Undetermined_plan_skips_the_decision_reconcile_and_still_runs_the_quest_loop()
    {
        // While intro eligibility is still being observed after a reload, the plan reports
        // DecisionUndetermined even though native still shows a restored decision. The projector
        // must not clear or set anything for the decision, but quest reconciliation still runs in
        // the same pass.
        var harness = new Harness();
        var story = WithSmallCourtesyState(DeferredStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        harness.Native.ObservedDecision = new Release1ObservedDecision("stale-decision", new[] { "Whatever" }, null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, true, view, assignment));

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e == "ClearDecision");
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("SetDecision:", StringComparison.Ordinal));
        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Chosen_callback_invokes_the_sink_exactly_once_and_reconciles()
    {
        var harness = new Harness();
        harness.ActivateStory(null);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(null, true, false, null, null));
        projector.Reconcile();
        Assert.NotNull(harness.Native.LastOnChosen);
        var eventsBefore = harness.Native.Events.Count;

        harness.Native.LastOnChosen!(Release1PresentationCommand.IntroAccept);

        Assert.Equal(new[] { Release1PresentationCommand.IntroAccept }, harness.Commands.Invoked);
        Assert.True(harness.Native.Events.Count > eventsBefore); // Reconcile() ran again
        Assert.Equal("EnsureContact", harness.Native.Events[eventsBefore]);
    }

    // ---- Step 4: quest tests ---------------------------------------------------------------

    [Fact]
    public void Desired_quest_absent_is_applied()
    {
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("EndQuest:", StringComparison.Ordinal));
    }

    [Fact]
    public void AllComplete_desired_quest_with_no_native_quest_makes_no_quest_write()
    {
        // Once every desired entry reaches Complete, S1API auto-completes and deregisters the quest,
        // so TryReadQuest reports it absent on every later load. Applying here would only recreate it
        // for S1API to complete and deregister again next pass; the projector must leave it dropped.
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Satisfied);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Completed, "Small Courtesy", "Complete.", false, false, false);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ReadQuest:{Release1MissionCatalog.SmallCourtesy}");
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
    }

    [Fact]
    public void AllComplete_desired_quest_with_present_native_quest_applies_once()
    {
        // When native still has the quest (mid-completion this pass, or not yet auto-deregistered by
        // S1API), the projector must keep applying so it actually reaches Complete rather than skip it.
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Satisfied);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        harness.Native.ObservedQuests[Release1MissionCatalog.SmallCourtesy] = new Release1ObservedQuest(
            Release1MissionCatalog.SmallCourtesy, new[] { Release1DesiredEntryState.Active, Release1DesiredEntryState.Inactive });
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Completed, "Small Courtesy", "Complete.", false, false, false);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Quest_with_an_active_entry_and_no_native_quest_is_still_created()
    {
        // Guards the new all-Complete skip against over-triggering: a desired quest carrying any
        // non-Complete entry must still be created when native has none, exactly as before this fix.
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Active);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.Active, "Small Courtesy", "Delivery active.", false, false, false);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Present_but_undesired_quest_is_ended_while_a_desired_quest_is_left_alone()
    {
        var harness = new Harness();
        // Mission is Offered, not Accepted/Active/Satisfied, so the plan drops the Small Courtesy
        // quest entirely even though native still reports one showing (e.g. from an earlier chapter).
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Offered);
        harness.ActivateStory(story);
        harness.Native.ObservedQuests[Release1MissionCatalog.SmallCourtesy] = new Release1ObservedQuest(
            Release1MissionCatalog.SmallCourtesy, new[] { Release1DesiredEntryState.Active, Release1DesiredEntryState.Inactive });
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"EndQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Partial_decision_bind_does_not_stall_quest_reconciliation()
    {
        // A decision status other than Faulted/Succeeded (e.g. TrySetDecision still waiting
        // on restored responses) is an ordinary recoverable condition, like Unavailable/Rejected
        // everywhere else on this boundary (see TryStatus and the class doc), so it is never logged.
        // The pass must still continue past it, so a desired quest still gets applied in the same
        // pass instead of waiting on the decision to settle first.
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, true, true);
        harness.Native.SetDecisionStatus = Release1NativePresentationStatus.Unavailable;
        var logs = new List<string>();
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment), logs.Add);

        projector.Reconcile();

        Assert.Empty(logs);
        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Identical_entry_states_make_no_apply_call()
    {
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));
        projector.Reconcile();
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
        Assert.Contains(harness.Native.Events, e => e == $"ReadQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Changed_entry_states_apply_once()
    {
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        harness.Native.ObservedQuests[Release1MissionCatalog.SmallCourtesy] = new Release1ObservedQuest(
            Release1MissionCatalog.SmallCourtesy, new[] { Release1DesiredEntryState.Inactive, Release1DesiredEntryState.Inactive });
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment));

        projector.Reconcile();

        Assert.Single(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.SmallCourtesy}");
    }

    [Fact]
    public void Boundary_faulted_ends_the_pass_without_throwing_and_logs_once()
    {
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, false, false);
        harness.Native.QuestReadStatus = Release1NativePresentationStatus.Faulted;
        var logs = new List<string>();
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment), logs.Add);

        var exception = Record.Exception(() => projector.Reconcile());

        Assert.Null(exception);
        Assert.Single(logs);
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
    }

    // ---- Decision-status logging must not flood the log --------------------------------

    [Fact]
    public void Repeated_reconcile_passes_with_a_persistently_unavailable_decision_do_not_flood_the_log()
    {
        // For roughly the first 10 seconds after a load, S1API has not yet restored decision
        // responses, so TrySetDecision reports Unavailable on every pass while the host calls
        // Reconcile() every frame (~700 passes over that window). Unavailable/Rejected are ordinary
        // recoverable conditions everywhere else on this boundary (TryStatus never logs them), so
        // ApplyDecisionStatus must not log them either, no matter how many passes it takes to settle.
        // Before the fix, the "log once" guard was a local reset on every Reconcile() call, so this
        // logged once per pass: 100 passes produced ~100 identical lines instead of zero.
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, true, true);
        harness.Native.SetDecisionStatus = Release1NativePresentationStatus.Unavailable;
        var logs = new List<string>();
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment), logs.Add);

        for (var pass = 0; pass < 100; pass++)
            projector.Reconcile();

        Assert.Empty(logs);
    }

    [Fact]
    public void Faulted_decision_set_still_logs_and_ends_the_pass()
    {
        // A genuine fault on the decision-set call is not a recoverable condition: it must still be
        // logged once, exactly like every other boundary call, and the pass must still end before the
        // quest loop runs (unlike Unavailable/Rejected, which let the pass continue).
        var harness = new Harness();
        var story = WithSmallCourtesyState(AcceptedStory(), Release1MissionState.Accepted);
        harness.ActivateStory(story);
        var assignment = MakeAssignment(1);
        var view = new Release1SmallCourtesyViewModel(true, Release1SmallCourtesyCardStage.AwaitingActivation, "Small Courtesy", "Accepted.", false, true, true);
        harness.Native.SetDecisionStatus = Release1NativePresentationStatus.Faulted;
        var logs = new List<string>();
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, view, assignment), logs.Add);

        var exception = Record.Exception(() => projector.Reconcile());

        Assert.Null(exception);
        Assert.Single(logs);
        Assert.Contains(logs, message => message.Contains("reported a fault", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
    }

    // ---- Step 5: Wrong Address quest tests (Task 5) -----------------------------------------

    [Fact]
    public void Wrong_address_quest_is_applied_once_and_not_reapplied_when_entry_states_match()
    {
        var harness = new Harness();
        var story = WithWrongAddressState(AcceptedStory(), Release1MissionState.Active);
        harness.ActivateStory(story);
        var assignment = MakeWrongAddressAssignment(1);
        var view = WrongAddressView(Release1WrongAddressCardStage.Active, "Active.");
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null, view, assignment, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.WrongAddress}");
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
        Assert.Contains(harness.Native.Events, e => e == $"ReadQuest:{Release1MissionCatalog.WrongAddress}");
    }

    [Fact]
    public void Plan_dropping_the_wrong_address_quest_ends_the_observed_one()
    {
        var harness = new Harness();
        // Mission is Offered, not Accepted/Active/Satisfied, so the plan drops the Wrong Address
        // quest entirely even though native still reports one showing (e.g. from an earlier chapter).
        var story = WithWrongAddressState(AcceptedStory(), Release1MissionState.Offered);
        harness.ActivateStory(story);
        harness.Native.ObservedQuests[Release1MissionCatalog.WrongAddress] = new Release1ObservedQuest(
            Release1MissionCatalog.WrongAddress, new[] { Release1DesiredEntryState.Active, Release1DesiredEntryState.Inactive });
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null, null, null, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"EndQuest:{Release1MissionCatalog.WrongAddress}");
    }

    [Fact]
    public void Wrong_address_chosen_callback_invokes_the_sink_exactly_once_and_reconciles()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var view = WrongAddressView(Release1WrongAddressCardStage.Review, "Terms.", canAccept: true, canDefer: true);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null, view, null, null));
        projector.Reconcile();
        Assert.NotNull(harness.Native.LastOnChosen);
        var eventsBefore = harness.Native.Events.Count;

        harness.Native.LastOnChosen!(Release1PresentationCommand.WrongAddressAccept);

        Assert.Equal(new[] { Release1PresentationCommand.WrongAddressAccept }, harness.Commands.Invoked);
        Assert.True(harness.Native.Events.Count > eventsBefore); // Reconcile() ran again
        Assert.Equal("EnsureContact", harness.Native.Events[eventsBefore]);
    }

    // ---- Step 5b: Room With No Name quest tests (Task 7) --------------------------------------

    [Fact]
    public void Room_with_no_name_quest_is_applied_once_and_not_reapplied_when_entry_states_match()
    {
        var harness = new Harness();
        var story = WithRoomWithNoNameState(AcceptedStory(), Release1MissionState.Active);
        harness.ActivateStory(story);
        var assignment = MakeRoomWithNoNameAssignment(1);
        var view = RoomWithNoNameView(Release1RoomWithNoNameCardStage.Active, "Active.");
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(
            harness.Story.State, false, false, null, null, null, null, null, view, assignment, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ApplyQuest:{Release1MissionCatalog.RoomWithNoName}");
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
        Assert.Contains(harness.Native.Events, e => e == $"ReadQuest:{Release1MissionCatalog.RoomWithNoName}");
    }

    [Fact]
    public void Plan_dropping_the_room_with_no_name_quest_ends_the_observed_one()
    {
        var harness = new Harness();
        // Mission is Offered, not Accepted/Active/Satisfied, so the plan drops the Room With No Name
        // quest entirely even though native still reports one showing (e.g. from an earlier chapter).
        var story = WithRoomWithNoNameState(AcceptedStory(), Release1MissionState.Offered);
        harness.ActivateStory(story);
        harness.Native.ObservedQuests[Release1MissionCatalog.RoomWithNoName] = new Release1ObservedQuest(
            Release1MissionCatalog.RoomWithNoName, new[] { Release1DesiredEntryState.Active, Release1DesiredEntryState.Inactive, Release1DesiredEntryState.Inactive });
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(
            harness.Story.State, false, false, null, null, null, null, null, null, null, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"EndQuest:{Release1MissionCatalog.RoomWithNoName}");
    }

    [Fact]
    public void A_fully_complete_room_with_no_name_quest_already_dropped_by_native_stays_dropped()
    {
        var harness = new Harness();
        var story = WithRoomWithNoNameState(AcceptedStory(), Release1MissionState.Satisfied);
        harness.ActivateStory(story);
        var assignment = MakeRoomWithNoNameAssignment(1);
        var view = RoomWithNoNameView(Release1RoomWithNoNameCardStage.Completed, "Complete.");
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(
            harness.Story.State, false, false, null, null, null, null, null, view, assignment, null));

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == $"ReadQuest:{Release1MissionCatalog.RoomWithNoName}");
        Assert.DoesNotContain(harness.Native.Events, e => e.StartsWith("ApplyQuest:", StringComparison.Ordinal));
    }

    [Fact]
    public void Room_with_no_name_chosen_callback_invokes_the_sink_exactly_once_and_reconciles()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var view = RoomWithNoNameView(Release1RoomWithNoNameCardStage.Review, "Terms.", canAccept: true, canDefer: true);
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(
            harness.Story.State, false, false, null, null, null, null, null, view, null, null));
        projector.Reconcile();
        Assert.NotNull(harness.Native.LastOnChosen);
        var eventsBefore = harness.Native.Events.Count;

        harness.Native.LastOnChosen!(Release1PresentationCommand.RoomWithNoNameAccept);

        Assert.Equal(new[] { Release1PresentationCommand.RoomWithNoNameAccept }, harness.Commands.Invoked);
        Assert.True(harness.Native.Events.Count > eventsBefore); // Reconcile() ran again
        Assert.Equal("EnsureContact", harness.Native.Events[eventsBefore]);
    }

    private static Release1RoomWithNoNameViewModel RoomWithNoNameView(
        Release1RoomWithNoNameCardStage stage, string body, bool canReview = false, bool canAccept = false, bool canDefer = false) =>
        new(true, stage, "A Room With No Name", Release1PlayerCopy.Normalize(body), canReview, canAccept, canDefer);

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

    private static Release1RoomWithNoNameAssignment MakeRoomWithNoNameAssignment(int attempt)
    {
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.RoomWithNoName, attempt, Release1TransitionKind.MissionAccepted, "test-rn-authorization").Value;
        return new Release1RoomWithNoNameAssignment(
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            Release1RoomWithNoNameAssignmentMode.Primary,
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

    private static Release1WrongAddressViewModel WrongAddressView(
        Release1WrongAddressCardStage stage, string body, bool canReview = false, bool canAccept = false, bool canDefer = false) =>
        new(true, stage, "Wrong Address", Release1PlayerCopy.Normalize(body), canReview, canAccept, canDefer);

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

    private static Release1WrongAddressAssignment MakeWrongAddressAssignment(int attempt)
    {
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, attempt, Release1TransitionKind.MissionAccepted, "test-wa-authorization").Value;
        return new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress,
            attempt,
            Release1WrongAddressAssignmentMode.Primary,
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

    // ---- Lifecycle: save suspension ---------------------------------------------------------

    [Fact]
    public void Reconcile_is_suspended_between_OnSaveStart_and_OnSaveComplete()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.OnSaveStart();
        Assert.Contains(harness.Native.Events, e => e == "OnSaveStart");
        harness.Native.Events.Clear();

        projector.Reconcile();
        Assert.Empty(harness.Native.Events);

        projector.OnSaveComplete();
        Assert.Contains(harness.Native.Events, e => e == "EnsureContact");
    }

    [Fact]
    public void OnPreLoad_clears_the_save_suspension()
    {
        var harness = new Harness();
        harness.ActivateStory(AcceptedStory());
        var projector = harness.CreateProjector(() => new Release1PresentationInputs(harness.Story.State, false, false, null, null));

        projector.OnSaveStart();
        projector.OnPreLoad();
        Assert.Contains(harness.Native.Events, e => e == "OnPreLoad");
        harness.Native.Events.Clear();

        projector.Reconcile();

        Assert.Contains(harness.Native.Events, e => e == "EnsureContact");
    }

    // ---- Shared test infrastructure ---------------------------------------------------------

    private static Release1StoryState AcceptedStory()
    {
        var introCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "test-intro-receipt").Value;
        return Release1StoryState.CreateAccepted(PlayerId, introCorrelation);
    }

    private static Release1StoryState DeferredStory()
    {
        const string receipt = "test-defer-receipt";
        var command = new Release1StoryCommand(
            Harness.ReadyContext.SessionEpoch, Harness.ReadyContext.LoadEpoch, PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroDeferred, receipt,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroDeferred, receipt).Value);
        return Release1StoryTransitions.Apply(null, command).State!;
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

    private static Release1SmallCourtesyAssignment MakeAssignment(int attempt)
    {
        var authorization = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.SmallCourtesy, attempt, Release1TransitionKind.MissionAccepted, "test-sc-authorization").Value;
        return new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy, attempt, Release1SmallCourtesyAssignmentMode.Primary, authorization,
            "cocaine", "Cocaine", 500d, "brick", "Brick",
            "drop-guid-1", "Drainage Ditch Drop", "Behind the drainage ditch off Dyer Street.",
            120.5d, 10.25d, -40.75d, 1.25d);
    }

    private static Release1MissionRecord NewMission(string key, Release1MissionState state) => new(
        key, state, 1, null, null, null, Release1MissionOutcome.None, 0,
        Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0);

    private sealed class Harness
    {
        public static readonly Release1StoryHostContextSnapshot ReadyContext = new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            1,
            PlayerId,
            Path.GetFullPath(Path.GetTempPath()));

        public Harness()
        {
            Context = new FakeContext { Snapshot = ReadyContext };
            Repository = new FakeRepository(null);
            Story = new Release1StoryRuntimeService(Context, Repository);
            Native = new FakeNative();
            Commands = new FakeCommandSink();
        }

        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public Release1StoryRuntimeService Story { get; }
        public FakeNative Native { get; }
        public FakeCommandSink Commands { get; }

        public void ActivateStory(Release1StoryState? initialState)
        {
            Repository.StoredState = initialState;
            Story.OnPreLoad();
            Story.OnLoadComplete();
        }

        public Release1PresentationProjector CreateProjector(Func<Release1PresentationInputs> inputs, Action<string>? log = null) =>
            new(Story, () => Release1PresentationPlanner.Build(inputs()), Native, Commands, log);
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; set; }
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public FakeRepository(Release1StoryState? storedState) => StoredState = storedState;
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; set; }
        public bool FailLoad { get; set; }
        public Release1StoryStoreLoadResult Load() => FailLoad
            ? new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.SidecarReadFailed, "synthetic load failure")
            : new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
                Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            StoredState = state;
            return new(true, Release1StoryStoreUpdateStatus.Updated,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
                Release1StoryStoreFailureReason.None, string.Empty);
        }
    }

    private sealed class FakeCommandSink : IRelease1PresentationCommandSink
    {
        public List<Release1PresentationCommand> Invoked { get; } = new();
        public bool ReturnValue { get; set; } = true;
        public bool TryInvoke(Release1PresentationCommand command)
        {
            Invoked.Add(command);
            return ReturnValue;
        }
    }

    private sealed class FakeNative : IRelease1NativePresentation
    {
        public List<string> Events { get; } = new();
        public HashSet<string> ThrowFrom { get; } = new();

        public Release1NativePresentationStatus ContactStatus = Release1NativePresentationStatus.Succeeded;
        public Release1NativePresentationStatus ReadSentStatus = Release1NativePresentationStatus.Succeeded;
        public List<string> SentMessages { get; } = new();
        public Release1NativePresentationStatus SendStatus = Release1NativePresentationStatus.Succeeded;
        public Release1NativePresentationStatus DecisionReadStatus = Release1NativePresentationStatus.Succeeded;
        public Release1ObservedDecision? ObservedDecision;
        public Release1NativePresentationStatus SetDecisionStatus = Release1NativePresentationStatus.Succeeded;
        public Release1NativePresentationStatus ClearDecisionStatus = Release1NativePresentationStatus.Succeeded;
        public Dictionary<string, Release1ObservedQuest?> ObservedQuests { get; } = new();
        public Release1NativePresentationStatus QuestReadStatus = Release1NativePresentationStatus.Succeeded;
        public Release1NativePresentationStatus ApplyQuestStatus = Release1NativePresentationStatus.Succeeded;
        public Action<Release1PresentationCommand>? LastOnChosen;

        private void MaybeThrow(string member)
        {
            if (ThrowFrom.Contains(member)) throw new InvalidOperationException($"synthetic {member} failure");
        }

        public Release1NativePresentationStatus TryEnsureContact()
        {
            Events.Add("EnsureContact");
            MaybeThrow(nameof(TryEnsureContact));
            return ContactStatus;
        }

        public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts)
        {
            Events.Add("ReadSentMessages");
            MaybeThrow(nameof(TryReadSentMessages));
            texts = SentMessages.ToArray();
            return ReadSentStatus;
        }

        public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision)
        {
            Events.Add("ReadDecision");
            MaybeThrow(nameof(TryReadDecision));
            decision = ObservedDecision;
            return DecisionReadStatus;
        }

        public Release1NativePresentationStatus TrySendMessage(string text)
        {
            Events.Add($"SendMessage:{text}");
            MaybeThrow(nameof(TrySendMessage));
            if (SendStatus == Release1NativePresentationStatus.Succeeded) SentMessages.Add(text);
            return SendStatus;
        }

        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
        {
            Events.Add($"SetDecision:{decision.Id}");
            MaybeThrow(nameof(TrySetDecision));
            if (SetDecisionStatus == Release1NativePresentationStatus.Succeeded)
            {
                var desiredLabels = decision.Options.Select(o => o.Label).ToArray();
                var existingLabels = ObservedDecision?.Labels ?? Array.Empty<string>();
                // Mirrors the real boundary: bind onto already-showing responses when the labels
                // already match (no send), otherwise send the prompt fresh, which is what actually
                // puts it into native message history.
                if (!Release1NativePresentationSupport.ShouldBindExistingResponses(existingLabels, desiredLabels))
                    SentMessages.Add(decision.Prompt);
                ObservedDecision = new Release1ObservedDecision(decision.Id, desiredLabels, decision.Prompt);
                LastOnChosen = onChosen;
            }
            return SetDecisionStatus;
        }

        public Release1NativePresentationStatus TryClearDecision()
        {
            Events.Add("ClearDecision");
            MaybeThrow(nameof(TryClearDecision));
            if (ClearDecisionStatus == Release1NativePresentationStatus.Succeeded) ObservedDecision = null;
            return ClearDecisionStatus;
        }

        public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest)
        {
            Events.Add($"ReadQuest:{key}");
            MaybeThrow(nameof(TryReadQuest));
            ObservedQuests.TryGetValue(key, out quest);
            return QuestReadStatus;
        }

        public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest)
        {
            Events.Add($"ApplyQuest:{quest.Key}");
            MaybeThrow(nameof(TryApplyQuest));
            if (ApplyQuestStatus == Release1NativePresentationStatus.Succeeded)
                ObservedQuests[quest.Key] = new Release1ObservedQuest(quest.Key, quest.Entries.Select(e => e.State).ToArray());
            return ApplyQuestStatus;
        }

        public Release1NativePresentationStatus TryEndQuest(string key)
        {
            Events.Add($"EndQuest:{key}");
            MaybeThrow(nameof(TryEndQuest));
            return Release1NativePresentationStatus.Succeeded;
        }

        public void OnPreLoad()
        {
            Events.Add("OnPreLoad");
            MaybeThrow(nameof(OnPreLoad));
        }

        public void OnSaveStart()
        {
            Events.Add("OnSaveStart");
            MaybeThrow(nameof(OnSaveStart));
        }
    }
}
