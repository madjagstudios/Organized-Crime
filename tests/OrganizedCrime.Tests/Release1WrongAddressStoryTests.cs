using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressStoryTests
{
    private const string PlayerId = "76561190000000001";
    private const string TermsVersion = "wrong-address-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("62626262-6262-6262-6262-626262626262");

    [Fact]
    public void Story_rejects_duplicate_mission_attempt_assignments()
    {
        var assignment = Assignment();
        var state = Story() with { WrongAddressAssignments = new[] { assignment, assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_assignment_from_a_future_mission_attempt()
    {
        var assignment = Assignment(
            Release1WrongAddressAssignmentMode.MakeGood,
            2,
            Release1TransitionKind.MakeGoodAccepted,
            "jar");
        var state = Story() with { WrongAddressAssignments = new[] { assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_assignment_whose_authorization_was_not_accepted_by_wrong_address()
    {
        var assignment = Assignment();
        var state = Story() with { WrongAddressAssignments = new[] { assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_product_or_later_stage_package_drift()
    {
        var primary = Assignment();
        var makeGood = Assignment(Release1WrongAddressAssignmentMode.MakeGood, 2, Release1TransitionKind.MakeGoodAccepted, "jar");
        var recovery = Assignment(Release1WrongAddressAssignmentMode.Recovery, 3, Release1TransitionKind.RecoveryAccepted, "jar");

        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood) with
        {
            WrongAddressAssignments = new[] { primary, makeGood with { ProductId = "other-product" } }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood, recovery) with
        {
            WrongAddressAssignments = new[] { primary, makeGood, recovery with { PackagingId = "other-jar" } }
        }).Validate());
    }

    [Fact]
    public void Story_rejects_progress_with_no_matching_assignment()
    {
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, true, false);
        var state = Story() with { WrongAddressProgress = new[] { progress } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_duplicate_progress_for_the_same_attempt()
    {
        var assignment = Assignment();
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, true, false);
        var state = StoryWithAssignments(assignment) with { WrongAddressProgress = new[] { progress, progress } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_value_equality_and_hash_include_assignments_and_progress()
    {
        var assignment = Assignment();
        var progress = new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, true, false);
        var left = StoryWithAssignments(assignment) with { WrongAddressProgress = new[] { progress } };
        var same = StoryWithAssignments(assignment) with { WrongAddressProgress = new[] { progress } };
        var differentAssignment = StoryWithAssignments(assignment with { SourceDropName = "Other source" }) with { WrongAddressProgress = new[] { progress } };
        var differentProgress = StoryWithAssignments(assignment) with { WrongAddressProgress = Array.Empty<Release1WrongAddressProgress>() };

        left.Validate(); same.Validate(); differentAssignment.Validate(); differentProgress.Validate();
        Assert.True(left.ValueEquals(same));
        Assert.Equal(left.GetHashCode(), same.GetHashCode());
        Assert.False(left.ValueEquals(differentAssignment));
        Assert.NotEqual(left.GetHashCode(), differentAssignment.GetHashCode());
        Assert.False(left.ValueEquals(differentProgress));
        Assert.NotEqual(left.GetHashCode(), differentProgress.GetHashCode());
    }

    [Fact]
    public void Primary_acceptance_and_assignment_share_one_snapshot_before_durable_activation()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary", terms: TermsVersion, accepted: 10, deadline: 34);
        var assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[missionIndex].State);
        Assert.Equal(assignment, Assert.Single(accepted.WrongAddressAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[missionIndex].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[missionIndex].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
        Assert.Contains(active.Missions[missionIndex].AcceptedLogicalCorrelations, value =>
            Release1LogicalCorrelation.TryParse(value, out var correlation) &&
            correlation.TransitionKind == Release1TransitionKind.MissionActivated);
    }

    [Fact]
    public void Duplicate_identical_acceptance_is_inert_but_conflicting_assignment_reuse_rejects()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-idempotent", terms: TermsVersion);
        var assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment).Accepted);
        var updateCount = harness.Repository.Updates.Count;

        var duplicate = harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment);
        var conflict = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            acceptance,
            assignment with { SourceDropGuid = "other-drop", SourceDropName = "Other drop" });

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal(updateCount, harness.Repository.Updates.Count);
    }

    [Fact]
    public void Acceptance_repository_failure_leaves_the_offer_and_assignment_collection_unchanged()
    {
        using var harness = ActiveHarness();
        harness.Repository.FailOnUpdateCall = 1;
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-fails", terms: TermsVersion);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            acceptance,
            Assignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[missionIndex].State);
        Assert.Empty(harness.Service.State.WrongAddressAssignments);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Acceptance_rejects_wrong_terms_mode_epoch_identity_and_saving_before_persistence()
    {
        using var harness = ActiveHarness();
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "bad-accept", terms: "wrong-terms");
        var assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, assignment).Status);

        command = harness.Command(Release1TransitionKind.MissionAccepted, "wrong-mode", terms: TermsVersion);
        var makeGoodAuthorization = Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.WrongAddress,
            1,
            Release1TransitionKind.MakeGoodAccepted,
            "wrong-mode-assignment").Value;
        assignment = Assignment(Release1WrongAddressAssignmentMode.MakeGood, 1, makeGoodAuthorization, "jar");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, assignment).Status);

        command = harness.Command(Release1TransitionKind.MissionAccepted, "wrong-epoch", terms: TermsVersion) with { LoadEpoch = 99 };
        assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, assignment).Status);

        command = harness.Command(Release1TransitionKind.MissionAccepted, "wrong-identity", terms: TermsVersion) with { PlayerId = "76561190000000002" };
        assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.Rejected, harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, assignment).Status);

        harness.Service.OnSaveStart();
        command = harness.Command(Release1TransitionKind.MissionAccepted, "during-save", terms: TermsVersion);
        assignment = Assignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick");
        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, assignment).Status);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Make_good_and_recovery_assignments_attach_to_the_post_increment_attempt()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.RequiredFailure, "primary-failed")).Accepted);
        var makeGood = harness.Command(Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2, accepted: 40, deadline: 64);

        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            makeGood,
            Assignment(Release1WrongAddressAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar")).Accepted);
        Assert.Equal(2, harness.Service.State!.Missions[missionIndex].Attempt);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State.Missions[missionIndex].State);
        Assert.True(harness.Service.TryExecuteDurably(harness.Command(Release1TransitionKind.MakeGoodFailed, "make-good-failed")).Accepted);
        var recovery = harness.Command(Release1TransitionKind.RecoveryAccepted, "accept-recovery", attempt: 3, accepted: 70);

        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            recovery,
            Assignment(Release1WrongAddressAssignmentMode.Recovery, 3, recovery.CorrelationId, "jar")).Accepted);
        Assert.Equal(3, harness.Service.State!.Missions[missionIndex].Attempt);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Service.State.Missions[missionIndex].State);
        Assert.Equal(new[] { 1, 2, 3 }, harness.Service.State.WrongAddressAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void Activation_failure_leaves_a_durable_inert_acceptance_and_load_retries_activation_once()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = first.Command(Release1TransitionKind.MissionAccepted, "accept-before-activation-fault", terms: TermsVersion);
            var result = first.Service.TryExecuteWrongAddressAcceptanceDurably(
                acceptance,
                Assignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

            var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, first.Service.State!.Missions[missionIndex].State);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[missionIndex].State);
            Assert.Single(repository.StoredState.WrongAddressAssignments);
        }

        repository.ResetEvidence();
        using var second = LoadedHarness(repository);
        var idx = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[idx].State);
        Assert.Single(repository.Updates);
        second.Service.OnLoadComplete();
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Deferred_mission_is_reoffered_once_on_the_next_load_and_advances_attempt()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository))
            Assert.True(first.Service.TryExecuteDurably(first.Command(Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);

        repository.ResetEvidence();
        using var second = LoadedHarness(repository);
        var idx = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);

        Assert.Equal(Release1MissionState.Offered, second.Service.State!.Missions[idx].State);
        Assert.Equal(2, second.Service.State.Missions[idx].Attempt);
        Assert.Null(second.Service.State.Missions[idx].TermsVersion);
        Assert.Single(repository.Updates);
        second.Service.OnLoadComplete();
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Accepted_mission_with_no_assignment_on_load_returns_a_typed_rejection_not_silence()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository))
        {
            // Bypass TryExecuteWrongAddressAcceptanceDurably so the mission lands Accepted with no
            // assignment on file, reproducing a corrupted-or-partial persistence state.
            var command = first.Command(Release1TransitionKind.MissionAccepted, "accept-without-assignment", terms: TermsVersion);
            Assert.True(first.Service.TryExecuteDurably(command).Accepted);
        }

        repository.ResetEvidence();
        var context = new FakeContext();
        using var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();

        var loaded = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.RejectReason);
        Assert.NotEqual(string.Empty, loaded.Message);
        var idx = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        Assert.Equal(Release1MissionState.Accepted, service.State!.Missions[idx].State);
        Assert.Empty(service.State.WrongAddressAssignments);
    }

    private static void AcceptPrimary(Harness harness)
    {
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary-helper", terms: TermsVersion);
        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            command,
            Assignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick")).Accepted);
    }

    private static Release1WrongAddressAssignment Assignment(
        Release1WrongAddressAssignmentMode mode = Release1WrongAddressAssignmentMode.Primary,
        int attempt = 1,
        Release1TransitionKind transition = Release1TransitionKind.MissionAccepted,
        string packagingId = "brick") =>
        Assignment(mode, attempt, Correlation(attempt, transition, $"authorize-{attempt}"), packagingId);

    private static Release1WrongAddressAssignment Assignment(
        Release1WrongAddressAssignmentMode mode,
        int attempt,
        string authorization,
        string packagingId) =>
        new(
            Release1MissionCatalog.WrongAddress,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            packagingId,
            packagingId == "brick" ? "Brick" : "Jar",
            1,
            $"drop-source-{attempt}",
            $"Source {attempt}",
            "A wrong address",
            attempt,
            2,
            3,
            $"drop-handoff-{attempt}",
            $"Handoff {attempt}",
            "The right address",
            attempt + 10,
            2,
            3,
            1.25);

    private static string Correlation(int attempt, Release1TransitionKind transition, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.WrongAddress, attempt, transition, receipt).Value;

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState StoryWithAssignments(params Release1WrongAddressAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        missions[index] = missions[index] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments.Select(assignment => assignment.AuthorizationCorrelationId).ToArray()
        };
        return story with { Missions = missions, WrongAddressAssignments = assignments };
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
            "intro-wrong-address",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-wrong-address").Value);
        Assert.True(harness.Service.TryExecuteDurably(intro).Accepted);

        var acceptSmallCourtesy = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "accept-small-courtesy-unlock",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "accept-small-courtesy-unlock").Value,
            "small-courtesy-v1");
        Assert.True(harness.Service.TryExecuteDurably(acceptSmallCourtesy).Accepted);

        var activateSmallCourtesy = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionActivated, "activate-small-courtesy-unlock",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionActivated, "activate-small-courtesy-unlock").Value);
        Assert.True(harness.Service.TryExecuteDurably(activateSmallCourtesy).Accepted);

        var completeSmallCourtesy = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionCompleted, "complete-small-courtesy-unlock",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionCompleted, "complete-small-courtesy-unlock").Value,
            CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: "reward-small-courtesy-unlock");
        Assert.True(harness.Service.TryExecuteDurably(completeSmallCourtesy).Accepted);

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
            var mission = Service.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
            var commandAttempt = attempt ?? mission.Attempt;
            return new(
                Context.Snapshot.SessionEpoch,
                Context.Snapshot.LoadEpoch,
                Context.Snapshot.PlayerId,
                Release1MissionCatalog.WrongAddress,
                commandAttempt,
                kind,
                receipt,
                Release1LogicalCorrelation.Create(Context.Snapshot.PlayerId, Release1MissionCatalog.WrongAddress, commandAttempt, kind, receipt).Value,
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
