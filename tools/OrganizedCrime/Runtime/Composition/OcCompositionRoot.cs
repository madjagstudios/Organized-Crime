// OC-151: the composition root. Owns every service the mod shell used to hold as
// fields and builds them in construction order through the setup modules. The shell
// publishes the root before Build runs, so a throw mid-build leaves the earlier fields
// assigned, and teardown disposes exactly those. Lifecycle handlers and teardown
// null-check every property, as before.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal sealed class OcCompositionRoot
{
    public OcCompositionRoot(IOcEngineAdapters engine, Action<string> log, Action<string> warn, OcCompositionLifetime? lifetime = null)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Log = log ?? throw new ArgumentNullException(nameof(log));
        Warn = warn ?? throw new ArgumentNullException(nameof(warn));
        Lifetime = lifetime ?? new OcCompositionLifetime();
        Lifecycle = new LifecycleFanout(this);
        QaKeys = new OwnerQaKeys(this);
    }

    public IOcEngineAdapters Engine { get; }
    public Action<string> Log { get; }
    public Action<string> Warn { get; }
    public LifecycleFanout Lifecycle { get; }
    public OwnerQaKeys QaKeys { get; }
    public OcCompositionLifetime Lifetime { get; }

    public OrganizedCrimeTimingReceipts Timing { get; private set; } = OrganizedCrimeTimingReceipts.Disabled;
    public bool OwnerQaKeysEnabled { get; private set; }

    public SyndicateHqHostContextAdapter? HqContext { get; set; }
    public Release1StoryRuntimeService? Story { get; set; }
    public IChapter3DayClock? DayClock { get; set; }
    public Chapter3RuntimeService? Chapter3 { get; set; }
    public IChapter3ArthurWorld? Arthur { get; set; }
    public OcOfficerRosterService? OfficerRoster { get; set; }
    public IDisposable? OvernightLine { get; set; }
    public IRelease1SmallCourtesyWorld? CourtesyWorld { get; set; }
    public Release1ProductionComposition? Production { get; set; }
    public Chapter3PresentationProjector? Chapter3Projector { get; set; }
    public SyndicateHqNativeStorageRuntime? HqStorage { get; set; }
    public SyndicateHqRuntimeComposition? Hq { get; set; }
    public IFishWarehousePropertyHost? PropertyHost { get; set; }
    public FishWarehouseRuntimeService? WarehouseRuntime { get; set; }
    public FishWarehousePersistenceService? WarehousePersistence { get; set; }
    public FishWarehousePropertyCaptureService? WarehouseCapture { get; set; }
    public FishWarehousePersistenceReplayService? WarehouseReplay { get; set; }
    public NativeLawResponseModComposition? Law { get; set; }
    public NativeLawResponseController? LawController { get; set; }
    public LocalPressureRuntimeComposition? LocalPressure { get; set; }
    public PoliceCustodyEvidenceBridge? CustodyBridge { get; set; }
    public Release1ChiefComposition? Chief { get; set; }
    public Chapter3CheckpointService? Checkpoints { get; set; }
    internal Chapter3.Chapter3PatrolService? Patrols { get; set; }

    // OC-151 round 2: the static log sinks capture only the sink delegate. A lambda
    // inside Build would share its closure with Build locals (hqObserver) and retain
    // the root, engine, lifetime and HQ runtime through OrganizedCrimeLog after
    // teardown. The mod shell logging callbacks capture only the MelonLogger statics.
    private static Action<string> CreateWarningSink(Action<string> warn) =>
        message => warn($"[Organized Crime] {message}");

    private static Action<string> CreateReceiptSink(Action<string> log) =>
        message => log($"[Organized Crime] {message}");

    public void Build()
    {
        var engine = Engine;
        var log = Log;
        var warn = Warn;
        OrganizedCrimeLog.Warning = CreateWarningSink(warn);
        OrganizedCrimeLog.Receipt = CreateReceiptSink(log);
        OwnerQaKeysEnabled = engine.ReadOwnerQaKeys();
        OrganizedCrimeLog.Diagnostic("[Organized Crime] production runtime shell loaded.", Log);
        Timing = new OrganizedCrimeTimingReceipts(message => OrganizedCrimeLog.Diagnostic(message, Log), engine.ReadTimingThresholdMs());
        if (Timing.IsEnabled)
        {
            OrganizedCrimeLog.Diagnostic($"[Organized Crime] Timing receipts report any phase at or above {Timing.ThresholdMs} ms.", Log);
        }
        if (OwnerQaKeysEnabled)
        {
            try
            {
                engine.RegisterOwnerQaConsoleCommand();
            }
            catch
            {
                try
                {
                    OrganizedCrimeLog.Warning("Chapter 3 QA command could not be registered on this S1API version.");
                }
                catch
                {
                    // Owner QA registration and its warning sink must not abort Build.
                }
            }
            OrganizedCrimeLog.Diagnostic("[Organized Crime] Owner QA keys are enabled by preference; F8 recognition and fish warehouse developer keys are live.", Log);
        }
        var hqContext = new SyndicateHqHostContextAdapter();
        HqContext = hqContext;
        engine.CompositionTrace?.Add("step:Root.HqContext");

        var story = Release1Module.BuildStory(engine, hqContext);
        Story = story;

        var core = Chapter3Module.BuildCore(this, engine, hqContext, story, warn, log);
        var chapter3 = core.Service;

        var mode = Release1Module.BuildPresentationMode(engine);
        var release1Native = Release1Module.BuildRelease1Native(engine, mode, warn);

        var hqStorage = HqModule.BuildStorage(this, engine, warn);
        HqStorage = hqStorage;

        var world = Release1Module.BuildWorld(engine, hqContext, hqStorage);
        CourtesyWorld = world;

        SyndicateHqDoorObserver? hqObserver = null;
        var production = Release1Module.BuildProduction(this, engine, story, hqContext, world, mode, release1Native, () => hqObserver, warn);

        Chapter3Module.BuildWallet(engine, chapter3, world);
        Chapter3Projector = Chapter3Module.BuildProjector(this, engine, release1Native, chapter3, log);

        HqModule.ApplyStorageLoadPatch(engine, hqStorage, warn);
        var hq = HqModule.BuildRuntime(this, engine, hqContext, story, hqStorage, warn, o => hqObserver = o);
        Hq = hq.Composition;

        Chapter3Module.BuildPresence(engine, this, chapter3, hq.Runtime);
        Chapter3Module.BuildDemand(this, engine, chapter3, hqStorage, () => hqObserver, production.Banner, warn);
        chapter3.WireRaidWorld(new Chapter3.Il2CppChapter3RaidWorld(hqStorage, engine.IsHost, () =>
        {
            var fingerprint = hqObserver?.CurrentFingerprint;
            return fingerprint is null || !fingerprint.Position.IsFinite ? null :
                new Release1WorldPoint(fingerprint.Position.X, fingerprint.Position.Y, fingerprint.Position.Z);
        }, hq.Runtime)
        {
            RestorationReady = () => WarehousePersistence?.CurrentState is { } saved &&
                WarehouseReplay?.RequiresSnapshotProtection == false &&
                (!saved.Owned || PropertyHost?.CanRestoreDeliveries == true)
        });

        WarehouseModule.Build(this, engine, chapter3, log, warn);

        engine.SubscribeSaveComplete(Lifecycle.HandleSaveComplete);
        engine.SubscribeSaveStart(Lifecycle.HandleSaveStart);
        engine.SubscribePreLoad(Lifecycle.HandlePreLoad);
        engine.SubscribeLoadComplete(Lifecycle.HandleLoadComplete);
        engine.SubscribePreSceneChange(Lifecycle.HandlePreSceneChange);

        var lawComposition = LawModule.BuildLaw(this, engine, warn, log);

        var bridge = LawModule.BuildCustodyBridge(engine, LocalPressure!, warn);
        CustodyBridge = bridge;
        LawModule.ApplyCustodyPatch(engine, bridge, warn);

        var chief = Release1Module.BuildChief(this, engine, story, world, lawComposition, LocalPressure!, mode, warn);
        Chief = chief;

        var chiefService = chief.Service;
        Chapter3Module.BuildChiefLinks(engine, chapter3, chiefService);

        Chapter3Module.BuildCheckpoints(this, chapter3, chiefService, world, core.Roster, engine, warn, log);

        Release1Module.BuildProtectionGates(engine, world, chiefService, warn);
        Chapter3Module.ApplyCheckpointSwapPatch(engine, warn);
    }

    public void Teardown()
    {
        var engine = Engine;
        var trace = engine.CompositionTrace;
        engine.ResetLockdownGate();
        engine.ResetProtectionGate();
        HqStorage?.MarkTeardown();
        trace?.Add("teardown:Hq.MarkTeardown");
        if (Hq is not null && !Hq.TryDispose())
        {
            Warn("[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.");
        }
        trace?.Add("teardown:Hq.TryDispose");
        Hq = null;
        engine.ResetHqStorageLoad();
        HqStorage?.Dispose();
        trace?.Add("teardown:HqStorage.Dispose");
        HqStorage = null;
        Production?.Dispose();
        trace?.Add("teardown:Production.Dispose");
        Production = null;
        Chief?.Dispose();
        trace?.Add("teardown:Chief.Dispose");
        Chief = null;
        CourtesyWorld = null;
        Story?.Dispose();
        trace?.Add("teardown:Story.Dispose");
        Story = null;
        Chapter3Projector?.Dispose();
        trace?.Add("teardown:Projector.Dispose");
        Chapter3Projector = null;
        Patrols?.Dispose();
        if (Patrols is not null) OrganizedCrimeLog.Diagnostic("[Organized Crime] teardown:Patrols.Dispose", Log);
        Patrols = null;
        Checkpoints?.Dispose();
        trace?.Add("teardown:Checkpoints.Dispose");
        Checkpoints = null;
        OfficerRoster?.Dispose();
        trace?.Add("teardown:Roster.Dispose");
        OfficerRoster = null;
        if (Arthur is not null)
            engine.DisposeArthurRuntime(Arthur);
        Arthur = null;
        Chapter3QaAccess.Service = null;
        trace?.Add("teardown:QaAccess.Clear");
        Chapter3?.Dispose();
        trace?.Add("teardown:Chapter3.Dispose");
        Chapter3 = null;
        OvernightLine?.Dispose();
        trace?.Add("teardown:Overnight.Dispose");
        OvernightLine = null;
        if (DayClock is not null)
            engine.DisposeDayClock(DayClock);
        DayClock = null;
        HqContext = null;
        engine.ResetCustody();
        CustodyBridge?.Dispose();
        trace?.Add("teardown:Custody.Dispose");
        CustodyBridge = null;
        Law?.Dispose();
        trace?.Add("teardown:Law.Dispose");
        Law = null;
        LawController = null;
        LocalPressure = null;
        engine.ClearWarehouseManagementTarget();
        engine.ResetWarehouseDeliveryRestore();
        engine.ResetWarehouseLoadCapture();
        engine.ResetWarehouseWriteGuard();
        engine.UnsubscribeSaveComplete(Lifecycle.HandleSaveComplete);
        engine.UnsubscribeSaveStart(Lifecycle.HandleSaveStart);
        engine.UnsubscribePreLoad(Lifecycle.HandlePreLoad);
        engine.UnsubscribeLoadComplete(Lifecycle.HandleLoadComplete);
        engine.UnsubscribePreSceneChange(Lifecycle.HandlePreSceneChange);
        WarehouseReplay = null;
        WarehousePersistence = null;
        WarehouseCapture?.Reset();
        trace?.Add("teardown:Capture.Reset");
        WarehouseCapture = null;
        WarehouseRuntime = null;
        PropertyHost = null;
    }

    public void ApplyLoadedWarehouseState(FishWarehouseSaveState state) => WarehouseRuntime?.ApplyLoadedSaveState(state);

    public static ILocalPressureStateRepository? CreateLocalPressureRepository(string? activeSaveFolder)
    {
        if (!LocalPressureSavePath.TryCreate(activeSaveFolder, out var savePath, out _))
            return null;

        return new LocalPressureStateStoreRepository(new LocalPressureStateStore(savePath!));
    }

    public static IRelease1StoryRepository CreateRelease1StoryRepository(string activeSaveFolder)
    {
        if (!Release1StorySavePath.TryCreate(activeSaveFolder, out var savePath, out var result))
            throw new InvalidOperationException(result.Message);
        return new Release1StoryStateStoreRepository(new Release1StoryStateStore(savePath!));
    }

    public static IChapter3Repository CreateChapter3StateRepository(string activeSaveFolder)
    {
        if (!Chapter3SavePath.TryCreate(activeSaveFolder, out var savePath, out var result))
            throw new InvalidOperationException(result.Message);
        return new Chapter3StateStoreRepository(new Chapter3StateStore(savePath!));
    }
}
