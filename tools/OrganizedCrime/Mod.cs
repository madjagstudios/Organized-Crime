using MelonLoader;
using MelonLoader.Utils;
using Il2CppFishNet;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tiles;
using Il2CppScheduleOne.Vehicles;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using S1API.Entities;
using S1API.Lifecycle;
using UnityEngine;
using UnityEngine.AI;

[assembly: MelonInfo(
    typeof(OrganizedCrime.OrganizedCrimeMod),
    "Organized Crime",
    "1.0.1",
    "MadJag Studios")]

namespace OrganizedCrime;

public sealed class OrganizedCrimeMod : MelonMod
{
    private FishWarehouseRuntimeService? _fishWarehouseRuntimeService;
    private RuntimePropertyHost? _runtimePropertyHost;
    private FishWarehousePersistenceService? _fishWarehousePersistenceService;
    private FishWarehousePropertyCaptureService? _fishWarehousePropertyCaptureService;
    private FishWarehousePersistenceReplayService? _fishWarehousePersistenceReplayService;
    private LocalPressureRuntimeComposition? _localPressureRuntime;
    private NativeLawResponseModComposition? _nativeLawResponseComposition;
    private NativeLawResponseController? _nativeLawResponseController;
    private PoliceCustodyEvidenceBridge? _policeCustodyEvidenceBridge;
    private SyndicateHqHostContextAdapter? _syndicateHqHostContext;
    private Release1StoryRuntimeService? _release1StoryRuntime;
    private Release1ProductionComposition? _release1ProductionComposition;
    private Release1ChiefComposition? _release1ChiefComposition;
    private SyndicateHqRuntimeComposition? _syndicateHqComposition;
    private SyndicateHqNativeStorageRuntime? _syndicateHqStorageRuntime;
#if OC_OWNER_SPIKES
    private S1ApiRelease1FieldContactRuntime? _release1FieldContactRuntime;
#endif
    private IRelease1SmallCourtesyWorld? _release1SmallCourtesyWorld;
    private bool _initialized;
    private bool _ownerQaKeysEnabled;
    private bool _theEnvelopeCashGateDumpNext = true;
    private bool _theEnvelopeClosetCashGateDumpNext = true;
    private bool _keepTheLightsOffProductionGateDumpNext = true;
#if OC_OWNER_SPIKES
    // OC-69 parked lifecycle, 2026-09-05 (a seventh spec review round): true only after a park mutation
    // has actually succeeded, either the load reconcile parking a freshly resolved contact or F4 parking
    // one the owner had unparked. F4's own toggle trusts this flag the same way the pre-parked-lifecycle
    // toggle trusted its own spawn flag: true unparks straight away with no read, false reads presence
    // first to decide between parking and reporting that no contact exists yet.
    private bool _arthurFieldContactParked;
    private readonly Release1ArthurLoadReconcile _arthurLoadReconcile = new(() => Time.realtimeSinceStartup);
    // OC-69 live defect fix, 2026-09-05: a fresh provoke, and an F4 press that cannot yet tell whether
    // the load constructed contact is present, share the same several-second not-ready read window the
    // load reconcile above already retries around. Each of these two presses owns its own pump rather
    // than sharing one, so a provoke's pending run and a decide-before-toggling retry can never cancel
    // one another out.
    private readonly Release1ArthurFieldContactReadyPump _arthurProvokeFollowUp = new(() => Time.realtimeSinceStartup);
    private readonly Release1ArthurFieldContactReadyPump _arthurToggleDecide = new(() => Time.realtimeSinceStartup);
#endif
    private OrganizedCrimeTimingReceipts _timing = OrganizedCrimeTimingReceipts.Disabled;

    public override void OnInitializeMelon()
    {
        if (_initialized)
        {
            MelonLogger.Warning("[Organized Crime] duplicate initialization was ignored.");
            return;
        }
        _initialized = true;
        OrganizedCrimeLog.Warning = message => MelonLogger.Warning($"[Organized Crime] {message}");
        OrganizedCrimeLog.Receipt = message => MelonLogger.Msg($"[Organized Crime] {message}");
        MelonLogger.Msg("[Organized Crime] production runtime shell loaded.");
        _ownerQaKeysEnabled = OwnerQaKeysPreference.Read();
        _timing = new OrganizedCrimeTimingReceipts(MelonLogger.Msg, TimingReceiptThresholdPreference.Read());
        if (_timing.IsEnabled)
        {
            MelonLogger.Msg($"[Organized Crime] Timing receipts report any phase at or above {_timing.ThresholdMs} ms.");
        }
        if (_ownerQaKeysEnabled)
        {
            MelonLogger.Msg("[Organized Crime] Owner QA keys are enabled by preference; F8 recognition and fish warehouse developer keys are live.");
        }
        var hqContext = new SyndicateHqHostContextAdapter();
        _syndicateHqHostContext = hqContext;
        _release1StoryRuntime = new Release1StoryRuntimeService(hqContext, CreateRelease1StoryRepository);
        var release1PresentationMode = Release1PresentationModePreference.Read();
        var release1Native = release1PresentationMode == Release1PresentationMode.Native
            ? new S1ApiRelease1NativePresentation(
                Release1QuestPersistencePolicyPreference.Read(),
                message => MelonLogger.Warning($"[Organized Crime] {message}"))
            : null;
        var hqStorageBoundary = new SyndicateHqNativeStorageBoundary { Timing = _timing };
        var hqStorageRuntime = new SyndicateHqNativeStorageRuntime(
            hqStorageBoundary,
            message => MelonLogger.Warning($"[Organized Crime] {message}"),
            receiptLog: OrganizedCrimeLog.Receipt);
        _syndicateHqStorageRuntime = hqStorageRuntime;
#if OC_OWNER_SPIKES
        var fieldContactRuntime = new S1ApiRelease1FieldContactRuntime(
            message => MelonLogger.Warning($"[Organized Crime] {message}"));
        _release1FieldContactRuntime = fieldContactRuntime;
        var release1SmallCourtesyWorld = new S1ApiRelease1SmallCourtesyWorld(hqContext, hqStorageRuntime, fieldContactRuntime, HarmonyInstance);
#else
        var release1SmallCourtesyWorld = new S1ApiRelease1SmallCourtesyWorld(hqContext, hqStorageRuntime, lockdownHarmony: HarmonyInstance);
#endif
        _release1SmallCourtesyWorld = release1SmallCourtesyWorld;
        SyndicateHqDoorObserver? hqObserver = null;
        _release1ProductionComposition = new Release1ProductionComposition(
            _release1StoryRuntime,
            new Release1PostBenziesUnlockReader(hqContext, new S1CartelStatusSource()),
            new S1ApiRelease1PhoneCallQueue(),
            release1SmallCourtesyWorld,
            message => MelonLogger.Warning($"[Organized Crime] {message}"),
            mode: release1PresentationMode,
            native: release1Native,
            holdRoomMarker: () =>
            {
                var fingerprint = hqObserver?.CurrentFingerprint;
                if (fingerprint is null || !fingerprint.Position.IsFinite) return null;
                return new Release1WorldPoint(fingerprint.Position.X, fingerprint.Position.Y, fingerprint.Position.Z);
            });
        _release1StoryRuntime.Timing = _timing;
        _release1ProductionComposition.Timing = _timing;
        _release1ProductionComposition.KeepTheLightsOff.Service.Timing = _timing;
        _release1ProductionComposition.TheEnvelope.Service.Timing = _timing;
        if (!SyndicateHqStorageLoadPatch.Apply(
                HarmonyInstance,
                hqStorageRuntime,
                new SyndicateHqStorageLoadContextAdapter(),
                message => MelonLogger.Warning($"[Organized Crime] {message}"),
                receiptLog: OrganizedCrimeLog.Receipt))
        {
            MelonLogger.Warning("[Organized Crime] Native Syndicate HQ storage will remain inert because its exact vanilla load prefix was unavailable.");
        }
        SyndicateHqRuntimeService? hqRuntime = null;
        hqObserver = new SyndicateHqDoorObserver(
            (fingerprint, generation) => hqRuntime?.HandleDoorInteraction(fingerprint, generation),
            message => MelonLogger.Warning($"[Organized Crime] {message}"))
        {
            Timing = _timing
        };
        var hqInterior = new SyndicateHqInteriorRuntime(
            message => MelonLogger.Warning($"[Organized Crime] {message}"),
            hqStorageRuntime)
        {
            Timing = _timing
        };
        hqRuntime = new SyndicateHqRuntimeService(
            hqContext,
            new Release1StorySyndicateHqUnlockAuthority(hqContext, _release1StoryRuntime),
            hqObserver,
            hqInterior,
            new SyndicateHqNativePlayerTransport(message => MelonLogger.Warning($"[Organized Crime] {message}")),
            message => MelonLogger.Warning($"[Organized Crime] {message}"));
        _syndicateHqComposition = new SyndicateHqRuntimeComposition(hqRuntime, () => _syndicateHqHostContext?.BeginLoad());
        FishWarehouseManagementCanvasPatch.Apply(HarmonyInstance);
        FishWarehouseNativeGridPlacementPatches.Apply(HarmonyInstance);
        FishWarehouseDockOccupantRefreshPatch.Apply(HarmonyInstance);
        var propertyHost = new RuntimePropertyHost();
        _runtimePropertyHost = propertyHost;
        FishWarehouseDeliveryRestorePatch.Apply(
            HarmonyInstance,
            () => propertyHost.CanRestoreDeliveries,
            message => MelonLogger.Msg($"[Organized Crime] {message}"));
        _fishWarehouseRuntimeService = new FishWarehouseRuntimeService(
            propertyHost,
            RuntimePropertyReadiness.IsFishWarehouseReady,
            propertyHost.TryPlaceEmployeeHome,
            message => MelonLogger.Msg($"[Organized Crime] {message}"));
        var snapshotStore = new FishWarehousePropertySnapshotStore();
        FishWarehousePropertyCaptureService? captureService = null;
        var replayLifecycle = new FishWarehouseReplayLifecycle(
            checkpoint => captureService?.GetRecoveryPrerequisites(checkpoint) ??
                new FishWarehouseCaptureRecoveryPrerequisites(
                    ByteProtectionSucceeded: false,
                    InspectionSucceeded: false,
                    TypedPayloadRetentionSucceeded: false,
                    CheckpointPersistenceSucceeded: false));
        _fishWarehousePersistenceService = new FishWarehousePersistenceService(
            new FishWarehouseSaveStore(),
            GetActiveSaveFolder,
            () => _fishWarehouseRuntimeService?.CaptureSaveState() ?? FishWarehouseSaveState.UnownedState(),
            ApplyLoadedSaveState,
            message => MelonLogger.Msg($"[Organized Crime] {message}"),
            snapshotStore,
            replayLifecycle);
        captureService = new FishWarehousePropertyCaptureService(
            snapshotStore,
            new FishWarehouseNativePropertySnapshotInspector(),
            _fishWarehousePersistenceService,
            replayLifecycle,
            message => MelonLogger.Msg($"[Organized Crime] {message}"));
        _fishWarehousePropertyCaptureService = captureService;
        var runtimeService = _fishWarehouseRuntimeService;
        var persistenceService = _fishWarehousePersistenceService;
        var replayOperations = new FishWarehousePersistenceReplayOperations(
            propertyHost,
            runtimeService,
            persistenceService,
            captureService,
            message => MelonLogger.Msg($"[Organized Crime] {message}"));
        _fishWarehousePersistenceReplayService = new FishWarehousePersistenceReplayService(
            replayLifecycle,
            GetActiveSaveFolder,
            captureService.TryRecoverAtLoadComplete,
            persistenceService.HandleLoadComplete,
            () => persistenceService.CurrentState,
            replayOperations,
            persistenceService.TryPersistReplayCheckpoint,
            () =>
            {
                captureService.ResetRuntimeReferences();
                replayOperations.ResetRuntimeReferences();
                persistenceService.PrimeStateForLoad();
            },
            FishWarehouseDeliveryRestorePatch.Reset,
            () =>
            {
                if (!runtimeService.PrepareForLoad())
                    MelonLogger.Warning("[Organized Crime] Fish Warehouse runtime Property could not be fully prepared for the next save load.");
            },
            log: message => MelonLogger.Msg($"[Organized Crime] {message}"));
        FishWarehousePropertyLoadCapturePatch.Apply(
            HarmonyInstance,
            IsAuthoritativeHost,
            GetActiveSaveFolder,
            captureService,
            message => MelonLogger.Msg($"[Organized Crime] {message}"));
        FishWarehouseNativePropertyWriteGuardPatch.Apply(
            HarmonyInstance,
            IsAuthoritativeHost,
            () => captureService.IsDegraded);
        GameLifecycle.OnSaveComplete += HandleSaveComplete;
        GameLifecycle.OnSaveStart += HandleSaveStart;
        GameLifecycle.OnPreLoad += HandlePreLoad;
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;

        _nativeLawResponseComposition = new NativeLawResponseModComposition(
            CreateLocalPressureRepository,
            message => MelonLogger.Warning($"[Organized Crime] {message}"),
            message => MelonLogger.Msg($"[Organized Crime] {message}"),
            receiptLog: OrganizedCrimeLog.Receipt);
        _nativeLawResponseController = _nativeLawResponseComposition.Controller;
        _localPressureRuntime = _nativeLawResponseComposition.LocalPressureRuntime;
        _policeCustodyEvidenceBridge = new PoliceCustodyEvidenceBridge(
            new CustodyEntryGate(),
            _localPressureRuntime.Service,
            message => MelonLogger.Warning($"[Organized Crime] {message}"));
        PoliceCustodyPatch.Apply(
            HarmonyInstance,
            _policeCustodyEvidenceBridge,
            message => MelonLogger.Warning($"[Organized Crime] {message}"));

        // OC-73. The Chief needs the Local Pressure runtime, which does not exist until
        // _nativeLawResponseComposition is built above, so he is composed here, as a sibling of
        // _release1ProductionComposition rather than a child of it, which also keeps that file free
        // of the word Chief (see Release1ProductionCompositionTests).
        var chiefNative = release1PresentationMode == Release1PresentationMode.Native
            ? new S1ApiRelease1NativePresentation(
                Release1QuestPersistencePolicyPreference.Read(),
                Release1NativePresentationSupport.ChiefNpcId,
                () => new Release1ChiefCampbellNpc(),
                npc => npc is Release1ChiefCampbellNpc,
                message => MelonLogger.Warning($"[Organized Crime] {message}"))
            : null;
        var localPressureService = _localPressureRuntime.Service;
        _release1ChiefComposition = new Release1ChiefComposition(
            _release1StoryRuntime,
            release1SmallCourtesyWorld,
            _nativeLawResponseComposition.ChiefObserver,
            playerId => localPressureService.TryGetState(playerId, out var state) ? state : null,
            playerId => localPressureService.TryApplyRecordWipe(playerId, localPressureService.SessionEpoch, localPressureService.LoadEpoch),
            (out Guid sessionEpoch, out long loadEpoch) =>
            {
                sessionEpoch = localPressureService.SessionEpoch;
                loadEpoch = localPressureService.LoadEpoch;
                return localPressureService.Phase == LocalPressureRuntimePhase.Active;
            },
            release1PresentationMode,
            chiefNative,
            message => MelonLogger.Warning($"[Organized Crime] {message}"));
    }

    public override void OnDeinitializeMelon()
    {
        Release1LockdownGatePatch.Reset();
        _syndicateHqStorageRuntime?.MarkTeardown();
        if (_syndicateHqComposition is not null && !_syndicateHqComposition.TryDispose())
        {
            MelonLogger.Warning("[Organized Crime] Syndicate HQ deinitialization remains pending until exact listener cleanup succeeds.");
        }
        _syndicateHqComposition = null;
        SyndicateHqStorageLoadPatch.Reset();
        _syndicateHqStorageRuntime?.Dispose();
        _syndicateHqStorageRuntime = null;
#if OC_OWNER_SPIKES
        _release1FieldContactRuntime?.Dispose();
        _release1FieldContactRuntime = null;
#endif
        _release1ProductionComposition?.Dispose();
        _release1ProductionComposition = null;
        _release1ChiefComposition?.Dispose();
        _release1ChiefComposition = null;
        _release1SmallCourtesyWorld = null;
        _release1StoryRuntime?.Dispose();
        _release1StoryRuntime = null;
        _syndicateHqHostContext = null;
        PoliceCustodyPatch.Reset();
        _policeCustodyEvidenceBridge?.Dispose();
        _policeCustodyEvidenceBridge = null;
        _nativeLawResponseComposition?.Dispose();
        _nativeLawResponseComposition = null;
        _nativeLawResponseController = null;
        _localPressureRuntime = null;
        FishWarehouseManagementCanvasPatch.SetTargetProperty(null);
        FishWarehouseDeliveryRestorePatch.Reset();
        FishWarehousePropertyLoadCapturePatch.Reset();
        FishWarehouseNativePropertyWriteGuardPatch.Reset();
        GameLifecycle.OnSaveComplete -= HandleSaveComplete;
        GameLifecycle.OnSaveStart -= HandleSaveStart;
        GameLifecycle.OnPreLoad -= HandlePreLoad;
        GameLifecycle.OnLoadComplete -= HandleLoadComplete;
        GameLifecycle.OnPreSceneChange -= HandlePreSceneChange;
        _fishWarehousePersistenceReplayService = null;
        _fishWarehousePersistenceService = null;
        _fishWarehousePropertyCaptureService?.Reset();
        _fishWarehousePropertyCaptureService = null;
        _fishWarehouseRuntimeService = null;
        _runtimePropertyHost = null;
        _initialized = false;
    }

    public override void OnUpdate()
    {
        _syndicateHqComposition?.Update();
        _release1ProductionComposition?.Update();
        _release1ChiefComposition?.Update();
        _localPressureRuntime?.Service.PumpReadiness();
        if (IsAuthoritativeHost())
        {
            _fishWarehouseRuntimeService?.Update();
            _fishWarehousePersistenceReplayService?.Update();
        }
        _runtimePropertyHost?.EnsureManagementWorldspaceUiContainerForOpenCanvas();
#if OC_OWNER_SPIKES
        if (_release1SmallCourtesyWorld is not null)
        {
            var reconcileStep = _arthurLoadReconcile.Pump(_release1SmallCourtesyWorld);
            if (reconcileStep is not null)
            {
                switch (reconcileStep.Outcome)
                {
                    case Release1ArthurLoadReconcileOutcome.Parked when reconcileStep.ParkResult is not null:
                        _arthurFieldContactParked = true;
                        MelonLogger.Msg("[Organized Crime] OC-69 parked the load constructed field contact");
                        foreach (var line in reconcileStep.ParkResult.Lines)
                            MelonLogger.Msg($"[Organized Crime] OC-69 field contact load reconcile: {line}");
                        break;
                    case Release1ArthurLoadReconcileOutcome.ReadFailed:
                        MelonLogger.Warning($"[Organized Crime] OC-69 field contact read failed: {reconcileStep.Detail}");
                        break;
                    default:
                        MelonLogger.Warning($"[Organized Crime] OC-69 field contact load reconcile gave up after {Release1ArthurLoadReconcile.MaxSeconds} seconds without a ready read; a load constructed contact may still be present and unparked.");
                        break;
                }
            }

            LogReadyPumpStep("field contact provoke", _arthurProvokeFollowUp.Pump(_release1SmallCourtesyWorld));
            LogReadyPumpStep("field contact proof", _arthurToggleDecide.Pump(_release1SmallCourtesyWorld));
        }
#endif
        if (_ownerQaKeysEnabled)
        {
            if (Input.GetKeyDown(KeyCode.Backslash))
                _fishWarehouseRuntimeService?.ToggleDeveloperLifecycle();
            if (Input.GetKeyDown(KeyCode.O))
                _fishWarehouseRuntimeService?.TryUnlockDeveloperLifecycle();
            if (Input.GetKeyDown(KeyCode.P))
                _fishWarehouseRuntimeService?.TryPlaceEmployeeHome();
            if (Input.GetKeyDown(KeyCode.F8) && _syndicateHqHostContext is not null && _release1StoryRuntime is not null)
            {
                var result = Release1StoryOwnerQaRecognitionHarness.TryRecognize(_syndicateHqHostContext, _release1StoryRuntime);
                MelonLogger.Msg($"[Organized Crime] OC-48 owner QA recognition: {result.Status}: {result.Message}");
            }
            if (Input.GetKeyDown(KeyCode.F9) && _release1SmallCourtesyWorld is not null)
            {
                var result = Release1WrongAddressStagingHarness.TryStage(_release1SmallCourtesyWorld);
                MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: stage status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: {line}");
            }
            if (Input.GetKeyDown(KeyCode.F10) && _release1SmallCourtesyWorld is not null)
            {
                var result = Release1WrongAddressStagingHarness.TryDump(_release1SmallCourtesyWorld);
                MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: dump status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: {line}");

                try
                {
                    var storageEntities = WorldStorageEntity.All;
                    var storageCount = storageEntities == null ? 0 : storageEntities.Count;
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: world storage entities: {storageCount}");
                    if (storageEntities != null)
                    {
                        foreach (var entity in storageEntities)
                        {
                            if (entity == null) continue;
                            var slots = entity.ItemSlots;
                            var slotCount = slots == null ? 0 : slots.Count;
                            var occupied = 0;
                            if (slots != null)
                            {
                                foreach (var slot in slots)
                                {
                                    if (slot != null && slot.Quantity > 0) occupied++;
                                }
                            }
                            MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: entity {entity.GUID} name {entity.gameObject.name} changed {entity.HasChanged} slots {slotCount} occupied {occupied}");
                            if (slots != null)
                            {
                                for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                                {
                                    var slot = slots[slotIndex];
                                    if (slot == null || slot.Quantity <= 0) continue;
                                    var item = slot.ItemInstance;
                                    var itemId = item == null ? "null" : item.ID;
                                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: entity {entity.GUID} slot {slotIndex} id {itemId} quantity {slot.Quantity}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: world storage dump failed: {ex.Message}");
                }

                try
                {
                    var deadDrops = DeadDrop.DeadDrops;
                    var dropCount = deadDrops == null ? 0 : deadDrops.Count;
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: dead drops: {dropCount}");
                    if (deadDrops != null)
                    {
                        foreach (var drop in deadDrops)
                        {
                            if (drop == null) continue;
                            var storage = drop.Storage;
                            if (storage == null)
                            {
                                MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: drop {drop.DeadDropName} guid {drop.GUID} storage null");
                                continue;
                            }
                            MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: drop {drop.DeadDropName} guid {drop.GUID} storage {storage.GUID} storageName {storage.gameObject.name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Organized Crime] OC-52 staging proof: dead drop dump failed: {ex.Message}");
                }
            }
            if (Input.GetKeyDown(KeyCode.F11) && _release1SmallCourtesyWorld is not null)
            {
                var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(_release1SmallCourtesyWorld);
                MelonLogger.Msg($"[Organized Crime] OC-57 hold room proof: dump status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-57 hold room proof: {line}");
            }
            if (Input.GetKeyDown(KeyCode.F6) && _release1SmallCourtesyWorld is not null)
            {
                var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(_release1SmallCourtesyWorld);
                MelonLogger.Msg($"[Organized Crime] OC-58 quantity proof: status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-58 quantity proof: {line}");
            }
            if (Input.GetKeyDown(KeyCode.F1) && _release1SmallCourtesyWorld is not null)
            {
                var result = _theEnvelopeCashGateDumpNext
                    ? Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(_release1SmallCourtesyWorld)
                    : Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(_release1SmallCourtesyWorld);
                var mode = _theEnvelopeCashGateDumpNext ? "dump" : "decrement";
                MelonLogger.Msg($"[Organized Crime] OC-60 cash gate proof ({mode}): status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-60 cash gate proof: {line}");
                _theEnvelopeCashGateDumpNext = !_theEnvelopeCashGateDumpNext;
            }
            if (Input.GetKeyDown(KeyCode.F2) && _release1SmallCourtesyWorld is not null)
            {
                var result = _theEnvelopeClosetCashGateDumpNext
                    ? Release1TheEnvelopeClosetCashGateHarness.TryDumpClosetCashSlots(_release1SmallCourtesyWorld)
                    : Release1TheEnvelopeClosetCashGateHarness.TryDecrementFirstClosetCashSlot(_release1SmallCourtesyWorld);
                var mode = _theEnvelopeClosetCashGateDumpNext ? "dump" : "decrement";
                MelonLogger.Msg($"[Organized Crime] OC-61 closet cash gate proof ({mode}): status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-61 closet cash gate proof: {line}");
                _theEnvelopeClosetCashGateDumpNext = !_theEnvelopeClosetCashGateDumpNext;
            }
            if (Input.GetKeyDown(KeyCode.Home) && _release1SmallCourtesyWorld is not null)
            {
                var result = _keepTheLightsOffProductionGateDumpNext
                    ? Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(_release1SmallCourtesyWorld)
                    : Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(_release1SmallCourtesyWorld);
                var mode = _keepTheLightsOffProductionGateDumpNext ? "dump" : "cross check";
                MelonLogger.Msg($"[Organized Crime] OC-65 production gate proof ({mode}): status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-65 production gate proof: {line}");
                _keepTheLightsOffProductionGateDumpNext = !_keepTheLightsOffProductionGateDumpNext;
            }
#if OC_OWNER_SPIKES
            if (Input.GetKeyDown(KeyCode.F3) && _release1SmallCourtesyWorld is not null)
            {
                var result = Release1ArthurFieldContactHarness.TryReport(_release1SmallCourtesyWorld, _arthurFieldContactParked);
                MelonLogger.Msg($"[Organized Crime] OC-69 field contact report: status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-69 field contact report: {line}");
            }
#endif
#if OC_OWNER_SPIKES
            if (Input.GetKeyDown(KeyCode.F4) && _release1SmallCourtesyWorld is not null)
            {
                if (_arthurFieldContactParked)
                {
                    // The contact is parked: unpark straight away (TryToggle skips its own decision
                    // read for this case, see its doc comment). The contact's own native components
                    // already exist, so no resolve pump is needed the way the abandoned on-demand
                    // spawn path once required.
                    var result = Release1ArthurFieldContactHarness.TryToggle(
                        _release1SmallCourtesyWorld, ref _arthurFieldContactParked);
                    LogF4Result(result);
                }
                else if (_arthurToggleDecide.IsPending)
                {
                    MelonLogger.Msg("[Organized Crime] OC-69 field contact proof: a presence decision from an earlier press is still retrying; wait for it to resolve before pressing F4 again.");
                }
                else
                {
                    Release1SmallCourtesyWorldReadStatus readStatus;
                    Release1FieldContactSnapshot? snapshot;
                    try
                    {
                        readStatus = _release1SmallCourtesyWorld.TryReadFieldContact(
                            Release1ArthurFieldContactHarness.ContactId, out snapshot);
                    }
                    catch (Exception ex)
                    {
                        readStatus = Release1SmallCourtesyWorldReadStatus.Faulted;
                        snapshot = null;
                        MelonLogger.Warning($"[Organized Crime] OC-69 field contact proof: an exception was thrown while reading before deciding: {TruncateForLog(ex.Message)}");
                    }

                    if (readStatus == Release1SmallCourtesyWorldReadStatus.Ready && snapshot is not null)
                    {
                        LogF4Result(Release1ArthurFieldContactHarness.ToggleFromSnapshot(
                            _release1SmallCourtesyWorld, snapshot, ref _arthurFieldContactParked));
                    }
                    else
                    {
                        // Not yet parked, and the presence read is not ready: retry on the same wall
                        // clock window the load reconcile already retries around, rather than deciding
                        // from a faulted read.
                        _arthurToggleDecide.Begin((world, ready) => Release1ArthurFieldContactHarness.ToggleFromSnapshot(
                            world, ready, ref _arthurFieldContactParked));
                        MelonLogger.Msg("[Organized Crime] OC-69 field contact proof: presence read not ready yet; retrying before deciding whether to park or report no contact.");
                    }
                }
            }
#endif
#if OC_OWNER_SPIKES
            if (Input.GetKeyDown(KeyCode.F5) && _release1SmallCourtesyWorld is not null)
            {
                if (_arthurFieldContactParked)
                {
                    // F5 provokes only when unparked: a parked contact sits far outside play and forced
                    // invisible, so provoking it would be unobservable and unreachable by the player.
                    MelonLogger.Msg("[Organized Crime] OC-69 field contact provoke: refused; the field contact is parked. Press F4 to unpark it before provoking.");
                }
                else if (_arthurProvokeFollowUp.IsPending)
                {
                    MelonLogger.Msg("[Organized Crime] OC-69 field contact provoke: a provoke from an earlier press is still retrying; wait for it to resolve before pressing F5 again.");
                }
                else
                {
                    Release1SmallCourtesyWorldReadStatus readStatus;
                    Release1FieldContactSnapshot? snapshot;
                    try
                    {
                        readStatus = _release1SmallCourtesyWorld.TryReadFieldContact(
                            Release1ArthurFieldContactHarness.ContactId, out snapshot);
                    }
                    catch (Exception ex)
                    {
                        readStatus = Release1SmallCourtesyWorldReadStatus.Faulted;
                        snapshot = null;
                        MelonLogger.Warning($"[Organized Crime] OC-69 field contact provoke: an exception was thrown while reading before provoking: {TruncateForLog(ex.Message)}");
                    }

                    if (readStatus == Release1SmallCourtesyWorldReadStatus.Ready && snapshot is not null)
                    {
                        var result = Release1ArthurFieldContactHarness.TryProvoke(_release1SmallCourtesyWorld);
                        MelonLogger.Msg($"[Organized Crime] OC-69 field contact provoke: status {result.Status}");
                        foreach (var line in result.Lines)
                            MelonLogger.Msg($"[Organized Crime] OC-69 field contact provoke: {line}");
                    }
                    else
                    {
                        // Live evidence, 2026-09-05: a provoke pressed inside the same not-ready window
                        // the load window can still have (the mutation itself does not read first)
                        // while its own follow-up report faults; provoking blind before a read ever
                        // succeeds is exactly what this retry avoids.
                        _arthurProvokeFollowUp.Begin((world, ready) => Release1ArthurFieldContactHarness.TryProvoke(world));
                        MelonLogger.Msg("[Organized Crime] OC-69 field contact provoke: presence read not ready yet; retrying before provoking so the provoke never runs blind.");
                    }
                }
            }
#endif
            if (Input.GetKeyDown(KeyCode.End) && _release1SmallCourtesyWorld is not null)
            {
                string reason;
                var status = Release1LockdownGatePatch.LockdownActive
                    ? _release1SmallCourtesyWorld.TryReleaseLockdown(out reason)
                    : _release1SmallCourtesyWorld.TryEngageLockdown(out reason);
                MelonLogger.Msg($"[Organized Crime] OC-73 lockdown gate: status {status}");
                if (!string.IsNullOrEmpty(reason))
                    MelonLogger.Msg($"[Organized Crime] OC-73 lockdown gate: {reason}");
            }
            if (Input.GetKeyDown(KeyCode.Insert))
            {
                var result = Release1NpcRegistryDumpHarness.TryDump();
                MelonLogger.Msg($"[Organized Crime] OC-56 npc registry: status {result.Status}");
                foreach (var line in result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-56 npc registry: {line}");
            }
        }
    }

#if OC_OWNER_SPIKES
    private static void LogF4Result(Release1StagingHarnessResult result)
    {
        MelonLogger.Msg($"[Organized Crime] OC-69 field contact proof: status {result.Status}");
        foreach (var line in result.Lines)
            MelonLogger.Msg($"[Organized Crime] OC-69 field contact proof: {line}");
    }

    /// <summary>
    /// The shared logger for every <see cref="Release1ArthurFieldContactReadyPump"/> Mod.cs owns
    /// (provoke, and the F4 decide retry), matching the load reconcile's own three-outcome logging
    /// shape one block above in <see cref="OnUpdate"/>.
    /// </summary>
    private static void LogReadyPumpStep(string label, Release1ArthurReadyPumpStep? step)
    {
        if (step is null) return;
        switch (step.Outcome)
        {
            case Release1ArthurReadyPumpOutcome.Ready when step.Result is not null:
                MelonLogger.Msg($"[Organized Crime] OC-69 {label}: status {step.Result.Status}");
                foreach (var line in step.Result.Lines)
                    MelonLogger.Msg($"[Organized Crime] OC-69 {label}: {line}");
                break;
            case Release1ArthurReadyPumpOutcome.ReadFailed:
                MelonLogger.Warning($"[Organized Crime] OC-69 {label} read failed: {step.Detail}");
                break;
            default:
                MelonLogger.Warning($"[Organized Crime] OC-69 {label} gave up after {Release1ArthurFieldContactReadyPump.MaxSeconds} seconds without a ready read.");
                break;
        }
    }

    private static string TruncateForLog(string? text)
    {
        text ??= string.Empty;
        return text.Length <= 160 ? text : text[..160];
    }
#endif

    public override void OnGUI()
    {
        _syndicateHqComposition?.OnGUI();
        _release1ProductionComposition?.OnGUI();
    }

    private void HandleSaveComplete()
    {
        _timing.Measure("save-complete", () => FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
            IsAuthoritativeHost(),
            () =>
            {
                _timing.Measure("save-complete/story", () => { Release1StoryLifecycleLogging.LogIfRejected("OnSaveComplete", _release1StoryRuntime?.OnSaveComplete()); });
                _timing.Measure("save-complete/production", () => { _release1ProductionComposition?.OnSaveComplete(); });
                _timing.Measure("save-complete/chief", () => { _release1ChiefComposition?.OnSaveComplete(); });
                _timing.Measure("save-complete/hq", () => { _syndicateHqComposition?.OnSaveComplete(); });
                _timing.Measure("save-complete/fish-warehouse", () => { _fishWarehousePersistenceService?.HandleSaveComplete(); });
            }));
    }

    private void HandleSaveStart()
    {
        _timing.Measure("save-start", () =>
        {
            _timing.Measure("save-start/story", () => { Release1StoryLifecycleLogging.LogIfRejected("OnSaveStart", _release1StoryRuntime?.OnSaveStart()); });
            _timing.Measure("save-start/production", () => { _release1ProductionComposition?.OnSaveStart(); });
            _timing.Measure("save-start/chief", () => { _release1ChiefComposition?.OnSaveStart(); });
            _timing.Measure("save-start/hq", () => { _syndicateHqComposition?.OnSaveStart(); });
        });
    }

    private void HandlePreLoad()
    {
        _timing.Measure("pre-load", () =>
        {
            SyndicateHqStorageLoadPatch.BeginLoadCycle();
            _timing.Measure("pre-load/hq", () => { _syndicateHqComposition?.OnPreLoad(); });
            _timing.Measure("pre-load/production", () => { _release1ProductionComposition?.OnPreLoad(); });
            _timing.Measure("pre-load/chief", () => { _release1ChiefComposition?.OnPreLoad(); });
            _timing.Measure("pre-load/story", () => { Release1StoryLifecycleLogging.LogIfRejected("OnPreLoad", _release1StoryRuntime?.OnPreLoad()); });
            FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
                IsAuthoritativeHost(),
                () =>
                {
                    _timing.Measure("pre-load/fish-warehouse", () => { _fishWarehousePersistenceReplayService?.PrepareForLoad(); });
                });
        });
    }

    private void HandleLoadComplete()
    {
        _timing.Measure("load-complete", () =>
        {
            var hqLoadPrepared = true;
            _timing.Measure("load-complete/hq-prepare", () => { hqLoadPrepared = _syndicateHqComposition?.PrepareLoadComplete() ?? true; });
            _timing.Measure("load-complete/story", () => { Release1StoryLifecycleLogging.LogIfRejected("OnLoadComplete", _release1StoryRuntime?.OnLoadComplete()); });
            _timing.Measure("load-complete/production", () => { _release1ProductionComposition?.OnLoadComplete(); });
            _timing.Measure("load-complete/chief", () => { _release1ChiefComposition?.OnLoadComplete(); });
            // C1 fix (final review): unconditional, not gated by _ownerQaKeysEnabled, because S1API's
            // own load-time sweep constructs Release1ArthurNpc regardless of whether owner keys are
            // on. Fix for a live defect (2026-09-05): a single read attempt right here, at
            // load-complete, faulted (Il2CppException) because S1API's freshly network registered
            // contact's Unity object was not ready on that exact frame, and the old one-shot design
            // then never retried; a load-constructed Arthur stood in the world until reached by save.
            // This now only arms Release1ArthurLoadReconcile; OnUpdate below calls Pump every update
            // pass, unconditionally, until it reads Ready or its bound expires.
#if OC_OWNER_SPIKES
            _timing.Measure("load-complete/field-contact-reconcile", () => { _arthurLoadReconcile.BeginAfterLoad(); });
#endif
            _timing.Measure("load-complete/hq-storage", () => { SyndicateHqStorageLoadPatch.CompleteAfterVanillaLoad(); });
            if (hqLoadPrepared) _timing.Measure("load-complete/hq", () => { _syndicateHqComposition?.OnLoadComplete(); });
            FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
                IsAuthoritativeHost(),
                () => _timing.Measure("load-complete/fish-warehouse", () => { _fishWarehousePersistenceReplayService?.HandleLoadComplete(); }));
        });

        if (_ownerQaKeysEnabled && _release1SmallCourtesyWorld is not null)
        {
            try
            {
                var result = Release1WrongAddressStagingHarness.TryDump(_release1SmallCourtesyWorld);
                foreach (var line in result.Lines)
                {
                    if (line.StartsWith("dead drops read: ", StringComparison.Ordinal) ||
                        line.Contains(" quantity ", StringComparison.Ordinal))
                        MelonLogger.Msg($"[Organized Crime] OC-52 staging proof at load: {line}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Organized Crime] OC-52 staging proof at load failed: {ex.Message}");
            }
        }
    }

    private void HandlePreSceneChange()
    {
        _timing.Measure("pre-scene-change", () =>
        {
            _timing.Measure("pre-scene-change/production", () => { _release1ProductionComposition?.OnPreSceneChange(); });
            _timing.Measure("pre-scene-change/hq", () => { _syndicateHqComposition?.OnPreSceneChange(); });
        });
    }

    private static string? GetActiveSaveFolder()
    {
        try
        {
            return Il2CppScheduleOne.Persistence.LoadManager.Instance?.LoadedGameFolderPath;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[Organized Crime] Could not resolve the active save folder: {ex.Message}");
            return null;
        }
    }

    private static ILocalPressureStateRepository? CreateLocalPressureRepository(string? activeSaveFolder)
    {
        if (!LocalPressureSavePath.TryCreate(activeSaveFolder, out var savePath, out _))
            return null;

        return new LocalPressureStateStoreRepository(new LocalPressureStateStore(savePath!));
    }

    private static IRelease1StoryRepository CreateRelease1StoryRepository(string activeSaveFolder)
    {
        if (!Release1StorySavePath.TryCreate(activeSaveFolder, out var savePath, out var result))
            throw new InvalidOperationException(result.Message);
        return new Release1StoryStateStoreRepository(new Release1StoryStateStore(savePath!));
    }

    private void ApplyLoadedSaveState(FishWarehouseSaveState state)
    {
        _fishWarehouseRuntimeService?.ApplyLoadedSaveState(state);
    }

    private static bool IsAuthoritativeHost()
    {
        try
        {
            var serverManager = InstanceFinder.ServerManager;
            return serverManager is not null && serverManager.OneServerStarted();
        }
        catch
        {
            return false;
        }
    }

    private sealed class FishWarehousePersistenceReplayOperations : IFishWarehousePersistenceReplayOperations
    {
        private readonly RuntimePropertyHost _propertyHost;
        private readonly FishWarehouseRuntimeService _runtimeService;
        private readonly FishWarehousePersistenceService _persistenceService;
        private readonly FishWarehousePropertyCaptureService _captureService;
        private readonly Action<string> _log;
        private FishWarehousePropertyReplayHost? _objectReplay;
        private FishWarehouseEmployeeReplayHost? _employeeReplay;

        public FishWarehousePersistenceReplayOperations(
            RuntimePropertyHost propertyHost,
            FishWarehouseRuntimeService runtimeService,
            FishWarehousePersistenceService persistenceService,
            FishWarehousePropertyCaptureService captureService,
            Action<string> log)
        {
            _propertyHost = propertyHost;
            _runtimeService = runtimeService;
            _persistenceService = persistenceService;
            _captureService = captureService;
            _log = log;
        }

        public bool IsRuntimeReady => _propertyHost.PersistenceRuntimeReady;
        public bool IsNavigationReady => _runtimeService.EmployeeNavigationReady;

        public void ConfigureReplay(bool objectReplayRequired, int? employeeAgentTypeId) =>
            _propertyHost.ConfigurePersistenceReplay(objectReplayRequired, employeeAgentTypeId);

        public void RequestOwnedRestore() => _runtimeService.RequestLoadedOwnedRestore();

        public void ResetRuntimeReferences()
        {
            _objectReplay = null;
            _employeeReplay = null;
        }

        public FishWarehouseReplayOperationResult ReplayObjects()
        {
            var propertyData = _captureService.CapturedPropertyData;
            var property = _propertyHost.CurrentProperty;
            if (propertyData is null || property is null)
                return FishWarehouseReplayOperationResult.Failed("captured PropertyData or runtime Property was unavailable.");

            if (_objectReplay is null)
            {
                if (!FishWarehouseNativePropertyReplayAdapter.TryCreateDescriptors(
                        propertyData,
                        out var descriptors,
                        out var descriptorFailure))
                {
                    return FishWarehouseReplayOperationResult.Failed(descriptorFailure ?? "object replay descriptors were unavailable.");
                }

                _objectReplay = new FishWarehousePropertyReplayHost(
                    new FishWarehouseNativePropertyReplayAdapter(propertyData, property, _log),
                    descriptors,
                    log: _log);
            }

            var result = _objectReplay.Replay();
            if (result.State == FishWarehousePropertyReplayState.Failed)
                return FishWarehouseReplayOperationResult.Failed(result.FailureReason ?? "native object replay failed.");
            if (result.State != FishWarehousePropertyReplayState.Succeeded)
                return FishWarehouseReplayOperationResult.Pending();

            return FishWarehouseReplayOperationResult.Succeeded(result.ResolvedObjectCount);
        }

        public FishWarehouseReplayOperationResult MarkObjectsReplayed()
        {
            if (_objectReplay?.Result.State != FishWarehousePropertyReplayState.Succeeded)
                return FishWarehouseReplayOperationResult.Failed("native object replay was not complete before the runtime gate was marked.");

            _runtimeService.MarkPersistenceObjectsReplayed();
            // Pass the saved employee home (bed) GUIDs so the host can disambiguate
            // when more than one locker belongs to the Property (e.g. a stray dev
            // P-press) instead of failing the whole replay and losing the employee.
            var savedHomeGuids = _captureService.CapturedEmployeeRecords
                .Select(record => record.BedGuid)
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .ToArray();
            var adoption = _propertyHost.AssessRestoredEmployeeHome(savedHomeGuids);
            if (adoption == FishWarehouseEmployeeHomeAdoptionResult.MultipleFound)
                return FishWarehouseReplayOperationResult.Failed("multiple restored Fish Warehouse employee homes were found; authored fallback was not permitted.");
            if (adoption == FishWarehouseEmployeeHomeAdoptionResult.Failed)
                return FishWarehouseReplayOperationResult.Failed("restored Fish Warehouse employee-home adoption failed.");
            if (adoption == FishWarehouseEmployeeHomeAdoptionResult.Adopted)
            {
                _runtimeService.MarkPersistenceEmployeeHomeRestored();
            }
            else if (_persistenceService.CurrentState?.EmployeeHomePlaced == true &&
                     !_runtimeService.TryPlaceEmployeeHome())
            {
                return FishWarehouseReplayOperationResult.Failed("saved Fish Warehouse employee home was not restored and authored fallback placement failed.");
            }

            return FishWarehouseReplayOperationResult.Succeeded(_objectReplay.Result.ResolvedObjectCount);
        }

        public FishWarehouseReplayOperationResult ReplayEmployees()
        {
            var propertyData = _captureService.CapturedPropertyData;
            var property = _propertyHost.CurrentProperty;
            if (propertyData is null || property is null)
                return FishWarehouseReplayOperationResult.Failed("captured PropertyData or runtime Property was unavailable.");

            if (_employeeReplay is null)
            {
                var descriptors = FishWarehousePropertyCaptureService.CreateEmployeeReplayDescriptors(
                    _captureService.CapturedEmployeeRecords);

                if (!_propertyHost.TryCreateEmployeeReplayAdapter(
                        propertyData,
                        out var adapter,
                        out var adapterFailure,
                        _persistenceService.CurrentState?.Replay?.InventoryRestoredEmployeeGuids))
                {
                    return FishWarehouseReplayOperationResult.Failed(adapterFailure ?? "employee replay adapter was unavailable.");
                }

                _employeeReplay = new FishWarehouseEmployeeReplayHost(
                    adapter,
                    descriptors,
                    _persistenceService.CurrentState?.UnsupportedEmployees ?? Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
                    log: _log);
            }

            var result = _employeeReplay.Replay(IsNavigationReady);
            if (result.State == FishWarehouseEmployeeReplayState.Pending)
                return new FishWarehouseReplayOperationResult(
                    FishWarehouseReplayOperationState.Pending,
                    0,
                    null,
                    result.InventoryRestoredEmployeeGuids);
            if (result.State == FishWarehouseEmployeeReplayState.Failed || !result.CanAdvanceEmployeesReplayed)
            {
                return new FishWarehouseReplayOperationResult(
                    FishWarehouseReplayOperationState.Failed,
                    0,
                    result.FailureReason ?? result.PrimaryFailureReason ?? "supported employee replay did not complete.",
                    result.InventoryRestoredEmployeeGuids);
            }

            if (!_persistenceService.TryMergeUnsupportedEmployees(result.UnsupportedEmployees))
                return FishWarehouseReplayOperationResult.Failed("unsupported employee records could not be persisted.");

            _log($"Fish Warehouse employee replay summary: {result.ReplayedEmployeeCount}/{result.SupportedCapturedEmployeeCount} supported employees replayed.");
            return new FishWarehouseReplayOperationResult(
                FishWarehouseReplayOperationState.Succeeded,
                result.ReplayedEmployeeCount,
                null,
                result.InventoryRestoredEmployeeGuids);
        }

        public FishWarehouseDeliveryRestoreFlushResult ReleaseDeliveries() =>
            FishWarehouseDeliveryRestorePatch.FlushIfReady();

    }

}
