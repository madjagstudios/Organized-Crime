using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressStagingTests
{
    [Fact]
    public void An_empty_source_drop_is_staged_once_and_never_re_inserted()
    {
        using var harness = Release1WrongAddressHarness.Active();

        Assert.Equal(Release1WrongAddressStageStatus.Staged, harness.Service.ReconcileStaging());

        var slot = harness.World.Slot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(harness.Assignment.ProductId, slot.ProductId);
        Assert.Equal(harness.Assignment.PackagingId, slot.PackagingId);
        Assert.Equal(1, slot.Quantity);
        Assert.True(slot.IsPackaged);
        Assert.Equal(1, harness.World.InsertCount);
        Assert.True(harness.Progress()!.Staged);
        Assert.False(harness.Progress()!.Custody);

        Assert.Equal(Release1WrongAddressStageStatus.AlreadyStaged, harness.Service.ReconcileStaging());
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void Staging_never_locks_any_slot_in_the_source_drop()
    {
        using var harness = Release1WrongAddressHarness.Active();

        harness.Service.ReconcileStaging();

        Assert.DoesNotContain(harness.World.MutationLog, entry => entry.StartsWith("lock:", StringComparison.Ordinal));
        Assert.False(harness.World.IsLocked);
    }

    [Fact]
    public void Staging_writes_no_sidecar_row_until_the_next_native_save()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Repository.ResetEvidence();

        harness.Service.ReconcileStaging();

        Assert.Empty(harness.Repository.Updates);
        Assert.True(harness.Story.State!.Revision > harness.Story.LastPersistedRevision);

        Release1WrongAddressHarness.Save(harness);
        Assert.True(harness.Repository.StoredState!.WrongAddressProgress.Single().Staged);
    }

    [Fact]
    public void An_exact_fingerprint_already_present_records_staged_without_inserting()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.PlaceExactPackage(harness.Assignment);

        Assert.Equal(Release1WrongAddressStageStatus.AlreadyStaged, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.True(harness.Progress()!.Staged);
    }

    [Fact]
    public void An_empty_slot_with_staged_recorded_infers_custody()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);

        Assert.Equal(Release1WrongAddressStageStatus.CustodyInferred, harness.Service.ReconcileStaging());

        Assert.True(harness.Progress()!.Custody);
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void The_source_close_event_is_the_fast_path_to_the_same_custody_transition()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();
        Assert.Equal(harness.Assignment.SourceDropGuid, harness.World.SubscribedDropGuid);
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);

        harness.World.RaiseClosed(harness.Assignment.SourceDropGuid);

        Assert.True(harness.Progress()!.Custody);
    }

    [Fact]
    public void A_close_on_an_unrelated_drop_does_nothing()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();

        Assert.Equal(Release1WrongAddressStageStatus.NoWork, harness.Service.TryHandleDropClosed("drop-z"));
        Assert.False(harness.Progress()!.Custody);
    }

    [Theory]
    [InlineData("other-product", "brick", 1, true)]
    [InlineData("cocaine", "jar", 1, true)]
    [InlineData("cocaine", "brick", 2, true)]
    [InlineData("cocaine", "brick", 1, false)]
    public void Anything_else_in_the_source_drop_holds_without_mutating(
        string productId, string packagingId, int quantity, bool isPackaged)
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.SetSlot(harness.Assignment.SourceDropGuid, 0, productId, packagingId, quantity, isPackaged);

        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.Null(harness.Progress());
        Assert.Single(harness.Logs, line => line.Contains("source drop", StringComparison.Ordinal));

        harness.Service.ReconcileStaging();
        Assert.Single(harness.Logs, line => line.Contains("source drop", StringComparison.Ordinal));
    }

    [Fact]
    public void An_exact_fingerprint_match_alongside_a_second_item_in_another_slot_holds_without_mutating()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.PlaceExactPackage(harness.Assignment);
        harness.World.SetSlot(harness.Assignment.SourceDropGuid, 1, "other-product", "brick", 1, true);

        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.Null(harness.Progress());
    }

    [Fact]
    public void Several_slots_matching_the_exact_fingerprint_hold_without_mutating()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.PlaceExactPackage(harness.Assignment);
        harness.World.SetSlot(
            harness.Assignment.SourceDropGuid, 1,
            harness.Assignment.ProductId, harness.Assignment.PackagingId, 1, true);

        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.Null(harness.Progress());
    }

    [Fact]
    public void A_rejected_or_throwing_insertion_holds_without_recording_staged()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.RejectNextInsert = true;
        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());
        Assert.Null(harness.Progress());

        harness.World.ThrowOnInsert = true;
        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());
        Assert.Null(harness.Progress());
    }

    [Fact]
    public void An_insertion_whose_readback_does_not_match_holds_without_recording_staged()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.CorruptNextInsertReadback = true;

        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Null(harness.Progress());
        Assert.Equal(1, harness.World.InsertCount);

        Assert.Equal(Release1WrongAddressStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Null(harness.Progress());
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void A_ready_read_with_zero_slots_is_not_treated_as_empty()
    {
        using var harness = Release1WrongAddressHarness.Active();
        Assert.Equal(Release1WrongAddressStageStatus.Staged, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Staged);
        harness.World.ClearDropSlots(harness.Assignment.SourceDropGuid);

        var status = harness.Service.ReconcileStaging();

        Assert.Equal(Release1WrongAddressStageStatus.Unavailable, status);
        Assert.False(harness.Progress()!.Custody);
        Assert.True(harness.Progress()!.Staged);
        Assert.Equal(1, harness.World.InsertCount);
        Assert.Single(harness.Logs, line => line.Contains("zero slots", StringComparison.Ordinal));
    }

    [Fact]
    public void A_package_present_only_in_the_handoff_drop_does_not_satisfy_the_source_drop_staging_read()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.SetSlot(
            harness.Assignment.HandoffDropGuid, 0,
            harness.Assignment.ProductId, harness.Assignment.PackagingId, harness.Assignment.PackageQuantity, true);

        var status = harness.Service.ReconcileStaging();

        Assert.Equal(Release1WrongAddressStageStatus.Staged, status);
        Assert.Equal(1, harness.World.InsertCount);
        var sourceSlot = harness.World.Slot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(harness.Assignment.ProductId, sourceSlot.ProductId);
        var handoffSlot = harness.World.Slot(harness.Assignment.HandoffDropGuid, 0);
        Assert.Equal(harness.Assignment.PackageQuantity, handoffSlot.Quantity);
    }

    [Fact]
    public void Staged_and_custody_both_revert_with_an_unsaved_reload_and_the_stage_runs_once_again()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.Active(repository, world))
        {
            first.Service.ReconcileStaging();
            Assert.True(first.Progress()!.Staged);
            first.Service.OnPreLoad();
        }

        world.EmptySlot(world.LastStagedDropGuid!, 0);
        world.ResetMutationEvidence();

        using var restored = Release1WrongAddressHarness.Load(repository, world);
        Assert.Null(restored.Progress());
        Assert.Equal(Release1WrongAddressStageStatus.Staged, restored.Service.ReconcileStaging());
        Assert.Equal(1, world.InsertCount);
    }

    [Fact]
    public void Reload_matrix_row_taken_before_a_save_retakes_the_package_without_reinserting()
    {
        // Reload matrix: "Taken before a save" | Both revert; package back in the slot, Custody false | Player retakes it.
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.Active(repository, world))
        {
            Assert.Equal(Release1WrongAddressStageStatus.Staged, first.Service.ReconcileStaging());
            first.Service.OnPreLoad();
        }

        using var restored = Release1WrongAddressHarness.Load(repository, world, clearSlots: false);
        Assert.Null(restored.Progress());

        var status = restored.Service.ReconcileStaging();

        Assert.Equal(Release1WrongAddressStageStatus.AlreadyStaged, status);
        Assert.Equal(0, world.InsertCount);
        Assert.True(restored.Progress()!.Staged);
        Assert.False(restored.Progress()!.Custody);
    }

    [Fact]
    public void Reload_matrix_row_taken_then_saved_continues_to_handoff_without_re_staging()
    {
        // Reload matrix: "Taken, then saved" | Slot empty, Custody true | Continue to handoff.
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        Release1WrongAddressAssignment assignment;
        using (var first = Release1WrongAddressHarness.Active(repository, world))
        {
            first.Service.ReconcileStaging();
            world.EmptySlot(first.Assignment.SourceDropGuid, 0);
            Assert.Equal(Release1WrongAddressStageStatus.CustodyInferred, first.Service.ReconcileStaging());
            assignment = first.Assignment;
            Release1WrongAddressHarness.Save(first);
            first.Service.OnPreLoad();
        }

        using var restored = Release1WrongAddressHarness.Load(repository, world, clearSlots: false);
        Assert.True(restored.Progress()!.Custody);

        var status = restored.Service.ReconcileStaging();

        Assert.Equal(Release1WrongAddressStageStatus.NoWork, status);
        Assert.Equal(0, world.InsertCount);
        Assert.Equal(assignment.HandoffDropGuid, world.SubscribedDropGuid);
    }

    [Fact]
    public void Custody_moves_the_subscription_from_the_source_to_the_handoff_drop()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();
        Assert.Equal(harness.Assignment.SourceDropGuid, harness.World.SubscribedDropGuid);

        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        harness.Service.Update();

        Assert.True(harness.Progress()!.Custody);
        Assert.Equal(harness.Assignment.HandoffDropGuid, harness.World.SubscribedDropGuid);
    }

    [Fact]
    public void A_close_that_stages_returns_the_staging_status()
    {
        using var harness = Release1WrongAddressHarness.Active();

        var status = harness.Service.TryHandleDropClosed(harness.Assignment.SourceDropGuid);

        Assert.Equal(Release1WrongAddressStageStatus.Staged, status);
        Assert.Equal(1, harness.World.InsertCount);
        Assert.True(harness.Progress()!.Staged);
    }

    [Fact]
    public void A_close_that_infers_custody_returns_the_custody_status()
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.Service.ReconcileStaging();
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);

        var status = harness.Service.TryHandleDropClosed(harness.Assignment.SourceDropGuid);

        Assert.Equal(Release1WrongAddressStageStatus.CustodyInferred, status);
        Assert.True(harness.Progress()!.Custody);
    }

    [Fact]
    public void No_active_stage_holds_no_subscription()
    {
        using var harness = Release1WrongAddressHarness.Offered();

        harness.Service.Update();

        Assert.Null(harness.World.SubscribedDropGuid);
        Assert.Equal(0, harness.World.InsertCount);
    }
}
