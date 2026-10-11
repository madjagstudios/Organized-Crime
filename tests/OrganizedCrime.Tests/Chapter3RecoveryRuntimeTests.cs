using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using RecoveryFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-123: the second Chapter 3 job type. Accepting a PackageRecovery offer
/// freezes product, brick count and the pickup/handoff pair, then stages N
/// bricks into the pickup drop. The player collects them (custody) and
/// delivers them to the handoff drop, which removes exactly N, pays the fixed
/// reward, and completes the job.
/// </summary>
public sealed class Chapter3RecoveryRuntimeTests
{
    [Fact]
    public void Accept_freezes_the_pair_and_stages_the_bricks()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.Equal(Chapter3JobType.PackageRecovery, job.JobType);
        Assert.NotNull(job.Recovery);
        var recovery = job.Recovery!;
        Assert.Equal("rich", recovery.ProductId);
        Assert.Equal("Rich", recovery.ProductName);
        Assert.Equal("brick", recovery.PackagingId);
        Assert.Equal(Chapter3JobTuning.BricksForReward(job.Reward), recovery.BrickCount);
        Assert.NotEqual(recovery.Pickup.Guid, recovery.Handoff.Guid);
        Assert.True(recovery.Staged);
        Assert.Equal(0, recovery.StagedSlotIndex);
        Assert.False(recovery.Custody);
        var insert = Assert.Single(harness.World.Inserts);
        Assert.Equal(recovery.Pickup.Guid, insert.DropGuid);
        Assert.Equal(0, insert.SlotIndex);
        Assert.Equal("rich", insert.ProductId);
        Assert.Equal("brick", insert.PackagingId);
        Assert.Equal(recovery.BrickCount, insert.Quantity);
        Assert.Equal(new[] { recovery.Pickup.Guid }, harness.World.DropClosedCallbacks.Keys);
        Assert.Contains(harness.Logs, line => line.Contains("staged", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambiguous_insert_that_landed_is_adopted_without_reinserting()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.World.InsertStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        Assert.False(harness.Service.TryAcceptOffer().Applied);
        Assert.Single(harness.World.Inserts);

        // The ambiguous insert landed despite its status; the world now holds it.
        var pickup = harness.World.Inserts[0].DropGuid;
        var bricks = Chapter3JobTuning.BricksForReward(harness.Service.State!.JobBoard.OpenOffer!.Reward);
        harness.World.SeedSlots(pickup, Slot(0, "rich", "brick", bricks));
        harness.World.InsertStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Staged);
        Assert.Single(harness.World.Inserts);
    }

    [Fact]
    public void Accept_without_a_world_stays_unfrozen()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob!.Recovery);
        Assert.Empty(harness.World.DropClosedCallbacks);
    }

    [Fact]
    public void Accept_with_no_discovered_product_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 50_000d, false));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", true), Drop("drop-b", "Drop B", true));
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_with_no_empty_pair_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", true));
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("pair", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_with_an_unreadable_world_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.World.ProductsStatus = Release1SmallCourtesyWorldReadStatus.Pending;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
    }

    [Fact]
    public void Accept_with_a_changed_pickup_slot_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        // Another crew filled both candidate slots between the freeze read and
        // the staging insert; whichever pair the freeze picks, slot 0 is taken.
        harness.World.SeedSlots("drop-a", Slot(0, "cheap", "brick", 1));
        harness.World.SeedSlots("drop-b", Slot(0, "cheap", "brick", 1));

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Empty(harness.World.Inserts);
    }

    [Fact]
    public void Pickup_close_marks_custody_only_once_the_staged_bricks_are_gone()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;

        harness.World.FireDropClosed(recovery.Pickup.Guid);
        Assert.False(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);

        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);

        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);
        Assert.Equal(new[] { recovery.Handoff.Guid }, harness.World.DropClosedCallbacks.Keys);
        Assert.Contains(harness.Logs, line => line.Contains("custody", StringComparison.Ordinal));
    }

    [Fact]
    public void Handoff_close_without_custody_changes_nothing()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;

        harness.World.SeedSlots(recovery.Handoff.Guid, Slot(0, "rich", "brick", recovery.BrickCount));
        harness.World.FireDropClosed(recovery.Handoff.Guid);

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.False(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);
        Assert.Empty(harness.World.QuantityChanges);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(recovery.BrickCount, harness.World.SlotQuantity(recovery.Handoff.Guid, 0));
    }

    [Fact]
    public void Handoff_close_with_custody_removes_pays_and_completes()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);

        harness.World.SeedSlots(recovery.Handoff.Guid, Slot(0, "rich", "brick", recovery.BrickCount));
        harness.World.FireDropClosed(recovery.Handoff.Guid);

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(1, harness.Service.State.JobBoard.SuccessfulJobs);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal((recovery.Handoff.Guid, 0, -recovery.BrickCount), harness.World.QuantityChanges[0]);
        Assert.Equal(new[] { (float)job.Reward }, harness.World.CashChanges);
        Assert.Equal(0, harness.World.SlotQuantity(recovery.Handoff.Guid, 0));
    }

    [Fact]
    public void Day_tick_completes_a_fulfilled_handoff_past_its_deadline()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 0));
        harness.World.SeedSlots(recovery.Handoff.Guid, Slot(0, "rich", "brick", recovery.BrickCount));

        harness.TickTo(17);

        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
        Assert.Empty(harness.Service.State.Ledger.Entries);
        Assert.Equal(new[] { (float)job.Reward }, harness.World.CashChanges);
    }

    [Fact]
    public void Failed_job_cleans_staged_bricks_from_the_pickup_drop()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;
        Assert.Equal(recovery.BrickCount, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));

        harness.TickTo(17);

        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Single(harness.Service.State.Ledger.Entries);
        Assert.Equal(0, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
        Assert.Contains(harness.World.QuantityChanges, change =>
            change.DropGuid == recovery.Pickup.Guid && change.SlotIndex == 0 && change.Amount == -recovery.BrickCount);
        Assert.Contains(harness.Logs, line => line.Contains("cleaned", StringComparison.Ordinal));
    }

    [Fact]
    public void QaFail_cleans_staged_bricks_from_the_pickup_drop()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;

        Assert.True(harness.Service.QaFailActiveJob().Ok);

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.Service.State.Ledger.Entries);
        Assert.Equal(0, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
        Assert.Contains(harness.Logs, line => line.Contains("cleaned", StringComparison.Ordinal));
    }

    [Fact]
    public void Cleanup_leaves_foreign_bricks_alone()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "cheap", "brick", 5));

        Assert.True(harness.Service.QaFailActiveJob().Ok);

        Assert.Equal(5, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
        Assert.Empty(harness.World.QuantityChanges);
    }

    [Fact]
    public void Cleanup_removes_partial_remnants_up_to_the_frozen_count()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;
        Assert.True(recovery.BrickCount > 1);
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 1));

        Assert.True(harness.Service.QaFailActiveJob().Ok);

        Assert.Equal(0, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
        Assert.Contains(harness.World.QuantityChanges, change =>
            change.DropGuid == recovery.Pickup.Guid && change.SlotIndex == 0 && change.Amount == -1);
    }

    [Fact]
    public void Cleanup_removes_remnants_regardless_of_custody()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var recovery = harness.Service.State!.JobBoard.ActiveJob!.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 1));

        Assert.True(harness.Service.QaFailActiveJob().Ok);

        Assert.Equal(0, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
    }

    [Fact]
    public void Unfrozen_failed_job_cleans_nothing()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        harness.TickTo(17);

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.Service.State.Ledger.Entries);
        Assert.Empty(harness.World.QuantityChanges);
    }

    [Fact]
    public void Consumed_unpaid_handoff_survives_the_deadline_then_completes()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(0, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);
        harness.World.SeedSlots(recovery.Handoff.Guid, Slot(0, "rich", "brick", recovery.BrickCount));
        harness.World.FireDropClosed(recovery.Handoff.Guid);
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.RemovalConfirmed);

        harness.TickTo(17);

        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.ActiveJob);
        Assert.Empty(harness.Service.State.Ledger.Entries);

        harness.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        harness.World.FireDropClosed(recovery.Handoff.Guid);

        board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
        Assert.Equal(job.Reward, harness.World.CashBalance);
        Assert.All(harness.World.CashChanges, attempt => Assert.Equal((float)job.Reward, attempt));
        Assert.Single(harness.World.QuantityChanges);
    }

    [Fact]
    public void Reloaded_unstaged_job_stages_on_the_next_pass()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;
        harness.World.SeedSlots(recovery.Pickup.Guid);
        harness.Repository.Stored = harness.Service.State! with
        {
            JobBoard = harness.Service.State.JobBoard with
            {
                ActiveJob = job with { Recovery = recovery with { Staged = false, StagedSlotIndex = -1 } }
            }
        };

        var reloaded = new Chapter3RuntimeService(
            new FakeContext(), harness.Repository, new FakeGate { Satisfied = true }, harness.Clock, harness.Logs.Add);
        reloaded.Wallet = new Chapter3Wallet(harness.World);
        reloaded.DeadDropWorld = harness.World;
        reloaded.OnPreLoad();
        reloaded.OnLoadComplete();
        harness.World.FireDropClosed(recovery.Pickup.Guid);

        var unstaged = reloaded.State!.JobBoard.ActiveJob!.Recovery!;
        Assert.True(unstaged.Staged);
        Assert.Equal(0, unstaged.StagedSlotIndex);
        Assert.False(unstaged.Custody);
        Assert.Equal(2, harness.World.Inserts.Count);
        Assert.Equal(recovery.BrickCount, harness.World.SlotQuantity(recovery.Pickup.Guid, 0));
    }

    [Fact]
    public void Reloaded_custody_stage_subscribes_to_handoff_and_delivers()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var recovery = job.Recovery!;

        harness.World.SeedSlots(recovery.Pickup.Guid, Slot(recovery.StagedSlotIndex, "rich", "brick", 0));
        harness.World.FireDropClosed(recovery.Pickup.Guid);
        Assert.True(harness.Service.State!.JobBoard.ActiveJob!.Recovery!.Custody);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        var reloaded = new Chapter3RuntimeService(
            new FakeContext(), harness.Repository, new FakeGate { Satisfied = true }, harness.Clock, harness.Logs.Add);
        reloaded.Wallet = new Chapter3Wallet(harness.World);
        reloaded.DeadDropWorld = harness.World;
        reloaded.OnPreLoad();
        reloaded.OnLoadComplete();

        var loadedRecovery = reloaded.State!.JobBoard.ActiveJob!.Recovery!;
        Assert.True(loadedRecovery.Custody);
        Assert.Equal(new[] { loadedRecovery.Handoff.Guid }, harness.World.DropClosedCallbacks.Keys);

        harness.World.SeedSlots(loadedRecovery.Handoff.Guid, Slot(0, "rich", "brick", loadedRecovery.BrickCount));
        harness.World.FireDropClosed(loadedRecovery.Handoff.Guid);

        Assert.Null(reloaded.State!.JobBoard.ActiveJob);
        Assert.Single(harness.World.QuantityChanges);
        Assert.Equal((loadedRecovery.Handoff.Guid, 0, -loadedRecovery.BrickCount), harness.World.QuantityChanges[0]);
        Assert.Equal(new[] { (float)job.Reward }, harness.World.CashChanges);
    }

    [Fact]
    public void Unfrozen_recovery_logs_once_that_no_quest_is_projected()
    {
        var harness = new Harness();
        harness.Service.DeadDropWorld = null;
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        Assert.Null(harness.Service.GetPresentationPlan(false).Quest);
        harness.Service.GetPresentationPlan(false);

        Assert.Single(harness.Logs, line => line.Contains("no quest", StringComparison.Ordinal));
    }

    [Fact]
    public void Frozen_recovery_projects_a_two_entry_quest_without_warning()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        var plan = harness.Service.GetPresentationPlan(false);

        Assert.NotNull(plan.Quest);
        Assert.Equal(2, plan.Quest!.Entries.Count);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("no quest", StringComparison.Ordinal));
    }

    [Fact]
    public void QaDeliver_refuses_a_recovery_job()
    {
        var harness = new Harness();
        SeedRecoveryWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        var result = harness.Service.QaDeliverActiveJob();

        Assert.False(result.Ok);
        Assert.Contains("package recovery", result.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
    }

    private static void SeedRecoveryWorld(RecoveryFakeWorld world)
    {
        world.SeedProducts(
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("richer", "Richer", 50_000d, false));
        world.SeedDrops(
            Drop("drop-full", "Drop Full", false),
            Drop("drop-a", "Drop A", true),
            Drop("drop-b", "Drop B", true));
    }

    private static Release1SmallCourtesyDropCandidate Drop(string guid, string name, bool isEmpty) =>
        new(guid, name, "description", 1d, 2d, 3d, isEmpty);

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    private sealed class Harness
    {
        public RecoveryFakeWorld World { get; } = new();
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

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, "76561190000000001", System.IO.Path.GetTempPath());

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
