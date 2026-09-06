using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// The observation table row by row: what ReconcileObservation reports and what it writes for
/// every classification of the handoff drop, plus the shortfall and spread dedupe behavior that
/// the progress fields exist to carry into Task 7's Nell messages. Nothing here mutates the world;
/// every assertion is either a returned status or an in-memory progress row.
/// </summary>
public sealed class Release1ShortNoticeObservationTests
{
    [Fact]
    public void Slots_that_cannot_be_read_report_Unavailable_and_write_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.ThrowOnRead = true;

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Unavailable, status);
        Assert.Equal(Release1ShortNoticeProgress.Fresh(assignment.Attempt), ProgressOf(harness));
        Assert.DoesNotContain(harness.Logs, line => line.Contains("reported no slots at all"));
    }

    [Fact]
    public void A_zero_length_read_is_never_treated_as_empty()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());
        var recorded = ProgressOf(harness);
        Assert.Equal(1, recorded.ObservedQuantity);
        Assert.Equal(2, recorded.LastShortfallNoticed);

        // A zero-length read reports that the drop has not finished initializing; it must never be
        // treated as an empty drop, and it never clears what was already recorded.
        harness.World.ClearDropSlots(assignment.HandoffDropGuid);
        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Unavailable, status);
        Assert.Equal(recorded, ProgressOf(harness));
        Assert.Contains(harness.Logs, line => line.Contains("reported no slots at all"));
    }

    [Fact]
    public void No_identity_match_after_a_shortfall_reports_NoWork_and_clears_the_observed_quantity_to_zero()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());

        harness.World.EmptySlot(assignment.HandoffDropGuid, 0);
        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.NoWork, status);
        var progress = ProgressOf(harness);
        // Per the observation table, a no-match pass only clears ObservedQuantity to zero; the last
        // shortfall count noticed is left as it was, exactly as every other non-Shortfall row leaves it.
        Assert.Equal(0, progress.ObservedQuantity);
        Assert.Equal(2, progress.LastShortfallNoticed);
    }

    [Fact]
    public void A_shortfall_records_the_remaining_count_and_a_second_pass_at_the_same_count_writes_nothing_new()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);

        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());
        var revisionAfterFirst = harness.Story.State!.Revision;
        var first = ProgressOf(harness);
        Assert.Equal(1, first.ObservedQuantity);
        Assert.Equal(2, first.LastShortfallNoticed);

        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());

        Assert.Equal(revisionAfterFirst, harness.Story.State!.Revision);
        Assert.Equal(first, ProgressOf(harness));
    }

    [Fact]
    public void A_shortfall_that_moves_from_two_remaining_to_one_records_the_new_count()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());
        Assert.Equal(2, ProgressOf(harness).LastShortfallNoticed);
        var revisionAfterFirst = harness.Story.State!.Revision;

        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 2);
        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, status);
        var second = ProgressOf(harness);
        Assert.Equal(2, second.ObservedQuantity);
        Assert.Equal(1, second.LastShortfallNoticed);
        Assert.True(harness.Story.State!.Revision > revisionAfterFirst);
    }

    [Fact]
    public void A_shortfall_that_falls_back_to_two_remaining_is_accepted_and_writes_no_new_message()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        // Two remaining, then one remaining: the receipt for two remaining already exists.
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 2);
        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, harness.Service.ReconcileObservation());
        Assert.Equal(1, ProgressOf(harness).LastShortfallNoticed);

        // The player takes a unit back out; the manifest falls back to a count already seen once.
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Shortfall, status);
        var progress = ProgressOf(harness);
        Assert.Equal(1, progress.ObservedQuantity);
        Assert.Equal(2, progress.LastShortfallNoticed);
    }

    [Fact]
    public void A_spread_is_recorded_once_per_attempt_and_never_consumes()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        harness.World.SetSlot(assignment.HandoffDropGuid, 1, assignment.ProductId, assignment.PackagingId, 2);

        Assert.Equal(Release1ShortNoticeObservationStatus.Spread, harness.Service.ReconcileObservation());
        var first = ProgressOf(harness);
        Assert.True(first.SpreadNoticed);
        Assert.Equal(0, first.ObservedQuantity);
        var revisionAfterFirst = harness.Story.State!.Revision;

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Spread, status);
        Assert.Equal(revisionAfterFirst, harness.Story.State!.Revision);
        Assert.Equal(1, harness.World.Slot(assignment.HandoffDropGuid, 0).Quantity);
        Assert.Equal(2, harness.World.Slot(assignment.HandoffDropGuid, 1).Quantity);
    }

    [Fact]
    public void A_spread_that_becomes_one_slot_at_or_above_N_reports_Ready()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);
        harness.World.SetSlot(assignment.HandoffDropGuid, 1, assignment.ProductId, assignment.PackagingId, 2);
        Assert.Equal(Release1ShortNoticeObservationStatus.Spread, harness.Service.ReconcileObservation());
        Assert.True(ProgressOf(harness).SpreadNoticed);

        // The player consolidates the whole order into one slot at or above N.
        harness.World.EmptySlot(assignment.HandoffDropGuid, 1);
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity);

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Ready, status);
        var progress = ProgressOf(harness);
        Assert.Equal(assignment.RequiredQuantity, progress.ObservedQuantity);
        Assert.True(progress.SpreadNoticed);
    }

    [Fact]
    public void The_wrong_product_or_an_unpackaged_slot_is_not_a_match()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, "methamphetamine", assignment.PackagingId, 5);
        harness.World.SetSlot(assignment.HandoffDropGuid, 1, assignment.ProductId, assignment.PackagingId, 5, isPackaged: false);

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.NoWork, status);
        Assert.Equal(0, ProgressOf(harness).ObservedQuantity);
    }

    [Fact]
    public void A_slot_above_N_reports_Ready_and_the_surplus_is_not_recorded_as_a_shortfall()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 5);

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Ready, status);
        var progress = ProgressOf(harness);
        Assert.Equal(5, progress.ObservedQuantity);
        Assert.Null(progress.LastShortfallNoticed);
    }

    [Fact]
    public void Exactly_N_units_in_one_slot_reports_Ready()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity);

        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.Ready, status);
        Assert.Equal(assignment.RequiredQuantity, ProgressOf(harness).ObservedQuantity);
    }

    [Fact]
    public void TryHandleDropClosed_ignores_a_guid_that_is_not_the_handoff()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity);

        var status = harness.Service.TryHandleDropClosed("not-the-handoff-drop");

        Assert.Equal(Release1ShortNoticeObservationStatus.NoWork, status);
        Assert.Equal(Release1ShortNoticeProgress.Fresh(assignment.Attempt), ProgressOf(harness));
    }

    [Fact]
    public void TryHandleDropClosed_reconciles_the_matching_handoff_drop_and_reports_Ready()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity);

        var status = harness.Service.TryHandleDropClosed(assignment.HandoffDropGuid);

        Assert.Equal(Release1ShortNoticeObservationStatus.Ready, status);
        Assert.Equal(assignment.RequiredQuantity, ProgressOf(harness).ObservedQuantity);
    }

    [Fact]
    public void The_world_raising_OnClosed_for_the_handoff_drop_runs_the_same_observation_pass_as_TryHandleDropClosed()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);

        harness.World.RaiseClosed(assignment.HandoffDropGuid);

        var progress = ProgressOf(harness);
        Assert.Equal(1, progress.ObservedQuantity);
        Assert.Equal(2, progress.LastShortfallNoticed);
    }

    [Fact]
    public void Observation_does_no_work_while_saving_or_when_the_context_does_not_match()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1);

        harness.Service.OnSaveStart();
        Assert.Equal(Release1ShortNoticeObservationStatus.NoWork, harness.Service.ReconcileObservation());
        Assert.Equal(Release1ShortNoticeProgress.Fresh(assignment.Attempt), ProgressOf(harness));
        harness.Service.OnSaveComplete();

        // The convergence pass that OnSaveComplete runs on its own observed the shortfall normally.
        var settled = ProgressOf(harness);
        Assert.Equal(1, settled.ObservedQuantity);

        harness.World.Context = harness.World.Context with { PlayerId = "someone-else" };
        var status = harness.Service.ReconcileObservation();

        Assert.Equal(Release1ShortNoticeObservationStatus.NoWork, status);
        Assert.Equal(settled, ProgressOf(harness));
    }

    private static Release1ShortNoticeProgress ProgressOf(Release1ShortNoticeHarness.Harness harness) =>
        harness.Story.State!.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == harness.Assignment.Attempt)
        ?? Release1ShortNoticeProgress.Fresh(harness.Assignment.Attempt);
}
