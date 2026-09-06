using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1KeepTheLightsOffStoryTests
{
    private const string PlayerId = "76561190000000001";
    private const string TermsVersion = "keep-the-lights-off-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("73737373-7373-7373-7373-737373737373");

    [Fact]
    public void Acceptance_writes_the_assignment_in_the_same_revision_as_the_transition()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary", terms: TermsVersion, accepted: 10, deadline: 34);
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[missionIndex].State);
        Assert.Equal(assignment, Assert.Single(accepted.KeepTheLightsOffAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[missionIndex].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[missionIndex].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
    }

    [Fact]
    public void Acceptance_rejects_an_assignment_whose_correlation_is_not_the_command_correlation()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-mismatch", terms: TermsVersion);
        var otherCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.KeepTheLightsOff, acceptance.Attempt, Release1TransitionKind.MissionAccepted, "other-authorize").Value;
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, acceptance.Attempt, otherCorrelation);

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Acceptance_rejects_a_primary_stage_without_the_keep_the_lights_off_terms_version()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-wrong-terms", terms: "wrong-terms");
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Acceptance_is_idempotent_on_a_repeated_correlation()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-idempotent", terms: TermsVersion);
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment).Accepted);
        var updateCount = harness.Repository.Updates.Count;

        var duplicate = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal(updateCount, harness.Repository.Updates.Count);
    }

    [Fact]
    public void Progress_is_written_in_memory_only_and_does_not_persist_until_a_save()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        harness.Repository.ResetEvidence();
        var progress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 100d, null);

        var result = harness.Service.TrySetKeepTheLightsOffProgress(progress);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Empty(harness.Repository.Updates);
        Assert.True(harness.Service.State!.Revision > harness.Service.LastPersistedRevision);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        Assert.Equal(progress, Assert.Single(harness.Repository.StoredState!.KeepTheLightsOffProgress));
    }

    [Fact]
    public void Progress_refuses_an_attempt_with_no_assignment()
    {
        using var harness = ActiveHarness();

        var result = harness.Service.TrySetKeepTheLightsOffProgress(Release1KeepTheLightsOffProgress.Fresh(1));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
    }

    [Fact]
    public void Clear_confirmation_cannot_be_withdrawn_but_a_breach_may_be_recorded_and_cleared_freely()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var cleared = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 100d, null);
        Assert.True(harness.Service.TrySetKeepTheLightsOffProgress(cleared).Accepted);

        var withdrawn = harness.Service.TrySetKeepTheLightsOffProgress(
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, null, null));
        Assert.Equal(Release1StoryCommandStatus.Rejected, withdrawn.Status);

        var breached = harness.Service.TrySetKeepTheLightsOffProgress(
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 100d, 150d));
        Assert.True(breached.Accepted);

        var breachCleared = harness.Service.TrySetKeepTheLightsOffProgress(
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 100d, null));
        Assert.True(breachCleared.Accepted);
        Assert.Null(harness.Service.State!.KeepTheLightsOffProgress.Single().BreachSincePassGameMinutes);
    }

    [Fact]
    public void A_migrated_active_attempt_accepts_one_transition_free_assignment_restore()
    {
        var (harness, assignment) = MigratedActiveHarness();
        using var disposable = harness;
        var before = harness.Service.State!;
        var beforeMission = before.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

        var result = harness.Service.TryRestoreKeepTheLightsOffAssignment(assignment);

        Assert.True(result.Accepted);
        var after = harness.Service.State!;
        var afterMission = after.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        Assert.Equal(beforeMission.State, afterMission.State);
        Assert.Equal(beforeMission.Attempt, afterMission.Attempt);
        Assert.Equal(beforeMission.DeadlineGameTimeHours, afterMission.DeadlineGameTimeHours);
        Assert.Equal(before.Standing, after.Standing);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(assignment, Assert.Single(after.KeepTheLightsOffAssignments));
    }

    [Fact]
    public void A_restore_is_refused_when_an_assignment_already_exists_for_that_attempt()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var assignment = Assert.Single(harness.Service.State!.KeepTheLightsOffAssignments);
        var revisionBefore = harness.Service.State!.Revision;

        var result = harness.Service.TryRestoreKeepTheLightsOffAssignment(assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, result.Status);
        Assert.Equal(revisionBefore, harness.Service.State!.Revision);
    }

    [Fact]
    public void A_restore_is_refused_on_a_mismatched_mode_attempt_or_correlation()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

        var wrongMode = Assignment(Release1KeepTheLightsOffAssignmentMode.MakeGood, mission.Attempt, Correlation(mission.Attempt, Release1TransitionKind.MakeGoodAccepted, "wrong-mode"));
        var wrongModeResult = harness.Service.TryRestoreKeepTheLightsOffAssignment(wrongMode);
        Assert.False(wrongModeResult.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, wrongModeResult.RejectReason);

        var wrongAttempt = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, mission.Attempt + 1, Correlation(mission.Attempt + 1, Release1TransitionKind.MissionAccepted, "wrong-attempt"));
        var wrongAttemptResult = harness.Service.TryRestoreKeepTheLightsOffAssignment(wrongAttempt);
        Assert.False(wrongAttemptResult.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, wrongAttemptResult.RejectReason);

        var wrongCorrelation = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, mission.Attempt, Correlation(mission.Attempt, Release1TransitionKind.MissionAccepted, "not-accepted-yet"));
        var wrongCorrelationResult = harness.Service.TryRestoreKeepTheLightsOffAssignment(wrongCorrelation);
        Assert.False(wrongCorrelationResult.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, wrongCorrelationResult.RejectReason);
    }

    [Fact]
    public void A_restore_is_refused_when_the_mission_is_not_in_an_active_stage()
    {
        using var offeredHarness = ActiveHarness();
        var offeredAssignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "offered-authorize"));
        var offeredResult = offeredHarness.Service.TryRestoreKeepTheLightsOffAssignment(offeredAssignment);
        Assert.False(offeredResult.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, offeredResult.RejectReason);

        using var satisfiedHarness = SatisfiedHarness();
        var satisfiedAssignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "satisfied-authorize"));
        var satisfiedResult = satisfiedHarness.Service.TryRestoreKeepTheLightsOffAssignment(satisfiedAssignment);
        Assert.False(satisfiedResult.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, satisfiedResult.RejectReason);
    }

    [Fact]
    public void A_restore_never_touches_progress_or_any_other_mission()
    {
        var (harness, assignment) = MigratedActiveHarness();
        using var disposable = harness;
        var missionsBefore = harness.Service.State!.Missions;

        var result = harness.Service.TryRestoreKeepTheLightsOffAssignment(assignment);

        Assert.True(result.Accepted);
        Assert.Empty(harness.Service.State!.KeepTheLightsOffProgress);
        Assert.Same(missionsBefore, harness.Service.State!.Missions);
    }

    private static void AcceptPrimary(Harness harness)
    {
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary-helper", terms: TermsVersion);
        Assert.True(harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            command,
            Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, command.Attempt, command.CorrelationId)).Accepted);
    }

    private static Release1KeepTheLightsOffAssignment Assignment(
        Release1KeepTheLightsOffAssignmentMode mode,
        int attempt,
        string authorization) =>
        new(
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            mode,
            authorization,
            1,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);

    private static string Correlation(int attempt, Release1TransitionKind transition, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.KeepTheLightsOff, attempt, transition, receipt).Value;

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    /// <summary>
    /// Drives a real acceptance to Active, then routes the resulting state through the codec's v9
    /// read path to reproduce exactly what a save made before OC-65 landed looks like once loaded:
    /// the mission record, its attempt, and its accepted correlation intact, its assignment gone.
    /// </summary>
    private static (Harness Harness, Release1KeepTheLightsOffAssignment Assignment) MigratedActiveHarness()
    {
        using var seed = ActiveHarness();
        AcceptPrimary(seed);
        var assignment = Assert.Single(seed.Service.State!.KeepTheLightsOffAssignments);

        Assert.True(Release1StorySaveCodec.TrySerialize(seed.Service.State, out var json, out var writeResult), writeResult.Message);
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 9;
        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var migrated, out var readResult), readResult.Message);
        Assert.Empty(migrated!.Story!.KeepTheLightsOffAssignments);

        var repository = new FakeRepository();
        repository.Update(migrated.Story);
        return (LoadedHarness(repository), assignment);
    }

    private static Harness SatisfiedHarness()
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var correlation = Correlation(1, Release1TransitionKind.MissionAccepted, "satisfied-authorize-helper");
        missions[index] = missions[index] with
        {
            State = Release1MissionState.Satisfied,
            Attempt = 1,
            TermsVersion = TermsVersion,
            LastOutcome = Release1MissionOutcome.OnTime,
            RewardAuthorizationReceiptId = "reward-ktlo-helper",
            AcceptedLogicalCorrelations = new[] { correlation }
        };
        var repository = new FakeRepository();
        repository.Update(story with { Missions = missions });
        return LoadedHarness(repository);
    }

    private static Harness ActiveHarness(FakeRepository? repository = null)
    {
        repository ??= new FakeRepository();
        var harness = LoadedHarness(repository);
        var intro = new Release1StoryCommand(
            SessionEpoch,
            1,
            PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            "intro-keep-the-lights-off",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-keep-the-lights-off").Value);
        Assert.True(harness.Service.TryExecuteDurably(intro).Accepted);

        foreach (var missionKey in new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress, Release1MissionCatalog.RoomWithNoName, Release1MissionCatalog.ShortNotice })
        {
            var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(missionKey)];
            var acceptReceipt = $"accept-{missionKey}-unlock";
            var accept = new Release1StoryCommand(
                harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
                missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt,
                Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
                "terms-v1");
            Assert.True(harness.Service.TryExecuteDurably(accept).Accepted);

            var activateReceipt = $"activate-{missionKey}-unlock";
            var activate = new Release1StoryCommand(
                harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
                missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt,
                Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
            Assert.True(harness.Service.TryExecuteDurably(activate).Accepted);

            var completeReceipt = $"complete-{missionKey}-unlock";
            var complete = new Release1StoryCommand(
                harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
                missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt,
                Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
                CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: $"reward-{missionKey}-unlock");
            Assert.True(harness.Service.TryExecuteDurably(complete).Accepted);
        }

        repository.ResetEvidence();
        return harness;
    }

    private static Harness LoadedHarness(FakeRepository repository)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        var loaded = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, loaded.RejectReason);
        return new Harness(service, context, repository);
    }

    private sealed class Harness : IDisposable
    {
        public Harness(Release1StoryRuntimeService service, FakeContext context, FakeRepository repository)
        {
            Service = service;
            Context = context;
            Repository = repository;
        }

        public Release1StoryRuntimeService Service { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }

        public Release1StoryCommand Command(
            Release1TransitionKind kind,
            string receipt,
            int? attempt = null,
            string? terms = null,
            double? accepted = null,
            double? deadline = null)
        {
            var mission = Service.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
            var commandAttempt = attempt ?? mission.Attempt;
            return new(
                Context.Snapshot.SessionEpoch,
                Context.Snapshot.LoadEpoch,
                Context.Snapshot.PlayerId,
                Release1MissionCatalog.KeepTheLightsOff,
                commandAttempt,
                kind,
                receipt,
                Release1LogicalCorrelation.Create(Context.Snapshot.PlayerId, Release1MissionCatalog.KeepTheLightsOff, commandAttempt, kind, receipt).Value,
                terms,
                accepted,
                deadline);
        }

        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public List<Release1StoryState> Updates { get; } = new();
        public int UpdateCalls { get; private set; }
        public int? FailOnUpdateCall { get; set; }

        public Release1StoryStoreLoadResult Load() =>
            new(
                true,
                StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
                Release1StoryStoreFailureReason.None,
                string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            UpdateCalls++;
            if (FailOnUpdateCall == UpdateCalls)
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public void ResetEvidence()
        {
            Updates.Clear();
            UpdateCalls = 0;
            FailOnUpdateCall = null;
        }
    }
}
