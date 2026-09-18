using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameDeliveryTests
{
    [Fact]
    public void A_deposit_before_the_release_is_never_consumed_or_paid()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.NoWork, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(cashBefore, harness.World.CashBalance);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void Delivery_consumes_exactly_one_unit_and_pays_one_hundred_and_fifty_percent_at_once()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        var status = harness.Service.ReconcileDelivery();

        Assert.True(status is Release1RoomWithNoNameDeliveryStatus.Paid or Release1RoomWithNoNameDeliveryStatus.AwaitingAppliedSave);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.Slot(harness.Assignment.HandoffDropGuid, 2).Quantity);
        Assert.Equal(cashBefore + 1_500f, harness.World.CashBalance);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.OnTime, harness.Mission().LastOutcome);
    }

    [Fact]
    public void A_clean_primary_completion_reaches_standing_fifty_and_offers_short_notice()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        harness.Service.ReconcileDelivery();

        Assert.Equal(50, harness.Story.State!.Standing);
        Assert.Equal(
            Release1MissionState.Offered,
            harness.Story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)].State);
    }

    [Fact]
    public void Payment_is_never_repeated_across_repeated_passes_or_a_save()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        harness.Service.ReconcileDelivery();
        var cashAfter = harness.World.CashBalance;
        Release1RoomWithNoNameHarness.Save(harness);
        harness.Service.ReconcileDelivery();
        harness.Service.ReconcileDelivery();

        Assert.Equal(cashAfter, harness.World.CashBalance);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(1, harness.World.QuantityChanges);
    }

    [Fact]
    public void Both_effects_leave_the_applied_phase_in_the_same_post_save_pass()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.Service.ReconcileDelivery();

        Release1RoomWithNoNameHarness.Save(harness);
        harness.Service.ReconcileDelivery();

        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Reward().Phase);
    }

    [Fact]
    public void Two_matching_consignments_at_the_hand_off_consume_neither()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 3, value: 1_000f);

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    // Cash drifts up by 250 after consumption; the reward re-anchors to a fresh delta and pays
    // through the drift rather than blocking on it.
    public void A_cash_balance_that_drifted_between_the_baseline_and_the_payment_no_longer_blocks_the_reward()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.DriftCashAfterConsumption = 250f;

        var status = harness.Service.ReconcileDelivery();

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.Paid, status);
        Assert.False(harness.Reward().ExecutionBlocked);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore + 250f + 1_500f, harness.World.CashBalance);
    }

    [Fact]
    // Same idea in the other direction: a 400 drop instead of the 250 rise above, so a
    // sign-dependent bug couldn't hide behind just one of the two.
    public void Pulling_cash_out_after_consumption_does_not_starve_the_payout()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.DriftCashAfterConsumption = -400f;

        var status = harness.Service.ReconcileDelivery();

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.Paid, status);
        Assert.False(harness.Reward().ExecutionBlocked);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore - 400f + 1_500f, harness.World.CashBalance);
    }

    [Fact]
    public void A_failed_quantity_change_blocks_the_consumption_and_never_pays()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.ThrowOnChange = true;

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.CashChanges);
        Assert.True(harness.Consumption().ExecutionBlocked);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void The_slot_lock_is_held_only_across_the_one_quantity_change_and_always_released()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        harness.Service.ReconcileDelivery();

        var log = harness.World.MutationLog;
        var lockIndex = log.FindIndex(entry => entry == "lock:2:true");
        var changeIndex = log.FindIndex(entry => entry == "change:2:-1");
        var unlockIndex = log.FindIndex(entry => entry == "lock:2:false");
        Assert.True(lockIndex >= 0 && changeIndex > lockIndex && unlockIndex > changeIndex);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void A_delivery_and_payment_that_was_never_saved_reverts_and_replays_exactly_once()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        float cashBefore;
        using (var first = Release1RoomWithNoNameHarness.Released(repository, world))
        {
            cashBefore = first.World.CashBalance;
            // PlaceExactPackage is a raw fixture poke (like SetSlot/EmptySlot) that bypasses
            // CapturePreSaveSnapshotIfNeeded, so RevertToLastSave() below cannot itself move this
            // package back to "inventory": the fake has no inventory concept for a dead drop, only
            // the drop's own slots. What RevertToLastSave() genuinely undoes is the mod's own
            // TryChangeSlotQuantity(-1) consumption, which *is* captured, so the package correctly
            // reappears in the same handoff-drop slot it was consumed from once the unsaved session
            // ends without a save, the same way it would in the real game. Kept as the poke per Task
            // 6 fix 3's fallback: the fake's snapshot semantics do not extend to a raw placement.
            first.World.PlaceExactPackage(first.Assignment, first.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            first.Service.ReconcileDelivery();
            Assert.Equal(cashBefore + 1_500f, first.World.CashBalance);
            first.World.RevertToLastSave();
            first.Service.OnPreLoad();
        }

        Assert.Equal(cashBefore, world.CashBalance);
        using var restored = Release1RoomWithNoNameHarness.Load(repository, world);

        // Before the redeposit: the reload lands exactly back at the release checkpoint the unsaved
        // delivery attempt started from, not part-way through it.
        Assert.Equal(Release1MissionState.Active, restored.Mission().State);
        Assert.True(restored.Progress()!.HoldSatisfied);

        restored.Service.ReconcileDelivery();

        Assert.Equal(cashBefore + 1_500f, restored.World.CashBalance);
        Assert.Equal(1, restored.World.CashChanges);
    }

    // Proves the guard at the top of the reward's Prepared branch that stops a second attempt from
    // paying twice: TryChangeCashBalance can succeed while the native mutation that should mark the
    // reward Applied never lands (a crash, a lost connection, anything between the two). The world's
    // cash fake models that split by failing only the post-payment verification read the mod makes
    // right after TryChangeCashBalance, exactly once, the same seam
    // Release1SmallCourtesyRewardTests.Post_payment_verification_failure_blocks_reward_after_one_native_attempt
    // uses. That leaves the reward Prepared and marked ExecutionBlocked, which is the mod's own
    // "ambiguous, needs authoritative reconciliation" state; ReconcileDelivery will not retry it on
    // its own, by design, because it can no longer tell whether the earlier payment actually landed.
    // A second pass first performs that reconciliation directly on the story (the same public seam
    // Release1StoryThirdReviewCorrectionTests.Authoritative_reconciliation_clears_execution_blocked_and_survives_restart
    // uses), then asserts ReconcileDelivery settles the reward as Applied without ever calling
    // TryChangeCashBalance a second time.
    [Fact]
    public void A_blocked_reward_is_reconciled_and_never_pays_twice()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        var cashBefore = harness.World.CashBalance;
        harness.World.ThrowOnCashReadAfterMutation = true;

        var firstPass = harness.Service.ReconcileDelivery();

        Assert.Equal(Release1RoomWithNoNameDeliveryStatus.Ambiguous, firstPass);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore + 1_500f, harness.World.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Reward().Phase);
        Assert.True(harness.Reward().ExecutionBlocked);

        harness.World.ThrowOnCashReadAfterMutation = false;
        var reconciled = harness.Story.TryMarkNativeEffectApplied(
            harness.Reward().EffectId,
            "room-with-no-name-reward-authoritative-v1",
            Release1NativeEffectPersistenceMode.RevertTolerant);
        Assert.Equal(Release1StoryCommandStatus.Accepted, reconciled.Status);
        Assert.False(harness.Reward().ExecutionBlocked);

        var secondPass = harness.Service.ReconcileDelivery();

        Assert.True(secondPass is Release1RoomWithNoNameDeliveryStatus.Committed or Release1RoomWithNoNameDeliveryStatus.AwaitingAppliedSave);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore + 1_500f, harness.World.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
    }

    [Fact]
    public void A_hand_off_already_at_the_exact_post_state_completes_and_pays_nothing_further()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        using (var first = Release1RoomWithNoNameHarness.Released(repository, world))
        {
            first.World.PlaceExactPackage(first.Assignment, first.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            first.Service.ReconcileDelivery();
            Release1RoomWithNoNameHarness.Save(first);
            first.Service.OnPreLoad();
        }

        var cashAfterFirst = world.CashBalance;
        using var restored = Release1RoomWithNoNameHarness.Load(repository, world);
        restored.Service.ReconcileDelivery();
        restored.Service.ReconcileDelivery();

        Assert.Equal(cashAfterFirst, restored.World.CashBalance);
        Assert.Equal(0, restored.World.CashChanges);
        Assert.Equal(Release1MissionState.Satisfied, restored.Mission().State);
    }

    [Fact]
    public void A_late_make_good_delivery_completes_late_and_still_pays_once()
    {
        using var harness = Release1RoomWithNoNameHarness.MakeGoodReleased();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 400f);

        harness.Service.ReconcileDelivery();

        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.Late, harness.Mission().LastOutcome);
        Assert.Equal(cashBefore + 600f, harness.World.CashBalance);
    }

    [Fact]
    public void Delivery_never_touches_a_closet()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        var sourceDropBefore = SourceDropSlots(harness);

        harness.Service.ReconcileDelivery();

        // Closets untouched: ClosetMutations now increments on every fake write a real
        // TryStowIntoCloset-style call would make (see StowIntoCloset/EmptyCloset), so this is a
        // real assertion, not a counter nothing ever moves.
        Assert.Equal(0, harness.World.ClosetMutations);
        // Source drop untouched: delivery reads and mutates only the hand-off drop; the source drop
        // the consignment was originally staged from and taken into custody from is byte-for-byte
        // the same before and after.
        Assert.Equal(sourceDropBefore, SourceDropSlots(harness));
    }

    private static IReadOnlyList<Release1SmallCourtesySlotSnapshot> SourceDropSlots(Release1RoomWithNoNameHarness.Harness harness)
    {
        harness.World.TryReadDeadDropSlots(harness.Assignment.SourceDropGuid, out var slots);
        return slots;
    }
}
