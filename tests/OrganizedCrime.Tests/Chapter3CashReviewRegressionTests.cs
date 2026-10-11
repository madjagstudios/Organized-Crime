using System.Text.Json;
using OrganizedCrime.Model;
using Xunit;
using Harness = OrganizedCrime.Tests.Chapter3CashRuntimeTests.Harness;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CashReviewRegressionTests
{
    private static readonly Release1WorldPoint Hq = new(10, 20, 30);

    private static Chapter3PresentationPlan MixedPlan(Harness h)
    {
        h.Ctx.StorageRoomMarker = () => Hq;
        h.Ctx.State = h.Ctx.State! with
        {
            ActiveDemand = new("demand-1", "rich", "Rich", Chapter3DemandProductType.Weed,
                250, 10, 0, 3, 250, 0, 0, 0, 0, 1, 2, 3, null),
            WarehouseOffer = new(0, Purchased: true, PurchasedDay: 0),
            WarehouseCapacityTier = 1,
            DemandsCompleted = 1
        };
        return Chapter3PresentationPlanner.Build(h.Ctx.State, false, true, new(false, 7, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cash_projection_only_marks_its_two_entries_among_demand_capacity_and_protection(bool markerlessDemand)
    {
        var h = new Harness(); h.Accept();
        var plan = MixedPlan(h);
        Assert.Equal(7, plan.Quest!.Entries.Count);
        var cash = plan.Quest.Entries.Take(2).ToArray();
        var siblings = plan.Quest.Entries.Skip(2).Select(e => markerlessDemand ? e with { Marker = null } : e).ToArray();
        // Move both cash entries away from their planner positions. Include a
        // markerless line with identical copy that belongs to neither cash scope.
        var decoy = new Release1DesiredEntry(cash[0].Text, cash[0].State, null);
        var entries = siblings.Take(2).Append(cash[1]).Concat(siblings.Skip(2)).Append(decoy).Append(cash[0]).ToArray();
        plan = plan with { Quest = plan.Quest with { Entries = entries } };

        var result = h.Cash.Project(plan);

        Assert.Equal(entries.Length, result.Quest!.Entries.Count);
        Assert.Same(plan.Messages, result.Messages);
        Assert.Same(plan.Decision, result.Decision);
        for (var i = 0; i < entries.Length; i++)
        {
            var expected = cash.Any(e => ReferenceEquals(e, entries[i])) ? entries[i] with { Marker = Hq } : entries[i];
            Assert.Equal(expected, result.Quest.Entries[i]);
            if (!cash.Any(e => ReferenceEquals(e, entries[i]))) Assert.Same(entries[i], result.Quest.Entries[i]);
        }
        Assert.Equal(2, result.Quest.Entries.Count(e => e.Marker == Hq));
    }

    [Fact]
    public void No_cash_job_leaves_projection_byte_identical()
    {
        var h = new Harness();
        var plan = MixedPlan(h);
        Assert.Equal(5, plan.Quest!.Entries.Count);
        var before = JsonSerializer.SerializeToUtf8Bytes(plan);
        h.Ctx.StorageRoomMarker = () => throw new InvalidOperationException("No cash projection should read the HQ.");

        var result = h.Cash.Project(plan);

        Assert.Same(plan, result);
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_duplicate_cash_entry_leaves_projection_unchanged(bool duplicate)
    {
        var h = new Harness(); h.Accept();
        var plan = MixedPlan(h);
        var entries = duplicate ? plan.Quest!.Entries.Append(plan.Quest.Entries[0]).ToArray()
            : plan.Quest!.Entries.Skip(1).ToArray();
        plan = plan with { Quest = plan.Quest with { Entries = entries } };
        Assert.Same(plan, h.Cash.Project(plan));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-plan")]
    [InlineData("v1|0,1000,0|0,1000,0")]
    public void Unparseable_cash_plan_holds_once_without_throw_drain_credit_or_expiry(string encoded)
    {
        var h = new Harness(); h.Accept(); h.Deposit(5);
        h.Ctx.State = h.Ctx.State! with
        {
            JobBoard = h.Ctx.State!.JobBoard with { ActiveJob = h.Job with { CashMovement = h.Run with { Plan = encoded } } }
        };

        Assert.Null(Record.Exception(() => h.Poll()));
        var held = h.Ctx.State;
        h.Poll(); h.Poll(5); h.Cash.Fail(5);

        Assert.Same(held, h.Ctx.State);
        Assert.True(h.Run.Ambiguous);
        Assert.Equal(encoded, h.Run.Plan);
        Assert.Equal(0, h.Run.ConfirmedSlots);
        Assert.Equal(0, h.Room.OwnedLocks);
        Assert.Equal(5, h.Room.Cash.Count);
        Assert.Empty(h.Room.Changes);
        Assert.Empty(h.Wallet.CashChanges);
        Assert.Empty(h.Ctx.State!.Ledger.Entries);
        Assert.NotNull(Chapter3JobScheduler.OnDayTick(h.Ctx.State.JobBoard, "player", 5).Board.ActiveJob);
        Assert.Contains("ambiguous", Assert.Single(h.Cash.Status(), s => s.Contains("plan state")));
        Assert.Contains("unreadable", Assert.Single(h.Logs, s => s.Contains("cash holding:")));
    }

    [Fact]
    public void Cash_QA_offer_log_includes_offer_id_like_storage()
    {
        var h = new Harness();
        Assert.True(h.Cash.Queue("offer").Ok);
        h.Poll();
        Assert.Equal($"Cash QA offer {h.Offer.OfferId}: {Chapter3NellCopy.CashOfferText(h.Offer.CashMovement!, h.Offer.Reward)}",
            Assert.Single(h.Logs));
    }
}
