using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ProtectionGateTests : IDisposable
{
    public Release1ProtectionGateTests() => Release1ProtectionGateState.Reset();

    public void Dispose() => Release1ProtectionGateState.Reset();

    [Fact]
    public void Calls_drop_only_for_the_covered_local_player_on_the_host()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Assert.True(Release1ProtectionGateState.ShouldDropPoliceCall(reportedPlayerIsLocal: true));
        Assert.False(Release1ProtectionGateState.ShouldDropPoliceCall(reportedPlayerIsLocal: false));
        Assert.Equal(1, Release1ProtectionGateState.CallsDropped);

        Release1ProtectionGateState.IsCovered = () => false;
        Assert.False(Release1ProtectionGateState.ShouldDropPoliceCall(reportedPlayerIsLocal: true));

        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => false;
        Assert.False(Release1ProtectionGateState.ShouldDropPoliceCall(reportedPlayerIsLocal: true));
        Assert.Equal(1, Release1ProtectionGateState.CallsDropped);
    }

    [Fact]
    public void Civilian_phone_calls_void_their_target_only_for_the_covered_local_player_and_never_count()
    {
        // OC-118 owner run: CallPoliceBehaviour.FinalizeCall sets Investigating and adds the
        // crime before PoliceCalled, so its target must read invalid while covered.
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Assert.True(Release1ProtectionGateState.ShouldVoidCivilianCallTarget(targetIsLocal: true));
        Assert.False(Release1ProtectionGateState.ShouldVoidCivilianCallTarget(targetIsLocal: false));

        Release1ProtectionGateState.IsCovered = () => false;
        Assert.False(Release1ProtectionGateState.ShouldVoidCivilianCallTarget(targetIsLocal: true));

        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => false;
        Assert.False(Release1ProtectionGateState.ShouldVoidCivilianCallTarget(targetIsLocal: true));

        // IsTargetValid runs every behaviour update; the dropped-call counter is for whole calls.
        Assert.Equal(0, Release1ProtectionGateState.CallsDropped);
    }

    [Fact]
    public void Patrol_skip_is_deterministic_per_instance_and_day_and_about_half()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Assert.Equal(
            Release1ProtectionGateState.ShouldSkipFootPatrol("foot:route-a:1", 5),
            Release1ProtectionGateState.ShouldSkipFootPatrol("foot:route-a:1", 5));
        Assert.Equal(
            Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:route-b:2", 5),
            Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:route-b:2", 5));

        var skipped = 0;
        const int instances = 200;
        for (var i = 0; i < instances; i++)
            if (Release1ProtectionGateState.ShouldSkipFootPatrol($"foot:route-{i}:0", 5))
                skipped++;
        Assert.InRange(skipped, 60, 140);

        var differsByDay = false;
        for (var i = 0; i < instances; i++)
            if (Release1ProtectionGateState.ShouldSkipFootPatrol($"foot:route-{i}:0", 5) !=
                Release1ProtectionGateState.ShouldSkipFootPatrol($"foot:route-{i}:0", 6))
                differsByDay = true;
        Assert.True(differsByDay);
    }

    [Fact]
    public void Patrol_gates_count_runs_and_skips_only_while_active()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Release1ProtectionGateState.ShouldSkipFootPatrol("foot:a:1", 5);
        Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:b:2", 5);
        Assert.Equal(1, Release1ProtectionGateState.FootPatrolsSkipped + Release1ProtectionGateState.FootPatrolsRun);
        Assert.Equal(1, Release1ProtectionGateState.VehiclePatrolsSkipped + Release1ProtectionGateState.VehiclePatrolsRun);

        Release1ProtectionGateState.IsCovered = () => false;
        Release1ProtectionGateState.ShouldSkipFootPatrol("foot:a:1", 5);
        Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:b:2", 5);
        Assert.Equal(1, Release1ProtectionGateState.FootPatrolsSkipped + Release1ProtectionGateState.FootPatrolsRun);
        Assert.Equal(1, Release1ProtectionGateState.VehiclePatrolsSkipped + Release1ProtectionGateState.VehiclePatrolsRun);
    }

    [Fact]
    public void Vehicle_funnel_and_start_share_one_coin_per_route_and_day()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        for (var i = 0; i < 50; i++)
        {
            var key = $"vehicle:route-{i}:0";
            Assert.Equal(
                Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance(key, 5),
                Release1ProtectionGateState.ShouldSkipVehiclePatrol(key, 5));
            Assert.Equal(
                Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance(key, 6),
                Release1ProtectionGateState.ShouldSkipVehiclePatrol(key, 6));
        }
    }

    [Fact]
    public void Foot_funnel_and_start_share_one_coin_per_route_and_day()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        for (var i = 0; i < 50; i++)
        {
            var key = $"foot:route-{i}:0";
            Assert.Equal(
                Release1ProtectionGateState.ShouldSkipFootPatrolInstance(key, 5),
                Release1ProtectionGateState.ShouldSkipFootPatrol(key, 5));
            Assert.Equal(
                Release1ProtectionGateState.ShouldSkipFootPatrolInstance(key, 6),
                Release1ProtectionGateState.ShouldSkipFootPatrol(key, 6));
        }
    }

    [Fact]
    public void Instance_runs_stay_silent_while_blocks_count()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        var footSkips = 0;
        var vehicleSkips = 0;
        for (var i = 0; i < 200; i++)
        {
            if (Release1ProtectionGateState.ShouldSkipFootPatrolInstance($"foot:route-{i}:0", 5)) footSkips++;
            if (Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance($"vehicle:route-{i}:0", 5)) vehicleSkips++;
        }

        Assert.Equal(footSkips, Release1ProtectionGateState.FootPatrolsSkipped);
        Assert.Equal(0, Release1ProtectionGateState.FootPatrolsRun);
        Assert.Equal(vehicleSkips, Release1ProtectionGateState.VehiclePatrolsSkipped);
        Assert.Equal(0, Release1ProtectionGateState.VehiclePatrolsRun);
        Assert.InRange(footSkips, 60, 140);
        Assert.InRange(vehicleSkips, 60, 140);
    }

    [Fact]
    public void Sentries_skip_wholesale_while_covered_on_the_host()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Assert.True(Release1ProtectionGateState.ShouldSkipSentry());
        Assert.Equal(1, Release1ProtectionGateState.SentriesSkipped);

        Release1ProtectionGateState.IsHost = () => false;
        Assert.False(Release1ProtectionGateState.ShouldSkipSentry());
        Assert.Equal(1, Release1ProtectionGateState.SentriesSkipped);
    }

    [Fact]
    public void Checkpoint_enables_block_while_disables_always_pass()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;

        Assert.True(Release1ProtectionGateState.ShouldBlockCheckpointEnable(enabling: true));
        Assert.False(Release1ProtectionGateState.ShouldBlockCheckpointEnable(enabling: false));
        Assert.Equal(1, Release1ProtectionGateState.CheckpointsBlocked);

        Release1ProtectionGateState.IsCovered = () => false;
        Assert.False(Release1ProtectionGateState.ShouldBlockCheckpointEnable(enabling: true));
        Assert.Equal(1, Release1ProtectionGateState.CheckpointsBlocked);
    }

    [Fact]
    public void Faulty_or_missing_reads_act_as_if_unprotected_and_never_throw()
    {
        Release1ProtectionGateState.IsCovered = () => throw new InvalidOperationException("boom");
        Release1ProtectionGateState.IsHost = () => throw new InvalidOperationException("boom");
        Release1ProtectionGateState.Today = () => throw new InvalidOperationException("boom");

        Assert.False(Release1ProtectionGateState.ShouldDropPoliceCall(true));
        Assert.False(Release1ProtectionGateState.ShouldSkipFootPatrol("foot:a:1", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipFootPatrolInstance("foot:a:1", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:b:2", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance("vehicle:b:2", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipSentry());
        Assert.False(Release1ProtectionGateState.ShouldBlockCheckpointEnable(true));

        Release1ProtectionGateState.Reset();
        Assert.False(Release1ProtectionGateState.ShouldDropPoliceCall(true));
        Assert.False(Release1ProtectionGateState.ShouldSkipFootPatrol("foot:a:1", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipFootPatrolInstance("foot:a:1", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipVehiclePatrol("vehicle:b:2", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance("vehicle:b:2", 5));
        Assert.False(Release1ProtectionGateState.ShouldSkipSentry());
        Assert.False(Release1ProtectionGateState.ShouldBlockCheckpointEnable(true));
        var counters = Release1ProtectionGateState.Snapshot();
        Assert.Equal(0, counters.CallsDropped + counters.FootPatrolsSkipped + counters.VehiclePatrolsSkipped +
            counters.SentriesSkipped + counters.CheckpointsBlocked);
    }

    [Fact]
    public void Reset_clears_providers_and_counters()
    {
        Release1ProtectionGateState.IsCovered = () => true;
        Release1ProtectionGateState.IsHost = () => true;
        Release1ProtectionGateState.ShouldSkipSentry();

        Release1ProtectionGateState.Reset();

        Assert.False(Release1ProtectionGateState.ShouldSkipSentry());
        Assert.Equal(0, Release1ProtectionGateState.SentriesSkipped);
    }
}
