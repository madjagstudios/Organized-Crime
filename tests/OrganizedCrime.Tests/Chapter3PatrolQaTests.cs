using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PatrolQaTests
{
    [Theory]
    [InlineData("Quiet")]
    [InlineData("Noticed")]
    [InlineData("Watched")]
    [InlineData("Critical")]
    public void Heat_routes_named_tiers(string tier)
    {
        var target = new Target();
        Assert.Equal("heat:" + tier, Assert.Single(Chapter3QaCommand.Execute(target, new[] { "heat", tier }, true)));
    }
    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void Lockdown_and_auto_route_switches(string mode, bool enabled)
    {
        var target = new Target();
        Assert.Equal("lockdown:" + enabled, Assert.Single(Chapter3QaCommand.Execute(target, new[] { "lockdown", mode }, true)));
        Assert.Equal("auto:" + enabled, Assert.Single(Chapter3QaCommand.Execute(target, new[] { "patrol", "auto", mode }, true)));
    }
    [Theory]
    [InlineData("heat", "99")]
    [InlineData("heat", "4")]
    [InlineData("heat", "Critical", "extra")]
    [InlineData("lockdown", "on", "extra")]
    [InlineData("patrol", "auto", "maybe")]
    public void Invalid_inputs_never_dispatch(params string[] args)
    {
        var target = new Target();
        Chapter3QaCommand.Execute(target, args, true);
        Assert.Equal(0, target.NewCalls);
    }
    [Fact]
    public void Disabled_owner_QA_never_dispatches_new_controls()
    {
        var target = new Target();
        Chapter3QaCommand.Execute(target, new[] { "heat", "Critical" }, false);
        Chapter3QaCommand.Execute(target, new[] { "lockdown", "on" }, false);
        Chapter3QaCommand.Execute(target, new[] { "patrol", "auto", "off" }, false);
        Assert.Equal(0, target.NewCalls);
    }
    private sealed class Target : IChapter3QaTarget, IChapter3PatrolQaTarget
    {
        public Chapter3State? State => null;
        internal int NewCalls;
        public Chapter3QaResult PatrolHeat(LocalPressureTier tier) { NewCalls++; return new(true, "heat:" + tier); }
        public Chapter3QaResult PatrolLockdown(bool on) { NewCalls++; return new(true, "lockdown:" + on); }
        public Chapter3QaResult PatrolAuto(bool on) { NewCalls++; return new(true, "auto:" + on); }
        public Chapter3QaResult PatrolHere(OcPatrolDuty duty) => new(true, "here");
        public Chapter3QaResult PatrolStop() => new(true, "stop");
        public IReadOnlyList<string> PatrolStatus() => new[] { "status" };
        public Chapter3QaResult QaAcceptOpenOffer() => new(true, "ok");
        public Chapter3QaResult QaFailActiveJob() => new(true, "ok");
        public Chapter3QaResult QaPayMakeGood(int id) => new(true, "ok");
        public Chapter3QaResult QaRecordWarning(int id) => new(true, "ok");
        public Chapter3QaResult QaSimulateWeekPass() => new(true, "ok");
        public Chapter3QaResult QaPayCut(int weekIndex) => new(true, "ok");
        public Chapter3QaResult QaDeliverActiveJob() => new(true, "ok");
        public Chapter3QaResult QaOfferEmergency() => new(true, "ok");
        public int? QaEmergencyMinutesLeft() => null;
        public Chapter3QaResult QaOfferWarehouse() => new(true, "ok");
        public bool QaWarehouseOwned() => false;
        public Chapter3QaResult QaArthurCome() => new(true, "ok");
        public Chapter3QaResult QaArthurSay(string text) => new(true, "ok");
        public Chapter3QaResult QaArthurLeave() => new(true, "ok");
        public IReadOnlyList<string> QaArthurStatus() => Array.Empty<string>();
        public Chapter3QaResult QaArthurHq(string mode) => new(true, "ok");
        public Chapter3QaResult QaArthurHqHere() => new(true, "ok");
        public Chapter3QaResult QaArthurVisit(string kind) => new(true, "ok");
        public Chapter3QaResult QaArthurBeat() => new(true, "ok");
        public IReadOnlyList<string> QaBeatSnapshot() => Array.Empty<string>();
        public IReadOnlyList<string> QaBeatInvincible(bool on) => Array.Empty<string>();
        public IReadOnlyList<string> QaBeatAnim(string? trigger) => Array.Empty<string>();
        public Chapter3QaResult QaDemandNow() => new(true, "ok");
        public IReadOnlyList<string> QaDemandEligible() => Array.Empty<string>();
        public IReadOnlyList<string> QaDemandFresh() => Array.Empty<string>();
        public Chapter3QaResult QaDemandExpire() => new(true, "ok");
        public Chapter3QaResult QaDemandFreshAdd(int units) => new(true, "ok");
        public Chapter3QaResult QaDemandEligibleAdd(string productId) => new(true, "ok");
        public IReadOnlyList<string> QaCapacityStatus() => Array.Empty<string>();
        public Chapter3QaResult QaCapacityUnlock() => new(true, "ok");
        public Chapter3QaResult QaCapacitySet(int tier) => new(true, "ok");
    }
}
