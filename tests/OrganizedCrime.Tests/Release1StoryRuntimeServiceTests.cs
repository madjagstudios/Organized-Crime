using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryRuntimeServiceTests
{
    [Fact]
    public void Every_mutating_command_during_saving_is_deferred_without_mutation()
    {
        var harness = ActiveHarness();
        harness.Service.OnSaveStart();
        var before = harness.Service.State;
        var result = harness.Service.TryExecute(AcceptCurrentMission(harness));
        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, result.Status);
        Assert.Same(before, harness.Service.State);
        harness.Service.OnSaveComplete();
        Assert.Equal(before, harness.Repository.StoredState);
    }

    [Fact]
    public void Prepared_effect_is_executable_only_after_its_revision_is_persisted()
    {
        var harness = ActiveHarnessWithPreparedEffect();
        Assert.False(harness.Service.TryGetExecutablePreparedEffect("effect-1", out _));
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.True(harness.Service.TryGetExecutablePreparedEffect("effect-1", out var effect));
        Assert.Equal(Release1NativeEffectPhase.Prepared, effect.Phase);
    }

    [Fact]
    public void Immediate_persistence_defers_while_an_applied_effect_is_ahead_of_the_native_save()
    {
        var harness = ActiveHarnessWithPreparedEffect();
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var persistedPrepared = harness.Repository.StoredState;
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("effect-1", "native-receipt").Status);
        var applied = harness.Service.State;
        var nextMission = harness.Service.State!.Missions[1];

        var result = harness.Service.TryExecuteDurably(
            Command(nextMission, Release1TransitionKind.MissionAccepted, "next-mission-after-applied", "v1"));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, result.Status);
        Assert.Same(applied, harness.Service.State);
        Assert.Same(persistedPrepared, harness.Repository.StoredState);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[1].State);
        Assert.True(harness.Service.State!.Revision > harness.Service.LastPersistedRevision);
    }

    [Fact]
    public void Native_effect_phases_advance_only_across_their_native_save_boundaries()
    {
        var harness = ActiveHarnessWithPreparedEffect();
        var authorization = harness.Service.State!.Missions[0].AcceptedLogicalCorrelations.Last();

        Assert.False(harness.Service.TryGetExecutablePreparedEffect("effect-1", out _));
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.True(harness.Service.TryGetExecutablePreparedEffect("effect-1", out _));
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Repository.StoredState!.NativeEffects.Single().Phase);

        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryMarkNativeEffectApplied("effect-1", "native-receipt").Status);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Service.State!.NativeEffects.Single().Phase);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Repository.StoredState!.NativeEffects.Single().Phase);
        Assert.Equal(
            Release1StoryRuntimeRejectReason.InvalidTransition,
            harness.Service.TryCommitNativeEffect("effect-1", authorization).RejectReason);

        using (var restoredBeforeAppliedSave = new Release1StoryRuntimeService(harness.Context, harness.Repository))
        {
            restoredBeforeAppliedSave.OnPreLoad();
            restoredBeforeAppliedSave.OnLoadComplete();
            Assert.Equal(Release1NativeEffectPhase.Prepared, restoredBeforeAppliedSave.State!.NativeEffects.Single().Phase);
        }

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Repository.StoredState!.NativeEffects.Single().Phase);
        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryCommitNativeEffect("effect-1", authorization).Status);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Service.State!.NativeEffects.Single().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Repository.StoredState!.NativeEffects.Single().Phase);

        using (var restoredBeforeCommittedSave = new Release1StoryRuntimeService(harness.Context, harness.Repository))
        {
            restoredBeforeCommittedSave.OnPreLoad();
            restoredBeforeCommittedSave.OnLoadComplete();
            Assert.Equal(Release1NativeEffectPhase.Applied, restoredBeforeCommittedSave.State!.NativeEffects.Single().Phase);
        }

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        using var restoredCommitted = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restoredCommitted.OnPreLoad();
        restoredCommitted.OnLoadComplete();
        Assert.Equal(Release1NativeEffectPhase.Committed, restoredCommitted.State!.NativeEffects.Single().Phase);
    }

    [Fact]
    public void Pending_context_does_not_quarantine_and_ready_load_hydrates_once()
    {
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.Pending };
        var repository = new FakeRepository();
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);
        context.Status = Release1StoryHostContextReadStatus.Ready;
        service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Provisional_not_authoritative_load_does_not_quarantine_before_host_startup_settles()
    {
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.NotAuthoritative };
        var repository = new FakeRepository();
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();

        var provisional = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.NotAuthoritative, provisional.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);
        context.Status = Release1StoryHostContextReadStatus.Ready;
        service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Durable_intro_persists_the_accepted_revision_before_returning()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        var command = IntroCommand(context, Release1TransitionKind.IntroAccepted, "intro-durable");
        var result = service.TryExecuteDurably(command);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.NotNull(result.State);
        Assert.Same(result.State, repository.StoredState);
        Assert.Equal(result.State!.Revision, service.LastPersistedRevision);
        Assert.Equal(Release1RelationshipState.Accepted, repository.StoredState!.RelationshipState);
    }

    [Fact]
    public void Durable_intro_repository_failure_does_not_publish_an_unpersisted_state()
    {
        var context = new FakeContext();
        var repository = new FakeRepository { FailUpdates = true };
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        var result = service.TryExecuteDurably(IntroCommand(context, Release1TransitionKind.IntroAccepted, "intro-rejected"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Null(service.State);
        Assert.Null(repository.StoredState);
        Assert.Equal(-1, service.LastPersistedRevision);
    }

    [Fact]
    public void Existing_execute_contract_remains_memory_only_until_the_native_save_boundary()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        var result = service.TryExecute(IntroCommand(context, Release1TransitionKind.IntroAccepted, "intro-memory-only"));

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.NotNull(service.State);
        Assert.Null(repository.StoredState);
        Assert.Equal(-1, service.LastPersistedRevision);
    }

    [Fact]
    public void Wrong_address_progress_flags_are_memory_only_and_ride_the_next_save()
    {
        var repository = new FakeRepository();
        using var story = ActiveWrongAddressStory(repository, out var attempt);
        var persistedBefore = story.LastPersistedRevision;

        Assert.Equal(Release1StoryCommandStatus.Accepted, story.TrySetWrongAddressStaged(attempt).Status);
        Assert.Equal(persistedBefore, story.LastPersistedRevision);
        Assert.True(story.State!.WrongAddressProgress.Single().Staged);
        Assert.False(story.State.WrongAddressProgress.Single().Custody);
        Assert.Equal(Release1StoryCommandStatus.NoOp, story.TrySetWrongAddressStaged(attempt).Status);

        Assert.Equal(Release1StoryCommandStatus.Accepted, story.TrySetWrongAddressCustody(attempt).Status);
        Assert.True(story.State!.WrongAddressProgress.Single().Custody);

        story.OnSaveStart();
        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, story.TrySetWrongAddressStaged(attempt).Status);
        story.OnSaveComplete();
        Assert.True(repository.StoredState!.WrongAddressProgress.Single().Custody);
    }

    [Fact]
    public void Wrong_address_progress_reverts_with_an_unsaved_reload()
    {
        var repository = new FakeRepository();
        using (var first = ActiveWrongAddressStory(repository, out var attempt))
        {
            first.TrySetWrongAddressStaged(attempt);
            first.OnPreLoad();
        }

        using var restored = new Release1StoryRuntimeService(new FakeContext(), repository);
        restored.OnPreLoad();
        restored.OnLoadComplete();
        Assert.Empty(restored.State!.WrongAddressProgress);
    }

    [Fact]
    public void Revert_tolerant_marking_is_allowed_only_for_wrong_address_effects()
    {
        var repository = new FakeRepository();
        using var story = ActiveWrongAddressStory(repository, out var attempt);
        var effect = PrepareWrongAddressCargo(story, attempt);
        Assert.True(effect.PreparedStoryRevision > story.LastPersistedRevision);

        Assert.Equal(
            Release1StoryCommandStatus.Rejected,
            story.TryMarkNativeEffectApplied(effect.EffectId, "native-receipt").Status);
        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            story.TryMarkNativeEffectApplied(
                effect.EffectId, "native-receipt", Release1NativeEffectPersistenceMode.RevertTolerant).Status);
    }

    [Fact]
    public void Revert_tolerant_marking_refuses_a_small_courtesy_effect()
    {
        var repository = new FakeRepository();
        using var story = ActiveSmallCourtesyStory(repository, out var attempt);
        var effect = PrepareSmallCourtesyCargo(story, attempt);

        var result = story.TryMarkNativeEffectApplied(
            effect.EffectId, "native-receipt", Release1NativeEffectPersistenceMode.RevertTolerant);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Contains("revert-tolerant", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Release1StoryRuntimeService ActiveSmallCourtesyStory(FakeRepository repository, out int attempt)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        var introCorrelation = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-sc-fixture").Value;
        service.TryExecute(new(context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-sc-fixture", introCorrelation));

        var mission = service.State!.Missions[0];
        attempt = mission.Attempt;
        var acceptCorrelation = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, attempt, Release1TransitionKind.MissionAccepted, "accept-sc-fixture").Value;
        var assignment = new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy, attempt, Release1SmallCourtesyAssignmentMode.Primary, acceptCorrelation,
            "cocaine", "Cocaine", 500d, "brick", "Brick", "drop-a", "Drop A", "Behind the diner", 1, 2, 3, 1.25d);
        var command = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, attempt, Release1TransitionKind.MissionAccepted, "accept-sc-fixture",
            acceptCorrelation, "small-courtesy-v1");
        var result = service.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment);
        if (!result.Accepted) throw new InvalidOperationException("Small Courtesy fixture setup failed: " + result.Message);
        return service;
    }

    private static Release1StoryRuntimeService ActiveWrongAddressStory(FakeRepository repository, out int attempt)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        var introCorrelation = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-wa-fixture").Value;
        service.TryExecute(new(context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-wa-fixture", introCorrelation));

        var smallCourtesy = service.State!.Missions[0];
        var scAccept = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionAccepted, "accept-sc-for-wa").Value;
        var scAssignment = new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1SmallCourtesyAssignmentMode.Primary, scAccept,
            "cocaine", "Cocaine", 500d, "brick", "Brick", "drop-a", "Drop A", "Behind the diner", 1, 2, 3, 1.25d);
        var scCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionAccepted, "accept-sc-for-wa",
            scAccept, "small-courtesy-v1");
        var scResult = service.TryExecuteSmallCourtesyAcceptanceDurably(scCommand, scAssignment);
        if (!scResult.Accepted) throw new InvalidOperationException("Small Courtesy unlock fixture setup failed: " + scResult.Message);

        smallCourtesy = service.State!.Missions[0];
        var completeCorrelation = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, "complete-sc-for-wa").Value;
        var completeCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, "complete-sc-for-wa",
            completeCorrelation, CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: "reward-sc-for-wa");
        var completeResult = service.TryExecuteDurably(completeCommand);
        if (!completeResult.Accepted) throw new InvalidOperationException("Small Courtesy completion fixture setup failed: " + completeResult.Message);

        var wrongAddressIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var wrongAddress = service.State!.Missions[wrongAddressIndex];
        attempt = wrongAddress.Attempt;
        var waAccept = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.WrongAddress, attempt, Release1TransitionKind.MissionAccepted, "accept-wa-fixture").Value;
        var waAssignment = new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress, attempt, Release1WrongAddressAssignmentMode.Primary, waAccept,
            "cocaine", "Cocaine", "brick", "Brick", 1,
            "drop-source", "Drop Source", "Behind the diner", 1, 2, 3,
            "drop-handoff", "Drop Handoff", "Under the bench", 4, 5, 6,
            1.25d);
        var waCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.WrongAddress, attempt, Release1TransitionKind.MissionAccepted, "accept-wa-fixture",
            waAccept, "wrong-address-v1");
        var waResult = service.TryExecuteWrongAddressAcceptanceDurably(waCommand, waAssignment);
        if (!waResult.Accepted) throw new InvalidOperationException("Wrong Address fixture setup failed: " + waResult.Message);

        return service;
    }

    private static Release1NativeEffectJournalEntry PrepareSmallCourtesyCargo(Release1StoryRuntimeService story, int attempt)
    {
        var mission = story.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var revision = story.State!.Revision + 1;
        var effect = new Release1NativeEffectJournalEntry(
            $"effect-sc-{attempt}", Release1MissionCatalog.SmallCourtesy, attempt, "CargoTransfer",
            "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, revision,
            AuthorizedStoryCorrelationId: authorization);
        var result = story.TryPrepareNativeEffect(effect);
        if (!result.Accepted) throw new InvalidOperationException("Small Courtesy cargo fixture setup failed: " + result.Message);
        return result.State!.NativeEffects.Single(e => e.EffectId == effect.EffectId);
    }

    private static Release1NativeEffectJournalEntry PrepareWrongAddressCargo(Release1StoryRuntimeService story, int attempt)
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var mission = story.State!.Missions[index];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var revision = story.State!.Revision + 1;
        var effect = new Release1NativeEffectJournalEntry(
            $"effect-wa-{attempt}", Release1MissionCatalog.WrongAddress, attempt, "CargoTransfer",
            "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, revision,
            AuthorizedStoryCorrelationId: authorization);
        var result = story.TryPrepareNativeEffect(effect);
        if (!result.Accepted) throw new InvalidOperationException("Wrong Address cargo fixture setup failed: " + result.Message);
        return result.State!.NativeEffects.Single(e => e.EffectId == effect.EffectId);
    }

    private static Harness ActiveHarness()
    {
        var context = new FakeContext(); var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        var intro = "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-runtime";
        service.TryExecute(new(context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-runtime", intro));
        return new Harness(service, context, repository);
    }

    private static Harness ActiveHarnessWithPreparedEffect()
    {
        var h = ActiveHarness();
        var state = h.Service.State!;
        var mission = state.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, h.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept-effect", "v1")).Status);
        mission = h.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, h.Service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate-effect")).Status);
        mission = h.Service.State!.Missions[0];
        var effect = new Release1NativeEffectJournalEntry("effect-1", Release1MissionCatalog.SmallCourtesy, mission.Attempt, "Reward", "oc", "native", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        Assert.Equal(Release1StoryCommandStatus.Accepted, h.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, "complete-effect", timing: Release1CompletionTiming.OnTime, reward: "reward-effect", effect: effect)).Status);
        return h;
    }

    private static Release1StoryCommand AcceptCurrentMission(Harness h)
    {
        var mission = h.Service.State!.Missions[0];
        return new(h.Context.Snapshot.SessionEpoch, h.Context.Snapshot.LoadEpoch, h.Context.Snapshot.PlayerId, mission.MissionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, "accept-runtime", Release1LogicalCorrelation.Create(h.Context.Snapshot.PlayerId, mission.MissionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, "accept-runtime").Value, "v1");
    }

    private static Release1StoryCommand IntroCommand(FakeContext context, Release1TransitionKind kind, string receipt) => new(
        context.Snapshot.SessionEpoch,
        context.Snapshot.LoadEpoch,
        context.Snapshot.PlayerId,
        Release1MissionCatalog.IntroScopeKey,
        0,
        kind,
        receipt,
        Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, kind, receipt).Value);

    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, Release1NativeEffectJournalEntry? effect = null) =>
        new(SessionEpoch: hSession(mission), LoadEpoch: 1, PlayerId: "76561190000000001", MissionKey: mission.MissionKey, Attempt: mission.Attempt, TransitionKind: kind, ReceiptId: receipt, CorrelationId: Release1LogicalCorrelation.Create("76561190000000001", mission.MissionKey, mission.Attempt, kind, receipt).Value, TermsVersion: terms, CompletionTiming: timing, RewardAuthorizationReceiptId: reward, PreparedNativeEffect: effect);

    private static Guid hSession(Release1MissionRecord _) => Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed record Harness(Release1StoryRuntimeService Service, FakeContext Context, FakeRepository Repository);
    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, "76561190000000001", System.IO.Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Status; }
    }
    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Release1StoryState? StoredState { get; set; }
        public bool FailUpdates { get; set; }
        public Release1StoryStoreLoadResult Load() => new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState), Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            if (FailUpdates)
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            StoredState = state;
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }
    }
}
