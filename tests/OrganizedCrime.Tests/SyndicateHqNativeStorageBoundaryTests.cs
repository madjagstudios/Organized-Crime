using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqNativeStorageBoundaryTests
{
    // Normalised to LF so the multi-line expectations below match on any checkout, not only a
    // CRLF-converting Windows one.
    private static readonly string Source = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqNativeStorageBoundary.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Boundary_contract_prepares_a_grid_instead_of_resolving_a_supplier_storage_template()
    {
        Assert.Null(typeof(OrganizedCrime.Runtime.ISyndicateHqNativeStorageBoundary).GetMethod("TryResolveTemplate"));
        Assert.NotNull(typeof(OrganizedCrime.Runtime.ISyndicateHqNativeStorageBoundary).GetMethod("TryPrepareGrid"));
    }

    [Fact]
    public void Boundary_places_the_complete_vanilla_huge_storage_closet_on_a_native_grid()
    {
        Assert.Contains("PlaceableStorageEntity", Source, StringComparison.Ordinal);
        Assert.Contains("BuildManager.CreateGridItem", Source, StringComparison.Ordinal);
        Assert.Contains("ItemManager.GetDefinition", Source, StringComparison.Ordinal);
        Assert.Contains("Il2CppScheduleOne.Tiles", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplierLocation", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldStorageEntity", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerManager.Spawn", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_avoids_manual_inventory_and_alternate_storage_surrogates()
    {
        Assert.DoesNotContain("SupplierLocation", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldStorageEntity", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearContents", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertItem", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStoredInstance", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageInstance", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_grid_identity_and_parent_are_assigned_before_activation()
    {
        var identity = Source.IndexOf("TrySetMember(prepared!, \"_guid\", GridGuid)", StringComparison.Ordinal);
        var parent = Source.IndexOf("clone.transform.SetParent(parentProperty.transform, true)", StringComparison.Ordinal);
        var activate = Source.IndexOf("clone.SetActive(true)", StringComparison.Ordinal);
        var destroyStaging = Source.IndexOf("DestroyImmediate(staging)", StringComparison.Ordinal);

        Assert.True(identity >= 0);
        Assert.True(parent > identity);
        Assert.True(activate > parent);
        Assert.True(destroyStaging > activate);
    }

    [Fact]
    public void Boundary_uses_one_native_build_call_and_never_manually_spawns_the_closet()
    {
        Assert.Equal(1, Source.Split("BuildManager.CreateGridItem", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("ServerManager.Spawn", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_closet_remains_registered_with_its_native_parent_property()
    {
        Assert.DoesNotContain("BuildableItems?.Remove(closet)", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("_closets.TryGetValue", Source, StringComparison.Ordinal);
    }

    // OrganizedCrime.Runtime.SyndicateHqNativeStorageBoundary cannot be exercised behaviorally in this
    // test project: even constructing it runs a static field initializer that builds a UnityEngine
    // .Vector3, which is not available outside a running Unity/IL2Cpp process (see
    // SyndicateHqUnityNullOverloadTests.cs for the established pattern). These four guards are
    // source-scan regression guards instead, the same pattern the rest of this file already uses.
    [Fact]
    public void An_unresolved_definition_returns_unavailable_with_a_reason()
    {
        Assert.Contains(
            "if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))\n" +
            "                return Release1SmallCourtesyWorldReadStatus.Unavailable;",
            Source, StringComparison.Ordinal);
        Assert.Contains(
            "if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))\n" +
            "                return Release1SmallCourtesyWorldMutationStatus.Unavailable;",
            Source, StringComparison.Ordinal);
        Assert.Contains(
            "if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))\n" +
            "            return Release1SmallCourtesyWorldMutationStatus.Unavailable;",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decrement_without_an_owned_lock_returns_rejected()
    {
        Assert.Contains(
            "if (!_ownedClosetSlotLocks.Contains(key))\n" +
            "        {\n" +
            "            reason = $\"Closet '{definition.Name}' slot {slotIndex} was not locked by this mod.\";\n" +
            "            return Release1SmallCourtesyWorldMutationStatus.Rejected;\n" +
            "        }",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_finite_zero_positive_or_past_maximum_decrement_amount_returns_rejected()
    {
        // Checked before TryResolveClosetSlot, so this guard never touches a native slot either.
        Assert.Contains(
            "if (!float.IsFinite(amount) || amount >= 0f || amount < -S1ApiRelease1SmallCourtesyWorld.MaximumCashDecrement)\n" +
            "        {\n" +
            "            reason = $\"Closet '{definition.Name}' slot {slotIndex} decrement {amount} was out of bounds.\";\n" +
            "            return Release1SmallCourtesyWorldMutationStatus.Rejected;\n" +
            "        }",
            Source, StringComparison.Ordinal);
        var lockCheckIndex = Source.IndexOf("if (!_ownedClosetSlotLocks.Contains(key))", StringComparison.Ordinal);
        var amountCheckIndex = Source.IndexOf("if (!float.IsFinite(amount) || amount >= 0f", StringComparison.Ordinal);
        var resolveCallIndex = Source.IndexOf(
            "if (!TryResolveClosetSlot(definition, slotIndex, out var slot, out reason))\n            return Release1SmallCourtesyWorldMutationStatus.Unavailable;",
            StringComparison.Ordinal);
        Assert.True(lockCheckIndex >= 0 && amountCheckIndex > lockCheckIndex && resolveCallIndex > amountCheckIndex,
            "the amount guard must run after the lock guard and before the native slot is ever resolved.");
    }

    [Fact]
    public void Release_owned_closet_slot_locks_empties_the_owned_set_even_when_a_slot_no_longer_resolves()
    {
        Assert.Contains(
            "public void ReleaseOwnedClosetSlotLocks()\n" +
            "    {\n" +
            "        foreach (var (guid, slotIndex) in _ownedClosetSlotLocks.ToArray())\n" +
            "        {\n" +
            "            var definition = SyndicateHqStorageContract.Definitions.FirstOrDefault(candidate => candidate.Guid == guid);\n" +
            "            if (definition is not null && TryResolveClosetSlot(definition, slotIndex, out var slot, out _))\n" +
            "                try { slot.SetIsRemovalLocked(false); slot.SetIsAddLocked(false); } catch { }\n" +
            "            _ownedClosetSlotLocks.Remove((guid, slotIndex));\n" +
            "        }\n" +
            "    }",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_declares_a_settable_timing_receipts_property_defaulting_to_disabled()
    {
        Assert.Contains(
            "public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_restored_closet_scans_at_most_once_per_load_and_is_timed()
    {
        // Final review fix (finding 1): FindRestoredCloset itself must never run the full scene scan;
        // it only ever reads _closets (populated once per load by ScanAndRetainClosets) and the
        // _conflictedClosetGuids set that scan fills alongside it.
        var source = Source;
        var start = source.IndexOf("public SyndicateHqNativeStorageLookupStatus FindRestoredCloset(", StringComparison.Ordinal);
        var end = source.IndexOf("private void ScanAndRetainClosets()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "FindRestoredCloset could not be sliced.");
        var body = source[start..end];

        Assert.Contains("if (!_closetsScannedForLoad) ScanAndRetainClosets();", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources.FindObjectsOfTypeAll<PlaceableStorageEntity>()", body, StringComparison.Ordinal);
        Assert.Contains("Timing.Measure($\"hq/closet-resolve/", body, StringComparison.Ordinal);
        Assert.Contains("_closets.GetValueOrDefault(definition.Guid)", body, StringComparison.Ordinal);
        Assert.Contains("IsUnityNull(retained)", body, StringComparison.Ordinal);

        // Finding 2: a retained-cache hit still runs PrepareCompleteCloset before validation, exactly
        // like the scan path does, so a reused closet is renamed, culling-guard registered and
        // deactivated for the load window every time, not only on the load that first discovers it.
        var retainedIndex = body.IndexOf("_closets.GetValueOrDefault(definition.Guid)", StringComparison.Ordinal);
        var prepareIndex = body.IndexOf("PrepareCompleteCloset(retained, definition, enabled: false)", StringComparison.Ordinal);
        var validateIndex = body.IndexOf("TryValidate(retained, definition, out localReason)", StringComparison.Ordinal);
        Assert.True(retainedIndex >= 0 && prepareIndex > retainedIndex && validateIndex > prepareIndex,
            "a retained-cache hit must be prepared (renamed, culling-guard registered, deactivated) before it is validated.");
    }

    [Fact]
    public void Scan_and_retain_closets_takes_exactly_one_scene_pass_mapped_by_guid_for_every_definition()
    {
        // Final review fix (finding 1). OC-63's own recommended fix (OC-63.md line 113): one
        // Resources.FindObjectsOfTypeAll pass mapped by GUID for all nine definitions, kept per load,
        // in place of the nine separate per-definition scans this used to cost every load boundary.
        var source = Source;
        var start = source.IndexOf("private void ScanAndRetainClosets()", StringComparison.Ordinal);
        var end = source.IndexOf("public bool TryCreate(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "ScanAndRetainClosets could not be sliced.");
        var body = source[start..end];

        Assert.Equal(1, body.Split("Resources.FindObjectsOfTypeAll<PlaceableStorageEntity>()", StringSplitOptions.None).Length - 1);
        Assert.Contains("_closetsScannedForLoad = true;", body, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqStorageContract.Definitions.Select(candidate => candidate.Guid)", body, StringComparison.Ordinal);
        Assert.Contains("GroupBy(storage =>", body, StringComparison.Ordinal);
        Assert.Contains("_conflictedClosetGuids.Add(group.Key)", body, StringComparison.Ordinal);
        Assert.Contains("_closets[group.Key] = matches[0];", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Evict_retained_closets_for_load_clears_the_retained_cache_and_the_scanned_flag_so_the_next_resolve_rescans()
    {
        // Final review fix (finding 1, superseding the prior final-review fix at finding 5): the
        // load-boundary eviction hook must clear _closets, _conflictedClosetGuids, and the
        // _closetsScannedForLoad flag that gates ScanAndRetainClosets, so the next FindRestoredCloset
        // call that load is forced through one fresh scene scan instead of trusting a possibly-stale
        // retained reference from a prior load.
        var source = Source;
        var start = source.IndexOf("public void EvictRetainedClosetsForLoad()", StringComparison.Ordinal);
        var end = source.IndexOf("public SyndicateHqNativeStorageLookupStatus FindRestoredCloset(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "EvictRetainedClosetsForLoad could not be sliced.");
        var body = source[start..end];

        Assert.Contains("_closets.Clear();", body, StringComparison.Ordinal);
        Assert.Contains("_conflictedClosetGuids.Clear();", body, StringComparison.Ordinal);
        Assert.Contains("_closetsScannedForLoad = false;", body, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "OrganizedCrime")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
