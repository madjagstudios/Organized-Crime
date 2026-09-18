using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// The quantity N deposit transaction: exactly one consumption of exactly N units from the single
/// matching slot, a surplus left untouched, and the 175 percent reward quoted from the value rule's
/// summed consumed value and paid in the same pass. Mirrors
/// <see cref="Release1WrongAddressDeliveryTests"/> in shape; the differences are identity-only
/// matching, the run condition of at least N, and the reward basis reading both the pre and post
/// slot state through <see cref="Release1ShortNoticeValueRule"/>.
/// </summary>
public sealed class Release1ShortNoticeDepositTests
{
    [Fact]
    public void Exactly_N_in_one_slot_consumes_N_and_pays_at_one_hundred_and_seventy_five_percent()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        Assert.Equal(Release1ShortNoticeValueConvention.PerStack, assignment.ValueConvention);
        // Total stack value for 3 units is 3000; consuming all 3 leaves the stack at 0.
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 3_000f);
        var cashBefore = harness.World.CashBalance;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.Equal(0, harness.World.Slot(assignment.HandoffDropGuid, 0).Quantity);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(1, harness.World.CashChanges);
        // Consumed value = preValue - postValue = 3000 - 0 = 3000; reward = 3000 * 1.75 = 5250.
        Assert.Equal(cashBefore + 5_250f, harness.World.CashBalance);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.OnTime, harness.Mission().LastOutcome);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
    }

    [Fact]
    public void A_full_consumption_under_a_stale_per_unit_convention_fails_closed_and_pays_nothing()
    {
        // Regression coverage for the OC-58 overpayment: a fully consumed slot gives one read, not
        // two, so PerUnit can no longer be confirmed against it and must block rather than assume
        // agreement and pay out an unverified amount. Only PerStack (the frozen production reading)
        // can pay on a full consumption; see Exactly_N_in_one_slot_consumes_N_and_pays above.
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld { SlotValueSimulation = Release1ShortNoticeValueConvention.PerUnit };
        using var harness = Release1ShortNoticeHarness.Active(repository, world, valueConvention: Release1ShortNoticeValueConvention.PerUnit);
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);
        var cashBefore = harness.World.CashBalance;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, status);
        Assert.Equal(cashBefore, harness.World.CashBalance);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.True(harness.Consumption().ExecutionBlocked);
    }

    [Fact]
    public void The_same_case_under_the_per_stack_convention_pays_the_stack_value_at_one_hundred_and_seventy_five_percent()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld { SlotValueSimulation = Release1ShortNoticeValueConvention.PerStack };
        using var harness = Release1ShortNoticeHarness.Active(repository, world, valueConvention: Release1ShortNoticeValueConvention.PerStack);
        var assignment = harness.Assignment;
        Assert.Equal(Release1ShortNoticeValueConvention.PerStack, assignment.ValueConvention);
        // Total stack value for 3 units is 3000; consuming all 3 leaves the stack at 0.
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 3_000f);
        var cashBefore = harness.World.CashBalance;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.Equal(cashBefore + 5_250f, harness.World.CashBalance);
    }

    [Theory]
    [InlineData(Release1ShortNoticeValueConvention.PerUnit)]
    [InlineData(Release1ShortNoticeValueConvention.PerStack)]
    public void A_surplus_slot_consumes_exactly_N_and_leaves_the_surplus(Release1ShortNoticeValueConvention convention)
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld { SlotValueSimulation = convention };
        using var harness = Release1ShortNoticeHarness.Active(repository, world, valueConvention: convention);
        var assignment = harness.Assignment;
        var initialQuantity = assignment.RequiredQuantity + 2;
        var initialValue = convention == Release1ShortNoticeValueConvention.PerUnit ? 1_000f : initialQuantity * 1_000f;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, initialQuantity, monetaryValue: initialValue);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        var post = harness.World.Slot(assignment.HandoffDropGuid, 0);
        Assert.Equal(2, post.Quantity);
        Assert.Equal(assignment.ProductId, post.ProductId);
        Assert.Equal(assignment.PackagingId, post.PackagingId);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(-assignment.RequiredQuantity, int.Parse(harness.World.MutationLog.Single(entry => entry.StartsWith("change:", StringComparison.Ordinal)).Split(':')[2]));
    }

    [Fact]
    public void A_slot_below_N_consumes_nothing_and_pays_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity - 1, monetaryValue: 1_000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.NoWork, status);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void Two_matching_slots_consume_nothing_and_pay_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);
        harness.World.SetSlot(assignment.HandoffDropGuid, 1, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.NoWork, status);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void The_consumption_is_exactly_one_world_call_with_amount_minus_N()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity + 4, monetaryValue: 1_000f);
        harness.World.MutationLog.Clear();

        harness.Service.ReconcileDeposit();

        var changeEntries = harness.World.MutationLog.Where(entry => entry.StartsWith("change:", StringComparison.Ordinal)).ToArray();
        var expected = $"change:0:{-assignment.RequiredQuantity}";
        Assert.Equal(new[] { expected }, changeEntries);
        Assert.Equal(1, harness.World.QuantityChanges);
    }

    [Fact]
    public void A_post_read_that_does_not_match_pre_minus_N_blocks_the_effect_and_pays_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity + 2, monetaryValue: 1_000f);
        // Simulate a post-mutation read that lands somewhere other than pre-N (a competing write).
        harness.World.QuantityDriftOnNextChange = 1;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, status);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.True(harness.Consumption().ExecutionBlocked);
    }

    [Theory]
    [InlineData(Release1ShortNoticeValueConvention.PerUnit)]
    [InlineData(Release1ShortNoticeValueConvention.PerStack)]
    public void A_post_value_that_contradicts_the_frozen_convention_blocks_the_effect_and_pays_nothing(Release1ShortNoticeValueConvention convention)
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld { SlotValueSimulation = convention };
        using var harness = Release1ShortNoticeHarness.Active(repository, world, valueConvention: convention);
        var assignment = harness.Assignment;
        var initialQuantity = assignment.RequiredQuantity + 2;
        var initialValue = convention == Release1ShortNoticeValueConvention.PerUnit ? 1_000f : initialQuantity * 1_000f;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, initialQuantity, monetaryValue: initialValue);
        // Force a post-mutation value that disagrees with both readings under either convention:
        // neither unchanged (PerUnit) nor proportionally scaled down (PerStack).
        harness.World.ForcedPostChangeMonetaryValue = initialValue * 5f + 1f;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, status);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.True(harness.Consumption().ExecutionBlocked);
    }

    [Fact]
    // Short Notice's FakeWorld had no way to simulate a cash movement between consumption and
    // payment until DriftCashAfterConsumption was added to it here, mirroring the seam already on
    // Release1WrongAddressMissionServiceTests.FakeWorld.
    public void Losing_cash_between_consumption_and_payment_does_not_stop_the_reward()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 3_000f);
        var cashBefore = harness.World.CashBalance;
        harness.World.DriftCashAfterConsumption = -400f;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.False(harness.Reward().ExecutionBlocked);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(cashBefore - 400f + 5_250f, harness.World.CashBalance);
    }

    // Production (per-stack) convention on a full consumption: summed consumed value = preValue -
    // postValue = preValue, since postValue lands at zero. Each row's stack total (requiredQuantity *
    // perUnitValue) is chosen to land the 175 percent quote exactly on a half dollar (N=3: total 6,
    // 6*1.75=10.5; N=2: total 2, 2*1.75=3.5; N=1: total 10, 10*1.75=17.5), so
    // MidpointRounding.AwayFromZero is the only thing that can produce the expected whole-dollar
    // reward. N=1 also happens to make PerUnit and PerStack agree, since consuming the only unit is
    // consuming the whole stack either way.
    [Theory]
    [InlineData(3, 2f, 11f)]
    [InlineData(2, 1f, 4f)]
    [InlineData(1, 10f, 18f)]
    public void Reward_rounding_is_midpoint_away_from_zero_for_N_equal_to_three_two_and_one(
        int requiredQuantity, float perUnitValue, float expectedReward)
    {
        Release1ShortNoticeHarness.Harness harness = requiredQuantity switch
        {
            3 => Release1ShortNoticeHarness.Active(),
            2 => Release1ShortNoticeHarness.MakeGoodActive(),
            1 => Release1ShortNoticeHarness.RecoveryActive(),
            _ => throw new InvalidOperationException("Unexpected N.")
        };
        using (harness)
        {
            var assignment = harness.Assignment;
            Assert.Equal(requiredQuantity, assignment.RequiredQuantity);
            harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, requiredQuantity, monetaryValue: requiredQuantity * perUnitValue);
            var cashBefore = harness.World.CashBalance;

            var status = harness.Service.ReconcileDeposit();

            Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
            Assert.Equal(cashBefore + expectedReward, harness.World.CashBalance);
        }
    }

    [Fact]
    public void A_second_reconcile_pass_before_a_save_consumes_nothing_further_and_pays_nothing_further()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, harness.Service.ReconcileDeposit());
        harness.World.ResetMutationEvidence();

        Assert.Equal(Release1ShortNoticeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());

        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);
    }

    [Fact]
    public void A_reconstructed_service_from_the_persisted_sidecar_consumes_nothing_further_and_pays_nothing_further()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        using (var first = Release1ShortNoticeHarness.Active(repository, world))
        {
            var assignment = first.Assignment;
            world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);
            Assert.Equal(Release1ShortNoticeDepositStatus.Paid, first.Service.ReconcileDeposit());
            Release1ShortNoticeHarness.Save(first);
            first.Service.OnPreLoad();
        }

        world.ResetMutationEvidence();
        using var restored = Release1ShortNoticeHarness.Load(repository, world);

        Assert.Equal(Release1ShortNoticeDepositStatus.Committed, restored.Service.ReconcileDeposit());
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
    }

    [Fact]
    public void A_quit_without_saving_after_deposit_reverts_and_the_next_pass_pays_exactly_once()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        Release1ShortNoticeAssignment assignment;
        float cashBaseline;
        using (var first = Release1ShortNoticeHarness.Active(repository, world))
        {
            assignment = first.Assignment;
            world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 3_000f);
            cashBaseline = world.CashBalance;

            Assert.Equal(Release1ShortNoticeDepositStatus.Paid, first.Service.ReconcileDeposit());
            Assert.Equal(1, world.QuantityChanges);
            Assert.Equal(1, world.CashChanges);
            var changeEntry = world.MutationLog.Single(entry => entry.StartsWith("change:", StringComparison.Ordinal));
            Assert.Equal($"change:0:{-assignment.RequiredQuantity}", changeEntry);

            first.Service.OnPreLoad();
        }

        // Quit without saving: the world discards every mutation made since the last real save, so
        // the slot and cash land back where they were right before the deposit pass ran, and nothing
        // that pass wrote to the story was ever durably persisted either (unlike Wrong Address,
        // Short Notice's deposit has no staging/custody gate, so it is the plain slot-quantity read
        // that determines whether a reload's own convergence redeposits).
        world.RevertToLastSave();
        Assert.Equal(assignment.RequiredQuantity, world.Slot(assignment.HandoffDropGuid, 0).Quantity);
        Assert.Equal(cashBaseline, world.CashBalance);
        var storedMission = repository.StoredState!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        Assert.Equal(Release1MissionState.Active, storedMission.State);
        Assert.Empty(repository.StoredState.NativeEffects);
        world.ResetMutationEvidence();

        // The slot already holds N again after the revert and no effect is on record. Short Notice's
        // deposit has no staging/custody gate the way Wrong Address does, so loading's own
        // convergence pass (inside OnLoadComplete, before the harness resets the world's mutation
        // counters) redeposits and pays exactly once more on its own: exactly N units leave the slot
        // and exactly one reward lands, before any explicit call is made.
        using var restored = Release1ShortNoticeHarness.Load(repository, world);

        Assert.Equal(0, world.Slot(assignment.HandoffDropGuid, 0).Quantity);
        // Consumed value under the production per-stack convention = preValue - postValue = 3000 - 0
        // = 3000; reward = 3000 * 1.75 = 5250, same as the first pass, confirming exactly one more
        // payment rather than none or a double payment.
        Assert.Equal(cashBaseline + 5_250f, world.CashBalance);
        Assert.Equal(Release1MissionState.Satisfied, restored.Mission().State);

        // A further explicit pass in the same session finds the pair already Applied and unsaved:
        // no additional consumption or payment happens.
        world.ResetMutationEvidence();
        Assert.Equal(Release1ShortNoticeDepositStatus.AwaitingAppliedSave, restored.Service.ReconcileDeposit());
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
    }

    [Fact]
    public void An_applied_pair_commits_only_after_the_capturing_revision_is_persisted()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, harness.Service.ReconcileDeposit());
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Reward().Phase);

        Release1ShortNoticeHarness.Save(harness);

        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Reward().Phase);
        Assert.Equal(1, harness.World.QuantityChanges);
        Assert.Equal(1, harness.World.CashChanges);
    }

    [Fact]
    public void A_deadline_lapse_with_a_partial_deposit_present_fails_the_stage_and_consumes_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity - 1, monetaryValue: 1_000f);
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, harness.Mission().LastOutcome);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(0, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void Completion_reaches_Standing_60_and_offers_Keep_the_Lights_Off()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, harness.Service.ReconcileDeposit());

        Assert.Equal(60, harness.Story.State!.Standing);
        Assert.Equal(
            Release1MissionState.Offered,
            harness.Story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)].State);
    }

    [Fact]
    public void Make_good_completion_restores_the_penalty_and_pays_once()
    {
        using var harness = Release1ShortNoticeHarness.MakeGoodActive();
        var assignment = harness.Assignment;
        var standingBeforeCompletion = harness.Story.State!.Standing;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.Late, harness.Mission().LastOutcome);
        // The penalty applied at the required failure (12) is credited back on completion, plus the
        // Late completion award of 5: standing increases by exactly that much, and never doubles up.
        Assert.Equal(standingBeforeCompletion + 12 + 5, harness.Story.State!.Standing);
        Assert.Equal(1, harness.World.CashChanges);
    }

    [Fact]
    public void Recovery_completion_pays_once_at_one_unit()
    {
        using var harness = Release1ShortNoticeHarness.RecoveryActive();
        var assignment = harness.Assignment;
        Assert.Equal(1, assignment.RequiredQuantity);
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1, monetaryValue: 1_000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(1, harness.World.CashChanges);
    }

    [Fact]
    public void The_slot_lock_is_released_on_every_failure_path()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;

        // Path 1: the lock acquisition itself is refused as Ambiguous.
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);
        harness.World.ReturnAmbiguousAfterLockOnce = true;
        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, harness.Service.ReconcileDeposit());
        Assert.False(harness.World.IsLocked);

        // Path 2: the quantity mutation itself throws.
        var repository2 = new Release1ShortNoticeHarness.FakeRepository();
        var world2 = new Release1ShortNoticeHarness.FakeWorld();
        using var harness2 = Release1ShortNoticeHarness.Active(repository2, world2);
        var assignment2 = harness2.Assignment;
        world2.SetSlot(assignment2.HandoffDropGuid, 0, assignment2.ProductId, assignment2.PackagingId, assignment2.RequiredQuantity, monetaryValue: 1_000f);
        world2.ThrowOnChange = true;
        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, harness2.Service.ReconcileDeposit());
        Assert.False(world2.IsLocked);

        // Path 3: the post-mutation read comes back mismatched.
        var repository3 = new Release1ShortNoticeHarness.FakeRepository();
        var world3 = new Release1ShortNoticeHarness.FakeWorld();
        using var harness3 = Release1ShortNoticeHarness.Active(repository3, world3);
        var assignment3 = harness3.Assignment;
        world3.SetSlot(assignment3.HandoffDropGuid, 0, assignment3.ProductId, assignment3.PackagingId, assignment3.RequiredQuantity, monetaryValue: 1_000f);
        world3.QuantityDriftOnNextChange = 1;
        Assert.Equal(Release1ShortNoticeDepositStatus.Ambiguous, harness3.Service.ReconcileDeposit());
        Assert.False(world3.IsLocked);
    }
}
