// OC-151: every engine-facing construction, preference read, patch registration and
// lifecycle subscription the composition modules need, substitutable in tests. The
// production implementation (MelonEngineAdapters, not linked into tests) news up the
// real S1API/Il2Cpp adapters and calls the real patch Applies; the composition tests
// substitute a recording fake. CompositionTrace is null in production (zero overhead)
// and collects the exact setup/teardown step sequence in tests.
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime.Composition;

internal interface IOcEngineAdapters
{
    IList<string>? CompositionTrace { get; }

    bool ReadOwnerQaKeys();
    void RegisterOwnerQaConsoleCommand();
    int ReadTimingThresholdMs();
    Release1PresentationMode ReadPresentationMode();
    Release1QuestPersistencePolicy ReadQuestPolicy();

    bool IsHost();
    float DeltaTime();
    string? ActiveSaveFolder();
    bool IsPlayerTyping();
    bool ReadSleepBuildPhase();
    bool IsFishWarehouseReady();
    bool IsKeyDown(KeyCode key);
    bool IsLockdownActive();

    IChapter3DayClock CreateDayClock(Action<string>? warn);
    void DisposeDayClock(IChapter3DayClock clock);
    IChapter3ArthurWorld CreateArthurRuntime(Action<string> warn, Func<bool> hasAuthority);
    void DisposeArthurRuntime(IChapter3ArthurWorld world);
    IChapter3BeatProbeWorld CreateBeatProbeWorld(Action<string> warn, Func<bool> isHost);
    IChapter3BeatingWorld CreateBeatingWorld(Action<string> warn, Func<bool> isHost);
    IOcOfficerWorld CreateOfficerWorld(Action<string>? warn);
    IDisposable CreateOvernightLine(Chapter3RuntimeService service, Action<string>? log);
    IChapter3ProductionDemandWorld CreateDemandWorld(
        ISyndicateHqStorageRuntime? holdRoomStorage,
        Func<Release1WorldPoint?>? hqDoor,
        Func<bool>? isHost,
        Action<string>? log);
    IChapter3ArthurVisitConditions CreateVisitConditions(Action<string> warn);
    IChapter3ArthurPhone CreateArthurPhone(IRelease1PayphoneCue? cue, Func<bool> isAuthoritativeHost, Action<string> warn);
    IChapter3CheckpointWorld CreateCheckpointWorld(Action<string>? warn, Action<string>? log);

    IRelease1NativePresentation CreateRelease1Native(Release1QuestPersistencePolicy policy, Action<string>? warn);
    IRelease1NativePresentation CreateChiefNative(Release1QuestPersistencePolicy policy, Action<string>? warn);
    IRelease1SmallCourtesyWorld CreateSmallCourtesyWorld(IRelease1StoryHostContext hostContext, ISyndicateHqStorageRuntime? holdRoomStorage);
    IRelease1PhoneCallQueue CreatePhoneCallQueue();
    IRelease1CartelStatusSource CreateCartelStatusSource();
    IRelease1ProtectionCheckpointWorld CreateProtectionCheckpointWorld(Action<string>? log);
    IChapter3PlayerExposureReader CreateExposureReader(Action<string> warn);
    IChapter3CurfewReader CreateCurfewReader(Action<string> warn);
    IChapter3WantedWriter CreateWantedWriter(Action<string> warn);
    IChapter3PursuitHold CreatePursuitHold(Action<string> warn);

    IFishWarehousePropertyHost CreatePropertyHost();
    ILocalPressureRuntimeHostAdapter CreateLawHostAdapter(Action<string>? log);

    bool ApplyArthurResponse(Action<string>? log);
    bool ApplyHqStorageLoad(
        ISyndicateHqStorageRuntime runtime,
        SyndicateHqStorageLoadContextAdapter context,
        Action<string>? log,
        Action<string>? receiptLog);
    void ApplyWarehouseCanvas();
    void ApplyWarehouseGrid();
    void ApplyWarehouseDock();
    void ApplyWarehouseDeliveryRestore(Func<bool> isReady, Action<string>? log);
    void ApplyWarehouseLoadCapture(
        Func<bool> isAuthoritativeHost,
        Func<string?> activeSaveFolder,
        FishWarehousePropertyCaptureService captureService,
        Action<string>? log);
    void ApplyWarehouseWriteGuard(Func<bool> isAuthoritativeHost, Func<bool> isDegraded);
    void ApplyCustody(PoliceCustodyEvidenceBridge bridge, Action<string>? log);
    bool ApplyProtectionGate(Action<string>? log);
    bool ApplyCheckpointSwap(Action<string>? log);
    void BeginHqLoadCycle();
    void CompleteHqLoad();
    void ConfigureProtectionGate(Func<bool>? isCovered, Func<bool>? isHost, Func<int?>? today);
    void ResetLockdownGate();
    void ResetProtectionGate();
    void ResetHqStorageLoad();
    void ResetCustody();
    void ClearWarehouseManagementTarget();
    void ResetWarehouseDeliveryRestore();
    void ResetWarehouseLoadCapture();
    void ResetWarehouseWriteGuard();

    void SubscribeSaveComplete(Action handler);
    void SubscribeSaveStart(Action handler);
    void SubscribePreLoad(Action handler);
    void SubscribeLoadComplete(Action handler);
    void SubscribePreSceneChange(Action handler);
    void UnsubscribeSaveComplete(Action handler);
    void UnsubscribeSaveStart(Action handler);
    void UnsubscribePreLoad(Action handler);
    void UnsubscribeLoadComplete(Action handler);
    void UnsubscribePreSceneChange(Action handler);
}
