// OC-151: unit tests for the owner QA hotkey map. The binding test pins the exact
// 22-key code order; gate tests prove typing and the disabled preference read no keys;
// handler tests invoke bindings directly (the enabled flag is only settable by Build,
// covered in the composition integration) against a real warehouse runtime, a real
// Chapter 3 service, and throwing or recording courtesy worlds.
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OwnerQaKeysTests
{
    [Fact]
    public void Bindings_list_the_22_keys_in_code_order()
    {
        var (root, _, _) = CreateRoot();

        CompositionAssertions.AssertKeyMap(root.QaKeys.Bindings);
    }

    [Fact]
    public void Poll_returns_while_typing_without_reading_keys()
    {
        var (root, engine, logs) = CreateRoot();
        engine.IsPlayerTypingResult = true;

        root.QaKeys.Poll();

        Assert.Equal(1, engine.IsPlayerTypingCalls);
        Assert.Empty(engine.IsKeyDownCalls);
        Assert.Empty(logs);
    }

    [Fact]
    public void Poll_returns_while_disabled_without_reading_keys()
    {
        var (root, engine, logs) = CreateRoot();
        engine.PressedKeys.Add(KeyCode.F1);

        root.QaKeys.Poll();

        Assert.Equal(1, engine.IsPlayerTypingCalls);
        Assert.Empty(engine.IsKeyDownCalls);
        Assert.Empty(logs);
    }

    [Fact]
    public void Backslash_toggles_the_warehouse_runtime()
    {
        var (root, _, _) = CreateRoot();
        var host = new RecordingPropertyHost();
        var service = new FishWarehouseRuntimeService(host, () => true, log: _ => { });
        root.WarehouseRuntime = service;
        service.Update();
        Assert.Equal(RuntimePropertyLifecycleState.Ready, service.State);

        HandlerFor(root, KeyCode.Backslash)();

        Assert.Equal(1, host.StartCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);

        HandlerFor(root, KeyCode.Backslash)();

        Assert.Equal(1, host.StopCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
    }

    [Fact]
    public void Unlock_and_place_handlers_reach_the_unspawned_runtime()
    {
        var (root, _, logs) = CreateRoot();
        root.WarehouseRuntime = new FishWarehouseRuntimeService(new RecordingPropertyHost(), () => false, log: logs.Add);

        HandlerFor(root, KeyCode.O)();
        HandlerFor(root, KeyCode.P)();

        Assert.Contains(logs, line => line.Contains("ownership unlock is unavailable", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("employee-home placement is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Minus_always_logs_and_refuses_without_a_service()
    {
        var (root, _, logs) = CreateRoot();

        HandlerFor(root, KeyCode.Minus)();

        Assert.Equal(new[]
        {
            "[Organized Crime] Chapter 3 QA start key pressed.",
            "[Organized Crime] Chapter 3 QA start refused: the Chapter 3 service is not constructed.",
        }, logs);
    }

    [Fact]
    public void Minus_drives_TryQaStartNow_when_wired()
    {
        var (root, _, logs) = CreateRoot();
        root.Chapter3 = new Chapter3RuntimeService(
            new CompositionFakeStoryContext(),
            new CompositionFakeChapter3Repository(),
            new CompositionFakeGate(),
            new CompositionFakeClock(),
            logs.Add);

        HandlerFor(root, KeyCode.Minus)();

        Assert.Equal(new[]
        {
            "[Organized Crime] Chapter 3 QA start key pressed.",
            "Chapter 3 QA start refused: Chapter 3 is not active.",
        }, logs);
    }

    [Fact]
    public void F1_alternates_dump_and_decrement()
    {
        var (root, _, logs) = CreateRoot();
        root.CourtesyWorld = new CompositionThrowingCourtesyWorld();
        var press = HandlerFor(root, KeyCode.F1);

        press();
        press();
        press();

        Assert.Equal(new[]
        {
            "[Organized Crime] OC-60 cash gate proof (dump): status Faulted",
            "[Organized Crime] OC-60 cash gate proof (decrement): status Faulted",
            "[Organized Crime] OC-60 cash gate proof (dump): status Faulted",
        }, logs.Where(line => line.Contains("cash gate proof (", StringComparison.Ordinal)));
    }

    [Fact]
    public void F2_alternates_dump_and_decrement()
    {
        var (root, _, logs) = CreateRoot();
        root.CourtesyWorld = new CompositionThrowingCourtesyWorld();
        var press = HandlerFor(root, KeyCode.F2);

        press();
        press();

        Assert.Equal(new[]
        {
            "[Organized Crime] OC-61 closet cash gate proof (dump): status Faulted",
            "[Organized Crime] OC-61 closet cash gate proof (decrement): status Faulted",
        }, logs.Where(line => line.Contains("closet cash gate proof (", StringComparison.Ordinal)));
    }

    [Fact]
    public void Home_alternates_dump_and_cross_check()
    {
        var (root, _, logs) = CreateRoot();
        root.CourtesyWorld = new CompositionThrowingCourtesyWorld();
        var press = HandlerFor(root, KeyCode.Home);

        press();
        press();

        Assert.Equal(new[]
        {
            "[Organized Crime] OC-65 production gate proof (dump): status Faulted",
            "[Organized Crime] OC-65 production gate proof (cross check): status Faulted",
        }, logs.Where(line => line.Contains("production gate proof (", StringComparison.Ordinal)));
    }

    [Fact]
    public void End_releases_lockdown_while_active()
    {
        var (root, engine, logs) = CreateRoot();
        var world = new LockdownWorld();
        root.CourtesyWorld = world;
        engine.IsLockdownActiveResult = true;

        HandlerFor(root, KeyCode.End)();

        Assert.Equal(new[] { "release" }, world.Calls);
        Assert.Equal(new[]
        {
            "[Organized Crime] OC-73 lockdown gate: status Succeeded",
            "[Organized Crime] OC-73 lockdown gate: released",
        }, logs);
    }

    [Fact]
    public void End_engages_lockdown_while_inactive()
    {
        var (root, engine, logs) = CreateRoot();
        var world = new LockdownWorld();
        root.CourtesyWorld = world;
        engine.IsLockdownActiveResult = false;

        HandlerFor(root, KeyCode.End)();

        Assert.Equal(new[] { "engage" }, world.Calls);
        Assert.Equal(new[]
        {
            "[Organized Crime] OC-73 lockdown gate: status Succeeded",
            "[Organized Crime] OC-73 lockdown gate: engaged",
        }, logs);
    }

    [Fact]
    public void World_keys_skip_quietly_without_a_world()
    {
        var (root, _, logs) = CreateRoot();

        foreach (var key in new[] { KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F6, KeyCode.F1, KeyCode.F2, KeyCode.Home, KeyCode.End })
            HandlerFor(root, key)();

        Assert.Empty(logs);
    }

    [Fact]
    public void F8_skips_quietly_without_context_or_story()
    {
        var (root, _, logs) = CreateRoot();

        HandlerFor(root, KeyCode.F8)();
        Assert.Empty(logs);

        root.Story = new Release1StoryRuntimeService(
            new CompositionFakeStoryContext(),
            _ => new CompositionFakeRelease1StoryRepository());
        HandlerFor(root, KeyCode.F8)();

        Assert.Empty(logs);
    }

    [Fact]
    public void Staging_hold_room_and_quantity_execute_their_harnesses()
    {
        var (root, _, logs) = CreateRoot();
        root.CourtesyWorld = new CompositionThrowingCourtesyWorld();

        HandlerFor(root, KeyCode.F9)();
        HandlerFor(root, KeyCode.F11)();
        HandlerFor(root, KeyCode.F6)();

        Assert.Contains("[Organized Crime] OC-52 staging proof: stage status Faulted", logs);
        Assert.Contains("[Organized Crime] OC-57 hold room proof: dump status Faulted", logs);
        Assert.Contains("[Organized Crime] OC-58 quantity proof: status Faulted", logs);
    }

    [Fact]
    public void Insert_executes_the_npc_registry_dump()
    {
        var (root, _, logs) = CreateRoot();

        HandlerFor(root, KeyCode.Insert)();

        Assert.StartsWith("[Organized Crime] OC-56 npc registry: status ", logs[0]);
    }

    [Fact]
    public void F10_dumps_staging_before_the_world_reads()
    {
        var (root, _, logs) = CreateRoot();
        root.CourtesyWorld = new CompositionThrowingCourtesyWorld();

        HandlerFor(root, KeyCode.F10)();

        Assert.Equal("[Organized Crime] OC-52 staging proof: dump status Faulted", logs[0]);
    }

    private static (OcCompositionRoot Root, FakeOcEngineAdapters Engine, List<string> Logs) CreateRoot()
    {
        var engine = new FakeOcEngineAdapters();
        var logs = new List<string>();
        var root = new OcCompositionRoot(engine, logs.Add, _ => { });
        return (root, engine, logs);
    }

    private static Action HandlerFor(OcCompositionRoot root, KeyCode key) =>
        root.QaKeys.Bindings.Single(binding => binding.Key == key).Handler;

    private sealed class RecordingPropertyHost : IRuntimePropertyHost
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public RuntimePropertyHostResult Start(RuntimePropertyDefinition definition)
        {
            StartCalls++;
            return new RuntimePropertyHostResult(true, "spawn", true, true, true, false, true, true, true, null);
        }

        public RuntimePropertyHostResult Stop()
        {
            StopCalls++;
            return new RuntimePropertyHostResult(true, "stop", true, true, false, true, true, true, true, null);
        }

        public bool TrySetOwned() => true;
        public bool PersistenceRuntimeReady => true;
        public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId)
        {
        }

        public void MarkPersistenceObjectsReplayed()
        {
        }

        public bool TryAdoptRestoredEmployeeHome() => false;
        public void UpdateOwnedFeatures()
        {
        }

        public FishWarehouseContinuousNavigationState EmployeeNavigationState => FishWarehouseContinuousNavigationState.Inactive;
        public bool EmployeeNavigationReady => true;
        public int? EmployeeNavigationAgentTypeId => null;
    }

    private sealed class LockdownWorld : Release1FullWorldStub
    {
        public List<string> Calls { get; } = new();

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new NotSupportedException();

        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            Calls.Add("engage");
            reason = "engaged";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            Calls.Add("release");
            reason = "released";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
    }
}
