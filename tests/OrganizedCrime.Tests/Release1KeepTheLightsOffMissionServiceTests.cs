using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1KeepTheLightsOffMissionServiceTests
{
    [Fact]
    public void Offer_is_unavailable_until_short_notice_is_satisfied()
    {
        using var blocked = Release1KeepTheLightsOffHarness.ShortNoticeStillActive();
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Ineligible, blocked.Service.OfferStatus);

        using var ready = Release1KeepTheLightsOffHarness.Offered();
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Available, ready.Service.OfferStatus);
    }

    [Fact]
    public void Nell_does_not_offer_when_no_employee_is_assigned_at_any_owned_property()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, withEmployee: false);

        var review = harness.Service.TryReview();

        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ineligible, review.Status);
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Ineligible, harness.Service.OfferStatus);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public void The_offer_appears_on_the_next_pass_once_an_employee_is_assigned()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, withEmployee: false);
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ineligible, harness.Service.TryReview().Status);

        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 1, withEmployee: true);
        var review = harness.Service.TryReview();

        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, review.Status);
        Assert.Equal(1, review.Quote!.Assignment.ExpectedOwnedPropertyCount);
    }

    [Fact]
    public void A_convergence_pass_alone_pulls_the_offer_away_when_the_last_employee_is_unassigned()
    {
        // No TryReview or TryAccept call anywhere in this test: only Update(), the way a loaded
        // save's own per frame pass runs while the offer card is already on screen.
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Available, harness.Service.OfferStatus);

        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, withEmployee: false);
        harness.World.TotalMinutes = 6_001d;
        harness.Service.Update();

        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Ineligible, harness.Service.OfferStatus);
    }

    [Fact]
    public void A_convergence_pass_alone_publishes_the_offer_once_an_employee_is_assigned()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, withEmployee: false);
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Ineligible, harness.Service.OfferStatus);

        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 1, withEmployee: true);
        harness.World.TotalMinutes = 6_001d;
        harness.Service.Update();

        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Available, harness.Service.OfferStatus);
    }

    [Fact]
    public void The_offer_is_withheld_with_no_message_when_the_world_read_is_not_ready()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.ActivityStatus = Release1SmallCourtesyWorldReadStatus.Pending;

        var review = harness.Service.TryReview();

        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Pending, review.Status);
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Pending, harness.Service.OfferStatus);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public void Acceptance_freezes_the_owned_property_count_and_never_reads_a_product()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 2);
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(2, assignment.ExpectedOwnedPropertyCount);
        Assert.Equal(quote.Assignment, assignment);
        Assert.Equal(0, harness.World.ProductReads);
        Assert.Equal(0, harness.World.DeadDropReads);
    }

    [Fact]
    public void Accept_writes_the_assignment_and_the_seventy_two_hour_deadline_and_activates_the_mission()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(quote.Assignment, assignment);
        var mission = harness.Mission();
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("keep-the-lights-off-v1", mission.TermsVersion);
        Assert.Equal(100d, mission.AcceptedGameTimeHours);
        Assert.Equal(172d, mission.DeadlineGameTimeHours);
    }

    [Fact]
    public void Accept_after_the_owned_property_count_changed_reports_QuoteChanged_and_writes_nothing()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        // The owned property count changes after review; acceptance re-quotes against the live count,
        // so the quote on file no longer matches.
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 2);

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.QuoteChanged, decision.Status);
        Assert.Empty(harness.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
    }

    [Fact]
    public void Accept_during_a_save_reports_PersistenceDeferred_and_the_acceptance_rides_the_next_save()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;
        harness.Service.OnSaveStart();

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred, decision.Status);
        Assert.Empty(harness.Story.State!.KeepTheLightsOffAssignments);

        harness.Service.OnSaveComplete();
        var retried = harness.Service.TryAccept();

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, retried.Status);
        var assignment = Assert.Single(harness.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(quote.Assignment, assignment);
    }

    [Fact]
    public void Defer_records_MissionDeferred_and_a_later_load_re_offers_once_and_advances_the_attempt()
    {
        var repository = new Release1KeepTheLightsOffHarness.FakeRepository();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld();
        using (var first = Release1KeepTheLightsOffHarness.Offered(repository, world))
        {
            first.Service.TryReview();
            Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Deferred, first.Service.TryDefer().Status);
            Assert.Equal(Release1MissionState.Deferred, first.Mission().State);
            Assert.Equal(1, first.Mission().Attempt);
            first.Service.OnPreLoad();
        }

        using var restored = Release1KeepTheLightsOffHarness.Load(repository, world);
        Assert.Equal(Release1MissionState.Offered, restored.Mission().State);
        Assert.Equal(2, restored.Mission().Attempt);
        Assert.Equal(Release1KeepTheLightsOffOfferStatus.Available, restored.Service.OfferStatus);
    }

    [Fact]
    public void Make_good_and_recovery_each_freeze_the_live_owned_property_count_at_their_own_acceptance()
    {
        using var harness = Release1KeepTheLightsOffHarness.MakeGoodOffered();
        var primary = harness.Story.State!.KeepTheLightsOffAssignments.Single(a => a.Attempt == 1);
        Assert.Equal(1, primary.ExpectedOwnedPropertyCount);

        // A second property is bought before the make-good stage is accepted; its own assignment
        // freezes the live count at its own acceptance, never the primary stage's stale count.
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 2);
        var makeGoodQuote = harness.Service.TryReview().Quote!;

        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.MakeGood, makeGoodQuote.Assignment.Mode);
        Assert.Equal(2, makeGoodQuote.Assignment.ExpectedOwnedPropertyCount);
        Assert.Equal(2, makeGoodQuote.Assignment.Attempt);
        Assert.Equal(72d, makeGoodQuote.DeadlineDurationHours);
        Assert.NotEqual(primary.AuthorizationCorrelationId, makeGoodQuote.Assignment.AuthorizationCorrelationId);

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);

        harness.World.TotalMinutes = 400d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);

        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 3);
        var recoveryQuote = harness.Service.TryReview().Quote!;
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Recovery, recoveryQuote.Assignment.Mode);
        Assert.Equal(3, recoveryQuote.Assignment.ExpectedOwnedPropertyCount);
        Assert.Null(recoveryQuote.DeadlineDurationHours);
        // The earlier stages' own already-frozen assignments are untouched by the later stage's read.
        Assert.Equal(1, primary.ExpectedOwnedPropertyCount);
    }

    [Fact]
    public void Recovery_offers_the_mission_with_no_deadline()
    {
        using var harness = Release1KeepTheLightsOffHarness.RecoveryAvailable();

        var quote = harness.Service.TryReview().Quote!;
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Recovery, quote.Assignment.Mode);
        Assert.Null(quote.DeadlineDurationHours);

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(harness.Mission().DeadlineGameTimeHours);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
    }

    [Fact]
    public void The_window_lapsing_before_a_clear_is_ever_confirmed_records_RequiredFailure_once_and_costs_twelve_standing()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Single(harness.Mission().PenaltyReceiptIds);
    }

    [Fact]
    public void The_window_lapsing_in_the_make_good_records_MakeGoodFailed_once_and_costs_eight_standing()
    {
        using var harness = Release1KeepTheLightsOffHarness.MakeGoodActive();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 8, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
    }

    [Fact]
    public void The_deadline_still_fails_the_stage_without_a_reward_effect_guard()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, harness.Mission().LastOutcome);
        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        var receipt = Assert.Single(harness.Mission().PenaltyReceiptIds);
        Assert.Equal("keep-the-lights-off-deadline-primary-v1-a1", receipt);
    }

    [Fact]
    public void No_dead_drop_subscription_is_ever_created()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();

        harness.Service.Update();
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();
        harness.Service.Update();
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        harness.Service.OnPreLoad();

        Assert.False(harness.World.SubscribeCalled);
    }

    [Fact]
    public void A_notready_pass_right_after_a_reload_does_not_burn_the_baseline_and_the_next_pass_records_no_breach()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();

        // Reload #1: a baseline pass with a station already running opens the window without ever
        // treating that already-running station as a new operation (decision 8's baseline exemption).
        harness.Service.OnPreLoad();
        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "op-1", 10);
        harness.World.TotalMinutes = 6_100d;
        harness.Service.OnLoadComplete();
        Assert.Equal(6_100d, harness.Progress()?.ClearConfirmedAtGameMinutes);

        // Reload #2: the world is not ready on the very first pass after the boundary (as it would
        // not be for a frame or two while native objects settle), then comes back ready on the next
        // pass with the same station still running, unchanged.
        harness.Service.OnPreLoad();
        harness.World.ActivityStatus = Release1SmallCourtesyWorldReadStatus.Pending;
        harness.World.TotalMinutes = 6_101d;
        harness.Service.OnLoadComplete();

        harness.World.ActivityStatus = Release1SmallCourtesyWorldReadStatus.Ready;
        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "op-1", 15);
        harness.World.TotalMinutes = 6_102d;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Holding, status);
        Assert.Null(harness.Progress()?.BreachSincePassGameMinutes);
    }

    [Fact]
    public void A_migrated_active_attempt_restores_on_load_even_with_every_employee_unassigned()
    {
        // The v9 to v10 migration empties the assignment collection but leaves the mission Active
        // with its acceptance correlation intact. Decision 10's assigned-employee eligibility
        // precondition gates a new offer only; restoring this already-accepted attempt must not
        // depend on it, or a save with every employee unassigned could never restore a card or a
        // quest for it and would ride the deadline to RequiredFailure with nothing ever shown.
        using var harness = Release1KeepTheLightsOffHarness.MigratedActive();
        Assert.Empty(harness.Story.State!.KeepTheLightsOffAssignments);
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 1, withEmployee: false);

        harness.Service.OnLoadComplete();

        var assignment = Assert.Single(harness.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Primary, assignment.Mode);
        Assert.Equal(1, assignment.ExpectedOwnedPropertyCount);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void ExpectedAssignmentMode_maps_active_mission_states_to_their_assignment_mode()
    {
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Primary,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.Accepted));
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Primary,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.Active));
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.MakeGood,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.MakeGoodOffered));
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.MakeGood,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.MakeGoodActive));
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Recovery,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.RecoveryAvailable));
        Assert.Equal(Release1KeepTheLightsOffAssignmentMode.Recovery,
            Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.RecoveryActive));
        Assert.Null(Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.Offered));
        Assert.Null(Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.Satisfied));
        Assert.Null(Release1KeepTheLightsOffMissionService.ExpectedAssignmentMode(Release1MissionState.Locked));
    }
}

internal static class Release1KeepTheLightsOffHarness
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("88888888-8888-8888-8888-888888888888");

    internal static Harness ShortNoticeStillActive()
    {
        var repository = new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyShortNotice: false);
        var world = new FakeWorld();
        var service = new Release1KeepTheLightsOffMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, new List<string>(), phone: null, queue: new FakeQueue());
    }

    internal static Harness Offered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyShortNotice: true);
        world ??= new FakeWorld();
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1KeepTheLightsOffMissionService(story, world, phone, log: logs.Add);
        service.OnLoadComplete();
        repository.ResetEvidence();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    internal static Harness Load(FakeRepository repository, FakeWorld world)
    {
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var service = new Release1KeepTheLightsOffMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, new List<string>(), phone: null, queue: new FakeQueue());
    }

    /// <summary>
    /// Drives a real acceptance to Active, then routes the resulting state through the codec's v9
    /// read path to reproduce exactly what a save made before OC-65 landed looks like once loaded:
    /// the mission record, its attempt, and its accepted correlation intact, its assignment gone.
    /// Deliberately does not call OnLoadComplete: the caller configures the world (in particular,
    /// whether any employee is assigned anywhere) before triggering the load-time restore pass.
    /// </summary>
    internal static Harness MigratedActive()
    {
        Release1StoryState seedState;
        using (var seed = Active()) seedState = seed.Story.State!;

        Assert.True(Release1StorySaveCodec.TrySerialize(seedState, out var json, out var writeResult), writeResult.Message);
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 9;
        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var migrated, out var readResult), readResult.Message);
        Assert.Empty(migrated!.Story!.KeepTheLightsOffAssignments);

        var repository = new FakeRepository();
        repository.Update(migrated.Story);
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var world = new FakeWorld();
        var logs = new List<string>();
        var service = new Release1KeepTheLightsOffMissionService(story, world, log: logs.Add);
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static void Save(Harness harness)
    {
        harness.Story.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.World.MarkSaved();
    }

    internal static Harness Active(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Offered(repository, world, withPhone);
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
        harness.Repository.ResetEvidence();
        return harness;
    }

    internal static Harness MakeGoodOffered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Active(repository, world, withPhone);
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        harness.Repository.ResetEvidence();
        return harness;
    }

    internal static Harness MakeGoodActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodOffered(repository, world, withPhone);
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        return harness;
    }

    internal static Harness RecoveryAvailable(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodActive(repository, world, withPhone);
        harness.World.TotalMinutes = 400d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        harness.Repository.ResetEvidence();
        return harness;
    }

    internal static Harness RecoveryActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = RecoveryAvailable(repository, world, withPhone);
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        return harness;
    }

    // Drives a required-failure primary attempt through to the still-unaccepted make-good offer, with
    // the Nell accepted-message receipt already recorded so a single subsequent Update() can queue
    // Arthur's warning call without any further fixture setup.
    internal static Harness RequiredFailed(bool withPhone = false)
    {
        var harness = MakeGoodOffered(withPhone: withPhone);
        harness.RecordNellAcceptedReceipt();
        harness.Repository.ResetEvidence();
        return harness;
    }

    // Task 4 production-world knobs, shared by every suite that drives the shutdown census so the
    // whole test file speaks one vocabulary. SetQuietWorld and SetWorkingWorld cover the ordinary
    // "is anyone working" read; SetRunningStation drives the fingerprint-comparison rows (a station
    // already running, or one that starts a fresh operation) directly, since those need a named
    // operation identity SetWorkingWorld has no reason to expose.
    internal static void SetQuietWorld(Harness harness, int properties = 1, bool withEmployee = true) =>
        harness.World.Activity = ProductionWorld(properties, working: false, withEmployee: withEmployee);

    internal static void SetWorkingWorld(Harness harness, int properties = 1) =>
        harness.World.Activity = ProductionWorld(properties, working: true, withEmployee: true);

    internal static void SetRunningStation(Harness harness, string identity, int progress) =>
        harness.World.Activity = new Release1ProductionActivitySnapshot(new[]
        {
            new Release1ProductionPropertySnapshot("p0", "Property 0",
                new[] { Idle("npc-1") },
                new[] { new Release1ProductionStationSnapshot("s1",
                    Release1ProductionStationKind.ChemistryStation, true, identity, progress) })
        });

    internal static Release1ProductionActivitySnapshot ProductionWorld(int properties, bool working, bool withEmployee) =>
        new(Enumerable.Range(0, properties).Select(index => new Release1ProductionPropertySnapshot(
            $"p{index}", $"Property {index}",
            withEmployee
                ? new[] { working ? Working($"npc-{index}") : Idle($"npc-{index}") }
                : Array.Empty<Release1ProductionEmployeeSnapshot>(),
            Array.Empty<Release1ProductionStationSnapshot>())).ToArray());

    internal static Release1ProductionEmployeeSnapshot Idle(string id) =>
        new(id, "Employee", "Worker", false, true, false, 0, false, "Idle", "IdleBehaviour", true);

    internal static Release1ProductionEmployeeSnapshot Working(string id) =>
        new(id, "Employee", "Worker", false, true, false, 0, true, "Cook", "StartChemistryStationBehaviour", true);

    // Seeds a story whose Small Courtesy, Wrong Address, and Room With No Name missions (and, when
    // requested, Short Notice too) are Satisfied through the same pure Release1StoryTransitions path,
    // then loads it into a fresh runtime service backed by the given repository.
    private static Release1StoryRuntimeService SeededStory(FakeContext context, FakeRepository repository, bool satisfyShortNotice)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-keep-the-lights-off").Value);
        state = Satisfy(state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(state, Release1MissionCatalog.WrongAddress);
        state = Satisfy(state, Release1MissionCatalog.RoomWithNoName);
        if (satisfyShortNotice) state = Satisfy(state, Release1MissionCatalog.ShortNotice);
        repository.Update(state);
        return NewStory(context, repository);
    }

    private static Release1StoryState Satisfy(Release1StoryState story, string missionKey)
    {
        var index = Release1MissionCatalog.IndexOf(missionKey);
        var mission = story.Missions[index];
        var acceptReceipt = $"{missionKey}-accept";
        var accept = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted,
            acceptReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
            "terms-v1");
        var accepted = Release1StoryTransitions.Apply(story, accept);
        if (!accepted.Accepted) throw new InvalidOperationException($"Satisfy accept fixture setup failed for {missionKey}: {accepted.Message}");
        var activateReceipt = $"{missionKey}-activate";
        var activate = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated,
            activateReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
        var activated = Release1StoryTransitions.Apply(accepted.State!, activate);
        if (!activated.Accepted) throw new InvalidOperationException($"Satisfy activate fixture setup failed for {missionKey}: {activated.Message}");
        var completeReceipt = $"{missionKey}-complete";
        var complete = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted,
            completeReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
            CompletionTiming: Release1CompletionTiming.OnTime,
            RewardAuthorizationReceiptId: $"{missionKey}-reward");
        var completed = Release1StoryTransitions.Apply(activated.State!, complete);
        if (!completed.Accepted) throw new InvalidOperationException($"Satisfy complete fixture setup failed for {missionKey}: {completed.Message}");
        return completed.State!;
    }

    private static Release1StoryRuntimeService NewStory(FakeContext context, FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    internal sealed class Harness : IDisposable
    {
        public Harness(
            Release1StoryRuntimeService story,
            Release1KeepTheLightsOffMissionService service,
            FakeWorld world,
            FakeContext context,
            FakeRepository repository,
            List<string> logs,
            Release1PhoneCallService? phone,
            FakeQueue queue)
        {
            Story = story;
            Service = service;
            World = world;
            Context = context;
            Repository = repository;
            Logs = logs;
            Phone = phone;
            Queue = queue;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1KeepTheLightsOffMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public List<string> Logs { get; }
        public Release1PhoneCallService? Phone { get; }
        public FakeQueue Queue { get; }
        public Release1KeepTheLightsOffAssignment Assignment => Story.State!.KeepTheLightsOffAssignments.MaxBy(value => value.Attempt)!;

        public Release1MissionRecord Mission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

        public Release1KeepTheLightsOffProgress? Progress() =>
            Story.State?.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == Mission().Attempt);

        // Records the Nell accepted-message receipt Arthur's prerequisite requires, for the current
        // Keep the Lights Off mission attempt.
        public void RecordNellAcceptedReceipt()
        {
            var correlation = Release1LogicalCorrelation.Create(
                Context.Snapshot.PlayerId, Release1MissionCatalog.KeepTheLightsOff, Mission().Attempt,
                Release1TransitionKind.MissionAccepted, "presentation-nell-ktlo-accepted-v1").Value;
            var result = Story.TryRecordPresentationReceipt(correlation);
            if (!result.Accepted) throw new InvalidOperationException("Nell accepted-message receipt fixture setup failed: " + result.Message);
        }

        public void Dispose() { Service.Dispose(); Phone?.Dispose(); Story.Dispose(); }
    }

    // A phone-call queue test double that records every invocation attempt (including one that
    // throws) so tests can assert exactly-once delivery and failure-tolerance the same way the
    // production queue's caller (Release1PhoneCallService) observes it.
    internal sealed class FakeQueue : IRelease1PhoneCallQueue
    {
        public List<Release1PhoneCallRequest> Invocations { get; } = new();
        public bool Available { get; set; } = true;
        public bool ThrowOnInvoke { get; set; }
        public bool IsAvailable(Release1PhoneCallRequest request) => Available;

        public void Invoke(Release1PhoneCallRequest request)
        {
            Invocations.Add(request);
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic Keep the Lights Off phone queue failure");
        }
    }

    internal sealed class FakeCue : IRelease1PayphoneCue
    {
        public bool TryShow(string correlationId) => true;
        public void End(string correlationId) { }
        public void Reconcile(string correlationId) { }
        public void Dispose() { }
    }

    internal sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        private static readonly string[] DefaultDropGuids = { "drop-a", "drop-b", "drop-c", "drop-d" };

        public FakeWorld()
        {
            Context = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public double TotalMinutes { get; set; }
        public IReadOnlyList<Release1SmallCourtesyProductCandidate> Products { get; set; } =
            new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
        public IReadOnlyList<string> DropGuids { get; set; } = DefaultDropGuids;
        public float CashBalance { get; set; } = 500f;
        public bool SubscribeCalled { get; private set; }

        // Census-only knobs (Task 4). Defaults reproduce this fake's original always-empty,
        // always-unavailable behaviour exactly, so no test outside the census suite is affected.
        public Dictionary<string, IReadOnlyList<Release1SmallCourtesySlotSnapshot>> DropSlots { get; } = new(StringComparer.Ordinal);
        public Release1HoldRoomSnapshot HoldRoom { get; set; } = Release1HoldRoomSnapshot.Unavailable();
        public bool ThrowOnMutation { get; set; }

        // OC-63 read counters. Every world read except the canonical clock is counted here so the
        // convergence throttle can be pinned by observation rather than by timing. Nothing else
        // reads them, so no existing test is affected.
        public int ContextReads { get; private set; }
        public int ProductReads { get; private set; }
        public int DeadDropReads { get; private set; }
        public int DropSlotReads { get; private set; }
        public int HoldRoomReads { get; private set; }
        public int ProductionActivityReads { get; private set; }

        public int TotalWorldReadsExcludingClock =>
            ContextReads + ProductReads + DeadDropReads + DropSlotReads + HoldRoomReads + ProductionActivityReads;

        public void ResetReadCounters()
        {
            ContextReads = 0;
            ProductReads = 0;
            DeadDropReads = 0;
            DropSlotReads = 0;
            HoldRoomReads = 0;
            ProductionActivityReads = 0;
        }

        // OC-65 production census knobs. The default is one owned property with one working,
        // non-fired employee: an eligible world (Review/Accept succeed the way they always have,
        // mirroring how Products used to default to one discovered product) that also reads as
        // Working rather than Quiet, so a harness factory's own internal accept-time
        // ReconcileCensus pass never silently opens the shutdown window before a test gets to set
        // its own scenario up. A test that needs a different shape (no employee, a quiet world, a
        // running station) calls SetQuietWorld, SetWorkingWorld, or SetRunningStation explicitly.
        public Release1ProductionActivitySnapshot Activity { get; set; } =
            Release1KeepTheLightsOffHarness.ProductionWorld(properties: 1, working: true, withEmployee: true);
        public Release1SmallCourtesyWorldReadStatus ActivityStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;

        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            ProductionActivityReads++;
            activity = Activity;
            return ActivityStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            ContextReads++;
            context = Context;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = TotalMinutes;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            ProductReads++;
            products = Products;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            DeadDropReads++;
            drops = DropGuids.Select((guid, index) => new Release1SmallCourtesyDropCandidate(
                guid,
                $"Drop {index + 1}",
                "A vanilla dead drop.",
                index + 1,
                index + 2,
                index + 3,
                true)).ToArray();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind,
            out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            DropSlotReads++;
            slots = DropSlots.TryGetValue(deadDropGuid, out var configured) ? configured : Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            HoldRoomReads++;
            room = HoldRoom;
            return HoldRoom.Readiness == Release1HoldRoomReadiness.Ready
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Succeeded;

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            if (ThrowOnMutation) throw new InvalidOperationException("Keep the Lights Off never changes a dead drop slot's quantity.");
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            if (ThrowOnMutation) throw new InvalidOperationException("Keep the Lights Off never inserts packaged product at any drop.");
            reason = "not used by Keep the Lights Off, which selects nothing at any drop.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // Keep the Lights Off never chooses a location, so it never subscribes to a dead drop
        // closing; throwing here turns an accidental subscription into an immediate test failure.
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid,
            Action<string> callback,
            out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            SubscribeCalled = true;
            throw new InvalidOperationException("Keep the Lights Off never subscribes to a dead drop closing.");
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            if (ThrowOnCashReadAfterMutation && CashChanges > 0)
                throw new InvalidOperationException("synthetic post-payment read failure");
            if (_cashReadUnavailableArmed)
            {
                if (!_cashReadUnavailableSkippedOnce) _cashReadUnavailableSkippedOnce = true;
                else
                {
                    _cashReadUnavailableArmed = false;
                    _cashReadUnavailableSkippedOnce = false;
                    balance = 0f;
                    return Release1SmallCourtesyWorldReadStatus.Unavailable;
                }
            }
            if (_cashDriftArmed)
            {
                if (!_cashDriftSkippedOnce) _cashDriftSkippedOnce = true;
                else { CashBalance += _cashDriftOnSecondRead; _cashDriftArmed = false; _cashDriftSkippedOnce = false; }
            }
            balance = CashBalance;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            if (ThrowOnCashChange) throw new InvalidOperationException("synthetic cash mutation failure");
            CapturePreSaveCashSnapshotIfNeeded();
            CashBalance += amount;
            CashChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // Keep the Lights Off never debits the wallet either; this fake is not Chief-specific.
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }

        // Keep the Lights Off never reads or moves cash inside a dead drop slot; the mission pays
        // out of the player wallet only. Reporting Unavailable and Rejected keeps an accidental
        // call inert rather than silently succeeding.
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { reason = "this fake never despawns a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = "this fake never provokes a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { reason = "this fake never parks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { reason = "this fake never unparks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        // Task 5 completion knobs. Cash mutations are counted, and the balance a real native save
        // would durably capture is snapshotted lazily on the first mutation since the last
        // MarkSaved()/RevertToLastSave(), mirroring Release1RoomWithNoNameHarness.FakeWorld exactly:
        // RevertToLastSave() undoes only what this session's own unsaved mutations did, the same way
        // quitting without saving discards them.
        public int CashChanges { get; private set; }
        public bool ThrowOnCashChange { get; set; }
        public bool ThrowOnCashReadAfterMutation { get; set; }
        private float? _preSaveCashBalance;

        // Simulates an out-of-band cash change landing between the baseline read RunCompletion takes
        // and the verification read ReconcileReward makes right before it would attempt payment. The
        // first read after arming (the baseline) is left untouched; the second (the verification read)
        // gets the drift, exactly mirroring Release1RoomWithNoNameHarness.FakeWorld's
        // DriftCashAfterConsumption/_cashDriftArmed pair.
        private float _cashDriftOnSecondRead;
        private bool _cashDriftArmed;
        private bool _cashDriftSkippedOnce;

        public void ArmCashDriftBeforeVerification(float amount)
        {
            _cashDriftOnSecondRead = amount;
            _cashDriftArmed = true;
            _cashDriftSkippedOnce = false;
        }

        // Simulates the baseline read RunCompletion/TryPrepareCompletion takes succeeding, but the
        // verification read ReconcileReward makes on that very same pass (right after the Prepared
        // effect is created) coming back non-Ready: the first read after arming (the baseline) is
        // left untouched, the second (the verification read) reports Unavailable once and then the
        // knob disarms itself so every later read succeeds again, mirroring
        // ArmCashDriftBeforeVerification's skip-once pairing exactly.
        private bool _cashReadUnavailableArmed;
        private bool _cashReadUnavailableSkippedOnce;

        public void ArmCashReadUnavailableOnVerification()
        {
            _cashReadUnavailableArmed = true;
            _cashReadUnavailableSkippedOnce = false;
        }

        public void ResetMutationEvidence() => CashChanges = 0;

        private void CapturePreSaveCashSnapshotIfNeeded()
        {
            if (_preSaveCashBalance is not null) return;
            _preSaveCashBalance = CashBalance;
        }

        public void RevertToLastSave()
        {
            if (_preSaveCashBalance is not null) CashBalance = _preSaveCashBalance.Value;
            _preSaveCashBalance = null;
        }

        public void MarkSaved() => _preSaveCashBalance = null;
    }

    internal sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    internal sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public List<Release1StoryState> Updates { get; } = new();
        public bool FailNextUpdate { get; set; }

        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic save failure");
            }
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public void ResetEvidence() => Updates.Clear();
    }
}
