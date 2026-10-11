// OC-151: the composition seam's view of the fish warehouse property host.
// RuntimePropertyHost implements this (1-line decl change); the test seam never names
// the concrete host, which is engine-bound and not linked into the test project.
// IRuntimePropertyHost cannot be extended instead: three test fakes implement it.
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Property;

namespace OrganizedCrime.Runtime.Composition;

public interface IFishWarehousePropertyHost : IRuntimePropertyHost, IFishWarehouseLightingOwnerQa
{
    bool CanRestoreDeliveries { get; }
    bool TryPlaceEmployeeHome();
    IFishWarehouseEmployeeCapacitySource? EmployeeCapacitySource { get; set; }
    Property? CurrentProperty { get; }
    bool TryCreateEmployeeReplayAdapter(
        PropertyData propertyData,
        out IFishWarehouseNativeEmployeeReplayAdapter adapter,
        out string? failureReason,
        IReadOnlyCollection<string>? inventoryRestoredEmployeeGuids = null);
    FishWarehouseEmployeeHomeAdoptionResult AssessRestoredEmployeeHome(IReadOnlyCollection<string>? savedHomeGuids = null);
    void EnsureManagementWorldspaceUiContainerForOpenCanvas();
    bool IsEmployeeInfrastructureReady { get; }
    bool TrySetEmployeeCapacity(int capacity);
    bool TryMoveIdlePointsToPlayer(out string report);
    bool TryReadEmployeeCapacity(out int capacity, out int idlePointCount);
    bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot);
    bool TryMovePlayerSpawnToPlayer(bool exterior, out string report);
}
