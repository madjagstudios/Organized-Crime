using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-125: Nell's warehouse offer opens once earned, reminds weekly, and buys
/// through the wallet plus the warehouse acquisition seam.
/// </summary>
public sealed class Chapter3WarehousePurchaseRuntimeTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Offer_opens_at_the_first_eligible_tick()
    {
        var harness = new Harness();
        harness.Repository.Stored = EligibleStoredState();
        harness.Clock.ElapsedDays = 29;
        harness.Activate();

        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.Equal(18, offer.OpenedDay);
        Assert.Equal(0, offer.LastReminderWeek);
        Assert.False(offer.Purchased);
        Assert.Contains(harness.Logs, line => line.Contains("warehouse offer opened day 18", StringComparison.Ordinal));
    }

    [Fact]
    public void Offer_does_not_open_when_owned_or_ineligible_or_seamless()
    {
        var owned = new Harness();
        owned.Repository.Stored = EligibleStoredState();
        owned.Clock.ElapsedDays = 29;
        owned.Acquisition.Owned = true;
        owned.Activate();
        Assert.Null(owned.Service.State!.WarehouseOffer);

        var few = new Harness();
        var state = EligibleStoredState() with
        {
            JobBoard = EligibleStoredState().JobBoard with { SuccessfulJobs = 4 }
        };
        few.Repository.Stored = state;
        few.Clock.ElapsedDays = 29;
        few.Activate();
        Assert.Null(few.Service.State!.WarehouseOffer);

        var seamless = new Harness();
        seamless.Repository.Stored = EligibleStoredState();
        seamless.Clock.ElapsedDays = 29;
        seamless.Service.WarehouseAcquisition = null;
        seamless.Activate();
        Assert.Null(seamless.Service.State!.WarehouseOffer);
    }

    [Fact]
    public void Later_make_good_does_not_close_the_offer()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        Assert.False(harness.Service.State!.IsWarehouseOfferEligible());

        harness.TickTo(12);

        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.False(offer.Purchased);
        Assert.Equal(0, offer.OpenedDay);
    }

    [Fact]
    public void Reminder_week_advances_every_seven_days()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);

        harness.TickTo(17);
        Assert.Equal(0, harness.Service.State!.WarehouseOffer!.LastReminderWeek);
        harness.TickTo(18);
        Assert.Equal(1, harness.Service.State!.WarehouseOffer!.LastReminderWeek);
        harness.TickTo(25);
        Assert.Equal(2, harness.Service.State!.WarehouseOffer!.LastReminderWeek);
        Assert.Contains(harness.Logs, line => line.Contains("warehouse reminder week 2", StringComparison.Ordinal));
    }

    [Fact]
    public void QaOfferWarehouse_opens_ignoring_eligibility_and_reports_state()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.False(harness.Service.State!.IsWarehouseOfferEligible());

        var opened = harness.Service.QaOfferWarehouse();

        Assert.True(opened.Ok, opened.Message);
        Assert.Equal(0, harness.Service.State!.WarehouseOffer!.OpenedDay);

        var again = harness.Service.QaOfferWarehouse();
        Assert.True(again.Ok, again.Message);
        Assert.Contains("already open since day 0", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QaOfferWarehouse_refuses_when_owned_and_reports_when_purchased()
    {
        var owned = new Harness();
        owned.Activate();
        owned.Acquisition.Owned = true;

        var refused = owned.Service.QaOfferWarehouse();

        Assert.False(refused.Ok);
        Assert.Contains("already owned", refused.Message, StringComparison.Ordinal);
        Assert.Null(owned.Service.State!.WarehouseOffer);

        var bought = new Harness();
        bought.World.CashBalance = 400_000f;
        bought.Activate();
        Assert.True(bought.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, bought.Service.TryBuyWarehouse().Outcome);

        var again = bought.Service.QaOfferWarehouse();
        Assert.True(again.Ok, again.Message);
        Assert.Contains("already purchased", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Buy_short_sends_the_short_line_and_moves_nothing()
    {
        var harness = new Harness();
        harness.World.CashBalance = 10_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(Chapter3MoneyOutcome.Short, result.Outcome);
        Assert.Equal(1, harness.Service.State!.PaymentShortfallCount);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(10_000f, harness.World.CashBalance);
        Assert.False(harness.Service.State!.WarehouseOffer!.Purchased);
        Assert.Empty(harness.Acquisition.Calls);
    }

    [Fact]
    public void Buy_success_debits_and_records_a_pending_build_without_spawning()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(new Chapter3MoneyResult(Chapter3MoneyOutcome.Applied, 300_000), result);
        Assert.Equal(new[] { -300_000f }, harness.World.CashChanges);
        Assert.Equal(100_000f, harness.World.CashBalance);
        Assert.Empty(harness.Acquisition.Calls);
        Assert.False(harness.Acquisition.Owned);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.True(offer.Purchased);
        Assert.Equal(0, offer.PurchasedDay);
        Assert.True(harness.Service.HasPendingWarehouseBuild);
        Assert.Contains(harness.Logs, line => line.Contains("warehouse purchased day 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Sleep_poll_builds_once_acquire_then_quiet()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.True(harness.Acquisition.Owned);
        Assert.True(harness.Service.State!.WarehouseOffer!.Purchased);
        Assert.False(harness.Service.HasPendingWarehouseBuild);
        Assert.Single(harness.World.CashChanges);

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.Single(harness.World.CashChanges);
    }

    [Fact]
    public void Sleep_poll_builds_despite_a_dead_load_window()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Service.NotifySleepStarted();

        harness.Service.PollPendingWarehouseBuild(() => false);
        Assert.Empty(harness.Acquisition.Calls);

        harness.Service.PollPendingWarehouseBuild(() => true);
        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
    }

    [Fact]
    public void Load_with_nothing_pending_then_buy_never_builds_mid_play()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);

        harness.Service.PollPendingWarehouseBuild(() => false);
        harness.Service.PollPendingWarehouseBuild(() => false);

        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.True(harness.Acquisition.Owned);
        Assert.Single(harness.World.CashChanges);
    }

    [Fact]
    public void Load_poll_never_fires_mid_play()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Service.NotifySleepStarted();

        harness.Service.PollPendingWarehouseBuild(() => false);

        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);
    }

    [Fact]
    public void Stored_purchased_but_unowned_becomes_a_pending_build()
    {
        // The v9 side of this is Schema9_file_loads_with_zero_fell_throughs:
        // a v9 save that says purchased deserializes to this state.
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        var stored = EligibleStoredState() with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(7, Purchased: true, PurchasedDay: 14)
        };
        harness.Repository.Stored = stored;
        harness.Clock.ElapsedDays = 29;
        harness.Acquisition.Owned = false;
        harness.Activate();

        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => false);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Purchased_warehouse_waits_while_ownership_is_unsettled()
    {
        // RecoveryRequired on the capture side (or an in-flight replay)
        // surfaces here as unsettled ownership: the purchase waits instead
        // of building over an unknown warehouse, and the load window never
        // arms for it, so the build still waits for sleep cover.
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        var stored = EligibleStoredState() with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(7, Purchased: true, PurchasedDay: 14)
        };
        harness.Repository.Stored = stored;
        harness.Clock.ElapsedDays = 29;
        harness.Acquisition.Owned = false;
        harness.Acquisition.Settled = false;
        harness.Activate();

        Assert.False(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => false);
        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Empty(harness.Acquisition.Calls);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.True(offer.Purchased);
        Assert.Equal(0, offer.FellThroughCount);
        Assert.Empty(harness.World.CashChanges);
        Assert.Single(harness.Logs, line => line.Contains("build waits: warehouse ownership is unsettled", StringComparison.Ordinal));

        harness.Acquisition.Settled = true;

        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => false);
        Assert.Empty(harness.Acquisition.Calls);

        harness.Service.PollPendingWarehouseBuild(() => true);
        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
    }

    [Fact]
    public void Direct_build_attempt_waits_while_ownership_is_unsettled()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Acquisition.Settled = false;

        Assert.False(harness.Service.TryBuildPendingWarehouse());

        Assert.Empty(harness.Acquisition.Calls);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.True(offer.Purchased);
        Assert.Equal(0, offer.FellThroughCount);
        Assert.Single(harness.World.CashChanges);
    }

    [Fact]
    public void Load_with_pending_build_but_unacquirable_disarms_and_waits_for_sleep()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        var stored = EligibleStoredState() with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(7, Purchased: true, PurchasedDay: 14)
        };
        harness.Repository.Stored = stored;
        harness.Clock.ElapsedDays = 29;
        harness.Acquisition.Owned = false;
        harness.Acquisition.CanAcquireValue = false;
        harness.Activate();

        harness.Service.PollPendingWarehouseBuild(() => false);

        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);
        Assert.Contains(
            harness.Logs,
            line => line.Contains("load window missed the pending warehouse build", StringComparison.Ordinal));

        // The window is spent: a later acquirable poll with no sleep builds nothing.
        harness.Acquisition.CanAcquireValue = true;
        harness.Service.PollPendingWarehouseBuild(() => false);

        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Build_failure_refunds_reopens_and_counts_a_fell_through()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Acquisition.AcquireResult = false;

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.Equal(new[] { -300_000f, 300_000f }, harness.World.CashChanges);
        Assert.Equal(400_000f, harness.World.CashBalance);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.False(offer.Purchased);
        Assert.Null(offer.PurchasedDay);
        Assert.Equal(1, offer.FellThroughCount);
        Assert.False(harness.Service.HasPendingWarehouseBuild);
    }

    [Fact]
    public void Build_failure_when_unacquirable_refunds_without_touching_the_world()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Acquisition.CanAcquireValue = false;

        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Empty(harness.Acquisition.Calls);
        Assert.Equal(new[] { -300_000f, 300_000f }, harness.World.CashChanges);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.False(offer.Purchased);
        Assert.Equal(1, offer.FellThroughCount);
    }

    [Fact]
    public void Build_failure_twice_counts_twice()
    {
        var harness = new Harness();
        harness.World.CashBalance = 700_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Acquisition.CanAcquireValue = false;
        harness.Service.PollPendingWarehouseBuild(() => true);
        Assert.Equal(1, harness.Service.State!.WarehouseOffer!.FellThroughCount);

        harness.Acquisition.CanAcquireValue = true;
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);
        harness.Acquisition.CanAcquireValue = false;
        harness.Service.PollPendingWarehouseBuild(() => true);

        Assert.Equal(2, harness.Service.State!.WarehouseOffer!.FellThroughCount);
        Assert.Equal(new[] { -300_000f, 300_000f, -300_000f, 300_000f }, harness.World.CashChanges);
        Assert.Equal(700_000f, harness.World.CashBalance);
    }

    [Fact]
    public void Build_records_ownership_without_placing_an_employee_home()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);

        var built = harness.Service.TryBuildPendingWarehouse();

        Assert.True(built);
        Assert.True(harness.Acquisition.Owned);
        Assert.True(harness.Service.State!.WarehouseOffer!.Purchased);
        Assert.Equal(new[] { "acquire" }, harness.Acquisition.Calls);
        Assert.Single(harness.World.CashChanges);
    }

    [Fact]
    public void Build_without_a_pending_build_is_silent()
    {
        var unpurchased = new Harness();
        unpurchased.Activate();
        Assert.True(unpurchased.Service.QaOfferWarehouse().Ok);
        Assert.False(unpurchased.Service.TryBuildPendingWarehouse());
        Assert.Empty(unpurchased.Acquisition.Calls);

        var owned = new Harness();
        owned.World.CashBalance = 400_000f;
        owned.Activate();
        Assert.True(owned.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, owned.Service.TryBuyWarehouse().Outcome);
        owned.Acquisition.Owned = true;
        Assert.False(owned.Service.TryBuildPendingWarehouse());
        Assert.Empty(owned.Acquisition.Calls);

        var seamless = new Harness();
        seamless.World.CashBalance = 400_000f;
        seamless.Activate();
        Assert.True(seamless.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, seamless.Service.TryBuyWarehouse().Outcome);
        seamless.Service.WarehouseAcquisition = null;
        Assert.False(seamless.Service.TryBuildPendingWarehouse());
        Assert.False(seamless.Service.HasPendingWarehouseBuild);
    }

    [Fact]
    public void Buy_ambiguous_debit_records_nothing()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.World.DebitCashBalanceStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Empty(harness.Acquisition.Calls);
        var offer = harness.Service.State!.WarehouseOffer!;
        Assert.False(offer.Purchased);
        Assert.Equal(0, offer.NotReadyCount);
        Assert.Equal(0, harness.Service.State!.PaymentShortfallCount);
    }

    [Fact]
    public void Buy_not_ready_moves_no_money()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Acquisition.CanAcquireValue = false;

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(1, harness.Service.State!.WarehouseOffer!.NotReadyCount);
        Assert.False(harness.Service.State!.WarehouseOffer!.Purchased);
    }

    [Fact]
    public void Buy_without_the_seam_is_not_ready()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Service.WarehouseAcquisition = null;

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(1, harness.Service.State!.WarehouseOffer!.NotReadyCount);
    }

    [Fact]
    public void Buy_adopts_dev_owned_without_charging()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Acquisition.Owned = true;

        var result = harness.Service.TryBuyWarehouse();

        Assert.Equal(new Chapter3MoneyResult(Chapter3MoneyOutcome.Applied, 0), result);
        Assert.Empty(harness.World.CashChanges);
        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.State!.WarehouseOffer!.Purchased);
    }

    [Fact]
    public void Buy_after_purchase_or_with_debt_is_not_payable()
    {
        var harness = new Harness();
        harness.World.CashBalance = 800_000f;
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, harness.Service.TryBuyWarehouse().Outcome);
        Assert.Single(harness.World.CashChanges);

        var owing = new Harness();
        owing.World.CashBalance = 800_000f;
        owing.Activate();
        Assert.True(owing.Service.QaOfferWarehouse().Ok);
        owing.Service.DeadDropWorld = null;
        Assert.True(owing.Service.TryAcceptOffer().Applied);
        Assert.True(owing.Service.QaFailActiveJob().Ok);

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, owing.Service.TryBuyWarehouse().Outcome);
        Assert.Empty(owing.World.CashChanges);
        Assert.False(owing.Service.State!.WarehouseOffer!.Purchased);
    }

    [Fact]
    public void Successes_count_after_purchase_only()
    {
        var harness = new Harness();
        harness.World.CashBalance = 400_000f;
        SeedFreezableWorld(harness.World);
        harness.Activate();

        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Deliver(harness);
        Assert.Equal(1, harness.Service.State!.JobBoard.SuccessfulJobs);
        Assert.Equal(0, harness.Service.State!.JobBoard.SuccessfulJobsSinceWarehousePurchase);

        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        Assert.Equal(Chapter3MoneyOutcome.Applied, harness.Service.TryBuyWarehouse().Outcome);

        // Offer 2 is a recovery; decline through to the next DeadDrop.
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.Equal(Chapter3JobType.PackageRecovery, harness.Service.State!.JobBoard.OpenOffer!.JobType);
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(15);
        harness.TickTo(16);
        harness.TickTo(17);
        Assert.Equal(Chapter3JobType.DeadDrop, harness.Service.State!.JobBoard.OpenOffer!.JobType);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Deliver(harness);
        Assert.Equal(2, harness.Service.State!.JobBoard.SuccessfulJobs);
        Assert.Equal(1, harness.Service.State!.JobBoard.SuccessfulJobsSinceWarehousePurchase);
    }

    private static void Deliver(Harness harness)
    {
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        harness.World.SeedSlots(
            job.Delivery!.DropGuid, Slot(0, job.Delivery.ProductId, "brick", job.Delivery.BrickCount));
        harness.World.FireDropClosed(job.Delivery.DropGuid);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
    }

    private static Chapter3State EligibleStoredState()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        for (var elapsed = 11; elapsed <= 28; elapsed++)
            state = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true).State;
        return state with { JobBoard = state.JobBoard with { SuccessfulJobs = 5 } };
    }

    private static void SeedFreezableWorld(DeadDropFakeWorld world)
    {
        world.SeedProducts(
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        world.SeedDrops(new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "description", 1d, 2d, 3d, true));
    }

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    private sealed class Harness
    {
        public DeadDropFakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public FakeAcquisition Acquisition { get; } = new();
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = new Chapter3Wallet(World);
            Service.DeadDropWorld = World;
            Service.WarehouseAcquisition = Acquisition;
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }

        public void TickTo(int elapsedDays)
        {
            Clock.ElapsedDays = elapsedDays;
            Service.OnDayPassed();
        }
    }

    private sealed class FakeAcquisition : IChapter3WarehouseAcquisition
    {
        public bool Owned { get; set; }
        public bool Settled { get; set; } = true;
        public bool CanAcquireValue { get; set; } = true;
        public bool AcquireResult { get; set; } = true;
        public List<string> Calls { get; } = new();

        public bool IsOwned() => Owned;
        public bool IsOwnershipSettled() => Settled;
        public bool CanAcquire() => CanAcquireValue;

        public bool TryAcquire()
        {
            Calls.Add("acquire");
            if (AcquireResult)
                Owned = true;
            return AcquireResult;
        }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("77777777-7777-7777-7777-777777777777"), 1, PlayerId, System.IO.Path.GetTempPath());

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
