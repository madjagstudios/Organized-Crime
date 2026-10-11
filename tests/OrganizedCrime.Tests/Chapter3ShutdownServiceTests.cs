using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;
using Harness = OrganizedCrime.Tests.Chapter3ShutdownTests.Harness;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ShutdownServiceTests
{
    [Fact]
    public void Console_offer_accept_two_saves_reload_mid_window_and_paid_receipt()
    {
        var h = new Harness(); var repository = new Repository(); var clock = new Clock();
        Chapter3RuntimeService Create()
        {
            var service = new Chapter3RuntimeService(h.Host, repository, new Gate(), clock, h.Logs.Add)
                { DeadDropWorld = h.Wallet, Wallet = new Chapter3Wallet(h.Wallet) };
            service.WireShutdownWorld(h.World); service.OnPreLoad(); service.OnLoadComplete(); return service;
        }
        var current = Create(); current.OnDayPassed();
        Assert.Contains("queued", Command(current, "offer", "shutdown")); current.PollEmergencyExpiry();
        var offer = current.State!.JobBoard.OpenOffer!;
        var decision = current.GetPresentationPlan(false).Decision!;
        Assert.Equal("Need target name quiet for twelve hours. Stop the staff and finish the batches first. $16,000, two days. Your other places can keep working.", decision.Prompt);
        Assert.Contains(current.GetPresentationPlan(false).Messages, m => m.Text == Chapter3NellCopy.ShutdownMonitoringText);
        Assert.True(current.TryRecordPresentationReceipt(decision.PromptCorrelationId!));
        current.OnSaveStart(); current.OnSaveComplete(); current.OnSaveStart(); current.OnSaveComplete();
        Assert.Equal(2, repository.Writes); Assert.Contains("\"schemaVersion\": 20", repository.Json!);
        current.Dispose(); current = Create();
        Assert.Equal(offer, current.State!.JobBoard.OpenOffer);
        var reloadedDecision = current.GetPresentationPlan(false).Decision!;
        Assert.Equal(decision.Id, reloadedDecision.Id); Assert.Equal(decision.Prompt, reloadedDecision.Prompt);
        Assert.Equal(Chapter3NellCopy.AnswerRepromptText, reloadedDecision.RepromptText);
        Assert.Contains("accepted", Command(current, "accept")); current.PollEmergencyExpiry();
        Assert.Null(current.State.JobBoard.ActiveJob!.Shutdown!.QuietSince);
        h.Quiet(); h.Advance(1); current.PollEmergencyExpiry();
        var start = current.State.JobBoard.ActiveJob.Shutdown!.QuietSince;
        Assert.Equal(3, current.GetPresentationPlan(false).Quest!.Entries.Count);
        current.OnSaveStart(); current.OnSaveComplete(); current.Dispose(); current = Create();
        Assert.Equal(start, current.State!.JobBoard.ActiveJob!.Shutdown!.QuietSince);
        Assert.Equal(3, current.GetPresentationPlan(false).Quest!.Entries.Count);
        Assert.Contains("queued", Command(current, "shutdown", "hold", "5")); current.PollEmergencyExpiry();
        var adjusted = current.State.JobBoard.ActiveJob.Shutdown!.QuietSince;
        current.OnSaveStart(); current.OnSaveComplete(); current.Dispose(); current = Create();
        Assert.Equal(adjusted, current.State!.JobBoard.ActiveJob!.Shutdown!.QuietSince);
        h.Advance(5); current.PollEmergencyExpiry();
        Assert.Null(current.State.JobBoard.ActiveJob); Assert.Equal(16000f, Assert.Single(h.Wallet.CashChanges));
        Assert.Contains(current.GetPresentationPlan(false).Messages,
            m => m.Text == "Quiet window's done. $16,000's yours. Get back to work.");
        current.OnSaveStart(); current.OnSaveComplete(); current.Dispose(); current = Create(); current.PollEmergencyExpiry();
        Assert.True(current.State!.JobBoard.LastCompletedJob!.Shutdown!.Paid); Assert.Single(h.Wallet.CashChanges); current.Dispose();
    }

    [Fact]
    public void Shutdown_unreadiness_does_not_stop_day_tick_or_reward_debt()
    {
        var h = new Harness(); h.Accept(); var repo = new Repository { Json = h.Serialize() }; var clock = new Clock { Day = 3 };
        using var service = Create(h, repo, clock);
        h.World.Readable = false; service.OnDayPassed();
        Assert.Equal(3, service.State!.LastProcessedElapsedDay); Assert.Null(service.State.JobBoard.ActiveJob);
        var debt = Assert.Single(service.State.Ledger.Entries); Assert.Equal(16000, debt.Amount); Assert.Equal(5, debt.DueDay);
    }

    [Fact]
    public void Poll_before_day_tick_cannot_pay_a_late_quiet_window()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll();
        var repo = new Repository { Json = h.Serialize() }; var clock = new Clock { Day = 3 };
        using var service = Create(h, repo, clock);
        h.Advance(720); service.PollEmergencyExpiry();
        Assert.Empty(h.Wallet.CashChanges);
        service.OnDayPassed(); Assert.Null(service.State!.JobBoard.ActiveJob);
        Assert.Equal(16000, Assert.Single(service.State.Ledger.Entries).Amount);
    }

    [Fact]
    public void Console_expiry_and_client_noop_use_existing_host_poll()
    {
        var h = new Harness(); h.Accept(); var repo = new Repository { Json = h.Serialize() };
        using var service = Create(h, repo, new Clock { Day = 0 });
        h.Host.Ready = false;
        Assert.Contains("host", Command(service, "shutdown", "expire"));
        Assert.Contains("host", Command(service, "shutdown", "hold", "done"));
        Assert.Contains("host", Command(service, "offer", "shutdown"));
        Assert.DoesNotContain("accepted", Command(service, "accept")); Assert.NotNull(service.State!.JobBoard.ActiveJob);
        h.Host.Ready = true;
        Assert.Contains("queued", Command(service, "shutdown", "expire")); service.PollEmergencyExpiry();
        Assert.Null(service.State.JobBoard.ActiveJob);
        Assert.Equal(16000, Assert.Single(service.State.Ledger.Entries).Amount);
    }

    [Fact]
    public void Sold_and_unavailable_target_are_distinct_even_at_deadline()
    {
        var h = new Harness(); h.Accept(); var repo = new Repository { Json = h.Serialize() };
        using var service = Create(h, repo, new Clock { Day = 3 });
        h.World.Readable = false; service.PollEmergencyExpiry();
        Assert.NotNull(service.State!.JobBoard.ActiveJob);
        Assert.Contains(Chapter3QaCommand.Execute(service, new[] { "status" }, true), s => s.Contains("activity unreadable"));
        h.World.Readable = true; h.World.Snapshot = Release1ProductionActivitySnapshot.Empty;
        h.Advance(1); service.OnDayPassed();
        Assert.Null(service.State.JobBoard.ActiveJob); Assert.Empty(service.State.Ledger.Entries);
    }

    [Fact]
    public void Reload_with_running_target_keeps_breach_mark_and_fails_after_grace()
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll(); h.Advance(1); h.Work(); h.Poll();
        var mark = h.Run.BreachSince; var repo = new Repository { Json = h.Serialize() };
        using var service = Create(h, repo, new Clock { Day = 0 });
        Assert.Equal(mark, service.State!.JobBoard.ActiveJob!.Shutdown!.BreachSince);
        h.Advance(60); service.PollEmergencyExpiry();
        Assert.Null(service.State.JobBoard.ActiveJob); Assert.Single(service.State.Ledger.Entries); Assert.Empty(h.Wallet.CashChanges);
    }

    [Fact]
    public void Shutdown_preserves_demand_and_protection_markers_and_has_own_scopes()
    {
        var h = new Harness(); h.Accept();
        var demand = new Chapter3ProductionDemand("demand-1", "rich", "Rich", Chapter3DemandProductType.Weed,
            250, 10, 0, 3, 250, 0, 0, 0, 0, 10, 20, 30, null);
        h.Ctx.State = h.Ctx.State! with { ActiveDemand = demand };
        var before = Chapter3PresentationPlanner.Build(h.Ctx.State with { JobBoard = h.Ctx.State.JobBoard with { ActiveJob = null } },
            false, true, new(true, 3, true));
        var after = Chapter3PresentationPlanner.Build(h.Ctx.State, false, true, new(true, 3, true));
        var others = after.Quest!.Entries.Where(e => e.Scope?.StartsWith("chapter3.shutdown:", StringComparison.Ordinal) != true).ToArray();
        Assert.Equal(before.Quest!.Entries, others);
        var own = after.Quest.Entries.Where(e => e.Scope?.StartsWith("chapter3.shutdown:", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(3, own.Length); Assert.All(own, e => Assert.Null(e.Marker));
        Assert.Contains(others, e => e.Marker == new Release1WorldPoint(10, 20, 30));
    }

    [Fact]
    public void Eligible_offer_cadence_preserves_siblings_and_ineligible_falls_back_without_refusal()
    {
        var h = new Harness();
        var before = Chapter3JobBoard.Empty with { NextOfferId = 8 };
        var board = before with { OpenOffer = new(8, Chapter3JobType.DeadDrop, 20000, 0, 1), NextOfferId = 9 };
        var frozen = h.Offers.FreezeFreshShutdownOfferLocked(before, board);
        Assert.Equal(Chapter3JobType.OperationalShutdown, frozen.OpenOffer!.JobType);
        Assert.Equal(20000, frozen.OpenOffer.Reward);
        h.World.Readable = false;
        Assert.Same(board, h.Offers.FreezeFreshShutdownOfferLocked(before, board));
        Assert.Equal(0, board.RefusalStreak);
        h.World.Readable = true;
        foreach (var type in new[] { Chapter3JobType.EmergencySupply, Chapter3JobType.TemporaryStorageSafehouse, Chapter3JobType.CashMovement })
        {
            var sibling = board with { OpenOffer = board.OpenOffer with { JobType = type } };
            Assert.Same(sibling, h.Offers.FreezeFreshShutdownOfferLocked(before, sibling));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_from_inside_wallet_credit_reconciles_after_reload_without_second_payment(bool afterEffect)
    {
        var h = new Harness(); h.Accept(); h.Quiet(); h.Poll();
        var repo = new Repository { Json = h.Serialize() }; var clock = new Clock { Day = 0 };
        var service = Create(h, repo, clock);
        service.Wallet = new BoundaryWallet(new Chapter3Wallet(h.Wallet), () =>
        {
            service.OnSaveStart(); service.OnSaveComplete();
        }, afterEffect);
        h.Advance(720); service.PollEmergencyExpiry(); Assert.Single(h.Wallet.CashChanges);
        Assert.NotNull(repo.Json); service.Dispose();
        clock.Day = 3; service = Create(h, repo, clock);
        Assert.True(service.State!.JobBoard.ActiveJob!.Shutdown!.Reconciling);
        Assert.Contains("suppressed", Command(service, "fail"));
        Assert.Contains("queued", Command(service, "shutdown", "expire")); service.PollEmergencyExpiry();
        Assert.Null(service.State.JobBoard.ActiveJob); Assert.Single(h.Wallet.CashChanges);
        Assert.Empty(service.State.Ledger.Entries); service.Dispose();
    }

    private sealed class BoundaryWallet : IChapter3Wallet
    {
        private readonly IChapter3Wallet _inner; private readonly Action _capture; private readonly bool _after;
        internal BoundaryWallet(IChapter3Wallet inner, Action capture, bool after) { _inner = inner; _capture = capture; _after = after; }
        public Chapter3WalletDebit TryDebit(int amount) => _inner.TryDebit(amount);
        public Chapter3WalletCredit TryCredit(int amount)
        {
            if (!_after) _capture();
            var result = _inner.TryCredit(amount);
            if (_after) _capture();
            return result;
        }
    }
    [Fact]
    public void Readable_sale_cancels_without_debt_even_when_canonical_minutes_are_unavailable()
    {
        var h = new Harness(); h.Accept(); var repo = new Repository { Json = h.Serialize() };
        using var service = Create(h, repo, new Clock { Day = 3 });
        h.World.Snapshot = Release1ProductionActivitySnapshot.Empty;
        h.Wallet.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        service.OnDayPassed();
        Assert.Null(service.State!.JobBoard.ActiveJob); Assert.Empty(service.State.Ledger.Entries);
        Assert.Empty(h.Wallet.CashChanges);
    }
    private static string Command(Chapter3RuntimeService service, params string[] args) =>
        Assert.Single(Chapter3QaCommand.Execute(service, args, true));
    private static Chapter3RuntimeService Create(Harness h, Repository repo, Clock clock)
    {
        var service = new Chapter3RuntimeService(h.Host, repo, new Gate(), clock, h.Logs.Add)
            { DeadDropWorld = h.Wallet, Wallet = new Chapter3Wallet(h.Wallet) };
        service.WireShutdownWorld(h.World); service.OnPreLoad(); service.OnLoadComplete(); return service;
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        internal int Day = 11;
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int day) { day = Day; return true; }
    }
    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        internal string? Json;
        internal int Writes;
        public Chapter3StoreLoadResult Load()
        {
            if (Json is null) return new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "");
            Assert.True(Chapter3SaveCodec.TryDeserialize(Json, out var loaded, out var result), result.Message);
            return new(true, Chapter3StoreLoadStatus.Loaded, loaded, Chapter3StoreFailureReason.None, "");
        }
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Writes++; Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var result), result.Message); Json = json;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "");
        }
    }
}
