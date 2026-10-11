using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-121: the first completable Chapter 3 job. Accepting a DeadDrop offer
/// freezes bricks, product and drop; closing the drop with the bricks in one
/// slot removes exactly N, pays the fixed reward, and completes the job.
/// </summary>
public sealed class Chapter3DeadDropRuntimeTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void TryCredit_credits_and_verifies_the_delta()
    {
        var world = new FakeWorld { CashBalance = 100f };
        var wallet = new Chapter3Wallet(world);

        var outcome = wallet.TryCredit(15_000);

        Assert.Equal(Chapter3WalletCredit.Credited, outcome);
        Assert.Equal(15_100f, world.CashBalance);
        Assert.Equal(new[] { 15_000f }, world.CashChanges);
    }

    [Fact]
    public void New_drop_close_subscription_replaces_the_old_one()
    {
        var world = new FakeWorld();
        var callbacks = new List<string>();

        Assert.Equal(
            Release1SmallCourtesyWorldReadStatus.Ready,
            world.TrySubscribeDeadDropClosed("drop-a", callbacks.Add, out var first));
        Assert.NotNull(first);
        Assert.Equal(
            Release1SmallCourtesyWorldReadStatus.Ready,
            world.TrySubscribeDeadDropClosed("drop-b", callbacks.Add, out _));

        Assert.Equal(new[] { "drop-b" }, world.DropClosedCallbacks.Keys);
        world.FireDropClosed("drop-a");
        world.FireDropClosed("drop-b");
        Assert.Equal(new[] { "drop-b" }, callbacks);

        first!.Dispose();
        Assert.Equal(new[] { "drop-b" }, world.DropClosedCallbacks.Keys);
        world.FireDropClosed("drop-b");
        Assert.Equal(new[] { "drop-b", "drop-b" }, callbacks);
    }

    [Fact]
    public void TryCredit_rejects_nonpositive_and_overbound_amounts()
    {
        var world = new FakeWorld { CashBalance = 100f };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(0));
        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(-5));
        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(Chapter3PaymentTuning.MaximumPayment + 1));

        Assert.Empty(world.CashChanges);
        Assert.Equal(100f, world.CashBalance);
    }

    [Fact]
    public void TryCredit_is_unavailable_when_the_balance_is_unreadable()
    {
        var world = new FakeWorld
        {
            CashBalance = 100f,
            CashStatus = Release1SmallCourtesyWorldReadStatus.Unavailable
        };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(15_000));
        Assert.Empty(world.CashChanges);
    }

    [Fact]
    public void TryCredit_is_unavailable_when_the_change_is_rejected()
    {
        var world = new FakeWorld
        {
            CashBalance = 100f,
            CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Rejected
        };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(15_000));
        Assert.Equal(100f, world.CashBalance);
    }

    [Fact]
    public void TryCredit_ambiguous_with_a_matching_delta_counts_as_credited()
    {
        var world = new FakeWorld
        {
            CashBalance = 100f,
            CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous,
            ApplyCashChangeOnAmbiguous = true
        };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Credited, wallet.TryCredit(15_000));
        Assert.Equal(15_100f, world.CashBalance);
    }

    [Fact]
    public void TryCredit_ambiguous_without_a_delta_is_unavailable()
    {
        var world = new FakeWorld
        {
            CashBalance = 100f,
            CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous
        };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(15_000));
        Assert.Equal(100f, world.CashBalance);
    }

    [Fact]
    public void TryCredit_post_mismatch_is_unavailable()
    {
        var world = new FakeWorld { CashBalance = 100f, SuppressCashApply = true };
        var wallet = new Chapter3Wallet(world);

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(15_000));
    }

    [Fact]
    public void TryCredit_default_reports_unavailable()
    {
        IChapter3Wallet wallet = new BareWallet();

        Assert.Equal(Chapter3WalletCredit.Unavailable, wallet.TryCredit(15_000));
    }

    [Fact]
    public void Accept_freezes_the_best_product_bricks_and_drop()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.Equal(Chapter3JobType.DeadDrop, job.JobType);
        Assert.Equal("rich", job.Delivery!.ProductId);
        Assert.Equal("Rich", job.Delivery.ProductName);
        Assert.Equal(2, job.Delivery.BrickCount);
        Assert.Equal("drop-a", job.Delivery.DropGuid);
        Assert.Equal("Drop A", job.Delivery.DropName);
        Assert.Equal("brick", job.Delivery.PackagingId);
        Assert.False(job.Delivery.RemovalConfirmed);
        Assert.True(harness.World.DropClosedCallbacks.ContainsKey("drop-a"));
        Assert.Contains(harness.Logs, line => line.Contains("frozen", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_without_a_world_stays_unfrozen()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob!.Delivery);
        Assert.Empty(harness.World.DropClosedCallbacks);
    }

    [Fact]
    public void Accept_with_no_discovered_product_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 50_000d, false));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", true));
        harness.Activate();

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_with_no_empty_drop_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", false));
        harness.Activate();

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
    }

    [Fact]
    public void Accept_with_an_unreadable_world_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.ProductsStatus = Release1SmallCourtesyWorldReadStatus.Pending;
        harness.Activate();

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
    }

    [Fact]
    public void QaAccept_freezes_too()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();

        var result = harness.Service.QaAcceptOpenOffer();

        Assert.True(result.Ok);
        Assert.Equal("rich", harness.Service.State!.JobBoard.ActiveJob!.Delivery!.ProductId);
    }

    [Fact]
    public void QaAccept_refused_when_nothing_can_freeze()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 50_000d, false));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", true));
        harness.Activate();

        var result = harness.Service.QaAcceptOpenOffer();

        Assert.False(result.Ok);
        Assert.Contains("refused", result.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.Service.State!.JobBoard.OpenOffer);
    }

    [Fact]
    public void DropClosed_with_a_full_slot_removes_pays_and_completes()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2), Slot(1, null, null, 0));

        harness.World.FireDropClosed("drop-a");

        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
        Assert.Equal(new Chapter3JobCompletion(1, Chapter3JobType.DeadDrop, 17_000), board.LastCompletedJob);
        Assert.Equal(new[] { ("drop-a", 0, -2) }, harness.World.QuantityChanges);
        Assert.Equal(0, harness.World.SlotQuantity("drop-a", 0));
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
        Assert.Equal(17_000f, harness.World.CashBalance);
        Assert.Empty(harness.World.LockedSlots);
        Assert.Empty(harness.World.DropClosedCallbacks);
        Assert.Contains(harness.Logs, line => line.Contains("removed 2 bricks", StringComparison.Ordinal));
        Assert.Contains(harness.Logs, line => line.Contains("paid $17,000", StringComparison.Ordinal));
        Assert.Contains(harness.Logs, line => line.Contains("completed", StringComparison.Ordinal));
    }

    [Fact]
    public void DropClosed_with_overdelivery_removes_exactly_N()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 5));

        harness.World.FireDropClosed("drop-a");

        Assert.Equal(new[] { ("drop-a", 0, -2) }, harness.World.QuantityChanges);
        Assert.Equal(3, harness.World.SlotQuantity("drop-a", 0));
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Short_slot_holds_once_without_touching_anything()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 1));

        harness.World.FireDropClosed("drop-a");
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.QuantityChanges);
        Assert.Empty(harness.World.CashChanges);
        Assert.Single(harness.Logs, line => line.Contains("holding", StringComparison.Ordinal));
    }

    [Fact]
    public void Spread_slots_hold()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2), Slot(1, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.QuantityChanges);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Wrong_product_holds()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "cheap", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.QuantityChanges);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Empty_drop_holds()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, null, null, 0));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.QuantityChanges);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Removal_rejected_holds_and_retries()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Rejected;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.False(job.Delivery!.RemovalConfirmed);
        Assert.Empty(harness.World.CashChanges);

        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(2, harness.World.QuantityChanges.Count);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
    }

    [Fact]
    public void Removal_confirmed_is_not_removed_twice()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Rejected;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.True(job.Delivery!.RemovalConfirmed);
        Assert.Equal(0, harness.World.SlotQuantity("drop-a", 0));

        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal(2, harness.World.CashChanges.Count);
        Assert.Equal(17_000f, harness.World.CashBalance);
    }

    [Fact]
    public void Consumed_unpaid_job_survives_the_deadline_then_completes()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Delivery!.RemovalConfirmed);

        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.ActiveJob);
        Assert.Empty(harness.Service.State.Ledger.Entries);

        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed("drop-a");

        board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
    }

    [Fact]
    public void Day_tick_completes_a_fulfilled_job_past_its_deadline()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.TickTo(14);

        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
        Assert.Empty(harness.Service.State.Ledger.Entries);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
    }

    [Fact]
    public void Unfrozen_job_ticks_silently()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        harness.TickTo(12);

        Assert.DoesNotContain(harness.Logs, line => line.Contains("deliver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reload_resubscribes_and_delivers()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.Repository.Stored = harness.Service.State;

        var reloaded = new Chapter3RuntimeService(
            new FakeContext(), harness.Repository, new FakeGate { Satisfied = true }, harness.Clock, harness.Logs.Add);
        reloaded.Wallet = new Chapter3Wallet(harness.World);
        reloaded.DeadDropWorld = harness.World;
        reloaded.OnPreLoad();
        reloaded.OnLoadComplete();

        Assert.Equal("rich", reloaded.State!.JobBoard.ActiveJob!.Delivery!.ProductId);
        var attempts = harness.World.SubscriptionAttempts;
        Assert.True(attempts >= 2);

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");

        Assert.Null(reloaded.State!.JobBoard.ActiveJob);
        Assert.Equal(1, reloaded.State.JobBoard.SuccessfulJobs);
    }

    [Fact]
    public void Fail_disposes_the_subscription()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Assert.NotEmpty(harness.World.DropClosedCallbacks);

        Assert.True(harness.Service.QaFailActiveJob().Ok);

        Assert.Empty(harness.World.DropClosedCallbacks);
    }

    [Fact]
    public void Lock_failure_holds_without_removing()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.LockStatus = Release1SmallCourtesyWorldMutationStatus.Rejected;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.QuantityChanges);

        harness.World.LockStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void QaDeliver_stages_and_completes()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        var result = harness.Service.QaDeliverActiveJob();

        Assert.True(result.Ok);
        Assert.Contains("completed", result.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { ("drop-a", 0, "rich", "brick", 2) }, harness.World.Inserts);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(1, harness.Service.State.JobBoard.SuccessfulJobs);
        Assert.Equal(17_000f, harness.World.CashBalance);
    }

    [Fact]
    public void QaDeliver_refused_without_a_frozen_job()
    {
        var empty = new Harness();
        SeedFreezableWorld(empty.World);
        empty.Activate();

        Assert.False(empty.Service.QaDeliverActiveJob().Ok);

        var unfrozen = new Harness();
        unfrozen.Service.DeadDropWorld = null;
        unfrozen.Activate();
        Assert.True(unfrozen.Service.TryAcceptOffer().Applied);

        var result = unfrozen.Service.QaDeliverActiveJob();

        Assert.False(result.Ok);
        Assert.Contains("no frozen delivery", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QaDeliver_refused_without_a_world()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.Service.DeadDropWorld = null;

        var result = harness.Service.QaDeliverActiveJob();

        Assert.False(result.Ok);
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void QaDeliver_reports_a_hold()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(1, "rich", "brick", 2));

        var result = harness.Service.QaDeliverActiveJob();

        Assert.True(result.Ok);
        Assert.Contains("holding", result.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void QaDeliver_skips_staging_once_removal_is_confirmed()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Rejected;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Delivery!.RemovalConfirmed);
        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        var result = harness.Service.QaDeliverActiveJob();

        Assert.True(result.Ok);
        Assert.Empty(harness.World.Inserts);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Unfrozen_job_logs_once_that_no_quest_is_projected()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        harness.Service.GetPresentationPlan(false);
        harness.Service.GetPresentationPlan(false);

        Assert.Single(harness.Logs, line => line.Contains("no quest", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambiguous_exact_delivery_confirms_on_reread_and_pays()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.World.ApplyQuantityChangeOnAmbiguous = true;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.False(harness.Service.State.JobBoard.ActiveJob!.Delivery!.RemovalConfirmed);

        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
        Assert.Contains(harness.Logs, line => line.Contains("confirmed on re-read", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambiguous_removal_retried_when_the_slot_still_reads_pre()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);

        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(2, harness.World.QuantityChanges.Count);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
    }

    [Fact]
    public void Ambiguous_removal_diverged_holds_once()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.World.ApplyQuantityChangeOnAmbiguous = true;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 1));
        harness.World.FireDropClosed("drop-a");
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
        Assert.Single(harness.Logs, line => line.Contains("pending removal", StringComparison.Ordinal));
    }

    [Fact]
    public void Throwing_removal_confirms_on_reread()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.ThrowOnQuantityChange = true;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);

        harness.World.ThrowOnQuantityChange = false;
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
    }

    [Fact]
    public void Post_read_failure_after_succeeded_removal_confirms_on_reread()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.FailSlotReads.Add(2);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));

        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.False(harness.Service.State.JobBoard.ActiveJob!.Delivery!.RemovalConfirmed);

        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal(new[] { 17_000f }, harness.World.CashChanges);
    }

    [Fact]
    public void Stale_pending_from_a_failed_job_is_dropped()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.World.ApplyQuantityChangeOnAmbiguous = true;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        harness.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        // Offer 2 is a package recovery now, which this world cannot freeze;
        // the stale-pending scenario needs a dead drop, so decline it and take
        // offer 3.
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(15);
        harness.TickTo(16);
        harness.TickTo(17);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Assert.Equal(Chapter3JobType.DeadDrop, harness.Service.State!.JobBoard.ActiveJob!.JobType);
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("confirmed on re-read", StringComparison.Ordinal));

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 2));
        harness.World.FireDropClosed("drop-a");

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(1, harness.Service.State.JobBoard.SuccessfulJobs);
    }

    [Fact]
    public void Frozen_job_logs_no_quest_warning()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        var plan = harness.Service.GetPresentationPlan(false);

        Assert.NotNull(plan.Quest);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("no quest", StringComparison.Ordinal));
    }

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    private static void SeedFreezableWorld(FakeWorld world)
    {
        world.SeedProducts(
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("richer", "Richer", 50_000d, false));
        world.SeedDrops(
            Drop("drop-full", "Drop Full", false),
            Drop("drop-a", "Drop A", true));
    }

    private static Release1SmallCourtesyDropCandidate Drop(string guid, string name, bool isEmpty) =>
        new(guid, name, "description", 1d, 2d, 3d, isEmpty);

    private sealed class BareWallet : IChapter3Wallet
    {
        public Chapter3WalletDebit TryDebit(int amount) => Chapter3WalletDebit.Short;
    }

    private sealed class Harness
    {
        public FakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = new Chapter3Wallet(World);
            Service.DeadDropWorld = World;
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

    internal sealed class FakeWorld : IChapter3World
    {
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = CashBalance;
            return CashStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            CashChanges.Add(amount);
            if (CashChangeStatus == Release1SmallCourtesyWorldMutationStatus.Succeeded && !SuppressCashApply)
                CashBalance += amount;
            else if (CashChangeStatus == Release1SmallCourtesyWorldMutationStatus.Ambiguous && ApplyCashChangeOnAmbiguous)
                CashBalance += amount;
            return CashChangeStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount, float maximumDebit)
        {
            CashChanges.Add(amount);
            if (DebitCashBalanceStatus == Release1SmallCourtesyWorldMutationStatus.Succeeded && !SuppressCashApply)
                CashBalance += amount;
            else if (DebitCashBalanceStatus == Release1SmallCourtesyWorldMutationStatus.Ambiguous &&
                ApplyCashChangeOnAmbiguous)
                CashBalance += amount;
            return DebitCashBalanceStatus;
        }

        private FakeSubscription? _closeEntry;

        public Release1SmallCourtesyWorldReadStatus ProductsStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public List<Release1SmallCourtesyProductCandidate> Products { get; } = new();
        public Release1SmallCourtesyWorldReadStatus CanonicalMinutesStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public double CanonicalMinutes { get; set; }
        public Release1SmallCourtesyWorldReadStatus DropsStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public List<Release1SmallCourtesyDropCandidate> Drops { get; } = new();
        public Release1SmallCourtesyWorldReadStatus PackagingStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public Release1SmallCourtesyPackagingCandidate Packaging { get; set; } =
            new("brick", "Brick");
        public Release1SmallCourtesyWorldReadStatus SlotsStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public Dictionary<string, List<Release1SmallCourtesySlotSnapshot>> Slots { get; } =
            new(StringComparer.Ordinal);
        public Release1SmallCourtesyWorldReadStatus CashStatus { get; set; } =
            Release1SmallCourtesyWorldReadStatus.Ready;
        public float CashBalance { get; set; }
        public Release1SmallCourtesyWorldMutationStatus QuantityStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public List<(string DropGuid, int SlotIndex, int Amount)> QuantityChanges { get; } = new();
        public bool ApplyQuantityChangeOnAmbiguous { get; set; }
        public bool ThrowOnQuantityChange { get; set; }
        public HashSet<int> FailSlotReads { get; } = new();
        public int SlotReadCount { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus CashChangeStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public bool ApplyCashChangeOnAmbiguous { get; set; }
        public bool SuppressCashApply { get; set; }
        public List<float> CashChanges { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus DebitCashBalanceStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public Release1SmallCourtesyWorldMutationStatus InsertStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public List<(string DropGuid, int SlotIndex, string ProductId, string PackagingId, int Quantity)> Inserts { get; } =
            new();
        public Dictionary<string, Action<string>> DropClosedCallbacks { get; } =
            new(StringComparer.Ordinal);
        public int SubscriptionAttempts { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus LockStatus { get; set; } =
            Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public HashSet<(string DropGuid, int SlotIndex)> LockedSlots { get; } = new();

        public void SeedProducts(params Release1SmallCourtesyProductCandidate[] products)
        {
            Products.Clear();
            Products.AddRange(products);
        }

        public void SeedDrops(params Release1SmallCourtesyDropCandidate[] drops)
        {
            Drops.Clear();
            Drops.AddRange(drops);
        }

        public void SeedSlots(string dropGuid, params Release1SmallCourtesySlotSnapshot[] slots) =>
            Slots[dropGuid] = slots.ToList();

        public void FireDropClosed(string dropGuid)
        {
            if (DropClosedCallbacks.TryGetValue(dropGuid, out var callback))
                callback(dropGuid);
        }

        public int SlotQuantity(string dropGuid, int slotIndex) =>
            Slots.TryGetValue(dropGuid, out var slots)
                ? slots.FirstOrDefault(slot => slot.SlotIndex == slotIndex)?.Quantity ?? 0
                : 0;

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            context = new(Guid.Parse("77777777-7777-7777-7777-777777777777"), 1, PlayerId, System.IO.Path.GetTempPath());
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = CanonicalMinutes;
            return CanonicalMinutesStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(
            out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = Products.ToArray();
            return ProductsStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(
            out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = Drops.ToArray();
            return DropsStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = Packaging;
            return PackagingStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            SlotReadCount++;
            if (FailSlotReads.Contains(SlotReadCount))
            {
                slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            }
            slots = Slots.TryGetValue(deadDropGuid, out var found)
                ? found.ToArray()
                : Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return SlotsStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
        {
            if (LockStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return LockStatus;
            if (locked)
                LockedSlots.Add((deadDropGuid, slotIndex));
            else
                LockedSlots.Remove((deadDropGuid, slotIndex));
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            QuantityChanges.Add((deadDropGuid, slotIndex, amount));
            if (ThrowOnQuantityChange)
                throw new InvalidOperationException("Fake slot quantity change fault.");
            if (QuantityStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                if (QuantityStatus == Release1SmallCourtesyWorldMutationStatus.Ambiguous && ApplyQuantityChangeOnAmbiguous)
                    ApplySlotDelta(deadDropGuid, slotIndex, amount);
                return QuantityStatus;
            }
            ApplySlotDelta(deadDropGuid, slotIndex, amount);
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        private void ApplySlotDelta(string deadDropGuid, int slotIndex, int amount)
        {
            if (Slots.TryGetValue(deadDropGuid, out var slots))
            {
                var index = slots.FindIndex(slot => slot.SlotIndex == slotIndex);
                if (index >= 0)
                    slots[index] = slots[index] with { Quantity = slots[index].Quantity + amount };
            }
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            reason = string.Empty;
            Inserts.Add((deadDropGuid, slotIndex, productId, packagingId, quantity));
            if (InsertStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return InsertStatus;
            if (!Slots.TryGetValue(deadDropGuid, out var slots))
            {
                slots = new List<Release1SmallCourtesySlotSnapshot>();
                Slots[deadDropGuid] = slots;
            }
            var index = slots.FindIndex(slot => slot.SlotIndex == slotIndex);
            var snapshot = new Release1SmallCourtesySlotSnapshot(slotIndex, productId, packagingId, quantity, true, 0f);
            if (index >= 0)
                slots[index] = snapshot;
            else
                slots.Add(snapshot);
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            subscription = null;
            SubscriptionAttempts++;

            if (_closeEntry is { IsActive: true } current)
            {
                if (string.Equals(current.DeadDropGuid, deadDropGuid, StringComparison.Ordinal) &&
                    current.Callback == callback)
                {
                    subscription = current;
                    return Release1SmallCourtesyWorldReadStatus.Ready;
                }

                current.Dispose();
            }

            var created = new FakeSubscription(this, deadDropGuid, callback);
            _closeEntry = created;
            DropClosedCallbacks[deadDropGuid] = callback;
            subscription = created;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        private void RemoveSubscription(FakeSubscription subscription)
        {
            if (!ReferenceEquals(_closeEntry, subscription))
                return;

            _closeEntry = null;
            if (DropClosedCallbacks.TryGetValue(subscription.DeadDropGuid, out var callback) &&
                callback == subscription.Callback)
                DropClosedCallbacks.Remove(subscription.DeadDropGuid);
        }

        private sealed class FakeSubscription : IRelease1SmallCourtesyDropSubscription
        {
            private readonly FakeWorld _world;
            public bool IsActive { get; private set; } = true;

            public FakeSubscription(FakeWorld world, string deadDropGuid, Action<string> callback)
            {
                _world = world;
                DeadDropGuid = deadDropGuid;
                Callback = callback;
            }

            public string DeadDropGuid { get; }
            public Action<string> Callback { get; }

            public void Dispose()
            {
                if (!IsActive)
                    return;

                IsActive = false;
                _world.RemoveSubscription(this);
            }
        }
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
