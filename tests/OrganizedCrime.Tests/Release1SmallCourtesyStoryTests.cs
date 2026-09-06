using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyStoryTests
{
    private const string PlayerId = "76561190000000001";
    private const string TermsVersion = "small-courtesy-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("51515151-5151-5151-5151-515151515151");

    [Fact]
    public void Primary_acceptance_and_assignment_share_one_snapshot_before_durable_activation()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary", terms: TermsVersion, accepted: 10, deadline: 34);
        var assignment = Assignment(Release1SmallCourtesyAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");

        var result = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[0].State);
        Assert.Equal(assignment, Assert.Single(accepted.SmallCourtesyAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[0].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[0].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
        Assert.Contains(active.Missions[0].AcceptedLogicalCorrelations, value =>
            Release1LogicalCorrelation.TryParse(value, out var correlation) &&
            correlation.TransitionKind == Release1TransitionKind.MissionActivated);
    }

    [Fact]
    public void Acceptance_repository_failure_leaves_the_offer_and_assignment_collection_unchanged()
    {
        using var harness = ActiveHarness();
        harness.Repository.FailOnUpdateCall = 1;
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-fails", terms: TermsVersion);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            acceptance,
            Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[0].State);
        Assert.Empty(harness.Service.State.SmallCourtesyAssignments);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Activation_failure_leaves_a_durable_inert_acceptance_and_load_retries_activation_once()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = first.Command(Release1TransitionKind.MissionAccepted, "accept-before-activation-fault", terms: TermsVersion);
            var result = first.Service.TryExecuteSmallCourtesyAcceptanceDurably(
                acceptance,
                Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, first.Service.State!.Missions[0].State);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[0].State);
            Assert.Single(repository.StoredState.SmallCourtesyAssignments);
        }

        repository.ResetEvidence();
        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[0].State);
        Assert.Single(repository.Updates);
        second.Service.OnLoadComplete();
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Duplicate_identical_acceptance_is_inert_but_conflicting_assignment_reuse_rejects()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-idempotent", terms: TermsVersion);
        var assignment = Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment).Accepted);
        var updateCount = harness.Repository.Updates.Count;

        var duplicate = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment);
        var conflict = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            acceptance,
            assignment with { DeadDropGuid = "other-drop", DeadDropName = "Other drop" });

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal(updateCount, harness.Repository.Updates.Count);
    }

    [Fact]
    public void Make_good_and_recovery_assignments_attach_to_the_post_increment_attempt()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.RequiredFailure, "primary-failed")).Accepted);
        var makeGood = harness.Command(Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2, accepted: 40, deadline: 64);

        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            makeGood,
            Assignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar")).Accepted);
        Assert.Equal(2, harness.Service.State!.Missions[0].Attempt);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State.Missions[0].State);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.MakeGoodFailed, "make-good-failed")).Accepted);
        var recovery = harness.Command(Release1TransitionKind.RecoveryAccepted, "accept-recovery", attempt: 3, accepted: 70);

        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            recovery,
            Assignment(Release1SmallCourtesyAssignmentMode.Recovery, 3, recovery.CorrelationId, "jar")).Accepted);
        Assert.Equal(3, harness.Service.State!.Missions[0].Attempt);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Service.State.Missions[0].State);
        Assert.Equal(new[] { 1, 2, 3 }, harness.Service.State.SmallCourtesyAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void Deferred_mission_is_reoffered_once_on_the_next_load_and_advances_attempt()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository))
            Assert.True(first.Service.TryExecuteDurably(first.Command(Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);

        repository.ResetEvidence();
        using var second = LoadedHarness(repository);

        Assert.Equal(Release1MissionState.Offered, second.Service.State!.Missions[0].State);
        Assert.Equal(2, second.Service.State.Missions[0].Attempt);
        Assert.Null(second.Service.State.Missions[0].TermsVersion);
        Assert.Single(repository.Updates);
        second.Service.OnLoadComplete();
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Accepted_but_not_activated_cannot_authorize_deadline_failure()
    {
        using var harness = ActiveHarness();
        harness.Repository.FailOnUpdateCall = 2;
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-inert", terms: TermsVersion);
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            acceptance,
            Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick")).Status);

        var failure = harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.RequiredFailure, "deadline-before-active"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, failure.Status);
        Assert.Equal(Release1MissionState.Accepted, harness.Service.State!.Missions[0].State);
    }

    [Fact]
    public void Cargo_authorization_is_narrow_for_primary_make_good_and_recovery()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var primaryMission = harness.Service.State!.Missions[0];
        var primaryActivation = FindCorrelation(primaryMission, Release1TransitionKind.MissionActivated);
        Assert.True(harness.Service.TryPrepareNativeEffect(Effect(harness, primaryMission, primaryActivation, "primary-effect")).Accepted);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.RequiredFailure, "primary-failure-for-auth")).Accepted);
        var makeGood = harness.Command(Release1TransitionKind.MakeGoodAccepted, "make-good-auth", attempt: 2);
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            makeGood,
            Assignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar")).Accepted);
        var makeGoodMission = harness.Service.State!.Missions[0];
        Assert.True(harness.Service.TryPrepareNativeEffect(Effect(harness, makeGoodMission, makeGood.CorrelationId, "make-good-effect")).Accepted);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.MakeGoodFailed, "make-good-failure-for-auth")).Accepted);
        var recovery = harness.Command(Release1TransitionKind.RecoveryAccepted, "recovery-auth", attempt: 3);
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            recovery,
            Assignment(Release1SmallCourtesyAssignmentMode.Recovery, 3, recovery.CorrelationId, "jar")).Accepted);
        var recoveryMission = harness.Service.State!.Missions[0];
        Assert.True(harness.Service.TryPrepareNativeEffect(Effect(harness, recoveryMission, recovery.CorrelationId, "recovery-effect")).Accepted);

        var stale = Effect(harness, recoveryMission, makeGood.CorrelationId, "stale-effect");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryPrepareNativeEffect(stale).Status);
    }

    [Fact]
    public void Primary_acceptance_and_cross_mission_correlations_cannot_authorize_cargo()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-without-activation", terms: TermsVersion);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        var acceptedMission = harness.Service.State!.Missions[0];

        Assert.Equal(
            Release1StoryCommandStatus.Rejected,
            harness.Service.TryPrepareNativeEffect(Effect(harness, acceptedMission, acceptance.CorrelationId, "accepted-effect")).Status);

        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.MissionActivated, "activate-for-cross-mission")).Accepted);
        var activeMission = harness.Service.State!.Missions[0];
        var crossMission = Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.WrongAddress,
            1,
            Release1TransitionKind.MissionActivated,
            "wrong-mission").Value;
        Assert.Equal(
            Release1StoryCommandStatus.Rejected,
            harness.Service.TryPrepareNativeEffect(Effect(harness, activeMission, crossMission, "cross-mission-effect")).Status);
    }

    [Fact]
    public void Acceptance_rejects_wrong_terms_mode_epoch_and_saving_before_persistence()
    {
        using var harness = ActiveHarness();
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "bad-accept", terms: "wrong-terms");
        var assignment = Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment).Status);

        command = harness.Command(Release1TransitionKind.MissionAccepted, "wrong-mode", terms: TermsVersion);
        var makeGoodAuthorization = Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1TransitionKind.MakeGoodAccepted,
            "wrong-mode-assignment").Value;
        assignment = Assignment(Release1SmallCourtesyAssignmentMode.MakeGood, 1, makeGoodAuthorization, "jar");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment).Status);

        command = harness.Command(Release1TransitionKind.MissionAccepted, "wrong-epoch", terms: TermsVersion) with { LoadEpoch = 99 };
        assignment = Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment).Status);

        harness.Service.OnSaveStart();
        command = harness.Command(Release1TransitionKind.MissionAccepted, "during-save", terms: TermsVersion);
        assignment = Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment).Status);
        Assert.Empty(harness.Repository.Updates);
    }

    private static void AcceptPrimary(Harness harness)
    {
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary-helper", terms: TermsVersion);
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            command,
            Assignment(Release1SmallCourtesyAssignmentMode.Primary, 1, command.CorrelationId, "brick")).Accepted);
    }

    private static Release1NativeEffectJournalEntry Effect(Harness harness, Release1MissionRecord mission, string authorization, string id) =>
        new(
            id,
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            "CargoTransfer",
            "dead-drop",
            "oc",
            "one-package",
            Release1NativeEffectPhase.Prepared,
            null,
            harness.Service.State!.Revision + 1,
            AuthorizedStoryCorrelationId: authorization,
            AuthorizedMissionRevision: mission.Revision);

    private static string FindCorrelation(Release1MissionRecord mission, Release1TransitionKind kind) =>
        mission.AcceptedLogicalCorrelations.Single(value =>
            Release1LogicalCorrelation.TryParse(value, out var correlation) && correlation.TransitionKind == kind);

    private static Release1SmallCourtesyAssignment Assignment(
        Release1SmallCourtesyAssignmentMode mode,
        int attempt,
        string authorization,
        string packaging) =>
        new(
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            500,
            packaging,
            packaging == "brick" ? "Brick" : "Jar",
            $"drop-{attempt}",
            $"Drop {attempt}",
            "A dead drop",
            attempt,
            2,
            3,
            1.25);

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
            "intro-small-courtesy",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-small-courtesy").Value);
        Assert.True(harness.Service.TryExecuteDurably(intro).Accepted);
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
            var mission = Service.State!.Missions[0];
            var commandAttempt = attempt ?? mission.Attempt;
            return new(
                Context.Snapshot.SessionEpoch,
                Context.Snapshot.LoadEpoch,
                Context.Snapshot.PlayerId,
                Release1MissionCatalog.SmallCourtesy,
                commandAttempt,
                kind,
                receipt,
                Release1LogicalCorrelation.Create(Context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, commandAttempt, kind, receipt).Value,
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
