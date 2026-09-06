using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseReplayLifecycleTests
{
    [Fact]
    public void Lifecycle_advances_only_through_the_required_forward_sequence_and_duplicate_ticks_are_idempotent()
    {
        var lifecycle = new FishWarehouseReplayLifecycle(_ => CompleteRecoveryPrerequisites);
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);

        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        Assert.True(lifecycle.RequiresSnapshotProtection);
        AdvanceTwice(lifecycle, FishWarehouseReplayPhase.RuntimeReady);
        AdvanceTwice(lifecycle, FishWarehouseReplayPhase.ObjectsReplayed);
        AdvanceTwice(lifecycle, FishWarehouseReplayPhase.NavigationReady);
        AdvanceTwice(lifecycle, FishWarehouseReplayPhase.DeliveriesReleased);
        AdvanceTwice(lifecycle, FishWarehouseReplayPhase.Complete);
        Assert.False(lifecycle.RequiresSnapshotProtection);
    }

    [Fact]
    public void Lifecycle_rejects_skipped_or_out_of_order_phases()
    {
        var lifecycle = new FishWarehouseReplayLifecycle();
        Assert.True(lifecycle.Begin(Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured), out var beginFailure), beginFailure);

        Assert.False(lifecycle.TryAdvance(FishWarehouseReplayPhase.ObjectsReplayed, out var skipFailure));
        Assert.Contains("RuntimeReady", skipFailure, StringComparison.Ordinal);
        Assert.True(lifecycle.TryAdvance(FishWarehouseReplayPhase.RuntimeReady, out var advanceFailure), advanceFailure);
        Assert.False(lifecycle.TryAdvance(FishWarehouseReplayPhase.Captured, out var backwardFailure));
        Assert.Contains("already", backwardFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lifecycle_rejects_a_new_generation_or_folder_while_a_generation_is_active()
    {
        var lifecycle = new FishWarehouseReplayLifecycle();
        Assert.True(lifecycle.Begin(Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured), out var beginFailure), beginFailure);

        Assert.False(lifecycle.Begin(Checkpoint("generation-2", "folder-a", FishWarehouseReplayPhase.Captured), out var generationFailure));
        Assert.Contains("active", generationFailure, StringComparison.OrdinalIgnoreCase);
        Assert.False(lifecycle.Begin(Checkpoint("generation-1", "folder-b", FishWarehouseReplayPhase.Captured), out var folderFailure));
        Assert.Contains("folder", folderFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_failure_can_be_recovered_only_by_the_same_generation_and_folder()
    {
        var lifecycle = new FishWarehouseReplayLifecycle(_ => CompleteRecoveryPrerequisites);
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        lifecycle.Fail(FishWarehouseReplayPhase.Captured, "native snapshot inspection failed");

        Assert.True(lifecycle.RequiresSnapshotProtection);
        Assert.True(lifecycle.TryRecoverCaptureFailure(captured, out var recoveryFailure), recoveryFailure);
        Assert.Equal(FishWarehouseReplayPhase.Captured, lifecycle.CurrentCheckpoint!.Phase);

        lifecycle.Fail(FishWarehouseReplayPhase.Captured, "second capture failure");
        Assert.False(lifecycle.TryRecoverCaptureFailure(Checkpoint("generation-2", "folder-a", FishWarehouseReplayPhase.Captured), out var generationFailure));
        Assert.Contains("generation", generationFailure, StringComparison.OrdinalIgnoreCase);
        Assert.False(lifecycle.TryRecoverCaptureFailure(Checkpoint("generation-1", "folder-b", FishWarehouseReplayPhase.Captured), out var folderFailure));
        Assert.Contains("folder", folderFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Later_phase_failure_stays_failed_when_recovery_prerequisites_are_missing()
    {
        var lifecycle = new FishWarehouseReplayLifecycle();
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        Assert.True(lifecycle.TryAdvance(FishWarehouseReplayPhase.RuntimeReady, out var advanceFailure), advanceFailure);
        lifecycle.ResetRuntimeReferences();
        lifecycle.Fail(FishWarehouseReplayPhase.RuntimeReady, "runtime replay failed");

        Assert.Equal("generation-1", lifecycle.CurrentCheckpoint!.GenerationId);
        Assert.Equal("folder-a", lifecycle.CurrentCheckpoint.SaveFolderKey);
        Assert.False(lifecycle.TryRecoverCaptureFailure(captured, out var recoveryFailure));
        Assert.Contains("prerequisite", recoveryFailure, StringComparison.OrdinalIgnoreCase);
        Assert.False(lifecycle.TryAdvance(FishWarehouseReplayPhase.ObjectsReplayed, out var advanceAfterFailure));
        Assert.Contains("failed", advanceAfterFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lifecycle_records_the_active_phase_when_a_later_failure_is_mislabeled_as_captured()
    {
        var lifecycle = new FishWarehouseReplayLifecycle(_ => CompleteRecoveryPrerequisites);
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        Assert.True(lifecycle.TryAdvance(FishWarehouseReplayPhase.RuntimeReady, out var advanceFailure), advanceFailure);

        lifecycle.Fail(FishWarehouseReplayPhase.Captured, "later replay failure");

        Assert.Equal(nameof(FishWarehouseReplayPhase.RuntimeReady), lifecycle.CurrentCheckpoint!.FailurePhase);
        Assert.True(lifecycle.TryRecoverCaptureFailure(captured, out var recoveryFailure), recoveryFailure);
        Assert.Equal(FishWarehouseReplayPhase.Captured, lifecycle.CurrentCheckpoint!.Phase);
    }

    [Fact]
    public void Lifecycle_recovers_capture_failure_only_when_all_prerequisites_are_verified()
    {
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        var lifecycle = new FishWarehouseReplayLifecycle(_ => CompleteRecoveryPrerequisites);
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        lifecycle.Fail(FishWarehouseReplayPhase.Captured, "capture failed");

        Assert.True(lifecycle.TryRecoverCaptureFailure(captured, out var recoveryFailure), recoveryFailure);
    }

    [Fact]
    public void Lifecycle_keeps_capture_failure_terminal_when_any_recovery_prerequisite_is_missing()
    {
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        var lifecycle = new FishWarehouseReplayLifecycle(_ => new FishWarehouseCaptureRecoveryPrerequisites(
            ByteProtectionSucceeded: true,
            InspectionSucceeded: false,
            TypedPayloadRetentionSucceeded: true,
            CheckpointPersistenceSucceeded: true));
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        lifecycle.Fail(FishWarehouseReplayPhase.Captured, "capture failed");

        Assert.False(lifecycle.TryRecoverCaptureFailure(captured, out var recoveryFailure));
        Assert.Contains("prerequisite", recoveryFailure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FishWarehouseReplayPhase.Failed, lifecycle.CurrentCheckpoint!.Phase);
    }

    [Fact]
    public void Lifecycle_begins_a_persisted_failed_checkpoint_with_snapshot_protection_active()
    {
        var lifecycle = new FishWarehouseReplayLifecycle();
        var failed = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Failed) with
        {
            FailurePhase = nameof(FishWarehouseReplayPhase.EmployeesReplayed),
            FailureReason = "employee replay failed"
        };

        Assert.True(lifecycle.Begin(failed, out var beginFailure), beginFailure);
        Assert.True(lifecycle.RequiresSnapshotProtection);
        Assert.False(lifecycle.TryAdvance(FishWarehouseReplayPhase.Complete, out var failure));
        Assert.Contains("failed", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Later_phase_failure_can_be_rearmed_from_a_valid_same_generation_capture()
    {
        var lifecycle = new FishWarehouseReplayLifecycle(_ => CompleteRecoveryPrerequisites);
        var captured = Checkpoint("generation-1", "folder-a", FishWarehouseReplayPhase.Captured);
        Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
        Assert.True(lifecycle.TryAdvance(FishWarehouseReplayPhase.RuntimeReady, out var advanceFailure), advanceFailure);
        lifecycle.Fail(FishWarehouseReplayPhase.RuntimeReady, "employee replay failed");

        var recovered = captured with { SnapshotSha256 = "FFEEDDCCBBAA99887766554433221100FFEEDDCCBBAA99887766554433221100" };
        Assert.True(lifecycle.TryRecoverCaptureFailure(recovered, out var recoveryFailure), recoveryFailure);
        Assert.Equal(FishWarehouseReplayPhase.Captured, lifecycle.CurrentCheckpoint!.Phase);
        Assert.Equal(recovered.SnapshotSha256, lifecycle.CurrentCheckpoint.SnapshotSha256);
        Assert.Equal("employee replay failed", lifecycle.LastRecoveredFailureReason);
    }

    private static void AdvanceTwice(FishWarehouseReplayLifecycle lifecycle, FishWarehouseReplayPhase phase)
    {
        Assert.True(lifecycle.TryAdvance(phase, out var firstFailure), firstFailure);
        Assert.True(lifecycle.TryAdvance(phase, out var duplicateFailure), duplicateFailure);
        Assert.Equal(phase, lifecycle.CurrentCheckpoint!.Phase);
    }

    private static FishWarehouseReplayCheckpoint Checkpoint(string generationId, string saveFolderKey, FishWarehouseReplayPhase phase) =>
        new(
            generationId,
            saveFolderKey,
            phase,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            2,
            1);

    private static FishWarehouseCaptureRecoveryPrerequisites CompleteRecoveryPrerequisites { get; } = new(
        ByteProtectionSucceeded: true,
        InspectionSucceeded: true,
        TypedPayloadRetentionSucceeded: true,
        CheckpointPersistenceSucceeded: true);
}
