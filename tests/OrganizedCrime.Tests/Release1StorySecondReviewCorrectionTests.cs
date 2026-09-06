using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StorySecondReviewCorrectionTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Theory]
    [InlineData(Release1TransitionKind.RequiredFailure)]
    [InlineData(Release1TransitionKind.MissionAbandoned)]
    [InlineData(Release1TransitionKind.MakeGoodFailed)]
    public void Failure_from_make_good_active_consumes_the_one_make_good(Release1TransitionKind failureKind)
    {
        var state = MakeGoodActive();
        var result = Apply(state, Command(state.Missions[0], failureKind, "bounded-failure"));
        Assert.True(result.Accepted, result.Message);
        Assert.Equal(Release1MissionState.RecoveryAvailable, result.State!.Missions[0].State);
        Assert.Equal(1, result.State.Missions[0].MakeGoodFailures);
        var retry = Apply(result.State, Command(result.State.Missions[0], Release1TransitionKind.MakeGoodAccepted, "retry-good"));
        Assert.False(retry.Accepted);
        Assert.Equal(Release1MissionState.RecoveryAvailable, retry.State!.Missions[0].State);
    }

    [Fact]
    public void Every_failure_command_has_an_exact_source_state_allowlist()
    {
        var states = Enum.GetValues<Release1MissionState>();
        foreach (var missionState in states)
        {
            var state = StateWithMissionState(missionState);
            foreach (var kind in new[] { Release1TransitionKind.MissionAbandoned, Release1TransitionKind.RequiredFailure, Release1TransitionKind.MakeGoodFailed })
            {
                var before = state;
                var result = Apply(before, Command(before.Missions[0], kind, $"{missionState}-{kind}"));
                var expected = kind switch
                {
                    Release1TransitionKind.MissionAbandoned => missionState is Release1MissionState.Accepted or Release1MissionState.Active or Release1MissionState.MakeGoodActive,
                    Release1TransitionKind.RequiredFailure => missionState is Release1MissionState.Active or Release1MissionState.MakeGoodActive,
                    _ => missionState == Release1MissionState.MakeGoodActive
                };
                Assert.Equal(expected, result.Accepted);
                Assert.Equal(result.Accepted && result.Changed ? before.Revision + 1 : before.Revision, result.State!.Revision);
            }
        }
    }

    [Fact]
    public void Native_apply_requires_a_durable_prepared_snapshot()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        var accepted = Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1"));
        mission = accepted.State!.Missions[0];
        var activated = Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate"));
        mission = activated.State!.Missions[0];
        var authorization = activated.State.Missions[0].AcceptedLogicalCorrelations.Last();
        var effect = Effect("durability", mission, harness.Service.State!.Revision + 1, authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        var before = harness.Service.State;
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryMarkNativeEffectApplied("durability", "native").RejectReason);
        Assert.Equal(before, harness.Service.State);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, harness.Service.OnSaveStart().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, harness.Service.OnSaveComplete().RejectReason);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("durability", "native").Status);
    }

    [Fact]
    public void Failed_save_quarantines_the_runtime_and_blocks_native_application()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept-failed-save", terms: "v1"));
        mission = harness.Service.State!.Missions[0];
        Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate-failed-save"));
        mission = harness.Service.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = Effect("failed-save", mission, harness.Service.State!.Revision + 1, authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        harness.Repository.FailUpdates = true;
        harness.Service.OnSaveStart();
        Assert.Equal(Release1StoryRuntimeRejectReason.SidecarSaveFailed, harness.Service.OnSaveComplete().RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, harness.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.Quarantined, harness.Service.TryMarkNativeEffectApplied("failed-save", "native").RejectReason);
    }

    [Fact]
    public void Effect_apis_revalidate_live_authority_before_each_journal_phase()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        var accepted = Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1"));
        mission = accepted.State!.Missions[0];
        var activated = Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate"));
        mission = activated.State!.Missions[0];
        var authorization = activated.State.Missions[0].AcceptedLogicalCorrelations.Last();
        var effect = Effect("authority", mission, harness.Service.State!.Revision + 1, authorization);
        var normalizedFolder = harness.Context.Snapshot.ActiveSaveFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        harness.Context.Snapshot = harness.Context.Snapshot with { ActiveSaveFolder = normalizedFolder.ToUpperInvariant() };
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        var before = harness.Service.State;
        harness.Context.Status = Release1StoryHostContextReadStatus.Pending;
        Assert.Equal(Release1StoryRuntimeRejectReason.ContextPending, harness.Service.TryMarkNativeEffectApplied("authority", "native").RejectReason);
        Assert.Equal(before, harness.Service.State);
        Assert.False(harness.Service.TryGetExecutablePreparedEffect("authority", out _));
        harness.Context.Status = Release1StoryHostContextReadStatus.Ready;
        harness.Context.Snapshot = harness.Context.Snapshot with { LoadEpoch = 2 };
        Assert.Equal(Release1StoryRuntimeRejectReason.WrongEpoch, harness.Service.TryMarkNativeEffectApplied("authority", "native").RejectReason);
        Assert.Equal(before, harness.Service.State);
    }

    [Fact]
    public void Post_completion_or_wrong_authorization_cannot_mint_a_native_effect()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        var accepted = Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1"));
        mission = accepted.State!.Missions[0];
        var activated = Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate"));
        mission = activated.State!.Missions[0];
        var wrong = Effect("wrong", mission, harness.Service.State!.Revision + 1,
            Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, Release1TransitionKind.MissionDeferred, "not-accepted").Value);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryPrepareNativeEffect(wrong).RejectReason);
        var completion = Execute(harness, Command(mission, Release1TransitionKind.MissionCompleted, "complete", timing: Release1CompletionTiming.OnTime, reward: "reward", effect: null));
        mission = completion.State!.Missions[0];
        var postCompletion = Effect("post-completion", mission, completion.State.Revision + 1, mission.AcceptedLogicalCorrelations.Last());
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, harness.Service.TryPrepareNativeEffect(postCompletion).RejectReason);
    }

    [Fact]
    public void Duplicate_prepare_preserves_revision_and_rejects_changed_authorization()
    {
        var authorization = Release1LogicalCorrelation.Create(Player, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionActivated, "activate").Value;
        var command = new Release1NativeEffectCommand(Release1NativeEffectCommandKind.Prepare, "duplicate", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", StoryCorrelationId: authorization);
        var first = Release1NativeEffectJournal.Apply(Array.Empty<Release1NativeEffectJournalEntry>(), command, 7);
        var duplicate = Release1NativeEffectJournal.Apply(first.Effects, command, 99);
        Assert.True(duplicate.Idempotent);
        Assert.Equal(first.Effects.Single().PreparedStoryRevision, duplicate.Effects.Single().PreparedStoryRevision);
        var changed = Release1NativeEffectJournal.Apply(first.Effects, command with { StoryCorrelationId = authorization + "-changed" }, 99);
        Assert.False(changed.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.DuplicateConflict, changed.RejectReason);
    }

    [Fact]
    public void Ambiguous_recording_mutates_once_and_repeated_recording_is_a_noop()
    {
        var harness = ActiveHarness();
        var mission = harness.Service.State!.Missions[0];
        var accepted = Execute(harness, Command(mission, Release1TransitionKind.MissionAccepted, "accept", terms: "v1"));
        mission = accepted.State!.Missions[0];
        var activated = Execute(harness, Command(mission, Release1TransitionKind.MissionActivated, "activate-ambiguous-once"));
        mission = activated.State!.Missions[0];
        var authorization = mission.AcceptedLogicalCorrelations.Last();
        var effect = Effect("ambiguous-once", mission, harness.Service.State!.Revision + 1, authorization);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryPrepareNativeEffect(effect).Status);
        var first = harness.Service.TryRecordNativeEffectIssuance("ambiguous-once", Release1NativeEffectIssuanceOutcome.Ambiguous);
        var revision = harness.Service.State!.Revision;
        Assert.Equal(Release1StoryCommandStatus.Accepted, first.Status);
        Assert.True(harness.Service.State.NativeEffects.Single().ExecutionBlocked);
        var second = harness.Service.TryRecordNativeEffectIssuance("ambiguous-once", Release1NativeEffectIssuanceOutcome.Ambiguous);
        Assert.Equal(Release1StoryCommandStatus.NoOp, second.Status);
        Assert.Equal(revision, harness.Service.State!.Revision);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, harness.Service.OnSaveStart().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, harness.Service.OnSaveComplete().RejectReason);
    }

    [Fact]
    public void Undefined_effect_command_kind_and_phase_are_rejected_by_the_model()
    {
        var invalid = new Release1NativeEffectCommand((Release1NativeEffectCommandKind)99, "invalid", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo");
        var result = Release1NativeEffectJournal.Apply(Array.Empty<Release1NativeEffectJournalEntry>(), invalid, 1);
        Assert.False(result.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.InvalidCommand, result.RejectReason);
        Assert.Throws<ArgumentException>(() => new Release1NativeEffectJournalEntry("phase", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", (Release1NativeEffectPhase)99, null, 0));
    }

    private static Release1StoryState MakeGoodActive()
    {
        var state = StateWithMissionState(Release1MissionState.Active);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RequiredFailure, "initial-failure")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "accept-good")).State!;
        return state;
    }

    private static Release1StoryState StateWithMissionState(Release1MissionState missionState)
    {
        var state = Release1StoryState.CreateAccepted(Player, IntroCorrelation("intro"));
        var mission = state.Missions[0] with
        {
            State = missionState,
            RewardAuthorizationReceiptId = missionState == Release1MissionState.Satisfied ? "reward" : null,
            LastOutcome = missionState == Release1MissionState.Satisfied ? Release1MissionOutcome.OnTime : Release1MissionOutcome.None
        };
        return state with { Missions = state.Missions.Select((m, i) => i == 0 ? mission : m).ToArray() };
    }

    private static Release1StoryRuntimeCommandResult Execute(Harness harness, Release1StoryCommand command) => harness.Service.TryExecute(command);
    private static Release1StoryTransitionResult Apply(Release1StoryState state, Release1StoryCommand command) => Release1StoryTransitions.Apply(state, command);
    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, Release1NativeEffectJournalEntry? effect = null) => new(Session, 1, Player, mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, null, null, timing, reward, null, null, effect);
    private static Release1NativeEffectJournalEntry Effect(string id, Release1MissionRecord mission, long revision, string authorization) => new(id, mission.MissionKey, mission.Attempt, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, revision, AuthorizedStoryCorrelationId: authorization);
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

    private sealed record Harness(Release1StoryRuntimeService Service, FakeContext Context, BoundRepository Repository);
    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; set; } = new(Session, 1, Player, Path.GetFullPath(Path.GetTempPath()));
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Status; }
    }
    private sealed class BoundRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public BoundRepository(string folder) => BoundSaveFolder = folder;
        public string BoundSaveFolder { get; }
        public bool FailUpdates { get; set; }
        public Release1StoryState? StoredState { get; set; }
        public Release1StoryStoreLoadResult Load() => new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded, new Release1StorySaveEnvelope(1, StoredState), Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) => FailUpdates ? new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.SidecarReadFailed, "save failed") : Store(state);
        private Release1StoryStoreUpdateResult Store(Release1StoryState? state) { StoredState = state; return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(1, state), Release1StoryStoreFailureReason.None, string.Empty); }
    }
}
