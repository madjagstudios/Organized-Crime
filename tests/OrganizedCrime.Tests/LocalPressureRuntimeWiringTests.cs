using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureRuntimeWiringTests
{
    [Fact]
    public void Mod_tick_pumps_local_pressure_readiness()
    {
        var onUpdateBody = BodyOf(ReadModSource(), "public override void OnUpdate()");

        Assert.Contains("_localPressureRuntime?.Service.PumpReadiness();", onUpdateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Mod_initialization_uses_the_native_law_response_composition_helper_before_custody_bridge_wiring()
    {
        var initializeBody = BodyOf(ReadModSource(), "public override void OnInitializeMelon()");

        Assert.Equal(1, Count(initializeBody, "_nativeLawResponseComposition = new NativeLawResponseModComposition("));
        Assert.Equal(1, Count(initializeBody, "_nativeLawResponseController = _nativeLawResponseComposition.Controller;"));
        Assert.Equal(1, Count(initializeBody, "_localPressureRuntime = _nativeLawResponseComposition.LocalPressureRuntime;"));
        Assert.Equal(1, Count(initializeBody, "_policeCustodyEvidenceBridge = new PoliceCustodyEvidenceBridge("));

        AssertOrdered(
            initializeBody,
            "_nativeLawResponseComposition = new NativeLawResponseModComposition(",
            "_nativeLawResponseController = _nativeLawResponseComposition.Controller;",
            "_localPressureRuntime = _nativeLawResponseComposition.LocalPressureRuntime;",
            "_policeCustodyEvidenceBridge = new PoliceCustodyEvidenceBridge(",
            "PoliceCustodyPatch.Apply(");
    }

    [Fact]
    public void Mod_deinitialization_disposes_native_law_response_composition_after_custody_bridge_and_before_fish_warehouse_teardown()
    {
        var deinitializeBody = BodyOf(ReadModSource(), "public override void OnDeinitializeMelon()");

        Assert.Equal(1, Count(deinitializeBody, "PoliceCustodyPatch.Reset();"));
        Assert.Equal(1, Count(deinitializeBody, "_policeCustodyEvidenceBridge?.Dispose();"));
        Assert.Equal(1, Count(deinitializeBody, "_nativeLawResponseComposition?.Dispose();"));

        AssertOrdered(
            deinitializeBody,
            "PoliceCustodyPatch.Reset();",
            "_policeCustodyEvidenceBridge?.Dispose();",
            "_policeCustodyEvidenceBridge = null;",
            "_nativeLawResponseComposition?.Dispose();",
            "_nativeLawResponseComposition = null;",
            "_nativeLawResponseController = null;",
            "_localPressureRuntime = null;",
            "FishWarehouseManagementCanvasPatch.SetTargetProperty(null);");
    }

    [Fact]
    public void Mod_keeps_oc41_out_of_update_and_adds_no_new_lifecycle_or_patch_hooks()
    {
        var modSource = ReadModSource();
        var onUpdateBody = BodyOf(modSource, "public override void OnUpdate()");

        Assert.DoesNotContain("NativeLawResponse", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("NativePoliceDispatchAdapter", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeLawResponseController", onUpdateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeLawResponseComposition", onUpdateBody, StringComparison.Ordinal);

        Assert.Equal(1, Count(modSource, "FishWarehouseManagementCanvasPatch.Apply(HarmonyInstance);"));
        Assert.Equal(1, Count(modSource, "FishWarehouseNativeGridPlacementPatches.Apply(HarmonyInstance);"));
        Assert.Equal(1, Count(modSource, "FishWarehouseDockOccupantRefreshPatch.Apply(HarmonyInstance);"));
        Assert.Equal(1, Count(modSource, "FishWarehousePropertyLoadCapturePatch.Apply("));
        Assert.Equal(1, Count(modSource, "FishWarehouseNativePropertyWriteGuardPatch.Apply("));
        Assert.Equal(1, Count(modSource, "PoliceCustodyPatch.Apply("));

        Assert.Equal(1, Count(modSource, "GameLifecycle.OnSaveComplete += HandleSaveComplete;"));
        Assert.Equal(1, Count(modSource, "GameLifecycle.OnPreLoad += HandlePreLoad;"));
        Assert.Equal(1, Count(modSource, "GameLifecycle.OnLoadComplete += HandleLoadComplete;"));
        Assert.Equal(1, Count(modSource, "GameLifecycle.OnSaveComplete -= HandleSaveComplete;"));
        Assert.Equal(1, Count(modSource, "GameLifecycle.OnPreLoad -= HandlePreLoad;"));
        Assert.Equal(1, Count(modSource, "GameLifecycle.OnLoadComplete -= HandleLoadComplete;"));
    }

    [Fact]
    public void Mod_defers_hq_load_epoch_until_recovery_and_keeps_other_lifecycle_notifications()
    {
        var modSource = ReadModSource();
        var preLoadBody = BodyOf(modSource, "private void HandlePreLoad()");
        var loadCompleteBody = BodyOf(modSource, "private void HandleLoadComplete()");

        Assert.Contains("_syndicateHqComposition?.OnPreLoad();", preLoadBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_syndicateHqHostContext?.BeginLoad();", preLoadBody, StringComparison.Ordinal);
        Assert.Contains("_release1StoryRuntime?.OnPreLoad()", preLoadBody, StringComparison.Ordinal);
        Assert.Contains("_fishWarehousePersistenceReplayService?.PrepareForLoad();", preLoadBody, StringComparison.Ordinal);
        AssertOrdered(preLoadBody, "_syndicateHqComposition?.OnPreLoad();", "_release1StoryRuntime?.OnPreLoad()");

        // OC-63 wraps each load-complete child in the timing seam, so the assignment now lands
        // inside a measured lambda rather than reading straight into a var declaration. The wiring
        // the guard cares about is unchanged: the HQ prepares first, and its result still gates
        // the later OnLoadComplete. OC-10 wraps the story call in
        // Release1StoryLifecycleLogging.LogIfRejected so a rejected lifecycle result gets logged,
        // so the call site is now a substring without its own trailing semicolon.
        Assert.Contains("hqLoadPrepared = _syndicateHqComposition?.PrepareLoadComplete() ?? true;", loadCompleteBody, StringComparison.Ordinal);
        AssertOrdered(loadCompleteBody, "PrepareLoadComplete()", "_release1StoryRuntime?.OnLoadComplete()", "_syndicateHqComposition?.OnLoadComplete();");
    }

    [Fact]
    public void Mod_finishes_teardown_when_hq_listener_cleanup_cannot_succeed_at_quit()
    {
        var deinitializeBody = BodyOf(ReadModSource(), "public override void OnDeinitializeMelon()");

        Assert.Contains("if (_syndicateHqComposition is not null && !_syndicateHqComposition.TryDispose())", deinitializeBody, StringComparison.Ordinal);
        Assert.Contains(
            "MelonLogger.Warning(\"[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.\");",
            deinitializeBody,
            StringComparison.Ordinal);
        AssertOrdered(
            deinitializeBody,
            "_syndicateHqStorageRuntime?.MarkTeardown();",
            "_syndicateHqComposition.TryDispose()",
            "MelonLogger.Warning(\"[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.\");",
            "SyndicateHqStorageLoadPatch.Reset();",
            "_syndicateHqStorageRuntime?.Dispose();",
            "_release1StoryRuntime?.Dispose();");
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
    public void Mod_preserves_exact_fish_warehouse_statement_count_and_order()
    {
        var modSource = ReadModSource();
        var initializeFishWarehouseStatements = MatchingLines(
            BodyOf(modSource, "public override void OnInitializeMelon()"),
            line => line.Contains("FishWarehouse", StringComparison.Ordinal) ||
                line.Contains("_fishWarehouse", StringComparison.Ordinal));
        var deinitializeFishWarehouseStatements = MatchingLines(
            BodyOf(modSource, "public override void OnDeinitializeMelon()"),
            line => line.Contains("FishWarehouse", StringComparison.Ordinal) ||
                line.Contains("_fishWarehouse", StringComparison.Ordinal));

        Assert.Equal(
            new[]
            {
                "FishWarehouseManagementCanvasPatch.Apply(HarmonyInstance);",
                "FishWarehouseNativeGridPlacementPatches.Apply(HarmonyInstance);",
                "FishWarehouseDockOccupantRefreshPatch.Apply(HarmonyInstance);",
                "FishWarehouseDeliveryRestorePatch.Apply(",
                "_fishWarehouseRuntimeService = new FishWarehouseRuntimeService(",
                "RuntimePropertyReadiness.IsFishWarehouseReady,",
                "var snapshotStore = new FishWarehousePropertySnapshotStore();",
                "FishWarehousePropertyCaptureService? captureService = null;",
                "var replayLifecycle = new FishWarehouseReplayLifecycle(",
                "new FishWarehouseCaptureRecoveryPrerequisites(",
                "_fishWarehousePersistenceService = new FishWarehousePersistenceService(",
                "new FishWarehouseSaveStore(),",
                "() => _fishWarehouseRuntimeService?.CaptureSaveState() ?? FishWarehouseSaveState.UnownedState(),",
                "captureService = new FishWarehousePropertyCaptureService(",
                "new FishWarehouseNativePropertySnapshotInspector(),",
                "_fishWarehousePersistenceService,",
                "_fishWarehousePropertyCaptureService = captureService;",
                "var runtimeService = _fishWarehouseRuntimeService;",
                "var persistenceService = _fishWarehousePersistenceService;",
                "var replayOperations = new FishWarehousePersistenceReplayOperations(",
                "_fishWarehousePersistenceReplayService = new FishWarehousePersistenceReplayService(",
                "FishWarehouseDeliveryRestorePatch.Reset,",
                "FishWarehousePropertyLoadCapturePatch.Apply(",
                "FishWarehouseNativePropertyWriteGuardPatch.Apply("
            },
            initializeFishWarehouseStatements);

        Assert.Equal(
            new[]
            {
                "FishWarehouseManagementCanvasPatch.SetTargetProperty(null);",
                "FishWarehouseDeliveryRestorePatch.Reset();",
                "FishWarehousePropertyLoadCapturePatch.Reset();",
                "FishWarehouseNativePropertyWriteGuardPatch.Reset();",
                "_fishWarehousePersistenceReplayService = null;",
                "_fishWarehousePersistenceService = null;",
                "_fishWarehousePropertyCaptureService?.Reset();",
                "_fishWarehousePropertyCaptureService = null;",
                "_fishWarehouseRuntimeService = null;"
            },
            deinitializeFishWarehouseStatements);
    }

    private static string ReadModSource() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Mod.cs"));

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

    private static string[] MatchingLines(string source, Func<string, bool> predicate) =>
        source.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && predicate(line))
            .ToArray();

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
