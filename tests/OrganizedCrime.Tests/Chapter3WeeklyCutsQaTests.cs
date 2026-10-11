using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3WeeklyCutsQaTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Weekpass_forwards_the_result_message()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "week 1 cut due") };

        var lines = Chapter3QaCommand.Execute(target, new[] { "weekpass" }, qaEnabled: true);

        Assert.Equal(new[] { "week 1 cut due" }, lines);
        Assert.Equal(new[] { "weekpass" }, target.Calls);
    }

    [Fact]
    public void Paycut_needs_a_numeric_week()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "paid") };

        Assert.Equal(new[] { "Usage: oc_ch3 paycut <week>" }, Chapter3QaCommand.Execute(target, new[] { "paycut" }, qaEnabled: true));
        Assert.Equal(new[] { "Usage: oc_ch3 paycut <week>" }, Chapter3QaCommand.Execute(target, new[] { "paycut", "abc" }, qaEnabled: true));
        Assert.Empty(target.Calls);

        var lines = Chapter3QaCommand.Execute(target, new[] { "paycut", "3" }, qaEnabled: true);

        Assert.Equal(new[] { "paid" }, lines);
        Assert.Equal(new[] { "paycut:3" }, target.Calls);
    }

    [Fact]
    public void Paycut_accepts_the_hash_form_that_status_prints()
    {
        var target = new FakeTarget { Result = new Chapter3QaResult(true, "ok") };

        Chapter3QaCommand.Execute(target, new[] { "paycut", "#2" }, qaEnabled: true);

        Assert.Equal(new[] { "paycut:2" }, target.Calls);
    }

    [Fact]
    public void Status_shows_the_cuts()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 1, 7, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.OnWeekPass(cuts, 2, 14, warehouseOwned: true).Cuts;
        cuts = Chapter3WeeklyCutTransitions.OnDayTick(cuts, 10).Cuts;
        var state = ActiveState() with { Cuts = cuts };
        var target = new FakeTarget { State = state };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("cut #1: $30,000, due day 7, grace until day 9, Missed", lines);
        Assert.Contains("cut #2: $30,000, due day 14, grace until day 16, Due", lines);
    }

    [Fact]
    public void Status_with_no_cuts_says_none()
    {
        var target = new FakeTarget { State = ActiveState() };

        var lines = Chapter3QaCommand.Execute(target, new[] { "status" }, qaEnabled: true);

        Assert.Contains("cuts: none", lines);
    }

    private static Chapter3State ActiveState() =>
        Chapter3State.ProcessDayTick(Chapter3State.CreateInitial(PlayerId), 11, envelopeSatisfied: true).State;

    private sealed class FakeTarget : IChapter3QaTarget
    {
        public Chapter3State? State { get; set; }
        public Chapter3QaResult Result { get; set; } = new(true, "ok");
        public List<string> Calls { get; } = new();

        public Chapter3QaResult QaAcceptOpenOffer()
        {
            Calls.Add("accept");
            return Result;
        }

        public Chapter3QaResult QaFailActiveJob()
        {
            Calls.Add("fail");
            return Result;
        }

        public Chapter3QaResult QaPayMakeGood(int id)
        {
            Calls.Add($"pay:{id}");
            return Result;
        }

        public Chapter3QaResult QaRecordWarning(int id)
        {
            Calls.Add($"warn:{id}");
            return Result;
        }

        public Chapter3QaResult QaSimulateWeekPass()
        {
            Calls.Add("weekpass");
            return Result;
        }

        public Chapter3QaResult QaPayCut(int weekIndex)
        {
            Calls.Add($"paycut:{weekIndex}");
            return Result;
        }

        public Chapter3QaResult QaDeliverActiveJob()
        {
            Calls.Add("deliver");
            return Result;
        }

        public Chapter3QaResult QaOfferEmergency()
        {
            Calls.Add("offer");
            return Result;
        }

        public bool WarehouseOwned { get; set; }

        public bool QaWarehouseOwned() => WarehouseOwned;

        public Chapter3QaResult QaOfferWarehouse()
        {
            Calls.Add("warehouse");
            return Result;
        }

        public int? QaEmergencyMinutesLeft() => null;

        public Chapter3QaResult QaArthurCome() => Result;

        public Chapter3QaResult QaArthurSay(string text) => Result;

        public Chapter3QaResult QaArthurLeave() => Result;

        public IReadOnlyList<string> QaArthurStatus() => new[] { "arthur: parked" };
        public Chapter3QaResult QaArthurHq(string mode) => Result;
        public Chapter3QaResult QaArthurHqHere() => Result;
        public Chapter3QaResult QaArthurVisit(string kind) => Result;
        public Chapter3QaResult QaArthurBeat() => Result;
        public IReadOnlyList<string> QaBeatSnapshot() => new[] { "snapshot" };
        public IReadOnlyList<string> QaBeatInvincible(bool on) => new[] { "invincible" };
        public IReadOnlyList<string> QaBeatAnim(string? trigger) => new[] { "anim" };
        public Chapter3QaResult QaDemandNow() => Result;
        public IReadOnlyList<string> QaDemandEligible() => new[] { "eligible" };
        public IReadOnlyList<string> QaDemandFresh() => new[] { "fresh" };
        public Chapter3QaResult QaDemandExpire() => Result;
        public Chapter3QaResult QaDemandFreshAdd(int units) => Result;
        public Chapter3QaResult QaDemandEligibleAdd(string productId) => Result;
        public IReadOnlyList<string> QaCapacityStatus() => new[] { "capacity" };
        public Chapter3QaResult QaCapacityUnlock() => Result;
        public Chapter3QaResult QaCapacitySet(int tier) => Result;
    }
}
