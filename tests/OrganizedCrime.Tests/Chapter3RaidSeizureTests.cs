using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidSeizureTests
{
    [Fact]
    public void Away_raid_freezes_plan_and_takes_one_slot_per_poll_with_readback()
    {
        var h = new Harness(); h.Away();
        Assert.NotNull(h.State.Raid);
        Assert.Equal(10, h.State.Raid!.Budget);
        h.Poll(); Assert.Equal(5, h.State.Raid.ActualUnits); Assert.Single(h.World.Writes);
        h.Poll(); Assert.Equal(10, h.State.Raid.ActualUnits); Assert.Equal(2, h.World.Writes.Count);
        Assert.True(h.State.Raid.Completed); Assert.True(h.State.Raid.CoolingApplied);
        h.Poll(); Assert.Equal(2, h.World.Writes.Count); Assert.Single(h.Coolings);
        Assert.Equal(0, h.World.Locks);
    }

    [Theory]
    [InlineData("client")] [InlineData("tier")] [InlineData("time")] [InlineData("sweep")]
    [InlineData("protection")] [InlineData("unknown-cover")] [InlineData("arrest")]
    [InlineData("unknown-arrest")] [InlineData("save")] [InlineData("load")]
    [InlineData("cap")] [InlineData("restoration")]
    public void Eligibility_gates_never_mutate(string gate)
    {
        var h = new Harness();
        switch (gate)
        {
            case "client": h.Input = h.Input with { IsHost = false }; break;
            case "tier": h.Input = h.Input with { Tier = LocalPressureTier.Watched }; break;
            case "time": h.Input = h.Input with { Minutes = 1000 }; break;
            case "sweep": h.Context.State = h.State with { Sweep = new("s", "a", 1080, false, ActiveStartMinutes: 1080, ActiveEndMinutes: 2520), LastSweepStartMinutes = 1080 }; break;
            case "protection": h.Input = h.Input with { Protected = true }; break;
            case "unknown-cover": h.Input = h.Input with { Protected = null }; break;
            case "arrest": h.Input = h.Input with { ArrestTransition = true }; break;
            case "unknown-arrest": h.Input = h.Input with { ArrestTransition = null }; break;
            case "save": h.Context.Phase = Chapter3RuntimePhase.Saving; break;
            case "load": h.Context.Phase = Chapter3RuntimePhase.AwaitingLoad; break;
            case "cap": h.Context.State = h.State with { LastRaidSuccessMinutes = 1079 }; break;
            case "restoration": h.World.Restored = false; break;
        }
        h.Away(); h.Poll();
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes); Assert.Empty(h.Coolings);
    }

    [Fact]
    public void Occupied_richest_site_is_abandoned_for_the_evening_without_falling_back()
    {
        var h = new Harness(); h.World.Presence = Chapter3RaidPresence.Inside;
        h.Away(); Assert.Null(h.State.Raid);
        h.Input = h.Input with { Minutes = 1380 }; h.Poll();
        h.World.Presence = Chapter3RaidPresence.Clear; h.Poll();
        Assert.Null(h.State.Raid); Assert.Null(h.State.LastRaidSuccessMinutes);
        h.Input = h.Input with { Minutes = 2520 }; h.Away();
        Assert.NotNull(h.State.Raid);
    }

    [Fact]
    public void No_whole_package_means_no_raid_or_cooldown()
    {
        var h = new Harness(); h.World.Slots = new[] { Harness.Slot(0, 1, 20) };
        h.Away(); h.Poll(); Assert.Null(h.State.Raid); Assert.Null(h.State.LastRaidSuccessMinutes);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    public void Reload_before_or_after_pending_take_reconciles_without_duplicate_loss(bool landed, int index)
    {
        var h = new Harness(); h.Away();
        if (index == 1) h.Poll();
        var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select((s, i) => i == index ? s with { State = Chapter3RaidRemovalState.Pending } : s).ToArray() } };
        if (landed) h.World.SetQuantity(index, 0);
        h.Reload(); h.Away(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits);
        Assert.Equal(landed ? 1 : 2, h.World.Writes.Count);
        h.Reload(); h.Away(); h.Poll();
        Assert.Equal(landed ? 1 : 2, h.World.Writes.Count); Assert.Single(h.Coolings);
    }

    [Theory]
    [InlineData("quantity")] [InlineData("fingerprint")] [InlineData("partial-write")]
    public void Divergence_is_a_durable_ambiguous_hold_even_if_original_stock_returns(string divergence)
    {
        var h = new Harness(); h.Away();
        if (divergence == "quantity") h.World.SetQuantity(0, 4);
        if (divergence == "fingerprint") h.World.Slots = h.World.Slots.Select((s, i) => i == 0 ? s with { Fingerprint = "replacement" } : s).ToArray();
        if (divergence == "partial-write") h.World.Partial = true;
        h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, h.State.Raid!.Slots[0].State);
        var writes = h.World.Writes.Count;
        h.World.Partial = false; h.World.Slots = Harness.Stock(); h.Reload(); h.Away(); h.Poll();
        Assert.Equal(writes, h.World.Writes.Count); Assert.Equal(0, h.State.Raid.ActualUnits);
        Assert.Null(h.State.LastRaidSuccessMinutes); Assert.Empty(h.Coolings);
    }

    [Fact]
    public void Frozen_plan_never_expands_after_new_stock_arrives_or_confirmed_stock_is_restored()
    {
        var h = new Harness(); h.Away(); h.Poll();
        h.World.SetQuantity(0, 5);
        h.World.Slots = h.World.Slots.Append(Harness.Slot(8, 100, 1)).ToArray();
        h.Reload(); h.Away(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits); Assert.Equal(2, h.World.Writes.Count);
        Assert.Equal(100, h.World.Slots.Single(s => s.Identity.SlotIndex == 8).Quantity);
        Assert.Equal(5, h.World.Slots.Single(s => s.Identity.SlotIndex == 0).Quantity);
    }

    [Theory]
    [InlineData("cover")] [InlineData("occupancy")] [InlineData("arrest")] [InlineData("sweep")] [InlineData("restoration")]
    public void Every_further_take_rechecks_safety(string change)
    {
        var h = new Harness(); h.Away(); h.Poll(); Assert.Single(h.World.Writes);
        if (change == "cover") h.Input = h.Input with { Protected = true };
        if (change == "occupancy") h.World.Presence = Chapter3RaidPresence.Inside;
        if (change == "arrest") h.Input = h.Input with { ArrestTransition = true };
        if (change == "sweep") h.Context.State = h.State with { Sweep = new("s", "a", 1080, false, ActiveStartMinutes: 1080, ActiveEndMinutes: 2520), LastSweepStartMinutes = 1080 };
        if (change == "restoration") h.World.Restored = false;
        h.Poll(); Assert.Single(h.World.Writes);
    }

    [Fact]
    public void Missed_window_reconciles_pending_but_never_catches_up_further_takes()
    {
        var h = new Harness(); h.Away(); h.World.UnreadableAfterWrite = true; h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Pending, h.State.Raid!.Slots[0].State);
        h.Input = h.Input with { Minutes = 2520 }; h.World.UnreadableAfterWrite = false; h.Reload(); h.Poll();
        Assert.Equal(5, h.State.Raid.ActualUnits); Assert.True(h.State.Raid.Completed);
        h.Away(); h.Poll(); Assert.Single(h.World.Writes);
    }

    [Fact]
    public void Busy_slot_and_unreadable_post_hold_intent_and_release_locks()
    {
        var h = new Harness(); h.Away(); h.World.Busy = true; h.Poll();
        Assert.Empty(h.World.Writes); Assert.Equal(Chapter3RaidRemovalState.Planned, h.State.Raid!.Slots[0].State);
        h.World.Busy = false; h.World.UnreadableAfterWrite = true; h.Poll();
        Assert.Single(h.World.Writes); Assert.Equal(0, h.World.Locks);
        h.Reload(); h.World.UnreadableAfterWrite = false; h.Poll();
        Assert.Equal(5, h.State.Raid.ActualUnits); Assert.Single(h.World.Writes);
    }

    internal sealed class Harness
    {
        internal static readonly Guid Storage = Guid.Parse("00000000-0000-0000-0000-000000000001");
        internal static Chapter3RaidSlot Slot(int i, int quantity, int units) => new(new(Storage, i), "p|bag|" + units, "p", "bag", quantity, units);
        internal static Chapter3RaidSlot[] Stock() => new[] { Slot(0, 5, 1), Slot(1, 5, 1), Slot(2, 30, 1) };
        internal Chapter3FeatureContext Context = new() { State = new("player", Chapter3Phase.Active, 0, 0, 0), Phase = Chapter3RuntimePhase.Active };
        internal Chapter3RaidInput Input = new(true, 1080, LocalPressureTier.Critical, false, false);
        internal World World = new();
        internal List<string> Coolings = new();
        internal Chapter3RaidFeature Feature;
        internal Chapter3State State => Context.State!;
        internal Harness() => Feature = Create();
        private Chapter3RaidFeature Create() => new(Context, World, World, () => Input, id => { if (!Coolings.Contains(id)) Coolings.Add(id); return true; });
        internal void Poll() => Feature.PollLocked();
        internal void Away() { for (var i = 0; i <= 10; i++) { Poll(); Input = Input with { Minutes = Input.Minutes + 1 }; } }
        internal void Reload()
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(State, out var json, out var write), write.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);
            Context.State = envelope!.State; Feature = Create();
        }
    }

    internal sealed class World : IChapter3RaidWorld, IChapter3RaidSeizureWorld
    {
        internal string? StableFingerprint;
        internal Action? OnLock;
        public bool IsHost => true;
        internal bool Restored = true, Busy, Partial, UnreadableAfterWrite;
        internal Chapter3RaidPresence Presence = Chapter3RaidPresence.Clear;
        internal Chapter3RaidSlot[] Slots = Harness.Stock();
        internal List<Chapter3RaidSlotIdentity> Writes = new();
        internal int Locks;
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions) => new[] { new Chapter3RaidSite("a", "Site A", true, Slots, Presence) };
        public bool IsRestored(string site) => Restored;
        public bool TryReadSlot(string site, Chapter3RaidSlotIdentity id, out Chapter3RaidSlotRead read)
        {
            var slot = Slots.SingleOrDefault(s => s.Identity == id);
            read = new(slot?.Quantity ?? 0, slot?.Quantity == 0 ? null : StableFingerprint ?? slot?.Fingerprint);
            return !(UnreadableAfterWrite && Writes.Count > 0);
        }
        public bool TryLock(string site, Chapter3RaidSlotIdentity id) { if (Busy) return false; Locks++; OnLock?.Invoke(); return true; }
        public void Unlock(string site, Chapter3RaidSlotIdentity id) => Locks--;
        public void Take(string site, Chapter3RaidSlotIdentity id, int packages)
        {
            Assert.Equal(1, Locks); Writes.Add(id);
            SetQuantity(id.SlotIndex, Slots.Single(s => s.Identity == id).Quantity - (Partial ? 1 : packages));
        }
        internal void SetQuantity(int i, int quantity) => Slots = Slots.Select(s => s.Identity.SlotIndex == i ? s with { Quantity = quantity } : s).ToArray();
    }
}
