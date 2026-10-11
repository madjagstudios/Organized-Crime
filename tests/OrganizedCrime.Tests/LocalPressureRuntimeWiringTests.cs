using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class LocalPressureRuntimeWiringTests
{
    [Fact]
    public void Fanout_tick_pumps_local_pressure_readiness()
    {
        var fanout = ReadCompositionSource("LifecycleFanout.cs");

        Assert.Contains("ReadinessPump = () => root.LocalPressure?.Service.PumpReadiness(),", fanout, StringComparison.Ordinal);
        Assert.Contains("slots.ReadinessPump?.Invoke();", BodyOf(fanout, "public void Update()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Law_module_builds_the_native_composition_before_the_custody_bridge_and_patch()
    {
        var law = ReadCompositionSource("LawModule.cs");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");

        Assert.Equal(1, Count(law, "new NativeLawResponseModComposition("));
        Assert.Equal(1, Count(law, "new PoliceCustodyEvidenceBridge("));
        Assert.Equal(1, Count(melon, "PoliceCustodyPatch.Apply("));
        AssertOrdered(
            law,
            "new NativeLawResponseModComposition(",
            "root.Law = composition;",
            "root.LawController = composition.Controller;",
            "root.LocalPressure = composition.LocalPressureRuntime;",
            "return composition;",
            "new PoliceCustodyEvidenceBridge(",
            "engine.ApplyCustody(bridge, serviceWarn);");

        using var scope = new CompositionScope();
        var trace = scope.Engine.Trace;
        Assert.True(trace.IndexOf("step:Law.Composition") < trace.IndexOf("step:Law.Bridge"));
        Assert.True(trace.IndexOf("step:Law.Bridge") < trace.IndexOf("apply:Custody"));
    }

    [Fact]
    public void Teardown_disposes_the_custody_bridge_and_law_before_the_warehouse_resets()
    {
        var teardown = BodyOf(ReadCompositionSource("OcCompositionRoot.cs"), "public void Teardown()");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");

        Assert.Equal(1, Count(teardown, "engine.ResetCustody();"));
        Assert.Equal(1, Count(teardown, "CustodyBridge?.Dispose();"));
        Assert.Equal(1, Count(teardown, "Law?.Dispose();"));
        Assert.Equal(1, Count(melon, "PoliceCustodyPatch.Reset()"));
        AssertOrdered(
            teardown,
            "engine.ResetCustody();",
            "CustodyBridge?.Dispose();",
            "CustodyBridge = null;",
            "Law?.Dispose();",
            "Law = null;",
            "LawController = null;",
            "LocalPressure = null;",
            "engine.ClearWarehouseManagementTarget();");

        using var scope = new CompositionScope();
        scope.Root.Teardown();

        Assert.Null(scope.Root.CustodyBridge);
        Assert.Null(scope.Root.Law);
        Assert.Null(scope.Root.LawController);
        Assert.Null(scope.Root.LocalPressure);
        var trace = scope.Engine.Trace;
        Assert.True(trace.IndexOf("reset:Custody") < trace.IndexOf("teardown:Custody.Dispose"));
        Assert.True(trace.IndexOf("teardown:Custody.Dispose") < trace.IndexOf("teardown:Law.Dispose"));
        Assert.True(trace.IndexOf("teardown:Law.Dispose") < trace.IndexOf("reset:WarehouseManagementTarget"));
    }

    [Fact]
    public void Fanout_keeps_oc41_out_of_update_and_wires_current_patch_and_lifecycle_hooks()
    {
        var fanout = ReadCompositionSource("LifecycleFanout.cs");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");
        var onUpdateBody = BodyOf(fanout, "public void Update()");

        Assert.DoesNotContain("NativeLawResponse", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("NativePoliceDispatchAdapter", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeLawResponseController", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeLawResponseComposition", onUpdateBody, StringComparison.Ordinal);

        Assert.Equal(1, Count(melon, "FishWarehouseManagementCanvasPatch.Apply(_harmony);"));
        Assert.Equal(1, Count(melon, "FishWarehouseNativeGridPlacementPatches.Apply(_harmony);"));
        Assert.Equal(1, Count(melon, "FishWarehouseDockOccupantRefreshPatch.Apply(_harmony);"));
        Assert.Equal(1, Count(melon, "FishWarehousePropertyLoadCapturePatch.Apply("));
        Assert.Equal(1, Count(melon, "FishWarehouseNativePropertyWriteGuardPatch.Apply("));
        Assert.Equal(1, Count(melon, "PoliceCustodyPatch.Apply("));

        using var scope = new CompositionScope();

        CompositionAssertions.AssertPatchCalls(scope.Engine);
        CompositionAssertions.AssertSubscribeCalls(scope.Engine);
        scope.Root.Teardown();
        CompositionAssertions.AssertUnsubscribeCalls(scope.Engine);
    }

    [Fact]
    public void Fanout_defers_hq_load_epoch_until_recovery_and_keeps_other_lifecycle_notifications()
    {
        var fanout = ReadCompositionSource("LifecycleFanout.cs");

        Assert.Contains("new LifecycleStep(\"pre-load/hq\", () => root.Hq?.OnPreLoad()),", fanout, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginLoad", fanout, StringComparison.Ordinal);
        Assert.Contains("root.Story?.OnPreLoad()", fanout, StringComparison.Ordinal);
        Assert.Contains("root.WarehouseReplay?.PrepareForLoad()", fanout, StringComparison.Ordinal);
        AssertOrdered(fanout, "\"pre-load/hq\"", "\"pre-load/story\"");

        // OC-63 wraps each load-complete child in the timing seam, so the assignment now lands
        // inside a measured lambda rather than reading straight into a var declaration. The wiring
        // the guard cares about is unchanged: the HQ prepares first, and its result still gates
        // the later OnLoadComplete. OC-10 wraps the story call in
        // Release1StoryLifecycleLogging.LogIfRejected so a rejected lifecycle result gets logged,
        // so the call site is now a substring without its own trailing semicolon.
        Assert.Contains("LoadCompletePrepare = new LifecycleGateStep(\"load-complete/hq-prepare\", () => root.Hq?.PrepareLoadComplete() ?? true),", fanout, StringComparison.Ordinal);
        Assert.Contains("hqLoadPrepared = prepare.Run();", fanout, StringComparison.Ordinal);
        AssertOrdered(fanout, "PrepareLoadComplete()", "\"load-complete/story\"", "\"load-complete/hq\"");

        using var scope = new CompositionScope();
        var tables = LifecycleTables.ForRoot(scope.Root);

        CompositionAssertions.AssertPreLoadTable(tables);
        CompositionAssertions.AssertLoadCompleteTable(tables);
    }

    [Fact]
    public void Teardown_finishes_when_hq_listener_cleanup_cannot_succeed_at_quit()
    {
        var teardown = BodyOf(ReadCompositionSource("OcCompositionRoot.cs"), "public void Teardown()");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");

        Assert.Contains("if (Hq is not null && !Hq.TryDispose())", teardown, StringComparison.Ordinal);
        Assert.Contains(
            "Warn(\"[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.\");",
            teardown,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(melon, "SyndicateHqStorageLoadPatch.Reset()"));
        AssertOrdered(
            teardown,
            "HqStorage?.MarkTeardown();",
            "Hq.TryDispose()",
            "Warn(\"[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.\");",
            "engine.ResetHqStorageLoad();",
            "HqStorage?.Dispose();",
            "Story?.Dispose();");
    }

    [Fact]
    public void Native_law_response_runtime_files_do_not_use_forbidden_invoke_syntax()
    {
        var runtimeDirectory = Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime");

        foreach (var path in Directory.GetFiles(runtimeDirectory, "Native*.cs"))
        {
            Assert.DoesNotContain(".Invoke(", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Composition_preserves_exact_fish_warehouse_setup_and_teardown_order()
    {
        var warehouse = ReadCompositionSource("WarehouseModule.cs");
        var chapter3 = ReadCompositionSource("Chapter3Module.cs");
        var teardown = BodyOf(ReadCompositionSource("OcCompositionRoot.cs"), "public void Teardown()");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");

        Assert.Contains("new Chapter3WarehouseOwnership(() => root.WarehouseRuntime?.OwnershipUnlocked ?? false)", chapter3, StringComparison.Ordinal);
        AssertOrdered(
            warehouse,
            "engine.ApplyWarehouseCanvas();",
            "engine.ApplyWarehouseGrid();",
            "engine.ApplyWarehouseDock();",
            "engine.CreatePropertyHost()",
            "engine.ApplyWarehouseDeliveryRestore(",
            "new FishWarehouseRuntimeService(",
            "new Chapter3WarehouseAcquisition(",
            "new FishWarehousePropertySnapshotStore();",
            "new FishWarehouseReplayLifecycle(",
            "new FishWarehousePersistenceService(",
            "new FishWarehousePropertyCaptureService(",
            "StaffReturnBannerSender = new FishWarehouseStaffReturnBannerSender(",
            "EmployeeCapacitySource = new FishWarehouseEmployeeCapacitySource(",
            "WarehouseCapacity = new Chapter3WarehouseCapacityApplier(",
            "new FishWarehousePersistenceReplayOperations(",
            "new FishWarehousePersistenceReplayService(",
            "engine.ApplyWarehouseLoadCapture(",
            "engine.ApplyWarehouseWriteGuard(");
        AssertOrdered(
            teardown,
            "engine.ClearWarehouseManagementTarget();",
            "engine.ResetWarehouseDeliveryRestore();",
            "engine.ResetWarehouseLoadCapture();",
            "engine.ResetWarehouseWriteGuard();",
            "WarehouseReplay = null;",
            "WarehousePersistence = null;",
            "WarehouseCapture?.Reset();",
            "WarehouseCapture = null;",
            "WarehouseRuntime = null;");
        Assert.Equal(1, Count(melon, "FishWarehouseDeliveryRestorePatch.Apply("));
        Assert.Equal(1, Count(melon, "FishWarehouseDeliveryRestorePatch.Reset()"));
        Assert.Equal(1, Count(melon, "FishWarehouseManagementCanvasPatch.SetTargetProperty(null);"));
        Assert.Equal(1, Count(melon, "FishWarehousePropertyLoadCapturePatch.Reset();"));
        Assert.Equal(1, Count(melon, "FishWarehouseNativePropertyWriteGuardPatch.Reset();"));

        using var scope = new CompositionScope();
        Assert.Equal(
            new[]
            {
                "apply:WarehouseCanvas",
                "apply:WarehouseGrid",
                "apply:WarehouseDock",
                "factory:PropertyHost",
                "apply:WarehouseDeliveryRestore",
                "step:Warehouse.RuntimeService",
                "step:Warehouse.Acquisition",
                "step:Warehouse.SnapshotStore",
                "step:Warehouse.ReplayLifecycle",
                "step:Warehouse.PersistenceService",
                "step:Warehouse.CaptureService",
                "step:Warehouse.BannerSender",
                "step:Warehouse.CapacitySource",
                "step:Warehouse.Capacity",
                "step:Warehouse.ReplayOperations",
                "step:Warehouse.ReplayService",
                "apply:WarehouseLoadCapture",
                "apply:WarehouseWriteGuard",
            },
            scope.Engine.Trace
                .Where(entry => entry.StartsWith("apply:Warehouse", StringComparison.Ordinal) ||
                    entry.StartsWith("step:Warehouse", StringComparison.Ordinal) ||
                    entry == "factory:PropertyHost")
                .ToList());
        Assert.NotNull(scope.Root.Chapter3!.WarehouseAcquisition);
        Assert.NotNull(scope.Root.Chapter3.StaffReturnBannerSender);

        scope.Root.Teardown();
        Assert.Equal(
            new[]
            {
                "reset:WarehouseManagementTarget",
                "reset:WarehouseDeliveryRestore",
                "reset:WarehouseLoadCapture",
                "reset:WarehouseWriteGuard",
                "teardown:Capture.Reset",
            },
            scope.Engine.Trace
                .Where(entry => entry.StartsWith("reset:Warehouse", StringComparison.Ordinal) ||
                    entry == "teardown:Capture.Reset")
                .ToList());
        Assert.Null(scope.Root.WarehouseReplay);
        Assert.Null(scope.Root.WarehousePersistence);
        Assert.Null(scope.Root.WarehouseCapture);
        Assert.Null(scope.Root.WarehouseRuntime);
    }


    private static string ReadCompositionSource(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", fileName));

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string BodyOf(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find signature: {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Could not find body start: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
                depth--;

            if (depth == 0)
                return source[(bodyStart + 1)..index];
        }

        throw new InvalidOperationException($"Could not parse body for signature: {signature}");
    }

    private static void AssertOrdered(string source, params string[] expected)
    {
        var lastIndex = -1;
        foreach (var value in expected)
        {
            var index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Could not find expected source: {value}");
            Assert.True(index > lastIndex, $"Expected source out of order: {value}");
            lastIndex = index;
        }
    }


    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git", "HEAD")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Organized Crime repository root could not be located.");
    }
}
