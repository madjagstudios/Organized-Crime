using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using OrganizedCrime.Model;
using S1API.Building;
using S1API.Items;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public enum SyndicateHqNativeStorageLookupStatus
{
    Missing,
    Restored,
    IdentityConflict
}

public interface ISyndicateHqNativeStorageBoundary
{
    bool TryPrepareGrid(out object grid, out string reason);
    void EvictRetainedClosetsForLoad();
    SyndicateHqNativeStorageLookupStatus FindRestoredCloset(SyndicateHqStorageDefinition definition, out object entity, out string reason);
    bool TryCreate(object grid, SyndicateHqStorageDefinition definition, out object entity, out string reason);
    bool TryValidate(object entity, SyndicateHqStorageDefinition definition, out string reason);
    bool TryReadClosetSlots(
        SyndicateHqStorageDefinition definition,
        out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots,
        out string reason);
    Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(
        SyndicateHqStorageDefinition definition, int slotIndex, out float balance, out string reason);
    Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(
        SyndicateHqStorageDefinition definition, int slotIndex, bool locked, out string reason);
    Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(
        SyndicateHqStorageDefinition definition, int slotIndex, float amount, out string reason);
    void ReleaseOwnedClosetSlotLocks();
    bool TryPlace(object entity, SyndicateHqVector3 worldPosition, float yawDegrees, out string reason);
    void SetInteractionEnabled(object entity, bool enabled);
    bool TryCleanup(object entity, out string reason);
}

public sealed class SyndicateHqNativeStorageBoundary : ISyndicateHqNativeStorageBoundary
{
    private const string GridRootName = "OC_SyndicateHQ_StorageGrid";
    private const string GridGuid = "7ce4e626-4438-48c4-a88a-c1b47f99d66f";
    private const int GridWidth = SyndicateHqStorageGridContract.Width;
    private const int GridDepth = SyndicateHqStorageGridContract.Depth;
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 PocketRoot = new(956.253998f, -4.000184f, 1156.47406f);

    private readonly Dictionary<Guid, PlaceableStorageEntity> _closets = new();
    private readonly HashSet<Guid> _conflictedClosetGuids = new();
    private bool _closetsScannedForLoad;
    private readonly HashSet<(Guid Guid, int SlotIndex)> _ownedClosetSlotLocks = new();
    private const float ClosetCashBalanceTolerance = 0.01f;
    private GameObject? _gridProxy;
    private Grid? _grid;
    private Property? _parentProperty;

    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    public bool TryPrepareGrid(out object grid, out string reason)
    {
        grid = null!;
        try
        {
            if (IsPreparedGrid(_grid))
            {
                grid = _grid!;
                reason = "Retained native HQ Grid is ready.";
                return true;
            }

            _grid = null;
            _gridProxy = null;
            _parentProperty = null;

            var existing = FindGridByGuid(GridGuid);
            if (IsPreparedGrid(existing))
            {
                RetainGrid(existing!);
                grid = existing!;
                reason = "Adopted the existing native HQ Grid.";
                return true;
            }
            if (IsLegacyGrid(existing))
            {
                if (!TryUpgradeLegacyGrid(existing!, out reason)) return false;
                RetainGrid(existing!);
                grid = existing!;
                return true;
            }
            if (!IsUnityNull(existing))
            {
                reason = "An existing HQ Grid used the reserved GUID but failed its Property or tile validation.";
                return false;
            }

            var template = FindLargestOwnedGrid(out var sourceTileCount);
            var parentProperty = template?.ParentProperty;
            var sourceTile = template?.GetComponentsInChildren<Tile>(includeInactive: true)
                .FirstOrDefault(tile => !IsUnityNull(tile) && !IsUnityNull(tile.gameObject));
            if (IsUnityNull(template) || IsUnityNull(parentProperty) || IsUnityNull(sourceTile))
            {
                reason = "No owned vanilla Property Grid with a native Tile was available.";
                return false;
            }

            GameObject? staging = null;
            GameObject? clone = null;
            Grid? prepared = null;
            try
            {
                staging = new GameObject(GridRootName + "_Staging");
                staging.transform.SetParent(parentProperty!.transform, false);
                staging.SetActive(false);

                clone = UnityEngine.Object.Instantiate(template!.gameObject, staging.transform, false);
                clone.name = GridRootName;
                clone.SetActive(false);
                prepared = clone.GetComponent<Grid>();
                if (IsUnityNull(prepared))
                    throw new InvalidOperationException("The owned Grid clone lost its Grid component.");

                if (!TrySetMember(prepared!, "_guid", GridGuid) ||
                    !TrySetMember(prepared!, "_parentProperty", parentProperty!))
                    throw new InvalidOperationException("The HQ Grid identity could not be assigned before Awake.");

                ReplaceTilesWithDeterministicFootprint(clone, prepared!, sourceTile!);
                DisableProxyVisualsAndColliders(clone);
                PositionGrid(clone.transform);
                clone.transform.SetParent(parentProperty.transform, true);

                clone.SetActive(true);
                var tiles = clone.GetComponentsInChildren<Tile>(includeInactive: true)
                    .Where(tile => !IsUnityNull(tile))
                    .ToArray();
                RebuildNativeTopology(prepared!, tiles);
                RegisterPropertyGrid(parentProperty!, prepared!);
                if (!IsPreparedGrid(prepared))
                    throw new InvalidOperationException("The native HQ Grid failed its GUID, Property, or tile validation.");

                RetainGrid(prepared!);
                clone = null;
                UnityEngine.Object.DestroyImmediate(staging);
                staging = null;
                Physics.SyncTransforms();
                grid = prepared!;
                reason = $"Prepared a hidden {GridWidth}x{GridDepth} native HQ Grid from an owned Property Grid ({sourceTileCount} source tiles).";
                return true;
            }
            catch
            {
                if (!IsUnityNull(prepared) && !IsUnityNull(parentProperty?.Grids)) parentProperty!.Grids.Remove(prepared);
                if (!IsUnityNull(clone)) UnityEngine.Object.DestroyImmediate(clone);
                if (!IsUnityNull(staging)) UnityEngine.Object.DestroyImmediate(staging);
                throw;
            }
        }
        catch (Exception ex)
        {
            reason = $"Native HQ Grid preparation threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// OC-70 final review fix (finding 1). Evicts every retained closet identity, and the
    /// <see cref="_closetsScannedForLoad"/> flag that gates the one scene scan
    /// <see cref="ScanAndRetainClosets"/> takes per load, so the next <see cref="FindRestoredCloset"/>
    /// call is forced through a fresh scan of the whole scene (the per-GUID duplicate-match check
    /// included), instead of trusting a possibly-stale retained reference. Called exactly once per
    /// vanilla load boundary, by <see cref="SyndicateHqNativeStorageRuntime.CompleteVanillaLoadBoundary"/>
    /// (a method that, unlike this boundary's other members, is only ever reached from the mod's real
    /// vanilla load path, never from a save), so a vanilla restore that produces a second in-scene
    /// object for a GUID the mod already retained from a prior boundary is still caught. Retention
    /// resumes immediately after: the forced fresh scan that follows repopulates <see cref="_closets"/>
    /// for every one of the nine definitions in one pass, so every other resolve within the same load
    /// (including every save boundary until the next load) keeps the OC-70 performance win untouched.
    /// </summary>
    public void EvictRetainedClosetsForLoad()
    {
        _closets.Clear();
        _conflictedClosetGuids.Clear();
        _closetsScannedForLoad = false;
    }

    public SyndicateHqNativeStorageLookupStatus FindRestoredCloset(
        SyndicateHqStorageDefinition definition, out object entity, out string reason)
    {
        entity = null!;
        var status = SyndicateHqNativeStorageLookupStatus.Missing;
        var localReason = string.Empty;
        PlaceableStorageEntity? localEntity = null;

        Timing.Measure($"hq/closet-resolve/{definition.Name}", () =>
        {
            try
            {
                // OC-70: the underlying native closet is not destroyed by a save or a load; only the
                // mod's own boundary state was being reset every time. OC-70 final review fix (finding
                // 1): the first call after EvictRetainedClosetsForLoad performs one scene scan for all
                // nine definitions at once (ScanAndRetainClosets, OC-63's own recommended fix); every
                // later definition that load, and every resolve on a save boundary until the next load,
                // reads the map that scan filled with no further scan.
                if (!_closetsScannedForLoad) ScanAndRetainClosets();

                if (_conflictedClosetGuids.Contains(definition.Guid))
                {
                    localReason = $"Multiple restored vanilla closets matched {definition.Guid:D}.";
                    status = SyndicateHqNativeStorageLookupStatus.IdentityConflict;
                    return;
                }

                var retained = _closets.GetValueOrDefault(definition.Guid);
                if (retained is null || IsUnityNull(retained))
                {
                    localReason = $"No restored vanilla closet matched {definition.Guid:D}.";
                    return;
                }

                // Finding 2 of the same review: a closet reused from the retained cache still gets
                // renamed, culling-guard registered and deactivated for the load window, exactly like a
                // freshly scanned one does below. The culling guard itself is reset every load cycle
                // (SyndicateHqStorageLoadPatch.BeginLoadCycle), so skipping this on a cache hit would
                // leave a reused closet out of the guard's registry for the new load.
                PrepareCompleteCloset(retained, definition, enabled: false);
                if (!TryValidate(retained, definition, out localReason))
                {
                    status = SyndicateHqNativeStorageLookupStatus.IdentityConflict;
                    return;
                }
                localEntity = retained;
                localReason = $"Retained the restored vanilla closet '{definition.Name}' across the boundary.";
                status = SyndicateHqNativeStorageLookupStatus.Restored;
            }
            catch (Exception ex)
            {
                localReason = $"Restored vanilla closet lookup threw {ex.GetType().Name}: {ex.Message}";
                status = SyndicateHqNativeStorageLookupStatus.IdentityConflict;
            }
        });

        entity = localEntity!;
        reason = localReason;
        return status;
    }

    /// <summary>
    /// OC-70 final review fix (finding 1). One
    /// <c>Resources.FindObjectsOfTypeAll&lt;PlaceableStorageEntity&gt;()</c> pass over the whole scene,
    /// grouped by parsed GUID and restricted to the nine <see cref="SyndicateHqStorageContract.Definitions"/>
    /// GUIDs, replacing the nine separate per-definition scans <see cref="FindRestoredCloset"/> used to
    /// run every load boundary (OC-63's own recommended fix, OC-63.md line 113). A GUID with more than
    /// one live match is recorded in <see cref="_conflictedClosetGuids"/> so <see cref="FindRestoredCloset"/>
    /// still reports IdentityConflict for it, the same outcome the old per-definition
    /// <c>matches.Length != 1</c> check produced. A definition whose GUID this pass never sees is simply
    /// absent from <see cref="_closets"/>, which <see cref="FindRestoredCloset"/> reads as Missing so
    /// <see cref="TryCreate"/> can spawn it. Sets <see cref="_closetsScannedForLoad"/> first so a
    /// throw partway through this method still leaves the flag set and does not retry the scan for the
    /// remaining definitions this load.
    /// </summary>
    private void ScanAndRetainClosets()
    {
        _closetsScannedForLoad = true;
        var wanted = SyndicateHqStorageContract.Definitions.Select(candidate => candidate.Guid).ToHashSet();
        var groups = Resources.FindObjectsOfTypeAll<PlaceableStorageEntity>()
            .Where(storage => !IsUnityNull(storage) && !IsUnityNull(storage.gameObject) &&
                Guid.TryParse(storage.GUID.ToString(), out var guid) && wanted.Contains(guid))
            .GroupBy(storage => Guid.Parse(storage.GUID.ToString()));
        foreach (var group in groups)
        {
            var matches = group.ToArray();
            if (matches.Length != 1) { _conflictedClosetGuids.Add(group.Key); continue; }
            _closets[group.Key] = matches[0];
        }
    }

    public bool TryCreate(object grid, SyndicateHqStorageDefinition definition, out object entity, out string reason)
    {
        entity = null!;
        GridItem? created = null;
        try
        {
            if (grid is not Grid nativeGrid || nativeGrid != _grid || !IsPreparedGrid(nativeGrid))
            {
                reason = "The complete vanilla closet was not given the retained native HQ Grid.";
                return false;
            }

            var itemDefinition = ItemManager.GetDefinition(definition.ItemId);
            if (itemDefinition is null)
            {
                reason = $"Vanilla item '{definition.ItemId}' was unavailable.";
                return false;
            }

            var gameObject = BuildManager.CreateGridItem(
                itemDefinition.CreateInstance(),
                nativeGrid,
                CoordinateFor(definition),
                NormalizeGridRotation(definition.YawDegrees),
                definition.Guid.ToString("D"));
            created = gameObject?.GetComponent<GridItem>();
            var storage = gameObject?.GetComponent<PlaceableStorageEntity>();
            if (IsUnityNull(created) || IsUnityNull(storage))
            {
                reason = "Vanilla placement did not produce a complete PlaceableStorageEntity prefab.";
                RollbackCreated(created);
                return false;
            }

            PrepareCompleteCloset(storage!, definition, enabled: false);
            if (!TryValidate(storage!, definition, out reason))
            {
                RollbackCreated(created);
                return false;
            }

            _closets[definition.Guid] = storage!;
            entity = storage!;
            reason = $"Created standard vanilla closet '{definition.Name}' on the native HQ Grid.";
            return true;
        }
        catch (Exception ex)
        {
            RollbackCreated(created);
            reason = $"Standard vanilla closet creation threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public bool TryValidate(object entity, SyndicateHqStorageDefinition definition, out string reason)
    {
        try
        {
            if (entity is not PlaceableStorageEntity closet || IsUnityNull(closet) ||
                IsUnityNull(closet.gameObject) || IsUnityNull(closet.StorageEntity) ||
                IsUnityNull(closet.OwnerGrid) || closet.OwnerGrid != _grid ||
                IsUnityNull(closet.ParentProperty) || closet.ParentProperty != _parentProperty ||
                !Guid.TryParse(closet.GUID.ToString(), out var guid) || guid != definition.Guid ||
                closet.StorageEntity.SlotCount != definition.RequiredSlotCount ||
                closet.StorageEntity.ItemSlots is null || closet.StorageEntity.ItemSlots.Count != definition.RequiredSlotCount ||
                closet.AccessPoints is null || closet.AccessPoints.Length == 0 ||
                closet.GetComponentsInChildren<Collider>(includeInactive: true).All(IsUnityNull))
            {
                reason = "The native closet was missing its exact GUID, Grid/Property ownership, 20 slots, access points, or colliders.";
                return false;
            }

            reason = $"Validated complete vanilla closet '{definition.Name}'.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Complete vanilla closet validation threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private const string UnpackagedId = "unpackaged";

    public bool TryReadClosetSlots(
        SyndicateHqStorageDefinition definition,
        out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots,
        out string reason)
    {
        slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        try
        {
            var closet = _closets.GetValueOrDefault(definition.Guid);
            if (closet is null || IsUnityNull(closet))
            {
                if (!TryFindSingleCloset(definition, out closet, out reason)) return false;
            }

            if (IsUnityNull(closet!.StorageEntity) || closet.StorageEntity.ItemSlots is null)
            {
                reason = $"Closet '{definition.Name}' had no readable StorageEntity slots.";
                return false;
            }

            var itemSlots = closet.StorageEntity.ItemSlots;
            var read = new List<Release1SmallCourtesySlotSnapshot>(itemSlots.Count);
            for (var index = 0; index < itemSlots.Count; index++)
            {
                var slot = itemSlots[index];
                if (slot is null)
                {
                    reason = $"Closet '{definition.Name}' slot {index} was null.";
                    return false;
                }
                var item = slot.ItemInstance;
                if (slot.Quantity == 0 || item == null)
                {
                    read.Add(new(index, null, null, 0, false, 0f));
                    continue;
                }

                var productId = item.Definition == null ? null : item.Definition.ID;
                if (string.IsNullOrWhiteSpace(productId))
                {
                    reason = $"Closet '{definition.Name}' slot {index} carried an item with no definition ID.";
                    return false;
                }

                var product = item.TryCast<ProductItemInstance>();
                var packaged = false;
                var packagingId = UnpackagedId;
                var monetaryValue = 0f;
                if (product != null)
                {
                    var applied = product.AppliedPackaging;
                    if (applied != null && !string.IsNullOrWhiteSpace(applied.ID))
                    {
                        packaged = true;
                        packagingId = applied.ID;
                    }
                    else if (!string.IsNullOrWhiteSpace(product.PackagingID) &&
                             !string.Equals(product.PackagingID, "none", StringComparison.Ordinal))
                    {
                        packaged = true;
                        packagingId = product.PackagingID;
                    }
                    monetaryValue = product.GetMonetaryValue();
                    if (!float.IsFinite(monetaryValue) || monetaryValue < 0f) monetaryValue = 0f;
                }

                var snapshot = new Release1SmallCourtesySlotSnapshot(index, productId, packagingId, slot.Quantity, packaged, monetaryValue);
                snapshot.Validate();
                read.Add(snapshot);
            }

            slots = read;
            reason = $"Read {read.Count} slots from closet '{definition.Name}'.";
            return true;
        }
        catch (Exception ex)
        {
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            reason = $"Closet slot read threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// The exact single match lookup <see cref="TryReadClosetSlots"/> has always performed, lifted out
    /// so <see cref="TryResolveClosetSlot"/> shares it: Resources.FindObjectsOfTypeAll&lt;PlaceableStorageEntity&gt;()
    /// filtered to a parsed GUID equal to definition.Guid, Take(2); anything other than one match fails
    /// with a counted reason.
    /// </summary>
    private static bool TryFindSingleCloset(SyndicateHqStorageDefinition definition, out PlaceableStorageEntity closet, out string reason)
    {
        closet = null!;
        try
        {
            var matches = Resources.FindObjectsOfTypeAll<PlaceableStorageEntity>()
                .Where(storage => !IsUnityNull(storage) && !IsUnityNull(storage.gameObject) &&
                    Guid.TryParse(storage.GUID.ToString(), out var guid) && guid == definition.Guid)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                reason = $"Exactly one closet did not resolve for {definition.Guid:D}; found {matches.Length}.";
                return false;
            }
            closet = matches[0];
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            // An unresolved definition is Unavailable, not Faulted or Ambiguous, whether the lookup
            // cleanly found zero or multiple matches or the lookup itself threw (for example, when no
            // live Unity/IL2CPP process backs it): either way the closet was not resolved.
            closet = null!;
            reason = $"Closet lookup for {definition.Guid:D} threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(
        SyndicateHqStorageDefinition definition, int slotIndex, out float balance, out string reason)
    {
        balance = 0f;
        try
        {
            if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            var item = slot.Quantity == 0 ? null : slot.ItemInstance;
            var cash = item == null ? null : item.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
            if (cash == null)
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} was empty or did not hold cash.";
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            }
            balance = cash.Balance;
            reason = $"Closet '{definition.Name}' slot {slotIndex} cash balance {balance}.";
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch (Exception ex)
        {
            balance = 0f;
            reason = $"Closet cash read threw {ex.GetType().Name}: {ex.Message}";
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(
        SyndicateHqStorageDefinition definition, int slotIndex, bool locked, out string reason)
    {
        try
        {
            if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))
                return Release1SmallCourtesyWorldMutationStatus.Unavailable;
            var key = (definition.Guid, slotIndex);
            // The shipped S1ApiRelease1SmallCourtesyWorldAccess.TrySetSlotLocked rules, applied to the
            // closet and reported with a reason: never take a lock the game already holds, never
            // release one this mod does not own, and report Ambiguous if the flags do not take.
            if (locked && !_ownedClosetSlotLocks.Contains(key) && (slot.IsRemovalLocked || slot.IsAddLocked))
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} was already locked by the game.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            if (!locked && !_ownedClosetSlotLocks.Contains(key))
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} was not locked by this mod.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            if (locked) _ownedClosetSlotLocks.Add(key);
            slot.SetIsRemovalLocked(locked);
            slot.SetIsAddLocked(locked);
            reason = $"Closet '{definition.Name}' slot {slotIndex} lock set to {locked}.";
            if (slot.IsRemovalLocked != locked || slot.IsAddLocked != locked)
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            if (!locked) _ownedClosetSlotLocks.Remove(key);
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"Closet slot lock threw {ex.GetType().Name}: {ex.Message}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(
        SyndicateHqStorageDefinition definition, int slotIndex, float amount, out string reason)
    {
        var key = (definition.Guid, slotIndex);
        if (!_ownedClosetSlotLocks.Contains(key))
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} was not locked by this mod.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        // Checked before any slot resolution, exactly like the facade's own bound check, so an
        // out-of-bound amount is rejected without ever touching a native slot.
        if (!float.IsFinite(amount) || amount >= 0f || amount < -S1ApiRelease1SmallCourtesyWorld.MaximumCashDecrement)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} decrement {amount} was out of bounds.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var item = slot.ItemInstance;
        var cash = item == null ? null : item.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
        if (cash == null)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} did not hold cash.";
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }
        var preBalance = cash.Balance;
        if (preBalance + amount < 0f)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} decrement {amount} would leave a negative balance.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        try
        {
            slot.SetIsRemovalLocked(false);
            if (slot.IsRemovalLocked)
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} would not unlock for the change.";
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            }
            cash.ChangeBalance(amount);
        }
        finally
        {
            slot.SetIsRemovalLocked(true);
        }
        if (!slot.IsRemovalLocked)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} would not relock after the change.";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }

        var expected = preBalance + amount;
        var postItem = slot.ItemInstance;
        if (expected <= 0f)
        {
            // A decrement to exactly zero has two valid native outcomes: the game may empty the slot
            // entirely, or it may leave a CashInstance behind at a zero balance. Both are accepted as
            // Succeeded with a read-back balance of 0; only a CashInstance surviving at a non-zero
            // balance is a genuine mismatch. Mirrors S1ApiRelease1SmallCourtesyWorldAccess
            // .TryChangeDeadDropSlotCashBalance as it stands after its OC-60 review fix, with
            // ClosetCashBalanceTolerance in place of CashBalanceTolerance.
            if (postItem == null || slot.Quantity == 0)
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} changed by {amount} from {preBalance}.";
                return Release1SmallCourtesyWorldMutationStatus.Succeeded;
            }
            var zeroCash = postItem.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
            if (zeroCash != null && MathF.Abs(zeroCash.Balance) <= ClosetCashBalanceTolerance)
            {
                reason = $"Closet '{definition.Name}' slot {slotIndex} changed by {amount} from {preBalance}.";
                return Release1SmallCourtesyWorldMutationStatus.Succeeded;
            }
            reason = $"Closet '{definition.Name}' slot {slotIndex} did not settle at the expected zero balance after the change.";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
        var postCash = postItem == null ? null : postItem.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
        if (postCash != null && MathF.Abs(postCash.Balance - expected) <= ClosetCashBalanceTolerance)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} changed by {amount} from {preBalance}.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        reason = $"Closet '{definition.Name}' slot {slotIndex} did not settle at the expected balance after the change.";
        return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
    }

    public void ReleaseOwnedClosetSlotLocks()
    {
        foreach (var (guid, slotIndex) in _ownedClosetSlotLocks.ToArray())
        {
            var definition = SyndicateHqStorageContract.Definitions.FirstOrDefault(candidate => candidate.Guid == guid);
            if (definition is not null && TryResolveClosetSlot(definition, slotIndex, out var slot, out _))
                try { slot.SetIsRemovalLocked(false); slot.SetIsAddLocked(false); } catch { }
            _ownedClosetSlotLocks.Remove((guid, slotIndex));
        }
    }

    /// <summary>
    /// The one place a contract closet becomes a native item slot, resolving through the retained
    /// entity first and falling back to the same single match lookup TryReadClosetSlots uses.
    /// </summary>
    private bool TryResolveClosetSlot(
        SyndicateHqStorageDefinition definition,
        int slotIndex,
        out Il2CppScheduleOne.ItemFramework.ItemSlot slot,
        out string reason)
    {
        slot = null!;
        var closet = _closets.GetValueOrDefault(definition.Guid);
        if (closet is null || IsUnityNull(closet))
        {
            if (!TryFindSingleCloset(definition, out closet, out reason)) return false;
        }
        if (IsUnityNull(closet!.StorageEntity) || closet.StorageEntity.ItemSlots is null ||
            slotIndex < 0 || slotIndex >= closet.StorageEntity.ItemSlots.Count ||
            closet.StorageEntity.ItemSlots[slotIndex] is null)
        {
            reason = $"Closet '{definition.Name}' slot {slotIndex} was out of range, null, or unreadable.";
            return false;
        }
        slot = closet.StorageEntity.ItemSlots[slotIndex];
        reason = string.Empty;
        return true;
    }

    public bool TryPlace(object entity, SyndicateHqVector3 worldPosition, float yawDegrees, out string reason)
    {
        if (entity is not PlaceableStorageEntity closet || IsUnityNull(closet) || !worldPosition.IsFinite || !float.IsFinite(yawDegrees))
        {
            reason = "The standard vanilla closet placement inputs were invalid.";
            return false;
        }
        try
        {
            var expectedPosition = new Vector3(worldPosition.X, worldPosition.Y, worldPosition.Z);
            var expectedRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (!IsUnityNull(_grid))
            {
                _grid!.transform.SetParent(null, true);
                _grid.gameObject.SetActive(true);
            }
            if (Guid.TryParse(closet.GUID.ToString(), out var guid))
                SyndicateHqStorageCullingGuard.SetVisible(guid, true);
            closet.SetCulled(false);
            closet.gameObject.SetActive(true);
            closet.transform.SetPositionAndRotation(expectedPosition, expectedRotation);
            Physics.SyncTransforms();

            var positionError = Vector3.Distance(closet.transform.position, expectedPosition);
            var rotationError = Quaternion.Angle(closet.transform.rotation, expectedRotation);
            if (!closet.gameObject.activeInHierarchy || positionError > 0.05f || rotationError > 0.5f)
            {
                reason = $"The standard vanilla closet did not reach its visible HQ transform (active={closet.gameObject.activeInHierarchy}, positionError={positionError:F3}, rotationError={rotationError:F3}).";
                return false;
            }

            reason = "The standard vanilla closet is active at its verified HQ world transform.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Standard vanilla closet activation threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public void SetInteractionEnabled(object entity, bool enabled)
    {
        if (entity is not PlaceableStorageEntity closet || IsUnityNull(closet) || IsUnityNull(closet.gameObject)) return;
        var hasGuid = Guid.TryParse(closet.GUID.ToString(), out var guid);
        SyndicateHqStorageCullingGuard.ApplyVisibility(
            enabled,
            visible =>
            {
                if (hasGuid) SyndicateHqStorageCullingGuard.SetVisible(guid, visible);
            },
            () => closet.SetCulled(false),
            active => closet.gameObject.SetActive(active));
    }

    public bool TryCleanup(object entity, out string reason)
    {
        if (entity is not PlaceableStorageEntity closet || IsUnityNull(closet))
        {
            reason = "The native closet was already unavailable.";
            return true;
        }
        try
        {
            if (Guid.TryParse(closet.GUID.ToString(), out var guid))
            {
                SyndicateHqStorageCullingGuard.SetVisible(guid, false);
                _closets.Remove(guid);
            }
            closet.Destroy_Server();
            reason = "Rolled back the newly-created native closet.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Native closet rollback threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static Vector2 CoordinateFor(SyndicateHqStorageDefinition definition) =>
        new(definition.GridOriginX, definition.GridOriginY);

    private static int NormalizeGridRotation(float yawDegrees)
    {
        var quarterTurns = (int)MathF.Round(yawDegrees / 90f) % 4;
        return quarterTurns < 0 ? quarterTurns + 4 : quarterTurns;
    }

    private void PrepareCompleteCloset(PlaceableStorageEntity closet, SyndicateHqStorageDefinition definition, bool enabled)
    {
        SyndicateHqStorageCullingGuard.SetVisible(definition.Guid, enabled);
        closet.gameObject.name = definition.Name;
        closet.SetCulled(false);
        closet.gameObject.SetActive(enabled);
    }

    private static void RollbackCreated(GridItem? created)
    {
        if (IsUnityNull(created)) return;
        try { created!.Destroy_Server(); } catch { }
    }

    private void RetainGrid(Grid grid)
    {
        _grid = grid;
        _gridProxy = grid.gameObject;
        _parentProperty = grid.ParentProperty;
    }

    private static Grid? FindGridByGuid(string guid) =>
        UnityEngine.Object.FindObjectsOfType<Grid>(includeInactive: true)
            .FirstOrDefault(grid => !IsUnityNull(grid) && string.Equals(grid.GUID.ToString(), guid, StringComparison.OrdinalIgnoreCase));

    private static Grid? FindLargestOwnedGrid(out int tileCount)
    {
        tileCount = 0;
        Grid? best = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<Grid>(includeInactive: true))
        {
            var parent = candidate?.ParentProperty;
            if (IsUnityNull(candidate) || IsUnityNull(candidate?.gameObject) || IsUnityNull(parent) || !parent!.IsOwned) continue;
            var count = candidate!.GetComponentsInChildren<Tile>(includeInactive: true).Length;
            if (count <= tileCount) continue;
            best = candidate;
            tileCount = count;
        }
        return best;
    }

    private bool IsPreparedGrid(Grid? grid)
    {
        return HasReservedIdentityAndOwnedParent(grid) &&
            ClassifyGrid(grid) == SyndicateHqStorageGridShape.Current;
    }

    private bool IsLegacyGrid(Grid? grid) =>
        HasReservedIdentityAndOwnedParent(grid) &&
        ClassifyGrid(grid) == SyndicateHqStorageGridShape.LegacyTwoCloset;

    private static bool HasReservedIdentityAndOwnedParent(Grid? grid) =>
        !IsUnityNull(grid) && !IsUnityNull(grid?.gameObject) && !IsUnityNull(grid?.ParentProperty) &&
        grid!.ParentProperty.IsOwned && string.Equals(grid.GUID.ToString(), GridGuid, StringComparison.OrdinalIgnoreCase);

    private static SyndicateHqStorageGridShape ClassifyGrid(Grid? grid)
    {
        if (IsUnityNull(grid)) return SyndicateHqStorageGridShape.Invalid;
        var coordinates = grid!.GetComponentsInChildren<Tile>(includeInactive: true)
            .Where(tile => !IsUnityNull(tile))
            .Select(tile => new SyndicateHqStorageGridCoordinate(tile.x, tile.y));
        return SyndicateHqStorageGridContract.Classify(coordinates);
    }

    private bool TryUpgradeLegacyGrid(Grid grid, out string reason)
    {
        GameObject? sourceTemplate = null;
        try
        {
            var parentProperty = grid.ParentProperty;
            var sourceTile = grid.GetComponentsInChildren<Tile>(includeInactive: true)
                .FirstOrDefault(tile => !IsUnityNull(tile) && !IsUnityNull(tile.gameObject));
            if (IsUnityNull(parentProperty) || IsUnityNull(sourceTile))
            {
                reason = "The exact legacy HQ Grid did not retain its owned Property or source Tile.";
                return false;
            }

            sourceTemplate = UnityEngine.Object.Instantiate(sourceTile!.gameObject);
            sourceTemplate.name = GridRootName + "_LegacyTileTemplate";
            sourceTemplate.SetActive(false);
            var templateTile = sourceTemplate.GetComponent<Tile>();
            if (IsUnityNull(templateTile))
                throw new InvalidOperationException("The legacy HQ Grid tile template lost its Tile component.");

            grid.gameObject.SetActive(false);
            ReplaceTilesWithDeterministicFootprint(grid.gameObject, grid, templateTile!);
            DisableProxyVisualsAndColliders(grid.gameObject);
            PositionGrid(grid.transform);
            grid.gameObject.SetActive(true);
            var tiles = grid.GetComponentsInChildren<Tile>(includeInactive: true)
                .Where(tile => !IsUnityNull(tile))
                .ToArray();
            RebuildNativeTopology(grid, tiles);
            RegisterPropertyGrid(parentProperty!, grid);
            if (!IsPreparedGrid(grid))
                throw new InvalidOperationException("The expanded native HQ Grid failed its GUID, Property, or tile validation.");

            Physics.SyncTransforms();
            reason = $"Expanded the exact legacy 12x14 HQ Grid in place to {GridWidth}x{GridDepth}; existing closet GUIDs remain unchanged.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Legacy native HQ Grid expansion threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
        finally
        {
            if (!IsUnityNull(sourceTemplate)) UnityEngine.Object.DestroyImmediate(sourceTemplate);
        }
    }

    private static void ReplaceTilesWithDeterministicFootprint(GameObject root, Grid grid, Tile sourceTile)
    {
        var inheritedTiles = root.GetComponentsInChildren<Tile>(includeInactive: true)
            .Where(tile => !IsUnityNull(tile) && !IsUnityNull(tile.gameObject))
            .OrderByDescending(tile => HierarchyDepth(tile.transform))
            .ToArray();
        foreach (var tile in inheritedTiles) UnityEngine.Object.DestroyImmediate(tile.gameObject);

        var tileRoot = new GameObject("OC_SyndicateHQ_StorageTiles");
        tileRoot.transform.SetParent(root.transform, false);
        for (var x = 0; x < GridWidth; x++)
        for (var y = 0; y < GridDepth; y++)
        {
            var tileObject = UnityEngine.Object.Instantiate(sourceTile.gameObject, tileRoot.transform, false);
            tileObject.name = $"Tile_{x}_{y}";
            var first = SyndicateHqStorageContract.Definitions[0];
            tileObject.transform.localPosition = new Vector3(
                (x - first.GridOriginX) * Grid.TileSize,
                0f,
                (y - first.GridOriginY) * Grid.TileSize);
            tileObject.transform.localRotation = Quaternion.identity;
            var tile = tileObject.GetComponent<Tile>() ?? throw new InvalidOperationException("A deterministic HQ tile clone lost its Tile component.");
            tile.x = x;
            tile.y = y;
            tile.OwnerGrid = grid;
            ClearMember(tile, "BuildableOccupants");
            ClearMember(tile, "OccupantTiles");
        }
    }

    private static void PositionGrid(Transform grid)
    {
        var first = SyndicateHqStorageContract.Definitions[0].LocalPosition;
        grid.position = PocketRoot + new Vector3(first.X, first.Y, first.Z);
        grid.rotation = Quaternion.identity;
        grid.localScale = Vector3.one;
    }

    private static void DisableProxyVisualsAndColliders(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true)) if (!IsUnityNull(renderer)) renderer.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(includeInactive: true)) if (!IsUnityNull(collider)) collider.enabled = false;
    }

    private static void RebuildNativeTopology(Grid grid, Tile[] tiles)
    {
        grid.Tiles.Clear();
        grid.CoordinateTilePairs.Clear();
        foreach (var tile in tiles) { tile.OwnerGrid = grid; grid.RegisterTile(tile); }
        var lookup = GetMember(grid, "_coordinateToTile");
        if (!Invoke(lookup, "Clear") || !Invoke(grid, "ProcessCoordinateDataPairs") || !Invoke(grid, "SetGridSize"))
            throw new InvalidOperationException("The deterministic native HQ Grid topology could not be rebuilt.");
        if (grid.Tiles.Count != GridWidth * GridDepth)
            throw new InvalidOperationException($"The native HQ Grid retained {grid.Tiles.Count} tiles instead of {GridWidth * GridDepth}.");
    }

    private static void RegisterPropertyGrid(Property property, Grid grid)
    {
        if (property.Grids is null) throw new InvalidOperationException("The owned parent Property did not expose its Grid registry.");
        if (!property.Grids.Contains(grid)) property.Grids.Add(grid);
    }

    private static int HierarchyDepth(Transform transform)
    {
        var depth = 0;
        for (var current = transform; !IsUnityNull(current); current = current.parent) depth++;
        return depth;
    }

    private static object? GetMember(object target, string name)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, MemberFlags);
        return property is not null ? property.GetValue(target) : type.GetField(name, MemberFlags)?.GetValue(target);
    }

    private static bool TrySetMember(object target, string name, object value)
    {
        try
        {
            var type = target.GetType();
            var property = type.GetProperty(name, MemberFlags);
            if (property is not null && property.CanWrite) { property.SetValue(target, value); return true; }
            var field = type.GetField(name, MemberFlags);
            if (field is null) return false;
            field.SetValue(target, value);
            return true;
        }
        catch { return false; }
    }

    private static bool ClearMember(object target, string name)
    {
        var member = GetMember(target, name);
        return member is not null && Invoke(member, "Clear");
    }

    private static bool Invoke(object? target, string name)
    {
        if (target is null) return false;
        try
        {
            var method = target.GetType().GetMethod(name, MemberFlags, null, Type.EmptyTypes, null);
            if (method is null) return false;
            method.Invoke(target, null);
            return true;
        }
        catch { return false; }
    }

    private static bool IsUnityNull(object? value) => value is null || value == null;
    private static bool IsUnityNull(UnityEngine.Object? value) => value == null;
}
