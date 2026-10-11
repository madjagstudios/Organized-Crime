using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-117: warehouse staff capacity tiers 12 / 16 / 20. The tier table and
/// unlock rules, the Nell payment, the host attach and live raise behind
/// fakes, the schema 14 migration, the presentation transcript, the owner
/// QA commands, and the own-to-20 end to end.
/// </summary>
public sealed class Chapter3WarehouseCapacityTests
{
    private const string PlayerId = "76561190000000001";

    // ---- Tier model ----

    [Fact]
    public void Tier_table_holds_12_16_20()
    {
        Assert.Equal(12, Chapter3WarehouseCapacityTiers.CapacityForTier(1));
        Assert.Equal(16, Chapter3WarehouseCapacityTiers.CapacityForTier(2));
        Assert.Equal(20, Chapter3WarehouseCapacityTiers.CapacityForTier(3));
        Assert.Equal(12, Chapter3WarehouseCapacityTiers.Tier1Capacity);
        Assert.True(Chapter3WarehouseCapacityTiers.IsValidTier(1));
        Assert.True(Chapter3WarehouseCapacityTiers.IsValidTier(3));
        Assert.False(Chapter3WarehouseCapacityTiers.IsValidTier(0));
        Assert.False(Chapter3WarehouseCapacityTiers.IsValidTier(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3WarehouseCapacityTiers.CapacityForTier(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3WarehouseCapacityTiers.CapacityForTier(4));
    }

    [Fact]
    public void Unlock_thresholds_are_zero_one_three()
    {
        Assert.Equal(0, Chapter3WarehouseCapacityTiers.UnlockThresholdForTier(1));
        Assert.Equal(1, Chapter3WarehouseCapacityTiers.UnlockThresholdForTier(2));
        Assert.Equal(3, Chapter3WarehouseCapacityTiers.UnlockThresholdForTier(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3WarehouseCapacityTiers.UnlockThresholdForTier(0));
    }

    [Fact]
    public void Tier_prices_are_50k_and_100k_under_the_payment_bound()
    {
        Assert.Equal(50_000, Chapter3PaymentTuning.WarehouseCapacityTier2Price);
        Assert.Equal(100_000, Chapter3PaymentTuning.WarehouseCapacityTier3Price);
        Assert.Equal(50_000, Chapter3PaymentTuning.CapacityPriceForTier(2));
        Assert.Equal(100_000, Chapter3PaymentTuning.CapacityPriceForTier(3));
        Assert.True(Chapter3PaymentTuning.WarehouseCapacityTier3Price <= Chapter3PaymentTuning.MaximumPayment);
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3PaymentTuning.CapacityPriceForTier(1));
    }

    [Fact]
    public void Tier2_unlocks_on_the_first_completed_demand_once_purchased()
    {
        Assert.Equal(2, Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 1, demandsCompleted: 1));
        Assert.Equal(2, Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 1, demandsCompleted: 5));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 1, demandsCompleted: 0));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: false, tier: 1, demandsCompleted: 1));
    }

    [Fact]
    public void Tier3_unlocks_on_three_completed_and_follows_tier2_immediately_when_earned()
    {
        Assert.Equal(3, Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 2, demandsCompleted: 3));
        Assert.Equal(3, Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 2, demandsCompleted: 9));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 2, demandsCompleted: 2));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: false, tier: 2, demandsCompleted: 3));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 3, demandsCompleted: 9));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 0, demandsCompleted: 9));
        Assert.Null(Chapter3WarehouseCapacityTiers.OpenOfferTier(storyPurchased: true, tier: 1, demandsCompleted: -1));
    }

    // ---- Completed-demand counter ----

    [Fact]
    public void Completed_demand_increments_the_counter()
    {
        var harness = new Harness();
        harness.Repository.Stored = ActiveState() with { ActiveDemand = DeliveredDemand("d1") };
        harness.Activate();
        Assert.Equal(0, harness.Service.State!.DemandsCompleted);

        harness.TickTo(12);

        var state = harness.Service.State!;
        Assert.Equal(1, state.DemandsCompleted);
        Assert.Equal(new Chapter3DemandOutcome("d1", "OG Kush", LeftUnits: 0, Amount: 6_250, Completed: true), state.LastDemandOutcome);
    }

    [Fact]
    public void Missed_demand_never_counts()
    {
        var harness = new Harness();
        harness.Repository.Stored = ActiveState() with { ActiveDemand = ShortDemand("d1") };
        harness.Activate();

        harness.TickTo(12);

        var state = harness.Service.State!;
        Assert.Equal(0, state.DemandsCompleted);
        Assert.NotNull(state.LastDemandOutcome);
        Assert.False(state.LastDemandOutcome!.Completed);
        Assert.True(state.Ledger.HasOpenMakeGood);
    }

    // ---- Payment ----

    [Fact]
    public void Buy_applies_the_tier_records_and_raises_live()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.World.CashBalance = 60_000f;

        var result = harness.Service.TryBuyWarehouseCapacity();

        Assert.Equal(Chapter3MoneyOutcome.Applied, result.Outcome);
        Assert.Equal(50_000, result.Amount);
        Assert.Equal(2, harness.Service.State!.WarehouseCapacityTier);
        Assert.Equal(new[] { 16 }, harness.Capacity.Applied);
        Assert.Equal(new[] { -50_000f }, harness.World.CashChanges);
        Assert.Equal(10_000f, harness.World.CashBalance);
        Assert.Contains(harness.Logs, line => line.Contains("capacity tier 2 purchased for $50,000", StringComparison.Ordinal));
    }

    [Fact]
    public void Buy_short_counts_the_shortfall_and_keeps_the_offer()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.World.CashBalance = 10_000f;

        var result = harness.Service.TryBuyWarehouseCapacity();

        Assert.Equal(Chapter3MoneyOutcome.Short, result.Outcome);
        Assert.Equal(1, harness.Service.State!.PaymentShortfallCount);
        Assert.Equal(1, harness.Service.State!.WarehouseCapacityTier);
        Assert.Empty(harness.World.CashChanges);
        Assert.Empty(harness.Capacity.Applied);
        var plan = Chapter3PresentationPlanner.Build(harness.Service.State!, release1HasNellDecision: false, warehouseOwned: true);
        Assert.Contains(plan.Decision!.Options, option => option.Label == "Expand warehouse ($50,000)");
    }

    [Fact]
    public void Buy_refuses_while_a_make_good_is_open()
    {
        var harness = new Harness();
        var owing = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 1, Chapter3JobType.DeadDrop, 1_000, chapterDay: 0);
        harness.Repository.Stored = PurchasedState(demandsCompleted: 1) with { Ledger = owing.Ledger };
        harness.Activate();
        harness.World.CashBalance = 60_000f;

        var result = harness.Service.TryBuyWarehouseCapacity();

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, result.Outcome);
        Assert.Equal(1, harness.Service.State!.WarehouseCapacityTier);
        Assert.Empty(harness.World.CashChanges);
        Assert.Empty(harness.Capacity.Applied);
    }

    [Fact]
    public void Buy_refuses_when_the_warehouse_is_not_attached()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.World.CashBalance = 60_000f;
        harness.Capacity.Attached = false;

        var result = harness.Service.TryBuyWarehouseCapacity();

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Equal(1, harness.Service.State!.WarehouseCapacityTier);
        Assert.Empty(harness.World.CashChanges);
        Assert.Contains(harness.Logs, line => line.Contains("not attached", StringComparison.Ordinal));
    }

    [Fact]
    public void Buy_refuses_when_the_warehouse_is_not_owned()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.World.CashBalance = 60_000f;
        harness.Acquisition.Owned = false;

        var result = harness.Service.TryBuyWarehouseCapacity();

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Equal(1, harness.Service.State!.WarehouseCapacityTier);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Buy_refuses_at_the_top_tier_and_below_the_threshold()
    {
        var top = PurchasedHarness(demandsCompleted: 9, tier: 3);
        top.World.CashBalance = 200_000f;
        Assert.Equal(Chapter3MoneyOutcome.NotPayable, top.Service.TryBuyWarehouseCapacity().Outcome);
        Assert.Empty(top.World.CashChanges);

        var locked = PurchasedHarness(demandsCompleted: 0);
        locked.World.CashBalance = 200_000f;
        Assert.Equal(Chapter3MoneyOutcome.NotPayable, locked.Service.TryBuyWarehouseCapacity().Outcome);
        Assert.Empty(locked.World.CashChanges);

        var unpurchased = new Harness();
        unpurchased.Activate();
        unpurchased.World.CashBalance = 200_000f;
        Assert.Equal(Chapter3MoneyOutcome.NotPayable, unpurchased.Service.TryBuyWarehouseCapacity().Outcome);
    }

    [Fact]
    public void Expand_labels_never_collide_with_pay_labels()
    {
        var expand2 = Chapter3NellCopy.ExpandWarehouseLabel(50_000);
        var expand3 = Chapter3NellCopy.ExpandWarehouseLabel(100_000);

        Assert.Equal("Expand warehouse ($50,000)", expand2);
        Assert.Equal("Expand warehouse ($100,000)", expand3);
        Assert.NotEqual(expand2, expand3);
        foreach (var amount in new[] { 1_000, 17_000, 30_000, 50_000, 100_000, 300_000 })
            Assert.NotEqual(Chapter3NellCopy.PayLabel(amount), expand2);
        foreach (var amount in new[] { 1_000, 17_000, 30_000, 50_000, 100_000, 300_000 })
            Assert.NotEqual(Chapter3NellCopy.PayLabel(amount), expand3);
        Assert.NotEqual(Chapter3NellCopy.BuyWarehouseLabel(300_000), expand2);
        Assert.NotEqual(Chapter3NellCopy.AcceptLabel, expand2);
        Assert.NotEqual(Chapter3NellCopy.DeclineLabel, expand3);
    }

    [Fact]
    public void Router_routes_the_capacity_command()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.World.CashBalance = 60_000f;

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCapacityCommand());

        Assert.True(result.Handled);
        Assert.Equal("warehouse capacity bought", result.Detail);
        Assert.Equal(2, harness.Service.State!.WarehouseCapacityTier);
    }

    [Fact]
    public void Router_reports_stale_without_an_offer()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCapacityCommand());

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    // ---- Host behind fakes ----

    [Fact]
    public void Attach_at_tier1_hands_12_from_20_created()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var target = new FakeTarget();
        var source = new FakeSource { TierCapacity = 12 };
        var logs = new List<string>();

        Assert.True(host.TryAttach(target, source, logs.Add));
        Assert.True(host.IsReady);
        Assert.Equal(12, host.AppliedCapacity);
        Assert.Equal(12, target.EmployeeCapacity);
        Assert.Equal(12, target.IdleSlice!.Count);
        Assert.Equal(21, target.Created.Count);
        Assert.Equal(20, host.CreatedIdlePoints.Count);
        Assert.True(target.SpawnPointRetained(target.Spawn!));
        Assert.Equal(
            target.Created.Skip(1).Select(point => point.Name),
            host.CreatedIdlePoints.Select(point => point.Name));
        Assert.Contains(logs, line => line.Contains("capacity=12, idlePoints=12", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("OC_FishWarehouse_NPCSpawnPoint", StringComparison.Ordinal));

        Assert.True(host.TryAttach(target, source, logs.Add));
        Assert.Equal(21, target.Created.Count);
    }

    [Fact]
    public void Live_raise_to_16_and_20_reuses_the_same_handles()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var target = new FakeTarget();
        var logs = new List<string>();
        Assert.True(host.TryAttach(target, new FakeSource { TierCapacity = 12 }, logs.Add));
        var firstTwelve = target.IdleSlice!.ToArray();

        Assert.True(host.TryRaiseCapacity(16, logs.Add));
        Assert.Equal(16, host.AppliedCapacity);
        Assert.Equal(16, target.EmployeeCapacity);
        Assert.Equal(16, target.IdleSlice!.Count);
        Assert.All(
            Enumerable.Range(0, 12),
            index => Assert.Same(firstTwelve[index], target.IdleSlice[index]));
        Assert.Contains(logs, line => line.Contains("capacity raised 12 -> 16", StringComparison.Ordinal));

        var firstSixteen = target.IdleSlice.ToArray();
        Assert.True(host.TryRaiseCapacity(20, logs.Add));
        Assert.Equal(20, target.IdleSlice.Count);
        Assert.All(
            Enumerable.Range(0, 16),
            index => Assert.Same(firstSixteen[index], target.IdleSlice[index]));
        Assert.Equal(target.Created.Skip(1), target.IdleSlice);
        Assert.Contains(logs, line => line.Contains("capacity raised 16 -> 20", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_source_attaches_at_tier1()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var target = new FakeTarget();

        Assert.False(host.TryRaiseCapacity(16));
        Assert.True(host.TryAttach(target));
        Assert.Equal(12, host.AppliedCapacity);
        Assert.Equal(12, target.EmployeeCapacity);
        Assert.Equal(12, target.IdleSlice!.Count);
    }

    [Fact]
    public void Saved_replay_lifts_capacity_capped_at_20_with_a_warning()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var target = new FakeTarget();
        var logs = new List<string>();

        Assert.True(host.TryAttach(target, new FakeSource { TierCapacity = 12, PendingCount = 15 }, logs.Add));
        Assert.Equal(15, host.AppliedCapacity);
        Assert.Equal(15, target.IdleSlice!.Count);
        Assert.Contains(logs, line => line.Contains("tier allows 12", StringComparison.Ordinal) && line.Contains("15", StringComparison.Ordinal));

        var crowded = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var crowdedTarget = new FakeTarget { HeldEmployeeCount = 25 };
        var crowdedLogs = new List<string>();
        Assert.True(crowded.TryAttach(crowdedTarget, new FakeSource { TierCapacity = 12 }, crowdedLogs.Add));
        Assert.Equal(20, crowded.AppliedCapacity);
        Assert.Equal(20, crowdedTarget.IdleSlice!.Count);
        Assert.Contains(crowdedLogs, line => line.Contains("tier allows 12", StringComparison.Ordinal) && line.Contains("25", StringComparison.Ordinal));
    }

    [Fact]
    public void Raise_never_drops_below_the_held_employees()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();
        var target = new FakeTarget();
        var logs = new List<string>();
        Assert.True(host.TryAttach(target, new FakeSource { TierCapacity = 20 }, logs.Add));

        target.HeldEmployeeCount = 20;
        Assert.True(host.TryRaiseCapacity(12, logs.Add));
        Assert.Equal(20, host.AppliedCapacity);
        Assert.Equal(20, target.IdleSlice!.Count);

        target.HeldEmployeeCount = 18;
        Assert.True(host.TryRaiseCapacity(16, logs.Add));
        Assert.Equal(18, host.AppliedCapacity);
        Assert.Equal(18, target.IdleSlice!.Count);
        Assert.Contains(logs, line => line.Contains("tier allows 16", StringComparison.Ordinal) && line.Contains("18", StringComparison.Ordinal));
    }

    [Fact]
    public void Attach_refuses_a_null_target()
    {
        var host = new FishWarehouseEmployeeInfrastructureHost<FakePoint>();

        Assert.False(host.TryAttach(null));
        Assert.False(host.IsReady);
    }

    // ---- Persistence ----

    [Fact]
    public void V13_with_a_completed_outcome_migrates_to_tier1_with_one_completion()
    {
        var state = PurchasedState(demandsCompleted: 0) with
        {
            LastDemandOutcome = new Chapter3DemandOutcome("d9", "OG Kush", LeftUnits: 0, Amount: 6_250, Completed: true)
        };

        var envelope = RoundTripThroughSchema(state, targetSchemaVersion: 13);

        Assert.Equal(1, envelope!.State!.WarehouseCapacityTier);
        Assert.Equal(1, envelope.State.DemandsCompleted);
        Assert.Equal("d9", envelope.State.LastDemandOutcome!.DemandId);
    }

    [Fact]
    public void V13_without_a_completed_outcome_migrates_to_zero()
    {
        var completed = PurchasedState(demandsCompleted: 0) with
        {
            LastDemandOutcome = new Chapter3DemandOutcome("d9", "OG Kush", LeftUnits: 60, Amount: 3_000, Completed: false)
        };
        var migrated = RoundTripThroughSchema(completed, targetSchemaVersion: 13);
        Assert.Equal(1, migrated!.State!.WarehouseCapacityTier);
        Assert.Equal(0, migrated.State.DemandsCompleted);

        var none = RoundTripThroughSchema(PurchasedState(demandsCompleted: 0), targetSchemaVersion: 13);
        Assert.Equal(1, none!.State!.WarehouseCapacityTier);
        Assert.Equal(0, none.State.DemandsCompleted);
    }

    [Fact]
    public void V1_loads_with_tier1_and_zero()
    {
        const string json = """{"schemaVersion":1,"state":{"playerId":"76561190000000001","phase":"Active","startElapsedDay":11,"lastProcessedElapsedDay":11,"revision":0}}""";

        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var result), result.Message);

        Assert.Equal(1, envelope!.State!.WarehouseCapacityTier);
        Assert.Equal(0, envelope.State.DemandsCompleted);
        Assert.Equal(Chapter3Phase.Active, envelope.State.Phase);
    }

    [Fact]
    public void Round_trip_preserves_tier_and_counter()
    {
        var state = PurchasedState(demandsCompleted: 3, tier: 3);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serializeResult), serializeResult.Message);
        Assert.Contains("\"warehouseCapacityTier\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"demandsCompleted\": 3", json, StringComparison.Ordinal);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var result), result.Message);

        Assert.Equal(state, envelope!.State);
    }

    // ---- Presentation ----

    [Fact]
    public void Tier2_offer_text_button_and_quest_line()
    {
        var plan = Chapter3PresentationPlanner.Build(
            PurchasedState(demandsCompleted: 1), release1HasNellDecision: false, warehouseOwned: true);

        Assert.DoesNotContain(plan.Messages, message => message.Text == Chapter3NellCopy.WarehouseCapacityTier2OfferText);
        Assert.Contains(plan.Decision!.Options, option => option.Label == "Expand warehouse ($50,000)");
        Assert.Contains("capacity", plan.Decision.Id, StringComparison.Ordinal);
        Assert.Equal(Chapter3NellCopy.WarehouseCapacityTier2OfferText, plan.Decision.Prompt);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "capacity-2-offer"), plan.Decision.PromptCorrelationId);
        var entry = Assert.Single(plan.Quest!.Entries);
        Assert.Equal("Warehouse expansion: 16 staff for $50,000 (reply to Nell)", entry.Text);
        Assert.Null(entry.Marker);
    }

    [Fact]
    public void Tier2_paid_text_clears_the_button_and_the_quest_line()
    {
        var plan = Chapter3PresentationPlanner.Build(
            PurchasedState(demandsCompleted: 1, tier: 2), release1HasNellDecision: false, warehouseOwned: true);

        Assert.Contains(
            plan.Messages,
            message => message.Text == "Done. The warehouse takes 16 now. Go hire them, and buy them somewhere to sleep.");
        Assert.DoesNotContain(plan.Decision?.Options ?? Array.Empty<Chapter3DecisionOption>(), option => option.Label.StartsWith("Expand warehouse", StringComparison.Ordinal));
        Assert.Null(plan.Quest);
    }

    [Fact]
    public void Tier3_offer_and_paid_transcript()
    {
        var offer = Chapter3PresentationPlanner.Build(
            PurchasedState(demandsCompleted: 3, tier: 2), release1HasNellDecision: false, warehouseOwned: true);

        Assert.DoesNotContain(offer.Messages, message => message.Text == Chapter3NellCopy.WarehouseCapacityTier3OfferText);
        Assert.Contains(offer.Decision!.Options, option => option.Label == "Expand warehouse ($100,000)");
        Assert.Equal(Chapter3NellCopy.WarehouseCapacityTier3OfferText, offer.Decision.Prompt);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "capacity-3-offer"), offer.Decision.PromptCorrelationId);
        var entry = Assert.Single(offer.Quest!.Entries);
        Assert.Equal("Warehouse expansion: 20 staff for $100,000 (reply to Nell)", entry.Text);

        var paid = Chapter3PresentationPlanner.Build(
            PurchasedState(demandsCompleted: 3, tier: 3), release1HasNellDecision: false, warehouseOwned: true);

        Assert.Contains(
            paid.Messages,
            message => message.Text == "Done. The warehouse takes 20 now. Go hire them, and buy them somewhere to sleep.");
        Assert.DoesNotContain(paid.Decision?.Options ?? Array.Empty<Chapter3DecisionOption>(), option => option.Label.StartsWith("Expand warehouse", StringComparison.Ordinal));
        Assert.Null(paid.Quest);
    }

    [Fact]
    public void Receipted_offer_reprompts_on_load()
    {
        var state = PurchasedState(demandsCompleted: 1) with
        {
            PresentationReceipts = new[]
            {
                new Chapter3PresentationReceipt(Chapter3PresentationReceipt.Correlation(PlayerId, "capacity-2-offer"), 1)
            }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: true);

        Assert.DoesNotContain(plan.Messages, message => message.Text == Chapter3NellCopy.WarehouseCapacityTier2OfferText);
        Assert.Contains(plan.Decision!.Options, option => option.Label == "Expand warehouse ($50,000)");
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "capacity-2-offer"), plan.Decision.PromptCorrelationId);
        Assert.Equal(Chapter3NellCopy.WarehouseCapacityRepromptText, plan.Decision.RepromptText);
        Assert.NotNull(plan.Quest);
    }

    // ---- Owner QA ----

    [Fact]
    public void QaCapacityStatus_reports_tier_counter_offer_and_live()
    {
        var harness = PurchasedHarness(demandsCompleted: 1);
        harness.Capacity.LiveCapacity = 12;
        harness.Capacity.LiveIdlePoints = 12;

        var line = Assert.Single(harness.Service.QaCapacityStatus());

        Assert.Contains("tier 1 (12 staff)", line, StringComparison.Ordinal);
        Assert.Contains("completed demands 1", line, StringComparison.Ordinal);
        Assert.Contains("open offer tier 2 (16 staff for $50,000)", line, StringComparison.Ordinal);
        Assert.Contains("live capacity=12, live idle points=12", line, StringComparison.Ordinal);
    }

    [Fact]
    public void QaCapacityStatus_reports_unknown_live_when_detached()
    {
        var harness = PurchasedHarness(demandsCompleted: 3, tier: 3);
        harness.Capacity.Attached = false;

        var line = Assert.Single(harness.Service.QaCapacityStatus());

        Assert.Contains("tier 3 (20 staff)", line, StringComparison.Ordinal);
        Assert.Contains("open offer none", line, StringComparison.Ordinal);
        Assert.Contains("live capacity unknown", line, StringComparison.Ordinal);
    }

    [Fact]
    public void QaCapacityUnlock_raises_to_the_next_threshold()
    {
        var first = PurchasedHarness(demandsCompleted: 0);
        var unlocked = first.Service.QaCapacityUnlock();
        Assert.True(unlocked.Ok, unlocked.Message);
        Assert.Equal(1, first.Service.State!.DemandsCompleted);

        var second = PurchasedHarness(demandsCompleted: 1, tier: 2);
        var again = second.Service.QaCapacityUnlock();
        Assert.True(again.Ok, again.Message);
        Assert.Equal(3, second.Service.State!.DemandsCompleted);

        var top = PurchasedHarness(demandsCompleted: 9, tier: 3);
        Assert.False(top.Service.QaCapacityUnlock().Ok);
    }

    [Fact]
    public void QaCapacitySet_applies_live_with_no_charge()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);
        harness.World.CashBalance = 10_000f;

        var result = harness.Service.QaCapacitySet(3);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(3, harness.Service.State!.WarehouseCapacityTier);
        Assert.Equal(new[] { 20 }, harness.Capacity.Applied);
        Assert.Empty(harness.World.CashChanges);
        Assert.False(harness.Service.QaCapacitySet(4).Ok);
        Assert.False(harness.Service.QaCapacitySet(0).Ok);
    }

    [Fact]
    public void Capacity_command_dispatches_status_unlock_and_set()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var status = Chapter3QaCommand.Execute(harness.Service, new[] { "capacity" }, qaEnabled: true);
        Assert.Contains(status, line => line.Contains("tier 1 (12 staff)", StringComparison.Ordinal));

        var unlock = Chapter3QaCommand.Execute(harness.Service, new[] { "capacity", "unlock" }, qaEnabled: true);
        Assert.Equal(new[] { harness.Service.QaCapacityUnlock().Message }, unlock);

        var set = Chapter3QaCommand.Execute(harness.Service, new[] { "capacity", "set", "3" }, qaEnabled: true);
        Assert.Contains("tier 3", Assert.Single(set), StringComparison.Ordinal);
        Assert.Equal(3, harness.Service.State!.WarehouseCapacityTier);

        Assert.Equal(
            new[] { "Usage: oc_ch3 capacity set <1 | 2 | 3>" },
            Chapter3QaCommand.Execute(harness.Service, new[] { "capacity", "set" }, qaEnabled: true));
        Assert.Equal(
            new[] { "Unknown oc_ch3 capacity subcommand: 'bogus'." },
            Chapter3QaCommand.Execute(harness.Service, new[] { "capacity", "bogus" }, qaEnabled: true));
        Assert.Contains(
            "oc_ch3 capacity [unlock | set <1 | 2 | 3> | here]",
            Chapter3QaCommand.Execute(harness.Service, Array.Empty<string>(), qaEnabled: true));
    }

    [Fact]
    public void Capacity_here_moves_the_group_and_logs_the_positions()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var lines = Chapter3QaCommand.Execute(harness.Service, new[] { "capacity", "here" }, qaEnabled: true);

        Assert.Equal(1, harness.Capacity.Moves);
        Assert.Contains("idle group placed at the player", Assert.Single(lines), StringComparison.Ordinal);
        Assert.Contains(harness.Logs, line => line.Contains("[0] (1.00, 0.00, 2.00)", StringComparison.Ordinal));
    }

    [Fact]
    public void Capacity_here_refuses_a_detached_warehouse()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);
        harness.Capacity.Attached = false;

        var result = harness.Service.QaCapacityHere();

        Assert.False(result.Ok);
        Assert.Equal(0, harness.Capacity.Moves);
    }

    [Fact]
    public void Warehouse_spawns_reports_both_points_and_their_retention()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var line = Assert.Single(harness.Service.QaWarehouseSpawns());

        Assert.Equal("warehouse spawns: exterior (1.20, 0.00, 9.90) retained yes, interior (-8.40, 0.00, 4.80) retained yes", line);
    }

    [Fact]
    public void Warehouse_spawns_reports_an_unassigned_point()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);
        harness.Capacity.SpawnSnapshot = harness.Capacity.SpawnSnapshot with { InteriorAssigned = false, InteriorRetained = false };

        var line = Assert.Single(harness.Service.QaWarehouseSpawns());

        Assert.Equal("warehouse spawns: exterior (1.20, 0.00, 9.90) retained yes, interior unassigned", line);
    }

    [Fact]
    public void Warehouse_spawns_reports_a_detached_warehouse()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);
        harness.Capacity.Attached = false;

        var line = Assert.Single(harness.Service.QaWarehouseSpawns());

        Assert.Equal("warehouse spawns: the warehouse is not attached.", line);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spawn_here_moves_the_chosen_side_through_the_warehouse(bool exterior)
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var result = harness.Service.QaWarehouseSpawnHere(exterior);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(new[] { exterior }, harness.Capacity.SpawnMoves);
        Assert.Equal(harness.Capacity.SpawnMoveReport, result.Message);
    }

    [Fact]
    public void Spawn_here_refuses_a_detached_warehouse()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);
        harness.Capacity.Attached = false;

        var result = harness.Service.QaWarehouseSpawnHere(exterior: true);

        Assert.False(result.Ok);
        Assert.Empty(harness.Capacity.SpawnMoves);
    }

    [Fact]
    public void Warehouse_spawns_flows_through_the_qa_command()
    {
        var harness = PurchasedHarness(demandsCompleted: 0);

        var lines = Chapter3QaCommand.Execute(harness.Service, new[] { "warehouse", "spawns" }, qaEnabled: true);

        Assert.Contains(lines, line => line.StartsWith("warehouse spawns: exterior", StringComparison.Ordinal));
    }

    // ---- Load-order reconcile ----

    [Fact]
    public void Poll_raises_a_warehouse_that_attached_below_the_saved_tier()
    {
        // The warehouse attached before Chapter 3 turned Active, so it read
        // tier 1; the saved tier is 2.
        var harness = PurchasedHarness(demandsCompleted: 1, tier: 2);
        harness.Capacity.LiveCapacity = 12;

        harness.Service.PollProductionDemand();
        harness.Service.PollProductionDemand();

        Assert.Equal(new[] { 16 }, harness.Capacity.Applied);
        Assert.Contains(harness.Logs, line => line.Contains("live 12 was below tier 2, raised to 16", StringComparison.Ordinal));
    }

    [Fact]
    public void Poll_never_lowers_a_replay_lifted_capacity()
    {
        var harness = PurchasedHarness(demandsCompleted: 0, tier: 1);
        harness.Capacity.LiveCapacity = 14;

        harness.Service.PollProductionDemand();

        Assert.Empty(harness.Capacity.Applied);
    }

    [Fact]
    public void Poll_skips_a_detached_warehouse()
    {
        var harness = PurchasedHarness(demandsCompleted: 1, tier: 2);
        harness.Capacity.Attached = false;
        harness.Capacity.LiveCapacity = 10;

        harness.Service.PollProductionDemand();

        Assert.Empty(harness.Capacity.Applied);
    }

    // ---- End to end ----

    [Fact]
    public void Own_complete_pay_repeat_reload_keeps_20()
    {
        var first = StoredHarness(PurchasedState(demandsCompleted: 0) with { ActiveDemand = DeliveredDemand("d1") });
        first.World.CashBalance = 1_000_000f;
        Assert.Equal(0, first.Service.State!.DemandsCompleted);

        first.TickTo(12);
        Assert.Equal(1, first.Service.State!.DemandsCompleted);
        Assert.Contains(
            Chapter3PresentationPlanner.Build(first.Service.State!, release1HasNellDecision: false, warehouseOwned: true).Decision!.Options,
            option => option.Label == "Expand warehouse ($50,000)");
        Assert.Equal(Chapter3MoneyOutcome.Applied, first.Service.TryBuyWarehouseCapacity().Outcome);
        Assert.Equal(2, first.Service.State!.WarehouseCapacityTier);
        Assert.Equal(new[] { 16 }, first.Capacity.Applied);

        var second = StoredHarness(first.Service.State! with { ActiveDemand = DeliveredDemand("d2") });
        second.World.CashBalance = first.World.CashBalance;
        second.TickTo(13);
        Assert.Equal(2, second.Service.State!.DemandsCompleted);
        Assert.Equal(Chapter3MoneyOutcome.NotPayable, second.Service.TryBuyWarehouseCapacity().Outcome);

        var third = StoredHarness(second.Service.State! with { ActiveDemand = DeliveredDemand("d3") });
        third.World.CashBalance = second.World.CashBalance;
        third.TickTo(14);
        Assert.Equal(3, third.Service.State!.DemandsCompleted);
        Assert.Equal(Chapter3MoneyOutcome.Applied, third.Service.TryBuyWarehouseCapacity().Outcome);
        Assert.Equal(3, third.Service.State!.WarehouseCapacityTier);
        Assert.Equal(new[] { 20 }, third.Capacity.Applied);

        var reloaded = StoredHarness(third.Service.State! with { ActiveDemand = null });
        Assert.Equal(3, reloaded.Service.State!.WarehouseCapacityTier);
        Assert.Equal(3, reloaded.Service.State!.DemandsCompleted);
        Assert.Equal(20, reloaded.Service.ReadWarehouseTierCapacity());
        var plan = Chapter3PresentationPlanner.Build(reloaded.Service.State!, release1HasNellDecision: false, warehouseOwned: true);
        Assert.DoesNotContain(
            plan.Decision?.Options ?? Array.Empty<Chapter3DecisionOption>(),
            option => option.Label.StartsWith("Expand warehouse", StringComparison.Ordinal));
        Assert.Null(plan.Quest);
    }

    // ---- Helpers ----

    private static Chapter3State ActiveState() =>
        Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;

    private static Chapter3State PurchasedState(int demandsCompleted, int tier = 1) =>
        ActiveState() with
        {
            JobBoard = Chapter3JobBoard.Empty,
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 0, LastReminderWeek: 0, Purchased: true, PurchasedDay: 0),
            WarehouseCapacityTier = tier,
            DemandsCompleted = demandsCompleted
        };

    private static Harness PurchasedHarness(int demandsCompleted, int tier = 1) =>
        StoredHarness(PurchasedState(demandsCompleted, tier));

    private static Harness StoredHarness(Chapter3State stored)
    {
        var harness = new Harness();
        harness.Repository.Stored = stored;
        harness.Activate();
        return harness;
    }

    private static Chapter3ProductionDemand DeliveredDemand(string demandId) => new(
        DemandId: demandId,
        ProductId: "ogkush",
        ProductName: "OG Kush",
        ProductType: Chapter3DemandProductType.Weed,
        Quantity: 250,
        UnitValue: 50,
        IssueDay: 0,
        DueDay: 3,
        FreshCount: 250,
        Delivered: 250,
        PaidTotal: 6_250,
        LastTakeUnits: 250,
        HandOverCount: 1,
        HqDoorX: null,
        HqDoorY: null,
        HqDoorZ: null,
        PendingRemoval: null);

    private static Chapter3ProductionDemand ShortDemand(string demandId) => DeliveredDemand(demandId) with
    {
        FreshCount = 0,
        Delivered = 0,
        PaidTotal = 0,
        LastTakeUnits = 0,
        HandOverCount = 0,
        DueDay = 0
    };

    private static Chapter3SaveEnvelope? RoundTripThroughSchema(Chapter3State state, int targetSchemaVersion)
    {
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serializeResult), serializeResult.Message);
        var node = JsonNode.Parse(json)!.AsObject();
        node["state"]!.AsObject().Remove("sweep");
        node["state"]!.AsObject().Remove("lastSweepStartMinutes");
        node["schemaVersion"] = targetSchemaVersion;
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["state"]!.AsObject().Remove("warehouseCapacityTier");
        node["state"]!.AsObject().Remove("demandsCompleted");
        node["state"]!.AsObject().Remove("checkpointTargetCode");
        node["state"]!.AsObject().Remove("checkpointTargetDay");
        Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var envelope, out var result), result.Message);
        return envelope;
    }

    private sealed class Harness
    {
        public DeadDropFakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public FakeAcquisition Acquisition { get; } = new() { Owned = true };
        public FakeCapacity Capacity { get; } = new();
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }
        public Chapter3PresentationCommandRouter Router { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = new Chapter3Wallet(World);
            Service.DeadDropWorld = World;
            Service.WarehouseAcquisition = Acquisition;
            Service.WarehouseCapacity = Capacity;
            Router = new Chapter3PresentationCommandRouter(Service);
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }

        public void TickTo(int elapsedDays)
        {
            Clock.ElapsedDays = elapsedDays;
            Service.OnDayPassed();
        }
    }

    private sealed record FakePoint(string Name);

    private sealed class FakeTarget : IFishWarehouseEmployeeInfrastructureTarget<FakePoint>
    {
        public int EmployeeCapacity { get; set; }
        public int HeldEmployeeCount { get; set; }
        public List<FakePoint> Created { get; } = new();
        public FakePoint? Spawn { get; private set; }
        public List<FakePoint>? IdleSlice { get; private set; }
        public bool TornDown { get; private set; }

        public FakePoint CreatePoint(string name, float x, float y, float z)
        {
            var point = new FakePoint(name);
            Created.Add(point);
            return point;
        }

        public void SetSpawnPoint(FakePoint spawn) => Spawn = spawn;

        public bool SpawnPointRetained(FakePoint spawn) => ReferenceEquals(Spawn, spawn);

        public void SetIdlePoints(IReadOnlyList<FakePoint> points) => IdleSlice = points.ToList();

        public int ReadIdlePointCount() => IdleSlice?.Count ?? -1;

        public string PointName(FakePoint point) => point.Name;

        public void Teardown() => TornDown = true;
    }

    private sealed class FakeSource : IFishWarehouseEmployeeCapacitySource
    {
        public int TierCapacity { get; set; } = Chapter3WarehouseCapacityTiers.Tier1Capacity;
        public int PendingCount { get; set; }
        public int ReadTierCapacity() => TierCapacity;
        public int ReadPendingEmployeeCount() => PendingCount;
    }

    private sealed class FakeCapacity : IChapter3WarehouseCapacity
    {
        public bool Attached { get; set; } = true;
        public List<int> Applied { get; } = new();
        public int LiveCapacity { get; set; } = Chapter3WarehouseCapacityTiers.Tier1Capacity;
        public int LiveIdlePoints { get; set; } = Chapter3WarehouseCapacityTiers.Tier1Capacity;
        public bool LiveAvailable { get; set; } = true;

        public bool IsAttached() => Attached;

        public bool TryApplyCapacity(int capacity)
        {
            if (!Attached)
                return false;
            Applied.Add(capacity);
            LiveCapacity = capacity;
            LiveIdlePoints = capacity;
            return true;
        }

        public bool TryReadLive(out int capacity, out int idlePointCount)
        {
            capacity = LiveCapacity;
            idlePointCount = LiveIdlePoints;
            return LiveAvailable && Attached;
        }

        public FishWarehousePlayerSpawnQaSnapshot SpawnSnapshot { get; set; } = new(1.2f, 0f, 9.9f, true, true, -8.4f, 0f, 4.8f, true, true);
        public bool SpawnAvailable { get; set; } = true;
        public List<bool> SpawnMoves { get; } = new();
        public string SpawnMoveReport { get; set; } = "OC-139 exterior player spawn moved to the player (room-frame): (1.23, 0.00, 10.11) yaw 12.0";

        public bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot)
        {
            snapshot = SpawnSnapshot;
            return SpawnAvailable && Attached;
        }

        public bool TryMovePlayerSpawnToPlayer(bool exterior, out string report)
        {
            SpawnMoves.Add(exterior);
            report = SpawnMoveReport;
            return Attached;
        }

        public int Moves { get; private set; }

        public bool TryMoveIdleGroupToPlayer(out string report)
        {
            Moves++;
            report = "OC-117 idle group placed at the player (warehouse-local, 20 points): [0] (1.00, 0.00, 2.00)";
            return true;
        }
    }

    private sealed class FakeAcquisition : IChapter3WarehouseAcquisition
    {
        public bool Owned { get; set; }
        public bool Settled { get; set; } = true;
        public bool CanAcquireValue { get; set; } = true;
        public bool AcquireResult { get; set; } = true;
        public List<string> Calls { get; } = new();

        public bool IsOwned() => Owned;
        public bool IsOwnershipSettled() => Settled;
        public bool CanAcquire() => CanAcquireValue;

        public bool TryAcquire()
        {
            Calls.Add("acquire");
            if (AcquireResult)
                Owned = true;
            return AcquireResult;
        }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("77777777-7777-7777-7777-777777777777"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }

        public Chapter3StoreLoadResult Load() => Stored is null
            ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
            : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public bool Readable { get; set; } = true;
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return Readable;
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }
}
