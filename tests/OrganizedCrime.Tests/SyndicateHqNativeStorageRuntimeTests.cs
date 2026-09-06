using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqNativeStorageRuntimeTests
{
    [Fact]
    public void Only_visible_reserved_hq_closets_reject_a_vanilla_cull_request()
    {
        var hqGuid = SyndicateHqStorageContract.Definitions[0].Guid;
        var unrelatedGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        try
        {
            SyndicateHqStorageCullingGuard.Reset();
            Assert.True(SyndicateHqStorageCullingGuard.Filter(hqGuid, true));

            SyndicateHqStorageCullingGuard.SetVisible(hqGuid, true);

            Assert.False(SyndicateHqStorageCullingGuard.Filter(hqGuid, true));
            Assert.False(SyndicateHqStorageCullingGuard.Filter(hqGuid, false));
            Assert.True(SyndicateHqStorageCullingGuard.Filter(unrelatedGuid, true));

            SyndicateHqStorageCullingGuard.SetVisible(hqGuid, false);
            Assert.True(SyndicateHqStorageCullingGuard.Filter(hqGuid, true));
        }
        finally
        {
            SyndicateHqStorageCullingGuard.Reset();
        }
    }

    [Fact]
    public void Disabling_hq_storage_never_requests_native_unculling_during_teardown()
    {
        var events = new List<string>();
        SyndicateHqStorageCullingGuard.ApplyVisibility(
            false,
            visible => events.Add($"guard:{visible}"),
            () => events.Add("uncull"),
            active => events.Add($"active:{active}"));

        Assert.Equal(new[] { "guard:False", "active:False" }, events);
    }

    [Fact]
    public void Vanilla_load_lifecycle_separates_grid_preparation_from_closet_restoration()
    {
        Assert.NotNull(typeof(ISyndicateHqStorageRuntime).GetMethod("CompleteVanillaLoadBoundary"));
    }

    [Fact]
    public void Native_boundary_can_adopt_a_vanilla_restored_closet_before_creating_a_missing_one()
    {
        var assembly = typeof(ISyndicateHqNativeStorageBoundary).Assembly;
        Assert.NotNull(assembly.GetType("OrganizedCrime.Runtime.SyndicateHqNativeStorageLookupStatus"));
        Assert.NotNull(typeof(ISyndicateHqNativeStorageBoundary).GetMethod("FindRestoredCloset"));
        Assert.Null(typeof(ISyndicateHqNativeStorageBoundary).GetMethod("TryFindRestoredCloset"));
    }

    [Fact]
    public void Prefix_prepares_without_creating_and_postfix_creates_exactly_ten_closets_once()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);

        Assert.Equal(SyndicateHqStorageStatus.NotPrepared, runtime.BeginLoad(ReadyContext()).Status);
        Assert.Equal(SyndicateHqStorageStatus.NotPrepared, runtime.PrepareAtVanillaLoadBoundary().Status);
        Assert.Equal(0, boundary.CreateCalls);
        Assert.Equal(SyndicateHqStorageStatus.Ready, runtime.CompleteVanillaLoadBoundary().Status);
        Assert.Equal(SyndicateHqStorageStatus.Ready, runtime.CompleteVanillaLoadBoundary().Status);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreateCalls);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreatedGuids.Distinct().Count());
    }

    [Fact]
    public void Same_save_reload_reconciles_vanilla_restored_entities_without_recreation()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        var validationsAfterFirstLoad = boundary.ValidateCalls;

        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        var result = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.Ready, result.Status);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreateCalls);
        Assert.Equal(validationsAfterFirstLoad + SyndicateHqStorageContract.Definitions.Count, boundary.ValidateCalls);
    }

    [Fact]
    public void Complete_vanilla_load_boundary_evicts_the_retained_closet_cache_once_per_load_before_resolving()
    {
        // Final review fix (finding 5): a vanilla restore can hand back a second in-scene closet
        // object for the same GUID; evicting the retained cache once per vanilla load boundary forces
        // every definition through FindRestoredCloset's own fresh-scan identity-conflict check that
        // load, instead of trusting a stale retained reference from a prior boundary.
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();

        runtime.CompleteVanillaLoadBoundary();
        Assert.Equal(1, boundary.EvictCalls);

        // A repeat completion attempt on the same load cycle is a no-op past the completion boundary
        // guard, so eviction does not fire again until the next load cycle begins.
        runtime.CompleteVanillaLoadBoundary();
        Assert.Equal(1, boundary.EvictCalls);

        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        Assert.Equal(2, boundary.EvictCalls);
    }

    [Fact]
    public void Partial_creation_is_cleaned_and_not_retried()
    {
        var boundary = new FakeBoundary(failOnCreate: 2);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());

        runtime.PrepareAtVanillaLoadBoundary();
        var first = runtime.CompleteVanillaLoadBoundary();
        var second = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.SpawnUnavailable, first.Status);
        Assert.Equal(first, second);
        Assert.Equal(2, boundary.CreateCalls);
        Assert.Equal(1, boundary.CleanupCalls);
    }

    [Fact]
    public void Standard_closet_creation_uses_the_prepared_vanilla_grid()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());

        runtime.PrepareAtVanillaLoadBoundary();
        var result = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.Ready, result.Status);
        Assert.Equal(1, boundary.ResolveCalls);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreateCalls);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(21)]
    public void Unexpected_slot_count_cleans_up_and_fails_closed(int slots)
    {
        var boundary = new FakeBoundary(slotCount: slots);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());

        runtime.PrepareAtVanillaLoadBoundary();
        var result = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.ValidationFailed, result.Status);
        Assert.Equal(1, boundary.CreateCalls);
        Assert.Equal(1, boundary.CleanupCalls);
    }

    [Fact]
    public void Placement_uses_all_authored_offsets_then_enables_all_interactions()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        var pocket = new SyndicateHqVector3(1000f, 5f, 2000f);

        var result = runtime.PlaceAtPocket(pocket);

        Assert.Equal(SyndicateHqStorageStatus.Ready, result.Status);
        Assert.Equal(
            SyndicateHqStorageContract.Definitions.Select(definition => pocket + definition.LocalPosition),
            boundary.Placements.Select(placement => placement.Position));
        Assert.Equal(
            Enumerable.Repeat(true, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
    }

    [Fact]
    public void Failed_second_placement_disables_every_retained_interaction()
    {
        var boundary = new FakeBoundary(failOnPlace: 2);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        var result = runtime.PlaceAtPocket(new(1000f, 0f, 1000f));

        Assert.Equal(SyndicateHqStorageStatus.ValidationFailed, result.Status);
        Assert.Equal(
            Enumerable.Repeat(false, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
    }

    [Fact]
    public void Changed_save_binding_disables_storage_without_native_creation()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        var changed = runtime.BeginLoad(ReadyContext() with
        {
            ActiveSaveFolder = @"C:\Saves\76561190000000001\SaveGame_3"
        });
        var boundaryResult = runtime.PrepareAtVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.SaveBindingChanged, changed.Status);
        Assert.Equal(SyndicateHqStorageStatus.SaveBindingChanged, boundaryResult.Status);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreateCalls);
        Assert.Equal(
            Enumerable.Repeat(false, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
    }

    [Fact]
    public void Changed_canonical_identity_on_the_same_path_disables_retained_storage()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        var changed = runtime.BeginLoad(ReadyContext() with
        {
            CanonicalPlayerId = "76561197984645368"
        });

        Assert.Equal(SyndicateHqStorageStatus.SaveBindingChanged, changed.Status);
        Assert.Equal(SyndicateHqStorageContract.Definitions.Count, boundary.CreateCalls);
        Assert.Equal(
            Enumerable.Repeat(false, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
    }

    [Fact]
    public void Native_exceptions_are_contained_and_not_retried()
    {
        var boundary = new FakeBoundary(throwOnCreate: true);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());

        runtime.PrepareAtVanillaLoadBoundary();
        var exception = Record.Exception(() => runtime.CompleteVanillaLoadBoundary());
        var second = runtime.CompleteVanillaLoadBoundary();

        Assert.Null(exception);
        Assert.Equal(SyndicateHqStorageStatus.SpawnUnavailable, second.Status);
        Assert.Equal(1, boundary.CreateCalls);
    }

    [Fact]
    public void Second_disable_after_a_successful_placement_makes_no_further_boundary_calls()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        runtime.PlaceAtPocket(new SyndicateHqVector3(1000f, 5f, 2000f));

        runtime.DisableInteractions();
        var countAfterFirstDisable = boundary.InteractionStates.Count;
        runtime.DisableInteractions();

        Assert.Equal(countAfterFirstDisable, boundary.InteractionStates.Count);
    }

    [Fact]
    public void Disable_after_a_second_successful_placement_calls_the_boundary_again()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        runtime.PlaceAtPocket(new SyndicateHqVector3(1000f, 5f, 2000f));
        runtime.DisableInteractions();
        var countAfterFirstDisable = boundary.InteractionStates.Count;

        runtime.PlaceAtPocket(new SyndicateHqVector3(1000f, 5f, 2000f));
        runtime.DisableInteractions();

        Assert.Equal(
            countAfterFirstDisable + 2 * SyndicateHqStorageContract.Definitions.Count,
            boundary.InteractionStates.Count);
        Assert.Equal(
            Enumerable.Repeat(false, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
    }

    [Fact]
    public void Teardown_disable_makes_no_boundary_calls_and_logs_once_when_interactions_were_enabled()
    {
        var boundary = new FakeBoundary();
        var logs = new List<string>();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary, logs.Add);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        runtime.PlaceAtPocket(new SyndicateHqVector3(1000f, 5f, 2000f));
        var countBeforeTeardown = boundary.InteractionStates.Count;

        runtime.MarkTeardown();
        runtime.DisableInteractions();

        Assert.Equal(countBeforeTeardown, boundary.InteractionStates.Count);
        Assert.Equal(
            new[] { $"Native HQ storage interactions left to the game during teardown; count={SyndicateHqStorageContract.Definitions.Count}" },
            logs);
    }

    [Fact]
    public void Teardown_disable_logs_nothing_when_interactions_were_already_disabled()
    {
        var boundary = new FakeBoundary();
        var logs = new List<string>();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary, logs.Add);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        runtime.PlaceAtPocket(new SyndicateHqVector3(1000f, 5f, 2000f));
        runtime.DisableInteractions();
        var countAfterFirstDisable = boundary.InteractionStates.Count;
        logs.Clear();

        runtime.MarkTeardown();
        runtime.DisableInteractions();

        Assert.Equal(countAfterFirstDisable, boundary.InteractionStates.Count);
        Assert.Empty(logs);
    }

    [Fact]
    public void Native_exception_during_a_normal_disable_still_warns_for_every_retained_entity()
    {
        var boundary = new FakeBoundary(throwOnDisable: true);
        var logs = new List<string>();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary, logs.Add);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        runtime.DisableInteractions();

        Assert.Equal(
            SyndicateHqStorageContract.Definitions.Count,
            logs.Count(log => log.Contains("Native HQ storage interaction disable failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Disposal_logs_the_receipt_delegate_and_a_failure_logs_the_warning_delegate()
    {
        var boundary = new FakeBoundary(failOnCreate: 1);
        var warnings = new List<string>();
        var receipts = new List<string>();
        var runtime = new SyndicateHqNativeStorageRuntime(boundary, warnings.Add, receiptLog: receipts.Add);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();

        var failure = runtime.CompleteVanillaLoadBoundary();
        Assert.Equal(SyndicateHqStorageStatus.SpawnUnavailable, failure.Status);
        Assert.Contains(warnings, log => log.StartsWith("Native HQ storage: SpawnUnavailable", StringComparison.Ordinal));
        Assert.Empty(receipts);

        runtime.Dispose();

        Assert.Contains(receipts, log => log.StartsWith("Native HQ storage: Disposed", StringComparison.Ordinal));
    }

    [Fact]
    public void Disposal_falls_back_to_the_warning_delegate_when_no_receipt_delegate_is_supplied()
    {
        var boundary = new FakeBoundary();
        var logs = new List<string>();
        var runtime = new SyndicateHqNativeStorageRuntime(boundary, logs.Add);

        runtime.Dispose();

        Assert.Contains(logs, log => log.StartsWith("Native HQ storage: Disposed", StringComparison.Ordinal));
    }

    [Fact]
    public void Disposal_is_idempotent_and_rejects_all_later_work()
    {
        var boundary = new FakeBoundary();
        var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(SyndicateHqStorageStatus.Disposed, runtime.BeginLoad(ReadyContext()).Status);
        Assert.Equal(SyndicateHqStorageStatus.Disposed, runtime.PrepareAtVanillaLoadBoundary().Status);
        Assert.Equal(
            Enumerable.Repeat(false, SyndicateHqStorageContract.Definitions.Count),
            boundary.InteractionStates.TakeLast(SyndicateHqStorageContract.Definitions.Count));
        Assert.Equal(0, boundary.CleanupCalls);
    }

    [Fact]
    public void Partial_failure_never_cleans_up_a_vanilla_restored_closet()
    {
        var boundary = new FakeBoundary(failOnCreate: 1);
        boundary.SeedRestored(SyndicateHqStorageContract.Definitions[0]);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();

        var result = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.SpawnUnavailable, result.Status);
        Assert.Equal(0, boundary.CleanupCalls);
    }

    [Fact]
    public void Restored_identity_conflict_never_creates_another_closet()
    {
        var boundary = new FakeBoundary();
        boundary.ConflictingGuids.Add(SyndicateHqStorageContract.Definitions[0].Guid);
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();

        var result = runtime.CompleteVanillaLoadBoundary();

        Assert.Equal(SyndicateHqStorageStatus.IdentityConflict, result.Status);
        Assert.Equal(0, boundary.CreateCalls);
        Assert.Equal(0, boundary.CleanupCalls);
    }

    [Fact]
    public void Closet_cash_members_hold_when_the_runtime_is_not_ready()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        var guid = SyndicateHqStorageContract.Definitions[0].Guid.ToString("D");

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, runtime.TryReadClosetSlotCashBalance(guid, 0, out var balance));
        Assert.Equal(0f, balance);
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Unavailable, runtime.TrySetClosetSlotLocked(guid, 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Unavailable, runtime.TryChangeClosetSlotCashBalance(guid, 0, -100f));

        Assert.Empty(boundary.ClosetReadCalls);
        Assert.Empty(boundary.ClosetLockCalls);
        Assert.Empty(boundary.ClosetCashChangeCalls);
    }

    [Fact]
    public void Closet_cash_members_report_unavailable_for_a_guid_that_is_not_a_retained_contract_closet()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        const string unrelatedGuid = "11111111-2222-3333-4444-555555555555";

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, runtime.TryReadClosetSlotCashBalance(unrelatedGuid, 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Unavailable, runtime.TrySetClosetSlotLocked(unrelatedGuid, 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Unavailable, runtime.TryChangeClosetSlotCashBalance(unrelatedGuid, 0, -100f));

        Assert.Empty(boundary.ClosetReadCalls);
        Assert.Empty(boundary.ClosetLockCalls);
        Assert.Empty(boundary.ClosetCashChangeCalls);
    }

    [Fact]
    public void Closet_cash_read_forwards_the_resolved_definition_and_returns_the_boundary_status_and_value()
    {
        var boundary = new FakeBoundary { ClosetCashReadStatus = Release1SmallCourtesyWorldReadStatus.Ready, ClosetCashReadValue = 555f };
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        var definition = SyndicateHqStorageContract.Definitions[0];

        var status = runtime.TryReadClosetSlotCashBalance(definition.Guid.ToString("D"), 4, out var balance);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, status);
        Assert.Equal(555f, balance);
        Assert.Equal(new[] { (definition.Guid, 4) }, boundary.ClosetReadCalls);
    }

    [Fact]
    public void Closet_lock_and_decrement_forward_the_resolved_definition_and_the_exact_amount()
    {
        var boundary = new FakeBoundary();
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        var definition = SyndicateHqStorageContract.Definitions[2];

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, runtime.TrySetClosetSlotLocked(definition.Guid.ToString("D"), 7, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, runtime.TryChangeClosetSlotCashBalance(definition.Guid.ToString("D"), 7, -1000f));

        Assert.Equal(new[] { (definition.Guid, 7, true) }, boundary.ClosetLockCalls);
        Assert.Equal(new[] { (definition.Guid, 7, -1000f) }, boundary.ClosetCashChangeCalls);
    }

    [Fact]
    public void Dispose_releases_every_owned_closet_slot_lock_exactly_once()
    {
        var boundary = new FakeBoundary();
        var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(1, boundary.ReleaseOwnedClosetSlotLocksCalls);
    }

    [Fact]
    public void A_boundary_throw_is_mapped_to_faulted_or_ambiguous_and_never_propagates()
    {
        var boundary = new FakeBoundary { ThrowOnClosetCashRead = true, ThrowOnClosetLock = true, ThrowOnClosetCashChange = true };
        using var runtime = new SyndicateHqNativeStorageRuntime(boundary);
        runtime.BeginLoad(ReadyContext());
        runtime.PrepareAtVanillaLoadBoundary();
        runtime.CompleteVanillaLoadBoundary();
        var guid = SyndicateHqStorageContract.Definitions[0].Guid.ToString("D");

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, runtime.TryReadClosetSlotCashBalance(guid, 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, runtime.TrySetClosetSlotLocked(guid, 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, runtime.TryChangeClosetSlotCashBalance(guid, 0, -100f));
    }

    private static SyndicateHqStorageContext ReadyContext() => new(
        true,
        "76561190000000001",
        @"C:\Saves\76561190000000001\SaveGame_4",
        @"C:\Saves\76561190000000001\SaveGame_4");

    private sealed record FakeEntity(Guid Guid);

    private sealed class FakeBoundary : ISyndicateHqNativeStorageBoundary
    {
        private readonly bool _gridAvailable;
        private readonly int _slotCount;
        private readonly int _failOnCreate;
        private readonly int _failOnPlace;
        private readonly bool _throwOnCreate;
        private readonly bool _throwOnDisable;
        private readonly Dictionary<Guid, FakeEntity> _restoredByGuid = new();

        public FakeBoundary(
            bool gridAvailable = true,
            int slotCount = SyndicateHqStorageContract.SlotCount,
            int failOnCreate = -1,
            int failOnPlace = -1,
            bool throwOnCreate = false,
            bool throwOnDisable = false)
        {
            _gridAvailable = gridAvailable;
            _slotCount = slotCount;
            _failOnCreate = failOnCreate;
            _failOnPlace = failOnPlace;
            _throwOnCreate = throwOnCreate;
            _throwOnDisable = throwOnDisable;
        }

        public int ResolveCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int ValidateCalls { get; private set; }
        public int CleanupCalls { get; private set; }
        public int EvictCalls { get; private set; }
        public void EvictRetainedClosetsForLoad() => EvictCalls++;
        public List<Guid> CreatedGuids { get; } = new();
        public List<(Guid Guid, SyndicateHqVector3 Position)> Placements { get; } = new();
        public List<bool> InteractionStates { get; } = new();
        public HashSet<Guid> ConflictingGuids { get; } = new();
        private readonly Dictionary<Guid, IReadOnlyList<Release1SmallCourtesySlotSnapshot>> _closetSlots = new();

        public int ReleaseOwnedClosetSlotLocksCalls { get; private set; }
        public List<(Guid Guid, int SlotIndex)> ClosetReadCalls { get; } = new();
        public List<(Guid Guid, int SlotIndex, bool Locked)> ClosetLockCalls { get; } = new();
        public List<(Guid Guid, int SlotIndex, float Amount)> ClosetCashChangeCalls { get; } = new();
        public Release1SmallCourtesyWorldReadStatus ClosetCashReadStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;
        public float ClosetCashReadValue { get; set; }
        public Release1SmallCourtesyWorldMutationStatus ClosetLockStatus { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public Release1SmallCourtesyWorldMutationStatus ClosetCashChangeStatus { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public bool ThrowOnClosetCashRead { get; set; }
        public bool ThrowOnClosetLock { get; set; }
        public bool ThrowOnClosetCashChange { get; set; }

        public void SeedRestored(SyndicateHqStorageDefinition definition) =>
            _restoredByGuid[definition.Guid] = new FakeEntity(definition.Guid);

        public void SeedClosetSlots(Guid guid, IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) =>
            _closetSlots[guid] = slots;

        public bool TryPrepareGrid(out object template, out string reason)
        {
            ResolveCalls++;
            template = new object();
            reason = _gridAvailable ? "resolved" : "unavailable";
            return _gridAvailable;
        }

        public SyndicateHqNativeStorageLookupStatus FindRestoredCloset(
            SyndicateHqStorageDefinition definition,
            out object entity,
            out string reason)
        {
            if (ConflictingGuids.Contains(definition.Guid))
            {
                entity = null!;
                reason = "duplicate restored identity";
                return SyndicateHqNativeStorageLookupStatus.IdentityConflict;
            }
            if (_restoredByGuid.TryGetValue(definition.Guid, out var restored))
            {
                entity = restored;
                reason = "restored";
                return SyndicateHqNativeStorageLookupStatus.Restored;
            }
            entity = null!;
            reason = "not restored";
            return SyndicateHqNativeStorageLookupStatus.Missing;
        }

        public bool TryCreate(
            object template,
            SyndicateHqStorageDefinition definition,
            out object entity,
            out string reason)
        {
            CreateCalls++;
            if (_throwOnCreate) throw new InvalidOperationException("planned create failure");
            entity = new FakeEntity(definition.Guid);
            if (CreateCalls == _failOnCreate)
            {
                reason = "planned create failure";
                return false;
            }
            CreatedGuids.Add(definition.Guid);
            _restoredByGuid[definition.Guid] = (FakeEntity)entity;
            reason = "created";
            return true;
        }

        public bool TryValidate(object entity, SyndicateHqStorageDefinition definition, out string reason)
        {
            ValidateCalls++;
            var valid = entity is FakeEntity fake &&
                fake.Guid == definition.Guid &&
                _slotCount == definition.RequiredSlotCount;
            reason = valid ? "valid" : "invalid";
            return valid;
        }

        public bool TryPlace(
            object entity,
            SyndicateHqVector3 worldPosition,
            float yawDegrees,
            out string reason)
        {
            var fake = Assert.IsType<FakeEntity>(entity);
            Placements.Add((fake.Guid, worldPosition));
            var valid = Placements.Count != _failOnPlace;
            reason = valid ? "placed" : "planned placement failure";
            return valid;
        }

        public void SetInteractionEnabled(object entity, bool enabled)
        {
            if (_throwOnDisable && !enabled)
                throw new InvalidOperationException("planned disable failure");
            InteractionStates.Add(enabled);
        }

        public bool TryReadClosetSlots(
            SyndicateHqStorageDefinition definition,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots,
            out string reason)
        {
            if (_closetSlots.TryGetValue(definition.Guid, out var seeded))
            {
                slots = seeded;
                reason = "read";
                return true;
            }
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            reason = "not seeded";
            return false;
        }

        public bool TryCleanup(object entity, out string reason)
        {
            CleanupCalls++;
            if (entity is FakeEntity fake)
                _restoredByGuid.Remove(fake.Guid);
            reason = "cleaned";
            return true;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(
            SyndicateHqStorageDefinition definition, int slotIndex, out float balance, out string reason)
        {
            ClosetReadCalls.Add((definition.Guid, slotIndex));
            balance = ClosetCashReadValue;
            reason = "fake closet cash read";
            if (ThrowOnClosetCashRead) throw new InvalidOperationException("planned closet cash read failure");
            return ClosetCashReadStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(
            SyndicateHqStorageDefinition definition, int slotIndex, bool locked, out string reason)
        {
            ClosetLockCalls.Add((definition.Guid, slotIndex, locked));
            reason = "fake closet lock";
            if (ThrowOnClosetLock) throw new InvalidOperationException("planned closet lock failure");
            return ClosetLockStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(
            SyndicateHqStorageDefinition definition, int slotIndex, float amount, out string reason)
        {
            ClosetCashChangeCalls.Add((definition.Guid, slotIndex, amount));
            reason = "fake closet cash change";
            if (ThrowOnClosetCashChange) throw new InvalidOperationException("planned closet cash change failure");
            return ClosetCashChangeStatus;
        }

        public void ReleaseOwnedClosetSlotLocks() => ReleaseOwnedClosetSlotLocksCalls++;
    }
}
