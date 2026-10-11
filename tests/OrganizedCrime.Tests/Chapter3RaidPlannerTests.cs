using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidPlannerTests
{
    private static readonly Guid A = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("00000002-0000-0000-0000-000000000000");
    private static Chapter3RaidSlot Slot(int quantity, int units = 1, int index = 0, Guid? guid = null) =>
        new(new(guid ?? A, index), "fingerprint", "ogkush", units == 1 ? "baggie" : "brick", quantity, units);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Small_totals_have_no_plan(int quantity) =>
        Assert.Null(Chapter3RaidPlanner.Plan(new[] { Slot(quantity) }));

    [Theory]
    [InlineData(4, 1)] [InlineData(7, 1)] [InlineData(8, 2)] [InlineData(101, 25)]
    public void Budget_is_floor_of_total_quarter(int quantity, int expected)
    {
        var plan = Chapter3RaidPlanner.Plan(new[] { Slot(quantity) })!;
        Assert.Equal(expected, plan.Budget);
        Assert.Equal(expected, plan.PlannedUnits);
        Assert.Equal(quantity, plan.Takes.Single().PreQuantity);
        Assert.Equal("fingerprint", plan.Takes.Single().Fingerprint);
    }

    [Fact]
    public void Whole_packages_round_down_and_bricks_are_never_split()
    {
        Assert.Null(Chapter3RaidPlanner.Plan(new[] { Slot(3, 20) }));
        var plan = Chapter3RaidPlanner.Plan(new[] { Slot(5, 20) })!;
        Assert.Equal(100, plan.TotalUnits);
        Assert.Equal(25, plan.Budget);
        Assert.Equal(20, plan.PlannedUnits);
        Assert.Equal(1, plan.Takes.Single().Packages);
    }

    [Fact]
    public void Order_is_guid_then_slot_and_skips_packages_that_do_not_fit()
    {
        var slots = new[] { Slot(4, 1, 3, B), Slot(1, 20, 0), Slot(10, 5, 2), Slot(8, 1, 1) };
        var plan = Chapter3RaidPlanner.Plan(slots)!;
        var reverse = Chapter3RaidPlanner.Plan(slots.Reverse().ToArray())!;
        Assert.Equal(plan.Takes, reverse.Takes);
        Assert.Equal(new[] { 0 }, plan.Takes.Select(t => t.Identity.SlotIndex));
        Assert.Equal(20, plan.PlannedUnits);
        var mixed = Chapter3RaidPlanner.Plan(new[] { Slot(1, 20), Slot(8, 1, 1), Slot(4, 1, 3, B) })!;
        Assert.Equal(8, mixed.PlannedUnits);
        Assert.Equal(1, mixed.Takes.Single().Identity.SlotIndex);
    }

    [Fact]
    public void Duplicate_identity_is_rejected_instead_of_double_counted() =>
        Assert.Throws<ArgumentException>(() => Chapter3RaidPlanner.Plan(new[] { Slot(8), Slot(8) }));

    [Fact]
    public void Totals_use_long_without_int_overflow() =>
        Assert.Equal((long)int.MaxValue * 20, Chapter3RaidPlanner.TotalUnits(new[] { Slot(int.MaxValue, 20) }));

    [Fact]
    public void Picking_uses_most_units_then_stable_site_code_and_blocks_unknown_stock()
    {
        var sites = new[] { Site("b", 20), Site("a", 20), Site("c", 4), Site("unknown", 100, false) };
        Assert.Equal("a", Chapter3RaidPlanner.Pick(sites)!.Code);
        Assert.Null(Chapter3RaidPlanner.Pick(new[] { Site("empty", 0) }));
    }

    private static Chapter3RaidSite Site(string code, int quantity, bool known = true) =>
        new(code, code, known, new[] { Slot(quantity) }, Chapter3RaidPresence.Clear);

    [Fact]
    public void Family_locker_non_product_and_reserved_cash_slots_are_excluded()
    {
        var family = SyndicateHqStorageContract.Definitions.Single(d => d.Name == "HQ_PlayerLocker_J").Guid;
        Assert.False(Chapter3RaidEligibility.Allows(Slot(8, guid: family), new()));
        var exclusions = new Chapter3RaidExclusions();
        exclusions.Slots.Add(Slot(8).Identity);
        Assert.False(Chapter3RaidEligibility.Allows(Slot(8), exclusions));
        Assert.False(Chapter3RaidEligibility.Allows(Slot(8) with { ProductId = "" }, new()));
    }

    [Fact]
    public void Job_fingerprint_is_excluded_even_after_stack_moves_or_quantity_changes()
    {
        var exclusions = new Chapter3RaidExclusions();
        exclusions.Products.Add(("ogkush", "brick"));
        Assert.False(Chapter3RaidEligibility.Allows(Slot(10, 20, 7, B), exclusions));
        Assert.True(Chapter3RaidEligibility.Allows(Slot(10), exclusions));
    }

    [Fact]
    public void Real_storage_run_route_is_excluded_at_any_dedicated_slot()
    {
        var route = new Chapter3PackageRecovery(new(A.ToString("D"), "pickup", 0, 0, 0),
            new(B.ToString("D"), "handoff", 1, 0, 0), "ogkush", "OG Kush", "brick", "Brick", 2);
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.TemporaryStorageSafehouse, 16000, 0, 3, false)
            { StorageRun = new(route, 720) };
        var state = Chapter3State.CreateInitial("player") with { JobBoard = Chapter3JobBoard.Empty with { ActiveJob = job } };
        var exclusions = Chapter3RaidExclusions.From(state);
        Assert.Contains(A, exclusions.Storage);
        Assert.Contains(B, exclusions.Storage);
        var personal = SyndicateHqStorageContract.Definitions.First().Guid;
        Assert.False(Chapter3RaidEligibility.Allows(Slot(7, 20, 3, personal), exclusions));
    }

    [Theory]
    [InlineData("collect")]
    [InlineData("hold")]
    [InlineData("handoff")]
    [InlineData("handoff-pending")]
    [InlineData("payout")]
    [InlineData("payout-pending")]
    public void Storage_run_stages_exclude_only_job_stock_and_route_storage(string stage)
    {
        var closet = Guid.Parse(Chapter3StorageRun.PlayerClosetIds.First());
        var route = new Chapter3PackageRecovery(new(A.ToString("D"), "pickup", 0, 0, 0),
            new(B.ToString("D"), "handoff", 1, 0, 0), "ogkush", "OG Kush", "brick", "Brick", 2,
            StagedSlotIndex: 2, Staged: true, Custody: stage != "collect",
            RemovalConfirmed: stage.StartsWith("payout", StringComparison.Ordinal));
        var run = new Chapter3StorageRun(route, 720,
            StowedAt: stage == "collect" ? null : 100,
            ClosetGuid: stage == "collect" ? null : closet.ToString("D"),
            Released: stage.StartsWith("handoff", StringComparison.Ordinal) || route.RemovalConfirmed,
            PendingSlot: stage == "handoff-pending" ? 2 : null,
            PendingPreQuantity: stage == "handoff-pending" ? 2 : null,
            CreditBefore: stage == "payout-pending" ? 500 : null);
        run.Validate(16000, false);
        var job = new Chapter3ActiveJob(1, 1, Chapter3JobType.TemporaryStorageSafehouse, 16000, 0, 3, false)
            { StorageRun = run };
        var state = Chapter3State.CreateInitial("player") with { JobBoard = Chapter3JobBoard.Empty with { ActiveJob = job } };
        var exclusions = Chapter3RaidExclusions.From(state);
        Assert.Empty(exclusions.Slots);
        Assert.True(exclusions.Storage.SetEquals(new[] { A, B }));
        Assert.Equal(("ogkush", "brick"), Assert.Single(exclusions.Products));

        // Slot 2 is the handoff receipt index, never a reservation in this closet.
        var unrelated = Slot(8, 1, 2, closet);
        var stock = new[] { unrelated, Slot(2, 20, 3, closet), Slot(8, 1, 0, A), Slot(8, 1, 0, B) };
        var eligible = stock.Select(slot => Chapter3RaidEligibility.Capture(slot, exclusions)).Where(slot => slot is not null).Select(slot => slot!).ToArray();
        Assert.Equal(new[] { unrelated }, eligible);
        Assert.Equal(8, Chapter3RaidPlanner.TotalUnits(eligible));
    }

    [Fact]
    public void Actual_cash_plan_slots_are_reserved_even_if_contents_have_changed()
    {
        var h = new Chapter3CashRuntimeTests.Harness();
        h.Accept();
        for (var index = 0; index < 5; index++) h.Room.Cash[index] = 1000;
        Chapter3State? captured = null;
        h.Room.DuringChange = () => captured ??= h.Ctx.State;
        h.Poll();
        var exclusions = Chapter3RaidExclusions.From(captured);
        Assert.Equal(5, exclusions.Slots.Count);
        Assert.All(exclusions.Slots, slot => Assert.False(Chapter3RaidEligibility.Allows(Slot(8) with { Identity = slot },
            exclusions)));
    }
}
