using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidReviewRegressionTests
{
    [Theory]
    [InlineData(0)] [InlineData(15)] [InlineData(40)] [InlineData(69)]
    public void Cooling_never_raises_heat_below_the_Watched_ceiling(int heat)
    {
        var old = new LocalPressureState(heat, true, 1, 2, 3, "player", "north", "home", 7);
        var cooled = LocalPressureTransitions.ApplyRaidCooling(old, LocalPressureProfile.Moderate, "raid");
        Assert.Equal(heat, cooled.LocalHeat);
        Assert.True(cooled.KnownOffender);
        Assert.Equal(old.LastEvidenceGameTime, cooled.LastEvidenceGameTime);
        Assert.Equal(old.QuietGraceUntil, cooled.QuietGraceUntil);
        Assert.Equal(old.LastDecayEvaluation, cooled.LastDecayEvaluation);
        Assert.Equal(old.Revision + 1, cooled.Revision);
        Assert.Equal("raid", cooled.LastAppliedRaidCoolingId);
        Assert.Same(cooled, LocalPressureTransitions.ApplyRaidCooling(cooled, LocalPressureProfile.Moderate, "raid"));
    }

    [Fact]
    public void Failed_LP_save_reapplies_cooling_despite_a_saved_Chapter3_applied_flag()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        var repo = new PressureRepository();
        using var host = new PressureHost();
        using var pressure = new LocalPressureRuntimeService(host, _ => repo);
        Load(pressure);
        Wire(h, pressure);
        h.Away(); h.Poll(); h.Poll();
        Assert.True(h.State.Raid!.CoolingApplied);
        var id = h.State.Raid.Id;
        repo.FailWrites = true;
        pressure.OnSaveStart(); pressure.OnSaveComplete();
        Assert.Equal(LocalPressureRuntimeRejectReason.SidecarSaveFailed, pressure.LastRejectReason);
        Assert.Null(repo.Record.LastAppliedRaidCoolingId);
        h.Reload();
        Load(pressure);
        Wire(h, pressure);
        Assert.Equal(90, Read(pressure).LocalHeat);
        h.Poll();
        Assert.Equal(LocalPressureProfile.Moderate.WatchedUpperBound, Read(pressure).LocalHeat);
        Assert.Equal(id, Read(pressure).LastAppliedRaidCoolingId);
        Assert.Equal(2, h.World.Writes.Count);
        Assert.True(h.State.Raid.CoolingApplied);
        Assert.True(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        var reheated = Read(pressure);
        h.Poll(); h.Poll();
        Assert.Equal(reheated, Read(pressure));
        repo.FailWrites = false;
        pressure.OnSaveStart(); pressure.OnSaveComplete();
        h.Reload(); Load(pressure); Wire(h, pressure); h.Poll();
        Assert.Equal(reheated, Read(pressure));
        Assert.Equal(2, h.World.Writes.Count);
    }

    [Fact]
    public void Persisted_LP_id_repairs_a_missing_Chapter3_receipt_without_recooling_new_heat()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        var repo = new PressureRepository();
        using var host = new PressureHost();
        using var pressure = new LocalPressureRuntimeService(host, _ => repo);
        Load(pressure); Wire(h, pressure);
        h.Away(); h.Poll(); h.Poll();
        var id = h.State.Raid!.Id;
        Assert.True(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        var reheated = Read(pressure);
        pressure.OnSaveStart(); pressure.OnSaveComplete();
        Assert.Equal(id, repo.Record.LastAppliedRaidCoolingId);
        // Chapter 3 restored the confirmed losses but missed the cooling receipt.
        h.Context.State = h.State with { Raid = h.State.Raid with { CoolingApplied = false } };
        h.Reload(); Load(pressure); Wire(h, pressure);
        h.Poll(); h.Poll();
        Assert.True(h.State.Raid!.CoolingApplied);
        Assert.Equal(reheated, Read(pressure));
        Assert.Equal(2, h.World.Writes.Count);
    }

    [Fact]
    public void Unknown_LP_marker_waits_for_hydration_without_guessing_or_overwriting_the_raid()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away(); h.Poll(); h.Poll();
        var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { NoticeReceipt = raid.NoticeCorrelation(h.State.PlayerId) } };
        var calls = 0;
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => { calls++; return false; }, _ => null);
        h.Input = h.Input with { Minutes = raid.StartMinutes + Chapter3RaidFeature.WeekMinutes + 1 };
        h.Feature.Queue("a", true); h.Poll();
        Assert.Equal(raid.Id, h.State.Raid!.Id);
        Assert.Equal(0, calls);
        Assert.Equal(2, h.World.Writes.Count);
    }

    [Fact]
    public void Missing_LP_marker_and_refused_retry_restore_a_pending_cooling_receipt()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away(); h.Poll(); h.Poll();
        var id = h.State.Raid!.Id;
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => false, _ => false);
        h.Poll();
        Assert.False(h.State.Raid!.CoolingApplied);
        Assert.Equal(id, h.State.Raid.Id);
        h.Reload();
        Assert.False(h.State.Raid!.CoolingApplied);
        Assert.Equal(2, h.World.Writes.Count);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    public void Same_quantity_swap_between_plan_and_freeze_holds_the_original_take(bool livePlanIdentity, int slot)
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        var world = new SwapWorld(h.World, livePlanIdentity, slot);
        h.Feature = new(h.Context, world, world, () => h.Input, _ => true);
        h.Away(); h.Poll(); h.Poll();
        var raid = h.State.Raid!;
        Assert.True(raid.Completed);
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, raid.Slots[slot].State);
        Assert.Equal("p|bag|1", raid.Slots[slot].Take.Fingerprint);
        Assert.Equal(5, h.World.Slots[slot].Quantity);
        Assert.Equal("other|bag|1", h.World.Slots[slot].Fingerprint);
        Assert.Empty(h.World.Writes);
        Assert.Equal(0, raid.ActualUnits);
        Assert.Null(h.State.LastRaidSuccessMinutes);
        h.Reload();
        h.World.Slots = Chapter3RaidSeizureTests.Harness.Stock();
        h.Feature.Queue("a", true); h.Poll(); h.Poll();
        Assert.Empty(h.World.Writes);
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, h.State.Raid!.Slots[slot].State);
    }

    [Fact]
    public void Corroborated_HQ_plan_identity_can_freeze_a_distinct_quantity_independent_fingerprint()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.World.Slots = h.World.Slots.Select(s => s with { Fingerprint = "p|bag|1|value=15" }).ToArray();
        h.World.StableFingerprint = "p|bag|1|quality=Standard";
        var world = new SwapWorld(h.World, true, -1);
        h.Feature = new(h.Context, world, world, () => h.Input, _ => true);
        h.Away(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits);
        Assert.All(h.State.Raid.Slots, s => Assert.Equal("p|bag|1|quality=Standard", s.Take.Fingerprint));
        Assert.Equal(2, h.World.Writes.Count);
    }

    [Theory]
    [InlineData(LocalPressureTier.Quiet)] [InlineData(LocalPressureTier.Watched)]
    [InlineData(LocalPressureTier.Critical)]
    public void QA_now_reports_tier_leniency_but_always_honors_protection(LocalPressureTier tier)
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Input = h.Input with { Tier = tier, Protected = true };
        var result = h.Feature.Queue("a", true);
        Assert.True(result.Ok);
        Assert.Contains($"QA: tier gate bypassed (current tier {tier})", result.Message);
        h.Poll(); h.Poll();
        Assert.Null(h.State.Raid);
        Assert.Empty(h.World.Writes);
        h.Input = h.Input with { Protected = false };
        Assert.True(h.Feature.Queue("a", true).Ok);
        h.Poll(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits);
    }

    [Theory]
    [InlineData("expired")] [InlineData("missing-site")] [InlineData("no-plan")]
    public void Failed_QA_start_leaves_the_same_evenings_natural_raid_available(string failure)
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        MissingSiteWorld? missingWorld = null;
        if (failure == "expired") h.Input = h.Input with { Minutes = 100 };
        Assert.True(h.Feature.Queue("a", true).Ok);
        if (failure == "missing-site")
        {
            missingWorld = new MissingSiteWorld();
            h.Feature = new(h.Context, missingWorld, h.World, () => h.Input, _ => true);
            Assert.True(h.Feature.Queue("a", true).Ok);
            missingWorld.Missing = true;
        }
        if (failure == "no-plan") h.World.Slots = new[] { Chapter3RaidSeizureTests.Harness.Slot(0, 1, 20) };
        if (failure == "expired") h.Input = h.Input with { Minutes = 1080 };
        h.Poll();
        Assert.Null(h.State.Raid);
        Assert.Contains("abandoned evening=False", h.Feature.Status()[0]);
        h.World.Slots = Chapter3RaidSeizureTests.Harness.Stock();
        // Restore availability without rebuilding the feature or resetting its evening.
        if (missingWorld is not null) missingWorld.Missing = false;
        h.Away();
        Assert.NotNull(h.State.Raid);
    }

    [Fact]
    public void Unknown_QA_site_is_reported_immediately_and_does_not_replace_a_valid_queue()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.World.Presence = Chapter3RaidPresence.Inside;
        Assert.True(h.Feature.Queue("a", true).Ok);
        var result = h.Feature.Queue("missing", true);
        Assert.False(result.Ok);
        Assert.Equal("unknown site 'missing' (known: a)", result.Message);
        h.Poll(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits);
    }

    [Fact]
    public void Unknown_QA_site_does_not_create_a_queue_or_bypass_the_natural_tier_gate()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Input = h.Input with { Tier = LocalPressureTier.Quiet };
        Assert.False(h.Feature.Queue("missing", true).Ok);
        h.Away(); h.Poll();
        Assert.Null(h.State.Raid);
        Assert.Empty(h.World.Writes);
    }

    [Fact]
    public void Mid_seizure_sleep_does_not_show_a_raid_line()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away(); h.Poll();
        Assert.Equal(5, h.State.Raid!.ActualUnits);
        Assert.False(h.State.Raid.Completed);
        Assert.Null(Chapter3OvernightLine.Select(h.State, 0));
        h.Poll();
        Assert.Contains("Police have hit", Chapter3OvernightLine.Select(h.State, 0));
    }

    [Fact]
    public void Client_queue_is_refused_and_status_reveals_only_the_host_requirement()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away(); h.Poll();
        h.Input = h.Input with { IsHost = false };
        Assert.False(h.Feature.Queue("a", true).Ok);
        Assert.Equal(new[] { "raid status: host only." }, h.Feature.Status());
        var writes = h.World.Writes.Count;
        h.Poll();
        Assert.Equal(writes, h.World.Writes.Count);
    }

    [Fact]
    public void Equal_journals_hash_by_slot_values_and_order_instead_of_list_identity()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away();
        var raid = h.State.Raid!;
        var equal = raid with { Slots = raid.Slots.Select(s => s with { Take = s.Take with { } }).ToArray() };
        Assert.Equal(raid, equal);
        Assert.Equal(raid.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(raid.GetHashCode(), (raid with { TotalUnits = raid.TotalUnits + 1 }).GetHashCode());
        Assert.NotEqual(raid.GetHashCode(), (raid with { Slots = raid.Slots.Reverse().ToArray() }).GetHashCode());
        Assert.NotEqual(raid.GetHashCode(), (raid with { Slots = raid.Slots.Select(s => s with { State = Chapter3RaidRemovalState.Confirmed }).ToArray() }).GetHashCode());
    }

    [Fact]
    public void Console_unknown_site_returns_the_validation_error_without_queueing()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Input = h.Input with { Tier = LocalPressureTier.Quiet };
        var target = System.Reflection.DispatchProxy.Create<IChapter3QaTarget, QaTarget>();
        ((QaTarget)target).Feature = h.Feature;
        Assert.Equal(new[] { "unknown site 'typo' (known: a)" },
            Chapter3QaCommand.Execute(target, new[] { "raid", "now", "typo", "force-away" }, true));
        h.Away(); h.Poll();
        Assert.Null(h.State.Raid);
        Assert.Empty(h.World.Writes);
    }

    [Fact]
    public void Client_status_is_host_only_even_with_a_persisted_raid()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Away(); h.Poll(); h.Poll();
        h.Input = h.Input with { IsHost = false };
        Assert.Equal(new[] { "raid status: host only." }, h.Feature.Status());
    }

    public class QaTarget : System.Reflection.DispatchProxy, IChapter3RaidSeizureQaTarget
    {
        internal Chapter3RaidFeature Feature = null!;
        public Chapter3QaResult RaidNow(string? siteCode, bool forceAway) => Feature.Queue(siteCode, forceAway);
        public Chapter3QaResult RaidSkipCap() => Feature.SkipCap();
        public IReadOnlyList<string> RaidStatus() => Feature.Status();
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("Unexpected QA action.");
    }

    [Fact]
    public void Queue_reads_host_tier_and_clock_from_one_input_sample()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        var reads = 0;
        var feature = new Chapter3RaidFeature(h.Context, h.World, h.World,
            () => ++reads == 1 ? h.Input : new(false, null, null, null, null), _ => true);
        Assert.True(feature.Queue("a", true).Ok);
        Assert.Equal(1, reads);
    }

    private static void Wire(Chapter3RaidSeizureTests.Harness h, LocalPressureRuntimeService pressure) =>
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, pressure.TryApplyRaidCooling,
            id => pressure.Phase == LocalPressureRuntimePhase.Active && pressure.TryGetState("player", out var state)
                ? state!.LastAppliedRaidCoolingId == id : (bool?)null);

    private static LocalPressureState Read(LocalPressureRuntimeService pressure)
    { Assert.True(pressure.TryGetState("player", out var state)); return state!; }

    private static void Load(LocalPressureRuntimeService pressure)
    { pressure.OnPreLoad(); pressure.OnLoadComplete(); pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady); }

    private sealed class SwapWorld(Chapter3RaidSeizureTests.World inner, bool livePlanIdentity, int swappedSlot)
        : IChapter3RaidWorld, IChapter3RaidSeizureWorld
    {
        private bool _swapped;
        public bool IsHost => true;
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions) => inner.Read(exclusions);
        public bool IsRestored(string code) => inner.IsRestored(code);
        public bool TryReadSlot(string code, Chapter3RaidSlotIdentity id, out Chapter3RaidSlotRead read)
        {
            if (!_swapped && id.SlotIndex == swappedSlot)
            {
                _swapped = true;
                inner.Slots = inner.Slots.Select(s => s.Identity == id ? s with { Fingerprint = "other|bag|1", ProductId = "other" } : s).ToArray();
            }
            var ok = inner.TryReadSlot(code, id, out read);
            if (livePlanIdentity) read = read with { PlanFingerprint = inner.Slots.Single(s => s.Identity == id).Fingerprint };
            return ok;
        }
        public bool TryLock(string code, Chapter3RaidSlotIdentity id) => inner.TryLock(code, id);
        public void Unlock(string code, Chapter3RaidSlotIdentity id) => inner.Unlock(code, id);
        public void Take(string code, Chapter3RaidSlotIdentity id, int packages) => inner.Take(code, id, packages);
    }

    private sealed class MissingSiteWorld : IChapter3RaidWorld
    {
        internal bool Missing;
        public bool IsHost => true;
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions) =>
            Missing ? Array.Empty<Chapter3RaidSite>() : new[] { new Chapter3RaidSite("a", "Site A", true,
                Chapter3RaidSeizureTests.Harness.Stock(), Chapter3RaidPresence.Clear) };
    }

    private sealed class PressureRepository : ILocalPressureStateRepository
    {
        internal bool FailWrites;
        internal LocalPressurePlayerRecord Record = LocalPressurePlayerRecord.FromState(new(90, true, 1, 2, 3, "player", "north", "home", 7));
        public LocalPressureStoreLoadResult Load() => new(true, LocalPressureStoreLoadStatus.Loaded,
            new(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { Record }), LocalPressureStoreFailureReason.None, "loaded");
        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            if (FailWrites) return new(false, LocalPressureStoreUpdateStatus.Rejected, null, LocalPressureStoreFailureReason.AtomicReplacementFailed, "failed LP sidecar");
            Assert.True(LocalPressureSaveCodec.TrySerialize(new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { record }),
                out var json, out var written), written.Message);
            Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var decoded, out var loaded), loaded.Message);
            Record = Assert.Single(decoded!.Players);
            return new(true, LocalPressureStoreUpdateStatus.Updated, null, LocalPressureStoreFailureReason.None, "stored");
        }
    }

    private sealed class PressureHost : ILocalPressureRuntimeHostAdapter
    {
        public string ActiveSaveFolder => @"C:\saves\oc173-review";
        public string CanonicalHostIdentity => "player";
        public event Action? PreLoad { add { } remove { } }
        public event Action? LoadComplete { add { } remove { } }
        public event Action? SaveStart { add { } remove { } }
        public event Action? SaveComplete { add { } remove { } }
        public event Action<LocalPressureClockBoundary>? ClockBoundary { add { } remove { } }
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => LocalPressureHostAuthorityReadStatus.Ready;
        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() => LocalPressureClockBoundaryBindingStatus.Ready;
        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample clock)
        { clock = new(180, 0, 300, null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow); return LocalPressureClockReadStatus.Ready; }
        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        { players = new[] { new LocalPressurePlayerSample("player", true, true, "player", "north", "home", this) }; return LocalPressurePlayerReadStatus.Ready; }
        public void Dispose() { }
    }
}
