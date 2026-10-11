using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-136: the weekly production demand through the Chapter 3 service.
/// A fake demand world scripts stations, products, the family locker
/// and the HQ door; the tests pin issuing, fresh counting, the capped
/// hand-over, completion vs the make-good miss, reload survival, and
/// the QA surface.
/// </summary>
public sealed class Chapter3ProductionDemandRuntimeTests
{
    private const string PlayerId = "76561190000000001";
    private const int StartElapsedDay = 11;

    [Fact]
    public void Poll_grows_eligibility_with_no_demand_open()
    {
        var harness = new Harness();
        harness.Activate();
        harness.World.SeedStations(RunningStation("s1", "op-1", 40, finishing: false, ("ogkush", 10)));
        harness.Poll();
        harness.World.SeedStations(RunningStation("s1", "op-1", 90, finishing: true, ("ogkush", 22)));

        harness.Poll();

        Assert.Equal(new[] { "ogkush" }, harness.Service.State!.MadeProductIds);
        Assert.Null(harness.Service.State.ActiveDemand);
        Assert.Contains(harness.Logs, line => line.Contains("eligibility", StringComparison.Ordinal));
    }

    [Fact]
    public void Poll_ignores_insertions()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        harness.World.SeedStations(IdleStation("s1", ("ogkush", 10)));
        harness.Poll();
        harness.World.SeedStations(IdleStation("s1", ("ogkush", 40)));

        harness.Poll();

        Assert.Equal(0, harness.Service.State!.ActiveDemand!.FreshCount);
        Assert.Contains(harness.Logs, line => line.Contains("insertion, not counted", StringComparison.Ordinal));
    }

    [Fact]
    public void Poll_idles_with_no_world()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.DemandWorld = null;

        harness.Poll();

        Assert.Empty(harness.Service.State!.MadeProductIds);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("demand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Poll_keeps_the_family_locker_named()
    {
        var harness = new Harness();
        harness.Activate();

        harness.Poll();

        Assert.Equal(new[] { "Family locker" }, harness.World.Renames);
    }

    [Fact]
    public void Tick_issues_three_days_after_the_cut()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        harness.RaiseWeek();

        Assert.Null(harness.Service.State!.ActiveDemand);
        harness.TickTo(StartElapsedDay + 2);
        Assert.Null(harness.Service.State!.ActiveDemand);

        harness.TickTo(StartElapsedDay + 3);

        Assert.NotNull(harness.Service.State.ActiveDemand);
        var demand = harness.Service.State.ActiveDemand!;
        Assert.Equal("d1", demand.DemandId);
        Assert.Equal("ogkush", demand.ProductId);
        Assert.Equal("OG Kush", demand.ProductName);
        Assert.Equal(Chapter3DemandProductType.Weed, demand.ProductType);
        Assert.Equal(250, demand.Quantity);
        Assert.Equal(50, demand.UnitValue);
        Assert.Equal(3, demand.IssueDay);
        Assert.Equal(6, demand.DueDay);
        Assert.Equal(1, harness.Service.State.LastDemandWeek);
        Assert.Equal(2, harness.Service.State.NextDemandId);
        Assert.Equal(10, demand.HqDoorX);
    }

    [Fact]
    public void Tick_issues_nothing_without_eligibility_ownership_or_a_cut()
    {
        var bare = new Harness();
        bare.Activate();
        bare.RaiseWeek();
        bare.TickTo(StartElapsedDay + 5);

        Assert.Null(bare.Service.State!.ActiveDemand);

        var unowned = new Harness();
        unowned.Activate();
        MakeEligible(unowned, "ogkush");
        unowned.Ownership.Owned = false;
        unowned.RaiseWeek();
        unowned.TickTo(StartElapsedDay + 5);

        Assert.Null(unowned.Service.State!.ActiveDemand);

        var cutless = new Harness();
        cutless.Activate();
        MakeEligible(cutless, "ogkush");
        cutless.TickTo(StartElapsedDay + 5);

        Assert.Null(cutless.Service.State!.ActiveDemand);
    }

    [Fact]
    public void Tick_holds_the_issue_on_an_unknown_drug_type()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "mystery");
        harness.World.Products["mystery"] = new Chapter3DemandWorldProductInfo("mystery", "Mystery", "NotADrug", 10);
        harness.RaiseWeek();

        harness.TickTo(StartElapsedDay + 3);

        Assert.Null(harness.Service.State!.ActiveDemand);
        Assert.Contains(harness.Logs, line => line.Contains("unknown drug type", StringComparison.Ordinal));
    }

    [Fact]
    public void Fresh_output_counts_toward_the_open_demand()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        harness.World.SeedStations(RunningStation("s1", "op-9", 10, finishing: false, ("ogkush", 0)));
        harness.Poll();
        harness.World.SeedStations(IdleStation("s1", ("ogkush", 40)));

        harness.Poll();

        Assert.Equal(40, harness.Service.State!.ActiveDemand!.FreshCount);
    }

    [Fact]
    public void Hand_over_takes_capped_fresh_pays_half_and_completes()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 250);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 250, IsPackaged: false, "", UnitsPerPackage: 1));

        harness.Poll();

        var state = harness.Service.State!;
        Assert.Null(state.ActiveDemand);
        Assert.Equal(new Chapter3DemandOutcome("d1", "OG Kush", LeftUnits: 0, Amount: 6_250, Completed: true), state.LastDemandOutcome);
        Assert.Equal(new[] { 6_250 }, harness.Wallet.Credits);
        Assert.Equal(0, harness.World.LockerQuantity(0));
        Assert.Empty(harness.World.LockedSlots);
        Assert.Contains(harness.Logs, line => line.Contains("hand-over: 250 OG Kush, paid $6,250 (250/250)", StringComparison.Ordinal));
    }

    [Fact]
    public void Hand_over_leaves_stale_product_in_the_locker()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 30);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 250, IsPackaged: false, "", UnitsPerPackage: 1));

        harness.Poll();

        Assert.NotNull(harness.Service.State!.ActiveDemand);
        var demand = harness.Service.State!.ActiveDemand!;
        Assert.Equal(30, demand.FreshCount);
        Assert.Equal(30, demand.Delivered);
        Assert.Equal(750, demand.PaidTotal);
        Assert.Equal(1, demand.HandOverCount);
        Assert.Equal(new[] { 750 }, harness.Wallet.Credits);
        Assert.Equal(220, harness.World.LockerQuantity(0));
    }

    [Fact]
    public void Hand_over_takes_one_slot_per_pass()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 250);
        harness.World.SeedLocker(
            new Chapter3DemandLockerSlot(0, "ogkush", 100, IsPackaged: false, "", UnitsPerPackage: 1),
            new Chapter3DemandLockerSlot(1, "ogkush", 150, IsPackaged: false, "", UnitsPerPackage: 1));

        harness.Poll();
        Assert.Equal(100, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(150, harness.World.LockerQuantity(1));

        harness.Poll();

        Assert.Null(harness.Service.State.ActiveDemand);
        Assert.Equal(0, harness.World.LockerQuantity(1));
    }

    [Fact]
    public void Ambiguous_applied_removal_credits_once_without_re_removing()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 100);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 100, IsPackaged: false, "", UnitsPerPackage: 1));
        harness.World.ChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.World.ApplyChangeOnAmbiguous = true;

        harness.Poll();

        Assert.NotNull(harness.Service.State!.ActiveDemand!.PendingRemoval);
        var pending = harness.Service.State!.ActiveDemand!.PendingRemoval!;
        Assert.Equal(0, pending.SlotIndex);
        Assert.Equal(100, pending.PreQuantity);
        Assert.Equal(100, pending.PackageCount);
        Assert.Empty(harness.Wallet.Credits);
        harness.World.ChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        harness.Poll();

        Assert.Equal(100, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(new[] { 2_500 }, harness.Wallet.Credits);
        Assert.Single(harness.World.Changes);
        Assert.Null(harness.Service.State.ActiveDemand.PendingRemoval);
    }

    [Fact]
    public void Ambiguous_unapplied_removal_retries_the_take()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 100);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 100, IsPackaged: false, "", UnitsPerPackage: 1));
        harness.World.ChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;

        harness.Poll();

        Assert.NotNull(harness.Service.State!.ActiveDemand!.PendingRemoval);
        Assert.Equal(100, harness.World.LockerQuantity(0));
        harness.World.ChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        harness.Poll();

        Assert.Equal(100, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(2, harness.World.Changes.Count);
    }

    [Fact]
    public void Unpaid_credit_holds_then_completes_without_re_removing()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 100);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 100, IsPackaged: false, "", UnitsPerPackage: 1));
        harness.Wallet.NextCredit = Chapter3WalletCredit.Unavailable;

        harness.Poll();

        Assert.Equal(0, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.NotNull(harness.Service.State.ActiveDemand.PendingRemoval);
        Assert.Equal(0, harness.World.LockerQuantity(0));
        harness.Wallet.NextCredit = Chapter3WalletCredit.Credited;

        harness.Poll();

        Assert.Equal(100, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(new[] { 2_500 }, harness.Wallet.Credits);
        Assert.Single(harness.World.Changes);
    }

    [Fact]
    public void Miss_opens_a_full_value_make_good()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 100);
        var dueDay = harness.Service.State!.ActiveDemand!.DueDay;

        harness.TickTo(StartElapsedDay + dueDay + 1);

        var state = harness.Service.State!;
        Assert.Null(state.ActiveDemand);
        Assert.Equal(
            new Chapter3DemandOutcome("d1", "OG Kush", LeftUnits: 250, Amount: 12_500, Completed: false),
            state.LastDemandOutcome);
        var entry = Assert.Single(state.Ledger.Entries, e => e.Source is Chapter3MissedDemandSource);
        Assert.Equal(12_500, entry.Amount);
        Assert.Equal(new Chapter3MissedDemandSource("d1"), entry.Source);
    }

    [Fact]
    public void Qa_demand_now_forces_the_issue()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");

        var result = harness.Service.QaDemandNow();

        Assert.True(result.Ok);
        Assert.Contains("d1", result.Message, StringComparison.Ordinal);
        Assert.Contains("250 OG Kush", result.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.Service.State!.ActiveDemand);
    }

    [Fact]
    public void Qa_demand_now_refuses_without_eligibility()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Service.QaDemandNow();

        Assert.False(result.Ok);
        Assert.Contains("nothing has been made", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qa_demand_eligible_and_fresh_report_the_state()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        MakeEligible(harness, "sourdiesel");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 40);

        Assert.Equal(
            new[] { "Chapter 3 demand eligible: ogkush", "Chapter 3 demand eligible: sourdiesel" },
            harness.Service.QaDemandEligible());
        var demand = harness.Service.State!.ActiveDemand!;
        var fresh = demand.ProductId == "ogkush" ? 40 : 0;
        Assert.Equal(
            new[] { $"Chapter 3 demand {demand.DemandId} fresh: made {fresh}, handed 0 of {demand.Quantity}, take cap {fresh}." },
            harness.Service.QaDemandFresh());
    }

    [Fact]
    public void Qa_demand_expire_closes_and_reports()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        var demand = harness.Service.State!.ActiveDemand!;

        var expired = harness.Service.QaDemandExpire();

        Assert.True(expired.Ok);
        Assert.Contains("missed", expired.Message, StringComparison.Ordinal);
        Assert.Null(harness.Service.State!.ActiveDemand);
        Assert.False(harness.Service.State.LastDemandOutcome!.Completed);
        Assert.Equal(demand.Quantity * demand.UnitValue, harness.Service.State.LastDemandOutcome.Amount);
        Assert.True(demand.DueDay > harness.Service.State.ChapterDayAt(harness.Service.State.LastProcessedElapsedDay));
        var closed = harness.Service.QaDemandExpire();
        Assert.False(closed.Ok);
        Assert.Contains("no open demand", closed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_loop_from_eligibility_to_a_paid_completion()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        harness.RaiseWeek();
        harness.TickTo(StartElapsedDay + 3);
        Assert.NotNull(harness.Service.State!.ActiveDemand);
        var demand = harness.Service.State!.ActiveDemand!;
        FreshBatch(harness, demand.ProductId, demand.Quantity);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(
            0, demand.ProductId, demand.Quantity, IsPackaged: false, "", UnitsPerPackage: 1));

        harness.Poll();

        var state = harness.Service.State!;
        Assert.Null(state.ActiveDemand);
        Assert.True(state.LastDemandOutcome!.Completed);
        Assert.Equal(demand.Quantity * demand.UnitValue / 2, state.LastDemandOutcome.Amount);
        Assert.Equal(state.LastDemandOutcome.Amount, Assert.Single(harness.Wallet.Credits));
    }

    [Fact]
    public void Demand_survives_a_reload_and_counts_nothing_first_sight()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 40);
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();

        Assert.NotNull(harness.Service.State!.ActiveDemand);
        var demand = harness.Service.State!.ActiveDemand!;
        Assert.Equal(40, demand.FreshCount);
        harness.World.SeedStations(RunningStation("s1", "op-1", 100, finishing: true, ("ogkush", 500)));

        harness.Poll();

        Assert.Equal(40, harness.Service.State!.ActiveDemand!.FreshCount);
    }

    [Fact]
    public void Partial_take_queues_the_receipt_and_moves_the_quest()
    {
        var harness = new Harness();
        float receiptSeconds = 0;
        harness.Service.ArthurClock = () => receiptSeconds;
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);
        FreshBatch(harness, "ogkush", 100);
        harness.World.SeedLocker(new Chapter3DemandLockerSlot(0, "ogkush", 100, IsPackaged: false, "", UnitsPerPackage: 1));

        harness.Poll();

        receiptSeconds = 3.001f;
        var plan = harness.Service.GetPresentationPlan(release1HasNellDecision: true);
        Assert.Contains(plan.Messages, m => m.Text == "Got 100. 150 to go.");
        Assert.NotNull(plan.Quest);
        Assert.Contains(plan.Quest!.Entries, e => e.Text == "Hand over at the HQ family locker (back of the row nearest Arthur's desk): 100/250");
    }

    [Fact]
    public void Qa_fresh_add_seeds_units_onto_the_open_demand()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);

        var result = harness.Service.QaDemandFreshAdd(50);

        Assert.True(result.Ok);
        Assert.Equal(50, harness.Service.State!.ActiveDemand!.FreshCount);
        Assert.Contains("fresh +50", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qa_fresh_add_is_refused_with_no_open_demand()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Service.QaDemandFreshAdd(50);

        Assert.False(result.Ok);
        Assert.Contains("no open demand", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qa_fresh_add_refuses_non_positive_units()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        Assert.True(harness.Service.QaDemandNow().Ok);

        Assert.False(harness.Service.QaDemandFreshAdd(0).Ok);
        Assert.False(harness.Service.QaDemandFreshAdd(-5).Ok);
        Assert.Equal(0, harness.Service.State!.ActiveDemand!.FreshCount);
    }

    [Fact]
    public void Qa_eligible_add_grows_the_eligibility_set()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Service.QaDemandEligibleAdd("ogkush");

        Assert.True(result.Ok);
        Assert.Equal(new[] { "ogkush" }, harness.Service.State!.MadeProductIds);
        Assert.True(harness.Service.QaDemandNow().Ok);
        Assert.Equal("ogkush", harness.Service.State!.ActiveDemand!.ProductId);
    }

    [Fact]
    public void Qa_eligible_add_is_idempotent_for_known_ids()
    {
        var harness = new Harness();
        harness.Activate();
        MakeEligible(harness, "ogkush");
        var revision = harness.Service.State!.Revision;

        var result = harness.Service.QaDemandEligibleAdd("ogkush");

        Assert.True(result.Ok);
        Assert.Equal(new[] { "ogkush" }, harness.Service.State!.MadeProductIds);
        Assert.Equal(revision, harness.Service.State!.Revision);
    }

    [Fact]
    public void Qa_eligible_add_is_refused_when_inactive()
    {
        var harness = new Harness();

        var result = harness.Service.QaDemandEligibleAdd("ogkush");

        Assert.False(result.Ok);
    }

    [Fact]
    public void Poll_logs_the_locker_name_receipt_once_per_load()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Poll();
        harness.Poll();

        Assert.Single(harness.Logs, line => line.Contains("demand locker named", StringComparison.Ordinal));

        harness.Service.OnPreLoad();
        harness.Activate();
        harness.Poll();

        Assert.Equal(2, harness.Logs.Count(line => line.Contains("demand locker named", StringComparison.Ordinal)));
    }

    [Fact]
    public void Poll_withholds_the_locker_name_receipt_while_the_rename_holds()
    {
        var harness = new Harness();
        harness.Activate();
        harness.World.RenameStatus = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        harness.Poll();

        Assert.DoesNotContain(harness.Logs, line => line.Contains("demand locker named", StringComparison.Ordinal));
        Assert.Contains(harness.Logs, line => line.Contains("locker rename holding", StringComparison.Ordinal));
    }

    private static void MakeEligible(Harness harness, string productId)
    {
        harness.World.SeedStations(RunningStation("s1", "op-elig-" + productId, 10, finishing: false, (productId, 0)));
        harness.Poll();
        harness.World.SeedStations(RunningStation("s1", "op-elig-" + productId, 100, finishing: true, (productId, 10)));
        harness.Poll();
    }

    private static void FreshBatch(Harness harness, string productId, int units)
    {
        harness.World.SeedStations(RunningStation("s1", "op-batch", 10, finishing: false, (productId, 0)));
        harness.Poll();
        harness.World.SeedStations(RunningStation("s1", "op-batch", 100, finishing: true, (productId, units)));
        harness.Poll();
    }

    private static Chapter3DemandStationSnapshot RunningStation(
        string guid, string identity, int progress, bool finishing, params (string Id, int Qty)[] outputs) =>
        new(
            guid,
            Release1ProductionStationKind.MixingStation,
            true,
            identity,
            progress,
            finishing,
            outputs.Select((output, index) => new Chapter3DemandOutputSlot(index, output.Id, output.Qty)).ToArray());

    private static Chapter3DemandStationSnapshot IdleStation(string guid, params (string Id, int Qty)[] outputs) =>
        new(
            guid,
            Release1ProductionStationKind.MixingStation,
            false,
            "idle",
            0,
            Finishing: false,
            outputs.Select((output, index) => new Chapter3DemandOutputSlot(index, output.Id, output.Qty)).ToArray());

    private sealed class Harness
    {
        public FakeDemandWorld World { get; } = new();
        public FakeWallet Wallet { get; } = new();
        public FakeOwnership Ownership { get; } = new() { Owned = true };
        public FakeClock Clock { get; } = new() { ElapsedDays = StartElapsedDay };
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            World.Products["ogkush"] = new Chapter3DemandWorldProductInfo("ogkush", "OG Kush", "Marijuana", 50);
            World.Products["sourdiesel"] = new Chapter3DemandWorldProductInfo("sourdiesel", "Sour Diesel", "Marijuana", 45);
            Service = new Chapter3RuntimeService(
                new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true }, Clock, Logs.Add, Ownership);
            Service.Wallet = Wallet;
            Service.DemandWorld = World;
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }

        public void Poll() => Service.PollProductionDemand();

        public void TickTo(int elapsedDays)
        {
            Clock.ElapsedDays = elapsedDays;
            Service.OnDayPassed();
        }

        public void RaiseWeek() => Clock.RaiseWeek();
    }

    internal sealed class FakeDemandWorld : IChapter3ProductionDemandWorld
    {
        public Release1SmallCourtesyWorldReadStatus StationsStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public List<Chapter3DemandStationSnapshot> Stations { get; } = new();
        public Dictionary<string, Chapter3DemandWorldProductInfo> Products { get; } =
            new(StringComparer.Ordinal);
        public Release1SmallCourtesyWorldReadStatus ProductStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public Dictionary<string, int> PackagingUnits { get; } = new(StringComparer.Ordinal);
        public Release1SmallCourtesyWorldReadStatus LockerStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public List<Chapter3DemandLockerSlot> Locker { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus LockStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public HashSet<int> LockedSlots { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus ChangeStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public bool ApplyChangeOnAmbiguous { get; set; }
        public List<(int SlotIndex, int Amount)> Changes { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus RenameStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public List<string> Renames { get; } = new();
        public bool DoorReadable { get; set; } = true;

        public void SeedStations(params Chapter3DemandStationSnapshot[] stations)
        {
            Stations.Clear();
            Stations.AddRange(stations);
        }

        public void SeedLocker(params Chapter3DemandLockerSlot[] slots)
        {
            Locker.Clear();
            Locker.AddRange(slots);
        }

        public int LockerQuantity(int slotIndex) =>
            Locker.FirstOrDefault(slot => slot.SlotIndex == slotIndex)?.Quantity ?? 0;

        public Release1SmallCourtesyWorldReadStatus TryReadWarehouseStations(
            out IReadOnlyList<Chapter3DemandStationSnapshot> stations)
        {
            stations = Stations.ToArray();
            return StationsStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProductInfo(
            string productId, out Chapter3DemandWorldProductInfo? info)
        {
            info = null;
            if (ProductStatus != Release1SmallCourtesyWorldReadStatus.Ready)
                return ProductStatus;
            if (!Products.TryGetValue(productId, out info))
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackagingUnits(string packagingId, out int units)
        {
            units = 0;
            if (!PackagingUnits.TryGetValue(packagingId, out units))
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadFamilyLockerSlots(
            out IReadOnlyList<Chapter3DemandLockerSlot> slots)
        {
            slots = Locker.ToArray();
            return LockerStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetFamilyLockerSlotLocked(int slotIndex, bool locked)
        {
            if (LockStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return LockStatus;
            if (locked)
                LockedSlots.Add(slotIndex);
            else
                LockedSlots.Remove(slotIndex);
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeFamilyLockerSlotQuantity(int slotIndex, int amount)
        {
            Changes.Add((slotIndex, amount));
            if (ChangeStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded &&
                ChangeStatus != Release1SmallCourtesyWorldMutationStatus.Ambiguous)
                return ChangeStatus;
            var index = Locker.FindIndex(slot => slot.SlotIndex == slotIndex);
            if (index < 0 || !LockedSlots.Contains(slotIndex))
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (ChangeStatus == Release1SmallCourtesyWorldMutationStatus.Succeeded || ApplyChangeOnAmbiguous)
            {
                var slot = Locker[index];
                Locker[index] = slot with { Quantity = slot.Quantity + amount };
            }
            return ChangeStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TryEnsureFamilyLockerName()
        {
            if (RenameStatus == Release1SmallCourtesyWorldMutationStatus.Succeeded)
                Renames.Add(Chapter3DemandPolicy.FamilyLockerName);
            return RenameStatus;
        }

        public bool TryReadHqDoor(out float x, out float y, out float z)
        {
            x = 10f; y = 20f; z = 30f;
            return DoorReadable;
        }
    }

    private sealed class FakeWallet : IChapter3Wallet
    {
        public int Balance { get; private set; }
        public Chapter3WalletCredit NextCredit { get; set; } = Chapter3WalletCredit.Credited;
        public List<int> Credits { get; } = new();

        public Chapter3WalletCredit TryCredit(int amount)
        {
            if (NextCredit != Chapter3WalletCredit.Credited)
                return NextCredit;
            Credits.Add(amount);
            Balance += amount;
            return Chapter3WalletCredit.Credited;
        }

        public Chapter3WalletDebit TryDebit(int amount) => throw new NotImplementedException();
    }

    private sealed class FakeOwnership : IChapter3WarehouseOwnership
    {
        public bool Owned { get; set; }
        public bool IsOwned() => Owned;
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, PlayerId, System.IO.Path.GetTempPath());

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
