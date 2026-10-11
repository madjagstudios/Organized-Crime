// OC-151: the production engine adapters. News up the real S1API/Il2Cpp adapters,
// reads the real MelonLoader preferences, applies the real Harmony patches through
// the mod's Harmony instance, and subscribes the real S1API lifecycle events. Every
// call mirrors the Mod.cs line it replaces, with the same instance, arguments and
// order. This file is engine-bound and is NOT linked into the test project.
using Il2CppScheduleOne.GameTime;
using OrganizedCrime.Model;
using Il2CppScheduleOne.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Platform;
using S1API.Lifecycle;
using UnityEngine;

namespace OrganizedCrime.Runtime.Composition;

internal sealed class MelonEngineAdapters : IOcEngineAdapters
{
    private readonly HarmonyLib.Harmony _harmony;
    private readonly Action<string> _warn;
    private readonly Action<bool>? _configureDiagnostics;

    public MelonEngineAdapters(HarmonyLib.Harmony harmony, Action<string> warn, Action<bool>? configureDiagnostics = null)
    {
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _configureDiagnostics = configureDiagnostics;
    }

    public IList<string>? CompositionTrace => null;

    public bool ReadOwnerQaKeys()
    {
        var enabled = OwnerQaKeysPreference.Read();
        _configureDiagnostics?.Invoke(enabled);
        return enabled;
    }
    public void RegisterOwnerQaConsoleCommand() =>
        Chapter3QaConsoleCommand.TryRegisterForOwner(message => OrganizedCrimeLog.Diagnostic(message));

    public int ReadTimingThresholdMs() => TimingReceiptThresholdPreference.Read();
    public Release1PresentationMode ReadPresentationMode() => Release1PresentationModePreference.Read();
    public Release1QuestPersistencePolicy ReadQuestPolicy() => Release1QuestPersistencePolicyPreference.Read();

    public bool IsHost() => FishNetHostGate.IsHostNow();
    public float DeltaTime() => Time.deltaTime;

    public string? ActiveSaveFolder()
    {
        try
        {
            return LoadManager.Instance?.LoadedGameFolderPath;
        }
        catch (Exception ex)
        {
            _warn($"[Organized Crime] Could not resolve the active save folder: {ex.Message}");
            return null;
        }
    }

    public bool IsPlayerTyping()
    {
        try
        {
            return Il2CppScheduleOne.GameInput.IsTyping;
        }
        catch
        {
            return false;
        }
    }

    public bool ReadSleepBuildPhase()
    {
        try
        {
            return SleepController.Instance is not null &&
                SleepController.Instance.CurrentPhase == SleepController.EPhase.RunningSleepEvents;
        }
        catch
        {
            return false;
        }
    }

    public bool IsFishWarehouseReady() => RuntimePropertyReadiness.IsFishWarehouseReady();
    public bool IsKeyDown(KeyCode key) => Input.GetKeyDown(key);
    public bool IsLockdownActive() => Release1LockdownGatePatch.LockdownActive;

    public IChapter3DayClock CreateDayClock(Action<string>? warn) => new Chapter3DayClockAdapter(warn);
    public void DisposeDayClock(IChapter3DayClock clock) => ((IDisposable)clock).Dispose();
    public IChapter3ArthurWorld CreateArthurRuntime(Action<string> warn, Func<bool> hasAuthority) =>
        new S1ApiChapter3ArthurRuntime(warn, hasAuthority);
    public void DisposeArthurRuntime(IChapter3ArthurWorld world) => ((S1ApiChapter3ArthurRuntime)world).Dispose();
    public IChapter3BeatProbeWorld CreateBeatProbeWorld(Action<string> warn, Func<bool> isHost) =>
        new S1ApiChapter3BeatProbeWorld(warn, isHost);
    public IChapter3BeatingWorld CreateBeatingWorld(Action<string> warn, Func<bool> isHost) =>
        new S1ApiChapter3BeatingWorld(warn, isHost);
    public IOcOfficerWorld CreateOfficerWorld(Action<string>? warn)
    {
        return new Il2CppOcOfficerWorld(warn);
    }
    public IDisposable CreateOvernightLine(Chapter3RuntimeService service, Action<string>? log) =>
        new Chapter3OvernightLineAdapter(service, log);
    public IChapter3ProductionDemandWorld CreateDemandWorld(
        ISyndicateHqStorageRuntime? holdRoomStorage,
        Func<Release1WorldPoint?>? hqDoor,
        Func<bool>? isHost,
        Action<string>? log) =>
        new S1ApiChapter3ProductionDemandWorld(holdRoomStorage, hqDoor, isHost, log);
    public IChapter3ArthurVisitConditions CreateVisitConditions(Action<string> warn) =>
        new S1ApiChapter3ArthurVisitConditions(warn);
    public IChapter3ArthurPhone CreateArthurPhone(IRelease1PayphoneCue? cue, Func<bool> isAuthoritativeHost, Action<string> warn) =>
        new S1ApiChapter3ArthurPhone(cue, isAuthoritativeHost, warn);
    public IChapter3CheckpointWorld CreateCheckpointWorld(Action<string>? warn, Action<string>? log) =>
        new Il2CppChapter3CheckpointWorld(warn: warn, log: log);

    public IRelease1NativePresentation CreateRelease1Native(Release1QuestPersistencePolicy policy, Action<string>? warn) =>
        new S1ApiRelease1NativePresentation(policy, warn);
    public IRelease1NativePresentation CreateChiefNative(Release1QuestPersistencePolicy policy, Action<string>? warn) =>
        new S1ApiRelease1NativePresentation(
            policy,
            Release1NativePresentationSupport.ChiefNpcId,
            () => new Release1ChiefCampbellNpc(),
            npc => npc is Release1ChiefCampbellNpc,
            warn);
    public IRelease1SmallCourtesyWorld CreateSmallCourtesyWorld(IRelease1StoryHostContext hostContext, ISyndicateHqStorageRuntime? holdRoomStorage) =>
        new S1ApiRelease1SmallCourtesyWorld(hostContext, holdRoomStorage, lockdownHarmony: _harmony);
    public IRelease1PhoneCallQueue CreatePhoneCallQueue() => new S1ApiRelease1PhoneCallQueue();
    public IRelease1CartelStatusSource CreateCartelStatusSource() => new S1CartelStatusSource();
    public IRelease1ProtectionCheckpointWorld CreateProtectionCheckpointWorld(Action<string>? log) =>
        new S1ApiRelease1ProtectionCheckpointWorld(log);
    public IChapter3PlayerExposureReader CreateExposureReader(Action<string> warn) =>
        new S1ApiChapter3PlayerExposureReader(warn);
    public IChapter3CurfewReader CreateCurfewReader(Action<string> warn) =>
        new S1ApiChapter3CurfewReader(warn);
    public IChapter3WantedWriter CreateWantedWriter(Action<string> warn) =>
        new S1ApiChapter3WantedWriter(warn);
    public IChapter3PursuitHold CreatePursuitHold(Action<string> warn) =>
        new S1ApiChapter3PursuitHold(warn);

    public IFishWarehousePropertyHost CreatePropertyHost() => new RuntimePropertyHost();
    public ILocalPressureRuntimeHostAdapter CreateLawHostAdapter(Action<string>? log) =>
        new LocalPressureHostLifecycleAdapter(log);

    public bool ApplyArthurResponse(Action<string>? log) => Chapter3ArthurResponsePatch.Apply(_harmony, log);
    public bool ApplyHqStorageLoad(
        ISyndicateHqStorageRuntime runtime,
        SyndicateHqStorageLoadContextAdapter context,
        Action<string>? log,
        Action<string>? receiptLog) =>
        SyndicateHqStorageLoadPatch.Apply(_harmony, runtime, context, log, receiptLog);
    public void ApplyWarehouseCanvas() => FishWarehouseManagementCanvasPatch.Apply(_harmony);
    public void ApplyWarehouseGrid() => FishWarehouseNativeGridPlacementPatches.Apply(_harmony);
    public void ApplyWarehouseDock() => FishWarehouseDockOccupantRefreshPatch.Apply(_harmony);
    public void ApplyWarehouseDeliveryRestore(Func<bool> isReady, Action<string>? log) =>
        FishWarehouseDeliveryRestorePatch.Apply(_harmony, isReady, log);
    public void ApplyWarehouseLoadCapture(
        Func<bool> isAuthoritativeHost,
        Func<string?> activeSaveFolder,
        FishWarehousePropertyCaptureService captureService,
        Action<string>? log) =>
        FishWarehousePropertyLoadCapturePatch.Apply(_harmony, isAuthoritativeHost, activeSaveFolder, captureService, log);
    public void ApplyWarehouseWriteGuard(Func<bool> isAuthoritativeHost, Func<bool> isDegraded) =>
        FishWarehouseNativePropertyWriteGuardPatch.Apply(_harmony, isAuthoritativeHost, isDegraded);
    public void ApplyCustody(PoliceCustodyEvidenceBridge bridge, Action<string>? log) =>
        PoliceCustodyPatch.Apply(_harmony, bridge, log);
    public bool ApplyProtectionGate(Action<string>? log) => Release1ProtectionGatePatch.Apply(_harmony, log);
    public bool ApplyCheckpointSwap(Action<string>? log) => OcCheckpointSwapPatch.Apply(_harmony, log);
    public void BeginHqLoadCycle() => SyndicateHqStorageLoadPatch.BeginLoadCycle();
    public void CompleteHqLoad() => SyndicateHqStorageLoadPatch.CompleteAfterVanillaLoad();

    public void ConfigureProtectionGate(Func<bool>? isCovered, Func<bool>? isHost, Func<int?>? today)
    {
        Release1ProtectionGateState.IsCovered = isCovered;
        Release1ProtectionGateState.IsHost = isHost;
        Release1ProtectionGateState.Today = today;
    }

    public void ResetLockdownGate() => Release1LockdownGatePatch.Reset();
    public void ResetProtectionGate() => Release1ProtectionGateState.Reset();
    public void ResetHqStorageLoad() => SyndicateHqStorageLoadPatch.Reset();
    public void ResetCustody() => PoliceCustodyPatch.Reset();
    public void ClearWarehouseManagementTarget() => FishWarehouseManagementCanvasPatch.SetTargetProperty(null);
    public void ResetWarehouseDeliveryRestore() => FishWarehouseDeliveryRestorePatch.Reset();
    public void ResetWarehouseLoadCapture() => FishWarehousePropertyLoadCapturePatch.Reset();
    public void ResetWarehouseWriteGuard() => FishWarehouseNativePropertyWriteGuardPatch.Reset();

    public void SubscribeSaveComplete(Action handler) => GameLifecycle.OnSaveComplete += handler;
    public void SubscribeSaveStart(Action handler) => GameLifecycle.OnSaveStart += handler;
    public void SubscribePreLoad(Action handler) => GameLifecycle.OnPreLoad += handler;
    public void SubscribeLoadComplete(Action handler) => GameLifecycle.OnLoadComplete += handler;
    public void SubscribePreSceneChange(Action handler) => GameLifecycle.OnPreSceneChange += handler;
    public void UnsubscribeSaveComplete(Action handler) => GameLifecycle.OnSaveComplete -= handler;
    public void UnsubscribeSaveStart(Action handler) => GameLifecycle.OnSaveStart -= handler;
    public void UnsubscribePreLoad(Action handler) => GameLifecycle.OnPreLoad -= handler;
    public void UnsubscribeLoadComplete(Action handler) => GameLifecycle.OnLoadComplete -= handler;
    public void UnsubscribePreSceneChange(Action handler) => GameLifecycle.OnPreSceneChange -= handler;
}
