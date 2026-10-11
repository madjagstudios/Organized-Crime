using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148 characterization: the Chapter 3 save/load lifecycle call sequence and
// repeat/failure behaviour. Pinned before SaveLifecycle replaces the Chapter 3
// and Release 1 Story skeletons; passes unchanged after.
public sealed class Chapter3LifecycleSequenceTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Happy_path_reads_context_then_loads_then_updates_once_at_save_complete()
    {
        var calls = new List<string>();
        var context = new FakeContext(calls);
        var repository = new FakeRepository(calls);
        var service = new Chapter3RuntimeService(context, repository, new FakeGate(), new FakeClock());

        var preLoad = service.OnPreLoad();
        var load = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
        var stateAtSaveStart = service.State;
        Assert.NotNull(stateAtSaveStart);
        var saveStart = service.OnSaveStart();
        var saveComplete = service.OnSaveComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.None, load.RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveStart.RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal(new[] { "context", "load", "update" }, calls);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Same(stateAtSaveStart, repository.UpdatedState);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Repeat_calls_are_idempotent_noops()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Chapter3RuntimeService(context, repository, new FakeGate(), new FakeClock());

        var preLoad = service.OnPreLoad();
        var preLoadRepeat = service.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal("PreLoad was already handled.", preLoadRepeat.Message);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);

        var load = service.OnLoadComplete();
        var loadRepeat = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, load.RejectReason);
        Assert.Equal("LoadComplete was already handled.", loadRepeat.Message);
        Assert.Equal(1, context.ReadCount);
        Assert.Equal(1, repository.LoadCallCount);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);

        var saveStart = service.OnSaveStart();
        var saveStartRepeat = service.OnSaveStart();
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveStart.RejectReason);
        Assert.Equal("SaveStart was already handled.", saveStartRepeat.Message);
        Assert.Equal(Chapter3RuntimePhase.Saving, service.Phase);

        var saveComplete = service.OnSaveComplete();
        var saveCompleteRepeat = service.OnSaveComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal("SaveComplete was a lifecycle no-op.", saveCompleteRepeat.Message);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Save_complete_without_save_start_is_a_noop_and_save_start_while_detached_is_inactive()
    {
        var repository = new FakeRepository();
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());

        var saveComplete = service.OnSaveComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal("SaveComplete was a lifecycle no-op.", saveComplete.Message);
        Assert.Equal(Chapter3RuntimePhase.Detached, service.Phase);
        Assert.Equal(0, repository.UpdateCallCount);

        var saveStart = service.OnSaveStart();
        Assert.Equal(Chapter3RuntimeRejectReason.Inactive, saveStart.RejectReason);
        Assert.Equal("Only an active Chapter 3 can save.", saveStart.Message);
        Assert.Equal(Chapter3RuntimePhase.Detached, service.Phase);
    }

    [Fact]
    public void Dispose_makes_every_call_return_disposed_and_second_dispose_is_silent()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Chapter3RuntimeService(context, repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();
        service.OnLoadComplete();

        service.Dispose();
        service.Dispose();

        Assert.Equal(Chapter3RuntimePhase.Disposed, service.Phase);
        Assert.Equal(Chapter3RuntimeRejectReason.Disposed, service.OnPreLoad().RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.Disposed, service.OnLoadComplete().RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.Disposed, service.OnSaveStart().RejectReason);
        Assert.Equal(Chapter3RuntimeRejectReason.Disposed, service.OnSaveComplete().RejectReason);
        Assert.Equal(1, context.ReadCount);
        Assert.Equal(1, repository.LoadCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public void Arthur_is_armed_before_the_repeated_load_guard()
    {
        var context = new FakeContext();
        var service = new Chapter3RuntimeService(context, new FakeRepository(), new FakeGate(), new FakeClock())
        {
            ArthurWorld = new FakeArthurWorld()
        };
        service.OnPreLoad();
        service.OnLoadComplete();
        var pump = ArthurPump(service);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.True(pump.BeginCome());
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
        var readsBeforeRepeat = context.ReadCount;

        var repeat = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.None, repeat.RejectReason);
        Assert.Equal("LoadComplete was already handled.", repeat.Message);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Equal(readsBeforeRepeat, context.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_load_and_poll_keep_reconcile_pending_for_later_authority(bool alreadyArmed)
    {
        var world = new FakeArthurWorld { HasAuthority = false };
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.NotAuthoritative };
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(context, new FakeRepository(), new FakeGate(), new FakeClock(), logs.Add)
        {
            ArthurWorld = world
        };
        service.OnPreLoad();
        var pump = ArthurPump(service);
        if (alreadyArmed) pump.BeginAfterLoad();

        Assert.Equal(Chapter3RuntimeRejectReason.NotAuthoritative, service.OnLoadComplete().RejectReason);
        Assert.True(ReconcilePending(pump));
        service.PollArthurVisit();
        Assert.True(ReconcilePending(pump));
        Assert.Equal(0, world.Reads);
        Assert.Equal(0, world.Mutations);
        Assert.Equal(0, world.ImpactPolls);
        Assert.Empty(logs);

        world.HasAuthority = true;
        service.PollArthurVisit();
        Assert.False(ReconcilePending(pump));
        Assert.Equal(1, world.Reads);
        Assert.Equal(3, world.Mutations);
        Assert.Equal(1, world.Parks);

        context.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.Equal(Chapter3RuntimeRejectReason.None, service.OnLoadComplete().RejectReason);
        Assert.True(ReconcilePending(pump));
        service.PollArthurVisit();
        Assert.Equal(2, world.Reads);
        Assert.Equal(6, world.Mutations); // appearance check, duplicate removal, park per load
        Assert.Equal(2, world.Parks);
        Assert.False(ReconcilePending(pump));
    }

    [Fact]
    public void Client_poll_keeps_an_arm_pending_without_any_pump_or_visit_side_effects()
    {
        var world = new FakeArthurWorld { HasAuthority = false };
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock());
        var pump = ArthurPump(service);
        pump.BeginAfterLoad();

        service.PollArthurVisit(); // A missing world also waits without cancelling.
        Assert.True(ReconcilePending(pump));
        service.ArthurWorld = world;
        service.PollArthurVisit();

        Assert.True(ReconcilePending(pump));
        Assert.Equal(0, world.Reads);
        Assert.Equal(0, world.Mutations);
        Assert.Equal(0, world.Parks);
        Assert.Equal(0, world.ImpactPolls);
        world.HasAuthority = true;
        service.PollArthurVisit();
        Assert.False(ReconcilePending(pump));
        Assert.Equal(1, world.Reads);
        Assert.Equal(3, world.Mutations);
        Assert.Equal(1, world.Parks);
    }

    [Fact]
    public void Solo_load_complete_before_arthur_authority_reconciles_on_first_authoritative_poll()
    {
        var world = new FakeArthurWorld { HasAuthority = false };
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock(), logs.Add)
        {
            ArthurWorld = world
        };
        service.OnPreLoad();

        Assert.Equal(Chapter3RuntimeRejectReason.None, service.OnLoadComplete().RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
        var pump = ArthurPump(service);
        Assert.True(ReconcilePending(pump));
        service.PollArthurVisit();
        Assert.True(ReconcilePending(pump));
        Assert.Equal(0, world.Reads);
        Assert.Equal(0, world.Mutations);
        Assert.Equal(0, world.Parks);
        Assert.Equal(0, world.ImpactPolls);
        Assert.Empty(logs);

        world.HasAuthority = true;
        service.PollArthurVisit();

        Assert.False(ReconcilePending(pump));
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Equal(1, world.Reads);
        Assert.Equal(3, world.Mutations);
        Assert.Equal(1, world.Parks);
        Assert.Contains(logs, line => line.StartsWith("Chapter 3 Arthur visit Parked:", StringComparison.Ordinal));
    }

    private static bool ReconcilePending(Chapter3ArthurVisitPump pump) =>
        (bool)typeof(Chapter3ArthurVisitPump)
            .GetField("_reconcilePending", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pump)!;

    [Fact]
    public void Disposed_service_does_not_arm_arthur()
    {
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock());
        service.OnPreLoad();
        service.OnLoadComplete();
        var pump = ArthurPump(service);
        Assert.True(pump.BeginCome());

        service.Dispose();
        var result = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.Disposed, result.RejectReason);
        Assert.Equal(Chapter3ArthurVisitState.Coming, pump.State);
    }

    [Fact]
    public void PreLoad_aborts_a_beating_before_state_clears()
    {
        var logs = new List<string>();
        var service = new Chapter3RuntimeService(new FakeContext(), new FakeRepository(), new FakeGate(), new FakeClock(), logs.Add);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.NotNull(service.State);
        var world = new FakeBeatingWorld();
        Chapter3State? stateDuringAbort = null;
        var unlocks = 0;
        world.OnUnlock = () => { unlocks++; stateDuringAbort = service.State; };
        var beating = new Chapter3ArthurBeating(
            world,
            new FakeArthurWorld(),
            new FakeBeatingHq(),
            Chapter3BeatingKind.Attack,
            parkArthur: true,
            Chapter3ArthurVisitState.Parked,
            startedAt: 0f,
            logs.Add,
            record: () => { },
            noteNell: () => { },
            parkArthurAction: () => { });
        Context(service).Beating = beating;

        var result = service.OnPreLoad();

        Assert.Equal(Chapter3RuntimeRejectReason.None, result.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);
        Assert.Null(service.State);
        Assert.Equal(1, unlocks);
        Assert.NotNull(stateDuringAbort);
        Assert.Contains(logs, line => line == "Chapter 3 Arthur beating aborted: the scene is unloading.");
    }

    [Fact]
    public void Quarantined_raw_contents_are_preserved_across_save_start_without_an_update()
    {
        const string garbage = "{ this is not valid chapter 3 json";
        var repository = new FakeRepository { LoadFailureContents = garbage };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());

        service.OnPreLoad();
        var load = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.SidecarLoadFailed, load.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);

        var saveStart = service.OnSaveStart();
        Assert.Equal(Chapter3RuntimeRejectReason.Quarantined, saveStart.RejectReason);
        Assert.Equal(garbage, repository.PreservedRawContents);
        Assert.Equal(1, repository.PreserveCallCount);
        Assert.Equal(0, repository.UpdateCallCount);

        var saveComplete = service.OnSaveComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, saveComplete.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.UpdateCallCount);

        service.OnSaveStart();
        Assert.Equal(2, repository.PreserveCallCount);

        repository.LoadFailureContents = null;
        var preLoad = service.OnPreLoad();
        Assert.Equal(Chapter3RuntimeRejectReason.None, preLoad.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);
        var recovery = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, recovery.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void SameSaveFolder_mismatch_quarantines_and_case_variants_match()
    {
        var repository = new FakeRepository
        {
            BoundSaveFolder = Path.Combine(Path.GetTempPath(), "oc-148-other-save-folder")
        };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var mismatch = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryPathMismatch, mismatch.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);
        Assert.Equal(0, repository.LoadCallCount);

        var cased = SwapFirstLetterCase(Path.GetFullPath(Path.GetTempPath()));
        var matching = new FakeRepository { BoundSaveFolder = cased };
        var matchingService = new Chapter3RuntimeService(new FakeContext(), matching, new FakeGate(), new FakeClock());
        matchingService.OnPreLoad();

        var match = matchingService.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.None, match.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, matchingService.Phase);
    }

    [Fact]
    public void Identity_mismatch_quarantines()
    {
        var repository = new FakeRepository
        {
            Stored = new Chapter3State("76561190000000002", Chapter3Phase.NotStarted, null, 11, 5)
        };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var result = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.IdentityMismatch, result.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);
        Assert.Null(service.State);
    }

    [Fact]
    public void Context_pending_and_not_authoritative_do_not_quarantine_and_ready_recovers()
    {
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.Pending };
        var service = new Chapter3RuntimeService(context, new FakeRepository(), new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var pending = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.ContextPending, pending.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);

        context.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        var settling = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.NotAuthoritative, settling.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.AwaitingLoad, service.Phase);

        context.Status = Release1StoryHostContextReadStatus.Ready;
        var ready = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.None, ready.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }

    [Fact]
    public void Context_and_factory_faults_quarantine()
    {
        var throwingContext = new FakeContext { ThrowOnRead = true };
        var contextFaulted = new Chapter3RuntimeService(throwingContext, new FakeRepository(), new FakeGate(), new FakeClock());
        contextFaulted.OnPreLoad();
        var contextResult = contextFaulted.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.ContextFaulted, contextResult.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, contextFaulted.Phase);

        var throwingFactory = new Chapter3RuntimeService(
            new FakeContext(),
            (Func<string, IChapter3Repository>)(_ => throw new InvalidOperationException("factory boom")),
            new FakeGate(),
            new FakeClock());
        throwingFactory.OnPreLoad();
        var factoryResult = throwingFactory.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryFaulted, factoryResult.RejectReason);
        Assert.Equal("factory boom", factoryResult.Message);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, throwingFactory.Phase);

        var nullFactory = new Chapter3RuntimeService(
            new FakeContext(),
            (Func<string, IChapter3Repository>)(_ => null!),
            new FakeGate(),
            new FakeClock());
        nullFactory.OnPreLoad();
        var nullResult = nullFactory.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryFaulted, nullResult.RejectReason);
        Assert.Equal("Repository factory returned null.", nullResult.Message);
    }

    [Fact]
    public void Repository_load_fault_and_unbound_repository_quarantine()
    {
        var repository = new FakeRepository { ThrowOnLoad = true };
        var service = new Chapter3RuntimeService(new FakeContext(), repository, new FakeGate(), new FakeClock());
        service.OnPreLoad();
        var loadFault = service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryFaulted, loadFault.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);

        var unbound = new Chapter3RuntimeService(new FakeContext(), new UnboundRepository(), new FakeGate(), new FakeClock());
        unbound.OnPreLoad();
        var unboundResult = unbound.OnLoadComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryUnbound, unboundResult.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, unbound.Phase);
    }

    [Fact]
    public void SaveComplete_update_failure_and_throw_quarantine()
    {
        var failing = new FakeRepository { FailUpdates = true };
        var failingService = new Chapter3RuntimeService(new FakeContext(), failing, new FakeGate(), new FakeClock());
        failingService.OnPreLoad();
        failingService.OnLoadComplete();
        failingService.OnSaveStart();
        var failure = failingService.OnSaveComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.SidecarSaveFailed, failure.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, failingService.Phase);

        var throwing = new FakeRepository { ThrowOnUpdate = true };
        var throwingService = new Chapter3RuntimeService(new FakeContext(), throwing, new FakeGate(), new FakeClock());
        throwingService.OnPreLoad();
        throwingService.OnLoadComplete();
        throwingService.OnSaveStart();
        var thrown = throwingService.OnSaveComplete();
        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryFaulted, thrown.RejectReason);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, throwingService.Phase);
    }

    private static Chapter3ArthurVisitPump ArthurPump(Chapter3RuntimeService service) =>
        Context(service).ArthurPump;

    private static Chapter3FeatureContext Context(Chapter3RuntimeService service) =>
        (Chapter3FeatureContext)typeof(Chapter3RuntimeService)
            .GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(service)!;

    private static string SwapFirstLetterCase(string value)
    {
        var first = value[0];
        var swapped = char.IsUpper(first) ? char.ToLowerInvariant(first) : char.ToUpperInvariant(first);
        return swapped + value.Substring(1);
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        private readonly List<string>? _calls;

        public FakeContext(List<string>? calls = null) => _calls = calls;

        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public bool ThrowOnRead { get; set; }
        public int ReadCount { get; private set; }
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, PlayerId, Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            ReadCount++;
            _calls?.Add("context");
            if (ThrowOnRead) throw new InvalidOperationException("context boom");
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        private readonly List<string>? _calls;

        public FakeRepository(List<string>? calls = null) => _calls = calls;

        public string BoundSaveFolder { get; set; } = Path.GetFullPath(Path.GetTempPath());
        public Chapter3State? Stored { get; set; }
        public string? LoadFailureContents { get; set; }
        public bool ThrowOnLoad { get; set; }
        public bool FailUpdates { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public int LoadCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int PreserveCallCount { get; private set; }
        public Chapter3State? UpdatedState { get; private set; }
        public string? PreservedRawContents { get; private set; }

        public Chapter3StoreLoadResult Load()
        {
            LoadCallCount++;
            _calls?.Add("load");
            if (ThrowOnLoad) throw new InvalidOperationException("load boom");
            if (LoadFailureContents is not null)
                return new(false, Chapter3StoreLoadStatus.Failed, null, Chapter3StoreFailureReason.InvalidState, "Fake corrupt sidecar.", LoadFailureContents);
            return Stored is null
                ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
                : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");
        }

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            UpdateCallCount++;
            _calls?.Add("update");
            UpdatedState = state;
            if (ThrowOnUpdate) throw new InvalidOperationException("update boom");
            if (FailUpdates)
                return new(false, Chapter3StoreUpdateStatus.Rejected, null, Chapter3StoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents)
        {
            PreserveCallCount++;
            PreservedRawContents = rawContents;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
        }
    }

    private sealed class UnboundRepository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() =>
            new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; } = true;
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    #pragma warning disable CS0067
    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; } = 11;
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return true;
        }
    }

    private sealed class FakeBeatingWorld : IChapter3BeatingWorld
    {
        public Action? OnUnlock { get; set; }
        public bool IsHost => true;
        public bool IsPlayerSpawned => true;

        public bool TryReadHealth(out float health, out float maxHealth, out string reason)
        {
            health = 100f;
            maxHealth = 100f;
            reason = string.Empty;
            return true;
        }

        public bool TrySetHealth(float health, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryLock(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryUnlock(out string reason)
        {
            OnUnlock?.Invoke();
            reason = string.Empty;
            return true;
        }

        public bool TryFadeOut(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryFadeIn(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryJoltCamera(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryMoveToWake(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason)
        {
            wokeAt = (0f, 0f, 0f);
            propertyCode = string.Empty;
            reason = string.Empty;
            return true;
        }

        public bool TryOpenBill(out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    private sealed class FakeBeatingHq : IChapter3BeatingHq
    {
        public bool PlayerInsideHq => false;

        public bool TryExit(out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    private sealed class FakeArthurWorld : IChapter3ArthurWorld
    {
        public bool HasAuthority { get; set; } = true;
        public int Reads;
        public int Mutations;
        public int Parks;
        public int ImpactPolls;

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            Reads++;
            snapshot = new Chapter3ArthurSnapshot(true, true, true, false, true, (5f, 0f, 5f), 7f, 3);
            return Chapter3ArthurReadStatus.Ready;
        }

        public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
        {
            Mutations++;
            reason = string.Empty;
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryParkArthur(out string reason)
        {
            Parks++;
            Mutations++;
            reason = string.Empty;
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason)
        {
            Mutations++;
            reason = string.Empty;
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryComeArthur(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
        {
            local = (0f, 0f, 0f);
            yawDegrees = 0f;
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryResumeWalk(out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason)
        {
            Mutations++;
            reason = string.Empty;
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason)
        {
            reason = string.Empty;
            return default;
        }

        public bool PollArthurImpact()
        {
            ImpactPolls++;
            return false;
        }
    }
}
