using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ProductionDemandTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Policy_pins_issue_offset_duration_and_locker_name()
    {
        Assert.Equal(3, Chapter3DemandPolicy.IssueOffsetDays);
        Assert.Equal(3, Chapter3DemandPolicy.DurationDays);
        Assert.Equal("Family locker", Chapter3DemandPolicy.FamilyLockerName);
    }

    [Theory]
    [InlineData(Chapter3DemandProductType.Weed, 250)]
    [InlineData(Chapter3DemandProductType.Shrooms, 200)]
    [InlineData(Chapter3DemandProductType.Meth, 200)]
    [InlineData(Chapter3DemandProductType.Cocaine, 150)]
    [InlineData(Chapter3DemandProductType.Mdma, 200)]
    [InlineData(Chapter3DemandProductType.Heroin, 150)]
    public void Quantities_are_pinned_per_type(Chapter3DemandProductType type, int quantity)
    {
        Assert.Equal(quantity, Chapter3DemandPolicy.QuantityFor(type));
    }

    [Fact]
    public void Quantity_for_an_unknown_type_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapter3DemandPolicy.QuantityFor((Chapter3DemandProductType)99));
    }

    [Theory]
    [InlineData("Marijuana", Chapter3DemandProductType.Weed)]
    [InlineData("Shrooms", Chapter3DemandProductType.Shrooms)]
    [InlineData("Methamphetamine", Chapter3DemandProductType.Meth)]
    [InlineData("Cocaine", Chapter3DemandProductType.Cocaine)]
    [InlineData("MDMA", Chapter3DemandProductType.Mdma)]
    [InlineData("Heroin", Chapter3DemandProductType.Heroin)]
    public void Drug_type_names_map_to_demand_types(string drugType, Chapter3DemandProductType expected)
    {
        Assert.True(Chapter3DemandPolicy.TryParseProductType(drugType, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotADrug")]
    public void Unknown_drug_type_names_refuse(string? drugType)
    {
        Assert.False(Chapter3DemandPolicy.TryParseProductType(drugType, out _));
    }

    [Fact]
    public void Pick_refuses_an_empty_eligibility_set()
    {
        Assert.False(Chapter3DemandPolicy.TryPickProduct(null, 3, out _));
        Assert.False(Chapter3DemandPolicy.TryPickProduct(Array.Empty<string>(), 3, out _));
        Assert.False(Chapter3DemandPolicy.TryPickProduct(new[] { "  ", "" }, 3, out _));
    }

    [Fact]
    public void Pick_is_deterministic_and_order_insensitive()
    {
        var set = new[] { "ogkush", "sourdiesel", "greencrack" };
        var shuffled = new[] { "greencrack", "ogkush", "sourdiesel" };

        Assert.True(Chapter3DemandPolicy.TryPickProduct(set, 5, out var first));
        Assert.True(Chapter3DemandPolicy.TryPickProduct(shuffled, 5, out var second));
        Assert.True(Chapter3DemandPolicy.TryPickProduct(set, 5, out var third));

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Contains(first!, set);
    }

    [Fact]
    public void Pick_ignores_blanks_and_duplicates()
    {
        Assert.True(Chapter3DemandPolicy.TryPickProduct(new[] { "ogkush", "  ", "ogkush" }, 1, out var picked));

        Assert.Equal("ogkush", picked);
    }

    [Fact]
    public void Finishing_latch_counts_the_increase()
    {
        var before = Station("s1", running: true, identity: "op-1", progress: 40, finishing: false, ("ogkush", 10));
        var after = Station("s1", running: true, identity: "op-1", progress: 90, finishing: true, ("ogkush", 22));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", Array.Empty<string>());

        Assert.Equal(12, result.FreshGain);
        Assert.Equal(new[] { "ogkush" }, result.EligibilityAdds);
        Assert.Equal("Station s1 finished a batch: +12 ogkush counted.", Assert.Single(result.Verdicts));
    }

    [Fact]
    public void Prior_tick_latch_counts_too()
    {
        var before = Station("s1", running: false, identity: "op-1", progress: 100, finishing: true, ("ogkush", 10));
        var after = Station("s1", running: false, identity: "op-1", progress: 100, finishing: false, ("ogkush", 22));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", new[] { "ogkush" });

        Assert.Equal(12, result.FreshGain);
        Assert.Empty(result.EligibilityAdds);
    }

    [Fact]
    public void Running_to_idle_edge_counts()
    {
        var before = Station("s1", running: true, identity: "op-1", progress: 100, finishing: false, ("ogkush", 10));
        var after = Station("s1", running: false, identity: "op-1", progress: 100, finishing: false, ("ogkush", 24));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", new[] { "ogkush" });

        Assert.Equal(14, result.FreshGain);
    }

    [Fact]
    public void Identity_change_and_progress_restart_count()
    {
        var identityBefore = Station("s1", running: true, identity: "op-1", progress: 100, finishing: false, ("ogkush", 10));
        var identityAfter = Station("s1", running: true, identity: "op-2", progress: 5, finishing: false, ("ogkush", 16));
        var restartBefore = Station("s2", running: true, identity: "op-1", progress: 100, finishing: false, ("ogkush", 4));
        var restartAfter = Station("s2", running: true, identity: "op-1", progress: 3, finishing: false, ("ogkush", 9));

        var identity = Chapter3DemandCounter.Process(new[] { identityBefore }, new[] { identityAfter }, "ogkush", new[] { "ogkush" });
        var restart = Chapter3DemandCounter.Process(new[] { restartBefore }, new[] { restartAfter }, "ogkush", new[] { "ogkush" });

        Assert.Equal(6, identity.FreshGain);
        Assert.Equal(5, restart.FreshGain);
    }

    [Fact]
    public void Decreases_never_count()
    {
        var before = Station("s1", running: true, identity: "op-1", progress: 100, finishing: true, ("ogkush", 30));
        var after = Station("s1", running: false, identity: "op-1", progress: 100, finishing: false, ("ogkush", 12));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", new[] { "ogkush" });

        Assert.Equal(0, result.FreshGain);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Increase_without_a_completion_is_an_insertion_and_never_counts()
    {
        var before = Station("s1", running: false, identity: "op-1", progress: 0, finishing: false, ("ogkush", 10));
        var after = Station("s1", running: false, identity: "op-1", progress: 0, finishing: false, ("ogkush", 18));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", Array.Empty<string>());

        Assert.Equal(0, result.FreshGain);
        Assert.Empty(result.EligibilityAdds);
        Assert.Equal(
            "Station s1 output rose by 8 with no batch finishing; insertion, not counted.",
            Assert.Single(result.Verdicts));
    }

    [Fact]
    public void New_station_baselines_silently()
    {
        var after = Station("s1", running: false, identity: "", progress: 0, finishing: false, ("ogkush", 18));

        var result = Chapter3DemandCounter.Process(null, new[] { after }, "ogkush", Array.Empty<string>());

        Assert.Equal(0, result.FreshGain);
        Assert.Empty(result.EligibilityAdds);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void First_sight_with_the_latch_set_still_baselines()
    {
        var after = Station("s1", running: false, identity: "op-1", progress: 100, finishing: true, ("ogkush", 18));

        var result = Chapter3DemandCounter.Process(null, new[] { after }, "ogkush", Array.Empty<string>());

        Assert.Equal(0, result.FreshGain);
        Assert.Empty(result.EligibilityAdds);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Other_product_ids_grow_eligibility_but_never_fresh()
    {
        var before = Station("s1", running: true, identity: "op-1", progress: 100, finishing: true, ("sourdiesel", 10));
        var after = Station("s1", running: false, identity: "op-1", progress: 100, finishing: false, ("sourdiesel", 16));

        var result = Chapter3DemandCounter.Process(new[] { before }, new[] { after }, "ogkush", new[] { "ogkush" });

        Assert.Equal(0, result.FreshGain);
        Assert.Equal(new[] { "sourdiesel" }, result.EligibilityAdds);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Counter_ignores_a_null_read()
    {
        var result = Chapter3DemandCounter.Process(null, null, "ogkush", null);

        Assert.Equal(0, result.FreshGain);
        Assert.Empty(result.EligibilityAdds);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Handover_takes_whole_packages_in_slot_order_up_to_the_cap()
    {
        var slots = new[]
        {
            new Chapter3DemandLockerSlot(0, "ogkush", 10, IsPackaged: true, "jar", UnitsPerPackage: 5),
            new Chapter3DemandLockerSlot(1, "ogkush", 20, IsPackaged: true, "jar", UnitsPerPackage: 5),
        };

        var takes = Chapter3DemandHandover.PlanTakes(slots, "ogkush", maxUnits: 60);

        Assert.Equal(
            new[]
            {
                new Chapter3DemandSlotTake(0, 10, 50),
                new Chapter3DemandSlotTake(1, 2, 10),
            },
            takes);
    }

    [Fact]
    public void Handover_skips_other_ids_unknown_packaging_and_partial_packages()
    {
        var slots = new[]
        {
            new Chapter3DemandLockerSlot(0, "sourdiesel", 10, IsPackaged: true, "jar", UnitsPerPackage: 5),
            new Chapter3DemandLockerSlot(1, "ogkush", 4, IsPackaged: true, "mystery", UnitsPerPackage: 0),
            new Chapter3DemandLockerSlot(2, "ogkush", 5, IsPackaged: false, "", UnitsPerPackage: 1),
            new Chapter3DemandLockerSlot(3, "ogkush", 3, IsPackaged: true, "brick", UnitsPerPackage: 20),
        };

        var takes = Chapter3DemandHandover.PlanTakes(slots, "ogkush", maxUnits: 10);

        Assert.Equal(new[] { new Chapter3DemandSlotTake(2, 5, 5) }, takes);
    }

    [Fact]
    public void Handover_plans_nothing_without_a_demand_or_a_cap()
    {
        var slots = new[] { new Chapter3DemandLockerSlot(0, "ogkush", 10, IsPackaged: false, "", UnitsPerPackage: 1) };

        Assert.Empty(Chapter3DemandHandover.PlanTakes(null, "ogkush", 10));
        Assert.Empty(Chapter3DemandHandover.PlanTakes(slots, null, 10));
        Assert.Empty(Chapter3DemandHandover.PlanTakes(slots, "  ", 10));
        Assert.Empty(Chapter3DemandHandover.PlanTakes(slots, "ogkush", 0));
    }

    [Fact]
    public void Demand_validation_rejects_bad_counts()
    {
        var demand = OpenDemand();

        Assert.Throws<ArgumentException>(() => (demand with { DemandId = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (demand with { ProductId = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (demand with { DueDay = demand.IssueDay }).Validate());
        Assert.Throws<ArgumentException>(() => (demand with { FreshCount = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (demand with { Delivered = demand.Quantity + 1 }).Validate());
        Assert.Throws<ArgumentException>(() => (demand with { ProductType = (Chapter3DemandProductType)99 }).Validate());
    }

    [Fact]
    public void Pending_removal_validation_rejects_bad_takes()
    {
        Assert.Throws<ArgumentException>(() => new Chapter3DemandPendingRemoval(-1, 10, 2, 10).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3DemandPendingRemoval(0, 10, 0, 10).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3DemandPendingRemoval(0, 10, 11, 55).Validate());
        new Chapter3DemandPendingRemoval(0, 10, 2, 10).Validate();
    }

    [Fact]
    public void Outcome_validation_rejects_bad_amounts()
    {
        Assert.Throws<ArgumentException>(() => new Chapter3DemandOutcome(" ", "OG Kush", 0, 0, true).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3DemandOutcome("d1", "OG Kush", -1, 0, false).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3DemandOutcome("d1", "OG Kush", 0, -5, false).Validate());
    }

    [Fact]
    public void State_defaults_carry_no_demand()
    {
        var state = Chapter3State.CreateInitial(PlayerId);

        Assert.Null(state.ActiveDemand);
        Assert.Empty(state.MadeProductIds);
        Assert.Equal(-1, state.LastDemandWeek);
        Assert.Equal(1, state.NextDemandId);
        Assert.Null(state.LastDemandOutcome);
    }

    [Fact]
    public void State_rejects_bad_demand_fields()
    {
        var state = ActiveState();

        Assert.Throws<ArgumentException>(() => (state with { MadeProductIds = new[] { "ogkush", "ogkush" } }).Validate());
        Assert.Throws<ArgumentException>(() => (state with { MadeProductIds = new[] { "  " } }).Validate());
        Assert.Throws<ArgumentException>(() => (state with { LastDemandWeek = -2 }).Validate());
        Assert.Throws<ArgumentException>(() => (state with { NextDemandId = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (state with { ActiveDemand = OpenDemand() with { Quantity = 0 } }).Validate());
    }

    [Fact]
    public void Quest_copy_pins_the_demand_lines()
    {
        Assert.Equal("Family demand: 250 OG Kush", Chapter3QuestCopy.DemandEntryTitle(250, "OG Kush"));
        Assert.Equal("Family demand: 250 product", Chapter3QuestCopy.DemandEntryTitle(250, "  "));
        Assert.Equal("Made in the warehouse: 30/250", Chapter3QuestCopy.DemandMadeLine(30, 250));
        Assert.Equal("Hand over at the HQ family locker (back of the row nearest Arthur's desk): 0/250", Chapter3QuestCopy.DemandHandedLine(0, 250));
    }

    [Fact]
    public void Nell_demand_copy_pins_the_texts_and_the_locker_phrase()
    {
        Assert.Equal(
            "The family wants 250 OG Kush out of the warehouse by day 9. Drop it in the family locker at the HQ, the back locker in the row nearest Arthur's desk. Half price, and don't fucking argue.",
            Chapter3NellCopy.DemandIssuedText(250, "OG Kush", 9));
        Assert.Equal("the back locker in the row nearest Arthur's desk", Chapter3NellCopy.FamilyLockerWhere);
        Assert.Equal("Got 40. 210 to go.", Chapter3NellCopy.DemandReceiptText(40, 210));
        Assert.Equal("Got 250. That's the lot.", Chapter3NellCopy.DemandReceiptText(250, 0));
        Assert.Equal("That's the order. Family's happy, for now.", Chapter3NellCopy.DemandCompleteText());
        Assert.Equal(
            "You came up short on the order. 60 OG Kush at full price, that's $3,000. Two days.",
            Chapter3NellCopy.DemandMissedText(60, "OG Kush", 3_000));
        Assert.Equal(
            "Family order's due tomorrow. 60 OG Kush still owed.",
            Chapter3NellCopy.DemandDueTomorrowText(60, "OG Kush"));
    }

    [Fact]
    public void Make_good_reason_retargets_the_demand_line()
    {
        Assert.Equal("The family order went short", Chapter3NellCopy.MakeGoodReason(new Chapter3MissedDemandSource("d1")));
    }

    [Fact]
    public void Issue_message_queues_once_then_hides_behind_its_receipt()
    {
        var state = ActiveState() with { ActiveDemand = OpenDemand() };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: true);

        var message = Assert.Single(
            plan.Messages,
            m => m.CorrelationId.EndsWith("demand-d2-issued", StringComparison.Ordinal));
        Assert.Equal(
            "The family wants 250 OG Kush out of the warehouse by day 9. Drop it in the family locker at the HQ, the back locker in the row nearest Arthur's desk. Half price, and don't fucking argue.",
            message.Text);
        var receipted = state with
        {
            PresentationReceipts = state.PresentationReceipts.Append(
                new Chapter3PresentationReceipt(message.CorrelationId, state.Revision)).ToArray()
        };

        Assert.DoesNotContain(
            Chapter3PresentationPlanner.Build(receipted, release1HasNellDecision: true, warehouseOwned: true).Messages,
            m => m.CorrelationId == message.CorrelationId);
    }

    [Fact]
    public void Latest_receipt_supersedes_earlier_takes()
    {
        var state = ActiveState() with { ActiveDemand = OpenDemand() with { HandOverCount = 2, LastTakeUnits = 90, Delivered = 90 } };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: true);

        Assert.Contains(plan.Messages, m => m.Text == "Got 90. 160 to go.");
        Assert.DoesNotContain(plan.Messages, m => m.CorrelationId.Contains("receipt-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Closed_demands_send_their_closing_text()
    {
        var completed = ActiveState() with
        {
            LastDemandOutcome = new Chapter3DemandOutcome("d1", "OG Kush", 0, 6_250, Completed: true)
        };
        var missed = ActiveState() with
        {
            LastDemandOutcome = new Chapter3DemandOutcome("d2", "OG Kush", 60, 3_000, Completed: false)
        };

        var donePlan = Chapter3PresentationPlanner.Build(completed, release1HasNellDecision: true, warehouseOwned: true);
        var missedPlan = Chapter3PresentationPlanner.Build(missed, release1HasNellDecision: true, warehouseOwned: true);

        Assert.Contains(donePlan.Messages, m => m.Text == "That's the order. Family's happy, for now.");
        Assert.Contains(
            missedPlan.Messages,
            m => m.Text == "You came up short on the order. 60 OG Kush at full price, that's $3,000. Two days.");
    }

    [Fact]
    public void Demand_only_quest_carries_three_entries_on_the_hq_door()
    {
        var state = ActiveState() with { ActiveDemand = OpenDemand() };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: true);

        Assert.NotNull(plan.Quest);
        Assert.Equal(Chapter3QuestCopy.QuestKey, plan.Quest!.Key);
        var door = new Release1WorldPoint(10f, 20f, 30f);
        Assert.Equal(
            new[]
            {
                new Release1DesiredEntry("Family demand: 250 OG Kush", Release1DesiredEntryState.Active, door),
                new Release1DesiredEntry("Made in the warehouse: 30/250", Release1DesiredEntryState.Active, door),
                new Release1DesiredEntry("Hand over at the HQ family locker (back of the row nearest Arthur's desk): 0/250", Release1DesiredEntryState.Active, door),
            },
            plan.Quest.Entries);
    }

    [Fact]
    public void Demand_entries_follow_progress()
    {
        var state = ActiveState() with
        {
            ActiveDemand = OpenDemand() with { FreshCount = 250, Delivered = 250 }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: true);

        Assert.Equal(
            new[] { "Family demand: 250 OG Kush", "Made in the warehouse: 250/250", "Hand over at the HQ family locker (back of the row nearest Arthur's desk): 250/250" },
            plan.Quest!.Entries.Select(e => e.Text).ToArray());
    }

    [Fact]
    public void Demand_without_a_door_desires_no_entries()
    {
        var state = ActiveState() with
        {
            ActiveDemand = OpenDemand() with { HqDoorX = null, HqDoorY = null, HqDoorZ = null }
        };

        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: true, warehouseOwned: true);

        Assert.Null(plan.Quest);
    }

    [Fact]
    public void Overnight_names_a_demand_due_tomorrow()
    {
        var state = ActiveState() with { ActiveDemand = OpenDemand() with { DueDay = 6, Delivered = 100 } };

        Assert.Equal(
            "Family order's due tomorrow. 150 OG Kush still owed.",
            Chapter3OvernightLine.Select(state, chapterDay: 5));
        Assert.Null(Chapter3OvernightLine.Select(state, chapterDay: 3));
    }

    [Fact]
    public void Demand_round_trips_through_schema14()
    {
        var state = ActiveState() with
        {
            ActiveDemand = OpenDemand() with { PendingRemoval = new Chapter3DemandPendingRemoval(2, 10, 4, 20) },
            MadeProductIds = new[] { "ogkush", "sourdiesel" },
            LastDemandWeek = 3,
            NextDemandId = 5,
            LastDemandOutcome = new Chapter3DemandOutcome("d1", "OG Kush", 0, 6_250, Completed: true),
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.Contains("\"schemaVersion\": 20", json);
        Assert.Contains("\"activeDemand\"", json);
        Assert.Contains("\"madeProductIds\"", json);
        Assert.Contains("\"lastDemandWeek\"", json);
        Assert.Contains("\"nextDemandId\"", json);
        Assert.Contains("\"lastDemandOutcome\"", json);
        Assert.Contains("\"productType\": \"Weed\"", json);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(20, envelope!.SchemaVersion);
        Assert.Equal(state, envelope.State);
    }

    [Fact]
    public void Schema12_file_loads_with_no_demand()
    {
        var state = ActiveState();
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var node = JsonNode.Parse(json)!;
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 12;
        var stateNode = node["state"]!.AsObject();
        stateNode.Remove("sweep");
        stateNode.Remove("lastSweepStartMinutes");
        stateNode.Remove("activeDemand");
        stateNode.Remove("madeProductIds");
        stateNode.Remove("lastDemandWeek");
        stateNode.Remove("nextDemandId");
        stateNode.Remove("lastDemandOutcome");
        stateNode.Remove("warehouseCapacityTier");
        stateNode.Remove("demandsCompleted");
        stateNode.Remove("checkpointTargetCode");
        stateNode.Remove("checkpointTargetDay");
        stateNode["jobBoard"]!["openOffer"]?.AsObject().Remove("storageRun");
        stateNode["jobBoard"]!["activeJob"]?.AsObject().Remove("storageRun");
        stateNode["jobBoard"]!["openOffer"]?.AsObject().Remove("cashMovement");
        stateNode["jobBoard"]!["activeJob"]?.AsObject().Remove("cashMovement");
        stateNode["jobBoard"]!["lastCompletedJob"]?.AsObject().Remove("cashMovement");
        stateNode["jobBoard"]!["openOffer"]?.AsObject().Remove("shutdown");
        stateNode["jobBoard"]!["activeJob"]?.AsObject().Remove("shutdown");
        stateNode["jobBoard"]!["lastCompletedJob"]?.AsObject().Remove("shutdown");
        var backdated = node.ToJsonString();

        Assert.True(Chapter3SaveCodec.TryDeserialize(backdated, out var envelope, out var result), result.Message);

        Assert.Equal(20, envelope!.SchemaVersion);
        Assert.Null(envelope.State!.ActiveDemand);
        Assert.Empty(envelope.State.MadeProductIds);
        Assert.Equal(-1, envelope.State.LastDemandWeek);
        Assert.Equal(1, envelope.State.NextDemandId);
        Assert.Null(envelope.State.LastDemandOutcome);
    }

    [Fact]
    public void Demand_with_an_unknown_field_is_rejected()
    {
        var state = ActiveState() with { ActiveDemand = OpenDemand() };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"pendingRemoval\": null", "\"pendingRemoval\": null, \"smuggled\": 1");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Pending_removal_with_an_unknown_field_is_rejected()
    {
        var state = ActiveState() with
        {
            ActiveDemand = OpenDemand() with { PendingRemoval = new Chapter3DemandPendingRemoval(2, 10, 4, 20) }
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"units\": 20", "\"units\": 20, \"smuggled\": 1");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Schema1_file_loads_with_no_demand()
    {
        const string v1 = """
            {
              "schemaVersion": 1,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 13,
                "revision": 3
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v1, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Null(envelope.State!.ActiveDemand);
        Assert.Empty(envelope.State.MadeProductIds);
        Assert.Equal(-1, envelope.State.LastDemandWeek);
        Assert.Equal(1, envelope.State.NextDemandId);
        Assert.Null(envelope.State.LastDemandOutcome);
    }

    private static Chapter3State ActiveState() =>
        Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;

    private static Chapter3ProductionDemand OpenDemand() => new(
        DemandId: "d2",
        ProductId: "ogkush",
        ProductName: "OG Kush",
        ProductType: Chapter3DemandProductType.Weed,
        Quantity: 250,
        UnitValue: 50,
        IssueDay: 6,
        DueDay: 9,
        FreshCount: 30,
        Delivered: 0,
        PaidTotal: 0,
        LastTakeUnits: 0,
        HandOverCount: 0,
        HqDoorX: 10,
        HqDoorY: 20,
        HqDoorZ: 30,
        PendingRemoval: null);

    private static Chapter3DemandStationSnapshot Station(
        string guid, bool running, string identity, int progress, bool finishing, params (string Id, int Qty)[] outputs) =>
        new(
            guid,
            Release1ProductionStationKind.MixingStation,
            running,
            identity,
            progress,
            finishing,
            outputs.Select((o, i) => new Chapter3DemandOutputSlot(i, o.Id, o.Qty)).ToArray());
}
