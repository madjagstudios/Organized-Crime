using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidAdversarialTests
{
    [Fact]
    public void Mixed_packages_reconcile_nonempty_post_quantity_without_exceeding_frozen_units()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.World.Slots = new[]
        {
            Chapter3RaidSeizureTests.Harness.Slot(0, 10, 3),
            Chapter3RaidSeizureTests.Harness.Slot(1, 1, 20),
            Chapter3RaidSeizureTests.Harness.Slot(2, 30, 1)
        };
        h.Away(); h.World.UnreadableAfterWrite = true; h.Poll();
        Assert.Equal(4, h.World.Slots[0].Quantity);
        h.Reload(); h.World.UnreadableAfterWrite = false; h.Away(); h.Poll();
        Assert.Equal(20, h.State.Raid!.Budget); Assert.Equal(20, h.State.Raid.ActualUnits);
        Assert.Equal(new[] { 4, 1, 28 }, h.World.Slots.Select(s => s.Quantity));
        Assert.Equal(new[] { 0, 2 }, h.World.Writes.Select(s => s.SlotIndex));
    }

    [Fact]
    public void Raid_freezes_quantity_independent_mutation_fingerprint_without_changing_D7a_budget()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.World.StableFingerprint = "p|bag|quality=Standard";
        h.Away(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.Budget); Assert.Equal(10, h.State.Raid.ActualUnits);
        Assert.All(h.State.Raid.Slots, s => Assert.Equal(h.World.StableFingerprint, s.Take.Fingerprint));
        Assert.All(h.World.Slots, s => Assert.Equal("p|bag|1", s.Fingerprint));
    }

    [Theory]
    [InlineData("cover")] [InlineData("occupancy")] [InlineData("host")]
    public void Safety_is_rechecked_after_lock_and_immediately_before_write(string change)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        h.World.OnLock = () =>
        {
            if (change == "cover") h.Input = h.Input with { Protected = true };
            if (change == "host") h.Input = h.Input with { IsHost = false };
            if (change == "occupancy") h.World.Presence = Chapter3RaidPresence.Inside;
        };
        h.Poll(); Assert.Empty(h.World.Writes); Assert.Equal(0, h.World.Locks);
    }

    [Fact]
    public void Manual_wait_is_abandoned_at_that_evenings_end_without_catchup()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.World.Presence = Chapter3RaidPresence.Inside;
        h.Feature.Queue("a", false); h.Poll();
        h.Input = h.Input with { Minutes = 1380 }; h.Poll();
        h.Input = h.Input with { Minutes = 2000 }; h.World.Presence = Chapter3RaidPresence.Clear;
        h.Away(); Assert.Null(h.State.Raid);
    }

    [Fact]
    public void Normal_raid_can_begin_at_1800_after_polling_during_the_day()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Input = h.Input with { Minutes = 1000 }; h.Poll();
        h.Input = h.Input with { Minutes = 1080 }; h.Away(); Assert.NotNull(h.State.Raid);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Reload_after_each_confirmed_slot_never_repeats_that_take(int completed)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        for (var i = 0; i < completed; i++) h.Poll();
        h.Reload(); h.Away(); h.Poll(); h.Poll();
        Assert.Equal(2, h.World.Writes.Count); Assert.Equal(10, h.State.Raid!.ActualUnits);
        Assert.Single(h.World.Writes, s => s.SlotIndex == 0);
        Assert.Single(h.World.Writes, s => s.SlotIndex == 1);
    }

    [Fact]
    public void Both_older_codecs_migrate_without_raid_state_and_current_writers_include_new_fields()
    {
        var state = Chapter3State.CreateInitial("player");
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var node = JsonNode.Parse(json)!;
        Assert.True(node["state"]!.AsObject().ContainsKey("raid"));
        Assert.True(node["state"]!.AsObject().ContainsKey("lastRaidSuccessMinutes"));
        node["schemaVersion"] = 18; node["state"]!.AsObject().Remove("raid"); node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var envelope, out var read), read.Message);
        Assert.Null(envelope!.State!.Raid); Assert.Null(envelope.State.LastRaidSuccessMinutes);
        var pressure = new LocalPressureState(90, true, 1, 2, 3, "player", null, null, 4);
        Assert.True(LocalPressureSaveCodec.TrySerialize(new[] { pressure }, out json, out _));
        node = JsonNode.Parse(json)!;
        Assert.True(node["players"]![0]!.AsObject().ContainsKey("lastAppliedRaidCoolingId"));
        node["schemaVersion"] = 1; node["players"]![0]!.AsObject().Remove("lastAppliedRaidCoolingId");
        Assert.True(LocalPressureSaveCodec.TryDeserializeStates(node.ToJsonString(), out var states, out var loaded), loaded.Message);
        Assert.Equal(pressure, Assert.Single(states!)); Assert.Null(states![0].LastAppliedRaidCoolingId);
    }

    [Theory]
    [InlineData("planned")] [InlineData("pending")] [InlineData("confirmed")] [InlineData("ambiguous")]
    public void Current_schema_round_trips_each_slot_receipt(string stage)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        if (stage == "pending") { h.World.UnreadableAfterWrite = true; h.Poll(); }
        if (stage == "confirmed") { h.Poll(); h.Poll(); }
        if (stage == "ambiguous") { h.World.SetQuantity(0, 4); h.Poll(); }
        var before = h.State; h.Reload(); Assert.Equal(before, h.State);
    }

    [Theory]
    [InlineData("budget", "11")] [InlineData("slots", "null")] [InlineData("coolingApplied", "true")]
    [InlineData("noticeReceipt", "\"foreign\"")] [InlineData("startMinutes", "-1")]
    [InlineData("unknown", "true")]
    public void Invalid_raid_journals_fail_closed(string field, string value)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        Assert.True(Chapter3SaveCodec.TrySerialize(h.State, out var json, out _));
        var node = JsonNode.Parse(json)!; node["state"]!["raid"]![field] = JsonNode.Parse(value);
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out _));
    }

    [Theory]
    [InlineData("raid")] [InlineData("slot")] [InlineData("take")] [InlineData("identity")]
    public void Invalid_nested_raid_types_return_a_failure_result(string level)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        Assert.True(Chapter3SaveCodec.TrySerialize(h.State, out var json, out _));
        var node = JsonNode.Parse(json)!;
        var raid = node["state"]!["raid"]!;
        if (level == "raid") node["state"]!["raid"] = 1;
        if (level == "slot") raid["slots"]![0] = 1;
        if (level == "take") raid["slots"]![0]!["take"] = 1;
        if (level == "identity") raid["slots"]![0]!["take"]!["identity"] = 1;
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out var result));
        Assert.Equal(Chapter3CodecFailureReason.InvalidJsonType, result.Reason);
    }

    [Fact]
    public void Pressure_cooling_marker_survives_evidence_decay_context_and_wipe_copies()
    {
        var profile = LocalPressureProfile.Moderate;
        var state = LocalPressureTransitions.ApplyRaidCooling(new(90, true, 1, 2, 3, "player", null, null, 7), profile, "raid-id");
        var afterEvidence = LocalPressureTransitions.ApplyEvidence(state, new(LocalPressureReasonCode.Arrest, 4, PlayerId: "player"), profile).State;
        var afterDecay = LocalPressureDecay.Evaluate(new(afterEvidence, 10, false), profile).State;
        var afterWipe = LocalPressureTransitions.ApplyRecordWipe(afterDecay, profile).State;
        foreach (var copy in new[] { state, afterEvidence, afterDecay, afterWipe })
        {
            Assert.True(LocalPressureSaveCodec.TrySerialize(new[] { copy }, out var json, out var write), write.Message);
            Assert.True(LocalPressureSaveCodec.TryDeserializeStates(json, out var states, out var read), read.Message);
            var restored = Assert.Single(states!);
            Assert.Equal("raid-id", restored.LastAppliedRaidCoolingId);
            Assert.Same(restored, LocalPressureTransitions.ApplyRaidCooling(restored, profile, "raid-id"));
        }
    }
}
