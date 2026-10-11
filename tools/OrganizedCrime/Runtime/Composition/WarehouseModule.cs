// OC-151: the fish warehouse setup module. Builds the warehouse patches, property
// host, runtime, acquisition link, snapshot store, replay lifecycle, persistence,
// capture, banner sender, capacity source and replay services as one ordered method,
// in today's order, with the Chapter 3 wiring inline exactly where Mod.cs does it.
// captureService stays a method local captured by deferred lambdas, as in Mod.cs;
// the defensive field-alias locals are unnecessary for locals and inlined. Deferred
// lambdas that read Mod.cs fields read the composition root at fire time instead.
// The log/warn parameters are the raw sinks; service delegates prefix "[Organized Crime] ".
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal sealed record WarehouseOutputs(
    IFishWarehousePropertyHost PropertyHost,
    FishWarehouseRuntimeService Runtime,
    FishWarehousePersistenceService Persistence,
    FishWarehousePropertyCaptureService Capture,
    FishWarehousePersistenceReplayService Replay);

internal static class WarehouseModule
{
    public static WarehouseOutputs Build(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Chapter3RuntimeService chapter3,
        Action<string> log,
        Action<string> warn)
    {
        Action<string> serviceLog = message => log($"[Organized Crime] {message}");
        engine.ApplyWarehouseCanvas();
        engine.ApplyWarehouseGrid();
        engine.ApplyWarehouseDock();
        var propertyHost = engine.CreatePropertyHost();
        root.PropertyHost = propertyHost;
        engine.ApplyWarehouseDeliveryRestore(() => propertyHost.CanRestoreDeliveries, serviceLog);
        var runtimeService = new FishWarehouseRuntimeService(
            propertyHost,
            engine.IsFishWarehouseReady,
            propertyHost.TryPlaceEmployeeHome,
            serviceLog,
            lighting: propertyHost);
        engine.CompositionTrace?.Add("step:Warehouse.RuntimeService");
        root.WarehouseRuntime = runtimeService;
        // OC-125: the Chapter 3 story purchase acquires through this seam.
        // OC-128: the story build waits while warehouse persistence leaves
        // ownership unknown (a blocked capture or an in-flight replay).
        chapter3.WarehouseAcquisition =
            new Chapter3WarehouseAcquisition(
                runtimeService,
                isOwnershipSettled: () => IsOwnershipSettledForStory(root.WarehouseCapture, root.WarehousePersistence));
        engine.CompositionTrace?.Add("step:Warehouse.Acquisition");
        var snapshotStore = new FishWarehousePropertySnapshotStore();
        engine.CompositionTrace?.Add("step:Warehouse.SnapshotStore");
        FishWarehousePropertyCaptureService? captureService = null;
        var replayLifecycle = new FishWarehouseReplayLifecycle(
            checkpoint => captureService?.GetRecoveryPrerequisites(checkpoint) ??
                new FishWarehouseCaptureRecoveryPrerequisites(
                    ByteProtectionSucceeded: false,
                    InspectionSucceeded: false,
                    TypedPayloadRetentionSucceeded: false,
                    CheckpointPersistenceSucceeded: false));
        engine.CompositionTrace?.Add("step:Warehouse.ReplayLifecycle");
        var persistenceService = new FishWarehousePersistenceService(
            new FishWarehouseSaveStore(),
            engine.ActiveSaveFolder,
            () => root.WarehouseRuntime?.CaptureSaveState() ?? FishWarehouseSaveState.UnownedState(),
            root.ApplyLoadedWarehouseState,
            serviceLog,
            snapshotStore,
            replayLifecycle);
        persistenceService.DiagnosticLog = message => OrganizedCrimeLog.Diagnostic(message, serviceLog);
        engine.CompositionTrace?.Add("step:Warehouse.PersistenceService");
        root.WarehousePersistence = persistenceService;
        captureService = new FishWarehousePropertyCaptureService(
            snapshotStore,
            new FishWarehouseNativePropertySnapshotInspector(),
            persistenceService,
            replayLifecycle,
            serviceLog);
        engine.CompositionTrace?.Add("step:Warehouse.CaptureService");
        root.WarehouseCapture = captureService;
        // OC-138: the owner QA notice show/hide posts through the same
        // banner sender the replay announcer uses.
        chapter3.StaffReturnBannerSender = new FishWarehouseStaffReturnBannerSender(
            new FishWarehouseHudBannerTarget(),
            serviceLog);
        engine.CompositionTrace?.Add("step:Warehouse.BannerSender");
        // OC-117: the warehouse reads its staff tier from Chapter 3 (tier 1
        // whenever Chapter 3 is not Active) and the pending replay count
        // from the capture service; the capacity purchase raises it live.
        propertyHost.EmployeeCapacitySource = new FishWarehouseEmployeeCapacitySource(
            () => root.Chapter3?.ReadWarehouseTierCapacity() ?? Chapter3WarehouseCapacityTiers.Tier1Capacity,
            () => captureService.CapturedEmployeeRecords.Count);
        engine.CompositionTrace?.Add("step:Warehouse.CapacitySource");
        chapter3.WarehouseCapacity = new Chapter3WarehouseCapacityApplier(propertyHost);
        engine.CompositionTrace?.Add("step:Warehouse.Capacity");
        var replayOperations = new FishWarehousePersistenceReplayOperations(
            propertyHost,
            runtimeService,
            persistenceService,
            captureService,
            serviceLog);
        engine.CompositionTrace?.Add("step:Warehouse.ReplayOperations");
        var replayService = new FishWarehousePersistenceReplayService(
            replayLifecycle,
            engine.ActiveSaveFolder,
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
            engine.ResetWarehouseDeliveryRestore,
            () =>
            {
                if (!runtimeService.PrepareForLoad())
                    warn("[Organized Crime] Fish Warehouse runtime Property could not be fully prepared for the next save load.");
            },
            log: serviceLog);
        engine.CompositionTrace?.Add("step:Warehouse.ReplayService");
        root.WarehouseReplay = replayService;
        engine.ApplyWarehouseLoadCapture(engine.IsHost, engine.ActiveSaveFolder, captureService, serviceLog);
        engine.ApplyWarehouseWriteGuard(engine.IsHost, () => captureService.IsDegraded);
        return new WarehouseOutputs(propertyHost, runtimeService, persistenceService, captureService, replayService);
    }

    /// <summary>
    /// OC-128: the Chapter 3 story build treats the warehouse as pending
    /// only while ownership is known-unowned. A blocked or degraded load
    /// capture, or a replay still under snapshot protection, leaves
    /// ownership unknown and the build waits for the next sleep.
    /// </summary>
    private static bool IsOwnershipSettledForStory(
        FishWarehousePropertyCaptureService? captureService,
        FishWarehousePersistenceService? persistenceService)
    {
        if (captureService?.State is FishWarehousePropertyCaptureState.RecoveryRequired or FishWarehousePropertyCaptureState.Degraded)
            return false;
        return persistenceService?.RequiresSnapshotProtection != true;
    }
}

internal sealed class FishWarehousePersistenceReplayOperations : IFishWarehousePersistenceReplayOperations
{
    private readonly IFishWarehousePropertyHost _propertyHost;
    private readonly FishWarehouseRuntimeService _runtimeService;
    private readonly FishWarehousePersistenceService _persistenceService;
    private readonly FishWarehousePropertyCaptureService _captureService;
    private readonly Action<string> _log;
    private FishWarehousePropertyReplayHost? _objectReplay;
    private FishWarehouseEmployeeReplayHost? _employeeReplay;
    private readonly FishWarehouseStaffReturnAnnouncer _staffReturn;

    public FishWarehousePersistenceReplayOperations(
        IFishWarehousePropertyHost propertyHost,
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
        _staffReturn = new FishWarehouseStaffReturnAnnouncer(
            new FishWarehouseStaffReturnBannerSender(new FishWarehouseHudBannerTarget(), log),
            log);
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
        _staffReturn.Reset();
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
            var preserved = FishWarehousePropertyCaptureService.CreatePreservedEmployeeReplayDescriptors(
                _persistenceService.CurrentState?.UnsupportedEmployees ?? Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
                descriptors,
                out var keptUnsupported);
            descriptors = descriptors.Concat(preserved).ToArray();

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
                keptUnsupported,
                log: _log);
        }

        var result = _employeeReplay.Replay(IsNavigationReady);
        // OC-138: the staff-return toasts learn each result after
        // navigation is ready, which only happens on the live scene
        // once the loading screen is gone; the purchase build never
        // runs this phase, so it never toasts.
        _staffReturn.Observe(IsNavigationReady, result);
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

        if (!_persistenceService.TryReplaceUnsupportedEmployees(result.UnsupportedEmployees))
            return FishWarehouseReplayOperationResult.Failed("unsupported employee records could not be persisted.");

        var receipt = $"Fish Warehouse employee replay summary: {result.ReplayedEmployeeCount}/{result.SupportedCapturedEmployeeCount} supported employees replayed ({FormatReplayedEmployeesByIdentity(result)}).";
        if (result.ReplayedEmployeeCount == result.CapturedEmployeeCount && result.UnsupportedEmployees.Count == 0)
            OrganizedCrimeLog.Diagnostic(receipt, _log);
        else
            _log(receipt);
        return new FishWarehouseReplayOperationResult(
            FishWarehouseReplayOperationState.Succeeded,
            result.ReplayedEmployeeCount,
            null,
            result.InventoryRestoredEmployeeGuids);
    }

    private static string FormatReplayedEmployeesByIdentity(FishWarehouseEmployeeReplayResult result)
    {
        var byIdentity = result.ReplayedEmployeesByIdentity;
        int count(string identity) =>
            byIdentity is not null && byIdentity.TryGetValue(identity, out var replayed) ? replayed : 0;
        return $"Botanist {count("Botanist")}, Chemist {count("Chemist")}, Cleaner {count("Cleaner")}, Packager {count("Packager")}";
    }

    public FishWarehouseDeliveryRestoreFlushResult ReleaseDeliveries() =>
        FishWarehouseDeliveryRestorePatch.FlushIfReady();
}
