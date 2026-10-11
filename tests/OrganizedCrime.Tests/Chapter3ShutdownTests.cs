using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Release1.World;
using Xunit;
using WalletWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ShutdownTests
{
    internal static Release1ProductionPropertySnapshot Property(string code, bool staff = false, bool station = false,
        Release1ProductionStationKind kind = Release1ProductionStationKind.ChemistryStation) =>
        new(code, code + " name", new[] { new Release1ProductionEmployeeSnapshot(code + "-employee", "Worker", "Chemist",
            false, true, false, 0, staff, null, null, false) },
            new[] { new Release1ProductionStationSnapshot(code + "-station", kind, station, station ? "batch" : null, 0) });

    [Fact]
    public void Selection_is_order_independent_and_only_operating_owned_properties_qualify()
    {
        var properties = new[] { Property("B", station: true), Property("A", staff: true), Property("quiet") };
        var first = Chapter3Shutdown.Select(new(properties), "player", 7);
        Assert.Contains(first!.PropertyCode, new[] { "A", "B" });
        Assert.Equal(first, Chapter3Shutdown.Select(new(properties.Reverse().ToArray()), "player", 7));
        Assert.Null(Chapter3Shutdown.Select(new(new[] { Property("quiet") }), "player", 7));
        Assert.Null(Chapter3Shutdown.Select(Release1ProductionActivitySnapshot.Empty, "player", 7));
    }

    [Theory]
    [InlineData(Release1ProductionStationKind.ChemistryStation)]
    [InlineData(Release1ProductionStationKind.LabOven)]
    [InlineData(Release1ProductionStationKind.MixingStation)]
    [InlineData(Release1ProductionStationKind.Cauldron)]
    [InlineData(Release1ProductionStationKind.DryingRack)]
    public void Every_monitored_running_station_blocks_the_window(Release1ProductionStationKind kind)
    {
        var h = new Harness(); h.World.Snapshot = new(new[] { Property("target", station: true, kind: kind) });
        h.Accept(); h.Poll(); Assert.Null(h.Run.QuietSince);
        h.World.Snapshot = new(new[] { Property("target", kind: kind) }); h.Advance(1); h.Poll();
        Assert.Equal(h.Wallet.CanonicalMinutes, h.Run.QuietSince); Assert.False(h.Run.QuietComplete);
    }

    [Fact]
    public void Working_staff_and_existing_batches_must_finish_without_breach_before_window()
    {
        var h = new Harness(); h.Accept(); h.Poll(); h.Advance(60); h.Poll();
        Assert.Null(h.Run.QuietSince); Assert.Null(h.Run.BreachSince); Assert.Empty(h.Ctx.State!.Ledger.Entries);
        h.Advance(1); h.Quiet(); h.Poll(); Assert.NotNull(h.Run.QuietSince);
    }

    [Fact]
    public void Quiet_window_completes_once_after_mid_window_reload_and_work_elsewhere_is_ignored()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll();
        var start = h.Run.QuietSince; h.Advance(360); h.Reload();
        h.World.Snapshot = new(new[] { Property("target"), Property("new-owned", staff: true, station: true) });
        h.Poll(); Assert.Equal(start, h.Run.QuietSince);
        h.Advance(359); h.Poll(); Assert.Empty(h.Wallet.CashChanges);
        h.Advance(1); h.Poll(); h.Reload(); h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Equal(16000f, Assert.Single(h.Wallet.CashChanges));
        Assert.True(h.Ctx.State.JobBoard.LastCompletedJob!.Shutdown!.Paid);
        Assert.Equal(1, h.Ctx.State.JobBoard.SuccessfulJobs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sustained_work_after_window_starts_fails_with_reward_make_good(bool staff)
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(1);
        h.World.Snapshot = new(new[] { Property("target", staff: staff, station: !staff) });
        h.Poll(); Assert.Equal(1, h.Run.BreachObservations); h.Reload();
        h.Advance(59); h.Poll(); Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
        h.Advance(1); h.Poll();
        Assert.Null(h.Ctx.State.JobBoard.ActiveJob); Assert.Empty(h.Wallet.CashChanges);
        var debt = Assert.Single(h.Ctx.State.Ledger.Entries); Assert.Equal(16000, debt.Amount); Assert.Equal(2, debt.DueDay);
    }

    [Fact]
    public void Brief_single_work_read_heals_before_elapsed_and_does_not_fail()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); var start = h.Run.QuietSince;
        h.Advance(700); h.Work(); h.Poll(); h.Advance(20); h.Quiet(); h.Poll();
        Assert.Equal(start, h.Run.QuietSince); Assert.Null(h.Run.BreachSince); Assert.Empty(h.Wallet.CashChanges);
        h.Poll(); Assert.Single(h.Wallet.CashChanges);
    }

    [Fact]
    public void Unreadable_never_starts_completes_cancels_or_heals_a_breach_and_logs_once()
    {
        var h = new Harness(); h.Accept(); h.World.Readable = false; h.Poll(); h.Poll();
        Assert.Null(h.Run.QuietSince); Assert.Contains(h.Shutdown.Status(), s => s.Contains("activity unreadable"));
        Assert.Single(h.Logs, s => s.Contains("shutdown holding"));
        h.World.Readable = true; h.Quiet(); h.Advance(1); h.Poll(); h.Advance(1); h.Work(); h.Poll();
        var mark = h.Run.BreachSince; h.World.Readable = false; h.Advance(800); h.Poll();
        Assert.Equal(mark, h.Run.BreachSince); Assert.False(h.Run.QuietComplete); Assert.Empty(h.Wallet.CashChanges);
        h.World.Readable = true; h.Advance(1); h.Poll(); Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Invalid_snapshot_is_unreadable_not_quiet_or_sold()
    {
        var h = new Harness(); h.Accept(); h.World.Snapshot = new(new[] { Property("target"), Property("target") });
        h.Poll(); Assert.Null(h.Run.QuietSince); Assert.Contains(h.Shutdown.Status(), s => s.Contains("activity unreadable"));
    }

    [Fact]
    public void Sold_target_cancels_without_debt_even_if_other_properties_are_working()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll();
        h.World.Snapshot = new(new[] { Property("elsewhere", staff: true) }); h.Advance(1); h.Poll(3);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Empty(h.Ctx.State.Ledger.Entries);
        Assert.Empty(h.Wallet.CashChanges); Assert.Equal(0, h.Ctx.State.JobBoard.SuccessfulJobs);
    }

    [Fact]
    public void Refused_accept_records_marker_and_keeps_exact_quote_and_host_client_noop()
    {
        var h = new Harness(); h.Freeze(); var offer = h.Offer; h.World.Readable = false;
        Assert.False(h.Offers.AcceptOfferLocked(offer, 0).Applied);
        Assert.Equal(offer, h.Offer); Assert.Equal(offer.OfferId, h.Ctx.State!.JobBoard.LastRefusedOfferId);
        h.Host.Ready = false; var before = h.Ctx.State;
        h.Poll(); h.Shutdown.Fail(0); Assert.False(h.Shutdown.Queue("offer").Ok);
        Assert.False(h.Offers.AcceptOfferLocked(offer, 0).Applied); Assert.Same(before, h.Ctx.State);
    }

    [Fact]
    public void Late_clean_poll_does_not_start_payout_and_scheduler_fails()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(720); h.Poll(3);
        Assert.Empty(h.Wallet.CashChanges); Assert.False(h.Run.QuietComplete);
        var tick = Chapter3JobScheduler.OnDayTick(h.Ctx.State!.JobBoard, "player", 3);
        Assert.Null(tick.Board.ActiveJob); Assert.Single(tick.Effects.OfType<Chapter3MakeGoodRequiredEffect>());
    }

    [Fact]
    public void Payout_intent_saved_inside_credit_resumes_and_suppresses_deadline_and_repeat_credit()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(720);
        string? captured = null; h.Ctx.Wallet = new CaptureWallet(h.Ctx.Wallet!, () => captured = h.Serialize());
        h.Poll(); Assert.Single(h.Wallet.CashChanges);
        h.Load(captured!); Assert.True(h.Run.Reconciling); h.Shutdown.Fail(3);
        var tick = Chapter3JobScheduler.OnDayTick(h.Ctx.State!.JobBoard, "player", 3);
        Assert.NotNull(tick.Board.ActiveJob);
        h.Poll(3); Assert.Null(h.Ctx.State.JobBoard.ActiveJob); Assert.Single(h.Wallet.CashChanges);
        Assert.Empty(h.Ctx.State.Ledger.Entries);
    }

    [Fact]
    public void Divergent_partial_credit_holds_once_and_never_retries_or_opens_debt()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll();
        h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
            { ActiveJob = h.Job with { Shutdown = h.Run with { QuietComplete = true, CreditBefore = 0 } } } };
        h.Wallet.CashBalance = 8000; h.Poll(); h.Reload(); h.Wallet.CashBalance = 0;
        h.Poll(3); h.Poll(3); h.Shutdown.Fail(3);
        Assert.True(h.Run.CreditAmbiguous); Assert.Empty(h.Wallet.CashChanges); Assert.Empty(h.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Qa_hold_requires_quiet_reads_and_preserves_adjusted_start_on_reload()
    {
        var h = new Harness(); h.Accept();
        Assert.True(h.Shutdown.Queue("done").Ok); h.Poll(); Assert.Null(h.Run.QuietSince);
        h.Quiet(); h.Poll(); Assert.True(h.Shutdown.Queue("5").Ok); h.Advance(1); h.Poll();
        var start = h.Run.QuietSince; h.Reload(); Assert.Equal(start, h.Run.QuietSince);
        h.Advance(5); h.Poll(); Assert.Single(h.Wallet.CashChanges);
    }

    [Fact]
    public void Schema19_migrates_without_shutdown_and_schema20_round_trips_progress()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(1); h.Work(); h.Poll();
        var before = h.Ctx.State; h.Reload(); Assert.Equal(before, h.Ctx.State);
        var root = System.Text.Json.Nodes.JsonNode.Parse(h.Serialize())!; root["schemaVersion"] = 19;
        root["state"]!["jobBoard"]!["activeJob"]!.AsObject().Remove("shutdown");
        Assert.True(Chapter3SaveCodec.TryDeserialize(root.ToJsonString(), out var migrated, out var result), result.Message);
        Assert.Null(migrated!.State!.JobBoard.ActiveJob!.Shutdown); Assert.Equal(20, migrated.SchemaVersion);
    }

    [Fact]
    public void Schema20_rejects_unknown_missing_and_inconsistent_shutdown_fields()
    {
        var h = new Harness(); h.Accept();
        foreach (var corrupt in new[] { "unknown", "missing", "progress" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(h.Serialize())!;
            var run = root["state"]!["jobBoard"]!["activeJob"]!["shutdown"]!.AsObject();
            if (corrupt == "unknown") run["surprise"] = true;
            if (corrupt == "missing") run.Remove("propertyCode");
            if (corrupt == "progress") run["paid"] = true;
            Assert.False(Chapter3SaveCodec.TryDeserialize(root.ToJsonString(), out _, out _));
        }
    }

    [Fact]
    public void Clock_unreadable_never_starts_or_completes_and_wallet_unreadable_never_becomes_zero()
    {
        var h = new Harness(); h.Accept(); h.Quiet();
        h.Wallet.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable; h.Poll();
        Assert.Null(h.Run.QuietSince);
        h.Wallet.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Ready; h.Poll(); h.Advance(720);
        h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Faulted; h.Poll();
        Assert.True(h.Run.QuietComplete); Assert.Null(h.Run.CreditBefore); Assert.Empty(h.Wallet.CashChanges);
        h.Reload(); h.Shutdown.Fail(3); Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
        h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Ready; h.Poll(3);
        Assert.Single(h.Wallet.CashChanges); Assert.Empty(h.Ctx.State.Ledger.Entries);
    }

    [Fact]
    public void Credit_that_never_landed_can_retry_after_reload_but_paid_receipt_never_repeats()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(720);
        h.Wallet.SuppressCashApply = true; h.Poll();
        Assert.False(h.Run.Paid); Assert.Equal(0, h.Run.CreditBefore); Assert.False(h.Run.CreditAmbiguous);
        h.Reload(); h.Wallet.SuppressCashApply = false; h.Poll(3);
        Assert.Equal(16000f, h.Wallet.CashBalance); Assert.Equal(2, h.Wallet.CashChanges.Count);
        h.Reload(); h.Poll(3); Assert.Equal(2, h.Wallet.CashChanges.Count);
    }

    [Fact]
    public void Same_code_name_change_and_quiet_accept_preserve_quoted_property()
    {
        var h = new Harness(); h.Freeze(); var frozen = h.Offer.Shutdown!;
        h.World.Snapshot = new(new[] { Property("target") with { PropertyName = "renamed" }, Property("other", staff: true) });
        Assert.True(h.Offers.AcceptOfferLocked(h.Offer, 0).Applied);
        Assert.Equal(frozen, h.Run); Assert.Equal("target name", h.Run.PropertyName);
        h.Poll(); Assert.NotNull(h.Run.QuietSince);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("721")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("bogus")]
    public void Invalid_QA_hold_does_not_enqueue(string command)
    {
        var h = new Harness(); h.Accept(); var before = h.Ctx.State;
        Assert.False(h.Shutdown.Queue(command).Ok); Assert.Same(before, h.Ctx.State);
    }

    [Fact]
    public void Stable_minute_poll_does_not_repeatedly_read_properties_and_load_clears_only_gate()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Poll();
        var count = h.World.Reads; var start = h.Run.QuietSince;
        h.Poll(); h.Poll(); Assert.Equal(count, h.World.Reads);
        h.Reload(); h.Poll(); Assert.True(h.World.Reads > count); Assert.Equal(start, h.Run.QuietSince);
    }
    [Fact]
    public void Switching_pending_shutdown_to_sibling_QA_offers_drops_only_shutdown_terms()
    {
        var h = new Harness(); h.Freeze();
        var cash = new Chapter3CashRuntimeTests.Harness();
        Assert.True(cash.Cash.TryFreezeOffer(h.Offer, out var cashOffer)); Assert.Null(cashOffer.Shutdown);
        var delivery = new Chapter3DeliveryFeature(h.Ctx, _ => { });
        var storage = new Chapter3StorageFeature(h.Ctx, delivery);
        h.Ctx.StorageRoomReader = () => new(Release1HoldRoomReadiness.Ready,
            Chapter3StorageRun.PlayerClosetIds.Select(id => new Release1HoldRoomClosetSnapshot(id, 1,
                new[] { new Release1SmallCourtesySlotSnapshot(0, "", "", 0, false, 0) })).ToArray());
        h.Wallet.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 100, true));
        h.Wallet.SeedDrops(new("a", "A", "description", 1, 2, 3, true), new("b", "B", "description", 4, 5, 6, true));
        Assert.True(storage.TryFreezeOffer(h.Offer, out var storageOffer)); Assert.Null(storageOffer.Shutdown);
        var emergency = Chapter3JobScheduler.ForceEmergencyOffer(h.Ctx.State!.JobBoard);
        Assert.Null(emergency.OpenOffer!.Shutdown);
    }
    [Fact]
    public void Sale_after_quiet_completion_but_before_credit_intent_cancels_without_payment_or_debt()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(720);
        h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Unavailable; h.Poll();
        Assert.True(h.Run.QuietComplete); Assert.Null(h.Run.CreditBefore);
        h.Reload(); h.World.Snapshot = Release1ProductionActivitySnapshot.Empty;
        h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Ready; h.Poll(3);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Empty(h.Wallet.CashChanges); Assert.Empty(h.Ctx.State.Ledger.Entries);
        Assert.Equal(0, h.Ctx.State.JobBoard.SuccessfulJobs);
    }
    [Fact]
    public void Verified_credit_is_final_even_if_redundant_balance_read_would_be_unreadable()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(720);
        h.Ctx.Wallet = new VerifiedWallet(h.Ctx.Wallet!, () => h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Unavailable);
        h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.True(h.Ctx.State.JobBoard.LastCompletedJob!.Shutdown!.Paid);
        h.Wallet.CashBalance = 0; h.Wallet.CashStatus = Release1SmallCourtesyWorldReadStatus.Ready;
        h.Reload(); h.Poll(3); Assert.Single(h.Wallet.CashChanges);
    }

    private sealed class VerifiedWallet : IChapter3Wallet
    {
        private readonly IChapter3Wallet _inner; private readonly Action _after;
        internal VerifiedWallet(IChapter3Wallet inner, Action after) { _inner = inner; _after = after; }
        public Chapter3WalletDebit TryDebit(int amount) => _inner.TryDebit(amount);
        public Chapter3WalletCredit TryCredit(int amount)
        {
            var result = _inner.TryCredit(amount); Assert.Equal(Chapter3WalletCredit.Credited, result); _after(); return result;
        }
    }
    internal sealed class Harness
    {
        internal readonly World World = new();
        internal readonly WalletWorld Wallet = new() { CashBalance = 0, CanonicalMinutes = 6000 };
        internal readonly Chapter3CashRuntimeTests.HostContext Host = new();
        internal readonly List<string> Logs = new();
        internal readonly Chapter3FeatureContext Ctx;
        internal readonly Chapter3OffersFeature Offers;
        internal readonly Chapter3ShutdownFeature Shutdown;
        internal Chapter3JobOffer Offer => Ctx.State!.JobBoard.OpenOffer!;
        internal Chapter3ActiveJob Job => Ctx.State!.JobBoard.ActiveJob!;
        internal Chapter3Shutdown Run => Job.Shutdown!;
        internal Harness()
        {
            Ctx = new Chapter3FeatureContext
            {
                Context = Host, Phase = Chapter3RuntimePhase.Active, Log = Logs.Add,
                State = Chapter3State.CreateInitial("player") with
                {
                    Phase = Chapter3Phase.Active, StartElapsedDay = 0, LastProcessedElapsedDay = 0,
                    JobBoard = Chapter3JobBoard.Empty with { OpenOffer = new(1, Chapter3JobType.OperationalShutdown, 16000, 0, 1) }
                },
                DeadDropWorld = Wallet, ShutdownWorld = World, Wallet = new Chapter3Wallet(Wallet),
                LoggedDeliveryHolds = new OnceLog(Logs.Add)
            };
            var delivery = new Chapter3DeliveryFeature(Ctx, _ => { });
            Shutdown = new Chapter3ShutdownFeature(Ctx, delivery); Ctx.Shutdown = Shutdown;
            Offers = new Chapter3OffersFeature(Ctx, delivery);
        }
        internal void Freeze() { Assert.True(Shutdown.TryFreezeOffer(Offer, out var frozen)); Ctx.State = Ctx.State! with { JobBoard = Ctx.State.JobBoard with { OpenOffer = frozen } }; }
        internal void Accept() { Freeze(); Assert.True(Offers.AcceptOfferLocked(Offer, 0).Applied); }
        internal void Quiet() => World.Snapshot = new(new[] { Property("target") });
        internal void Work() => World.Snapshot = new(new[] { Property("target", station: true) });
        internal void Advance(double minutes) => Wallet.CanonicalMinutes += minutes;
        internal void Poll(int day = 0) => Shutdown.Poll(day);
        internal string Serialize() { Assert.True(Chapter3SaveCodec.TrySerialize(Ctx.State, out var json, out var result), result.Message); return json; }
        internal void Reload() => Load(Serialize());
        internal void Load(string json) { Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var result), result.Message); Ctx.State = envelope!.State; Shutdown.ClearQaForLoad(); }
    }
    internal sealed class World : IProductionActivityWorld
    {
        internal bool Readable = true;
        internal int Reads;
        internal Release1ProductionActivitySnapshot Snapshot = new(new[] { Property("target", station: true) });
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot snapshot)
        { Reads++; snapshot = Snapshot; return Readable ? Release1SmallCourtesyWorldReadStatus.Ready : Release1SmallCourtesyWorldReadStatus.Unavailable; }
    }
    private sealed class CaptureWallet : IChapter3Wallet
    {
        private readonly IChapter3Wallet _inner; private readonly Action _capture;
        internal CaptureWallet(IChapter3Wallet inner, Action capture) { _inner = inner; _capture = capture; }
        public Chapter3WalletDebit TryDebit(int amount) => _inner.TryDebit(amount);
        public Chapter3WalletCredit TryCredit(int amount) { _capture(); return _inner.TryCredit(amount); }
    }
}
