using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-151: the Mod.cs composition contract, migrated from source text to composition
// assertions. Setup, patch, pref, throw, key-map and teardown guarantees assert the
// exact trace through the recording fake; lifecycle guarantees assert the production
// tables plus the fanout shapes that cannot run headless. Execution mechanics (step
// order, host gating, timers) live in LifecycleFanoutTests; graph identity lives in
// OcCompositionRootTests; each guarantee is pinned once.
[Collection("OC-151 composition scope")]
public sealed class ModCompositionCharacterizationTests
{
    [Fact]
    public void Setup_runs_every_step_in_order()
    {
        using var scope = new CompositionScope();

        CompositionAssertions.AssertSetupTrace(scope.Engine);
    }

    [Fact]
    public void Setup_reads_preferences_in_current_order_and_counts()
    {
        using var imgui = new CompositionScope();

        CompositionAssertions.AssertImguiPrefReads(imgui.Engine);
        Assert.Equal("read:PresentationMode", imgui.Engine.Trace[14]);

        using var native = new CompositionScope(engine => engine.PresentationModeResult = OrganizedCrime.Runtime.Release1PresentationMode.Native);

        CompositionAssertions.AssertNativeSetupTrace(native.Engine);
        CompositionAssertions.AssertNativePrefReads(native.Engine);
    }

    [Fact]
    public void Setup_applies_patches_in_order_and_never_the_lockdown_gate()
    {
        using var scope = new CompositionScope();

        CompositionAssertions.AssertPatchCalls(scope.Engine);
        Assert.DoesNotContain(scope.Engine.Trace, entry => entry.Contains("Lockdown", StringComparison.Ordinal));
    }

    [Fact]
    public void Setup_throw_aborts_later_steps_and_keeps_initialized()
    {
        CompositionAssertions.AssertThrowPartialKeepsPartialState();

        // The shell publishes the root before Build with no try: a throw keeps both
        // the partial root and _initialized, so a retry is ignored like before.
        var init = CompositionAssertions.Slice(ReadModCs(), "public override void OnInitializeMelon()", "public override void OnDeinitializeMelon()");
        AssertOrdered(init,
            "duplicate initialization was ignored.",
            "_initialized = true;",
            "new OcCompositionRoot(",
            "_root = root;",
            "root.Build();");
        Assert.Equal(0, CountTryLines(init));
    }

    [Fact]
    public void SaveComplete_carries_current_table_with_an_always_measured_outer()
    {
        // Gating mechanics (host-only children, throw aborts) live in LifecycleFanoutTests.
        CompositionAssertions.AssertSaveCompleteTable(Tables());

        var fanout = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = CompositionAssertions.Slice(fanout, "SaveComplete = new[]", "SaveStart = new[]");
        Assert.Contains("Release1StoryLifecycleLogging.LogIfRejected(\"OnSaveComplete\"", tables, StringComparison.Ordinal);
        Assert.Contains("Chapter3LifecycleLogging.LogIfRejected(\"OnSaveComplete\"", tables, StringComparison.Ordinal);
        Assert.Equal(2, Count(tables, "LogIfRejected(\"OnSaveComplete\""));
        Assert.Contains("root.WarehousePersistence?.HandleSaveComplete()", tables, StringComparison.Ordinal);

        var handler = CompositionAssertions.Slice(fanout, "public void HandleSaveComplete()", "public void HandleSaveStart()");
        Assert.Contains("timing.Measure(\"save-complete\", () => FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(", handler, StringComparison.Ordinal);
        Assert.Equal(1, Count(handler, "RunIfAuthoritativeHost("));
        Assert.Equal(1, Count(handler, "IsHost()"));
        Assert.Equal(0, CountTryLines(handler));
    }

    [Fact]
    public void SaveStart_carries_current_eight_participant_table_without_warehouse()
    {
        CompositionAssertions.AssertSaveStartTable(Tables());

        var fanout = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var handler = CompositionAssertions.Slice(fanout, "public void HandleSaveStart()", "public void HandlePreLoad()");
        Assert.DoesNotContain("RunIfAuthoritativeHost", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHost", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("fish-warehouse", handler, StringComparison.Ordinal);
        Assert.Equal(0, CountTryLines(handler));
    }

    [Fact]
    public void PreLoad_carries_current_table_with_the_storage_cycle_untimed_first()
    {
        CompositionAssertions.AssertPreLoadTable(Tables());

        var fanout = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = CompositionAssertions.Slice(fanout, "PreLoad = new[]", "LoadCompletePrepare =");
        var cycle = tables.IndexOf("engine.BeginHqLoadCycle, LifecycleStepKind.Untimed", StringComparison.Ordinal);
        var firstTimed = tables.IndexOf("\"pre-load/hq\"", StringComparison.Ordinal);
        Assert.True(cycle >= 0 && firstTimed > cycle, "BeginLoadCycle must run untimed before the first timed child.");
        Assert.Contains("root.WarehouseReplay?.PrepareForLoad()", tables, StringComparison.Ordinal);

        var handler = CompositionAssertions.Slice(fanout, "public void HandlePreLoad()", "public void HandleLoadComplete()");
        Assert.DoesNotContain("RunIfAuthoritativeHost", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHost", handler, StringComparison.Ordinal);
        Assert.Equal(0, CountTryLines(handler));
    }

    [Fact]
    public void LoadComplete_tables_prepare_hq_first_with_a_gated_warehouse_and_staging_tail()
    {
        // Gate mechanics (prepare/hq/host/tail-after-block) live in LifecycleFanoutTests;
        // the staging tail execution lives in OcCompositionRootTests.
        CompositionAssertions.AssertLoadCompleteTable(Tables());

        var fanout = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var handler = CompositionAssertions.Slice(fanout, "public void HandleLoadComplete()", "public void HandlePreSceneChange()");
        Assert.Contains("timing.Measure(prepare.Name, () => { hqLoadPrepared = prepare.Run(); });", handler, StringComparison.Ordinal);
        Assert.Contains("if (hqLoadPrepared && hq is not null)", handler, StringComparison.Ordinal);
        Assert.Equal(1, Count(handler, "RunIfAuthoritativeHost("));

        var tables = CompositionAssertions.Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");
        Assert.Contains("engine.CompleteHqLoad", tables, StringComparison.Ordinal);
        var tailGate = tables.IndexOf("if (root.OwnerQaKeysEnabled && world is not null)", StringComparison.Ordinal);
        var lastTimed = tables.IndexOf("\"load-complete/fish-warehouse\"", StringComparison.Ordinal);
        Assert.True(tailGate > lastTimed, "the staging tail must follow the timed block.");
        Assert.Contains("OC-52 staging proof at load", tables, StringComparison.Ordinal);
        Assert.Equal(1, CountTryLines(tables));
        Assert.Equal(0, CountTryLines(handler));
    }

    [Fact]
    public void PreSceneChange_carries_current_three_participant_table()
    {
        CompositionAssertions.AssertPreSceneChangeTable(Tables());

        var fanout = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var handler = CompositionAssertions.Slice(fanout, "public void HandlePreSceneChange()", "public void Update()");
        Assert.DoesNotContain("RunIfAuthoritativeHost", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHost", handler, StringComparison.Ordinal);
        Assert.Equal(0, CountTryLines(handler));
    }

    [Fact]
    public void Update_slots_are_wired_and_call_their_documented_services()
    {
        // Slot order, host section, timers and qa-last mechanics live in LifecycleFanoutTests.
        CompositionAssertions.AssertUpdateSlotsWired(LifecycleTables.ForRoot(FreshRoot()).Update);
        CompositionAssertions.AssertForRootSlotNeedles();
    }

    [Fact]
    public void Hotkey_map_lists_every_key_in_current_order()
    {
        CompositionAssertions.AssertKeyMap(FreshRoot().QaKeys.Bindings);

        // OC-151 Blocker 3: the toggles live in the mod-instance lifetime so they survive
        // deinit/reinit; OwnerQaKeys reads/writes through the lifetime.
        var lifetime = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "OcCompositionLifetime.cs");
        Assert.Contains("public bool TheEnvelopeCashGateDumpNext { get; set; } = true;", lifetime, StringComparison.Ordinal);
        Assert.Contains("public bool TheEnvelopeClosetCashGateDumpNext { get; set; } = true;", lifetime, StringComparison.Ordinal);
        Assert.Contains("public bool KeepTheLightsOffProductionGateDumpNext { get; set; } = true;", lifetime, StringComparison.Ordinal);

        var keys = CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Qa", "OwnerQaKeys.cs");
        Assert.Contains("_root.Lifetime.TheEnvelopeCashGateDumpNext", keys, StringComparison.Ordinal);
        Assert.Contains("_root.Lifetime.TheEnvelopeClosetCashGateDumpNext", keys, StringComparison.Ordinal);
        Assert.Contains("_root.Lifetime.KeepTheLightsOffProductionGateDumpNext", keys, StringComparison.Ordinal);
        Assert.Contains("Chapter 3 QA start key pressed.", keys, StringComparison.Ordinal);
        Assert.Contains("the Chapter 3 service is not constructed.", keys, StringComparison.Ordinal);
    }

    [Fact]
    public void Teardown_runs_every_step_in_order_and_clears_initialized_last()
    {
        using var scope = new CompositionScope();
        scope.Root.Teardown();

        CompositionAssertions.AssertTeardownTrace(scope.Engine, CompositionAssertions.ExpectedImguiSetupTrace);

        var teardown = CompositionAssertions.Slice(ReadModCs(), "public override void OnDeinitializeMelon()", "public override void OnUpdate()");
        AssertOrdered(teardown,
            "_root?.Teardown();",
            "_root = null;",
            "_initialized = false;");
        Assert.Equal(0, CountTryLines(teardown));
    }

    private static LifecycleTables Tables() => LifecycleTables.ForRoot(FreshRoot());

    private static OcCompositionRoot FreshRoot() =>
        new(new FakeOcEngineAdapters(), _ => { }, _ => { });

    private static string ReadModCs() =>
        CompositionAssertions.ReadProductionSource("tools", "OrganizedCrime", "Mod.cs");

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

    private static int Count(string text, string needle)
    {
        var total = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            total++;
            index += needle.Length;
        }
        return total;
    }

    private static int CountTryLines(string text)
    {
        var total = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == "try" || trimmed.StartsWith("try ", StringComparison.Ordinal) || trimmed.StartsWith("try{", StringComparison.Ordinal))
                total++;
        }
        return total;
    }
}
