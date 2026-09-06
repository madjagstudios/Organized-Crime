using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeStoryTests
{
    private const string PlayerId = "76561190000000001";
    private const string TermsVersion = "short-notice-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("73737373-7373-7373-7373-737373737373");

    [Fact]
    public void Acceptance_writes_the_assignment_in_the_same_revision_as_the_transition()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary", terms: TermsVersion, accepted: 10, deadline: 34);
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var missionIndex = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[missionIndex].State);
        Assert.Equal(assignment, Assert.Single(accepted.ShortNoticeAssignments));
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
            PlayerId, Release1MissionCatalog.ShortNotice, acceptance.Attempt, Release1TransitionKind.MissionAccepted, "other-authorize").Value;
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, acceptance.Attempt, otherCorrelation, "brick");

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Acceptance_rejects_a_primary_stage_without_the_short_notice_terms_version()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-wrong-terms", terms: "wrong-terms");
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Acceptance_is_idempotent_on_a_repeated_correlation()
    {
        using var harness = ActiveHarness();
        var acceptance = harness.Command(Release1TransitionKind.MissionAccepted, "accept-idempotent", terms: TermsVersion);
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment).Accepted);
        var updateCount = harness.Repository.Updates.Count;

        var duplicate = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal(updateCount, harness.Repository.Updates.Count);
    }

    [Fact]
    public void Progress_is_written_in_memory_only_and_does_not_persist_until_a_save()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        harness.Repository.ResetEvidence();
        var progress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 2, null, false);

        var result = harness.Service.TrySetShortNoticeProgress(progress);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Empty(harness.Repository.Updates);
        Assert.True(harness.Service.State!.Revision > harness.Service.LastPersistedRevision);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        Assert.Equal(progress, Assert.Single(harness.Repository.StoredState!.ShortNoticeProgress));
    }

    [Fact]
    public void Progress_refuses_an_attempt_with_no_assignment()
    {
        using var harness = ActiveHarness();

        var result = harness.Service.TrySetShortNoticeProgress(Release1ShortNoticeProgress.Fresh(1));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
    }

    [Fact]
    public void Spread_notice_cannot_be_withdrawn_but_the_observed_quantity_may_fall()
    {
        using var harness = ActiveHarness();
        AcceptPrimary(harness);
        var noticed = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 3, null, true);
        Assert.True(harness.Service.TrySetShortNoticeProgress(noticed).Accepted);

        var withdrawn = harness.Service.TrySetShortNoticeProgress(
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 3, null, false));
        Assert.Equal(Release1StoryCommandStatus.Rejected, withdrawn.Status);

        var fallen = harness.Service.TrySetShortNoticeProgress(
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 1, 2, true));
        Assert.True(fallen.Accepted);
        Assert.Equal(1, harness.Service.State!.ShortNoticeProgress.Single().ObservedQuantity);
    }

    [Fact]
    public void State_refuses_a_second_assignment_with_a_different_product_packaging_or_value_convention()
    {
        var primary = Assignment(Release1ShortNoticeAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "authorize-1"), "brick");
        var makeGood = Assignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, Correlation(2, Release1TransitionKind.MakeGoodAccepted, "authorize-2"), "brick");

        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood) with
        {
            ShortNoticeAssignments = new[] { primary, makeGood with { ProductId = "other-product" } }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood) with
        {
            ShortNoticeAssignments = new[] { primary, makeGood with { PackagingId = "other-brick" } }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood) with
        {
            ShortNoticeAssignments = new[] { primary, makeGood with { ValueConvention = Release1ShortNoticeValueConvention.PerStack } }
        }).Validate());
    }

    private static void AcceptPrimary(Harness harness)
    {
        var command = harness.Command(Release1TransitionKind.MissionAccepted, "accept-primary-helper", terms: TermsVersion);
        Assert.True(harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            command,
            Assignment(Release1ShortNoticeAssignmentMode.Primary, command.Attempt, command.CorrelationId, "brick")).Accepted);
    }

    private static Release1ShortNoticeAssignment Assignment(
        Release1ShortNoticeAssignmentMode mode,
        int attempt,
        string authorization,
        string packagingId) =>
        new(
            Release1MissionCatalog.ShortNotice,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            packagingId,
            packagingId == "brick" ? "Brick" : "Jar",
            Release1ShortNoticeAssignment.QuantityFor(mode),
            $"drop-handoff-{attempt}",
            $"Handoff {attempt}",
            "The right address",
            attempt + 10,
            2,
            3,
            mode == Release1ShortNoticeAssignmentMode.Recovery ? null : Release1ShortNoticeAssignment.TimedStageGameMinutes,
            Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
            Release1ShortNoticeValueConvention.PerUnit);

    private static string Correlation(int attempt, Release1TransitionKind transition, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.ShortNotice, attempt, transition, receipt).Value;

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState StoryWithAssignments(params Release1ShortNoticeAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        missions[index] = missions[index] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments.Select(assignment => assignment.AuthorizationCorrelationId).ToArray()
        };
        return story with { Missions = missions, ShortNoticeAssignments = assignments };
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
            "intro-short-notice",
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-short-notice").Value);
        Assert.True(harness.Service.TryExecuteDurably(intro).Accepted);

        foreach (var missionKey in new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress, Release1MissionCatalog.RoomWithNoName })
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
            var mission = Service.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
            var commandAttempt = attempt ?? mission.Attempt;
            return new(
                Context.Snapshot.SessionEpoch,
                Context.Snapshot.LoadEpoch,
                Context.Snapshot.PlayerId,
                Release1MissionCatalog.ShortNotice,
                commandAttempt,
                kind,
                receipt,
                Release1LogicalCorrelation.Create(Context.Snapshot.PlayerId, Release1MissionCatalog.ShortNotice, commandAttempt, kind, receipt).Value,
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
