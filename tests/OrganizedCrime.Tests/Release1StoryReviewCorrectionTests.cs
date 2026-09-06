using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryReviewCorrectionTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Satisfied_mission_cannot_be_reoffered_or_double_rewarded()
    {
        var state = ActiveFirstMission();
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionCompleted, "complete", timing: Release1CompletionTiming.OnTime, reward: "reward")).State!;
        var revision = state.Revision;
        var reopen = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionReoffered, "reopen"));
        Assert.False(reopen.Accepted);
        Assert.Equal(Release1MissionState.Satisfied, reopen.State!.Missions[0].State);
        Assert.Equal(revision, reopen.State.Revision);
        var duplicate = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionCompleted, "complete", timing: Release1CompletionTiming.OnTime, reward: "reward"));
        Assert.True(duplicate.Idempotent);
        Assert.Equal(1, duplicate.State!.Missions.Count(m => m.RewardAuthorizationReceiptId is not null));
    }

    [Fact]
    public void Accepted_abandonment_is_penalized_once_and_make_good_operations_are_noop_stable()
    {
        var state = ActiveFirstMission();
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAbandoned, "abandon")).State!;
        Assert.Equal(10, state.Missions[0].StandingPenaltyApplied);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodOffered, "offer")).State!;
        var revision = state.Revision;
        var repeatOffer = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodOffered, "offer-again"));
        Assert.True(repeatOffer.Idempotent);
        Assert.Equal(revision, repeatOffer.State!.Revision);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "accept-good")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodFailed, "fail-good")).State!;
        var second = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "accept-second-good"));
        Assert.False(second.Accepted);
        Assert.Equal(Release1MissionState.RecoveryAvailable, second.State!.Missions[0].State);
    }

    [Fact]
    public void Runtime_rejects_command_epoch_and_identity_mismatches_without_mutation()
    {
        var harness = ActiveHarness();
        var before = harness.Service.State!;
        var command = IntroCommand() with { SessionEpoch = Guid.NewGuid() };
        var result = harness.Service.TryExecute(command);
        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.WrongEpoch, result.RejectReason);
        Assert.Equal(before, harness.Service.State);
        command = IntroCommand() with { LoadEpoch = 999 };
        result = harness.Service.TryExecute(command);
        Assert.Equal(Release1StoryRuntimeRejectReason.WrongEpoch, result.RejectReason);
        command = IntroCommand() with { PlayerId = "76561197984645370" };
        result = harness.Service.TryExecute(command);
        Assert.Equal(Release1StoryRuntimeRejectReason.IdentityMismatch, result.RejectReason);
    }

    [Fact]
    public void PreLoad_during_saving_cancels_old_snapshot_and_loads_new_slot_once()
    {
        var harness = ActiveHarness();
        harness.Service.OnSaveStart();
        harness.Service.OnPreLoad();
        harness.Service.OnSaveComplete();
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, harness.Service.Phase);
        Assert.Null(harness.Repository.StoredState);
        harness.Service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Active, harness.Service.Phase);
        var after = harness.Service.State;
        harness.Service.OnLoadComplete();
        Assert.Same(after, harness.Service.State);
    }

    [Fact]
    public void Runtime_rejects_a_repository_bound_to_another_save_folder()
    {
        var context = new FakeContext(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "slot-a"));
        var repository = new BoundRepository(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "slot-b"));
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryPathMismatch, service.LastLifecycleRejectReason);
    }

    [Fact]
    public void Applied_and_committed_effects_survive_save_restart_and_commit_requires_accepted_correlation()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1")).Status);
        mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate")).Status);
        mission = harness.Service.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = new Release1NativeEffectJournalEntry("effect-1", mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State.Revision + 1, AuthorizedStoryCorrelationId: authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryMarkNativeEffectApplied("effect-1", "native-receipt").RejectReason);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("effect-1", "native-receipt").Status);
        var wrongCorrelation = Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, "not-accepted").Value;
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryCommitNativeEffect("effect-1", wrongCorrelation).RejectReason);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryCommitNativeEffect("effect-1", authorization).Status);
        harness.Service.OnSaveStart(); harness.Service.OnSaveComplete();
        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad(); restored.OnLoadComplete();
        Assert.Equal(Release1NativeEffectPhase.Committed, restored.State!.NativeEffects.Single().Phase);
    }

    [Fact]
    public void Ambiguous_effect_issuance_is_not_executable_or_replayable()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept-ambiguous", terms: "v1")).Status);
        mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate-ambiguous")).Status);
        mission = harness.Service.State!.Missions[0];
        var effect = new Release1NativeEffectJournalEntry("effect-ambiguous", mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State!.Revision + 1, AuthorizedStoryCorrelationId: mission.AcceptedLogicalCorrelations.Last());
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryRecordNativeEffectIssuance("effect-ambiguous", Release1NativeEffectIssuanceOutcome.Ambiguous).Status);
        Assert.False(harness.Service.TryGetExecutablePreparedEffect("effect-ambiguous", out _));
    }

    [Fact]
    public void Codec_requires_phase_revision_and_rejects_undefined_enum_values()
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        state = state with
        {
            Missions = state.Missions.Select(m => m.MissionKey == Release1MissionCatalog.SmallCourtesy ? m with { State = Release1MissionState.Accepted, TermsVersion = "v1", NativeEffectIds = new[] { "effect-schema" }, AcceptedLogicalCorrelations = new[] { Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "effect-schema-authorize").Value } } : m).ToArray(),
            NativeEffects = new[] { new Release1NativeEffectJournalEntry("effect-schema", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, 1, AuthorizedStoryCorrelationId: Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "effect-schema-authorize").Value, AuthorizedMissionRevision: 0) },
            Revision = 1
        };
        Assert.True(Release1StorySaveCodec.TrySerialize(state, out var json, out _));
        var withoutPhase = json.Replace("\"phase\": \"Prepared\",", string.Empty, StringComparison.Ordinal);
        Assert.False(Release1StorySaveCodec.TryDeserialize(withoutPhase, out _, out _));
        var undefined = json.Replace("\"phase\": \"Prepared\"", "\"phase\": 99", StringComparison.Ordinal);
        Assert.False(Release1StorySaveCodec.TryDeserialize(undefined, out _, out _));
    }

    [Fact]
    public void Persisted_correlations_must_match_player_and_containing_mission()
    {
        var valid = IntroCorrelation("intro");
        Assert.Throws<ArgumentException>(() => new Release1StoryState(Player, 20, Release1RelationshipState.Accepted, false, new[] { valid }, Array.Empty<string>(),
            Release1MissionCatalog.All.Select((m, i) => new Release1MissionRecord(m.MissionKey, i == 0 ? Release1MissionState.Offered : Release1MissionState.Locked, 1, null, null, null, Release1MissionOutcome.None, 0, Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false, Array.Empty<string>(), Array.Empty<string>(), new[] { Release1LogicalCorrelation.Create("76561197984645370", m.MissionKey, 1, Release1TransitionKind.MissionAccepted, "wrong").Value }, null, 0)).ToArray(), Array.Empty<Release1NativeEffectJournalEntry>(), 0));
    }

    [Fact]
    public void File_system_boundary_failures_are_typed_and_do_not_escape()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Release1Review", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.True(Release1StorySavePath.TryCreate(folder, out var path, out _));
            var store = new Release1StoryStateStore(path!, new ThrowingFileSystem());
            Assert.False(store.TryLoad(out var result));
            Assert.Equal(Release1StoryStoreFailureReason.SidecarReadFailed, result.FailureReason);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Exposed_story_snapshots_are_not_castable_mutable_arrays()
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        Assert.False(state.Missions is Release1MissionRecord[]);
        Assert.False(state.IntroLogicalCorrelationIds is string[]);
    }

    [Fact]
    public void Committed_effect_rejects_changed_receipt_or_story_correlation()
    {
        var authorization = Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionCompleted, "complete").Value;
        var prepare = new Release1NativeEffectCommand(Release1NativeEffectCommandKind.Prepare, "effect-commit", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", StoryCorrelationId: authorization);
        var prepared = Release1NativeEffectJournal.Apply(Array.Empty<Release1NativeEffectJournalEntry>(), prepare, 1);
        var applied = Release1NativeEffectJournal.Apply(prepared.Effects, prepare with { Kind = Release1NativeEffectCommandKind.MarkApplied, NativeReceiptId = "native" }, 2);
        var correlation = authorization;
        var committed = Release1NativeEffectJournal.Apply(applied.Effects, prepare with { Kind = Release1NativeEffectCommandKind.Commit, NativeReceiptId = "native", StoryCorrelationId = correlation }, 3);
        var changedCorrelation = Release1NativeEffectJournal.Apply(committed.Effects, prepare with { Kind = Release1NativeEffectCommandKind.Commit, NativeReceiptId = "native", StoryCorrelationId = Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionCompleted, "changed").Value }, 4);
        Assert.False(changedCorrelation.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.DuplicateConflict, changedCorrelation.RejectReason);
        var changedReceipt = Release1NativeEffectJournal.Apply(committed.Effects, prepare with { Kind = Release1NativeEffectCommandKind.Commit, NativeReceiptId = "other", StoryCorrelationId = correlation }, 4);
        Assert.False(changedReceipt.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.DuplicateConflict, changedReceipt.RejectReason);
    }

    [Fact]
    public void Prepared_effect_is_linked_and_only_prepared_input_can_be_issued()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept-link", terms: "v1")).Status);
        mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate-link")).Status);
        mission = harness.Service.State!.Missions[0];
        var effect = new Release1NativeEffectJournalEntry("effect-link", mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, harness.Service.State!.Revision + 1, AuthorizedStoryCorrelationId: mission.AcceptedLogicalCorrelations.Last());
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        Assert.Contains("effect-link", harness.Service.State!.Missions[0].NativeEffectIds);
        var applied = effect with { Phase = Release1NativeEffectPhase.Applied, NativeReceiptId = "native" };
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryPrepareNativeEffect(applied).RejectReason);
    }

    [Fact]
    public void Invalid_make_good_and_recovery_edges_are_rejected()
    {
        var state = ActiveFirstMission();
        var invalidDefer = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodDeferred, "defer-invalid"));
        Assert.False(invalidDefer.Accepted);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAbandoned, "abandon-edge")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodOffered, "offer-edge")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "accept-edge")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodFailed, "fail-edge")).State!;
        var recoveryWithDeadline = Apply(state, Command(state.Missions[0], Release1TransitionKind.RecoveryAccepted, "recover-edge", deadline: 10));
        Assert.False(recoveryWithDeadline.Accepted);
        Assert.Equal(Release1StoryTransitionRejectReason.InvalidTime, recoveryWithDeadline.RejectReason);
    }

    [Fact]
    public void Later_mission_reoffer_requires_prior_mission_satisfaction()
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        state = state with { Missions = state.Missions.Select((m, i) => i == 1 ? m with { State = Release1MissionState.Deferred } : m).ToArray() };
        var result = Apply(state, Command(state.Missions[1], Release1TransitionKind.MissionReoffered, "reoffer-later"));
        Assert.False(result.Accepted);
        Assert.Equal(Release1StoryTransitionRejectReason.WrongState, result.RejectReason);
    }

    private static Release1StoryState ActiveFirstMission()
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAccepted, "accept", terms: "v1")).State!;
        return Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionActivated, "activate")).State!;
    }
    private static Release1StoryTransitionResult Apply(Release1StoryState state, Release1StoryCommand command) => Release1StoryTransitions.Apply(state, command);
    private static string IntroCorrelation(string receipt) => Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
    private static Release1StoryCommand IntroCommand() => new(Session, 1, Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "new-intro", IntroCorrelation("new-intro"));
    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, double? deadline = null) => new(Session, 1, Player, mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, null, deadline, timing, reward);

    private static Harness ActiveHarness()
    {
        var context = new FakeContext(System.IO.Path.GetTempPath());
        var repository = new BoundRepository(context.Snapshot.ActiveSaveFolder);
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad(); service.OnLoadComplete();
        service.TryExecute(new(Session, 1, Player, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro", IntroCorrelation("intro")));
        return new Harness(service, context, repository);
    }

    private sealed record Harness(Release1StoryRuntimeService Service, FakeContext Context, BoundRepository Repository);
    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public FakeContext(string folder) => Snapshot = new(Session, 1, Player, folder);
        public Release1StoryHostContextSnapshot Snapshot { get; }
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
    private sealed class ThrowingFileSystem : IRelease1StoryFileSystem
    {
        public bool FileExists(string path) => throw new IOException("exists failed");
        public string ReadAllText(string path) => throw new IOException();
        public void CreateDirectory(string path) => throw new IOException();
        public string CreateTemporaryPath(string directory, string targetPath) => throw new IOException();
        public void WriteAllTextAndFlush(string path, string contents) => throw new IOException();
        public void ReplaceAtomically(string temporaryPath, string targetPath) => throw new IOException();
        public void DeleteFile(string path) => throw new IOException();
    }
}
