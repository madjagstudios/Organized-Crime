using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyRewardTests
{
    [Theory]
    [InlineData(100f, 125f)]
    [InlineData(100.4f, 126f)]
    [InlineData(100.39f, 125f)]
    public void Verified_cargo_completes_primary_and_prepares_whole_dollar_reward_in_memory(
        float depositedValue,
        float expectedReward)
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, depositedValue);

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Release1SmallCourtesyDepositTests.Save(harness);

        var mission = harness.Story.State!.Missions[0];
        Assert.Equal(Release1MissionState.Satisfied, mission.State);
        Assert.Equal(Release1MissionOutcome.OnTime, mission.LastOutcome);
        Assert.False(mission.QuietConditionAwarded);
        Assert.False(harness.Story.State.Release1Recognized);
        Assert.Equal(Release1NativeEffectPhase.Applied, Cargo(harness).Phase);
        var reward = Reward(harness);
        Assert.Equal(Release1NativeEffectPhase.Prepared, reward.Phase);
        Assert.True(Release1SmallCourtesyRewardIdentity.TryParse(reward.AmountOrCargoIdentity, out var identity));
        Assert.Equal(500f, identity!.BaselineCash);
        Assert.Equal(expectedReward, identity.WholeDollarAmount);
        Assert.Equal(500f + expectedReward, identity.ExpectedCash);
        Assert.Equal(500f, harness.World.CashBalance);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1MissionState.Active, harness.Repository.StoredState!.Missions[0].State);
        Assert.Single(harness.Repository.StoredState.NativeEffects);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyAssignmentMode.Primary, Release1MissionOutcome.OnTime)]
    [InlineData(Release1SmallCourtesyAssignmentMode.MakeGood, Release1MissionOutcome.Late)]
    [InlineData(Release1SmallCourtesyAssignmentMode.Recovery, Release1MissionOutcome.Late)]
    public void Every_stage_mode_satisfies_once_with_existing_timing_rules(
        Release1SmallCourtesyAssignmentMode mode,
        Release1MissionOutcome expectedOutcome)
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission(mode: mode);
        PrepareDeposit(harness, 1_000f);

        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Release1SmallCourtesyDepositTests.Save(harness);

        var mission = harness.Story.State!.Missions[0];
        Assert.Equal(Release1MissionState.Satisfied, mission.State);
        Assert.Equal(expectedOutcome, mission.LastOutcome);
        Assert.False(mission.QuietConditionAwarded);
        Assert.False(harness.Story.State.Release1Recognized);
        Assert.Equal(harness.Assignment.Attempt, mission.Attempt);
        Assert.Single(harness.Story.State.NativeEffects, effect => effect.EffectKind == "Reward");
    }

    [Fact]
    public void Reward_applies_and_commits_once_across_two_more_native_save_boundaries()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        Assert.Equal(
            Release1SmallCourtesyDepositStatus.AwaitingPreparedSave,
            harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid));
        Release1SmallCourtesyDepositTests.Save(harness);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Reward(harness).Phase);
        Assert.Equal(0, harness.World.CashChanges);

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(1_750f, harness.World.CashBalance);
        Assert.Equal(1, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, Reward(harness).Phase);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Repository.StoredState!.NativeEffects.Single(effect => effect.EffectKind == "Reward").Phase);
        Assert.Equal(Release1NativeEffectPhase.Committed, Cargo(harness).Phase);

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(Release1NativeEffectPhase.Committed, Reward(harness).Phase);
        Assert.Equal(1, harness.World.CashChanges);
        Release1SmallCourtesyDepositTests.Save(harness);
        Assert.Equal(1, harness.World.CashChanges);
    }

    [Fact]
    public void Persisted_prepared_with_expected_cash_infers_applied_without_paying_again()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        var identity = RewardIdentity(harness);
        harness.World.CashBalance = identity.ExpectedCash;

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Applied, Reward(harness).Phase);
    }

    [Fact]
    public void Conflicting_cash_blocks_prepared_reward_without_native_mutation()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        harness.World.CashBalance += 10f;

        Release1SmallCourtesyDepositTests.Save(harness);

        var reward = Reward(harness);
        Assert.Equal(Release1NativeEffectPhase.Prepared, reward.Phase);
        Assert.True(reward.ExecutionBlocked);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1SmallCourtesyRewardStatus.Ambiguous, harness.Service.ReconcileReward());
    }

    [Fact]
    public void Throwing_cash_mutation_is_ambiguous_and_never_retried()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        harness.World.ThrowOnCashChange = true;

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.True(Reward(harness).ExecutionBlocked);
        Assert.Equal(0, harness.World.CashChanges);
        harness.World.ThrowOnCashChange = false;
        Assert.Equal(Release1SmallCourtesyRewardStatus.Ambiguous, harness.Service.ReconcileReward());
        Assert.Equal(0, harness.World.CashChanges);
    }

    [Fact]
    public void Post_payment_verification_failure_blocks_reward_after_one_native_attempt()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        harness.World.ThrowOnCashReadAfterMutation = true;

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(1, harness.World.CashChanges);
        Assert.True(Reward(harness).ExecutionBlocked);
        harness.World.ThrowOnCashReadAfterMutation = false;
        Assert.Equal(Release1SmallCourtesyRewardStatus.Ambiguous, harness.Service.ReconcileReward());
        Assert.Equal(1, harness.World.CashChanges);
    }

    [Fact]
    public void Failed_save_of_prepared_reward_never_authorizes_cash_mutation()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Reward(harness).Phase);
        harness.Repository.FailNextUpdate = true;

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(500f, harness.World.CashBalance);
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(Release1NativeEffectPhase.Prepared, Reward(harness).Phase);
    }

    [Fact]
    public void Quit_after_live_payment_before_applied_save_replays_once_from_persisted_baseline()
    {
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        var world = new Release1SmallCourtesyDepositTests.FakeWorld(ContextSnapshot());
        float baseline;
        using (var first = Release1SmallCourtesyDepositTests.ActiveMission(repository, world))
        {
            PrepareDeposit(first, 1_000f);
            first.Service.TryHandleDropClosed(first.Assignment.DeadDropGuid);
            Release1SmallCourtesyDepositTests.Save(first);
            baseline = RewardIdentity(first).BaselineCash;
            Release1SmallCourtesyDepositTests.Save(first);
            Assert.Equal(1, world.CashChanges);
            Assert.Equal(Release1NativeEffectPhase.Prepared, repository.StoredState!.NativeEffects.Single(effect => effect.EffectKind == "Reward").Phase);
        }

        world.CashBalance = baseline;
        world.ResetMutationEvidence();
        using var restored = Release1SmallCourtesyDepositTests.LoadedMission(repository, world);

        Assert.Equal(1, world.CashChanges);
        Assert.Equal(1_750f, world.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Applied, Reward(restored).Phase);
    }

    [Fact]
    public void Persisted_applied_with_baseline_cash_is_ambiguous_and_does_not_pay_again()
    {
        using var harness = Release1SmallCourtesyDepositTests.ActiveMission();
        PrepareDeposit(harness, 1_000f);
        harness.Service.TryHandleDropClosed(harness.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(harness);
        var baseline = RewardIdentity(harness).BaselineCash;
        Release1SmallCourtesyDepositTests.Save(harness);
        Assert.Equal(Release1NativeEffectPhase.Applied, Reward(harness).Phase);
        harness.World.CashBalance = baseline;
        harness.World.ResetMutationEvidence();

        Release1SmallCourtesyDepositTests.Save(harness);

        Assert.Equal(Release1SmallCourtesyRewardStatus.Ambiguous, harness.Service.ReconcileReward());
        Assert.Equal(0, harness.World.CashChanges);
        Assert.Equal(baseline, harness.World.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Applied, Reward(harness).Phase);
    }

    private static void PrepareDeposit(Release1SmallCourtesyDepositTests.Harness harness, float monetaryValue)
    {
        harness.World.Slots[0] = harness.World.Slots[0] with
        {
            ProductId = harness.Assignment.ProductId,
            PackagingId = harness.Assignment.PackagingId,
            Quantity = 2,
            IsPackaged = true,
            MonetaryValue = monetaryValue
        };
    }

    private static Release1NativeEffectJournalEntry Cargo(Release1SmallCourtesyDepositTests.Harness harness) =>
        harness.Story.State!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer");

    private static Release1NativeEffectJournalEntry Reward(Release1SmallCourtesyDepositTests.Harness harness) =>
        harness.Story.State!.NativeEffects.Single(effect => effect.EffectKind == "Reward");

    private static Release1SmallCourtesyRewardIdentity RewardIdentity(Release1SmallCourtesyDepositTests.Harness harness)
    {
        Assert.True(Release1SmallCourtesyRewardIdentity.TryParse(Reward(harness).AmountOrCargoIdentity, out var identity));
        return identity!;
    }

    private static Release1StoryHostContextSnapshot ContextSnapshot() =>
        new(Guid.Parse("52525252-5252-5252-5252-525252525252"), 1, "76561190000000001", Path.GetTempPath());
}
