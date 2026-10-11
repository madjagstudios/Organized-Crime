using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-136: what an issue needs to know about the picked product: its
/// display name, its native drug type name (EDrugType, mapped through
/// <see cref="Chapter3DemandPolicy.TryParseProductType"/>), and its
/// definition price frozen as the demand's unit value.
/// </summary>
public sealed record Chapter3DemandWorldProductInfo(
    string ProductId,
    string ProductName,
    string DrugTypeName,
    int UnitValue);

/// <summary>
/// OC-136: the demand service's native seam. Stations come from inside
/// the fish warehouse only, with packaging stations excluded by station
/// type; the locker calls target the one family locker; the door read
/// feeds the quest marker. Reads report Ready only with a complete
/// snapshot, anything else holds the pass. Mutations mirror the
/// dead-drop slot rules: an owned lock first, a negative-only change,
/// and a verified post-quantity. Tests fake this seam; production is
/// <c>S1ApiChapter3ProductionDemandWorld</c>.
/// </summary>
public interface IChapter3ProductionDemandWorld
{
    Release1SmallCourtesyWorldReadStatus TryReadWarehouseStations(
        out IReadOnlyList<Chapter3DemandStationSnapshot> stations);

    Release1SmallCourtesyWorldReadStatus TryReadProductInfo(
        string productId, out Chapter3DemandWorldProductInfo? info);

    Release1SmallCourtesyWorldReadStatus TryReadPackagingUnits(
        string packagingId, out int units);

    Release1SmallCourtesyWorldReadStatus TryReadFamilyLockerSlots(
        out IReadOnlyList<Chapter3DemandLockerSlot> slots);

    Release1SmallCourtesyWorldMutationStatus TrySetFamilyLockerSlotLocked(
        int slotIndex, bool locked);

    Release1SmallCourtesyWorldMutationStatus TryChangeFamilyLockerSlotQuantity(
        int slotIndex, int amount);

    Release1SmallCourtesyWorldMutationStatus TryEnsureFamilyLockerName();

    bool TryReadHqDoor(out float x, out float y, out float z);
}
