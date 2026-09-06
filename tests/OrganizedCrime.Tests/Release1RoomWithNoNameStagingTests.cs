using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameStagingTests
{
    [Fact]
    public void An_empty_source_drop_is_staged_once_and_never_re_inserted()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();

        Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, harness.Service.ReconcileStaging());

        var slot = harness.World.Slot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(harness.Assignment.ProductId, slot.ProductId);
        Assert.Equal(harness.Assignment.PackagingId, slot.PackagingId);
        Assert.Equal(1, slot.Quantity);
        Assert.True(slot.IsPackaged);
        Assert.Equal(1, harness.World.InsertCount);
        Assert.True(harness.Progress()!.Staged);
        Assert.False(harness.Progress()!.Custody);

        Assert.Equal(Release1RoomWithNoNameStageStatus.AlreadyStaged, harness.Service.ReconcileStaging());
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void Staging_never_locks_any_slot_and_never_touches_a_closet()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();

        harness.Service.ReconcileStaging();

        Assert.DoesNotContain(harness.World.MutationLog, entry => entry.StartsWith("lock:", StringComparison.Ordinal));
        Assert.False(harness.World.IsLocked);
        Assert.Equal(0, harness.World.ClosetMutations);
    }

    [Fact]
    public void Staging_writes_no_sidecar_row_until_the_next_native_save()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.Repository.ResetEvidence();

        harness.Service.ReconcileStaging();

        Assert.Empty(harness.Repository.Updates);
        Assert.True(harness.Story.State!.Revision > harness.Story.LastPersistedRevision);

        Release1RoomWithNoNameHarness.Save(harness);
        Assert.True(harness.Repository.StoredState!.RoomWithNoNameProgress.Single().Staged);
    }

    [Fact]
    public void An_exact_fingerprint_already_present_records_staged_without_inserting()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.PlaceExactPackage(harness.Assignment);

        Assert.Equal(Release1RoomWithNoNameStageStatus.AlreadyStaged, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.True(harness.Progress()!.Staged);
    }

    [Fact]
    public void An_empty_drop_with_staged_recorded_infers_custody()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.Service.ReconcileStaging();
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);

        Assert.Equal(Release1RoomWithNoNameStageStatus.CustodyInferred, harness.Service.ReconcileStaging());

        Assert.True(harness.Progress()!.Custody);
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void A_zero_length_read_is_never_treated_as_empty()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.ClearDropSlots(harness.Assignment.SourceDropGuid);

        Assert.Equal(Release1RoomWithNoNameStageStatus.Unavailable, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.Null(harness.Progress());
    }

    [Fact]
    public void A_contaminated_drop_holds_and_logs_once_per_attempt_per_load()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.SetSlot(harness.Assignment.SourceDropGuid, 1, "meth", "jar", 1, isPackaged: true);

        Assert.Equal(Release1RoomWithNoNameStageStatus.Held, harness.Service.ReconcileStaging());
        Assert.Equal(Release1RoomWithNoNameStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Equal(0, harness.World.InsertCount);
        Assert.Single(harness.Logs, line => line.Contains("holds something other than exactly the staged consignment", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_insertion_holds_and_never_records_staged()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.RejectNextInsert = true;

        Assert.Equal(Release1RoomWithNoNameStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Null(harness.Progress());
    }

    [Fact]
    public void A_readback_that_is_not_the_exact_single_fingerprint_holds()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.CorruptNextInsertReadback = true;

        Assert.Equal(Release1RoomWithNoNameStageStatus.Held, harness.Service.ReconcileStaging());

        Assert.Null(harness.Progress());
        Assert.Single(harness.Logs, line => line.Contains("did not read back as the exact single fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public void The_source_close_event_is_the_fast_path_to_the_same_custody_transition()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.Service.ReconcileStaging();
        Assert.Equal(harness.Assignment.SourceDropGuid, harness.World.SubscribedDropGuid);
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);

        harness.World.RaiseClosed(harness.Assignment.SourceDropGuid);

        Assert.True(harness.Progress()!.Custody);
    }

    [Fact]
    public void Staging_and_custody_revert_with_an_unsaved_quit()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        using (var first = Release1RoomWithNoNameHarness.Active(repository, world))
        {
            first.Service.ReconcileStaging();
            first.World.EmptySlot(first.Assignment.SourceDropGuid, 0);
            first.Service.ReconcileStaging();
            Assert.True(first.Progress()!.Custody);
            first.World.RevertToLastSave();
            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world, clearSlots: true);
        Assert.Null(restored.Progress());
    }

    [Fact]
    public void The_subscription_follows_progress_and_never_binds_a_closet()
    {
        using var harness = Release1RoomWithNoNameHarness.InCustody();

        Assert.Null(harness.World.SubscribedDropGuid);
        Assert.Equal(0, harness.World.ClosetSubscriptions);
    }
}
