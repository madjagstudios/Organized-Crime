using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcPatrolRosterTests
{
    [Fact]
    public void Save_while_bound_stops_and_returns_before_native_clone_removal()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "save");
        var handle = Spawn(roster);
        Assert.Equal(OcOfficerWorldStatus.Ok, roster.TryBindPatrol(handle, OcPatrolDuty.Vehicle, default, out _));
        roster.OnSaveStart();
        Assert.Equal(new[] { "start", "stop", "return", "remove" }, world.Native.Events);
        Assert.Empty(world.Bindings.Snapshot());
        Assert.False(roster.Snapshot()[0].HasLiveClone);
        roster.OnSaveStart();
        Assert.Single(world.Native.Events, e => e == "return");
        roster.OnSaveComplete();
        roster.Update();
        Assert.True(roster.Snapshot()[0].HasLiveClone);
        // D5a does not schedule/restart saved patrol duty; D5b owns that policy.
        Assert.Empty(world.Bindings.Snapshot());
    }

    [Fact]
    public void Failed_return_blocks_clone_removal_and_orphan_retry_finishes_cleanup()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "retry");
        var handle = Spawn(roster);
        roster.TryBindPatrol(handle, OcPatrolDuty.Vehicle, default, out _);
        world.Native.FailReturn = true;
        roster.Release(handle);
        Assert.DoesNotContain("remove", world.Native.Events);
        Assert.True(world.Bindings.Snapshot()[0].CarLeased);
        world.Native.FailReturn = false;
        roster.Update();
        Assert.Equal(new[] { "start", "stop", "return", "remove" }, world.Native.Events);
        Assert.Empty(world.Bindings.Snapshot());
    }

    [Fact]
    public void Qa_stop_releases_only_patrol_slots_and_is_idempotent()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "stop");
        Spawn(roster, "checkpoint:control");
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Foot).Ok);
        roster.Update();
        roster.Update();
        Assert.Single(world.Bindings.Snapshot());
        Assert.True(roster.QaPatrolStop().Ok);
        Assert.True(roster.QaPatrolStop().Ok);
        Assert.Single(roster.Snapshot());
        Assert.Equal("checkpoint:control", roster.Snapshot()[0].OwnerKey);
        Assert.Empty(world.Bindings.Snapshot());
    }

    [Fact]
    public void Qa_save_cancels_pending_spawn_and_bound_patrol_without_restarting()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "qa-save");
        roster.QaPatrolHere(OcPatrolDuty.Vehicle);
        roster.Update();
        roster.Update();
        roster.QaPatrolHere(OcPatrolDuty.Foot);
        roster.OnSaveStart();
        roster.OnSaveComplete();
        roster.Update();
        Assert.Empty(roster.Snapshot());
        Assert.Empty(world.Bindings.Snapshot());
        Assert.Equal(new[] { "start", "stop", "return", "remove" }, world.Native.Events);
    }

    [Fact]
    public void Qa_vehicle_no_car_fails_once_and_releases_fresh_clone()
    {
        var world = new World();
        world.Native.FreeCars = 1;
        var log = new List<string>();
        using var roster = new OcOfficerRosterService(world, "no-car", log.Add);
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Vehicle).Ok);
        roster.Update();
        roster.Update();
        roster.Update();
        Assert.Empty(roster.Snapshot());
        Assert.Equal(0, world.Native.Starts);
        Assert.Equal(new[] { "remove" }, world.Native.Events);
        Assert.Contains(log, s => s.Contains("no patrol car free", StringComparison.Ordinal));
    }

    [Fact]
    public void Qa_status_contains_per_clone_duty_route_car_and_behaviour_stack()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "status");
        roster.QaPatrolHere(OcPatrolDuty.Vehicle);
        roster.Update(); roster.Update();
        var lines = roster.QaPatrolStatus();
        Assert.Contains(lines, l => l.Contains("duty=Vehicle, route=route, car leased=True", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("stack: Vehicle Patrol p1 enabled=True active=True", StringComparison.Ordinal));
    }

    [Fact]
    public void Clients_cannot_queue_qa_clones_or_stop_native_duty()
    {
        var world = new World();
        world.Native.IsHost = false;
        using var roster = new OcOfficerRosterService(world, "client");
        Assert.False(roster.QaPatrolHere(OcPatrolDuty.Foot).Ok);
        Assert.False(roster.QaPatrolStop().Ok);
        roster.Update();
        Assert.Empty(roster.Snapshot());
        Assert.Empty(world.Native.Events);
    }

    [Fact]
    public void Command_dispatches_both_patrol_modes_status_stop_and_checks_qa_gate()
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "command");
        using var service = new Chapter3RuntimeService(new Context(), new Repository(), new Gate(), new Clock());
        service.OfficerRoster = roster;
        Assert.Contains("disabled", Chapter3QaCommand.Execute(service, new[] { "patrol", "foot", "here" }, false)[0]);
        Assert.Empty(roster.Snapshot());
        foreach (var duty in new[] { "foot", "vehicle" })
        {
            Assert.Contains("requested fresh clone", Chapter3QaCommand.Execute(service, new[] { "patrol", duty, "here" }, true)[0]);
            roster.Update(); roster.Update();
        }
        Assert.Equal(2, world.Bindings.Snapshot().Count);
        Assert.Contains("patrols: 2", Chapter3QaCommand.Execute(service, new[] { "patrol", "status" }, true)[0]);
        Assert.Contains("Usage:", Chapter3QaCommand.Execute(service, new[] { "patrol", "vehicle", "elsewhere" }, true)[0]);
        Assert.Contains("released 2", Chapter3QaCommand.Execute(service, new[] { "patrol", "stop" }, true)[0]);
        Assert.Empty(roster.Snapshot());
        service.Dispose();
        Assert.Contains("disposed", Chapter3QaCommand.Execute(service, new[] { "patrol", "foot", "here" }, true)[0]);
    }

    private static int Spawn(OcOfficerRosterService roster, string owner = "test")
    {
        Assert.Equal(OcOfficerRequestStatus.Accepted, roster.TryRequest(owner, default, out var handle));
        roster.Update();
        Assert.True(roster.Snapshot().Single(s => s.Handle == handle).HasLiveClone);
        return handle;
    }

    private sealed class World : IOcOfficerWorld, IOcPatrolWorld
    {
        internal readonly OcPatrolBindingTests.Native Native = new();
        internal readonly OcPatrolBindings Bindings;
        private int _serial;
        public World() => Bindings = new(Native);
        public bool IsHost => Native.IsHost;
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) => Bindings.TryBind(clone, duty, target, out detail);
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => Bindings.TryUnbind(clone, out detail);
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => Bindings.Snapshot();
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) { minutes = 10; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) { status = new(true, true, 1, id); detail = "ok"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail) { _serial++; clone = new(new object(), _serial, _serial); detail = "ok"; return OcOfficerWorldStatus.Ok; }
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

    private sealed class Context : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        { snapshot = new(Guid.Empty, 1, "player", Path.GetTempPath()); return Release1StoryHostContextReadStatus.Ready; }
    }
    private sealed class Repository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() => new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "empty");
        public Chapter3StoreUpdateResult Update(Chapter3State? state) => new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "stored");
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => false; }
    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int days) { days = 0; return true; }
    }
}
