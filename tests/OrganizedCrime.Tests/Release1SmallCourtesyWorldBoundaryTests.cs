using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyWorldBoundaryTests
{
    private static readonly Release1StoryHostContextSnapshot ReadyContext = new(
        Guid.Parse("71717171-7171-7171-7171-717171717171"),
        4,
        "76561190000000001",
        @"C:\Saves\76561190000000001\SaveGame_5");

    private static readonly string BoundarySource = ReadSource("Runtime", "S1ApiRelease1SmallCourtesyWorld.cs");

    [Fact]
    public void Production_boundary_uses_only_the_reviewed_S1API_and_narrow_native_surfaces()
    {
        Assert.Contains("ProductManager.DiscoveredProducts", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("DeadDropManager.All", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("TimeManager.ElapsedDays", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("TimeManager.CurrentTime", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("TimeManager.GetMinutesFrom24HourTime", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("ItemManager.GetItemDefinition", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("storage.Slots", BoundarySource, StringComparison.Ordinal);
        Assert.Equal(1, Count("storage.OnClosed +="));
        Assert.Equal(1, Count("storage.OnClosed -="));
        Assert.Equal(1, Count("slot.ChangeQuantity(amount, true)"));
        Assert.Contains("if (!_ownedSlotLocks.Contains(key)) return Release1SmallCourtesyWorldMutationStatus.Rejected;", BoundarySource, StringComparison.Ordinal);
        Assert.Equal(3, Count("slot.SetIsRemovalLocked(false);"));
        Assert.Equal(2, Count("slot.SetIsRemovalLocked(true);"));
        Assert.Equal(1, Count("Money.ChangeCashBalance("));
        Assert.Equal(1, Count("GetMonetaryValue()"));
        Assert.Contains("SetIsRemovalLocked(locked)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("SetIsAddLocked(locked)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("candidate == null", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("native == null", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("gameObject.scene.IsValid()", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(native.DeadDropName)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(native.DeadDropDescription)", BoundarySource, StringComparison.Ordinal);

        Assert.DoesNotContain("slot.AddQuantity(amount)", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRandomEmptyDrop", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("Quest", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("NPC", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("Dialogue", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldStorageEntity", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new StorageEntity", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("PropertyManager", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOwned", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("AssetBundle", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveManager", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTime(", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetElapsedDays(", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(", BoundarySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", BoundarySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_preserves_stable_products_drops_slots_time_and_money()
    {
        var access = new FakeAccess
        {
            ElapsedDays = 2,
            MinuteOfDay = 90,
            Cash = 1_234.5f,
            Products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 999d, true) },
            Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "Behind the warehouse", 1, 2, 3, true) },
            Slots = new[] { new Release1SmallCourtesySlotSnapshot(3, "cocaine", "brick", 1, true, 800f) },
            HoldRoom = new Release1HoldRoomSnapshot(
                Release1HoldRoomReadiness.Ready,
                new[]
                {
                    new Release1HoldRoomClosetSnapshot("closet-a", 2, new[]
                    {
                        new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 800f),
                        new Release1SmallCourtesySlotSnapshot(1, null, null, 0, false, 0f)
                    }),
                    new Release1HoldRoomClosetSnapshot("closet-b", 0, Array.Empty<Release1SmallCourtesySlotSnapshot>())
                })
        };
        using var world = new S1ApiRelease1SmallCourtesyWorld(new FakeHostContext(), access);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadCanonicalTotalMinutes(out var minutes));
        Assert.Equal(2 * 1_440 + 90, minutes);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadProducts(out var products));
        Assert.Equal("cocaine", Assert.Single(products).ProductId);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadDeadDrops(out var drops));
        Assert.Equal("drop-a", Assert.Single(drops).Guid);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging));
        Assert.Equal("brick", packaging.PackagingId);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadDeadDropSlots("drop-a", out var slots));
        Assert.Equal(3, Assert.Single(slots).SlotIndex);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadCashBalance(out var cash));
        Assert.Equal(1_234.5f, cash);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadHoldRoom(out var room));
        Assert.Same(access.HoldRoom, room);
        Assert.Equal(Release1HoldRoomReadiness.Ready, room.Readiness);
        Assert.Equal(2, room.Closets.Count);
        Assert.Equal(2, room.Closets[0].Slots.Count);
        Assert.Equal(1, room.Closets[0].Slots[0].Quantity);
        Assert.Equal(0, room.Closets[0].Slots[1].Quantity);
        Assert.Empty(room.Closets[1].Slots);
    }

    [Fact]
    public void Invalid_or_missing_world_values_fail_closed_with_typed_statuses()
    {
        var access = new FakeAccess { ElapsedDays = -1, MinuteOfDay = 0, Cash = float.NaN };
        using var world = new S1ApiRelease1SmallCourtesyWorld(new FakeHostContext(), access);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadCanonicalTotalMinutes(out _));
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadCashBalance(out _));

        access.ElapsedDays = 0;
        access.MinuteOfDay = 1_440;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadCanonicalTotalMinutes(out _));

        access.Status = Release1SmallCourtesyWorldReadStatus.Unavailable;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadDeadDrops(out var drops));
        Assert.Empty(drops);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadHoldRoom(out var unavailableRoom));
        Assert.Equal(Release1HoldRoomReadiness.Unavailable, unavailableRoom.Readiness);

        access.Throw = true;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, world.TryReadProducts(out var products));
        Assert.Empty(products);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, world.TryReadHoldRoom(out var faultedRoom));
        Assert.Equal(Release1HoldRoomReadiness.Unavailable, faultedRoom.Readiness);
    }

    [Fact]
    public void HoldRoom_read_fails_closed_when_the_world_is_not_authoritative_or_the_snapshot_is_invalid()
    {
        var host = new FakeHostContext { Status = Release1StoryHostContextReadStatus.NotAuthoritative };
        var access = new FakeAccess();
        using var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, world.TryReadHoldRoom(out var blockedRoom));
        Assert.Equal(Release1HoldRoomReadiness.Unavailable, blockedRoom.Readiness);

        host.Status = Release1StoryHostContextReadStatus.Ready;
        access.HoldRoom = new Release1HoldRoomSnapshot(
            Release1HoldRoomReadiness.Ready,
            new[]
            {
                new Release1HoldRoomClosetSnapshot("closet a", 0, Array.Empty<Release1SmallCourtesySlotSnapshot>())
            });

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, world.TryReadHoldRoom(out var invalidRoom));
        Assert.Equal(Release1HoldRoomReadiness.Unavailable, invalidRoom.Readiness);
    }

    [Fact]
    public void Authority_and_argument_guards_precede_every_native_mutation()
    {
        var host = new FakeHostContext { Status = Release1StoryHostContextReadStatus.NotAuthoritative };
        var access = new FakeAccess();
        using var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TrySetSlotLocked("drop-a", 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeSlotQuantity("drop-a", 0, -1));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeCashBalance(10f));
        Assert.Equal(0, access.MutationCount);

        host.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeSlotQuantity("drop-a", 0, 0));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeSlotQuantity("drop-a", 0, 1));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeSlotQuantity("drop-a", 0, -4));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeCashBalance(float.PositiveInfinity));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TrySetSlotLocked("drop-a", 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeSlotQuantity("drop-a", 0, -1));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeSlotQuantity("drop-a", 0, -2));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeSlotQuantity("drop-a", 0, -3));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeCashBalance(10f));
        Assert.Equal(5, access.MutationCount);
    }

    [Fact]
    public void Cash_slot_members_reject_invalid_input_and_require_authority_and_a_held_lock()
    {
        var host = new FakeHostContext();
        var access = new FakeAccess();
        var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        host.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, world.TryReadDeadDropSlotCashBalance("drop-a", 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, -100f));

        host.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadDeadDropSlotCashBalance("", 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadDeadDropSlotCashBalance("drop-a", -1, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, 0f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, 100f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, float.NegativeInfinity));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, -20001f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, -100f));

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TrySetSlotLocked("drop-a", 0, true));
        access.CashBalances[(("drop-a", 0))] = 2000f;
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeDeadDropSlotCashBalance("drop-a", 0, -100f));
        Assert.Equal(1900f, access.CashBalances[("drop-a", 0)]);
    }

    [Fact]
    public void Closet_cash_members_reject_invalid_input_and_require_authority_and_a_held_lock()
    {
        var host = new FakeHostContext();
        var access = new FakeAccess();
        var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        host.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, world.TryReadHoldRoomSlotCashBalance("closet-a", 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TrySetHoldRoomSlotLocked("closet-a", 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, -100f));

        host.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadHoldRoomSlotCashBalance("", 0, out _));
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadHoldRoomSlotCashBalance("closet-a", -1, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TrySetHoldRoomSlotLocked("", 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, 0f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, 100f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, float.NaN));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, -20001f));

        access.ClosetCashBalances[("closet-a", 0)] = 1000f;
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TrySetHoldRoomSlotLocked("closet-a", 0, true));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryChangeHoldRoomSlotCashBalance("closet-a", 0, -100f));
        Assert.Equal(900f, access.ClosetCashBalances[("closet-a", 0)]);
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadHoldRoomSlotCashBalance("closet-a", 0, out var post));
        Assert.Equal(900f, post);
    }

    // FakeAccess (the outer-boundary fake used throughout this file) cannot express the access
    // implementation's own post-decrement-to-zero branch: that logic lives inside
    // S1ApiRelease1SmallCourtesyWorldAccess against a real IL2CPP CashInstance, which no fake can
    // stand in for. This is a source-scan test instead, following the same BoundarySource/Count
    // pattern the rest of this file already uses for other IL2CPP-only branches.
    [Fact]
    public void Decrement_to_zero_accepts_both_an_emptied_slot_and_a_zero_balance_CashInstance_as_Succeeded()
    {
        Assert.Contains(
            "if (postItem == null || slot.Quantity == 0) return Release1SmallCourtesyWorldMutationStatus.Succeeded;",
            BoundarySource, StringComparison.Ordinal);
        Assert.Contains(
            "var zeroCash = postItem.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();",
            BoundarySource, StringComparison.Ordinal);
        Assert.Contains(
            "return zeroCash != null && MathF.Abs(zeroCash.Balance) <= CashBalanceTolerance",
            BoundarySource, StringComparison.Ordinal);
        // The pre-fix code returned Ambiguous unconditionally whenever a CashInstance survived a
        // decrement to zero, regardless of its balance, rejecting the valid zero-balance outcome; the
        // emptied-slot check now returns immediately rather than feeding a ternary that always ended
        // in Ambiguous for a surviving CashInstance.
        Assert.Contains("postItem == null || slot.Quantity == 0) return", BoundarySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_close_subscription_is_idempotent_and_suppresses_stale_epoch_callbacks()
    {
        var host = new FakeHostContext();
        var access = new FakeAccess();
        using var world = new S1ApiRelease1SmallCourtesyWorld(host, access);
        var callbacks = 0;
        Action<string> callback = guid => { Assert.Equal("drop-a", guid); callbacks++; };

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TrySubscribeDeadDropClosed("drop-a", callback, out var first));
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TrySubscribeDeadDropClosed("drop-a", callback, out var second));
        Assert.Same(first, second);
        Assert.Equal(1, access.AttachCount);

        access.FireLatest();
        Assert.Equal(1, callbacks);
        host.Snapshot = host.Snapshot with { LoadEpoch = host.Snapshot.LoadEpoch + 1 };
        access.FireLatest();
        Assert.Equal(1, callbacks);

        first!.Dispose();
        first.Dispose();
        Assert.Equal(1, access.Registrations[0].RemoveCount);
    }

    [Fact]
    public void Failed_exact_removal_blocks_replacement_until_the_same_registration_is_removed()
    {
        var access = new FakeAccess();
        using var world = new S1ApiRelease1SmallCourtesyWorld(new FakeHostContext(), access);
        Action<string> firstCallback = _ => { };
        Action<string> replacementCallback = _ => { };

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TrySubscribeDeadDropClosed("drop-a", firstCallback, out var first));
        access.Registrations[0].CanRemove = false;
        first!.Dispose();

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TrySubscribeDeadDropClosed("drop-b", replacementCallback, out var blocked));
        Assert.Null(blocked);
        Assert.Equal(1, access.AttachCount);

        access.Registrations[0].CanRemove = true;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TrySubscribeDeadDropClosed("drop-b", replacementCallback, out var replacement));
        Assert.NotNull(replacement);
        Assert.Equal(2, access.AttachCount);
        Assert.Equal(3, access.Registrations[0].RemoveCount);
    }

    [Fact]
    public void Production_activity_requires_authority_and_validates_what_the_access_layer_returns()
    {
        var host = new FakeHostContext();
        var access = new FakeAccess();
        var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        host.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, world.TryReadProductionActivity(out var denied));
        Assert.Empty(denied.Properties);

        host.Status = Release1StoryHostContextReadStatus.Ready;
        access.ProductionActivity = null;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadProductionActivity(out _));

        // A duplicated property code fails Validate, and the boundary reports Faulted rather than handing
        // a half trusted census to the classifier.
        var barn = new Release1ProductionPropertySnapshot("barn", "Barn",
            Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>());
        access.ProductionActivity = new Release1ProductionActivitySnapshot(new[] { barn, barn });
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, world.TryReadProductionActivity(out _));

        access.ProductionActivity = new Release1ProductionActivitySnapshot(new[] { barn });
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadProductionActivity(out var ready));
        Assert.Equal("barn", Assert.Single(ready.Properties).PropertyCode);
    }

    [Fact]
    public void Field_contact_members_reject_invalid_input_and_require_authority()
    {
        var host = new FakeHostContext();
        var access = new FakeAccess();
        var world = new S1ApiRelease1SmallCourtesyWorld(host, access);

        host.Status = Release1StoryHostContextReadStatus.NotAuthoritative;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, world.TryReadFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryParkFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryUnparkFieldContact("oc_release1_arthur_spike", 3f, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryDespawnFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryProvokeFieldContact("oc_release1_arthur_spike", out _));

        host.Status = Release1StoryHostContextReadStatus.Ready;
        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Unavailable, world.TryReadFieldContact("", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryParkFieldContact("", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryUnparkFieldContact("", 3f, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryDespawnFieldContact(" ", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryProvokeFieldContact("a b", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryUnparkFieldContact("oc_release1_arthur_spike", 0f, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryUnparkFieldContact("oc_release1_arthur_spike", -3f, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryUnparkFieldContact("oc_release1_arthur_spike", float.NaN, out _));
        Assert.Equal(
            Release1SmallCourtesyWorldMutationStatus.Rejected,
            world.TryUnparkFieldContact("oc_release1_arthur_spike", S1ApiRelease1SmallCourtesyWorld.MaximumFieldContactAheadMetres + 1f, out _));

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryUnparkFieldContact("oc_release1_arthur_spike", 3f, out _));
        Assert.Equal(3f, access.LastAheadMetres);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Ready, world.TryReadFieldContact("oc_release1_arthur_spike", out var snapshot));
        Assert.True(snapshot.Present);
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryProvokeFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryParkFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, world.TryDespawnFieldContact("oc_release1_arthur_spike", out _));
    }

    [Fact]
    public void Field_contact_members_never_propagate_an_access_throw()
    {
        var access = new FakeAccess { Throw = true };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeHostContext(), access);

        Assert.Equal(Release1SmallCourtesyWorldReadStatus.Faulted, world.TryReadFieldContact("oc_release1_arthur_spike", out var snapshot));
        Assert.False(snapshot.Present);
        // Neither TryParkFieldContact nor TryUnparkFieldContact carries a read-only pre-check ahead of
        // its own access call (on-demand construction's already-spawned pre-check is gone with it), so
        // a throw from FakeAccess reaches only the mutation-attempt catch and stays Ambiguous, matching
        // TryDespawnFieldContact and TryProvokeFieldContact.
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, world.TryParkFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, world.TryUnparkFieldContact("oc_release1_arthur_spike", 3f, out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, world.TryDespawnFieldContact("oc_release1_arthur_spike", out _));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, world.TryProvokeFieldContact("oc_release1_arthur_spike", out _));
    }

    private static int Count(string value) =>
        BoundarySource.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class FakeHostContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; set; } = ReadyContext;

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Status == Release1StoryHostContextReadStatus.Ready ? Snapshot : default;
            return Status;
        }
    }

    private sealed class FakeAccess : IRelease1SmallCourtesyWorldAccess
    {
        public Release1SmallCourtesyWorldReadStatus Status { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;
        public bool Throw { get; set; }
        public int ElapsedDays { get; set; }
        public int MinuteOfDay { get; set; }
        public float Cash { get; set; } = 500f;
        public int MutationCount { get; private set; }
        public int AttachCount { get; private set; }
        public IReadOnlyList<Release1SmallCourtesyProductCandidate> Products { get; set; } =
            new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
        public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } =
            new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "First", 1, 2, 3, true) };
        public IReadOnlyList<Release1SmallCourtesySlotSnapshot> Slots { get; set; } = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        public Release1HoldRoomSnapshot HoldRoom { get; set; } = Release1HoldRoomSnapshot.Unavailable();
        public List<FakeRegistration> Registrations { get; } = new();
        public Dictionary<(string, int), float> CashBalances { get; } = new();
        public Dictionary<(string, int), float> ClosetCashBalances { get; } = new();
        public HashSet<(string, int)> ClosetLocks { get; } = new();
        public bool ContactPresent { get; set; }
        public float LastAheadMetres { get; private set; } = -1f;

        private static readonly Release1FieldContactSnapshot AbsentSnapshot =
            Release1FieldContactSnapshot.AbsentContact(
                100f, 100f, false, false, false, false,
                Release1FieldContactPursuitLevel.None, false, false, false, 0, 0);

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            if (Throw) throw new InvalidOperationException("field contact read fault");
            snapshot = ContactPresent
                ? AbsentSnapshot with
                {
                    Present = true, Physical = true, Visible = true, DistanceToPlayer = 3f,
                    ContactHealth = 100f, ContactMaxHealth = 100f, Conscious = true,
                    Aggressiveness = 0.5f, GiveUpRange = 20f, GiveUpTime = 8f
                }
                : AbsentSnapshot;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { if (Throw) throw new InvalidOperationException("field contact park fault"); reason = "fake park"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        {
            if (Throw) throw new InvalidOperationException("field contact unpark fault");
            LastAheadMetres = aheadMetres;
            ContactPresent = true;
            reason = "fake unpark";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { if (Throw) throw new InvalidOperationException("despawn fault"); reason = "fake despawn"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { if (Throw) throw new InvalidOperationException("provoke fault"); reason = "fake provoke"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldReadStatus TryReadClock(out int elapsedDays, out int minuteOfDay)
        {
            MaybeThrow(); elapsedDays = ElapsedDays; minuteOfDay = MinuteOfDay; return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            MaybeThrow(); products = Products; return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            MaybeThrow(); drops = Drops; return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)
        {
            MaybeThrow(); packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar"); return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            MaybeThrow(); slots = Slots; return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            MaybeThrow(); room = HoldRoom; return Status;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
        {
            MaybeThrow(); MutationCount++; return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            MaybeThrow(); MutationCount++; return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            MaybeThrow(); MutationCount++; reason = "staged."; return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TryAttachDropClosed(string deadDropGuid, Action callback, out IRelease1SmallCourtesyCloseRegistration? registration)
        {
            MaybeThrow(); AttachCount++; var created = new FakeRegistration(callback); Registrations.Add(created); registration = created; return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            MaybeThrow(); balance = Cash; return Status;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            MaybeThrow(); MutationCount++; Cash += amount; return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = CashBalances.TryGetValue((deadDropGuid, slotIndex), out var value) ? value : 0f;
            return CashBalances.ContainsKey((deadDropGuid, slotIndex))
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount)
        {
            if (!CashBalances.ContainsKey((deadDropGuid, slotIndex))) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            CashBalances[(deadDropGuid, slotIndex)] += amount;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = ClosetCashBalances.TryGetValue((closetGuid, slotIndex), out var value) ? value : 0f;
            return ClosetCashBalances.ContainsKey((closetGuid, slotIndex))
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked)
        {
            if (locked) ClosetLocks.Add((closetGuid, slotIndex)); else ClosetLocks.Remove((closetGuid, slotIndex));
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount)
        {
            if (!ClosetLocks.Contains((closetGuid, slotIndex))) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (!ClosetCashBalances.ContainsKey((closetGuid, slotIndex))) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
            ClosetCashBalances[(closetGuid, slotIndex)] += amount;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1ProductionActivitySnapshot? ProductionActivity { get; set; } =
            Release1ProductionActivitySnapshot.Empty;

        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = ProductionActivity!;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public void FireLatest() => Registrations[^1].Callback();
        public void Dispose() { }
        private void MaybeThrow() { if (Throw) throw new InvalidOperationException("synthetic boundary failure"); }
    }

    private sealed class FakeRegistration : IRelease1SmallCourtesyCloseRegistration
    {
        public FakeRegistration(Action callback) => Callback = callback;
        public Action Callback { get; }
        public bool CanRemove { get; set; } = true;
        public int RemoveCount { get; private set; }
        public bool TryRemove() { RemoveCount++; return CanRemove; }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root, "tools", "OrganizedCrime" }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "OrganizedCrime"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
