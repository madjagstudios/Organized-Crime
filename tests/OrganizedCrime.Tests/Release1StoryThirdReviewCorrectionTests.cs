using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryThirdReviewCorrectionTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("77777777-7777-7777-7777-777777777777");

    [Fact]
    public void Active_effect_is_superseded_by_completion_and_cannot_be_retrieved_or_committed()
    {
        var harness = ActiveHarness();
        var (mission, authorization) = PrepareActiveEffect(harness, "superseded");
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.True(harness.Service.TryGetExecutablePreparedEffect("superseded", out _));
        var completion = harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, "complete-superseded", timing: Release1CompletionTiming.OnTime, reward: "reward"));
        Assert.Equal(Release1StoryCommandStatus.Accepted, completion.Status);
        Assert.False(harness.Service.TryGetExecutablePreparedEffect("superseded", out _));
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryCommitNativeEffect("superseded", authorization).RejectReason);
    }

    [Fact]
    public void Superseded_prepared_effect_round_trips_through_the_real_store_but_all_mutations_reject()
    {
        using var harness = RealStoreHarness();
        var mission = harness.Service.State!.Missions[0];
        Execute(harness.Service, Command(mission, Release1TransitionKind.MissionAccepted, "accept-real", terms: "v1"));
        mission = harness.Service.State!.Missions[0];
        Execute(harness.Service, Command(mission, Release1TransitionKind.MissionActivated, "activate-real"));
        mission = harness.Service.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = new Release1NativeEffectJournalEntry("historical-prepared", mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State.Revision + 1, AuthorizedStoryCorrelationId: authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        Persist(harness.Service);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, "complete-real", timing: Release1CompletionTiming.OnTime, reward: "reward")).Status);
        Persist(harness.Service);

        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad(); restored.OnLoadComplete();
        var before = restored.State;
        Assert.Equal(Release1NativeEffectPhase.Prepared, before!.NativeEffects.Single().Phase);
        Assert.False(restored.TryGetExecutablePreparedEffect("historical-prepared", out _));
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, restored.TryRecordNativeEffectIssuance("historical-prepared", Release1NativeEffectIssuanceOutcome.Ambiguous).RejectReason);
        Assert.Equal(before, restored.State);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, restored.TryMarkNativeEffectApplied("historical-prepared", "native").RejectReason);
        Assert.Equal(before, restored.State);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, restored.TryCommitNativeEffect("historical-prepared", authorization).RejectReason);
        Assert.Equal(before, restored.State);
    }

    [Theory]
    [InlineData(Release1NativeEffectPhase.Applied)]
    [InlineData(Release1NativeEffectPhase.Committed)]
    public void Historical_applied_and_committed_effects_survive_later_progress_and_real_reload(Release1NativeEffectPhase finalPhase)
    {
        using var harness = RealStoreHarness();
        var mission = harness.Service.State!.Missions[0];
        Execute(harness.Service, Command(mission, Release1TransitionKind.MissionAccepted, $"accept-{finalPhase}", terms: "v1"));
        mission = harness.Service.State!.Missions[0];
        Execute(harness.Service, Command(mission, Release1TransitionKind.MissionActivated, $"activate-{finalPhase}"));
        mission = harness.Service.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = new Release1NativeEffectJournalEntry($"historical-{finalPhase}", mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State.Revision + 1, AuthorizedStoryCorrelationId: authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        Persist(harness.Service);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied(effect.EffectId, "native").Status);
        Persist(harness.Service);
        if (finalPhase == Release1NativeEffectPhase.Committed)
        {
            Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryCommitNativeEffect(effect.EffectId, authorization).Status);
            Persist(harness.Service);
        }
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, $"complete-{finalPhase}", timing: Release1CompletionTiming.OnTime, reward: "reward")).Status);
        Persist(harness.Service);

        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad(); restored.OnLoadComplete();
        Assert.Equal(finalPhase, restored.State!.NativeEffects.Single().Phase);
    }

    [Fact]
    public void Duplicate_prepare_with_changed_authorization_revision_is_a_conflict()
    {
        var authorization = Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionActivated, "activate-revision").Value;
        var command = new Release1NativeEffectCommand(Release1NativeEffectCommandKind.Prepare, "revision-conflict", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", StoryCorrelationId: authorization, AuthorizedMissionRevision: 4);
        var first = Release1NativeEffectJournal.Apply(Array.Empty<Release1NativeEffectJournalEntry>(), command, 7);
        var changed = Release1NativeEffectJournal.Apply(first.Effects, command with { AuthorizedMissionRevision = 5 }, 99);
        Assert.False(changed.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.DuplicateConflict, changed.RejectReason);
    }

    [Fact]
    public void Completion_reward_effect_remains_eligible_through_applied_and_committed_restart()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept-reward", terms: "v1"));
        mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate-reward"));
        mission = harness.Service.State!.Missions[0];
        var rewardEffect = new Release1NativeEffectJournalEntry("completion-reward", mission.MissionKey, mission.Attempt, "Reward", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        var completion = Execute(harness, Command(mission, Release1TransitionKind.MissionCompleted, "complete-reward", timing: Release1CompletionTiming.OnTime, reward: "reward", effect: rewardEffect));
        Assert.Equal(Release1StoryCommandStatus.Accepted, completion.Status);
        var authorization = completion.State!.Missions[0].AcceptedLogicalCorrelations.Last();
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.True(harness.Service.TryGetExecutablePreparedEffect("completion-reward", out _));
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("completion-reward", "native-reward").Status);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryCommitNativeEffect("completion-reward", authorization).Status);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad(); restored.OnLoadComplete();
        Assert.Equal(Release1NativeEffectPhase.Committed, restored.State!.NativeEffects.Single().Phase);
    }

    [Fact]
    public void Authoritative_reconciliation_clears_execution_blocked_and_survives_restart()
    {
        var harness = ActiveHarness();
        var (mission, authorization) = PrepareActiveEffect(harness, "blocked-reconcile");
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryRecordNativeEffectIssuance("blocked-reconcile", Release1NativeEffectIssuanceOutcome.Ambiguous).Status);
        Assert.True(harness.Service.State!.NativeEffects.Single().ExecutionBlocked);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("blocked-reconcile", "authoritative-native").Status);
        var applied = harness.Service.State!.NativeEffects.Single();
        Assert.Equal(Release1NativeEffectPhase.Applied, applied.Phase);
        Assert.False(applied.ExecutionBlocked);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad(); restored.OnLoadComplete();
        Assert.Equal(Release1NativeEffectPhase.Applied, restored.State!.NativeEffects.Single().Phase);
        Assert.Equal(Release1StoryCommandStatus.Accepted, restored.TryCommitNativeEffect("blocked-reconcile", authorization).Status);
    }

    [Fact]
    public void Penalty_receipt_reuse_is_rejected_without_mutation_and_model_rejects_durable_duplicates()
    {
        var state = ActiveState();
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RequiredFailure, "shared-penalty")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "make-good", terms: null)).State!;
        var before = state;
        var reused = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAbandoned, "shared-penalty"));
        Assert.False(reused.Accepted);
        Assert.Equal(Release1StoryTransitionRejectReason.DuplicateReceipt, reused.RejectReason);
        Assert.Equal(before, reused.State);
        var crossMission = before with
        {
            Missions = before.Missions.Select((mission, index) => index switch
            {
                0 => mission with { State = Release1MissionState.Satisfied, RewardAuthorizationReceiptId = "prior-reward" },
                1 => mission with { State = Release1MissionState.Active },
                _ => mission
            }).ToArray()
        };
        var crossMissionBefore = crossMission;
        var crossMissionReuse = Apply(crossMission, Command(crossMission.Missions[1], Release1TransitionKind.RequiredFailure, "shared-penalty"));
        Assert.False(crossMissionReuse.Accepted);
        Assert.Equal(Release1StoryTransitionRejectReason.DuplicateReceipt, crossMissionReuse.RejectReason);
        Assert.Equal(crossMissionBefore, crossMissionReuse.State);
        var duplicate = before.Missions.Select((mission, index) => mission with { PenaltyReceiptIds = index < 2 ? new[] { "durable-duplicate" } : mission.PenaltyReceiptIds }).ToArray();
        Assert.Throws<ArgumentException>(() => new Release1StoryState(before.PlayerId, before.Standing, before.RelationshipState, before.Release1Recognized, before.IntroLogicalCorrelationIds, before.RecognitionLogicalCorrelationIds, duplicate, before.NativeEffects, before.Revision));
    }

    [Fact]
    public void Required_failure_is_allowed_only_from_active_or_make_good_active()
    {
        foreach (var missionState in Enum.GetValues<Release1MissionState>())
        {
            var state = StateWithMissionState(missionState);
            var result = Apply(state, Command(state.Missions[0], Release1TransitionKind.RequiredFailure, $"required-{missionState}"));
            var expected = missionState is Release1MissionState.Active or Release1MissionState.MakeGoodActive;
            Assert.Equal(expected, result.Accepted);
            Assert.Equal(expected ? state.Revision + 1 : state.Revision, result.State!.Revision);
        }
    }

    [Theory]
    [InlineData(Release1NativeEffectPhase.Prepared)]
    [InlineData(Release1NativeEffectPhase.Applied)]
    [InlineData(Release1NativeEffectPhase.Committed)]
    public void Completion_rejects_an_existing_effect_id_without_mutation(Release1NativeEffectPhase phase)
    {
        var harness = ActiveHarness();
        var (mission, authorization) = PrepareActiveEffect(harness, $"existing-{phase}");
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        if (phase is Release1NativeEffectPhase.Applied or Release1NativeEffectPhase.Committed)
            Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied($"existing-{phase}", "native-existing").Status);
        if (phase == Release1NativeEffectPhase.Committed)
        {
            harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
            Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryCommitNativeEffect($"existing-{phase}", authorization).Status);
        }
        var before = harness.Service.State;
        var existing = before!.NativeEffects.Single();
        var result = harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, $"complete-existing-{phase}", timing: Release1CompletionTiming.OnTime, reward: "reward", effect: existing));
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(before, harness.Service.State);
    }

    [Fact]
    public void Completion_rejects_an_effect_id_already_referenced_by_the_mission()
    {
        var state = ActiveState();
        state = state with { Missions = state.Missions.Select((mission, index) => index == 0 ? mission with { NativeEffectIds = new[] { "orphan-effect" } } : mission).ToArray() };
        var before = state;
        var effect = new Release1NativeEffectJournalEntry("orphan-effect", state.Missions[0].MissionKey, state.Missions[0].Attempt, "Reward", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        var result = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionCompleted, "complete-orphan", timing: Release1CompletionTiming.OnTime, reward: "reward", effect: effect));
        Assert.False(result.Accepted);
        Assert.Equal(before, result.State);
    }

    private static (Release1MissionRecord Mission, string Authorization) PrepareActiveEffect(Harness harness, string id)
    {
        var mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, $"accept-{id}", terms: "v1"));
        mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, $"activate-{id}"));
        mission = harness.Service.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = new Release1NativeEffectJournalEntry(id, mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State!.Revision + 1, AuthorizedStoryCorrelationId: authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        return (mission, authorization);
    }

    private static Release1StoryState ActiveState()
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAccepted, "accept-state", terms: "v1")).State!;
        return Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionActivated, "activate-state")).State!;
    }
    private static Release1StoryState StateWithMissionState(Release1MissionState missionState)
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("state-intro"));
        var mission = state.Missions[0] with { State = missionState, RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "reward" : null, LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None };
        return state with { Missions = state.Missions.Select((current, index) => index == 0 ? mission : current).ToArray() };
    }
    private static Release1StoryRuntimeCommandResult Execute(Harness harness, Release1StoryCommand command) => harness.Service.TryExecute(command);
    private static Release1StoryRuntimeCommandResult Execute(Release1StoryRuntimeService service, Release1StoryCommand command) => service.TryExecute(command);
    private static Release1StoryTransitionResult Apply(Release1StoryState state, Release1StoryCommand command) => Release1StoryTransitions.Apply(state, command);
    private static void Persist(Release1StoryRuntimeService service)
    {
        Assert.Equal(Release1StoryRuntimeRejectReason.None, service.OnSaveStart().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, service.OnSaveComplete().RejectReason);
    }
    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, Release1NativeEffectJournalEntry? effect = null) => new(Session, 1, Player, mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, null, null, timing, reward, null, null, effect);
    private static string IntroCorrelation(string receipt) => Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
    private static Harness ActiveHarness()
    {
        var context = new FakeContext();
        var repository = new BoundRepository(context.Snapshot.ActiveSaveFolder);
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(new(Session, 1, Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro", IntroCorrelation("intro"))).Status);
        return new(service, context, repository);
    }
    private static RealHarness RealStoreHarness()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", "Release1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Assert.True(Release1StorySavePath.TryCreate(folder, out var path, out var pathResult), pathResult.Message);
        var repository = new Release1StoryStateStoreRepository(new Release1StoryStateStore(path!));
        var context = new FakeContext(folder);
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, service.TryExecute(new(Session, 1, Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-real", IntroCorrelation("intro-real"))).Status);
        return new(service, context, repository, folder);
    }
    private sealed record Harness(Release1StoryRuntimeService Service, FakeContext Context, BoundRepository Repository);
    private sealed class RealHarness : IDisposable
    {
        public RealHarness(Release1StoryRuntimeService service, FakeContext context, Release1StoryStateStoreRepository repository, string folder) { Service = service; Context = context; Repository = repository; Folder = folder; }
        public Release1StoryRuntimeService Service { get; }
        public FakeContext Context { get; }
        public Release1StoryStateStoreRepository Repository { get; }
        private string Folder { get; }
        public void Dispose() { Service.Dispose(); if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
    }
    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public FakeContext(string? saveFolder = null) => Snapshot = new(Session, 1, Player, Path.GetFullPath(saveFolder ?? Path.GetTempPath()));
        public Release1StoryHostContextSnapshot Snapshot { get; set; }
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Release1StoryHostContextReadStatus.Ready; }
    }
    private sealed class BoundRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public BoundRepository(string folder) => BoundSaveFolder = folder;
        public string BoundSaveFolder { get; }
        public Release1StoryState? StoredState { get; set; }
        public Release1StoryStoreLoadResult Load() => new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded, new Release1StorySaveEnvelope(1, StoredState), Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) { StoredState = state; return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(1, state), Release1StoryStoreFailureReason.None, string.Empty); }
    }
}
