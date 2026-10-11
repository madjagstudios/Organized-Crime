// OC-151: Build-time engine stubs behind OcCompositionRoot.Build. Each stub defaults to
// throwing; members the real composition calls during Build or Teardown return the
// benign value production tolerates (recorded where order matters). Scriptable hooks
// cover the configuration the integration tests vary (host clock minutes, lockdown).
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;

namespace OrganizedCrime.Tests;

internal sealed class BuildStubDemandWorld : IChapter3ProductionDemandWorld
{
    public Release1SmallCourtesyWorldReadStatus TryReadWarehouseStations(out IReadOnlyList<Chapter3DemandStationSnapshot> stations) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldReadStatus TryReadProductInfo(string productId, out Chapter3DemandWorldProductInfo? info) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldReadStatus TryReadPackagingUnits(string packagingId, out int units) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldReadStatus TryReadFamilyLockerSlots(out IReadOnlyList<Chapter3DemandLockerSlot> slots) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldMutationStatus TrySetFamilyLockerSlotLocked(int slotIndex, bool locked) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldMutationStatus TryChangeFamilyLockerSlotQuantity(int slotIndex, int amount) => throw new NotSupportedException();
    public Release1SmallCourtesyWorldMutationStatus TryEnsureFamilyLockerName() => throw new NotSupportedException();
    public bool TryReadHqDoor(out float x, out float y, out float z) => throw new NotSupportedException();
}

internal sealed class BuildStubVisitConditions : IChapter3ArthurVisitConditions
{
    public bool TryRead(out Chapter3ArthurVisitConditions conditions) => throw new NotSupportedException();
}

internal sealed class BuildStubArthurPhone : IChapter3ArthurPhone
{
    public bool TryCall(Chapter3ArthurVisitKind kind, IReadOnlyList<string> stages, int chapterDay, out string reason) => throw new NotSupportedException();
}

internal sealed class BuildStubNative : IRelease1NativePresentation
{
    public Release1NativePresentationStatus TryEnsureContact() => throw new NotSupportedException();
    public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts) => throw new NotSupportedException();
    public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision) => throw new NotSupportedException();
    public Release1NativePresentationStatus TrySendMessage(string text) => throw new NotSupportedException();
    public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen) => throw new NotSupportedException();
    public Release1NativePresentationStatus TrySetLabeledDecision(Release1LabeledDecision decision) => throw new NotSupportedException();
    public Release1NativePresentationStatus TryClearDecision() => throw new NotSupportedException();
    public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest) => throw new NotSupportedException();
    public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest) => throw new NotSupportedException();
    public Release1NativePresentationStatus TryEndQuest(string key) => throw new NotSupportedException();
    public void OnPreLoad() => throw new NotSupportedException();
    public void OnSaveStart() => throw new NotSupportedException();
}

internal sealed class BuildStubPhoneQueue : IRelease1PhoneCallQueue
{
    public bool IsAvailable(Release1PhoneCallRequest request) => throw new NotSupportedException();
    public void Invoke(Release1PhoneCallRequest request) => throw new NotSupportedException();
}

internal sealed class BuildStubCartelSource : IRelease1CartelStatusSource
{
    public Release1CartelStatusReadStatus TryRead(out Release1CartelStatus status) => throw new NotSupportedException();
}

internal sealed class BuildStubProtectionCheckpointWorld : IRelease1ProtectionCheckpointWorld
{
    public bool TryDisableAllCheckpoints() => throw new NotSupportedException();
    public bool TryReadEnabled(out int mask) => throw new NotSupportedException();
    public bool TryRestoreEnabled(int mask) => throw new NotSupportedException();
}

internal sealed class BuildStubExposureReader : IChapter3PlayerExposureReader
{
    public bool TryRead(out Chapter3PlayerExposure exposure) => throw new NotSupportedException();
}

internal sealed class BuildStubCurfewReader : IChapter3CurfewReader
{
    public bool TryReadActive(out bool active) => throw new NotSupportedException();
}

internal sealed class BuildStubWantedWriter : IChapter3WantedWriter
{
    public bool TrySetArresting(out int readBackLevel) => throw new NotSupportedException();
}

internal sealed class BuildStubPursuitHold : IChapter3PursuitHold
{
    public bool TryRead(out int pursuitLevel, out float timeSinceSighted, out float searchTime) => throw new NotSupportedException();
    public bool TryWriteTimeSinceSighted(float value) => throw new NotSupportedException();
}

internal sealed class BuildStubCourtesyWorld : Release1FullWorldStub
{
    public Func<Release1SmallCourtesyWorldReadStatus> MinutesStatus { get; set; } = () => Release1SmallCourtesyWorldReadStatus.Unavailable;
    public double MinutesValue { get; set; }
    public List<string> LockdownCalls { get; } = new();

    public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
    {
        activity = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
    {
        totalMinutes = MinutesValue;
        return MinutesStatus();
    }

    public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
    {
        LockdownCalls.Add("engage");
        reason = "engaged";
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }

    public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
    {
        LockdownCalls.Add("release");
        reason = "released";
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }
}

internal sealed class BuildStubPropertyHost : IFishWarehousePropertyHost
{
    public List<string> Calls { get; } = new();
    public IFishWarehouseEmployeeCapacitySource? EmployeeCapacitySource { get; set; }

    public RuntimePropertyHostResult Start(RuntimePropertyDefinition definition) => throw new NotSupportedException();
    public bool TrySetOwned() => throw new NotSupportedException();
    public bool PersistenceRuntimeReady => throw new NotSupportedException();
    public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId) => throw new NotSupportedException();
    public void MarkPersistenceObjectsReplayed() => throw new NotSupportedException();
    public bool TryAdoptRestoredEmployeeHome() => throw new NotSupportedException();
    public void UpdateOwnedFeatures() => throw new NotSupportedException();
    public FishWarehouseContinuousNavigationState EmployeeNavigationState => throw new NotSupportedException();
    public bool EmployeeNavigationReady => throw new NotSupportedException();
    public int? EmployeeNavigationAgentTypeId => throw new NotSupportedException();
    public RuntimePropertyHostResult Stop() => throw new NotSupportedException();

    public bool LightingActive => throw new NotSupportedException();
    public bool TryAddLampAbovePlayer() => throw new NotSupportedException();
    public bool TryRemoveMostRecentLamp() => throw new NotSupportedException();
    public bool TryAdjustMostRecentLampHeight(float delta) => throw new NotSupportedException();
    public bool TryAdjustAllLampIntensity(float delta) => throw new NotSupportedException();
    public bool TryToggleMostRecentLampYaw() => throw new NotSupportedException();
    public bool TryLogLampLayout() => throw new NotSupportedException();

    public bool CanRestoreDeliveries => throw new NotSupportedException();
    public bool TryPlaceEmployeeHome() => throw new NotSupportedException();
    public Property? CurrentProperty => throw new NotSupportedException();
    public bool TryCreateEmployeeReplayAdapter(PropertyData propertyData, out IFishWarehouseNativeEmployeeReplayAdapter adapter, out string? failureReason, IReadOnlyCollection<string>? inventoryRestoredEmployeeGuids = null) => throw new NotSupportedException();
    public FishWarehouseEmployeeHomeAdoptionResult AssessRestoredEmployeeHome(IReadOnlyCollection<string>? savedHomeGuids = null) => throw new NotSupportedException();
    public void EnsureManagementWorldspaceUiContainerForOpenCanvas() => Calls.Add("EnsureUi");
    public bool IsEmployeeInfrastructureReady => throw new NotSupportedException();
    public bool TrySetEmployeeCapacity(int capacity) => throw new NotSupportedException();
    public bool TryMoveIdlePointsToPlayer(out string report) => throw new NotSupportedException();
    public bool TryReadEmployeeCapacity(out int capacity, out int idlePointCount) => throw new NotSupportedException();
    public bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot) => throw new NotSupportedException();
    public bool TryMovePlayerSpawnToPlayer(bool exterior, out string report) => throw new NotSupportedException();
}

internal sealed class BuildStubLawAdapter : ILocalPressureRuntimeHostAdapter
{
    public List<string> Calls { get; } = new();
    public string? ActiveSaveFolderResult { get; set; }
    public string? CanonicalHostIdentityResult { get; set; }

    public string? ActiveSaveFolder => ActiveSaveFolderResult;
    public string? CanonicalHostIdentity => CanonicalHostIdentityResult;
    public event Action? PreLoad;
    public event Action? LoadComplete;
    public event Action? SaveStart;
    public event Action? SaveComplete;
    public event Action<LocalPressureClockBoundary>? ClockBoundary;

    public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => throw new NotSupportedException();
    public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() => throw new NotSupportedException();
    public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample) => throw new NotSupportedException();
    public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players) => throw new NotSupportedException();

    public void Dispose() => Calls.Add("Dispose");

    public void RaisePreLoad() => PreLoad?.Invoke();
    public void RaiseLoadComplete() => LoadComplete?.Invoke();
    public void RaiseSaveStart() => SaveStart?.Invoke();
    public void RaiseSaveComplete() => SaveComplete?.Invoke();
    public void RaiseClockBoundary(LocalPressureClockBoundary boundary) => ClockBoundary?.Invoke(boundary);
}

internal sealed class ScriptableCourtesyWorld : IRelease1SmallCourtesyWorld
{
    public Release1SmallCourtesyWorldReadStatus DropsStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Unavailable;
    public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } = Array.Empty<Release1SmallCourtesyDropCandidate>();
    public Release1SmallCourtesyWorldReadStatus SlotsStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Unavailable;
    public IReadOnlyList<Release1SmallCourtesySlotSnapshot> Slots { get; set; } = Array.Empty<Release1SmallCourtesySlotSnapshot>();
    public Release1SmallCourtesyWorldReadStatus MinutesStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Unavailable;
    public double MinutesValue { get; set; }
    public List<string> LockdownCalls { get; } = new();

    public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
    {
        totalMinutes = MinutesValue;
        return MinutesStatus;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
    {
        products = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
    {
        drops = Drops;
        return DropsStatus;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)
    {
        packaging = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        slots = Slots;
        return SlotsStatus;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
    {
        room = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
    {
        reason = string.Empty;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription)
    {
        subscription = null;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
    {
        balance = 0f;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
    {
        LockdownCalls.Add("engage");
        reason = "engaged";
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }

    public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
    {
        LockdownCalls.Add("release");
        reason = "released";
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;

    public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
    {
        activity = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
    {
        reason = string.Empty;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
    {
        reason = string.Empty;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
    {
        reason = string.Empty;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
    {
        reason = string.Empty;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
    {
        snapshot = null!;
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }
}

internal sealed class FakeDisposable : IDisposable
{
    public int DisposeCalls { get; private set; }
    public void Dispose() => DisposeCalls++;
}
