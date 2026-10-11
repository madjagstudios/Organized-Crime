// OC-151: the single home of every composition expectation shared by more than one
// test file: the exact setup/teardown traces, patch/subscribe sequences, pref reads,
// the QA key map, and the lifecycle table shapes. Test files keep intent-named thin
// callers; each guarantee is pinned once.
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

internal static class CompositionAssertions
{
    public static IReadOnlyList<string> ExpectedImguiSetupTrace { get; } = new[]
    {
        "read:OwnerQaKeys",
        "read:TimingThreshold",
        "step:Root.HqContext",
        "step:Release1.Story",
        "factory:DayClock",
        "step:Chapter3.Service",
        "step:Chapter3.QaAccess",
        "factory:Arthur",
        "apply:ArthurResponse",
        "factory:BeatProbe",
        "factory:Beating",
        "factory:OfficerWorld",
        "step:Chapter3.Roster",
        "factory:Overnight",
        "read:PresentationMode",
        "step:Hq.Boundary",
        "step:Hq.Storage",
        "factory:CourtesyWorld",
        "factory:CartelSource",
        "factory:PhoneQueue",
        "step:Release1.Production",
        "step:Chapter3.Wallet",
        "apply:HqStorageLoad",
        "step:Hq.Observer",
        "step:Hq.Interior",
        "step:Hq.Runtime",
        "step:Hq.Composition",
        "step:Chapter3.HqPresence",
        "step:Chapter3.BeatingHq",
        "factory:Demand",
        "factory:VisitConditions",
        "factory:ArthurPhone",
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
        "subscribe:SaveComplete",
        "subscribe:SaveStart",
        "subscribe:PreLoad",
        "subscribe:LoadComplete",
        "subscribe:PreSceneChange",
        "factory:LawAdapter",
        "step:Law.Composition",
        "step:Law.Bridge",
        "apply:Custody",
        "factory:ProtectionCheckpointWorld",
        "factory:ExposureReader",
        "factory:CurfewReader",
        "factory:WantedWriter",
        "factory:PursuitHold",
        "step:Release1.Chief",
        "step:Chapter3.ChiefLinks",
        "factory:CheckpointWorld",
        "step:Chapter3.Checkpoints",
        "configure:ProtectionGate",
        "apply:ProtectionGate",
        "apply:CheckpointSwap",
    };

    public static IReadOnlyList<string> ExpectedNativeSetupTrace { get; } = WithNativeSteps(ExpectedImguiSetupTrace);

    public static IReadOnlyList<string> ExpectedTeardownTrace { get; } = new[]
    {
        "reset:LockdownGate",
        "reset:ProtectionGate",
        "teardown:Hq.MarkTeardown",
        "teardown:Hq.TryDispose",
        "reset:HqStorageLoad",
        "teardown:HqStorage.Dispose",
        "teardown:Production.Dispose",
        "teardown:Chief.Dispose",
        "teardown:Story.Dispose",
        "teardown:Projector.Dispose",
        "teardown:Checkpoints.Dispose",
        "teardown:Roster.Dispose",
        "dispose:Arthur",
        "teardown:QaAccess.Clear",
        "teardown:Chapter3.Dispose",
        "teardown:Overnight.Dispose",
        "dispose:DayClock",
        "reset:Custody",
        "teardown:Custody.Dispose",
        "teardown:Law.Dispose",
        "reset:WarehouseManagementTarget",
        "reset:WarehouseDeliveryRestore",
        "reset:WarehouseLoadCapture",
        "reset:WarehouseWriteGuard",
        "unsubscribe:SaveComplete",
        "unsubscribe:SaveStart",
        "unsubscribe:PreLoad",
        "unsubscribe:LoadComplete",
        "unsubscribe:PreSceneChange",
        "teardown:Capture.Reset",
    };

    public static IReadOnlyList<string> ExpectedPatchCalls { get; } = new[]
    {
        "apply:ArthurResponse",
        "apply:HqStorageLoad",
        "apply:WarehouseCanvas",
        "apply:WarehouseGrid",
        "apply:WarehouseDock",
        "apply:WarehouseDeliveryRestore",
        "apply:WarehouseLoadCapture",
        "apply:WarehouseWriteGuard",
        "apply:Custody",
        "configure:ProtectionGate",
        "apply:ProtectionGate",
        "apply:CheckpointSwap",
    };

    public static IReadOnlyList<string> ExpectedSubscribeCalls { get; } = new[]
    {
        "subscribe:SaveComplete",
        "subscribe:SaveStart",
        "subscribe:PreLoad",
        "subscribe:LoadComplete",
        "subscribe:PreSceneChange",
    };

    public static IReadOnlyList<string> ExpectedUnsubscribeCalls { get; } = new[]
    {
        "unsubscribe:SaveComplete",
        "unsubscribe:SaveStart",
        "unsubscribe:PreLoad",
        "unsubscribe:LoadComplete",
        "unsubscribe:PreSceneChange",
    };

    public static IReadOnlyList<(KeyCode Key, string Name)> ExpectedKeyMap { get; } = new (KeyCode Key, string Name)[]
    {
        (KeyCode.Backslash, "warehouse-lifecycle"),
        (KeyCode.O, "warehouse-unlock"),
        (KeyCode.P, "warehouse-place-home"),
        (KeyCode.Semicolon, "warehouse-lamp-add"),
        (KeyCode.Quote, "warehouse-lamp-remove"),
        (KeyCode.PageUp, "warehouse-lamp-raise"),
        (KeyCode.PageDown, "warehouse-lamp-lower"),
        (KeyCode.LeftBracket, "warehouse-lamp-dim"),
        (KeyCode.RightBracket, "warehouse-lamp-brighten"),
        (KeyCode.Comma, "warehouse-lamp-yaw"),
        (KeyCode.Equals, "warehouse-lamp-layout"),
        (KeyCode.F8, "recognition"),
        (KeyCode.Minus, "chapter3-start"),
        (KeyCode.F9, "staging-stage"),
        (KeyCode.F10, "staging-dump"),
        (KeyCode.F11, "hold-room-dump"),
        (KeyCode.F6, "quantity-decrement"),
        (KeyCode.F1, "cash-gate"),
        (KeyCode.F2, "closet-cash-gate"),
        (KeyCode.Home, "production-gate"),
        (KeyCode.End, "lockdown-toggle"),
        (KeyCode.Insert, "npc-registry"),
    };

    public static void AssertSetupTrace(FakeOcEngineAdapters engine) =>
        Assert.Equal(ExpectedImguiSetupTrace, engine.Trace);

    public static void AssertNativeSetupTrace(FakeOcEngineAdapters engine) =>
        Assert.Equal(ExpectedNativeSetupTrace, engine.Trace);

    public static void AssertTeardownTrace(FakeOcEngineAdapters engine, IReadOnlyList<string> setup) =>
        Assert.Equal(setup.Concat(ExpectedTeardownTrace), engine.Trace);

    public static void AssertPatchCalls(FakeOcEngineAdapters engine) =>
        Assert.Equal(ExpectedPatchCalls, engine.PatchCalls);

    public static void AssertSubscribeCalls(FakeOcEngineAdapters engine) =>
        Assert.Equal(ExpectedSubscribeCalls, engine.SubscribeCalls);

    public static void AssertUnsubscribeCalls(FakeOcEngineAdapters engine) =>
        Assert.Equal(ExpectedUnsubscribeCalls, engine.UnsubscribeCalls);

    public static void AssertImguiPrefReads(FakeOcEngineAdapters engine)
    {
        Assert.Equal(1, engine.OwnerQaKeysReads);
        Assert.Equal(1, engine.TimingThresholdReads);
        Assert.Equal(1, engine.PresentationModeReads);
        Assert.Equal(0, engine.QuestPolicyReads);
        Assert.Equal("read:OwnerQaKeys", engine.Trace[0]);
        Assert.Equal("read:TimingThreshold", engine.Trace[1]);
    }

    public static void AssertNativePrefReads(FakeOcEngineAdapters engine)
    {
        Assert.Equal(1, engine.OwnerQaKeysReads);
        Assert.Equal(1, engine.TimingThresholdReads);
        Assert.Equal(1, engine.PresentationModeReads);
        Assert.Equal(2, engine.QuestPolicyReads);
    }

    public static void AssertKeyMap(IReadOnlyList<QaKeyBinding> bindings) =>
        Assert.Equal(ExpectedKeyMap, bindings.Select(binding => (binding.Key, binding.Name)));

    public static void AssertPhaseTables(LifecycleTables tables)
    {
        AssertSaveCompleteTable(tables);
        AssertSaveStartTable(tables);
        AssertPreLoadTable(tables);
        AssertLoadCompleteTable(tables);
        AssertPreSceneChangeTable(tables);
    }

    public static void AssertSaveCompleteTable(LifecycleTables tables) =>
        Assert.Equal(new[]
        {
            "save-complete/story",
            "save-complete/chapter3",
            "save-complete/chapter3-projector",
            "save-complete/officers",
            "save-complete/checkpoints",
            "save-complete/production",
            "save-complete/chief",
            "save-complete/hq",
            "save-complete/fish-warehouse",
        }, tables.SaveComplete.Select(step => step.Name));

    public static void AssertSaveStartTable(LifecycleTables tables) =>
        Assert.Equal(new[]
        {
            "save-start/story",
            "save-start/chapter3",
            "save-start/chapter3-projector",
            "save-start/officers",
            "save-start/checkpoints",
            "save-start/production",
            "save-start/chief",
            "save-start/hq",
        }, tables.SaveStart.Select(step => step.Name));

    public static void AssertPreLoadTable(LifecycleTables tables)
    {
        Assert.Equal(new[]
        {
            "pre-load/hq-storage-cycle",
            "pre-load/hq",
            "pre-load/production",
            "pre-load/chief",
            "pre-load/story",
            "pre-load/chapter3",
            "pre-load/chapter3-projector",
            "pre-load/officers",
            "pre-load/checkpoints",
            "pre-load/fish-warehouse",
        }, tables.PreLoad.Select(step => step.Name));
        Assert.Equal(LifecycleStepKind.Untimed, tables.PreLoad[0].Kind);
        Assert.All(tables.PreLoad.Skip(1), step => Assert.Equal(LifecycleStepKind.Timed, step.Kind));
    }

    public static void AssertLoadCompleteTable(LifecycleTables tables)
    {
        Assert.Equal("load-complete/hq-prepare", tables.LoadCompletePrepare!.Name);
        Assert.Equal(new[]
        {
            "load-complete/story",
            "load-complete/chapter3",
            "load-complete/chapter3-projector",
            "load-complete/officers",
            "load-complete/checkpoints",
            "load-complete/production",
            "load-complete/chief",
            "load-complete/hq-storage",
        }, tables.LoadCompleteMain.Select(step => step.Name));
        Assert.Equal("load-complete/hq", tables.LoadCompleteHq!.Name);
        Assert.Equal("load-complete/fish-warehouse", tables.LoadCompleteFish!.Name);
        Assert.NotNull(tables.LoadCompleteTail);
    }

    public static void AssertPreSceneChangeTable(LifecycleTables tables) =>
        Assert.Equal(new[]
        {
            "pre-scene-change/production",
            "pre-scene-change/hq",
            "pre-scene-change/chapter3-projector",
        }, tables.PreSceneChange.Select(step => step.Name));

    public static void AssertUpdateSlotsWired(LifecycleSlots slots)
    {
        Assert.NotNull(slots.HqUpdate);
        Assert.NotNull(slots.ProductionUpdate);
        Assert.NotNull(slots.ProjectorReconcile);
        Assert.NotNull(slots.ArthurPoll);
        Assert.NotNull(slots.EmergencyPoll);
        Assert.NotNull(slots.ChiefUpdate);
        Assert.NotNull(slots.ReadinessPump);
        Assert.NotNull(slots.WarehouseUpdate);
        Assert.NotNull(slots.ReplayUpdate);
        Assert.NotNull(slots.WarehouseBuildPoll);
        Assert.NotNull(slots.DemandPoll);
        Assert.NotNull(slots.RosterUpdate);
        Assert.NotNull(slots.CheckpointsUpdate);
        Assert.NotNull(slots.EnsureUi);
        Assert.NotNull(slots.QaPoll);
        Assert.NotNull(slots.HqGui);
        Assert.NotNull(slots.ProductionGui);
    }

    public static void AssertRosterAndCheckpointsSlotsDriveTheirServices(CompositionScope scope)
    {
        var slots = LifecycleTables.ForRoot(scope.Root).Update;
        var officerReads = scope.Engine.OfficerWorld.IsHostReads;
        var checkpointReads = scope.Engine.CheckpointWorld.IsHostReads;

        slots.RosterUpdate!();
        slots.CheckpointsUpdate!();

        Assert.Equal(officerReads + 1, scope.Engine.OfficerWorld.IsHostReads);
        Assert.Equal(checkpointReads + 1, scope.Engine.CheckpointWorld.IsHostReads);
    }

    public static void AssertThrowPartialKeepsPartialState()
    {
        using var scope = new CompositionScope(engine => engine.CourtesyWorld = null!, build: false);

        var thrown = Assert.Throws<ArgumentNullException>(() => scope.Root.Build());

        Assert.Equal("smallCourtesyWorld", thrown.ParamName);
        Assert.NotNull(scope.Root.Story);
        Assert.NotNull(scope.Root.Chapter3);
        Assert.NotNull(scope.Root.HqStorage);
        Assert.Null(scope.Root.Production);
        Assert.Null(scope.Root.WarehouseRuntime);
        Assert.Empty(scope.Engine.SubscribeCalls);
        Assert.Equal(new[]
        {
            "read:OwnerQaKeys",
            "read:TimingThreshold",
            "step:Root.HqContext",
            "step:Release1.Story",
            "factory:DayClock",
            "step:Chapter3.Service",
            "step:Chapter3.QaAccess",
            "factory:Arthur",
            "apply:ArthurResponse",
            "factory:BeatProbe",
            "factory:Beating",
            "factory:OfficerWorld",
            "step:Chapter3.Roster",
            "factory:Overnight",
            "read:PresentationMode",
            "step:Hq.Boundary",
            "step:Hq.Storage",
            "factory:CourtesyWorld",
            "factory:CartelSource",
            "factory:PhoneQueue",
        }, scope.Engine.Trace);
    }

    public static void AssertForRootSlotNeedles()
    {
        // OC-151 Blocker 5: each needle binds its callback to its slot assignment, so
        // swapping two callbacks between slots fails even though both calls still appear.
        var fanout = ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");

        foreach (var needle in new[]
                 {
                     "HqUpdate = () => root.Hq?.Update(),",
                     "ProductionUpdate = () => root.Production?.Update(),",
                     "ProjectorReconcile = () => root.Chapter3Projector?.Reconcile(),",
                     "ArthurPoll = () => root.Chapter3?.PollArthurVisit(),",
                     "EmergencyPoll = () => root.Chapter3?.PollEmergencyExpiry(),",
                     "ChiefUpdate = () => root.Chief?.Update(),",
                     "ReadinessPump = () => root.LocalPressure?.Service.PumpReadiness(),",
                     "WarehouseUpdate = () => root.WarehouseRuntime?.Update(),",
                     "ReplayUpdate = () => root.WarehouseReplay?.Update(),",
                     "WarehouseBuildPoll = () => root.Chapter3?.PollPendingWarehouseBuild(",
                     "DemandPoll = () => root.Chapter3?.PollProductionDemand(),",
                     "RosterUpdate = () => root.OfficerRoster?.Update(),",
                     "CheckpointsUpdate = () => root.Checkpoints?.Update(),",
                     "EnsureUi = () => root.PropertyHost?.EnsureManagementWorldspaceUiContainerForOpenCanvas(),",
                     "QaPoll = () => root.QaKeys.Poll(),",
                     "HqGui = () => root.Hq?.OnGUI(),",
                     "ProductionGui = () => root.Production?.OnGUI(),",
                 })
            Assert.Contains(needle, tables, StringComparison.Ordinal);
    }

    public static void AssertForRootRosterLifecycleNeedles()
    {
        var fanout = ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");

        foreach (var needle in new[]
                 {
                     "OfficerRoster?.OnSaveStart()",
                     "OfficerRoster?.OnSaveComplete()",
                     "OfficerRoster?.OnPreLoad()",
                     "OfficerRoster?.OnLoadComplete()",
                 })
            Assert.Contains(needle, tables, StringComparison.Ordinal);
    }

    public static void AssertForRootCheckpointLifecycleNeedles()
    {
        var fanout = ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");

        foreach (var needle in new[]
                 {
                     "Checkpoints?.OnSaveStart()",
                     "Checkpoints?.OnSaveComplete()",
                     "Checkpoints?.OnPreLoad()",
                     "Checkpoints?.OnLoadComplete()",
                 })
            Assert.Contains(needle, tables, StringComparison.Ordinal);
    }

    public static void AssertForRootProductionLifecycleNeedles()
    {
        var fanout = ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");

        Assert.Contains("root.Production?.OnSaveComplete()", Slice(tables, "SaveComplete = new[]", "SaveStart = new[]"), StringComparison.Ordinal);
        Assert.Contains("root.Production?.OnSaveStart()", Slice(tables, "SaveStart = new[]", "PreLoad = new[]"), StringComparison.Ordinal);
        Assert.Contains("root.Production?.OnPreLoad()", Slice(tables, "PreLoad = new[]", "LoadCompletePrepare ="), StringComparison.Ordinal);
        Assert.Contains("root.Production?.OnLoadComplete()", Slice(tables, "LoadCompleteMain = new[]", "LoadCompleteHq ="), StringComparison.Ordinal);
        Assert.Contains("root.Production?.OnPreSceneChange()", Slice(tables, "PreSceneChange = new[]", "Update = new LifecycleSlots"), StringComparison.Ordinal);
        Assert.Contains("ProductionUpdate = () => root.Production?.Update()", tables, StringComparison.Ordinal);
        Assert.Contains("ProductionGui = () => root.Production?.OnGUI()", tables, StringComparison.Ordinal);
    }

    public static void AssertForRootChiefLifecycleNeedles()
    {
        var fanout = ReadProductionSource("tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs");
        var tables = Slice(fanout, "public static LifecycleTables ForRoot", "public LifecycleFanout(");

        Assert.Contains("root.Chief?.OnSaveComplete()", Slice(tables, "SaveComplete = new[]", "SaveStart = new[]"), StringComparison.Ordinal);
        Assert.Contains("root.Chief?.OnSaveStart()", Slice(tables, "SaveStart = new[]", "PreLoad = new[]"), StringComparison.Ordinal);
        Assert.Contains("root.Chief?.OnPreLoad()", Slice(tables, "PreLoad = new[]", "LoadCompletePrepare ="), StringComparison.Ordinal);
        Assert.Contains("root.Chief?.OnLoadComplete()", Slice(tables, "LoadCompleteMain = new[]", "LoadCompleteHq ="), StringComparison.Ordinal);
        Assert.DoesNotContain("root.Chief", Slice(tables, "PreSceneChange = new[]", "Update = new LifecycleSlots"), StringComparison.Ordinal);
        Assert.Contains("ChiefUpdate = () => root.Chief?.Update()", tables, StringComparison.Ordinal);
    }

    public static int CountTryLines(string text)
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

    public static string ReadProductionSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("repository root not found");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    public static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"slice {from}..{to} not found");
        return text.Substring(start, end - start);
    }

    private static IReadOnlyList<string> WithNativeSteps(IReadOnlyList<string> imgui)
    {
        var list = imgui.ToList();
        list.InsertRange(
            list.IndexOf("read:PresentationMode") + 1,
            new[] { "read:QuestPolicy", "factory:Release1Native" });
        list.Insert(
            list.IndexOf("step:Chapter3.Wallet") + 1,
            "step:Chapter3.Projector");
        list.InsertRange(
            list.IndexOf("factory:ProtectionCheckpointWorld"),
            new[] { "read:QuestPolicy", "factory:ChiefNative" });
        return list;
    }
}
