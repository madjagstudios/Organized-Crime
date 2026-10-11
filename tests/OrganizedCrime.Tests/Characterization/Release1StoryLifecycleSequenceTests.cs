using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148 characterization: the Release 1 story save/load lifecycle call
// sequence and repeat/failure behaviour. Pinned before SaveLifecycle replaces
// the Chapter 3 and Release 1 Story skeletons; passes unchanged after.
public sealed class Release1StoryLifecycleSequenceTests
{
    private const string PlayerId = "76561190000000001";
    private const string SmallCourtesyTerms = "small-courtesy-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Happy_path_reads_context_then_loads_then_updates_once_at_save_complete()
    {
        var calls = new List<string>();
        var context = new FakeContext(calls);
        var repository = new FakeRepository(calls);
        var service = new Release1StoryRuntimeService(context, repository);

        var preLoad = service.OnPreLoad();
        var load = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
        var intro = IntroCommand(context);
        Assert.True(service.TryExecuteDurably(intro).Accepted);
        var stateAtSaveStart = service.State;
        var saveStart = service.OnSaveStart();
        var saveComplete = service.OnSaveComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, load.RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, saveStart.RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal(new[] { "context", "load", "context", "update", "update" }, calls);
        Assert.Same(stateAtSaveStart, repository.Updates[^1]);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Repeat_calls_are_idempotent_noops()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);

        var preLoad = service.OnPreLoad();
        var preLoadRepeat = service.OnPreLoad();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal("PreLoad was already handled.", preLoadRepeat.Message);
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);

        service.OnLoadComplete();
        var loadRepeat = service.OnLoadComplete();
        Assert.Equal("LoadComplete was already handled.", loadRepeat.Message);
        Assert.Equal(1, context.ReadCount);
        Assert.Equal(1, repository.LoadCallCount);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);

        service.OnSaveStart();
        var saveStartRepeat = service.OnSaveStart();
        Assert.Equal("SaveStart was already handled.", saveStartRepeat.Message);
        Assert.Equal(Release1StoryRuntimePhase.Saving, service.Phase);

        service.OnSaveComplete();
        var saveCompleteRepeat = service.OnSaveComplete();
        Assert.Equal("SaveComplete was a lifecycle no-op.", saveCompleteRepeat.Message);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Save_complete_without_save_start_is_a_noop_and_save_start_while_detached_is_inactive()
    {
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(new FakeContext(), repository);

        var saveComplete = service.OnSaveComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal("SaveComplete was a lifecycle no-op.", saveComplete.Message);
        Assert.Equal(Release1StoryRuntimePhase.Detached, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);

        var saveStart = service.OnSaveStart();
        Assert.Equal(Release1StoryRuntimeRejectReason.Inactive, saveStart.RejectReason);
        Assert.Equal("Only an active story can save.", saveStart.Message);
        Assert.Equal(Release1StoryRuntimePhase.Detached, service.Phase);
    }

    [Fact]
    public void Dispose_makes_every_call_return_disposed_and_second_dispose_is_silent()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        service.Dispose();
        service.Dispose();

        Assert.Equal(Release1StoryRuntimePhase.Disposed, service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, service.OnPreLoad().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, service.OnLoadComplete().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, service.OnSaveStart().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, service.OnSaveComplete().RejectReason);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, service.LastLifecycleRejectReason);
        Assert.Equal(1, context.ReadCount);
        Assert.Equal(1, repository.LoadCallCount);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void Quarantined_raw_contents_are_preserved_across_save_start_without_an_update()
    {
        const string garbage = "{ this is not valid story json";
        var repository = new FakeRepository { RawLoadFailureContents = garbage };
        var service = new Release1StoryRuntimeService(new FakeContext(), repository);

        service.OnPreLoad();
        var load = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.SidecarLoadFailed, load.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);

        var saveStart = service.OnSaveStart();
        Assert.Equal(Release1StoryRuntimeRejectReason.Quarantined, saveStart.RejectReason);
        Assert.Equal("Story runtime is quarantined; quarantined sidecar bytes were preserved.", saveStart.Message);
        Assert.Equal(garbage, repository.PreservedRawContents);
        Assert.Equal(1, repository.PreserveCallCount);
        Assert.Equal(0, repository.UpdateCalls);

        var saveComplete = service.OnSaveComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);

        service.OnSaveStart();
        Assert.Equal(2, repository.PreserveCallCount);

        repository.RawLoadFailureContents = null;
        var preLoad = service.OnPreLoad();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);
        var recovery = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, recovery.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void SameSaveFolder_mismatch_quarantines_and_variants_match()
    {
        var repository = new FakeRepository
        {
            BoundSaveFolder = Path.Combine(Path.GetTempPath(), "oc-148-other-save-folder")
        };
        var service = new Release1StoryRuntimeService(new FakeContext(), repository);
        service.OnPreLoad();

        var mismatch = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryPathMismatch, mismatch.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.LoadCallCount);

        var full = Path.GetFullPath(Path.GetTempPath());
        foreach (var variant in new[] { SwapFirstLetterCase(full), full + Path.DirectorySeparatorChar })
        {
            var matching = new FakeRepository { BoundSaveFolder = variant };
            var matchingService = new Release1StoryRuntimeService(new FakeContext(), matching);
            matchingService.OnPreLoad();

            var match = matchingService.OnLoadComplete();

            Assert.Equal(Release1StoryRuntimeRejectReason.None, match.RejectReason);
            Assert.Equal(Release1StoryRuntimePhase.Active, matchingService.Phase);
        }
    }

    [Fact]
    public void Identity_mismatch_quarantines()
    {
        var repository = new FakeRepository();
        using (var first = ActiveIntroHarness(repository))
        {
            Assert.NotNull(repository.StoredState);
        }
        var context = new FakeContext();
        context.Snapshot = context.Snapshot with { PlayerId = "76561190000000002" };
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();

        var result = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.IdentityMismatch, result.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
        Assert.Null(service.State);
    }

    [Fact]
    public void Context_pending_and_not_authoritative_do_not_quarantine_and_ready_recovers()
    {
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.Pending };
        var service = new Release1StoryRuntimeService(context, new FakeRepository());
        service.OnPreLoad();

        var pending = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.ContextPending, pending.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);

        context.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        var settling = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.NotAuthoritative, settling.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.AwaitingLoad, service.Phase);

        context.Status = Release1StoryHostContextReadStatus.Ready;
        var ready = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, ready.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Context_and_factory_faults_quarantine()
    {
        var throwingContext = new FakeContext { ThrowOnRead = true };
        var contextFaulted = new Release1StoryRuntimeService(throwingContext, new FakeRepository());
        contextFaulted.OnPreLoad();
        var contextResult = contextFaulted.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.ContextFaulted, contextResult.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, contextFaulted.Phase);

        var throwingFactory = new Release1StoryRuntimeService(
            new FakeContext(),
            (Func<string, IRelease1StoryRepository>)(_ => throw new InvalidOperationException("factory boom")));
        throwingFactory.OnPreLoad();
        var factoryResult = throwingFactory.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, factoryResult.RejectReason);
        Assert.Equal("factory boom", factoryResult.Message);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, throwingFactory.Phase);

        var nullFactory = new Release1StoryRuntimeService(
            new FakeContext(),
            (Func<string, IRelease1StoryRepository>)(_ => null!));
        nullFactory.OnPreLoad();
        var nullResult = nullFactory.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, nullResult.RejectReason);
        Assert.Equal("Repository factory returned null.", nullResult.Message);
    }

    [Fact]
    public void Repository_load_fault_and_unbound_repository_quarantine()
    {
        var repository = new FakeRepository { ThrowOnLoad = true };
        var service = new Release1StoryRuntimeService(new FakeContext(), repository);
        service.OnPreLoad();
        var loadFault = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, loadFault.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);

        var unbound = new Release1StoryRuntimeService(new FakeContext(), new UnboundRepository());
        unbound.OnPreLoad();
        var unboundResult = unbound.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryUnbound, unboundResult.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, unbound.Phase);
    }

    [Fact]
    public void SaveComplete_update_failure_and_throw_quarantine()
    {
        var failing = new FakeRepository { FailUpdates = true };
        var failingService = new Release1StoryRuntimeService(new FakeContext(), failing);
        failingService.OnPreLoad();
        failingService.OnLoadComplete();
        failingService.OnSaveStart();
        var failure = failingService.OnSaveComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.SidecarSaveFailed, failure.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, failingService.Phase);

        var throwing = new FakeRepository { ThrowOnUpdate = true };
        var throwingService = new Release1StoryRuntimeService(new FakeContext(), throwing);
        throwingService.OnPreLoad();
        throwingService.OnLoadComplete();
        throwingService.OnSaveStart();
        var thrown = throwingService.OnSaveComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, thrown.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, throwingService.Phase);
    }

    [Fact]
    public void Phone_presentation_recovery_runs_on_load_and_persists_best_effort()
    {
        var repository = new FakeRepository();
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.SmallCourtesy, 1,
            Release1TransitionKind.MissionAccepted, "release1-phone-nell-oc-148-recovery").Value;
        using (var first = ActiveIntroHarness(repository))
        {
            Assert.True(first.Service.TryAuthorizePhonePresentation(
                Release1MissionCatalog.SmallCourtesy, 1, correlation, "Nell", null).Accepted);
            Assert.True(first.Service.TryTransitionPhonePresentation(
                correlation, Release1PhonePresentationAttemptState.Attempting).Accepted);
        }
        repository.ResetEvidence();

        var context = new FakeContext();
        context.Snapshot = context.Snapshot with { LoadEpoch = 2 };
        var restored = new Release1StoryRuntimeService(context, repository);
        restored.OnPreLoad();
        var revisionBeforeLoad = repository.StoredState!.Revision;
        var result = restored.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.None, result.RejectReason);
        var recovered = restored.GetPhonePresentationAttempt(correlation);
        Assert.NotNull(recovered);
        Assert.Equal(Release1PhonePresentationAttemptState.Ambiguous, recovered!.State);
        Assert.Equal(revisionBeforeLoad + 1, restored.State!.Revision);
        Assert.Equal(restored.State.Revision, restored.LastPersistedRevision);
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Phone_presentation_recovery_keeps_the_in_memory_fix_when_persist_throws()
    {
        var repository = new FakeRepository();
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.SmallCourtesy, 1,
            Release1TransitionKind.MissionAccepted, "release1-phone-nell-oc-148-recovery-throw").Value;
        using (var first = ActiveIntroHarness(repository))
        {
            Assert.True(first.Service.TryAuthorizePhonePresentation(
                Release1MissionCatalog.SmallCourtesy, 1, correlation, "Nell", null).Accepted);
            Assert.True(first.Service.TryTransitionPhonePresentation(
                correlation, Release1PhonePresentationAttemptState.Attempting).Accepted);
        }
        repository.ResetEvidence();
        repository.ThrowOnUpdate = true;

        var context = new FakeContext();
        context.Snapshot = context.Snapshot with { LoadEpoch = 2 };
        var restored = new Release1StoryRuntimeService(context, repository);
        restored.OnPreLoad();
        var persistedBeforeLoad = repository.StoredState!.Revision;
        var result = restored.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.None, result.RejectReason);
        Assert.Equal(Release1PhonePresentationAttemptState.Ambiguous, restored.GetPhonePresentationAttempt(correlation)!.State);
        Assert.Equal(persistedBeforeLoad + 1, restored.State!.Revision);
        Assert.Equal(persistedBeforeLoad, restored.LastPersistedRevision);
    }

    [Fact]
    public void Accepted_mission_reconciles_on_load_and_deferred_mission_reoffers()
    {
        var repository = new FakeRepository();
        using (var first = ActiveIntroHarness(repository))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = SmallCourtesyCommand(first, Release1TransitionKind.MissionAccepted, "accept-before-activation-fault", terms: SmallCourtesyTerms);
            var result = first.Service.TryExecuteSmallCourtesyAcceptanceDurably(
                acceptance,
                SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[SmallCourtesyIndex()].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[SmallCourtesyIndex()].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveIntroHarness(deferring))
            Assert.True(first.Service.TryExecuteDurably(SmallCourtesyCommand(first, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[SmallCourtesyIndex()].State);
        Assert.Equal(2, reoffered.Service.State.Missions[SmallCourtesyIndex()].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void Reconcile_rejection_short_circuits_with_phase_active()
    {
        var repository = new FakeRepository();
        using (var first = ActiveIntroHarness(repository))
            Assert.True(first.Service.TryExecuteDurably(SmallCourtesyCommand(first, Release1TransitionKind.MissionDeferred, "defer-then-fail")).Accepted);
        repository.ResetEvidence();
        repository.FailUpdates = true;

        var service = new Release1StoryRuntimeService(new FakeContext(), repository);
        service.OnPreLoad();
        var result = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, service.LastLifecycleRejectReason);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public void Persisted_revision_tracks_load_commands_and_save()
    {
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(new FakeContext(), repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.Equal(-1, service.LastPersistedRevision);

        var context = new FakeContext();
        using var harness = ActiveIntroHarness(repository, context);
        Assert.Equal(harness.Service.State!.Revision, harness.Service.LastPersistedRevision);

        var mission = harness.Service.State.Missions[0];
        var drift = harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "drift-accept", "v1"));
        Assert.Equal(Release1StoryCommandStatus.Accepted, drift.Status);
        Assert.True(harness.Service.State.Revision > harness.Service.LastPersistedRevision);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(harness.Service.State.Revision, harness.Service.LastPersistedRevision);
        Assert.Same(harness.Service.State, repository.StoredState);
    }

    [Fact]
    public void Native_effect_journal_boundary_defers_until_covered()
    {
        var repository = new FakeRepository();
        using var harness = ActiveIntroHarness(repository);
        var state = harness.Service.State!;
        var mission = state.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionAccepted, "accept-effect", "v1")).Status);
        mission = harness.Service.State!.Missions[0];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionActivated, "activate-effect")).Status);
        mission = harness.Service.State!.Missions[0];
        var effect = new Release1NativeEffectJournalEntry("effect-1", Release1MissionCatalog.SmallCourtesy, mission.Attempt, "Reward", "oc", "native", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(mission, Release1TransitionKind.MissionCompleted, "complete-effect", timing: Release1CompletionTiming.OnTime, reward: "reward-effect", effect: effect)).Status);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var persistedPrepared = repository.StoredState;
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryMarkNativeEffectApplied("effect-1", "native-receipt").Status);
        var applied = harness.Service.State;
        var nextMission = harness.Service.State!.Missions[1];

        var deferred = harness.Service.TryExecuteDurably(
            Command(nextMission, Release1TransitionKind.MissionAccepted, "next-mission-after-applied", "v1"));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(applied, harness.Service.State);
        Assert.Same(persistedPrepared, repository.StoredState);
        Assert.True(harness.Service.State!.Revision > harness.Service.LastPersistedRevision);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(harness.Service.State!.Revision, harness.Service.LastPersistedRevision);

        var driftMission = harness.Service.State!.Missions[1];
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TryExecute(Command(driftMission, Release1TransitionKind.MissionAccepted, "next-accept-drift", "v1")).Status);
        Assert.True(harness.Service.State!.Revision > harness.Service.LastPersistedRevision);
        var activated = harness.Service.State!.Missions[1];
        var covered = harness.Service.TryExecuteDurably(Command(activated, Release1TransitionKind.MissionActivated, "next-activate-after-covered"));

        Assert.Equal(Release1StoryCommandStatus.Accepted, covered.Status);
        Assert.Equal(harness.Service.State!.Revision, harness.Service.LastPersistedRevision);
    }

    private static int SmallCourtesyIndex() => Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy);

    private static string SwapFirstLetterCase(string value)
    {
        var first = value[0];
        var swapped = char.IsUpper(first) ? char.ToLowerInvariant(first) : char.ToUpperInvariant(first);
        return swapped + value.Substring(1);
    }

    private static Release1StoryCommand IntroCommand(FakeContext context) => new(
        context.Snapshot.SessionEpoch,
        context.Snapshot.LoadEpoch,
        context.Snapshot.PlayerId,
        Release1MissionCatalog.IntroScopeKey,
        0,
        Release1TransitionKind.IntroAccepted,
        "intro-oc-148",
        Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-oc-148").Value);

    private static Harness ActiveIntroHarness(FakeRepository repository, FakeContext? context = null)
    {
        context ??= new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        var intro = IntroCommand(context);
        var result = service.TryExecuteDurably(intro);
        if (!result.Accepted) throw new InvalidOperationException("Intro setup failed: " + result.Message);
        repository.ResetEvidence();
        return new Harness(service, context, repository);
    }

    private static Harness LoadedHarness(FakeRepository repository)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        var loaded = service.OnLoadComplete();
        if (loaded.RejectReason != Release1StoryRuntimeRejectReason.None)
            throw new InvalidOperationException("Load setup failed: " + loaded.Message);
        return new Harness(service, context, repository);
    }

    private static Release1StoryCommand Command(
        Release1MissionRecord mission,
        Release1TransitionKind kind,
        string receipt,
        string? terms = null,
        Release1CompletionTiming? timing = null,
        string? reward = null,
        Release1NativeEffectJournalEntry? effect = null) =>
        new(
            SessionEpoch: SessionEpoch,
            LoadEpoch: 1,
            PlayerId: PlayerId,
            MissionKey: mission.MissionKey,
            Attempt: mission.Attempt,
            TransitionKind: kind,
            ReceiptId: receipt,
            CorrelationId: Release1LogicalCorrelation.Create(PlayerId, mission.MissionKey, mission.Attempt, kind, receipt).Value,
            TermsVersion: terms,
            CompletionTiming: timing,
            RewardAuthorizationReceiptId: reward,
            PreparedNativeEffect: effect);

    private static Release1StoryCommand SmallCourtesyCommand(
        Harness harness,
        Release1TransitionKind kind,
        string receipt,
        int? attempt = null,
        string? terms = null,
        double? accepted = null,
        double? deadline = null)
    {
        var mission = harness.Service.State!.Missions[SmallCourtesyIndex()];
        var commandAttempt = attempt ?? mission.Attempt;
        return new(
            harness.Context.Snapshot.SessionEpoch,
            harness.Context.Snapshot.LoadEpoch,
            harness.Context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            commandAttempt,
            kind,
            receipt,
            Release1LogicalCorrelation.Create(harness.Context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, commandAttempt, kind, receipt).Value,
            terms,
            accepted,
            deadline);
    }

    private static Release1SmallCourtesyAssignment SmallCourtesyAssignment(
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

        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        private readonly List<string>? _calls;

        public FakeContext(List<string>? calls = null) => _calls = calls;

        public Release1StoryHostContextSnapshot Snapshot { get; set; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public bool ThrowOnRead { get; set; }
        public int ReadCount { get; private set; }

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            ReadCount++;
            _calls?.Add("context");
            if (ThrowOnRead) throw new InvalidOperationException("context boom");
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository, IRelease1StoryRawContentPreservingRepository
    {
        private readonly List<string>? _calls;

        public FakeRepository(List<string>? calls = null) => _calls = calls;

        public string BoundSaveFolder { get; set; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; set; }
        public List<Release1StoryState> Updates { get; } = new();
        public int LoadCallCount { get; private set; }
        public int UpdateCalls { get; private set; }
        public int PreserveCallCount { get; private set; }
        public string? RawLoadFailureContents { get; set; }
        public string? PreservedRawContents { get; private set; }
        public bool FailUpdates { get; set; }
        public bool ThrowOnLoad { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public int? FailOnUpdateCall { get; set; }

        public Release1StoryStoreLoadResult Load()
        {
            LoadCallCount++;
            _calls?.Add("load");
            if (ThrowOnLoad) throw new InvalidOperationException("load boom");
            if (RawLoadFailureContents is not null)
                return new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.EmptyOrMalformedJson, "Fake corrupt sidecar.", RawLoadFailureContents);
            return new(
                true,
                StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
                Release1StoryStoreFailureReason.None,
                string.Empty);
        }

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            UpdateCalls++;
            _calls?.Add("update");
            if (ThrowOnUpdate) throw new InvalidOperationException("update boom");
            if (FailUpdates || FailOnUpdateCall == UpdateCalls)
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public Release1StoryStoreUpdateResult PreserveRawContents(string rawContents)
        {
            PreserveCallCount++;
            PreservedRawContents = rawContents;
            return new(true, Release1StoryStoreUpdateStatus.Updated, null, Release1StoryStoreFailureReason.None, "Fake preserved.");
        }

        public void ResetEvidence()
        {
            Updates.Clear();
            UpdateCalls = 0;
            LoadCallCount = 0;
            PreserveCallCount = 0;
            PreservedRawContents = null;
            FailUpdates = false;
            ThrowOnUpdate = false;
            FailOnUpdateCall = null;
        }
    }

    private sealed class UnboundRepository : IRelease1StoryRepository
    {
        public Release1StoryStoreLoadResult Load() =>
            new(true, Release1StoryStoreLoadStatus.Empty, Release1StorySaveEnvelope.CreateEmpty(), Release1StoryStoreFailureReason.None, string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) =>
            new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
    }
}
