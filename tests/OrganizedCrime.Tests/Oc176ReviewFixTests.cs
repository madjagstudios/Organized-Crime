using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc176ReviewFixTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public void Copy_selection_strips_only_live_owned_equipment_with_stale_avatar(
        bool owned, bool alive, bool bound, bool stripped)
    {
        var result = OcOfficerCloneLifetime.StaleCopies(new[] { 1 }, _ => owned, _ => alive, _ => bound);
        Assert.Equal(stripped, result.Count == 1);
    }

    [Fact]
    public void Bound_equipment_is_retained_and_native_equipment_is_never_inspected()
    {
        var items = new[] { "native", "clone flashlight", "clone baton", "clone gun", "stale copy" };
        var result = OcOfficerCloneLifetime.StaleCopies(items, item => item != "native", _ => true, item =>
        {
            Assert.NotEqual("native", item);
            return item != "stale copy";
        });
        Assert.Equal(new[] { "stale copy" }, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Throwing_storage_scan_cannot_publish_partial_guids(bool previous)
    {
        var cache = new Dictionary<string, HashSet<Guid>>();
        var old = Guid.NewGuid(); var partial = Guid.NewGuid();
        if (previous) cache["property"] = new() { old };
        Assert.Throws<InvalidOperationException>(() => Chapter3RaidScanCache.ScanGuids<int>(cache, "property", seen =>
        {
            seen.Add(partial);
            throw new InvalidOperationException("second storage unreadable");
        }));
        Assert.Equal(previous, cache.ContainsKey("property"));
        if (previous) Assert.Equal(new[] { old }, cache["property"]);
        Assert.DoesNotContain(cache.Values, guids => guids.Contains(partial));
        Assert.Equal(2, Chapter3RaidScanCache.ScanGuids(cache, "property", seen => { seen.Add(partial); return 2; }));
        Assert.Equal(new[] { partial }, cache["property"]);
    }

    [Fact]
    public void Dependency_warnings_are_once_per_clone_and_problem_with_observation_counts()
    {
        var warnings = new OcOfficerDependencyWarnings();
        var clone = new OcOfficerCloneRef(null, 1, 1);
        var other = new OcOfficerCloneRef(null, 2, 2);
        var logged = new List<string>();
        for (var scan = 0; scan < 6; scan++)
            warnings.Observe(clone, new[] { "movement.agent", "movement.agent" }, logged.Add);
        Assert.Single(logged);
        Assert.Equal("dependency warnings=1; observations=6", warnings.Status(clone));
        warnings.Observe(clone, new[] { "equippable.Light@3" }, logged.Add);
        warnings.Observe(other, new[] { "movement.agent" }, logged.Add);
        Assert.Equal(3, logged.Count);
        Assert.Equal("dependency warnings=2; observations=7", warnings.Status(clone));
        warnings.Reset();
        Assert.Equal("dependency warnings=0; observations=0", warnings.Status(clone));
        warnings.Observe(clone, new[] { "movement.agent" }, logged.Add);
        Assert.Equal(4, logged.Count);
    }

    [Fact]
    public void Logger_failure_is_counted_once_and_reused_pointer_is_a_new_clone()
    {
        var warnings = new OcOfficerDependencyWarnings(); var calls = 0;
        void Log(string _) { calls++; throw new InvalidOperationException(); }
        warnings.Observe(new(null, 1, 1), new[] { "avatar" }, Log);
        warnings.Observe(new(null, 1, 1), new[] { "avatar" }, Log);
        warnings.Observe(new(null, 1, 2), new[] { "avatar" }, Log);
        Assert.Equal(2, calls);
        Assert.Empty(OcOfficerDependencyWarnings.Problems("dependencies: ok; active=True; GUID=test"));
        Assert.Equal(new[] { "movement.agent", "equippable.Light@1" },
            OcOfficerDependencyWarnings.Problems("dependencies: missing=movement.agent,equippable.Light@1; active=True; GUID=test"));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Spawn_despawn_cycles_leave_no_oc_guids_and_never_remove_native_guids(int cycles)
    {
        var ownership = new OcOfficerGuidOwnership();
        var native = Guid.NewGuid();
        var registered = new HashSet<Guid> { native };
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var guid = Guid.Parse(OcOfficerCloneLifetime.NewGuid());
            ownership.Track(1, guid); registered.Add(guid);
            ownership.Release(99, (id, _) => registered.Remove(id));
            Assert.Contains(guid, registered);
            ownership.Release(1, (id, _) => registered.Remove(id));
            ownership.Release(1, (_, _) => throw new InvalidOperationException("must be idempotent"));
            Assert.Equal(0, ownership.Count);
            Assert.Equal(new[] { native }, registered);
        }
    }

    [Fact]
    public void All_owned_guids_are_cleared_without_a_live_officer_and_failed_cleanup_is_retried()
    {
        var ownership = new OcOfficerGuidOwnership();
        var registered = new HashSet<Guid>();
        var throwing = true;
        for (var pointer = 1; pointer <= 3; pointer++)
        {
            var guid = Guid.NewGuid();
            ownership.Track(pointer, guid); registered.Add(guid);
        }
        void Remove(Guid guid, long pointer)
        {
            if (pointer == 2 && throwing) throw new InvalidOperationException("native manager unavailable");
            registered.Remove(guid);
        }
        ownership.Clear(Remove);
        Assert.Single(registered); Assert.Equal(1, ownership.Count);
        throwing = false; ownership.Clear(Remove); ownership.Clear(Remove);
        Assert.Empty(registered); Assert.Equal(0, ownership.Count);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Missing_root_cleanup_releases_ownership_even_when_native_cleanup_throws(int fail)
    {
        var events = new List<int>(); var forgotten = false;
        void Step(int step) { events.Add(step); if (step == fail) throw new InvalidOperationException(); }
        OcOfficerCloneLifetime.RetireMissingRoot(() => Step(0), () => Step(1), () => Step(2), () => forgotten = true);
        Assert.True(forgotten); Assert.Equal(new[] { 0, 1, 2 }, events);
    }

    [Fact]
    public void Missing_root_release_drops_slot_orphan_and_pending_qa_work()
    {
        var world = new LifetimeWorld { MissingRoot = true };
        using var roster = new OcOfficerRosterService(world, "missing-root");
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Foot).Ok);
        roster.Update();
        var slot = Assert.Single(roster.Snapshot());
        Assert.Contains(roster.QaPatrolStatus(), line => line.Contains("waiting QA binds 1"));
        Assert.True(roster.Release(slot.Handle));
        Assert.Empty(roster.Snapshot()); Assert.Equal(0, roster.PendingRemovalCount);
        Assert.Contains(roster.QaPatrolStatus(), line => line.Contains("waiting QA binds 0"));
        roster.Update();
        Assert.Equal(1, world.Inner.Spawns); Assert.Empty(world.Registered);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("release")]
    [InlineData("pool sweep")]
    [InlineData("scene unload")]
    [InlineData("no server")]
    [InlineData("dispose")]
    public void Roster_removal_boundaries_leave_no_oc_guids_after_repeated_cycles(string boundary)
    {
        var world = new LifetimeWorld();
        for (var cycle = 0; cycle < 10; cycle++)
        {
            using var roster = new OcOfficerRosterService(world, $"guids-{cycle}");
            world.Inner.Native.IsHost = true;
            Assert.Equal(OcOfficerRequestStatus.Accepted, roster.TryRequest("owner", default, out var handle));
            roster.Update(); Assert.Single(world.Registered);
            switch (boundary)
            {
                case "save": roster.OnSaveStart(); break;
                case "release": roster.Release(handle); break;
                case "pool sweep": world.PoolRelease!(world.LastClone.Pointer); break;
                case "scene unload": roster.OnPreLoad(); break;
                case "no server": world.Inner.Native.IsHost = false; roster.OnPreLoad(); break;
                case "dispose": roster.Dispose(); break;
            }
            Assert.Empty(world.Registered); Assert.Equal(0, world.Guids.Count);
        }
    }

    private sealed class LifetimeWorld : IOcOfficerWorld, IOcOfficerGuidWorld, IOcOfficerPoolRecoveryWorld, IOcPatrolWorld
    {
        internal readonly Chapter3PatrolServiceTests.World Inner = new();
        internal readonly OcOfficerGuidOwnership Guids = new();
        internal readonly HashSet<Guid> Registered = new();
        internal bool MissingRoot;
        internal OcOfficerCloneRef LastClone;
        internal Action<long>? PoolRelease;
        public void ClearOwnedGuids() => Guids.Clear((guid, _) => Registered.Remove(guid));
        public bool IsOwnedOfficer(long pointer) => LastClone.Pointer == pointer;
        public void SetPoolRelease(Action<long> release) => PoolRelease = release;
        public void PollOfficerPools() { }
        public void ResetOfficerPoolWarnings() { }
        public bool IsHost => Inner.IsHost;
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => Inner.TryReadGameMinutes(out minutes);
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) => Inner.TryEnsureTemplate(id, out status, out detail);
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail)
        {
            var status = Inner.TrySpawnClone(position, id, out clone, out detail);
            LastClone = clone;
            var guid = Guid.Parse(OcOfficerCloneLifetime.NewGuid());
            Guids.Track(clone.Pointer, guid); Registered.Add(guid);
            return status;
        }
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            Guids.Release(clone.Pointer, (guid, _) => Registered.Remove(guid));
            if (MissingRoot)
            {
                OcOfficerCloneLifetime.RetireMissingRoot(() => { }, () => { }, () => { }, () => LastClone = default);
                detail = "retained root unavailable; ownership released";
                return OcOfficerWorldStatus.InvalidHandle;
            }
            return Inner.TryRemoveClone(clone, out detail);
        }
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => Inner.TryReadKnockedOut(clone, out knockedOut);
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => Inner.TryReadClonePosition(clone, out position);
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint) => Inner.TryReadBehaviourState(clone, out active, out checkpoint);
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state) => Inner.TryReadBoundState(clone, checkpoint, out state);
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) => Inner.TryReadBehaviourDiagnostics(clone, checkpoint, out diagnostics);
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) => Inner.TryBindToCheckpoint(clone, checkpoint, stand, out detail);
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => Inner.TryDestroyTemplate(out detail);
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) => Inner.TryClearSwapMap(out detail);
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => Inner.TryUnmapClone(clone, out detail);
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) => Inner.TryReadNativeLeak(label, out leaked, out detail);
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => Inner.TryReadTemplateStatus(out status);
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => Inner.TryReadNativeCounts(out counts);
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) => Inner.TryReadPlayerPose(out position, out yaw);
        public OcOfficerWorldStatus TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state) => Inner.TryReadPatrolState(clone, out state);
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) => Inner.TryBindPatrol(clone, duty, target, out detail);
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => Inner.TryUnbindPatrol(clone, out detail);
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Inner.PatrolSnapshot();
        public void ClearPatrols() => Inner.Bindings.ClearPatrols();
        public IReadOnlyList<string> PatrolPoolStatus() => Array.Empty<string>();
        public int PatrolGroupCount() => Inner.Bindings.PatrolGroupCount();
    }
}
