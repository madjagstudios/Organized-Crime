using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Chapter3PatrolCompositionTests
{
    [Fact]
    public void Patrol_uses_host_1Hz_demand_timer_and_preserves_it_across_reinit()
    {
        var lifetime = new OcCompositionLifetime();
        using var a = new CompositionScope(e => { e.IsHostResult = true; e.DeltaTimeDefault = 0.6f; }, build: false, lifetime: lifetime);
        var polls = 0;
        var first = new LifecycleFanout(new LifecycleTables
        {
            Update = new LifecycleSlots { PatrolPoll = () => polls++ }
        }, a.Engine, () => OrganizedCrimeTimingReceipts.Disabled, lifetime);
        first.Update(); Assert.Equal(0, polls);
        var reinit = new LifecycleFanout(new LifecycleTables
        {
            Update = new LifecycleSlots { PatrolPoll = () => polls++ }
        }, a.Engine, () => OrganizedCrimeTimingReceipts.Disabled, lifetime);
        reinit.Update(); Assert.Equal(1, polls);
        a.Engine.IsHostResult = false;
        for (var i = 0; i < 10; i++) reinit.Update();
        Assert.Equal(1, polls);
    }

    [Fact]
    public void Production_fanout_releases_before_save_suspension_then_rebuilds_current_window()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        using var scope = new CompositionScope(e => e.IsHostResult = true, build: false);
        scope.Root.OfficerRoster = f.Roster;
        scope.Root.Patrols = f.Service;
        f.Converge();
        var spawned = f.World.Spawns;
        scope.Root.Lifecycle.HandleSaveStart();
        Assert.Empty(f.Roster.Snapshot());
        Assert.Empty(f.World.Bindings.Snapshot());
        f.Input = f.Input with { Minutes = 1380d };
        scope.Root.Lifecycle.HandleSaveComplete();
        f.Converge();
        Assert.Equal(spawned, f.World.Spawns);
        Assert.Empty(f.Roster.Snapshot());
        scope.Root.Lifecycle.HandlePreLoad();
        f.Input = f.Input with { Minutes = 1080d };
        scope.Root.Lifecycle.HandleLoadComplete();
        f.Converge();
        Assert.Equal(2, f.World.Bindings.Snapshot().Count);
        scope.Root.Lifecycle.HandlePreSceneChange();
        f.Converge();
        Assert.Empty(f.World.Bindings.Snapshot());
    }

    [Fact]
    public void Module_wires_service_and_QA_without_new_static_ownership()
    {
        using var scope = new CompositionScope(e => e.IsHostResult = false);
        Assert.NotNull(scope.Root.Patrols);
        var slots = LifecycleTables.ForRoot(scope.Root).Update;
        Assert.NotNull(slots.PatrolPoll);
        slots.PatrolPoll!();
        Assert.Contains("host", scope.Root.Patrols!.SetAuto(false).Message);
        Assert.False(scope.Root.Patrols.SetAuto(false).Ok);
        Assert.Contains(Chapter3QaCommand.Execute(scope.Root.Chapter3!, new[] { "heat", "Critical" }, true), s => s.Contains("host", StringComparison.Ordinal));
        scope.Root.Teardown();
        Assert.Null(scope.Root.Patrols);
    }
}
