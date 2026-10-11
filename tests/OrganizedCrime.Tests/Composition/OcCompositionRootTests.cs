// OC-151: integration tests for the composition root. Each test builds the full graph
// through the recording fake (CompositionScope restores the statics Build assigns)
// and pins one facet: the exact setup/teardown traces, patch and subscribe order,
// pref flow, graph identity, protection-gate lambdas, and the slots the fanout drives.
using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class OcCompositionRootTests
{
    [Fact]
    public void Setup_trace_matches_exact_order()
    {
        using var scope = new CompositionScope();

        CompositionAssertions.AssertSetupTrace(scope.Engine);
        CompositionAssertions.AssertImguiPrefReads(scope.Engine);
    }

    [Fact]
    public void Native_setup_adds_projector_and_native_builds()
    {
        using var scope = new CompositionScope(engine => engine.PresentationModeResult = Release1PresentationMode.Native);

        CompositionAssertions.AssertNativeSetupTrace(scope.Engine);
        CompositionAssertions.AssertNativePrefReads(scope.Engine);
        Assert.NotNull(scope.Root.Chapter3Projector);
        Assert.NotNull(scope.Root.Production!.Projector);
        Assert.NotNull(scope.Root.Chief!.Projector);
    }

    [Fact]
    public void Imgui_setup_builds_no_projector_and_reads_no_policy()
    {
        using var scope = new CompositionScope();

        Assert.Null(scope.Root.Chapter3Projector);
        Assert.Null(scope.Root.Production!.Projector);
        Assert.NotNull(scope.Root.Production.DecisionPromptHost);
        Assert.Null(scope.Root.Chief!.Projector);
        Assert.Equal(0, scope.Engine.QuestPolicyReads);
        Assert.Contains("[Organized Crime] Chapter 3 has no fallback presenter; Nell's messages stay pending while the presentation mode is ImguiFallback.", scope.Logs);
    }

    [Fact]
    public void Timing_pref_flows_into_the_root_timing()
    {
        using var enabled = new CompositionScope(engine => engine.TimingThresholdResult = 25);

        Assert.Same(enabled.Root.Timing, enabled.Root.Story!.Timing);
        Assert.Same(enabled.Root.Timing, enabled.Root.Production!.Timing);
        Assert.Same(enabled.Root.Timing, enabled.Root.Production.KeepTheLightsOff.Service.Timing);
        Assert.Same(enabled.Root.Timing, enabled.Root.Production.TheEnvelope.Service.Timing);
        Assert.Contains("[Organized Crime] Timing receipts report any phase at or above 25 ms.", enabled.Logs);

        using var disabled = new CompositionScope();

        Assert.Equal(0, disabled.Root.Timing.ThresholdMs);
        Assert.False(disabled.Root.Timing.IsEnabled);
        Assert.DoesNotContain(disabled.Logs, line => line.Contains("Timing receipts report", StringComparison.Ordinal));
    }

    [Fact]
    public void Owner_qa_keys_pref_flows_into_the_root()
    {
        using var on = new CompositionScope(engine => engine.OwnerQaKeysResult = true);

        Assert.True(on.Root.OwnerQaKeysEnabled);
        Assert.Contains("[Organized Crime] Owner QA keys are enabled by preference; F8 recognition and fish warehouse developer keys are live.", on.Logs);

        using var off = new CompositionScope();

        Assert.False(off.Root.OwnerQaKeysEnabled);
        Assert.DoesNotContain(off.Logs, line => line.Contains("Owner QA keys are enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void Owner_qa_console_registration_failure_does_not_abort_build()
    {
        using var scope = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.ThrowOn.Add("register:OwnerQaConsoleCommand");
        });

        Assert.Equal(1, scope.Engine.OwnerQaConsoleRegistrations);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(scope.Root.Chief);
        Assert.NotNull(scope.Root.Checkpoints);
        CompositionAssertions.AssertSubscribeCalls(scope.Engine);
        CompositionAssertions.AssertPatchCalls(scope.Engine);
        Assert.Equal("[Organized Crime] Chapter 3 QA command could not be registered on this S1API version.", Assert.Single(scope.Warnings));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Owner_qa_console_registration_runs_only_when_enabled(bool enabled, int expectedCalls)
    {
        using var scope = new CompositionScope(engine => engine.OwnerQaKeysResult = enabled);

        Assert.Equal(expectedCalls, scope.Engine.OwnerQaConsoleRegistrations);
        Assert.Equal(expectedCalls, scope.Engine.Trace.Count(entry => entry == "register:OwnerQaConsoleCommand"));
    }

    [Fact]
    public void Owner_qa_console_constructor_prevents_public_auto_discovery()
    {
        var source = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Chapter3QaConsoleCommand.cs");

        Assert.Matches(@"private\s+Chapter3QaConsoleCommand\s*\(\s*\)", source);
        Assert.DoesNotMatch(@"public\s+Chapter3QaConsoleCommand\s*\(\s*\)", source);
    }

    [Fact]
    public void Graph_wires_the_chapter3_worlds_and_links()
    {
        using var scope = new CompositionScope();
        var chapter3 = scope.Root.Chapter3!;

        Assert.Same(scope.Engine.ArthurWorld, chapter3.ArthurWorld);
        Assert.NotNull(chapter3.ArthurClock);
        Assert.Same(scope.Engine.BeatProbeWorld, chapter3.BeatProbeWorld);
        Assert.Same(scope.Engine.BeatingWorld, chapter3.BeatingWorld);
        Assert.Same(scope.Root.OfficerRoster, chapter3.OfficerRoster);
        Assert.NotNull(chapter3.Wallet);
        Assert.Same(scope.Root.CourtesyWorld, chapter3.DeadDropWorld);
        Assert.Same(scope.Engine.DemandWorld, chapter3.DemandWorld);
        Assert.Same(scope.Engine.VisitConditions, chapter3.VisitConditions);
        Assert.Same(scope.Engine.ArthurPhone, chapter3.ArthurPhone);
        Assert.NotNull(chapter3.HqPresence);
        Assert.NotNull(chapter3.BeatingHq);
        Assert.Same(scope.Root.Chief!.Service, chapter3.ChiefProtectionQa);
        Assert.Same(scope.Root.Chief.Service, chapter3.ChiefForcedWantedQa);
        Assert.NotNull(chapter3.ProtectionSnapshot);
        Assert.NotNull(chapter3.WarehouseAcquisition);
        Assert.NotNull(chapter3.WarehouseCapacity);
        Assert.NotNull(chapter3.StaffReturnBannerSender);
        Assert.Same(scope.Root.Checkpoints, chapter3.Checkpoints);
        Assert.Same(chapter3, Chapter3QaAccess.Service);
    }

    [Fact]
    public void BuildCore_assigns_collaborators_in_construction_order_around_the_patch()
    {
        // Chapter3Module.BuildCore constructs the service and publishes QA
        // access first, then assigns each collaborator via setters around the patch call.
        // This pins the exact construction/publication/setter order.
        var module = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "Chapter3Module.cs");
        var buildCore = CompositionAssertions.Slice(module, "public static Chapter3CoreOutputs BuildCore", "public static void BuildWallet");

        AssertOrdered(buildCore,
            "engine.CreateDayClock",
            "new Chapter3RuntimeService(",
            "step:Chapter3.Service",
            "Chapter3QaAccess.Service = service;",
            "step:Chapter3.QaAccess",
            "engine.CreateArthurRuntime",
            "service.ArthurWorld = arthur;",
            "engine.ApplyArthurResponse",
            "service.ArthurClock =",
            "engine.CreateBeatProbeWorld",
            "service.BeatProbeWorld =",
            "engine.CreateBeatingWorld",
            "service.BeatingWorld =",
            "engine.CreateOfficerWorld",
            "new OcOfficerRosterService(",
            "step:Chapter3.Roster",
            "service.OfficerRoster = roster;",
            "engine.CreateOvernightLine");
    }

    [Fact]
    public void Graph_wires_production_chief_and_law()
    {
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Root.Production);
        Assert.NotNull(scope.Root.Production!.SmallCourtesy);
        Assert.NotNull(scope.Root.Chief);
        Assert.NotNull(scope.Root.Law);
        Assert.Same(scope.Root.Law!.Controller, scope.Root.LawController);
        Assert.Same(scope.Root.Law.LocalPressureRuntime, scope.Root.LocalPressure);
        Assert.NotNull(scope.Root.CustodyBridge);
        Assert.NotNull(scope.Root.HqContext);
        Assert.NotNull(scope.Root.HqStorage);
        Assert.NotNull(scope.Root.Hq);
    }

    [Fact]
    public void Graph_wires_the_warehouse_chain()
    {
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Root.WarehouseRuntime);
        Assert.NotNull(scope.Root.WarehousePersistence);
        Assert.NotNull(scope.Root.WarehouseCapture);
        Assert.NotNull(scope.Root.WarehouseReplay);
        Assert.NotNull(scope.Root.PropertyHost!.EmployeeCapacitySource);
    }

    [Fact]
    public void Patches_apply_in_order_without_a_lockdown_apply()
    {
        using var scope = new CompositionScope();

        CompositionAssertions.AssertPatchCalls(scope.Engine);
        Assert.DoesNotContain(scope.Engine.Trace, entry => entry.Contains("Lockdown", StringComparison.Ordinal));
    }

    [Fact]
    public void Lifecycle_handlers_subscribe_all_five_events_in_order()
    {
        using var scope = new CompositionScope();

        CompositionAssertions.AssertSubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Teardown_trace_matches_exact_order_and_nulls_the_graph()
    {
        using var scope = new CompositionScope();
        var overnight = scope.Engine.OvernightLine;

        scope.Root.Teardown();

        CompositionAssertions.AssertTeardownTrace(scope.Engine, CompositionAssertions.ExpectedImguiSetupTrace);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
        Assert.Null(scope.Root.Story);
        Assert.Null(scope.Root.Production);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.CourtesyWorld);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(scope.Root.Chapter3Projector);
        Assert.Null(scope.Root.Checkpoints);
        Assert.Null(scope.Root.OfficerRoster);
        Assert.Null(scope.Root.Arthur);
        Assert.Null(scope.Root.DayClock);
        Assert.Null(scope.Root.Hq);
        Assert.Null(scope.Root.HqStorage);
        Assert.Null(scope.Root.HqContext);
        Assert.Null(scope.Root.OvernightLine);
        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.LawController);
        Assert.Null(scope.Root.LocalPressure);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Null(scope.Root.WarehouseReplay);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.PropertyHost);
        Assert.Null(Chapter3QaAccess.Service);
        Assert.Equal(1, overnight.DisposeCalls);
        Assert.Equal(new[] { "dispose:Arthur", "dispose:DayClock" }, scope.Engine.DisposeCalls);
    }

    [Fact]
    public void Teardown_is_safe_to_run_twice()
    {
        using var scope = new CompositionScope();

        scope.Root.Teardown();
        var resets = scope.Engine.ResetCalls.Count;
        var unsubscribes = scope.Engine.UnsubscribeCalls.Count;

        scope.Root.Teardown();

        Assert.Equal(2 * resets, scope.Engine.ResetCalls.Count);
        Assert.Equal(2 * unsubscribes, scope.Engine.UnsubscribeCalls.Count);
    }

    [Fact]
    public void Throw_mid_build_aborts_later_steps_and_keeps_partial_state()
    {
        CompositionAssertions.AssertThrowPartialKeepsPartialState();
    }

    [Fact]
    public void Throw_at_overnight_keeps_chapter3_partial_and_teardown_cleans_it()
    {
        // A late throw inside Chapter3Module must leave DayClock,
        // Arthur, OfficerRoster and Chapter3 published (Chapter3Module.BuildCore assigns each
        // at its construction point), and teardown must dispose exactly those.
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:Overnight"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.DayClock);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(scope.Root.Arthur);
        Assert.NotNull(scope.Root.OfficerRoster);
        Assert.Null(scope.Root.OvernightLine);
        Assert.Null(scope.Root.Production);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.DoesNotContain("factory:Overnight", scope.Engine.Trace);

        var overnight = scope.Engine.OvernightLine;
        scope.Root.Teardown();

        Assert.Null(scope.Root.DayClock);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(scope.Root.Arthur);
        Assert.Null(scope.Root.OfficerRoster);
        Assert.Null(Chapter3QaAccess.Service);
        Assert.Equal(0, overnight.DisposeCalls);
        Assert.Contains("dispose:Arthur", scope.Engine.DisposeCalls);
        Assert.Contains("dispose:DayClock", scope.Engine.DisposeCalls);
    }

    [Fact]
    public void Throw_at_arthur_keeps_service_and_dayclock_and_teardown_cleans_them()
    {
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:Arthur"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.DayClock);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(Chapter3QaAccess.Service);
        Assert.Null(scope.Root.Arthur);
        Assert.Null(scope.Root.OfficerRoster);
        Assert.Null(scope.Root.OvernightLine);

        scope.Root.Teardown();

        Assert.Null(scope.Root.DayClock);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(Chapter3QaAccess.Service);
        Assert.Contains("dispose:DayClock", scope.Engine.DisposeCalls);
        Assert.DoesNotContain("dispose:Arthur", scope.Engine.DisposeCalls);
    }

    [Fact]
    public void Throw_at_officerworld_keeps_arthur_and_teardown_cleans_it()
    {
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:OfficerWorld"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.DayClock);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(scope.Root.Arthur);
        Assert.Null(scope.Root.OfficerRoster);
        Assert.Null(scope.Root.OvernightLine);

        scope.Root.Teardown();

        Assert.Null(scope.Root.DayClock);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(scope.Root.Arthur);
        Assert.Contains("dispose:Arthur", scope.Engine.DisposeCalls);
        Assert.Contains("dispose:DayClock", scope.Engine.DisposeCalls);
    }

    [Fact]
    public void Throw_at_warehouse_loadcapture_keeps_all_warehouse_fields_and_teardown_resets_capture()
    {
        // A late throw inside WarehouseModule must leave PropertyHost,
        // Runtime, Persistence, Capture and Replay published, and teardown must Reset the
        // capture (WarehouseModule.Build assigns each at its construction point).
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("apply:WarehouseLoadCapture"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.PropertyHost);
        Assert.NotNull(scope.Root.WarehouseRuntime);
        Assert.NotNull(scope.Root.WarehousePersistence);
        Assert.NotNull(scope.Root.WarehouseCapture);
        Assert.NotNull(scope.Root.WarehouseReplay);
        Assert.Empty(scope.Engine.SubscribeCalls);

        var capture = scope.Root.WarehouseCapture!;
        scope.Root.Teardown();

        Assert.Null(scope.Root.PropertyHost);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.WarehouseReplay);
        Assert.Equal(OrganizedCrime.Runtime.FishWarehousePropertyCaptureState.Inactive, capture.State);
        Assert.Contains("teardown:Capture.Reset", scope.Engine.Trace);
    }

    [Fact]
    public void Throw_at_warehouse_deliveryrestore_keeps_only_propertyhost()
    {
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("apply:WarehouseDeliveryRestore"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.PropertyHost);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.WarehouseReplay);

        scope.Root.Teardown();

        Assert.Null(scope.Root.PropertyHost);
    }

    [Fact]
    public void Throw_at_propertyhost_keeps_no_warehouse_fields()
    {
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:PropertyHost"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.Null(scope.Root.PropertyHost);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.WarehouseReplay);
        Assert.NotNull(scope.Root.Chapter3);

        scope.Root.Teardown();

        Assert.Null(scope.Root.Chapter3);
    }

    [Fact]
    public void Throw_at_hqstorageload_keeps_storage_and_production_and_teardown_cleans_them()
    {
        // HqModule.ApplyStorageLoadPatch applies the HQ storage patch
        // after publishing HqStorage/CourtesyWorld/Production/Projector; a throw aborts
        // the HQ runtime, presence/demand links, warehouse, subscriptions and everything
        // after. Teardown still resets, unsubscribes and disposes unconditionally.
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("apply:HqStorageLoad"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.HqContext);
        Assert.NotNull(scope.Root.Story);
        Assert.NotNull(scope.Root.DayClock);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(scope.Root.Arthur);
        Assert.NotNull(scope.Root.OfficerRoster);
        Assert.NotNull(scope.Root.OvernightLine);
        Assert.NotNull(Chapter3QaAccess.Service);
        Assert.NotNull(scope.Root.HqStorage);
        Assert.NotNull(scope.Root.CourtesyWorld);
        Assert.NotNull(scope.Root.Production);
        Assert.Null(scope.Root.Chapter3Projector);
        Assert.Contains("[Organized Crime] Chapter 3 has no fallback presenter; Nell's messages stay pending while the presentation mode is ImguiFallback.", scope.Logs);
        var chapter3 = scope.Root.Chapter3!;
        Assert.NotNull(chapter3.Wallet);
        Assert.Same(scope.Root.CourtesyWorld, chapter3.DeadDropWorld);
        Assert.Null(chapter3.HqPresence);
        Assert.Null(chapter3.BeatingHq);
        Assert.Null(chapter3.DemandWorld);
        Assert.Null(chapter3.VisitConditions);
        Assert.Null(chapter3.ArthurPhone);
        Assert.Null(scope.Root.Hq);
        Assert.Null(scope.Root.PropertyHost);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.WarehouseReplay);
        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.LawController);
        Assert.Null(scope.Root.LocalPressure);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        Assert.Empty(scope.Engine.SubscribeCalls);
        Assert.Contains("step:Chapter3.Wallet", scope.Engine.Trace);
        Assert.DoesNotContain("apply:HqStorageLoad", scope.Engine.Trace);
        Assert.DoesNotContain("step:Hq.Observer", scope.Engine.Trace);
        Assert.DoesNotContain("step:Hq.Composition", scope.Engine.Trace);
        Assert.DoesNotContain("factory:Demand", scope.Engine.Trace);
        Assert.DoesNotContain("apply:WarehouseCanvas", scope.Engine.Trace);
        Assert.DoesNotContain("factory:LawAdapter", scope.Engine.Trace);
        Assert.DoesNotContain("step:Release1.Chief", scope.Engine.Trace);

        var overnight = scope.Engine.OvernightLine;
        scope.Root.Teardown();

        Assert.Null(scope.Root.HqContext);
        Assert.Null(scope.Root.Story);
        Assert.Null(scope.Root.DayClock);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(scope.Root.Arthur);
        Assert.Null(scope.Root.OfficerRoster);
        Assert.Null(scope.Root.OvernightLine);
        Assert.Null(Chapter3QaAccess.Service);
        Assert.Null(scope.Root.HqStorage);
        Assert.Null(scope.Root.CourtesyWorld);
        Assert.Null(scope.Root.Production);
        Assert.Equal(1, overnight.DisposeCalls);
        Assert.Equal(new[] { "dispose:Arthur", "dispose:DayClock" }, scope.Engine.DisposeCalls);
        Assert.Equal(
            new[]
            {
                "reset:LockdownGate",
                "reset:ProtectionGate",
                "reset:HqStorageLoad",
                "reset:Custody",
                "reset:WarehouseManagementTarget",
                "reset:WarehouseDeliveryRestore",
                "reset:WarehouseLoadCapture",
                "reset:WarehouseWriteGuard",
            },
            scope.Engine.ResetCalls);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Throw_at_lawadapter_keeps_subscriptions_and_warehouse_and_no_law()
    {
        // LawModule.BuildLaw builds the law composition after
        // the five subscriptions; a throw in the host adapter leaves Law/Controller/
        // Runtime unassigned and aborts the bridge, custody patch, chief and later steps.
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:LawAdapter"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.Hq);
        Assert.NotNull(scope.Root.HqStorage);
        Assert.NotNull(scope.Root.PropertyHost);
        Assert.NotNull(scope.Root.WarehouseRuntime);
        Assert.NotNull(scope.Root.WarehousePersistence);
        Assert.NotNull(scope.Root.WarehouseCapture);
        Assert.NotNull(scope.Root.WarehouseReplay);
        Assert.NotNull(scope.Root.Production);
        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.LawController);
        Assert.Null(scope.Root.LocalPressure);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        var chapter3 = scope.Root.Chapter3!;
        Assert.Same(scope.Engine.DemandWorld, chapter3.DemandWorld);
        Assert.Null(chapter3.ProtectionSnapshot);
        Assert.Null(chapter3.ChiefProtectionQa);
        Assert.Null(chapter3.ChiefForcedWantedQa);
        CompositionAssertions.AssertSubscribeCalls(scope.Engine);
        Assert.DoesNotContain("factory:LawAdapter", scope.Engine.Trace);
        Assert.DoesNotContain("step:Law.Composition", scope.Engine.Trace);
        Assert.DoesNotContain("step:Law.Bridge", scope.Engine.Trace);
        Assert.DoesNotContain("apply:Custody", scope.Engine.Trace);
        Assert.DoesNotContain("step:Release1.Chief", scope.Engine.Trace);
        Assert.DoesNotContain("step:Chapter3.Checkpoints", scope.Engine.Trace);
        Assert.DoesNotContain("configure:ProtectionGate", scope.Engine.Trace);

        var capture = scope.Root.WarehouseCapture!;
        var overnight = scope.Engine.OvernightLine;
        scope.Root.Teardown();

        Assert.Null(scope.Root.Hq);
        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.Chapter3);
        Assert.Null(Chapter3QaAccess.Service);
        Assert.Equal(OrganizedCrime.Runtime.FishWarehousePropertyCaptureState.Inactive, capture.State);
        Assert.Equal(1, overnight.DisposeCalls);
        Assert.Equal(new[] { "dispose:Arthur", "dispose:DayClock" }, scope.Engine.DisposeCalls);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Throw_at_custody_keeps_bridge_and_law_and_teardown_cleans_them()
    {
        // LawModule.BuildCustodyBridge assigns the custody bridge before
        // applying the patch (return discarded); a throw leaves the bridge published and
        // aborts the chief, checkpoints, protection gates and swap patch.
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("apply:Custody"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.Law);
        Assert.NotNull(scope.Root.LawController);
        Assert.NotNull(scope.Root.LocalPressure);
        Assert.NotNull(scope.Root.CustodyBridge);
        Assert.Same(scope.Root.Law!.Controller, scope.Root.LawController);
        Assert.Same(scope.Root.Law.LocalPressureRuntime, scope.Root.LocalPressure);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        CompositionAssertions.AssertSubscribeCalls(scope.Engine);
        Assert.Contains("step:Law.Bridge", scope.Engine.Trace);
        Assert.DoesNotContain("apply:Custody", scope.Engine.Trace);
        Assert.DoesNotContain("factory:ProtectionCheckpointWorld", scope.Engine.Trace);
        Assert.DoesNotContain("step:Release1.Chief", scope.Engine.Trace);
        Assert.DoesNotContain("step:Chapter3.Checkpoints", scope.Engine.Trace);
        Assert.DoesNotContain("apply:CheckpointSwap", scope.Engine.Trace);

        scope.Root.Teardown();

        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.LawController);
        Assert.Null(scope.Root.LocalPressure);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Contains("teardown:Custody.Dispose", scope.Engine.Trace);
        Assert.Contains("teardown:Law.Dispose", scope.Engine.Trace);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Throw_at_chief_exposurereader_keeps_law_and_custody_and_aborts_chief()
    {
        // Release1Module.BuildChief evaluates the chief composition
        // args left to right; a throw in the exposure reader leaves Chief unassigned
        // (the assignment never runs) after the checkpoint-world side effect, aborting
        // the later readers, chief links, checkpoints and gates.
        using var scope = new CompositionScope(engine => engine.ThrowOn.Add("factory:ExposureReader"), build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.Law);
        Assert.NotNull(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        var chapter3 = scope.Root.Chapter3!;
        Assert.Null(chapter3.ProtectionSnapshot);
        Assert.Null(chapter3.ChiefProtectionQa);
        Assert.Null(chapter3.ChiefForcedWantedQa);
        Assert.Null(chapter3.Checkpoints);
        Assert.Contains("factory:ProtectionCheckpointWorld", scope.Engine.Trace);
        Assert.DoesNotContain("factory:ExposureReader", scope.Engine.Trace);
        Assert.DoesNotContain("factory:CurfewReader", scope.Engine.Trace);
        Assert.DoesNotContain("factory:WantedWriter", scope.Engine.Trace);
        Assert.DoesNotContain("factory:PursuitHold", scope.Engine.Trace);
        Assert.DoesNotContain("step:Release1.Chief", scope.Engine.Trace);
        Assert.DoesNotContain("step:Chapter3.ChiefLinks", scope.Engine.Trace);
        Assert.DoesNotContain("factory:CheckpointWorld", scope.Engine.Trace);

        scope.Root.Teardown();

        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Throw_at_chiefnative_in_native_mode_reads_policy_twice_and_aborts_chief()
    {
        // Release1Module.BuildChief reads the quest
        // policy a second time before constructing the chief native presenter; a throw
        // leaves the policy read recorded but Chief unassigned, aborting the chief,
        // checkpoints and gates. The Native projector (built before HQ) stays published.
        using var scope = new CompositionScope(engine =>
        {
            engine.PresentationModeResult = Release1PresentationMode.Native;
            engine.ThrowOn.Add("factory:ChiefNative");
        }, build: false);

        Assert.Throws<InvalidOperationException>(() => scope.Root.Build());

        Assert.NotNull(scope.Root.Chapter3Projector);
        Assert.NotNull(scope.Root.Law);
        Assert.NotNull(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Checkpoints);
        Assert.Equal(2, scope.Engine.QuestPolicyReads);
        Assert.Contains("factory:Release1Native", scope.Engine.Trace);
        Assert.DoesNotContain("factory:ChiefNative", scope.Engine.Trace);
        Assert.DoesNotContain("factory:ProtectionCheckpointWorld", scope.Engine.Trace);
        Assert.DoesNotContain("step:Release1.Chief", scope.Engine.Trace);

        scope.Root.Teardown();

        Assert.Null(scope.Root.Chapter3Projector);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Law);
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Emergency_accumulator_survives_teardown_and_fires_after_rebuild()
    {
        // The mod keeps the 1 Hz accumulators on the MelonMod instance across
        // deinit/reinit. 0.6 s before teardown + 0.4 s after must still trigger a poll.
        // The fanout runs against recording slots here (a full root Update would drive
        // Unity-bound production services headless); the root-level Teardown preservation
        // is pinned below, and Mod.cs ownership is pinned by source.
        var lifetime = new OcCompositionLifetime();

        var engine1 = new FakeOcEngineAdapters { DeltaTimeDefault = 0.6f };
        var ran1 = new List<string>();
        var fanout1 = new LifecycleFanout(
            new LifecycleTables { Update = new LifecycleSlots { EmergencyPoll = () => ran1.Add("emergency") } },
            engine1,
            () => OrganizedCrimeTimingReceipts.Disabled,
            lifetime);
        fanout1.Update();
        Assert.Empty(ran1);
        Assert.Equal(0.6f, lifetime.EmergencyPollSeconds, precision: 4);

        var engine2 = new FakeOcEngineAdapters { DeltaTimeDefault = 0.4f };
        var ran2 = new List<string>();
        var fanout2 = new LifecycleFanout(
            new LifecycleTables { Update = new LifecycleSlots { EmergencyPoll = () => ran2.Add("emergency") } },
            engine2,
            () => OrganizedCrimeTimingReceipts.Disabled,
            lifetime);
        fanout2.Update();
        Assert.Equal(new[] { "emergency" }, ran2);
        Assert.Equal(0f, lifetime.EmergencyPollSeconds, precision: 4);
    }

    [Fact]
    public void Demand_accumulator_survives_teardown_and_fires_after_rebuild_on_host()
    {
        var lifetime = new OcCompositionLifetime();

        var engine1 = new FakeOcEngineAdapters { DeltaTimeDefault = 0.6f, IsHostResult = true };
        var ran1 = new List<string>();
        var fanout1 = new LifecycleFanout(
            new LifecycleTables { Update = new LifecycleSlots { DemandPoll = () => ran1.Add("demand") } },
            engine1,
            () => OrganizedCrimeTimingReceipts.Disabled,
            lifetime);
        fanout1.Update();
        Assert.Empty(ran1);
        Assert.Equal(0.6f, lifetime.DemandPollSeconds, precision: 4);

        var engine2 = new FakeOcEngineAdapters { DeltaTimeDefault = 0.4f, IsHostResult = true };
        var ran2 = new List<string>();
        var fanout2 = new LifecycleFanout(
            new LifecycleTables { Update = new LifecycleSlots { DemandPoll = () => ran2.Add("demand") } },
            engine2,
            () => OrganizedCrimeTimingReceipts.Disabled,
            lifetime);
        fanout2.Update();
        Assert.Equal(new[] { "demand" }, ran2);
        Assert.Equal(0f, lifetime.DemandPollSeconds, precision: 4);
    }

    [Fact]
    public void Teardown_leaves_lifetime_accumulators_and_toggles_untouched()
    {
        var lifetime = new OcCompositionLifetime
        {
            EmergencyPollSeconds = 0.6f,
            DemandPollSeconds = 0.6f,
            TheEnvelopeCashGateDumpNext = false,
            TheEnvelopeClosetCashGateDumpNext = false,
            KeepTheLightsOffProductionGateDumpNext = false,
        };

        using var scope = new CompositionScope(lifetime: lifetime);
        scope.Root.Teardown();

        Assert.Equal(0.6f, lifetime.EmergencyPollSeconds, precision: 4);
        Assert.Equal(0.6f, lifetime.DemandPollSeconds, precision: 4);
        Assert.False(lifetime.TheEnvelopeCashGateDumpNext);
        Assert.False(lifetime.TheEnvelopeClosetCashGateDumpNext);
        Assert.False(lifetime.KeepTheLightsOffProductionGateDumpNext);
    }

    [Fact]
    public void Mod_shell_owns_one_lifetime_and_passes_it_to_each_root()
    {
        var mod = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Mod.cs");
        Assert.Contains("private readonly OcCompositionLifetime _lifetime = new();", mod, StringComparison.Ordinal);
        Assert.Contains("logging.Warning,", mod, StringComparison.Ordinal);
        Assert.Contains("_lifetime);", mod, StringComparison.Ordinal);

        var init = CompositionAssertions.Slice(mod, "public override void OnInitializeMelon()", "public override void OnDeinitializeMelon()");
        Assert.Contains("new OcCompositionRoot(", init, StringComparison.Ordinal);
        Assert.Contains("_lifetime", init, StringComparison.Ordinal);

        var teardown = CompositionAssertions.Slice(mod, "public override void OnDeinitializeMelon()", "public override void OnUpdate()");
        Assert.DoesNotContain("_lifetime =", teardown, StringComparison.Ordinal);
        Assert.DoesNotContain("_lifetime = null", teardown, StringComparison.Ordinal);
    }

    [Fact]
    public void Patch_failures_log_to_warning_sink_not_log_sink()
    {
        // The mod uses MelonLogger.Warning for all four fallible patch
        // applies; the composition must route them to Warn, not Log.
        using var scope = new CompositionScope(engine => engine.PatchApplyResult = false);

        var expected = new[]
        {
            "[Organized Crime] Arthur's native reaction suppression is incomplete; he may react like a native NPC.",
            "[Organized Crime] Native Syndicate HQ storage will remain inert because its exact vanilla load prefix was unavailable.",
            "[Organized Crime] Chief Campbell's protection gates are incomplete; some native policing stays ungated while covered.",
            "[Organized Crime] OC checkpoint staffing patch is incomplete; cloned officers cannot take checkpoint duty.",
        };

        foreach (var message in expected)
        {
            Assert.Contains(message, scope.Warnings);
            Assert.DoesNotContain(message, scope.Logs);
        }
    }

    [Fact]
    public void Warehouse_prepare_warning_uses_warning_sink()
    {
        // WarehouseModule.Build uses MelonLogger.Warning for the warehouse
        // PrepareForLoad failure; WarehouseModule must call warn, not log. Execution needs
        // a Starting/Unloading runtime (transient states), so the sink is pinned by source
        // plus the Build signature carrying warn.
        var module = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "WarehouseModule.cs");
        Assert.Contains("warn(\"[Organized Crime] Fish Warehouse runtime Property could not be fully prepared", module, StringComparison.Ordinal);
        Assert.DoesNotContain("log(\"[Organized Crime] Fish Warehouse runtime Property could not be fully prepared", module, StringComparison.Ordinal);

        var build = CompositionAssertions.Slice(module, "public static WarehouseOutputs Build(", "Action<string> serviceLog");
        Assert.Contains("Action<string> warn", build, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_receipts_log_to_log_sink_not_warning_sink()
    {
        using var scope = new CompositionScope(engine =>
        {
            engine.TimingThresholdResult = 25;
            engine.OwnerQaKeysResult = true;
        });

        var expectedLogs = new[]
        {
            "[Organized Crime] production runtime shell loaded.",
            "[Organized Crime] Timing receipts report any phase at or above 25 ms.",
            "[Organized Crime] Owner QA keys are enabled by preference; F8 recognition and fish warehouse developer keys are live.",
            "[Organized Crime] Chapter 3 has no fallback presenter; Nell's messages stay pending while the presentation mode is ImguiFallback.",
        };

        foreach (var message in expectedLogs)
        {
            Assert.Contains(message, scope.Logs);
            Assert.DoesNotContain(message, scope.Warnings);
        }

        Assert.Empty(scope.Warnings);
    }

    [Fact]
    public void Setup_modules_and_root_carry_no_try_except_owner_qa_registration_and_deferred_gates()
    {
        // Setup failures propagate immediately; only the optional owner QA
        // registration and its warning sink may be caught.
        var root = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "OcCompositionRoot.cs");
        var build = CompositionAssertions.Slice(root, "public void Build()", "public void Teardown()");
        var ownerQa = CompositionAssertions.Slice(build, "if (OwnerQaKeysEnabled)", "var hqContext =");
        Assert.Contains("engine.RegisterOwnerQaConsoleCommand();", ownerQa, StringComparison.Ordinal);
        Assert.Equal(2, CompositionAssertions.CountTryLines(ownerQa));
        Assert.Equal(0, CompositionAssertions.CountTryLines(build.Replace(ownerQa, string.Empty, StringComparison.Ordinal)));

        foreach (var file in new[] { "Chapter3Module.cs", "HqModule.cs", "WarehouseModule.cs", "LawModule.cs" })
        {
            var source = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", file);
            Assert.Equal(0, CompositionAssertions.CountTryLines(source));
        }

        var release1 = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "Release1Module.cs");
        Assert.Equal(3, CompositionAssertions.CountTryLines(release1));
        var gates = release1.Substring(release1.IndexOf("public static void BuildProtectionGates", StringComparison.Ordinal));
        Assert.Equal(3, CompositionAssertions.CountTryLines(gates));
    }

    [Fact]
    public void Teardown_disposes_before_nulling_in_declaration_order_without_try()
    {
        // Pin each Dispose-then-null pair in declaration order over the real
        // Teardown path (not manual trace labels); disposal must precede nulling, with no
        // catches. Execution below proves both ran (before non-null, after null, no throw).
        var root = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "OcCompositionRoot.cs");
        var teardown = CompositionAssertions.Slice(root, "public void Teardown()", "public void ApplyLoadedWarehouseState");
        Assert.Equal(0, CompositionAssertions.CountTryLines(teardown));

        AssertOrdered(teardown,
            "engine.ResetLockdownGate();",
            "engine.ResetProtectionGate();",
            "HqStorage?.MarkTeardown();",
            "Hq.TryDispose()",
            "Hq = null;",
            "engine.ResetHqStorageLoad();",
            "HqStorage?.Dispose();",
            "HqStorage = null;",
            "Production?.Dispose();",
            "Production = null;",
            "Chief?.Dispose();",
            "Chief = null;",
            "CourtesyWorld = null;",
            "Story?.Dispose();",
            "Story = null;",
            "Chapter3Projector?.Dispose();",
            "Chapter3Projector = null;",
            "Checkpoints?.Dispose();",
            "Checkpoints = null;",
            "OfficerRoster?.Dispose();",
            "OfficerRoster = null;",
            "engine.DisposeArthurRuntime(Arthur)",
            "Arthur = null;",
            "Chapter3QaAccess.Service = null;",
            "Chapter3?.Dispose();",
            "Chapter3 = null;",
            "OvernightLine?.Dispose();",
            "OvernightLine = null;",
            "engine.DisposeDayClock(DayClock)",
            "DayClock = null;",
            "HqContext = null;",
            "engine.ResetCustody();",
            "CustodyBridge?.Dispose();",
            "CustodyBridge = null;",
            "Law?.Dispose();",
            "Law = null;",
            "LawController = null;",
            "LocalPressure = null;",
            "engine.ClearWarehouseManagementTarget();",
            "engine.ResetWarehouseDeliveryRestore();",
            "engine.ResetWarehouseLoadCapture();",
            "engine.ResetWarehouseWriteGuard();",
            "engine.UnsubscribeSaveComplete",
            "engine.UnsubscribeSaveStart",
            "engine.UnsubscribePreLoad",
            "engine.UnsubscribeLoadComplete",
            "engine.UnsubscribePreSceneChange",
            "WarehouseReplay = null;",
            "WarehousePersistence = null;",
            "WarehouseCapture?.Reset();",
            "WarehouseCapture = null;",
            "WarehouseRuntime = null;",
            "PropertyHost = null;");

        using var scope = new CompositionScope();
        Assert.NotNull(scope.Root.Production);
        Assert.NotNull(scope.Root.Chief);
        Assert.NotNull(scope.Root.Story);
        Assert.NotNull(scope.Root.WarehouseCapture);
        var capture = scope.Root.WarehouseCapture!;
        var overnight = scope.Engine.OvernightLine;

        scope.Root.Teardown();

        Assert.Null(scope.Root.Production);
        Assert.Null(scope.Root.Chief);
        Assert.Null(scope.Root.Story);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Equal(OrganizedCrime.Runtime.FishWarehousePropertyCaptureState.Inactive, capture.State);
        Assert.Equal(1, overnight.DisposeCalls);
        Assert.Contains("dispose:Arthur", scope.Engine.DisposeCalls);
        Assert.Contains("dispose:DayClock", scope.Engine.DisposeCalls);
    }

    [Fact]
    public void Qa_cash_gate_toggle_stays_armed_across_teardown_and_rebuild()
    {
        // A QA key awaiting its second action stays armed across reinit.
        // F1 starts as dump, flips to decrement after one press, and must still be
        // decrement after teardown/rebuild with the same lifetime.
        var lifetime = new OcCompositionLifetime();

        using (var first = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.PressedKeys.Add(KeyCode.F1);
        }, lifetime: lifetime))
        {
            first.Root.QaKeys.Poll();
            Assert.Contains(first.Logs, line => line.Contains("cash gate proof (dump)", StringComparison.Ordinal));
            Assert.False(lifetime.TheEnvelopeCashGateDumpNext);
            first.Root.Teardown();
            Assert.False(lifetime.TheEnvelopeCashGateDumpNext);
        }

        using (var second = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.PressedKeys.Add(KeyCode.F1);
        }, lifetime: lifetime))
        {
            second.Root.QaKeys.Poll();
            Assert.Contains(second.Logs, line => line.Contains("cash gate proof (decrement)", StringComparison.Ordinal));
            Assert.True(lifetime.TheEnvelopeCashGateDumpNext);
        }
    }

    [Fact]
    public void Qa_closet_and_production_toggles_stay_armed_across_teardown_and_rebuild()
    {
        var lifetime = new OcCompositionLifetime();

        using (var first = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.PressedKeys.Add(KeyCode.F2);
            engine.PressedKeys.Add(KeyCode.Home);
        }, lifetime: lifetime))
        {
            first.Root.QaKeys.Poll();
            Assert.False(lifetime.TheEnvelopeClosetCashGateDumpNext);
            Assert.False(lifetime.KeepTheLightsOffProductionGateDumpNext);
            first.Root.Teardown();
        }

        using (var second = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.PressedKeys.Add(KeyCode.F2);
            engine.PressedKeys.Add(KeyCode.Home);
        }, lifetime: lifetime))
        {
            second.Root.QaKeys.Poll();
            Assert.Contains(second.Logs, line => line.Contains("closet cash gate proof (decrement)", StringComparison.Ordinal));
            Assert.Contains(second.Logs, line => line.Contains("production gate proof (cross check)", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Protection_today_lambda_computes_days_and_fails_open()
    {
        using var scope = new CompositionScope();
        var today = scope.Engine.ProtectionToday!;
        var world = scope.Engine.CourtesyWorld;

        world.MinutesStatus = () => Release1SmallCourtesyWorldReadStatus.Ready;
        world.MinutesValue = 2880d;
        Assert.Equal(2, today());
        world.MinutesValue = 0d;
        Assert.Equal(0, today());

        world.MinutesValue = -100d;
        Assert.Null(today());
        world.MinutesStatus = () => Release1SmallCourtesyWorldReadStatus.Unavailable;
        Assert.Null(today());
        world.MinutesStatus = () => throw new InvalidOperationException("boom");
        Assert.Null(today());
    }

    [Fact]
    public void Protection_covered_and_host_lambdas_read_through()
    {
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Engine.ProtectionIsCovered);
        Assert.NotNull(scope.Engine.ProtectionIsHost);
        Assert.False(scope.Engine.ProtectionIsCovered!());

        scope.Engine.IsHostResult = true;
        Assert.True(scope.Engine.ProtectionIsHost!());
        scope.Engine.IsHostResult = false;
        Assert.False(scope.Engine.ProtectionIsHost!());
    }

    [Fact]
    public void Hq_storage_load_steps_run_at_the_table_positions()
    {
        using var scope = new CompositionScope();
        var tables = LifecycleTables.ForRoot(scope.Root);

        tables.PreLoad[0].Run();
        Assert.Equal(new[] { "hq:BeginLoadCycle" }, scope.Engine.HqLoadCalls);

        tables.LoadCompleteMain.Single(step => step.Name == "load-complete/hq-storage").Run();
        Assert.Equal(new[] { "hq:BeginLoadCycle", "hq:CompleteHqLoad" }, scope.Engine.HqLoadCalls);
    }

    [Fact]
    public void Update_slots_drive_roster_checkpoints_warehouse_ensure_ui_and_qa()
    {
        using var scope = new CompositionScope(engine => engine.FishWarehouseReadyResult = true);
        scope.Engine.IsHostResult = true;
        var slots = LifecycleTables.ForRoot(scope.Root).Update;

        CompositionAssertions.AssertRosterAndCheckpointsSlotsDriveTheirServices(scope);

        slots.WarehouseUpdate!();
        Assert.Equal(RuntimePropertyLifecycleState.Ready, scope.Root.WarehouseRuntime!.State);

        slots.EnsureUi!();
        Assert.Equal(new[] { "EnsureUi" }, scope.Engine.PropertyHost.Calls);

        var typingReads = scope.Engine.IsPlayerTypingCalls;
        slots.QaPoll!();
        Assert.Equal(typingReads + 1, scope.Engine.IsPlayerTypingCalls);
    }

    [Fact]
    public void ForRoot_update_slots_call_their_documented_services()
    {
        CompositionAssertions.AssertForRootSlotNeedles();
    }

    [Fact]
    public void ForRoot_headless_safe_slots_invoke_without_throw_and_nulls_are_safe()
    {
        // Bind each callback to its slot by execution, not only source.
        // Headless-safe slots run with services present; Unity-bound slots (Hq, Production,
        // Chief, GUIs) prove null-safety only, since their Update/OnGUI needs Unity Input.
        using var scope = new CompositionScope(engine => engine.FishWarehouseReadyResult = true);
        scope.Engine.IsHostResult = true;
        var slots = LifecycleTables.ForRoot(scope.Root).Update;

        // ArthurPoll drives the visit pump into the fake world, which throws by design for
        // the unscripted impact read; the throw proves the slot is bound to its service.
        scope.Engine.ArthurWorld.HasAuthority = true;
        Assert.IsType<System.NotSupportedException>(Record.Exception(() => slots.ArthurPoll!()));
        scope.Engine.ArthurWorld.HasAuthority = false;
        Assert.Null(Record.Exception(() => slots.ArthurPoll!()));
        Assert.Null(Record.Exception(() => slots.EmergencyPoll!()));
        Assert.Null(Record.Exception(() => slots.DemandPoll!()));
        Assert.Null(Record.Exception(() => slots.WarehouseBuildPoll!()));
        Assert.Null(Record.Exception(() => slots.ReplayUpdate!()));
        Assert.Null(Record.Exception(() => slots.ReadinessPump!()));
        Assert.Null(Record.Exception(() => slots.ProjectorReconcile!()));
        Assert.Null(Record.Exception(() => slots.RosterUpdate!()));
        Assert.Null(Record.Exception(() => slots.CheckpointsUpdate!()));
        Assert.Null(Record.Exception(() => slots.WarehouseUpdate!()));
        Assert.Null(Record.Exception(() => slots.EnsureUi!()));
        Assert.Null(Record.Exception(() => slots.QaPoll!()));

        scope.Root.Chapter3 = null;
        scope.Root.OfficerRoster = null;
        scope.Root.Checkpoints = null;
        scope.Root.WarehouseRuntime = null;
        scope.Root.WarehouseReplay = null;
        scope.Root.LocalPressure = null;
        scope.Root.PropertyHost = null;
        scope.Root.Hq = null;
        scope.Root.Production = null;
        scope.Root.Chief = null;
        scope.Root.Chapter3Projector = null;

        var nulled = LifecycleTables.ForRoot(scope.Root).Update;
        Assert.Null(Record.Exception(() => nulled.HqUpdate!()));
        Assert.Null(Record.Exception(() => nulled.ProductionUpdate!()));
        Assert.Null(Record.Exception(() => nulled.ProjectorReconcile!()));
        Assert.Null(Record.Exception(() => nulled.ArthurPoll!()));
        Assert.Null(Record.Exception(() => nulled.EmergencyPoll!()));
        Assert.Null(Record.Exception(() => nulled.ChiefUpdate!()));
        Assert.Null(Record.Exception(() => nulled.ReadinessPump!()));
        Assert.Null(Record.Exception(() => nulled.WarehouseUpdate!()));
        Assert.Null(Record.Exception(() => nulled.ReplayUpdate!()));
        Assert.Null(Record.Exception(() => nulled.WarehouseBuildPoll!()));
        Assert.Null(Record.Exception(() => nulled.DemandPoll!()));
        Assert.Null(Record.Exception(() => nulled.RosterUpdate!()));
        Assert.Null(Record.Exception(() => nulled.CheckpointsUpdate!()));
        Assert.Null(Record.Exception(() => nulled.EnsureUi!()));
        Assert.Null(Record.Exception(() => nulled.QaPoll!()));
        Assert.Null(Record.Exception(() => nulled.HqGui!()));
        Assert.Null(Record.Exception(() => nulled.ProductionGui!()));
    }

    [Fact]
    public void Load_complete_tail_proves_staging_when_enabled()
    {
        using var scope = new CompositionScope(engine => engine.OwnerQaKeysResult = true);
        var world = new ScriptableCourtesyWorld
        {
            DropsStatus = Release1SmallCourtesyWorldReadStatus.Ready,
            Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-1", "Drop 1", "staged", 0d, 0d, 0d, false) },
            SlotsStatus = Release1SmallCourtesyWorldReadStatus.Ready,
            Slots = new[] { new Release1SmallCourtesySlotSnapshot(0, "product-a", null, 2, false, 1f) },
        };
        scope.Root.CourtesyWorld = world;
        var before = scope.Logs.Count;

        LifecycleTables.ForRoot(scope.Root).LoadCompleteTail!();

        Assert.Contains("[Organized Crime] OC-52 staging proof at load: dead drops read: 1", scope.Logs.Skip(before));
        Assert.Contains(scope.Logs.Skip(before), line => line.Contains("quantity 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_complete_tail_stays_silent_when_disabled_or_worldless()
    {
        using var enabled = new CompositionScope(engine => engine.OwnerQaKeysResult = true);
        enabled.Root.CourtesyWorld = null;
        var enabledBefore = enabled.Logs.Count;
        LifecycleTables.ForRoot(enabled.Root).LoadCompleteTail!();
        Assert.Equal(enabledBefore, enabled.Logs.Count);

        using var disabled = new CompositionScope();
        var disabledBefore = disabled.Logs.Count;
        LifecycleTables.ForRoot(disabled.Root).LoadCompleteTail!();
        Assert.Equal(disabledBefore, disabled.Logs.Count);
    }

    [Fact]
    public void Enabled_poll_dispatches_pressed_keys()
    {
        using var scope = new CompositionScope(engine =>
        {
            engine.OwnerQaKeysResult = true;
            engine.PressedKeys.Add(KeyCode.Minus);
            engine.PressedKeys.Add(KeyCode.End);
        });

        scope.Root.QaKeys.Poll();

        Assert.Contains("[Organized Crime] Chapter 3 QA start key pressed.", scope.Logs);
        Assert.Equal(new[] { "engage" }, scope.Engine.CourtesyWorld.LockdownCalls);
    }

    [Fact]
    public void Repository_factories_keep_current_throw_and_null_shapes()
    {
        Assert.Throws<InvalidOperationException>(() => OcCompositionRoot.CreateRelease1StoryRepository(null!));
        Assert.Throws<InvalidOperationException>(() => OcCompositionRoot.CreateChapter3StateRepository(null!));
        Assert.Null(OcCompositionRoot.CreateLocalPressureRepository(null));
    }

    [Fact]
    public void Global_log_sinks_capture_only_the_sink()
    {
        using var scope = new CompositionScope();
        var root = scope.Root;
        var engine = scope.Engine;
        var lifetime = root.Lifetime;

        scope.Root.Teardown();

        AssertSinkRetainsOnlySink(OrganizedCrimeLog.Warning, root, engine, lifetime);
        AssertSinkRetainsOnlySink(OrganizedCrimeLog.Receipt, root, engine, lifetime);
    }

    private static void AssertSinkRetainsOnlySink(Action<string> sink, params object[] forbidden)
    {
        var target = sink.Target;
        if (target is null)
            return;
        var seen = new HashSet<object>();
        var queue = new Queue<object>();
        queue.Enqueue(target);
        seen.Add(target);
        for (var depth = 0; depth < 4 && queue.Count > 0; depth++)
        {
            var count = queue.Count;
            for (var i = 0; i < count; i++)
            {
                var current = queue.Dequeue();
                foreach (var item in forbidden)
                    Assert.NotSame(item, current);
                var type = current.GetType();
                Assert.False(
                    type == typeof(OcCompositionRoot)
                        || type == typeof(OcCompositionLifetime)
                        || type == typeof(FakeOcEngineAdapters)
                        || type == typeof(SyndicateHqDoorObserver)
                        || type == typeof(SyndicateHqRuntimeService)
                        || type == typeof(SyndicateHqRuntimeComposition)
                        || type == typeof(SyndicateHqNativeStorageRuntime),
                    "Log sink retains " + type.FullName + ".");
                if (current is Delegate nested && nested.Target is not null && seen.Add(nested.Target))
                    queue.Enqueue(nested.Target);
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType.IsValueType || field.FieldType == typeof(string))
                        continue;
                    object? value;
                    try
                    {
                        value = field.GetValue(current);
                    }
                    catch
                    {
                        continue;
                    }
                    if (value is null || !seen.Add(value))
                        continue;
                    queue.Enqueue(value);
                }
            }
        }
    }

    private static void AssertOrdered(string text, params string[] needles)
    {
        var lastIndex = -1;
        foreach (var needle in needles)
        {
            var index = text.IndexOf(needle, lastIndex + 1, StringComparison.Ordinal);
            Assert.True(index > lastIndex, $"needle out of order or missing: {needle}");
            lastIndex = index;
        }
    }
}
