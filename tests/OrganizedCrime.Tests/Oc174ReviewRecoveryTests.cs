using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc174ReviewRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Status_counts_good_entries_after_an_unreadable_entry(bool failRead)
    {
        var pool = new[] { new Entry(1), new Entry(2), new Entry(3), new Entry(4) };
        var reads = new List<long>();
        var count = OcOfficerPoolSweep.CountOwned(pool.Length,
            i => i == 1 && failRead ? throw new InvalidOperationException() : pool[i],
            e => { reads.Add(e.Pointer); return e.Pointer == 2 ? throw new InvalidOperationException() : e.Pointer is 3 or 4; });
        Assert.Equal(2, count);
        Assert.Contains(4L, reads);
    }

    [Fact]
    public void Swept_vehicle_is_released_returned_and_replaced_on_patrol_cadence()
    {
        var world = new RecoveryWorld();
        using var roster = new OcOfficerRosterService(world, "review");
        var input = new Chapter3PatrolInput(true, true, 1080d, LocalPressureTier.Critical,
            false, false, new OcOfficerPosition(50, 0, 60), 0);
        using var patrol = new Chapter3PatrolService(roster, () => input);
        void Step() { patrol.Poll(); roster.Update(); }
        // Requests at 1/11 and binds at 6/16; next structural action is poll 21.
        for (var i = 0; i < 16; i++) Step();
        var vehicle = Assert.Single(world.Inner.Bindings.Snapshot(), b => b.Duty == OcPatrolDuty.Vehicle);
        var oldSlot = Assert.Single(roster.Snapshot(), s => s.OwnerKey.Contains("Vehicle:", StringComparison.Ordinal));
        world.Inner.Native.Events.Clear();
        world.Pool.Add(new(vehicle.Officer.Pointer));
        world.Pool.Add(new(999)); // A native officer remains untouched.
        world.Pool.Add(new(vehicle.Officer.Pointer)); // Duplicate must not block release.

        roster.Update();

        Assert.DoesNotContain(roster.Snapshot(), s => s.Handle == oldSlot.Handle);
        Assert.Equal(new[] { "stop", "return", "remove" }, world.Inner.Native.Events);
        Assert.Equal(3, world.Inner.Native.FreeCars);
        Assert.Equal(999L, Assert.Single(world.Pool).Pointer);
        Assert.Contains("releasing roster slot", Assert.Single(world.Warnings));
        Assert.DoesNotContain(world.Inner.Bindings.Snapshot(), b => b.Officer == vehicle.Officer);
        for (var i = 0; i < 4; i++) Step();
        Assert.DoesNotContain(roster.Snapshot(), s => s.OwnerKey.Contains("Vehicle:", StringComparison.Ordinal));
        Step();
        var replacement = Assert.Single(roster.Snapshot(), s => s.OwnerKey.Contains("Vehicle:", StringComparison.Ordinal));
        Assert.NotEqual(oldSlot.CloneId, replacement.CloneId);
        for (var i = 0; i < Chapter3PatrolRules.RetryPolls; i++) Step();
        var bound = Assert.Single(world.Inner.Bindings.Snapshot(), b => b.Duty == OcPatrolDuty.Vehicle);
        Assert.NotEqual(vehicle.Officer.Pointer, bound.Officer.Pointer);
        Assert.Equal(2, world.Inner.Native.FreeCars);
        Assert.Single(world.Warnings);
    }

    [Fact]
    public void Failed_car_return_keeps_released_clone_as_orphan_until_normal_retry()
    {
        var world = new RecoveryWorld();
        using var roster = new OcOfficerRosterService(world, "review");
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Vehicle).Ok);
        roster.Update(); roster.Update();
        var clone = Assert.Single(world.Inner.Bindings.Snapshot()).Officer;
        world.Pool.Add(new(clone.Pointer));
        world.Inner.Native.Events.Clear();
        world.Inner.Native.FailReturn = true;
        roster.Update();
        Assert.Empty(roster.Snapshot());
        Assert.Equal(1, roster.PendingRemovalCount);
        Assert.DoesNotContain("remove", world.Inner.Native.Events);
        Assert.Equal(2, world.Inner.Native.FreeCars);
        world.Inner.Native.FailReturn = false;
        roster.Update();
        Assert.Equal(0, roster.PendingRemovalCount);
        Assert.Equal(3, world.Inner.Native.FreeCars);
        Assert.Equal(new[] { "return", "remove" }, world.Inner.Native.Events.TakeLast(2));
    }

    [Fact]
    public void Released_checkpoint_handle_requeues_at_next_game_minute()
    {
        var world = new RecoveryWorld { CheckpointBound = true };
        using var roster = new OcOfficerRosterService(world, "review");
        using var story = new Release1StoryRuntimeService(new CompositionFakeStoryContext(), new CompositionFakeRelease1StoryRepository());
        using var chief = new Release1ChiefService(story, new ChiefWorld(), new Release1ChiefTierObserver(),
            _ => null, _ => throw new NotSupportedException(),
            (out Guid session, out long load) => { session = Guid.Empty; load = 0; return false; }, LocalPressureProfile.Moderate);
        var clock = new CheckpointClock();
        using var checkpoints = new Chapter3CheckpointService(new CheckpointHost(), chief, clock, new CheckpointWorld(), roster);
        Assert.True(checkpoints.QaSpawnTestAt("review", 0, 0, 0, 0).Ok);
        checkpoints.Update(); roster.Update(); roster.Update();
        clock.Minutes++;
        checkpoints.Update(); // Bind both, then establish the throttle before release.
        Assert.All(Assert.Single(checkpoints.Snapshot().Tests).Officers,
            e => Assert.Equal(Chapter3BindState.Bound, e.BindState));
        var old = roster.Snapshot()[0];
        world.Pool.Add(new(world.Spawned[old.CloneId].Pointer));
        roster.Update();
        Assert.Single(roster.Snapshot());
        checkpoints.Update();
        Assert.Single(roster.Snapshot()); // No unthrottled replacement.
        clock.Minutes++;
        checkpoints.Update();
        var replacement = Assert.Single(roster.Snapshot(), s => !s.HasLiveClone);
        Assert.NotEqual(old.CloneId, replacement.CloneId);
        Assert.Equal(OcOfficerSlotState.Requested, replacement.State);
        roster.Update();
        Assert.All(roster.Snapshot(), s => Assert.True(s.HasLiveClone));
        Assert.Contains(Assert.Single(checkpoints.Snapshot().Tests).Officers,
            e => e.RosterHandle == replacement.Handle && e.BindState == Chapter3BindState.Binding);
    }

    [Fact]
    public void D4_capacity_excludes_world_owned_pointer_before_sweep()
    {
        var world = new RecoveryWorld();
        world.Owned.Add(2);
        world.Pool.AddRange(new[] { new Entry(2), new Entry(999) });
        var guard = new OcNativeDispatchPoolGuard();
        guard.Configure(world);
        var eligible = guard.ReadEligible(world.Pool.Count, i => world.Pool[i], e => e.Pointer, e => e.Pointer.ToString());
        Assert.Equal("999", Assert.Single(eligible));
        Assert.True(eligible.Count < 2);
        Assert.Equal(0, world.Polls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void D4_sweeps_immediately_before_native_pick_and_never_uses_owned_clone(bool failSweep)
    {
        var world = new RecoveryWorld { FailSweep = failSweep };
        world.Owned.Add(2);
        world.Pool.AddRange(new[] { new Entry(2), new Entry(999), new Entry(1000) });
        var guard = new OcNativeDispatchPoolGuard();
        guard.Configure(world);
        var picked = new List<long>();
        var error = Record.Exception(() => guard.Dispatch(() => world.Pool.Count, i => world.Pool[i], e => e.Pointer,
            () => { Assert.Equal(1, world.Polls); picked.AddRange(world.Pool.Take(2).Select(e => e.Pointer)); }));
        if (failSweep) { Assert.IsType<InvalidOperationException>(error); Assert.Empty(picked); }
        else { Assert.Null(error); Assert.Equal(new long[] { 999, 1000 }, picked); }
        Assert.DoesNotContain(2L, picked);
        Assert.Equal(1, world.Polls);
    }

    [Fact]
    public void D4_unreadable_pool_entry_fails_closed_without_native_pick()
    {
        var world = new RecoveryWorld();
        var guard = new OcNativeDispatchPoolGuard();
        guard.Configure(world);
        var called = false;
        Assert.Throws<InvalidOperationException>(() => guard.Dispatch<Entry>(() => 1,
            _ => throw new InvalidOperationException(), e => e.Pointer, () => called = true));
        Assert.False(called);
    }

    [Fact]
    public void Production_wires_same_world_to_release_status_and_D4_guard()
    {
        var root = Root();
        var law = File.ReadAllText(Path.Combine(root, "tools/OrganizedCrime/Runtime/Composition/LawModule.cs"));
        Assert.Contains("dispatch.ConfigureOfficerPools(root.OfficerRoster?.OfficerPools)", law);
        var boundary = File.ReadAllText(Path.Combine(root, "tools/OrganizedCrime/Runtime/NativePoliceDispatchAdapter.cs"));
        Assert.Contains("PoolGuard.ReadEligible(pool.Count", boundary);
        Assert.Contains("PoolGuard.Dispatch(() => station.OfficerPool.Count", boundary);
        var native = File.ReadAllText(Path.Combine(root, "tools/OrganizedCrime/Runtime/Police/Il2CppOcPatrolNative.cs"));
        Assert.Contains("OcOfficerPoolSweep.CountOwned(pool.Count", native);
        Assert.Contains("PoolRelease?.Invoke(pointer)", native);
        var world = File.ReadAllText(Path.Combine(root, "tools/OrganizedCrime/Runtime/Il2CppOcOfficerWorld.cs"));
        Assert.Contains("IsOwnedOfficer(long pointer) => _context._ownPointers.Contains(pointer)", world);
    }

    private sealed record Entry(long Pointer);
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tools/OrganizedCrime/Mod.cs"))) return dir.FullName;
        throw new InvalidOperationException("No repo root.");
    }

    private sealed class RecoveryWorld : IOcOfficerWorld, IOcPatrolWorld, IOcOfficerPoolRecoveryWorld
    {
        internal readonly Chapter3PatrolServiceTests.World Inner = new();
        internal readonly List<Entry> Pool = new();
        internal readonly HashSet<long> Owned = new();
        internal readonly Dictionary<string, OcOfficerCloneRef> Spawned = new();
        internal readonly List<string> Warnings = new();
        private readonly OcOfficerPoolSweep _sweep;
        private Action<long>? _release;
        internal int Polls;
        internal bool FailSweep;
        internal bool CheckpointBound;
        internal RecoveryWorld() => _sweep = new(Warnings.Add);
        public bool IsOwnedOfficer(long pointer) => Owned.Contains(pointer);
        public void SetPoolRelease(Action<long> release) => _release = release;
        public void PollOfficerPools()
        {
            Polls++;
            if (!IsHost || FailSweep) return;
            var removed = new HashSet<long>();
            _sweep.RemoveOwned(true, "west", Pool.ToArray(), e => IsOwnedOfficer(e.Pointer),
                e => Spawned.FirstOrDefault(s => s.Value.Pointer == e.Pointer).Key ?? "oc-test",
                Pool.RemoveAt, e => removed.Add(e.Pointer));
            foreach (var pointer in removed) _release?.Invoke(pointer);
        }
        public void ResetOfficerPoolWarnings() => _sweep.ResetWarnings();
        public bool IsHost => Inner.IsHost;
        public OcOfficerWorldStatus TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state) => Inner.TryReadPatrolState(clone, out state);
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) => Inner.TryBindPatrol(clone, duty, target, out detail);
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => Inner.TryUnbindPatrol(clone, out detail);
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Inner.PatrolSnapshot();
        public void ClearPatrols() => Inner.Bindings.ClearPatrols();
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => Inner.TryReadGameMinutes(out minutes);
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) => Inner.TryEnsureTemplate(id, out status, out detail);
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail)
        {
            var status = Inner.TrySpawnClone(position, id, out clone, out detail);
            if (status == OcOfficerWorldStatus.Ok) { Owned.Add(clone.Pointer); Spawned[id] = clone; }
            return status;
        }
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => Inner.TryReadKnockedOut(clone, out knockedOut);
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => Inner.TryReadClonePosition(clone, out position);
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint)
        { active = CheckpointBound ? "Checkpoint" : "patrol"; checkpoint = CheckpointBound; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state)
        { state = CheckpointBound ? new(true, true, OcCheckpointKind.Ours, default(OcOfficerPosition)) : new(false, false, OcCheckpointKind.None, null); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) => Inner.TryReadBehaviourDiagnostics(clone, checkpoint, out diagnostics);
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) => Inner.TryBindToCheckpoint(clone, checkpoint, stand, out detail);
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            var status = Inner.TryRemoveClone(clone, out detail);
            if (status == OcOfficerWorldStatus.Ok) Owned.Remove(clone.Pointer);
            return status;
        }
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => Inner.TryDestroyTemplate(out detail);
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) => Inner.TryClearSwapMap(out detail);
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => Inner.TryUnmapClone(clone, out detail);
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) => Inner.TryReadNativeLeak(label, out leaked, out detail);
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => Inner.TryReadTemplateStatus(out status);
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => Inner.TryReadNativeCounts(out counts);
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) => Inner.TryReadPlayerPose(out position, out yaw);
    }

    private sealed class ChiefWorld : Release1ChiefWorldStub { }
    private sealed class CheckpointHost : IChapter3CheckpointHost
    {
        public Chapter3State? State => null;
        public Chapter3CheckpointTarget ReadCheckpointTarget() => new(null, null);
        public bool TryWriteCheckpointTarget(Chapter3CheckpointTarget target) => false;
    }
    private sealed class CheckpointClock : Release1ProductionMissionWorldStub
    {
        internal double Minutes = 10;
        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        { activity = null!; return Release1SmallCourtesyWorldReadStatus.Unavailable; }
        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double minutes)
        { minutes = Minutes; return Release1SmallCourtesyWorldReadStatus.Ready; }
    }
    private sealed class CheckpointWorld : IChapter3CheckpointWorld
    {
        public bool IsHost => true;
        public Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions)
        { positions = Array.Empty<Chapter3OwnedPropertyPosition>(); return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode)
        { propertyCode = null; return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions)
        { positions = Array.Empty<Chapter3NativeCheckpointPosition>(); return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yaw, out string? handle)
        { handle = "checkpoint"; return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle) => Chapter3CheckpointWorldStatus.Ok;
        public Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? native)
        { native = this; return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled) => Chapter3CheckpointWorldStatus.Ok;
        public Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose)
        { pose = new(0, 0, 0, 0); return Chapter3CheckpointWorldStatus.Ok; }
        public Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points)
        { points = new[] { new Chapter3StandPoint(0, 0, 0) }; return Chapter3CheckpointWorldStatus.Ok; }
    }
}
