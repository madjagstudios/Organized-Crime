using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using S1API.Products;
using NativeBusiness = Il2CppScheduleOne.Property.Business;
using NativeItemSlot = Il2CppScheduleOne.ItemFramework.ItemSlot;
using NativeProperty = Il2CppScheduleOne.Property.Property;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-136: the production demand world's S1API adapter. Stations enumerate
/// from the owned fish warehouse's buildable items through the same
/// per-kind casts as the activity read, so packaging stations never
/// appear; outputs read off each station's typed <c>OutputSlots</c>.
/// Products come from the discovered list, packaging quantities from the
/// native registry, and the family locker delegates to the HQ storage
/// runtime's retained closet. Mutations and the rename are host only.
/// </summary>
public sealed class S1ApiChapter3ProductionDemandWorld : IChapter3ProductionDemandWorld
{
    private const string FamilyLockerDefinitionName = SyndicateHqStorageContract.FamilyLockerDefinitionName;

    private readonly ISyndicateHqStorageRuntime? _holdRoomStorage;
    private readonly Func<Release1WorldPoint?>? _hqDoor;
    private readonly Func<bool>? _isHost;
    private readonly Action<string> _log;

    public S1ApiChapter3ProductionDemandWorld(
        ISyndicateHqStorageRuntime? holdRoomStorage = null,
        Func<Release1WorldPoint?>? hqDoor = null,
        Func<bool>? isHost = null,
        Action<string>? log = null)
    {
        _holdRoomStorage = holdRoomStorage;
        _hqDoor = hqDoor;
        _isHost = isHost;
        _log = log ?? (_ => { });
    }

    public Release1SmallCourtesyWorldReadStatus TryReadWarehouseStations(
        out IReadOnlyList<Chapter3DemandStationSnapshot> stations)
    {
        stations = Array.Empty<Chapter3DemandStationSnapshot>();
        var read = new List<Chapter3DemandStationSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in OwnedProperties())
        {
            if (property == null) continue;
            if (!string.Equals(property.PropertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal))
                continue;
            var items = property.BuildableItems;
            if (items == null) continue;
            foreach (var item in items)
            {
                if (item == null || item.IsDestroyed) continue;
                var guid = item.GUID.ToString();
                if (string.IsNullOrWhiteSpace(guid) || !seen.Add(guid)) continue;
                var station = ReadStation(item, guid);
                if (station is null) { seen.Remove(guid); continue; }
                read.Add(station);
            }
        }
        stations = read;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    /// <summary>
    /// The union of the two owned lists, like the activity read:
    /// whether an owned business also appears in OwnedProperties is
    /// unproven, and the warehouse match below makes the union correct
    /// either way.
    /// </summary>
    private static IEnumerable<NativeProperty> OwnedProperties()
    {
        var owned = NativeProperty.OwnedProperties;
        if (owned != null)
            foreach (var property in owned)
                yield return property;
        var businesses = NativeBusiness.OwnedBusinesses;
        if (businesses != null)
            foreach (var business in businesses)
                yield return business;
    }

    /// <summary>
    /// Per kind, from the station's own public members; MixingStationMk2
    /// is caught by the mixing cast. The finishing latch is the done
    /// condition the counter pairs with an output increase: the mixer's
    /// <c>IsMixingDone</c>, the oven's and chemistry's
    /// <c>IsComplete</c>. The cauldron and the rack expose no done flag,
    /// so their completions arrive as the running-to-idle edge and the
    /// operation-identity change. Anything else (packaging, brick press,
    /// plants) returns null and never enters a snapshot.
    /// </summary>
    private static Chapter3DemandStationSnapshot? ReadStation(BuildableItem item, string guid)
    {
        var chemistry = item.TryCast<ChemistryStation>();
        if (chemistry != null)
        {
            var operation = chemistry.CurrentCookOperation;
            return operation == null
                ? Station(guid, Release1ProductionStationKind.ChemistryStation, false, "idle", 0, false, chemistry.OutputSlots)
                : Station(guid, Release1ProductionStationKind.ChemistryStation, true,
                    Identity(operation.RecipeID), Math.Max(0, operation.CurrentTime),
                    operation.IsComplete(), chemistry.OutputSlots);
        }

        var oven = item.TryCast<LabOven>();
        if (oven != null)
        {
            var operation = oven.CurrentOperation;
            return operation == null
                ? Station(guid, Release1ProductionStationKind.LabOven, false, "idle", 0, false, oven.OutputSlots)
                : Station(guid, Release1ProductionStationKind.LabOven, true,
                    Identity(operation.ProductID), Math.Max(0, operation.CookProgress),
                    operation.IsComplete(), oven.OutputSlots);
        }

        var mixing = item.TryCast<MixingStation>();
        if (mixing != null)
        {
            var operation = mixing.CurrentMixOperation;
            return operation == null
                ? Station(guid, Release1ProductionStationKind.MixingStation, false, "idle", 0, false, mixing.OutputSlots)
                : Station(guid, Release1ProductionStationKind.MixingStation, true,
                    Identity(operation.ProductID), Math.Max(0, mixing.CurrentMixTime),
                    mixing.IsMixingDone, mixing.OutputSlots);
        }

        var cauldron = item.TryCast<Cauldron>();
        if (cauldron != null)
        {
            if (!cauldron.isCooking)
                return Station(guid, Release1ProductionStationKind.Cauldron, false, "idle", 0, false, cauldron.OutputSlots);
            var progress = Release1ProductionStationProgress.CauldronProgress(
                cauldron.CookTime, cauldron.RemainingCookTime,
                Release1ProductionStationProgress.CauldronRemainingCountsDown);
            return Station(guid, Release1ProductionStationKind.Cauldron, true, "cauldron-cook", progress, false, cauldron.OutputSlots);
        }

        var rack = item.TryCast<DryingRack>();
        if (rack != null)
        {
            var operations = rack.DryingOperations;
            if (operations == null || operations.Count == 0)
                return Station(guid, Release1ProductionStationKind.DryingRack, false, "idle", 0, false, rack.OutputSlots);
            var itemIds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var operation in operations)
                if (operation != null && !string.IsNullOrWhiteSpace(operation.ItemID)) itemIds.Add(operation.ItemID);
            if (itemIds.Count == 0)
                return Station(guid, Release1ProductionStationKind.DryingRack, false, "idle", 0, false, rack.OutputSlots);
            // Progress stays zero, like the activity read: DryingOperation
            // has no paired duration, and the identity set alone carries
            // the completion.
            return Station(guid, Release1ProductionStationKind.DryingRack, true, string.Join("|", itemIds), 0, false, rack.OutputSlots);
        }

        return null;
    }

    private static Chapter3DemandStationSnapshot Station(
        string guid,
        Release1ProductionStationKind kind,
        bool running,
        string identity,
        int progress,
        bool finishing,
        Il2CppSystem.Collections.Generic.List<NativeItemSlot>? outputs) =>
        new(guid, kind, running, identity, progress, finishing, ReadOutputs(outputs));

    private static IReadOnlyList<Chapter3DemandOutputSlot> ReadOutputs(
        Il2CppSystem.Collections.Generic.List<NativeItemSlot>? outputs)
    {
        if (outputs == null) return Array.Empty<Chapter3DemandOutputSlot>();
        var read = new List<Chapter3DemandOutputSlot>(outputs.Count);
        for (var index = 0; index < outputs.Count; index++)
        {
            var slot = outputs[index];
            if (slot == null || slot.Quantity <= 0 || slot.ItemInstance == null)
            {
                read.Add(new Chapter3DemandOutputSlot(index, string.Empty, 0));
                continue;
            }
            var definition = slot.ItemInstance.Definition;
            var productId = definition == null ? null : definition.ID;
            if (string.IsNullOrWhiteSpace(productId))
            {
                read.Add(new Chapter3DemandOutputSlot(index, string.Empty, 0));
                continue;
            }
            read.Add(new Chapter3DemandOutputSlot(index, productId, Math.Max(0, slot.Quantity)));
        }
        return read;
    }

    private static string Identity(string? value) => string.IsNullOrWhiteSpace(value) ? "unnamed" : value;

    public Release1SmallCourtesyWorldReadStatus TryReadProductInfo(
        string productId, out Chapter3DemandWorldProductInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(productId))
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        foreach (var product in ProductManager.DiscoveredProducts ?? Array.Empty<ProductDefinition>())
        {
            if (product is null) continue;
            try
            {
                if (!string.Equals(product.ID, productId, StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(product.Name) || !float.IsFinite(product.Price) || product.Price < 0f)
                    return Release1SmallCourtesyWorldReadStatus.Unavailable;
                var drugType = product.PrimaryDrugType.ToString();
                if (string.IsNullOrWhiteSpace(drugType))
                    return Release1SmallCourtesyWorldReadStatus.Unavailable;
                info = new Chapter3DemandWorldProductInfo(
                    product.ID, product.Name, drugType, Math.Max(0, (int)product.Price));
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            catch (Exception exception)
            {
                _log($"Chapter 3 demand product read held: {exception.GetType().Name}");
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            }
        }
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadPackagingUnits(string packagingId, out int units)
    {
        units = 0;
        if (string.IsNullOrWhiteSpace(packagingId))
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        try
        {
            var raw = Il2CppScheduleOne.Registry.GetItem(packagingId);
            var definition = raw == null
                ? null
                : raw.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>();
            if (definition == null || definition.Quantity < 1)
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            units = definition.Quantity;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand packaging read held: {exception.GetType().Name}");
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadFamilyLockerSlots(
        out IReadOnlyList<Chapter3DemandLockerSlot> slots)
    {
        slots = Array.Empty<Chapter3DemandLockerSlot>();
        if (_holdRoomStorage is null)
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        var definition = SyndicateHqStorageContract.Definitions.FirstOrDefault(
            candidate => string.Equals(candidate.Name, FamilyLockerDefinitionName, StringComparison.Ordinal));
        if (definition is null)
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        Release1HoldRoomSnapshot room;
        try { room = _holdRoomStorage.ReadHoldRoom(); }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand locker read held: {exception.GetType().Name}");
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        if (room.Readiness != Release1HoldRoomReadiness.Ready)
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        var closet = room.Closets.FirstOrDefault(
            candidate => string.Equals(candidate.ClosetGuid, definition.Guid.ToString("D"), StringComparison.OrdinalIgnoreCase));
        if (closet is null)
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        var read = new List<Chapter3DemandLockerSlot>(closet.Slots.Count);
        foreach (var slot in closet.Slots)
        {
            if (slot is null) continue;
            var unitsPerPackage = 0;
            if (!string.IsNullOrWhiteSpace(slot.ProductId) && slot.Quantity > 0)
            {
                if (!slot.IsPackaged)
                {
                    unitsPerPackage = 1;
                }
                else if (TryReadPackagingUnits(slot.PackagingId ?? string.Empty, out var units) ==
                    Release1SmallCourtesyWorldReadStatus.Ready)
                {
                    unitsPerPackage = units;
                }
            }
            read.Add(new Chapter3DemandLockerSlot(
                slot.SlotIndex, slot.ProductId ?? string.Empty, Math.Max(0, slot.Quantity),
                slot.IsPackaged, slot.PackagingId ?? string.Empty, unitsPerPackage));
        }
        slots = read;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetFamilyLockerSlotLocked(int slotIndex, bool locked)
    {
        if (!IsHost() || _holdRoomStorage is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var guid = FamilyLockerGuid();
        if (guid is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try { return _holdRoomStorage.TrySetClosetSlotLocked(guid, slotIndex, locked); }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand locker lock held: {exception.GetType().Name}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeFamilyLockerSlotQuantity(int slotIndex, int amount)
    {
        if (!IsHost() || _holdRoomStorage is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var guid = FamilyLockerGuid();
        if (guid is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try { return _holdRoomStorage.TryChangeClosetSlotQuantity(guid, slotIndex, amount); }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand locker change held: {exception.GetType().Name}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryEnsureFamilyLockerName()
    {
        if (!IsHost() || _holdRoomStorage is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var guid = FamilyLockerGuid();
        if (guid is null)
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try { return _holdRoomStorage.TrySetClosetStorageName(guid, Chapter3DemandPolicy.FamilyLockerName); }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand locker rename held: {exception.GetType().Name}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public bool TryReadHqDoor(out float x, out float y, out float z)
    {
        x = 0f; y = 0f; z = 0f;
        try
        {
            var door = _hqDoor?.Invoke();
            if (door is null || !float.IsFinite(door.X) || !float.IsFinite(door.Y) || !float.IsFinite(door.Z))
                return false;
            x = door.X; y = door.Y; z = door.Z;
            return true;
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 demand HQ door read held: {exception.GetType().Name}");
            return false;
        }
    }

    private static string? FamilyLockerGuid() =>
        SyndicateHqStorageContract.Definitions.FirstOrDefault(
            candidate => string.Equals(candidate.Name, FamilyLockerDefinitionName, StringComparison.Ordinal))?.Guid.ToString("D");

    private bool IsHost()
    {
        try { return _isHost?.Invoke() ?? false; }
        catch { return false; }
    }
}
