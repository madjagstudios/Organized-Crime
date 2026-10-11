using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcPatrolReviewFixTests
{
    private static readonly OcOfficerCloneRef Clone = new(new object(), 61, 1);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Patrol_binding_fully_unbinds_checkpoint_before_allocating_resources(int mode)
    {
        var native = new CheckpointNative();
        var bindings = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.Ok, bindings.TryBind(Clone, (OcPatrolDuty)mode, default, out _));
        Assert.False(native.Assigned);
        Assert.False(native.Listener);
        Assert.Equal(new[] { "unbind", "readback", "allocate", "start" }, native.Events);
        Assert.Equal(mode == 0 ? 1 : 0, bindings.PatrolGroupCount());
        bindings.TryUnbind(Clone, out _);
        Assert.Equal(0, bindings.PatrolGroupCount());
    }

    [Theory]
    [InlineData(0, "disable")]
    [InlineData(1, "disable")]
    [InlineData(0, "assigned")]
    [InlineData(1, "assigned")]
    [InlineData(0, "throw")]
    [InlineData(1, "throw")]
    public void Incomplete_checkpoint_unbind_refuses_patrol_without_a_group_car_or_start(int mode, string failure)
    {
        var native = new CheckpointNative { Failure = failure };
        var bindings = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.NotReady, bindings.TryBind(Clone, (OcPatrolDuty)mode, default, out var detail));
        Assert.Contains("checkpoint cleanup pending", detail);
        Assert.DoesNotContain("allocate", native.Events);
        Assert.DoesNotContain("start", native.Events);
        Assert.Empty(bindings.Snapshot());
    }

    [Fact]
    public void Readback_failure_is_not_ready()
    {
        var status = OcPatrolCheckpointTransition.TryUnbind(
            () => "checkpoint disabled; swap entry cleared",
            () => throw new ObjectDisposedException("checkpoint"), out var detail);
        Assert.Equal(OcOfficerWorldStatus.NotReady, status);
        Assert.Contains("ObjectDisposedException", detail);
    }

    [Fact]
    public void Failed_deployment_returns_only_new_cars_and_snapshots_before_list_mutation()
    {
        var deployed = new List<long> { 10, 20, 30 };
        var returned = new List<long>();
        OcPatrolDeploymentRecovery.ReturnNew(deployed, new HashSet<long> { 10 }, id => id,
            id => { returned.Add(id); deployed.Remove(id); });
        Assert.Equal(new long[] { 20, 30 }, returned);
        Assert.Equal(new long[] { 10 }, deployed);
    }

    [Fact]
    public void Failed_deployment_contains_return_failures_and_continues_with_other_new_cars()
    {
        var returned = new List<long>();
        OcPatrolDeploymentRecovery.ReturnNew(new long[] { 10, 20, 30 }, new HashSet<long> { 10 }, id => id,
            id => { if (id == 20) throw new ObjectDisposedException("car"); returned.Add(id); });
        Assert.Equal(new long[] { 30 }, returned);
    }

    [Fact]
    public void Patrol_qa_stop_and_status_contain_throwing_worlds()
    {
        var world = new StatusWorld { ThrowSnapshot = true };
        using var roster = new OcOfficerRosterService(world, "qa-throw");
        using var service = Service();
        service.OfficerRoster = roster;
        var qa = (IChapter3PatrolQaTarget)service;
        Assert.False(qa.PatrolStop().Ok);
        Assert.Contains("roster threw", qa.PatrolStop().Message);
        Assert.Contains("roster threw", Assert.Single(qa.PatrolStatus()));
    }

    [Fact]
    public void Patrol_qa_refuses_unwired_or_disposed_service_for_all_three_calls()
    {
        using var service = Service();
        var qa = (IChapter3PatrolQaTarget)service;
        Assert.Contains("not wired", qa.PatrolHere(OcPatrolDuty.Foot).Message);
        Assert.Contains("not wired", qa.PatrolStop().Message);
        Assert.Contains("not wired", Assert.Single(qa.PatrolStatus()));
        service.Dispose();
        Assert.Contains("disposed", qa.PatrolHere(OcPatrolDuty.Foot).Message);
        Assert.Contains("disposed", qa.PatrolStop().Message);
        Assert.Contains("disposed", Assert.Single(qa.PatrolStatus()));
    }

    [Fact]
    public void Patrol_status_labels_oc_owned_group_count_and_unknown_native_tracking()
    {
        using var roster = new OcOfficerRosterService(new StatusWorld(), "group-status");
        Assert.Contains(roster.QaPatrolStatus(), l => l.Contains("OC patrol groups=7 (owned references); native group tracking=UNKNOWN", StringComparison.Ordinal));
    }

    private static Chapter3RuntimeService Service() => new(new CompositionFakeStoryContext(),
        new CompositionFakeChapter3Repository(), new CompositionFakeGate(), new CompositionFakeClock());

    private sealed class CheckpointNative : IOcPatrolNative
    {
        public bool IsHost => true;
        internal bool Assigned = true;
        internal bool Listener = true;
        internal string Failure = "";
        internal readonly List<string> Events = new();
        public OcOfficerWorldStatus PrepareForPatrol(OcOfficerCloneRef clone, out string detail) =>
            OcPatrolCheckpointTransition.TryUnbind(
                () =>
                {
                    Events.Add("unbind");
                    if (Failure == "throw") throw new InvalidOperationException();
                    if (Failure == "disable") return "checkpoint disable failed; swap entry cleared";
                    if (Failure != "assigned") { Assigned = false; Listener = false; }
                    return "checkpoint disabled; swap entry cleared";
                },
                () => { Events.Add("readback"); return Assigned || Listener; }, out detail);
        public IReadOnlyList<OcPatrolRoute> ReadRoutes(OcPatrolDuty duty) => new[] { new OcPatrolRoute("route", new object(), new[] { default(OcOfficerPosition) }) };
        public object CreateFootGroup(OcPatrolRoute route) { Events.Add("allocate"); return new object(); }
        public object LeaseVehicle(OcOfficerPosition target) { Events.Add("allocate"); return new object(); }
        public void StartFoot(OcOfficerCloneRef clone, object group) => Events.Add("start");
        public void StartVehicle(OcOfficerCloneRef clone, OcPatrolRoute route, object car) => Events.Add("start");
        public void Stop(OcOfficerCloneRef clone, OcPatrolDuty duty, object? group, object? car) { }
        public void ReturnVehicle(object car) { }
    }

    private sealed class StatusWorld : IOcOfficerWorld, IOcPatrolWorld
    {
        public bool IsHost => true;
        internal bool ThrowSnapshot;
        public OcOfficerWorldStatus TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
        public IReadOnlyList<OcPatrolSnapshot> PatrolSnapshot() => ThrowSnapshot ? throw new InvalidOperationException("snapshot unavailable") : Array.Empty<OcPatrolSnapshot>();
        public int PatrolGroupCount() => 7;
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => throw new NotSupportedException();
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) => throw new NotSupportedException();
    }
}
