using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PatrolServiceTests
{
    internal sealed class Fixture : IDisposable
    {
        internal readonly World World = new();
        internal readonly OcOfficerRosterService Roster;
        internal readonly Chapter3PatrolService Service;
        internal Chapter3PatrolInput Input = new(true, true, 1080d, LocalPressureTier.Critical,
            false, false, new OcOfficerPosition(50, 0, 60), 6);
        internal Fixture()
        {
            Roster = new(World, "d5b");
            Service = new(Roster, () => Input);
        }
        internal void Step() { Service.Poll(); Roster.Update(); }
        internal void Converge() { for (var i = 0; i < 80; i++) Step(); }
        internal void Checkpoints(int count)
        {
            for (var i = 0; i < count; i++)
                Assert.Equal(OcOfficerRequestStatus.Accepted, Roster.TryRequest($"checkpoint:test:{i}", default, out _));
        }
        public void Dispose() { Service.Dispose(); Roster.Dispose(); }
    }

    [Fact]
    public void Critical_reserves_checkpoint_demand_before_they_have_spawned()
    {
        using var f = new Fixture();
        f.Converge();
        Assert.Equal(2, f.Roster.Count);
        f.Checkpoints(6);
        f.Converge();
        Assert.Equal(8, f.Roster.Count);
        Assert.Equal(2, f.World.Bindings.Snapshot().Count);
        Assert.All(f.World.Targets, p => Assert.Equal(f.Input.Target!.Value, p));
    }

    [Fact]
    public void Lockdown_accepts_ten_and_never_stacks_with_ordinary()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Lockdown = true, Minutes = 0d };
        f.Checkpoints(6);
        f.Converge();
        Assert.Equal(10, f.Roster.Count);
        Assert.Equal(3, f.World.Bindings.Snapshot().Count(p => p.Duty == OcPatrolDuty.Foot));
        Assert.Single(f.World.Bindings.Snapshot(), p => p.Duty == OcPatrolDuty.Vehicle);
        Assert.Equal(OcOfficerRequestStatus.RefusedCap, f.Roster.TryRequest("extra", default, out _));
    }

    [Fact]
    public void Extra_checkpoint_slots_take_priority_and_reduce_patrol_budget()
    {
        using var f = new Fixture();
        f.Checkpoints(7);
        f.Converge();
        Assert.Equal(8, f.Roster.Count);
        Assert.Single(f.World.Bindings.Snapshot());
        Assert.Equal(OcPatrolDuty.Foot, f.World.Bindings.Snapshot()[0].Duty);
    }

    [Fact]
    public void Busy_slot_survives_window_end_and_protection_and_blocks_replacement()
    {
        using var f = new Fixture();
        f.Converge();
        var busy = f.World.Bindings.Snapshot().First().Officer;
        f.World.Busy.Add(busy.Pointer);
        var spawns = f.World.Spawns;
        f.Input = f.Input with { Minutes = 1380d, Protected = true };
        f.Converge();
        Assert.Single(f.Roster.Snapshot());
        Assert.Equal(busy, f.World.Bindings.Snapshot()[0].Officer);
        Assert.Equal(spawns, f.World.Spawns);
        f.World.Busy.Clear();
        f.Converge();
        Assert.Empty(f.Roster.Snapshot());
    }

    [Theory]
    [InlineData("knockout")]
    [InlineData("dead")]
    [InlineData("gone")]
    public void Unavailable_clones_replace_on_reconcile_with_rate_limit(string health)
    {
        using var f = new Fixture();
        f.Input = f.Input with { Tier = LocalPressureTier.Watched, CheckpointDesired = 2 };
        f.Converge();
        var old = Assert.Single(f.Roster.Snapshot()).CloneId;
        f.World.Unavailable = health;
        f.Step();
        Assert.Empty(f.Roster.Snapshot());
        f.World.Unavailable = null;
        var spawns = f.World.Spawns;
        f.Step(); f.Step();
        Assert.Equal(spawns, f.World.Spawns);
        f.Converge();
        Assert.NotEqual(old, Assert.Single(f.Roster.Snapshot()).CloneId);
    }

    [Fact]
    public void Failed_vehicle_binding_keeps_its_slot_and_retries_without_spawning()
    {
        using var f = new Fixture();
        f.World.Native.FreeCars = 1;
        f.Converge();
        Assert.Equal(2, f.Roster.Count);
        Assert.Single(f.World.Bindings.Snapshot());
        var attempts = f.World.BindAttempts;
        var spawns = f.World.Spawns;
        f.Step(); f.Step();
        Assert.InRange(f.World.BindAttempts - attempts, 0, 1);
        Assert.Equal(spawns, f.World.Spawns);
        f.World.Native.FreeCars = 2;
        f.Converge();
        Assert.Equal(2, f.World.Bindings.Snapshot().Count);
        Assert.Equal(spawns, f.World.Spawns);
    }

    [Theory]
    [InlineData(1100d, 2)]
    [InlineData(1380d, 0)]
    [InlineData(1440d, 0)]
    public void Save_and_load_rebuild_only_the_current_window(double minute, int expected)
    {
        using var f = new Fixture();
        f.Converge();
        f.Service.OnSaveStart(); f.Roster.OnSaveStart();
        Assert.Empty(f.World.Bindings.Snapshot());
        f.Input = f.Input with { Minutes = minute };
        f.Converge();
        Assert.Empty(f.World.Bindings.Snapshot());
        f.Roster.OnSaveComplete(); f.Service.OnSaveComplete();
        f.Converge();
        Assert.Equal(expected, f.Roster.Count);
        f.Service.OnPreLoad(); f.Roster.OnPreLoad();
        f.Roster.OnLoadComplete(); f.Service.OnLoadComplete();
        f.Converge();
        Assert.Equal(expected, f.Roster.Count);
    }

    [Fact]
    public void Client_poll_and_QA_do_nothing()
    {
        using var f = new Fixture();
        f.Input = f.Input with { IsHost = false, Lockdown = true };
        f.World.Native.IsHost = false;
        f.Converge();
        Assert.Empty(f.Roster.Snapshot());
        Assert.Equal(0, f.World.Spawns);
        Assert.False(f.Service.SetAuto(false).Ok);
    }

    [Fact]
    public void Auto_off_isolates_D5a_and_off_on_does_not_reset_retries()
    {
        using var f = new Fixture();
        f.Converge();
        Assert.True(f.Service.SetAuto(false).Ok);
        f.Converge();
        Assert.Empty(f.Roster.Snapshot());
        Assert.True(f.Roster.QaPatrolHere(OcPatrolDuty.Foot).Ok);
        f.Converge();
        Assert.Single(f.Roster.Snapshot());
        Assert.True(f.Service.SetAuto(true).Ok);
        f.Converge();
        Assert.Equal(2, f.Roster.Count); // One QA slot leaves one automatic slot after six reserved checkpoints.
    }

    [Fact]
    public void Unknown_health_holds_existing_slot_and_does_not_bind_over_response()
    {
        using var f = new Fixture();
        f.Converge();
        f.World.Unknown = true;
        var starts = f.World.Native.Starts;
        f.Input = f.Input with { Protected = true, Target = new OcOfficerPosition(100, 0, 200) };
        f.Converge();
        Assert.Equal(2, f.Roster.Count);
        Assert.Equal(starts, f.World.Native.Starts);
    }

    [Fact]
    public void Status_shows_profile_counts_cap_and_busy_slots()
    {
        using var f = new Fixture();
        f.Checkpoints(6); f.Converge();
        f.World.Busy.Add(f.World.Bindings.Snapshot()[0].Officer.Pointer);
        f.Step();
        var text = string.Join("\n", f.Service.Status());
        Assert.Contains("critical", text);
        Assert.Contains("checkpoint desired=6 actual=6", text);
        Assert.Contains("foot desired=1 actual=1", text);
        Assert.Contains("vehicle desired=1 actual=1", text);
        Assert.Contains("cap=8", text);
        Assert.Contains("busy-in-response=1", text);
    }
    [Fact]
    public void Retained_response_driver_consumes_a_Watched_foot_only_budget()
    {
        using var f = new Fixture();
        f.Converge();
        var driver = f.World.Bindings.Snapshot().Single(p => p.Duty == OcPatrolDuty.Vehicle).Officer;
        f.World.Busy.Add(driver.Pointer);
        f.Input = f.Input with { Tier = LocalPressureTier.Watched, CheckpointDesired = 2 };
        f.Converge();
        Assert.Single(f.Roster.Snapshot());
        Assert.Equal(driver, Assert.Single(f.World.Bindings.Snapshot()).Officer);
        f.World.Busy.Clear();
        f.Converge();
        Assert.Equal(OcPatrolDuty.Foot, Assert.Single(f.World.Bindings.Snapshot()).Duty);
    }

    [Fact]
    public void Lockdown_busy_slots_drain_after_cap_shrink_without_new_requests()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Lockdown = true };
        f.Checkpoints(6); f.Converge();
        foreach (var binding in f.World.Bindings.Snapshot()) f.World.Busy.Add(binding.Officer.Pointer);
        var spawns = f.World.Spawns;
        f.Input = f.Input with { Lockdown = false };
        f.Converge();
        Assert.Equal(10, f.Roster.OccupiedCount);
        Assert.Equal(spawns, f.World.Spawns);
        Assert.Equal(OcOfficerRequestStatus.RefusedCap, f.Roster.TryRequest("another", default, out _));
        Assert.Contains(f.Service.Status(), s => s.Contains("draining cap=10", StringComparison.Ordinal));
        f.World.Busy.Clear(); f.Converge();
        Assert.Equal(8, f.Roster.OccupiedCount);
        Assert.Equal(spawns, f.World.Spawns);
    }

    [Fact]
    public void Failed_car_return_remains_occupied_and_blocks_replacement_spawns()
    {
        using var f = new Fixture();
        f.Checkpoints(6); f.Converge();
        var spawns = f.World.Spawns;
        f.World.Native.FailReturn = true;
        f.Input = f.Input with { Protected = true, CheckpointDesired = 0 };
        f.Converge();
        Assert.Equal(1, f.Roster.PendingRemovalCount);
        Assert.Equal(7, f.Roster.OccupiedCount);
        f.Input = f.Input with { Protected = false, CheckpointDesired = 6 };
        f.Converge();
        Assert.Equal(8, f.Roster.OccupiedCount);
        Assert.Equal(spawns, f.World.Spawns);
        f.World.Native.FailReturn = false; f.Converge();
        Assert.Equal(0, f.Roster.PendingRemovalCount);
        Assert.Equal(8, f.Roster.OccupiedCount);
        Assert.Equal(2, f.World.Bindings.Snapshot().Count);
    }

    [Fact]
    public void Status_includes_manual_QA_duty_in_actual_counts()
    {
        using var f = new Fixture();
        f.Service.SetAuto(false); f.Converge();
        Assert.True(f.Roster.QaPatrolHere(OcPatrolDuty.Foot).Ok);
        f.Converge();
        Assert.Contains(f.Service.Status(), s => s.Contains("foot desired=0 actual=1", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_target_never_spawns_and_failed_binding_does_not_spam()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Target = null };
        f.Converge(); Assert.Equal(0, f.World.Spawns);
        f.Input = f.Input with { Target = new OcOfficerPosition(1, 0, 2), Tier = LocalPressureTier.Watched };
        f.World.Native.FailReadRoutes = true;
        f.Converge();
        Assert.Equal(1, f.World.Spawns);
        var attempts = f.World.BindAttempts;
        for (var i = 0; i < 10; i++) f.Step();
        Assert.InRange(f.World.BindAttempts - attempts, 1, 2);
    }

    internal sealed class World : IOcOfficerWorld, IOcPatrolWorld
    {
        internal readonly OcPatrolBindingTests.Native Native = new();
        internal readonly OcPatrolBindings Bindings;
        private int _serial;
        internal int Spawns;
        internal int BindAttempts;
        internal string? Unavailable;
        internal bool Unknown;
        internal readonly HashSet<long> Busy = new();
        internal readonly List<OcOfficerPosition> Targets = new();
        public World() => Bindings = new(Native);
        public bool IsHost => Native.IsHost;
        public OcOfficerWorldStatus TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state)
        {
            state = new(Unavailable is not null, Busy.Contains(clone.Pointer));
            return Unknown ? OcOfficerWorldStatus.NotReady : Unavailable == "gone" ? OcOfficerWorldStatus.InvalidHandle : OcOfficerWorldStatus.Ok;
        }
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) { BindAttempts++; Targets.Add(target); return Bindings.TryBind(clone, duty, target, out detail); }
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => Bindings.TryUnbind(clone, out detail);
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Bindings.Snapshot();
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) { minutes = 10; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) { status = new(true, true, 1, id); detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail) { Spawns++; _serial++; clone = new(new object(), _serial, _serial); detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) { knockedOut = false; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) { position = default; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint) { active = "patrol"; checkpoint = false; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state) { state = new(false, false, OcCheckpointKind.None, null); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) { diagnostics = new(new[] { new OcOfficerBehaviourInfo("Vehicle Patrol", 1, true, true) }, "Vehicle Patrol", new(false, false, "none", OcCheckpointKind.None)); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) { detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail) { Native.Events.Add("remove"); detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) { detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) { detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) { detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) { leaked = false; detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) { status = new(true, true, 1, "template"); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) { counts = new(2, 2); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) { position = default; yaw = 0; return OcOfficerWorldStatus.Ok; }
    }

}
