using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressDeliveryTests
{
    [Fact]
    public void One_deposit_consumes_one_unit_completes_the_stage_and_pays_in_the_same_pass()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        var status = harness.Service.ReconcileDelivery();

        Assert.Equal(Release1WrongAddressDeliveryStatus.Paid, status);
        Assert.Equal(0, harness.World.Slot(harness.Assignment.HandoffDropGuid, 2).Quantity);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore + 1_250f, harness.World.CashBalance);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.OnTime, harness.Mission().LastOutcome);
        Assert.Equal(40, harness.Story.State!.Standing);
        Assert.Equal(
            Release1MissionState.Offered,
            harness.Story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)].State);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
    }

    [Fact]
    public void The_delivery_slot_is_locked_for_the_mutation_and_released_afterwards()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.ResetMutationEvidence();

        harness.Service.ReconcileDelivery();

        Assert.Equal(
            new[] { "read", "lock:2:true", "change:2:-1", "read", "lock:2:false" },
            harness.World.MutationLog);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void Nothing_is_persisted_until_the_next_native_save_and_then_both_applied_phases_land_together()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.Repository.ResetEvidence();

        harness.Service.ReconcileDelivery();
        Assert.Empty(harness.Repository.Updates);

        Release1WrongAddressHarness.Save(harness);

        var stored = harness.Repository.StoredState!;
        Assert.Equal(Release1NativeEffectPhase.Applied, stored.NativeEffects.Single(e => e.EffectKind == "CargoTransfer").Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, stored.NativeEffects.Single(e => e.EffectKind == "Reward").Phase);
        Assert.Equal(
            Release1MissionState.Satisfied,
            stored.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)].State);

        // The stored (durably saved) snapshot still reads Applied for both effects: the save writes
        // what existed when it started. Committing is in-memory story bookkeeping that Converge
        // performs right after, in the very same OnSaveComplete pass, once the save has covered it.
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Reward().Phase);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(1, harness.World.CashChanges);
    }

    // The commit for each effect is caller-side, in-memory story bookkeeping, not a native mutation,
    // so it does not need its own save: both land in the very same post-save convergence pass that
    // Service.OnSaveComplete() (inside Save()) triggers, which is also the pass that queues Arthur
    // once his own prerequisite (already recorded) and both commits stop blocking his authorization.
    [Fact]
    public void Both_effects_commit_after_one_save_and_arthur_queues_in_that_same_pass()
    {
        using var harness = Release1WrongAddressHarness.InCustody(withPhone: true);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.RecordNellAcceptedReceipt();

        harness.Service.ReconcileDelivery();
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
        Assert.Empty(harness.Queue.Invocations);

        Release1WrongAddressHarness.Save(harness);

        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Reward().Phase);
        Assert.Single(harness.Queue.Invocations);

        harness.Service.Update();
        harness.Service.Update();

        Assert.Single(harness.Queue.Invocations);
    }

    [Theory]
    [InlineData(1_000f, 1_250f)]
    [InlineData(999.6f, 1_250f)]
    [InlineData(10f, 13f)]
    [InlineData(0f, 0f)]
    public void The_reward_is_one_hundred_and_twenty_five_percent_rounded_away_from_zero(float value, float expected)
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        var cashBefore = harness.World.CashBalance;
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: value);

        harness.Service.ReconcileDelivery();

        Assert.Equal(cashBefore + expected, harness.World.CashBalance);
    }

    [Fact]
    public void A_late_delivery_completes_with_the_late_timing()
    {
        using var harness = Release1WrongAddressHarness.MakeGoodInCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 400f);

        harness.Service.ReconcileDelivery();

        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.Late, harness.Mission().LastOutcome);
    }

    [Theory]
    [InlineData("other-product", "brick", 1, true)]
    [InlineData("cocaine", "jar", 1, true)]
    [InlineData("cocaine", "brick", 1, false)]
    public void A_wrong_or_unpackaged_deposit_is_inert(string productId, string packagingId, int quantity, bool isPackaged)
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.SetSlot(harness.Assignment.HandoffDropGuid, 2, productId, packagingId, quantity, isPackaged);

        Assert.Equal(Release1WrongAddressDeliveryStatus.NoWork, harness.Service.ReconcileDelivery());

        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void Several_exact_matches_hold_and_consume_nothing()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 3, value: 1_000f);

        Assert.Equal(Release1WrongAddressDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void Delivery_never_touches_the_source_drop()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.SourceDropGuid, slotIndex: 0, value: 1_000f);

        harness.Service.ReconcileDelivery();

        Assert.Equal(1, harness.World.Slot(harness.Assignment.SourceDropGuid, 0).Quantity);
        Assert.DoesNotContain(harness.World.MutationLog, entry => entry.StartsWith($"change@{harness.Assignment.SourceDropGuid}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_quit_without_saving_after_delivery_reverts_and_the_next_pass_pays_exactly_once()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.InCustody(repository, world))
        {
            world.PlaceExactPackage(first.Assignment, first.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            Assert.Equal(Release1WrongAddressDeliveryStatus.Paid, first.Service.ReconcileDelivery());
            first.Service.OnPreLoad();
        }

        world.RevertToLastSave();
        world.ResetMutationEvidence();

        using var restored = Release1WrongAddressHarness.Load(repository, world);
        Assert.Empty(restored.Story.State!.NativeEffects);
        Assert.Equal(Release1WrongAddressDeliveryStatus.Paid, restored.Service.ReconcileDelivery());
        Assert.Equal(1, world.QuantityChanges);
        Assert.Equal(1, world.CashChanges);
    }

    [Fact]
    public void A_persisted_prepared_effect_with_the_exact_post_state_infers_applied_without_consuming_again()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.InCustody(repository, world))
        {
            world.PlaceExactPackage(first.Assignment, first.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            first.Service.ReconcileDelivery();
            Release1WrongAddressHarness.Save(first);
            first.Service.OnPreLoad();
        }

        world.ResetMutationEvidence();
        using var restored = Release1WrongAddressHarness.Load(repository, world);

        Assert.Equal(Release1WrongAddressDeliveryStatus.Committed, restored.Service.ReconcileDelivery());
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
    }

    [Fact]
    public void A_balance_matching_neither_baseline_nor_expected_holds_without_paying()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.DriftCashAfterConsumption = 77f;

        Assert.Equal(Release1WrongAddressDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.CashChanges);
        Assert.True(harness.Reward().ExecutionBlocked);
    }

    [Fact]
    public void A_throwing_or_unverified_cash_mutation_blocks_and_never_retries_blindly()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.ThrowOnCashChange = true;

        Assert.Equal(Release1WrongAddressDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());
        Assert.True(harness.Reward().ExecutionBlocked);

        harness.World.ThrowOnCashChange = false;
        Assert.Equal(Release1WrongAddressDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void A_failed_quantity_change_blocks_before_any_completion_or_payment()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.World.ThrowOnChange = true;

        Assert.Equal(Release1WrongAddressDeliveryStatus.Ambiguous, harness.Service.ReconcileDelivery());

        Assert.NotEqual(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void A_persisted_prepared_effect_with_the_exact_pre_state_consumes_once_and_pays_once_on_reload()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.InCustody(repository, world))
        {
            var assignment = first.Assignment;
            world.PlaceExactPackage(assignment, assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

            var authorization = first.Mission().AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty;
            var identity = new Release1SmallCourtesyCargoIdentity(
                assignment.ProductId, assignment.PackagingId, 2,
                assignment.PackageQuantity, assignment.PackageQuantity - 1, 1_000f);
            var prepared = new Release1NativeEffectJournalEntry(
                $"wrong-address-cargo-v1-a{assignment.Attempt}",
                Release1MissionCatalog.WrongAddress,
                assignment.Attempt,
                "CargoTransfer",
                assignment.HandoffDropGuid,
                "oc-wrong-address",
                identity.Serialize(),
                Release1NativeEffectPhase.Prepared,
                null,
                first.Story.State!.Revision + 1,
                AuthorizedStoryCorrelationId: authorization,
                AuthorizedMissionRevision: first.Mission().Revision);
            Assert.True(first.Story.TryPrepareNativeEffect(prepared).Accepted);

            // Persist the story only (the Prepared effect), never the mission service's own save
            // hook, so the world is left exactly as this test set it: the package still sitting,
            // unconsumed, in the handoff slot.
            first.Story.OnSaveStart();
            first.Story.OnSaveComplete();
            world.MarkSaved();

            first.Service.OnPreLoad();
            first.Story.OnPreLoad();
        }

        var context = new Release1WrongAddressHarness.FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        Assert.Equal(
            Release1NativeEffectPhase.Prepared,
            story.State!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer").Phase);
        var handoffDropGuid = story.State.WrongAddressAssignments.Single().HandoffDropGuid;

        var logs = new List<string>();
        using var service = new Release1WrongAddressMissionService(story, world, log: logs.Add);
        world.ResetMutationEvidence();

        service.OnLoadComplete();

        Assert.Equal(1, world.QuantityChanges);
        Assert.Equal(1, world.CashChanges);
        Assert.Equal(0, world.Slot(handoffDropGuid, 2).Quantity);
        Assert.Equal(
            Release1NativeEffectPhase.Applied,
            story.State!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer").Phase);
        Assert.Equal(
            Release1NativeEffectPhase.Applied,
            story.State.NativeEffects.Single(effect => effect.EffectKind == "Reward").Phase);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)].State);

        service.Dispose();
        story.Dispose();
    }

    [Fact]
    public void A_second_pass_without_saving_reports_awaiting_applied_save_and_mutates_nothing()
    {
        using var harness = Release1WrongAddressHarness.InCustody();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        Assert.Equal(Release1WrongAddressDeliveryStatus.Paid, harness.Service.ReconcileDelivery());
        harness.World.ResetMutationEvidence();

        Assert.Equal(Release1WrongAddressDeliveryStatus.AwaitingAppliedSave, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
    }

    [Fact]
    public void Delivery_is_inert_before_custody()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);

        Assert.Equal(Release1WrongAddressDeliveryStatus.NoWork, harness.Service.ReconcileDelivery());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }
}
