using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc174PoolSweepTests
{
    [Fact]
    public void Spawn_disables_station_recycling_before_activation_and_delayed_binding()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "tools/OrganizedCrime/Runtime/Police/OcOfficerSpawner.cs"));
        var start = source.IndexOf("public OcOfficerWorldStatus TrySpawnClone", StringComparison.Ordinal);
        var activation = source.IndexOf("instance.SetActive(true)", start, StringComparison.Ordinal);
        var prevent = source.IndexOf("officer.AutoDeactivate = false", start, StringComparison.Ordinal);
        Assert.True(prevent > start && prevent < activation, "Clone must suppress native recycling before activation, not at a later patrol bind.");
    }

    [Fact]
    public void Native_adapter_keeps_trycast_ownership_and_live_diagnostics_at_the_boundary()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "tools/OrganizedCrime/Runtime/Police/Il2CppOcPatrolNative.cs"));
        Assert.Contains("OC clones in pool=", source);
        Assert.Contains("PollOfficerPools", source);
        Assert.Contains("TryCast<PoliceOfficer>()", source);
        Assert.Contains("_context._ownPointers.Contains", source);
        Assert.Contains("UnityLife.IsAlive", source);
    }

    [Fact]
    public void Sweep_removes_only_owned_pointers_in_reverse_order_including_duplicates()
    {
        var pool = new List<Entry> { new(1, "native"), new(2, "oc-a"), new(3, "oc_officer_fake"), new(2, "oc-a"), new(4, "oc-b") };
        var removals = new List<int>();
        var warnings = new List<string>();
        var sweep = new OcOfficerPoolSweep(warnings.Add);
        Assert.Equal(3, sweep.RemoveOwned(true, "station west", pool.ToArray(), e => e.Pointer is 2 or 4,
            e => e.Id, i => { removals.Add(i); pool.RemoveAt(i); }));
        Assert.Equal(new[] { 4, 3, 1 }, removals);
        Assert.Equal(new long[] { 1, 3 }, pool.Select(e => e.Pointer));
        Assert.Contains("oc-b", Assert.Single(warnings));
        Assert.Contains("station west", warnings[0]);
        Assert.Equal(0, sweep.RemoveOwned(true, "station west", pool.ToArray(), e => e.Pointer is 2 or 4,
            e => e.Id, pool.RemoveAt));
        Assert.Single(warnings);
    }

    [Fact]
    public void Client_does_not_evaluate_ownership_or_identifiers_or_mutate_or_warn()
    {
        var pool = new[] { new Entry(2, "oc-a") };
        var sweep = new OcOfficerPoolSweep(_ => throw new Exception("warn on client"));
        Assert.Equal(0, sweep.RemoveOwned(false, "west", pool, _ => throw new Exception("read on client"),
            _ => throw new Exception("id on client"), _ => throw new Exception("write on client")));
    }

    [Fact]
    public void Warning_is_once_across_stations_and_polls_and_reset_for_next_load()
    {
        var warnings = new List<string>();
        var sweep = new OcOfficerPoolSweep(warnings.Add);
        var pool = new[] { new Entry(2, "oc-a") };
        for (var i = 0; i < 3; i++)
            Assert.Equal(1, sweep.RemoveOwned(true, i == 0 ? "west" : "east", pool, _ => true, e => e.Id, _ => { }));
        Assert.Single(warnings);
        sweep.ResetWarnings();
        Assert.Equal(1, sweep.RemoveOwned(true, "east", pool, _ => true, e => e.Id, _ => { }));
        Assert.Equal(2, warnings.Count);
        Assert.Contains("east", warnings[1]);
    }

    [Fact]
    public void Bad_entry_and_failed_removal_do_not_prevent_other_owned_cleanup_or_retry()
    {
        var pool = new List<Entry> { new(1, "native"), new(2, "oc-a"), new(3, "unreadable"), new(4, "oc-b") };
        var warnings = new List<string>();
        var sweep = new OcOfficerPoolSweep(warnings.Add);
        var fail = true;
        bool Owned(Entry e) => e.Pointer == 3 ? throw new InvalidOperationException() : e.Pointer is 2 or 4;
        void Remove(int i) { if (pool[i].Pointer == 4 && fail) throw new InvalidOperationException(); pool.RemoveAt(i); }
        Assert.Equal(1, sweep.RemoveOwned(true, "west", pool.ToArray(), Owned, e => e.Id, Remove));
        Assert.Equal(new long[] { 1, 3, 4 }, pool.Select(e => e.Pointer));
        fail = false;
        Assert.Equal(1, sweep.RemoveOwned(true, "west", pool.ToArray(), Owned, e => e.Id, Remove));
        Assert.Equal(new long[] { 1, 3 }, pool.Select(e => e.Pointer));
        Assert.Single(warnings);
    }

    [Fact]
    public void Throwing_logger_does_not_block_cleanup_or_repeat_warning()
    {
        var calls = 0;
        var sweep = new OcOfficerPoolSweep(_ => { calls++; throw new InvalidOperationException(); });
        var pool = new[] { new Entry(2, "oc-a") };
        for (var i = 0; i < 2; i++) Assert.Equal(1, sweep.RemoveOwned(true, "west", pool, _ => true, e => e.Id, _ => { }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Host_roster_polls_with_no_slots_and_before_pending_QA_bind_early_return()
    {
        var world = new PoolWorld();
        using var roster = new OcOfficerRosterService(world, "oc174");
        roster.Update();
        Assert.Equal(1, world.Polls);
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Vehicle).Ok);
        roster.Update(); roster.Update();
        Assert.Equal(3, world.Polls);
    }

    [Fact]
    public void Client_roster_never_calls_native_pool_capability()
    {
        var world = new PoolWorld();
        world.Inner.Native.IsHost = false;
        using var roster = new OcOfficerRosterService(world, "oc174");
        roster.Update();
        Assert.Equal(0, world.Polls);
    }

    [Fact]
    public void Warning_reset_occurs_only_at_load_complete_not_save_or_preload()
    {
        var world = new PoolWorld();
        using var roster = new OcOfficerRosterService(world, "oc174");
        roster.OnSaveStart(); roster.OnSaveComplete(); roster.OnPreLoad();
        Assert.Equal(0, world.Resets);
        roster.OnLoadComplete();
        Assert.Equal(1, world.Resets);
    }

    [Fact]
    public void Throwing_pool_capability_does_not_stop_roster_spawn_or_load_completion()
    {
        var world = new PoolWorld { Throw = true };
        using var roster = new OcOfficerRosterService(world, "oc174");
        Assert.Equal(OcOfficerRequestStatus.Accepted, roster.TryRequest("qa:test", default, out _));
        Assert.Null(Record.Exception(roster.Update));
        Assert.True(Assert.Single(roster.Snapshot()).HasLiveClone);
        Assert.Null(Record.Exception(roster.OnLoadComplete));
        Assert.Empty(roster.Snapshot());
    }

    [Fact]
    public void Unreadable_identifier_does_not_block_owned_removal_or_warning_budget()
    {
        var warnings = new List<string>();
        var removals = 0;
        var sweep = new OcOfficerPoolSweep(warnings.Add);
        var pool = new[] { new Entry(2, "oc-a") };
        for (var i = 0; i < 2; i++)
            Assert.Equal(1, sweep.RemoveOwned(true, "west", pool, _ => true,
                _ => throw new InvalidOperationException(), _ => removals++));
        Assert.Equal(2, removals);
        Assert.Contains("west", Assert.Single(warnings));
    }
    private sealed record Entry(long Pointer, string Id);
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tools/OrganizedCrime/Mod.cs"))) return dir.FullName;
        throw new InvalidOperationException("No repo root.");
    }

    private sealed class PoolWorld : IOcOfficerWorld, IOcPatrolWorld, IOcOfficerPoolWorld
    {
        internal readonly Chapter3PatrolServiceTests.World Inner = new();
        internal int Polls;
        internal int Resets;
        internal bool Throw;
        public void PollOfficerPools() { Polls++; if (Throw) throw new InvalidOperationException(); }
        public void ResetOfficerPoolWarnings() { Resets++; if (Throw) throw new InvalidOperationException(); }
        public bool IsHost => Inner.IsHost;
        public OcOfficerWorldStatus TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state) => Inner.TryReadPatrolState(clone, out state);
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) => Inner.TryBindPatrol(clone, duty, target, out detail);
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => Inner.TryUnbindPatrol(clone, out detail);
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Inner.PatrolSnapshot();
        public void ClearPatrols() => Inner.Bindings.ClearPatrols();
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => Inner.TryReadGameMinutes(out minutes);
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) => Inner.TryEnsureTemplate(id, out status, out detail);
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail) => Inner.TrySpawnClone(position, id, out clone, out detail);
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => Inner.TryReadKnockedOut(clone, out knockedOut);
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => Inner.TryReadClonePosition(clone, out position);
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint) => Inner.TryReadBehaviourState(clone, out active, out checkpoint);
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state) => Inner.TryReadBoundState(clone, checkpoint, out state);
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) => Inner.TryReadBehaviourDiagnostics(clone, checkpoint, out diagnostics);
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) => Inner.TryBindToCheckpoint(clone, checkpoint, stand, out detail);
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail) => Inner.TryRemoveClone(clone, out detail);
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => Inner.TryDestroyTemplate(out detail);
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) => Inner.TryClearSwapMap(out detail);
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => Inner.TryUnmapClone(clone, out detail);
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) => Inner.TryReadNativeLeak(label, out leaked, out detail);
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => Inner.TryReadTemplateStatus(out status);
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => Inner.TryReadNativeCounts(out counts);
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) => Inner.TryReadPlayerPose(out position, out yaw);
    }
}
