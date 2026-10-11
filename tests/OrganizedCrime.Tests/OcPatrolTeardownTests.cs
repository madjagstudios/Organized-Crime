using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcPatrolTeardownTests
{
    private static readonly OcOfficerCloneRef Clone = new(new object(), 51, 1);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Clear_stops_live_duty_and_drops_bindings(int mode)
    {
        var duty = (OcPatrolDuty)mode;
        var native = new OcPatrolBindingTests.Native();
        var bindings = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.Ok, bindings.TryBind(Clone, duty, default, out _));
        bindings.ClearPatrols();
        Assert.Empty(bindings.Snapshot());
        Assert.Contains("stop", native.Events);
        if (duty == OcPatrolDuty.Vehicle) Assert.Contains("return", native.Events);
        bindings.ClearPatrols();
        Assert.Single(native.Events, e => e == "stop");
    }

    [Fact]
    public void Clear_drops_a_failed_return_instead_of_retaining_cleanup_pending()
    {
        var native = new OcPatrolBindingTests.Native();
        var bindings = new OcPatrolBindings(native);
        bindings.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.FailReturn = true;
        Assert.Equal(OcOfficerWorldStatus.Failed, bindings.TryUnbind(Clone, out _));
        bindings.ClearPatrols();
        Assert.Empty(bindings.Snapshot());
        Assert.Single(native.Events, e => e == "stop");
    }

    [Fact]
    public void Clear_attempts_return_even_if_stopping_the_clone_fails()
    {
        var native = new OcPatrolBindingTests.Native();
        var bindings = new OcPatrolBindings(native);
        bindings.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.FailStop = true;
        bindings.ClearPatrols();
        Assert.Empty(bindings.Snapshot());
        Assert.Equal(new[] { "start", "stop", "return" }, native.Events);
    }

    [Fact]
    public void Clear_contains_dead_wrapper_failures_and_continues_with_other_entries()
    {
        var native = new DyingNative();
        var bindings = new OcPatrolBindings(native);
        bindings.TryBind(Clone, OcPatrolDuty.Foot, default, out _);
        bindings.TryBind(new(new object(), 52, 2), OcPatrolDuty.Vehicle, default, out _);
        native.Dead = true;
        Assert.Null(Record.Exception(bindings.ClearPatrols));
        Assert.Empty(bindings.Snapshot());
        Assert.Equal(2, native.StopAttempts);
        Assert.Equal(1, native.ReturnAttempts);
    }

    [Fact]
    public void Clear_after_losing_host_drops_entries_without_native_writes()
    {
        var native = new OcPatrolBindingTests.Native();
        var bindings = new OcPatrolBindings(native);
        bindings.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.IsHost = false;
        bindings.ClearPatrols();
        Assert.Empty(bindings.Snapshot());
        Assert.Equal(new[] { "start" }, native.Events);
    }

    [Theory]
    [InlineData("preload", false)]
    [InlineData("loadcomplete", false)]
    [InlineData("dispose", false)]
    [InlineData("preload", true)]
    [InlineData("loadcomplete", true)]
    [InlineData("dispose", true)]
    public void Every_roster_clear_boundary_clears_failed_patrols_including_nonhost(string boundary, bool nonhost)
    {
        var world = new World();
        using var roster = new OcOfficerRosterService(world, "teardown");
        Assert.True(roster.QaPatrolHere(OcPatrolDuty.Vehicle).Ok);
        roster.Update(); roster.Update();
        Assert.Single(world.Bindings.Snapshot());
        world.Native.FailReturn = true;
        world.Native.IsHost = !nonhost;
        switch (boundary)
        {
            case "preload": roster.OnPreLoad(); break;
            case "loadcomplete": roster.OnLoadComplete(); break;
            case "dispose": roster.Dispose(); break;
        }
        Assert.Equal(1, world.Clears);
        Assert.Empty(world.Bindings.Snapshot());
        Assert.Empty(roster.Snapshot());
        if (boundary != "dispose")
        {
            world.Native.IsHost = true;
            Assert.True(roster.QaPatrolStop().Ok);
            Assert.DoesNotContain(roster.QaPatrolStatus(), l => l.StartsWith("orphan ", StringComparison.Ordinal));
        }
        if (nonhost) Assert.Equal(new[] { "start" }, world.Native.Events);
    }

    private sealed class DyingNative : IOcPatrolNative
    {
        public bool IsHost => true;
        internal bool Dead;
        internal int StopAttempts;
        internal int ReturnAttempts;
        public IReadOnlyList<OcPatrolRoute> ReadRoutes(OcPatrolDuty duty) => new[] { new OcPatrolRoute("route", new object(), new[] { default(OcOfficerPosition) }) };
        public object CreateFootGroup(OcPatrolRoute route) => new object();
        public object LeaseVehicle(OcOfficerPosition target) => new object();
        public void StartFoot(OcOfficerCloneRef clone, object group) { }
        public void StartVehicle(OcOfficerCloneRef clone, OcPatrolRoute route, object car) { }
        public void Stop(OcOfficerCloneRef clone, OcPatrolDuty duty, object? group, object? car)
        { StopAttempts++; if (Dead) throw new ObjectDisposedException("clone/group"); }
        public void ReturnVehicle(object car)
        { ReturnAttempts++; if (Dead) throw new ObjectDisposedException("car/station"); }
    }

    private sealed class World : IOcOfficerWorld, IOcPatrolWorld
    {
        internal readonly OcPatrolBindingTests.Native Native = new();
        internal readonly OcPatrolBindings Bindings;
        internal int Clears;
        private int _serial;
        public World() => Bindings = new(Native);
        public bool IsHost => Native.IsHost;
        public void ClearPatrols() { Clears++; Bindings.ClearPatrols(); }
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
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) { diagnostics = new(Array.Empty<OcOfficerBehaviourInfo>(), "patrol", new(false, false, "none", OcCheckpointKind.None)); return OcOfficerWorldStatus.Ok; }
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
