// OC-151: unit tests for the lifecycle fan-out. Execution tests run the fanout against
// hand-built recording tables (phase order, host gating, throw shape, update timers);
// table tests assert the production step names, slot wiring and the untimed storage
// cycle on LifecycleTables.ForRoot. The enabled-timing test proves child names flow
// into nested Measure receipts.
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LifecycleFanoutTests
{
    private static LifecycleFanout CreateFanout(LifecycleTables tables, FakeOcEngineAdapters engine) =>
        new(tables, engine, () => OrganizedCrimeTimingReceipts.Disabled);

    private static LifecycleStep Rec(List<string> ran, string name) =>
        new(name, () => ran.Add(name));

    private static Action RecSlot(List<string> ran, string name) =>
        () => ran.Add(name);

    [Fact]
    public void SaveComplete_runs_steps_in_order_on_host_and_reads_host_once()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = true };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            SaveComplete = new[] { Rec(ran, "a"), Rec(ran, "b"), Rec(ran, "c") },
        }, engine);

        fanout.HandleSaveComplete();

        Assert.Equal(new[] { "a", "b", "c" }, ran);
        Assert.Equal(1, engine.IsHostCalls);
    }

    [Fact]
    public void SaveComplete_skips_steps_off_host_but_still_reads_host_once()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            SaveComplete = new[] { Rec(ran, "a"), Rec(ran, "b") },
        }, engine);

        fanout.HandleSaveComplete();

        Assert.Empty(ran);
        Assert.Equal(1, engine.IsHostCalls);
    }

    [Fact]
    public void SaveStart_runs_steps_ungated_without_reading_host()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            SaveStart = new[] { Rec(ran, "a"), Rec(ran, "b") },
        }, engine);

        fanout.HandleSaveStart();

        Assert.Equal(new[] { "a", "b" }, ran);
        Assert.Equal(0, engine.IsHostCalls);
    }

    [Fact]
    public void PreLoad_runs_the_untimed_cycle_first_then_timed_steps_in_order()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            PreLoad = new[]
            {
                new LifecycleStep("cycle", () => ran.Add("cycle"), LifecycleStepKind.Untimed),
                Rec(ran, "a"),
                Rec(ran, "b"),
            },
        }, engine);

        fanout.HandlePreLoad();

        Assert.Equal(new[] { "cycle", "a", "b" }, ran);
        Assert.Equal(0, engine.IsHostCalls);
    }

    [Fact]
    public void LoadComplete_runs_prepare_main_hq_fish_and_tail_in_order()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = true };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            LoadCompletePrepare = new LifecycleGateStep("prepare", () => { ran.Add("prepare"); return true; }),
            LoadCompleteMain = new[] { Rec(ran, "a"), Rec(ran, "b") },
            LoadCompleteHq = Rec(ran, "hq"),
            LoadCompleteFish = Rec(ran, "fish"),
            LoadCompleteTail = () => ran.Add("tail"),
        }, engine);

        fanout.HandleLoadComplete();

        Assert.Equal(new[] { "prepare", "a", "b", "hq", "fish", "tail" }, ran);
    }

    [Fact]
    public void LoadComplete_skips_hq_when_prepare_fails()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = true };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            LoadCompletePrepare = new LifecycleGateStep("prepare", () => { ran.Add("prepare"); return false; }),
            LoadCompleteMain = new[] { Rec(ran, "a") },
            LoadCompleteHq = Rec(ran, "hq"),
            LoadCompleteFish = Rec(ran, "fish"),
            LoadCompleteTail = () => ran.Add("tail"),
        }, engine);

        fanout.HandleLoadComplete();

        Assert.Equal(new[] { "prepare", "a", "fish", "tail" }, ran);
    }

    [Fact]
    public void LoadComplete_skips_fish_off_host_but_runs_the_rest()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            LoadCompletePrepare = new LifecycleGateStep("prepare", () => { ran.Add("prepare"); return true; }),
            LoadCompleteMain = new[] { Rec(ran, "a") },
            LoadCompleteHq = Rec(ran, "hq"),
            LoadCompleteFish = Rec(ran, "fish"),
            LoadCompleteTail = () => ran.Add("tail"),
        }, engine);

        fanout.HandleLoadComplete();

        Assert.Equal(new[] { "prepare", "a", "hq", "tail" }, ran);
    }

    [Fact]
    public void LoadComplete_skips_the_tail_when_a_main_step_throws()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = true };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            LoadCompleteMain = new[]
            {
                Rec(ran, "a"),
                new LifecycleStep("boom", () => throw new InvalidOperationException("boom")),
                Rec(ran, "c"),
            },
            LoadCompleteTail = () => ran.Add("tail"),
        }, engine);

        var thrown = Assert.Throws<InvalidOperationException>(() => fanout.HandleLoadComplete());

        Assert.Equal("boom", thrown.Message);
        Assert.Equal(new[] { "a" }, ran);
    }

    [Fact]
    public void PreSceneChange_runs_steps_in_order()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            PreSceneChange = new[] { Rec(ran, "a"), Rec(ran, "b") },
        }, engine);

        fanout.HandlePreSceneChange();

        Assert.Equal(new[] { "a", "b" }, ran);
        Assert.Equal(0, engine.IsHostCalls);
    }

    [Fact]
    public void Throwing_step_aborts_the_rest_of_its_phase()
    {
        var engine = new FakeOcEngineAdapters();
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            SaveStart = new[]
            {
                Rec(ran, "a"),
                new LifecycleStep("boom", () => throw new InvalidOperationException("boom")),
                Rec(ran, "c"),
            },
        }, engine);

        var thrown = Assert.Throws<InvalidOperationException>(() => fanout.HandleSaveStart());

        Assert.Equal("boom", thrown.Message);
        Assert.Equal(new[] { "a" }, ran);
    }

    [Fact]
    public void Root_tables_carry_current_phase_names_in_order()
    {
        var root = new OcCompositionRoot(new FakeOcEngineAdapters(), _ => { }, _ => { });

        CompositionAssertions.AssertPhaseTables(LifecycleTables.ForRoot(root));
    }

    [Fact]
    public void Root_update_slots_are_all_wired()
    {
        var root = new OcCompositionRoot(new FakeOcEngineAdapters(), _ => { }, _ => { });

        CompositionAssertions.AssertUpdateSlotsWired(LifecycleTables.ForRoot(root).Update);
    }

    [Fact]
    public void Root_roster_slot_drives_the_roster_and_the_checkpoints_slot_does_not()
    {
        var engine = new FakeOcEngineAdapters();
        var root = new OcCompositionRoot(engine, _ => { }, _ => { });
        var world = new CompositionFakeOfficerWorld();
        root.OfficerRoster = new OcOfficerRosterService(world, "oc151-test", _ => { });
        var slots = LifecycleTables.ForRoot(root).Update;

        slots.RosterUpdate!();

        Assert.Equal(1, world.IsHostReads);

        slots.CheckpointsUpdate!();

        Assert.Equal(1, world.IsHostReads);
    }

    [Fact]
    public void Root_qa_slot_polls_through_the_engine_typing_seam()
    {
        var engine = new FakeOcEngineAdapters();
        var root = new OcCompositionRoot(engine, _ => { }, _ => { });

        LifecycleTables.ForRoot(root).Update.QaPoll!();

        Assert.Equal(1, engine.IsPlayerTypingCalls);
        Assert.Empty(engine.IsKeyDownCalls);
    }

    [Fact]
    public void Update_runs_slots_in_order()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = true, DeltaTimeDefault = 0f };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            Update = new LifecycleSlots
            {
                HqUpdate = RecSlot(ran, "hq"),
                ProductionUpdate = RecSlot(ran, "production"),
                ProjectorReconcile = RecSlot(ran, "projector"),
                ArthurPoll = RecSlot(ran, "arthur"),
                EmergencyPoll = RecSlot(ran, "emergency"),
                ChiefUpdate = RecSlot(ran, "chief"),
                ReadinessPump = RecSlot(ran, "readiness"),
                WarehouseUpdate = RecSlot(ran, "warehouse"),
                ReplayUpdate = RecSlot(ran, "replay"),
                WarehouseBuildPoll = RecSlot(ran, "build"),
                DemandPoll = RecSlot(ran, "demand"),
                RosterUpdate = RecSlot(ran, "roster"),
                CheckpointsUpdate = RecSlot(ran, "checkpoints"),
                EnsureUi = RecSlot(ran, "ensure-ui"),
                QaPoll = RecSlot(ran, "qa"),
            },
        }, engine);

        fanout.Update();

        Assert.Equal(new[]
        {
            "hq", "production", "projector", "arthur", "chief", "readiness",
            "warehouse", "replay", "build", "roster", "checkpoints",
            "ensure-ui", "qa",
        }, ran);
        Assert.Equal(1, engine.IsHostCalls);
    }

    [Fact]
    public void Update_skips_the_host_section_off_host()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false, DeltaTimeDefault = 10f };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            Update = new LifecycleSlots
            {
                HqUpdate = RecSlot(ran, "hq"),
                ProductionUpdate = RecSlot(ran, "production"),
                ProjectorReconcile = RecSlot(ran, "projector"),
                ArthurPoll = RecSlot(ran, "arthur"),
                EmergencyPoll = RecSlot(ran, "emergency"),
                ChiefUpdate = RecSlot(ran, "chief"),
                ReadinessPump = RecSlot(ran, "readiness"),
                WarehouseUpdate = RecSlot(ran, "warehouse"),
                ReplayUpdate = RecSlot(ran, "replay"),
                WarehouseBuildPoll = RecSlot(ran, "build"),
                DemandPoll = RecSlot(ran, "demand"),
                RosterUpdate = RecSlot(ran, "roster"),
                CheckpointsUpdate = RecSlot(ran, "checkpoints"),
                EnsureUi = RecSlot(ran, "ensure-ui"),
                QaPoll = RecSlot(ran, "qa"),
            },
        }, engine);

        fanout.Update();

        Assert.Equal(new[] { "hq", "production", "projector", "arthur", "emergency", "chief", "readiness", "ensure-ui", "qa" }, ran);
        Assert.Equal(1, engine.IsHostCalls);
    }

    [Fact]
    public void Update_fires_emergency_poll_at_1hz_and_discards_overshoot()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false };
        engine.DeltaTimeValues.Enqueue(0.6f);
        engine.DeltaTimeValues.Enqueue(0.6f);
        engine.DeltaTimeValues.Enqueue(0.6f);
        engine.DeltaTimeValues.Enqueue(2.5f);
        engine.DeltaTimeValues.Enqueue(0.1f);
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            Update = new LifecycleSlots { EmergencyPoll = RecSlot(ran, "emergency") },
        }, engine);

        fanout.Update();
        Assert.Empty(ran);
        fanout.Update();
        Assert.Equal(new[] { "emergency" }, ran);
        fanout.Update();
        Assert.Equal(new[] { "emergency" }, ran);
        fanout.Update();
        Assert.Equal(new[] { "emergency", "emergency" }, ran);
        fanout.Update();
        Assert.Equal(new[] { "emergency", "emergency" }, ran);
    }

    [Fact]
    public void Update_fires_demand_poll_at_1hz_on_host_only()
    {
        var engine = new FakeOcEngineAdapters { IsHostResult = false, DeltaTimeDefault = 0.6f };
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            Update = new LifecycleSlots { DemandPoll = RecSlot(ran, "demand") },
        }, engine);

        for (var i = 0; i < 10; i++)
            fanout.Update();
        Assert.Empty(ran);

        engine.IsHostResult = true;
        fanout.Update();
        Assert.Empty(ran);
        fanout.Update();
        Assert.Equal(new[] { "demand" }, ran);
    }

    [Fact]
    public void OnGUI_runs_hq_before_production()
    {
        var engine = new FakeOcEngineAdapters();
        var ran = new List<string>();
        var fanout = CreateFanout(new LifecycleTables
        {
            Update = new LifecycleSlots
            {
                HqGui = RecSlot(ran, "hq"),
                ProductionGui = RecSlot(ran, "production"),
            },
        }, engine);

        fanout.OnGUI();

        Assert.Equal(new[] { "hq", "production" }, ran);
    }

    [Fact]
    public void Enabled_timing_reports_nested_receipt_names()
    {
        var engine = new FakeOcEngineAdapters();
        var receipts = new List<string>();
        var timing = new OrganizedCrimeTimingReceipts(receipts.Add, 1);
        var fanout = new LifecycleFanout(new LifecycleTables
        {
            SaveStart = new[] { Slow("slow-a"), Slow("slow-b") },
        }, engine, () => timing);

        fanout.HandleSaveStart();

        Assert.Equal(3, receipts.Count);
        Assert.StartsWith("[Organized Crime] timing: slow-a ", receipts[0]);
        Assert.StartsWith("[Organized Crime] timing: slow-b ", receipts[1]);
        Assert.StartsWith("[Organized Crime] timing: save-start ", receipts[2]);
    }

    private static LifecycleStep Slow(string name) =>
        new(name, () =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalMilliseconds < 5)
            {
            }
        });
}
