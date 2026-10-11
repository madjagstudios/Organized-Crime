// OC-151: the recording engine seam behind the composition unit tests. Scalars are
// settable results with call recording; patch applies, resets and lifecycle
// subscriptions record their call order; CompositionTrace collects the setup/teardown
// step sequence the modules report. Factories return settable stub instances and
// record their call order; tests pre-configure the stubs they script.
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using UnityEngine;

namespace OrganizedCrime.Tests;

internal sealed class FakeOcEngineAdapters : IOcEngineAdapters
{
    public IList<string> Trace { get; } = new List<string>();
    public IList<string>? CompositionTrace => Trace;

    public bool OwnerQaKeysResult { get; set; }
    public int OwnerQaKeysReads { get; private set; }
    public int OwnerQaConsoleRegistrations { get; private set; }
    public int TimingThresholdResult { get; set; }
    public int TimingThresholdReads { get; private set; }
    public Release1PresentationMode PresentationModeResult { get; set; }
    public int PresentationModeReads { get; private set; }
    public Release1QuestPersistencePolicy QuestPolicyResult { get; set; }
    public int QuestPolicyReads { get; private set; }

    public bool IsHostResult { get; set; } = true;
    public int IsHostCalls { get; private set; }
    public Queue<float> DeltaTimeValues { get; } = new();
    public float DeltaTimeDefault { get; set; }
    public int DeltaTimeCalls { get; private set; }
    public string? ActiveSaveFolderResult { get; set; }
    public bool IsPlayerTypingResult { get; set; }
    public int IsPlayerTypingCalls { get; private set; }
    public bool SleepBuildPhaseResult { get; set; }
    public int SleepBuildPhaseReads { get; private set; }
    public bool FishWarehouseReadyResult { get; set; }
    public HashSet<KeyCode> PressedKeys { get; } = new();
    public List<KeyCode> IsKeyDownCalls { get; } = new();
    public bool IsLockdownActiveResult { get; set; }

    public HashSet<string> ThrowOn { get; } = new();

    public bool PatchApplyResult { get; set; } = true;
    public List<string> PatchCalls { get; } = new();
    public List<string> ResetCalls { get; } = new();
    public List<string> HqLoadCalls { get; } = new();
    public List<string> SubscribeCalls { get; } = new();
    public List<string> UnsubscribeCalls { get; } = new();
    public List<string> FactoryCalls { get; } = new();
    public List<string> DisposeCalls { get; } = new();

    public CompositionFakeClock DayClock { get; set; } = new();
    public CompositionFakeArthurWorld ArthurWorld { get; set; } = new();
    public CompositionFakeBeatProbeWorld BeatProbeWorld { get; set; } = new();
    public CompositionFakeBeatingWorld BeatingWorld { get; set; } = new();
    public CompositionFakeOfficerWorld OfficerWorld { get; set; } = new();
    public FakeDisposable OvernightLine { get; set; } = new();
    public BuildStubDemandWorld DemandWorld { get; set; } = new();
    public BuildStubVisitConditions VisitConditions { get; set; } = new();
    public BuildStubArthurPhone ArthurPhone { get; set; } = new();
    public CompositionFakeCheckpointWorld CheckpointWorld { get; set; } = new();
    public BuildStubNative Release1Native { get; set; } = new();
    public BuildStubNative ChiefNative { get; set; } = new();
    public BuildStubCourtesyWorld CourtesyWorld { get; set; } = new();
    public BuildStubPhoneQueue PhoneQueue { get; set; } = new();
    public BuildStubCartelSource CartelSource { get; set; } = new();
    public BuildStubProtectionCheckpointWorld ProtectionCheckpointWorld { get; set; } = new();
    public BuildStubExposureReader ExposureReader { get; set; } = new();
    public BuildStubCurfewReader CurfewReader { get; set; } = new();
    public BuildStubWantedWriter WantedWriter { get; set; } = new();
    public BuildStubPursuitHold PursuitHold { get; set; } = new();
    public BuildStubPropertyHost PropertyHost { get; set; } = new();
    public BuildStubLawAdapter LawAdapter { get; set; } = new();
    public Func<bool>? ProtectionIsCovered { get; private set; }
    public Func<bool>? ProtectionIsHost { get; private set; }
    public Func<int?>? ProtectionToday { get; private set; }

    public bool ReadOwnerQaKeys()
    {
        ThrowIfArmed("read:OwnerQaKeys");
        OwnerQaKeysReads++;
        Trace.Add("read:OwnerQaKeys");
        return OwnerQaKeysResult;
    }

    public void RegisterOwnerQaConsoleCommand()
    {
        OwnerQaConsoleRegistrations++;
        Trace.Add("register:OwnerQaConsoleCommand");
        ThrowIfArmed("register:OwnerQaConsoleCommand");
    }

    public int ReadTimingThresholdMs()
    {
        ThrowIfArmed("read:TimingThreshold");
        TimingThresholdReads++;
        Trace.Add("read:TimingThreshold");
        return TimingThresholdResult;
    }

    public Release1PresentationMode ReadPresentationMode()
    {
        ThrowIfArmed("read:PresentationMode");
        PresentationModeReads++;
        Trace.Add("read:PresentationMode");
        return PresentationModeResult;
    }

    public Release1QuestPersistencePolicy ReadQuestPolicy()
    {
        ThrowIfArmed("read:QuestPolicy");
        QuestPolicyReads++;
        Trace.Add("read:QuestPolicy");
        return QuestPolicyResult;
    }

    public bool IsHost()
    {
        IsHostCalls++;
        return IsHostResult;
    }

    public float DeltaTime()
    {
        DeltaTimeCalls++;
        return DeltaTimeValues.Count > 0 ? DeltaTimeValues.Dequeue() : DeltaTimeDefault;
    }

    public string? ActiveSaveFolder() => ActiveSaveFolderResult;

    public bool IsPlayerTyping()
    {
        IsPlayerTypingCalls++;
        return IsPlayerTypingResult;
    }

    public bool ReadSleepBuildPhase()
    {
        SleepBuildPhaseReads++;
        return SleepBuildPhaseResult;
    }

    public bool IsFishWarehouseReady() => FishWarehouseReadyResult;

    public bool IsKeyDown(KeyCode key)
    {
        IsKeyDownCalls.Add(key);
        return PressedKeys.Contains(key);
    }

    public bool IsLockdownActive() => IsLockdownActiveResult;

    private void ThrowIfArmed(string name)
    {
        if (ThrowOn.Contains(name))
            throw new InvalidOperationException($"Fake throw on {name}");
    }

    private void Patch(string name)
    {
        ThrowIfArmed(name);
        PatchCalls.Add(name);
        Trace.Add(name);
    }

    private void Reset(string name)
    {
        ThrowIfArmed(name);
        ResetCalls.Add(name);
        Trace.Add(name);
    }

    private void Hq(string name)
    {
        ThrowIfArmed(name);
        HqLoadCalls.Add(name);
        Trace.Add(name);
    }

    private void Subscribe(string name)
    {
        ThrowIfArmed(name);
        SubscribeCalls.Add(name);
        Trace.Add(name);
    }

    private void Unsubscribe(string name)
    {
        ThrowIfArmed(name);
        UnsubscribeCalls.Add(name);
        Trace.Add(name);
    }

    private void Disposed(string name)
    {
        ThrowIfArmed(name);
        DisposeCalls.Add(name);
        Trace.Add(name);
    }

    private T Made<T>(string name, T instance)
    {
        ThrowIfArmed(name);
        FactoryCalls.Add(name);
        Trace.Add(name);
        return instance;
    }

    public IChapter3DayClock CreateDayClock(Action<string>? warn) => Made("factory:DayClock", DayClock);
    public void DisposeDayClock(IChapter3DayClock clock) => Disposed("dispose:DayClock");
    public IChapter3ArthurWorld CreateArthurRuntime(Action<string> warn, Func<bool> hasAuthority) => Made("factory:Arthur", ArthurWorld);
    public void DisposeArthurRuntime(IChapter3ArthurWorld world) => Disposed("dispose:Arthur");
    public IChapter3BeatProbeWorld CreateBeatProbeWorld(Action<string> warn, Func<bool> isHost) => Made("factory:BeatProbe", BeatProbeWorld);
    public IChapter3BeatingWorld CreateBeatingWorld(Action<string> warn, Func<bool> isHost) => Made("factory:Beating", BeatingWorld);
    public IOcOfficerWorld CreateOfficerWorld(Action<string>? warn) => Made("factory:OfficerWorld", OfficerWorld);
    public IDisposable CreateOvernightLine(Chapter3RuntimeService service, Action<string>? log) => Made("factory:Overnight", OvernightLine);
    public IChapter3ProductionDemandWorld CreateDemandWorld(ISyndicateHqStorageRuntime? holdRoomStorage, Func<Release1WorldPoint?>? hqDoor, Func<bool>? isHost, Action<string>? log) => Made("factory:Demand", DemandWorld);
    public IChapter3ArthurVisitConditions CreateVisitConditions(Action<string> warn) => Made("factory:VisitConditions", VisitConditions);
    public IChapter3ArthurPhone CreateArthurPhone(IRelease1PayphoneCue? cue, Func<bool> isAuthoritativeHost, Action<string> warn) => Made("factory:ArthurPhone", ArthurPhone);
    public IChapter3CheckpointWorld CreateCheckpointWorld(Action<string>? warn, Action<string>? log) => Made("factory:CheckpointWorld", CheckpointWorld);

    public IRelease1NativePresentation CreateRelease1Native(Release1QuestPersistencePolicy policy, Action<string>? warn) => Made("factory:Release1Native", Release1Native);
    public IRelease1NativePresentation CreateChiefNative(Release1QuestPersistencePolicy policy, Action<string>? warn) => Made("factory:ChiefNative", ChiefNative);
    public IRelease1SmallCourtesyWorld CreateSmallCourtesyWorld(IRelease1StoryHostContext hostContext, ISyndicateHqStorageRuntime? holdRoomStorage) => Made("factory:CourtesyWorld", CourtesyWorld);
    public IRelease1PhoneCallQueue CreatePhoneCallQueue() => Made("factory:PhoneQueue", PhoneQueue);
    public IRelease1CartelStatusSource CreateCartelStatusSource() => Made("factory:CartelSource", CartelSource);
    public IRelease1ProtectionCheckpointWorld CreateProtectionCheckpointWorld(Action<string>? log) => Made("factory:ProtectionCheckpointWorld", ProtectionCheckpointWorld);
    public IChapter3PlayerExposureReader CreateExposureReader(Action<string> warn) => Made("factory:ExposureReader", ExposureReader);
    public IChapter3CurfewReader CreateCurfewReader(Action<string> warn) => Made("factory:CurfewReader", CurfewReader);
    public IChapter3WantedWriter CreateWantedWriter(Action<string> warn) => Made("factory:WantedWriter", WantedWriter);
    public IChapter3PursuitHold CreatePursuitHold(Action<string> warn) => Made("factory:PursuitHold", PursuitHold);

    public IFishWarehousePropertyHost CreatePropertyHost() => Made("factory:PropertyHost", PropertyHost);
    public ILocalPressureRuntimeHostAdapter CreateLawHostAdapter(Action<string>? log) => Made("factory:LawAdapter", LawAdapter);

    public bool ApplyArthurResponse(Action<string>? log)
    {
        Patch("apply:ArthurResponse");
        return PatchApplyResult;
    }

    public bool ApplyHqStorageLoad(ISyndicateHqStorageRuntime runtime, SyndicateHqStorageLoadContextAdapter context, Action<string>? log, Action<string>? receiptLog)
    {
        Patch("apply:HqStorageLoad");
        return PatchApplyResult;
    }

    public void ApplyWarehouseCanvas() => Patch("apply:WarehouseCanvas");
    public void ApplyWarehouseGrid() => Patch("apply:WarehouseGrid");
    public void ApplyWarehouseDock() => Patch("apply:WarehouseDock");

    public void ApplyWarehouseDeliveryRestore(Func<bool> isReady, Action<string>? log) =>
        Patch("apply:WarehouseDeliveryRestore");

    public void ApplyWarehouseLoadCapture(Func<bool> isAuthoritativeHost, Func<string?> activeSaveFolder, FishWarehousePropertyCaptureService captureService, Action<string>? log) =>
        Patch("apply:WarehouseLoadCapture");

    public void ApplyWarehouseWriteGuard(Func<bool> isAuthoritativeHost, Func<bool> isDegraded) =>
        Patch("apply:WarehouseWriteGuard");

    public void ApplyCustody(PoliceCustodyEvidenceBridge bridge, Action<string>? log) =>
        Patch("apply:Custody");

    public bool ApplyProtectionGate(Action<string>? log)
    {
        Patch("apply:ProtectionGate");
        return PatchApplyResult;
    }

    public bool ApplyCheckpointSwap(Action<string>? log)
    {
        Patch("apply:CheckpointSwap");
        return PatchApplyResult;
    }

    public void BeginHqLoadCycle() => Hq("hq:BeginLoadCycle");
    public void CompleteHqLoad() => Hq("hq:CompleteHqLoad");

    public void ConfigureProtectionGate(Func<bool>? isCovered, Func<bool>? isHost, Func<int?>? today)
    {
        ProtectionIsCovered = isCovered;
        ProtectionIsHost = isHost;
        ProtectionToday = today;
        Patch("configure:ProtectionGate");
    }

    public void ResetLockdownGate() => Reset("reset:LockdownGate");
    public void ResetProtectionGate() => Reset("reset:ProtectionGate");
    public void ResetHqStorageLoad() => Reset("reset:HqStorageLoad");
    public void ResetCustody() => Reset("reset:Custody");
    public void ClearWarehouseManagementTarget() => Reset("reset:WarehouseManagementTarget");
    public void ResetWarehouseDeliveryRestore() => Reset("reset:WarehouseDeliveryRestore");
    public void ResetWarehouseLoadCapture() => Reset("reset:WarehouseLoadCapture");
    public void ResetWarehouseWriteGuard() => Reset("reset:WarehouseWriteGuard");

    public void SubscribeSaveComplete(Action handler) => Subscribe("subscribe:SaveComplete");
    public void SubscribeSaveStart(Action handler) => Subscribe("subscribe:SaveStart");
    public void SubscribePreLoad(Action handler) => Subscribe("subscribe:PreLoad");
    public void SubscribeLoadComplete(Action handler) => Subscribe("subscribe:LoadComplete");
    public void SubscribePreSceneChange(Action handler) => Subscribe("subscribe:PreSceneChange");
    public void UnsubscribeSaveComplete(Action handler) => Unsubscribe("unsubscribe:SaveComplete");
    public void UnsubscribeSaveStart(Action handler) => Unsubscribe("unsubscribe:SaveStart");
    public void UnsubscribePreLoad(Action handler) => Unsubscribe("unsubscribe:PreLoad");
    public void UnsubscribeLoadComplete(Action handler) => Unsubscribe("unsubscribe:LoadComplete");
    public void UnsubscribePreSceneChange(Action handler) => Unsubscribe("unsubscribe:PreSceneChange");
}
