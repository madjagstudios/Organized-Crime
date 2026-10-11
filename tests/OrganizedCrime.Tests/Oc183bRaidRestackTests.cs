using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc183bRaidRestackTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Taken_bricks_restacked_into_pre_quantity_slot_do_not_warn_or_retake(bool reload, bool nativeTotals)
    {
        var h = new Harness(nativeTotals); h.Freeze(); h.World.RestackAfterTake = true; h.Poll();
        Assert.Equal(new[] { 20, 3 }, h.World.Quantities);
        if (reload) h.Reload();
        h.Poll(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.Raid.Slots[0].State);
        Assert.DoesNotContain(h.Warnings, s => s.Contains("confirmed raid stock"));
        Assert.Single(h.World.Takes); Assert.Equal(140, h.Raid.ActualUnits);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Genuinely_unpersisted_confirmed_take_warns_once_and_never_retakes(bool restack)
    {
        var h = new Harness(); h.Freeze(); h.Poll();
        h.World.Quantities = restack ? new[] { 10, 20 } : new[] { 20, 10 };
        h.Reload(); h.Poll(); h.Poll();
        Assert.Single(h.Warnings, s => s.Contains("confirmed raid stock"));
        Assert.Single(h.World.Takes); Assert.Equal(30, h.World.Quantities.Sum());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Pending_reload_after_take_and_restack_confirms_without_second_take(bool moved, bool nativeTotals)
    {
        var h = new Harness(nativeTotals); h.Freeze(); h.World.HideAfterTake = true; h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Pending, h.Raid.Slots[0].State);
        h.World.Quantities = moved ? new[] { 0, 23 } : new[] { 20, 3 };
        h.World.HideAfterTake = false; h.Reload(); h.Poll(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.Raid.Slots[0].State);
        Assert.Single(h.World.Takes); Assert.Equal(23, h.World.Quantities.Sum());
    }

    [Theory]
    [InlineData(20, 4)] [InlineData(20, 9)] [InlineData(10, 20)]
    public void Ambiguous_pending_total_or_restacked_pre_holds_without_retake(int first, int second)
    {
        var h = new Harness(); h.Freeze(); h.World.HideAfterTake = true; h.Poll();
        h.World.Quantities = new[] { first, second };
        h.World.HideAfterTake = false; h.Reload(); h.Poll(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, h.Raid.Slots[0].State);
        Assert.Single(h.World.Takes);
        h.World.Quantities = new[] { 20, 10 }; h.Reload(); h.Poll();
        Assert.Single(h.World.Takes);
    }

    [Fact]
    public void Unchanged_pending_container_and_slot_can_retry_a_take_that_never_landed()
    {
        var h = new Harness(); h.Freeze(); h.World.NoOp = true; h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Pending, h.Raid.Slots[0].State);
        h.World.NoOp = false; h.Reload();
        for (var i = 0; i <= 10; i++) { h.Poll(); h.Minutes++; }
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.Raid.Slots[0].State);
        Assert.Equal(2, h.World.Takes.Count); Assert.Equal(23, h.World.Quantities.Sum());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Legacy_pending_receipt_without_total_holds_even_if_slot_matches_pre_or_post(bool landed)
    {
        var h = new Harness(); h.Freeze();
        h.Context.State = h.Context.State! with { Raid = h.Raid with { Slots = h.Raid.Slots.Select(s =>
            s with { State = Chapter3RaidRemovalState.Pending, PreFingerprintTotal = null }).ToArray() } };
        h.World.Quantities = landed ? new[] { 20, 3 } : new[] { 20, 10 };
        h.Reload(); h.Poll(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, h.Raid.Slots[0].State);
        Assert.Empty(h.World.Takes); Assert.Equal(0, h.Raid.ActualUnits);
    }

    [Fact]
    public void Legacy_confirmed_receipt_without_total_cannot_warn_from_a_refilled_slot()
    {
        var h = new Harness(); h.Freeze(); h.Poll();
        h.Context.State = h.Context.State! with { Raid = h.Raid with { Slots = h.Raid.Slots.Select(s =>
            s with { PreFingerprintTotal = null }).ToArray() } };
        h.World.Quantities = new[] { 20, 3 }; h.Reload(); h.Poll();
        Assert.DoesNotContain(h.Warnings, s => s.Contains("confirmed raid stock"));
        Assert.Single(h.World.Takes);
    }

    [Fact]
    public void Frozen_fingerprint_total_round_trips_without_changing_schema_and_legacy_omits_it()
    {
        var h = new Harness(); h.Freeze();
        Assert.Equal(30L, h.Raid.Slots[0].PreFingerprintTotal);
        var raid = h.Raid; h.Reload(); Assert.Equal(raid, h.Raid);
        Assert.Equal(20, Chapter3SaveCodec.CurrentSchemaVersion);
        h.Context.State = h.Context.State! with { Raid = h.Raid with { Slots = h.Raid.Slots.Select(s =>
            s with { PreFingerprintTotal = null }).ToArray() } };
        Assert.True(Chapter3SaveCodec.TrySerialize(h.Context.State, out var json, out _));
        Assert.DoesNotContain("preFingerprintTotal", json);
        h.Reload(); Assert.Null(h.Raid.Slots[0].PreFingerprintTotal);
    }

    [Theory]
    [InlineData("-1")] [InlineData("19")] [InlineData("30.5")] [InlineData("\"30\"")] [InlineData("{}")]
    public void Invalid_optional_total_is_rejected_by_codec(string value)
    {
        var h = new Harness(); h.Freeze();
        Assert.True(Chapter3SaveCodec.TrySerialize(h.Context.State, out var json, out _));
        json = json.Replace("\"preFingerprintTotal\": 30", "\"preFingerprintTotal\": " + value, StringComparison.Ordinal);
        Assert.False(Chapter3SaveCodec.TryDeserialize(json, out _, out _));
    }

    [Fact]
    public void Multiple_takes_of_same_fingerprint_reconcile_cumulative_loss_after_restack()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); Quiet(h); h.Away(); h.Poll();
        var raid = h.State.Raid!;
        Assert.All(raid.Slots, s => Assert.Equal(40L, s.PreFingerprintTotal));
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select((s, i) =>
            i == 1 ? s with { State = Chapter3RaidRemovalState.Pending } : s).ToArray() } };
        h.World.SetQuantity(1, 5); h.World.SetQuantity(2, 25);
        h.Reload(); Quiet(h); h.Poll(); h.Poll();
        Assert.All(h.State.Raid!.Slots, s => Assert.Equal(Chapter3RaidRemovalState.Confirmed, s.State));
        Assert.Equal(10, h.State.Raid.ActualUnits); Assert.Single(h.World.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Inconsistent_or_insufficient_frozen_group_total_is_rejected(bool insufficient)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); Quiet(h); h.Away();
        var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select((s, i) =>
            s with { PreFingerprintTotal = insufficient ? 5 : i == 0 ? 40 : 41 }).ToArray() } };
        Assert.False(Chapter3SaveCodec.TrySerialize(h.State, out _, out _));
    }

    [Fact]
    public void Whole_container_sum_uses_exact_product_packaging_quality_fingerprint()
    {
        const string fingerprint = "ogkush|brick|20|quality=Standard";
        var slots = new[] { new Chapter3RaidSlotRead(20, fingerprint), new(3, fingerprint),
            new(7, "ogkush|brick|20|quality=Premium"), new(7, "ogkush|bag|1|quality=Standard"),
            new(7, "sourdiesel|brick|20|quality=Standard"), new(0, null) };
        Assert.True(Chapter3RaidStorageQuantity.TrySum(slots.Length, i => slots[i], fingerprint, out var total));
        Assert.Equal(23, total);
    }

    [Theory]
    [InlineData("missing")] [InlineData("negative")] [InlineData("unknown")] [InlineData("throw")]
    public void Incomplete_container_read_is_not_a_quantity(string fault)
    {
        Assert.False(Chapter3RaidStorageQuantity.TrySum(2, i => i == 0 ? new(20, "p") : fault switch
        { "missing" => null, "negative" => new(-1, "p"), "unknown" => new(1, null),
            _ => throw new InvalidOperationException() }, "p", out var total));
        Assert.Equal(0, total);
    }

    [Fact]
    public void Container_sum_is_long_even_when_two_stack_quantities_exceed_int_range()
    {
        Assert.True(Chapter3RaidStorageQuantity.TrySum(2, _ => new(int.MaxValue, "p"), "p", out var total));
        Assert.Equal(2L * int.MaxValue, total);
    }
    [Fact]
    public void Pending_total_is_rechecked_after_locked_safety_census_before_a_retry()
    {
        var h = new Harness(); h.Freeze(); h.World.NoOp = true; h.Poll();
        h.World.NoOp = false; h.Reload();
        h.World.OnSecondLockedRead = () => h.World.Quantities = new[] { 20, 3 };
        for (var i = 0; i <= 10; i++) { h.Poll(); h.Minutes++; }
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.Raid.Slots[0].State);
        Assert.Single(h.World.Takes); Assert.Equal(23, h.World.Quantities.Sum());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unavailable_native_total_never_falls_back_to_a_matching_slot(bool afterWrite)
    {
        var h = new Harness(true); var world = (WorldWithTotals)h.World;
        if (afterWrite)
        {
            h.Freeze(); world.HideAfterTake = true; h.Poll();
            world.HideAfterTake = false; world.Quantities = new[] { 20, 10 };
            world.TotalsAvailable = false; h.Reload(); h.Poll();
            Assert.Equal(Chapter3RaidRemovalState.Pending, h.Raid.Slots[0].State);
            Assert.Single(world.Takes);
        }
        else
        {
            world.TotalsAvailable = false;
            Assert.True(h.Feature.Queue("motel", true).Ok); h.Poll();
            Assert.Null(h.Context.State!.Raid); Assert.Empty(world.Takes);
        }
    }
    private static void Quiet(Chapter3RaidSeizureTests.Harness h) => h.Feature =
        new(h.Context, h.World, h.World, () => h.Input, _ => true,
            report: _ => { }, seizureReport: _ => { });
    private sealed class Harness
    {
        internal Chapter3FeatureContext Context = new()
        { State = new("player", Chapter3Phase.Active, 0, 0, 0), Phase = Chapter3RuntimePhase.Active };
        internal World World = new();
        internal List<string> Warnings = new();
        internal double Minutes = 1080;
        internal Chapter3RaidFeature Feature;
        internal Chapter3RaidJournal Raid => Context.State!.Raid!;
        internal Harness(bool nativeTotals = false) { World = nativeTotals ? new WorldWithTotals() : new World(); Feature = Create(); }
        private Chapter3RaidFeature Create() => new(Context, World, World,
            () => new(true, Minutes, LocalPressureTier.Critical, false, false), _ => true,
            report: _ => { }, seizureReport: Warnings.Add);
        internal void Poll() => Feature.PollLocked();
        internal void Freeze() { Assert.True(Feature.Queue("motel", true).Ok); Poll(); Assert.NotNull(Raid); }
        internal void Reload()
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(Context.State, out var json, out var write), write.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);
            Context.State = envelope!.State; Feature = Create();
        }
    }

    private sealed class WorldWithTotals : World, IChapter3RaidStorageReader
    {
        internal bool TotalsAvailable = true;
        public bool TryReadStorageQuantity(string site, Guid storage, string fingerprint, out long quantity)
        {
            quantity = 0;
            return TotalsAvailable && Chapter3RaidStorageQuantity.TrySum(Quantities.Length,
                i => TryReadSlot(site, new(storage, i), out var read) ? read : null, fingerprint, out quantity);
        }
    }
    private class World : IChapter3RaidWorld, IChapter3RaidSeizureWorld
    {
        private static readonly Guid Storage = Guid.Parse("5dd9e755-23d7-40ef-9822-97f7092a0f5d");
        private const string Fingerprint = "ogkush|brick|20|quality=Standard";
        internal int[] Quantities = { 20, 10 };
        internal bool RestackAfterTake, HideAfterTake, NoOp;
        internal List<int> Takes = new();
        internal Action? OnSecondLockedRead;
        private bool _locked;
        private int _lockedReads;
        public bool IsHost => true;
        public bool IsRestored(string site) => true;
        public void Reset() { }
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions)
        {
            if (_locked && ++_lockedReads == 2) { var change = OnSecondLockedRead; OnSecondLockedRead = null; change?.Invoke(); }
            return new[]
        { new Chapter3RaidSite("motel", "Motel Room", true, Quantities.Select((q, i) =>
            new Chapter3RaidSlot(new(Storage, i), Fingerprint, "ogkush", "brick", q, 20)).ToArray(), Chapter3RaidPresence.Clear) };
        }
        public bool TryReadSlot(string site, Chapter3RaidSlotIdentity identity, out Chapter3RaidSlotRead read)
        {
            var q = Quantities[identity.SlotIndex];
            read = new(q, q == 0 ? null : Fingerprint, Fingerprint);
            return !(HideAfterTake && Takes.Count > 0);
        }
        public bool TryLock(string site, Chapter3RaidSlotIdentity identity) { _locked = true; _lockedReads = 0; return true; }
        public void Unlock(string site, Chapter3RaidSlotIdentity identity) { _locked = false; }
        public void Take(string site, Chapter3RaidSlotIdentity identity, int packages)
        {
            Takes.Add(packages); if (NoOp) return;
            Quantities[identity.SlotIndex] -= packages;
            if (RestackAfterTake) Quantities = new[] { 20, Quantities.Sum() - 20 };
        }
    }
}
