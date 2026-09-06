using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePropertyCapturePolicyTests
{
    [Fact]
    public void Non_target_property_policy_returns_pass_through()
    {
        var decision = FishWarehousePropertyCapturePolicy.EvaluateLoad(
            isAuthoritativeHost: true,
            propertyCode: "barn",
            activeSaveFolder: "active-save",
            attempt: SuccessfulAttempt);

        Assert.True(decision.RunOriginal);
        Assert.Equal(FishWarehousePropertyCaptureState.Inactive, decision.Result.State);
    }

    [Fact]
    public void Remote_client_policy_returns_pass_through()
    {
        var decision = FishWarehousePropertyCapturePolicy.EvaluateLoad(
            isAuthoritativeHost: false,
            propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
            activeSaveFolder: "active-save",
            attempt: SuccessfulAttempt);

        Assert.True(decision.RunOriginal);
        Assert.Equal(FishWarehousePropertyCaptureState.Inactive, decision.Result.State);
    }

    [Fact]
    public void Missing_active_folder_policy_returns_pass_through()
    {
        var decision = FishWarehousePropertyCapturePolicy.EvaluateLoad(
            isAuthoritativeHost: true,
            propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
            activeSaveFolder: null,
            attempt: SuccessfulAttempt);

        Assert.True(decision.RunOriginal);
        Assert.Equal(FishWarehousePropertyCaptureState.Inactive, decision.Result.State);
    }

    [Fact]
    public void Authoritative_target_capture_success_suppresses_native_load_and_records_captured()
    {
        var decision = FishWarehousePropertyCapturePolicy.EvaluateLoad(
            isAuthoritativeHost: true,
            propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
            activeSaveFolder: "active-save",
            attempt: SuccessfulAttempt);

        Assert.False(decision.RunOriginal);
        Assert.Equal(FishWarehousePropertyCaptureState.Captured, decision.Result.State);
        Assert.True(decision.PermitsReplay);
    }

    [Theory]
    [InlineData(false, true, true, true, "protection")]
    [InlineData(true, false, true, true, "inspection")]
    [InlineData(true, true, false, true, "typed payload")]
    [InlineData(true, true, true, false, "checkpoint")]
    public void Authoritative_target_capture_failure_never_passes_through(
        bool protectedBytes,
        bool inspected,
        bool retainedTypedPayload,
        bool persistedCheckpoint,
        string expectedFailure)
    {
        var decision = FishWarehousePropertyCapturePolicy.EvaluateLoad(
            isAuthoritativeHost: true,
            propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
            activeSaveFolder: "active-save",
            attempt: new FishWarehousePropertyCaptureAttempt(
                protectedBytes,
                inspected,
                retainedTypedPayload,
                persistedCheckpoint,
                expectedFailure));

        Assert.False(decision.RunOriginal);
        Assert.Equal(FishWarehousePropertyCaptureState.RecoveryRequired, decision.Result.State);
        Assert.Contains(expectedFailure, decision.Result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(decision.PermitsReplay);
    }

    [Fact]
    public void Successful_load_complete_retry_establishes_protection_and_permits_replay()
    {
        var result = FishWarehousePropertyCapturePolicy.EvaluateLoadCompleteRetry(SuccessfulAttempt);

        Assert.Equal(FishWarehousePropertyCaptureState.Captured, result.State);
        Assert.True(FishWarehousePropertyCapturePolicy.PermitsReplay(result));
        Assert.False(FishWarehousePropertyCapturePolicy.EnablesWriteGuard(result));
    }

    [Fact]
    public void Failed_load_complete_retry_enables_a_target_only_degraded_write_guard()
    {
        var result = FishWarehousePropertyCapturePolicy.EvaluateLoadCompleteRetry(
            new FishWarehousePropertyCaptureAttempt(true, false, true, true, "inspection"));

        Assert.Equal(FishWarehousePropertyCaptureState.Degraded, result.State);
        Assert.True(FishWarehousePropertyCapturePolicy.EnablesWriteGuard(result));
        Assert.Equal(
            FishWarehousePropertyWriteGuardDecision.SuppressWithEmptyResult,
            FishWarehouseNativePropertyWriteGuardPatch.EvaluateWriteGuard(
                isAuthoritativeHost: true,
                propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
                isDegraded: true));
        Assert.Equal(
            FishWarehousePropertyWriteGuardDecision.RunOriginal,
            FishWarehouseNativePropertyWriteGuardPatch.EvaluateWriteGuard(
                isAuthoritativeHost: true,
                propertyCode: "barn",
                isDegraded: true));
        Assert.Equal(
            FishWarehousePropertyWriteGuardDecision.RunOriginal,
            FishWarehouseNativePropertyWriteGuardPatch.EvaluateWriteGuard(
                isAuthoritativeHost: false,
                propertyCode: FishWarehouseSaveState.ExpectedPropertyCode,
                isDegraded: true));
    }

    [Fact(Skip = "Requires a live initialized Il2Cpp GameAssembly domain; standalone xUnit construction dereferences native memory.")]
    public void Suppressed_degraded_write_returns_empty_non_null_written_files_list()
    {
        Il2CppSystem.Collections.Generic.List<string> writtenFiles = null!;

        var runOriginal = FishWarehouseNativePropertyWriteGuardPatch.ApplyWriteGuardDecision(
            FishWarehousePropertyWriteGuardDecision.SuppressWithEmptyResult,
            ref writtenFiles);

        Assert.False(runOriginal);
        Assert.NotNull(writtenFiles);
        Assert.Equal(0, writtenFiles.Count);
    }

    [Fact]
    public void Recovery_checkpoint_is_persisted_before_the_capture_lifecycle_recovers()
    {
        var saveFolder = CreateTemporaryFolder();
        var store = new CaptureSaveStore();
        var recoveredCheckpoint = new FishWarehouseReplayCheckpoint(
            "generation-1",
            FishWarehouseSaveFolderKey.Create(saveFolder),
            FishWarehouseReplayPhase.Captured,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            CapturedObjectCount: 2,
            CapturedEmployeeCount: 1);
        var lifecycle = new FishWarehouseReplayLifecycle(_ => SuccessfulAttempt.ToRecoveryPrerequisites());
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);

        try
        {
            Assert.True(lifecycle.Begin(recoveredCheckpoint, out var beginFailure), beginFailure);
            lifecycle.Fail(FishWarehouseReplayPhase.Captured, "initial inspection failed");

            Assert.True(service.TryPersistRecoveredCaptureCheckpoint(recoveredCheckpoint));
            Assert.Equal(recoveredCheckpoint, store.LastWrittenState!.Replay);
            Assert.Equal(FishWarehouseReplayPhase.Failed, lifecycle.CurrentCheckpoint!.Phase);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Authoritative_property_code_read_fault_suppresses_native_load_and_records_recovery_required()
    {
        var saveFolder = CreateTemporaryFolder();
        var store = new CaptureSaveStore();
        var lifecycle = new FishWarehouseReplayLifecycle(_ => SuccessfulAttempt.ToRecoveryPrerequisites());
        var persistence = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);
        var service = new FishWarehousePropertyCaptureService(
            new FishWarehousePropertySnapshotStore(),
            new FishWarehouseNativePropertySnapshotInspector(),
            persistence,
            lifecycle);

        try
        {
            var runOriginal = FishWarehousePropertyLoadCapturePatch.HandleAuthoritativePropertyCodeReadFailure(
                saveFolder,
                service,
                new InvalidOperationException("PropertyCode access fault"));

            Assert.False(runOriginal);
            Assert.Equal(FishWarehousePropertyCaptureState.RecoveryRequired, service.State);
            Assert.Equal(FishWarehouseReplayPhase.Failed, lifecycle.CurrentCheckpoint!.Phase);
            Assert.Contains("PropertyCode", lifecycle.CurrentCheckpoint.FailureReason, StringComparison.Ordinal);
            Assert.Equal(FishWarehouseReplayPhase.Failed, store.LastWrittenState!.Replay!.Phase);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void New_load_reset_releases_the_failed_lifecycle_but_retains_serialized_snapshot_protection()
    {
        var saveFolder = CreateTemporaryFolder();
        var store = new CaptureSaveStore();
        var lifecycle = new FishWarehouseReplayLifecycle(_ => SuccessfulAttempt.ToRecoveryPrerequisites());
        var persistence = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);
        var service = new FishWarehousePropertyCaptureService(
            new FishWarehousePropertySnapshotStore(),
            new FishWarehouseNativePropertySnapshotInspector(),
            persistence,
            lifecycle);

        try
        {
            Assert.Equal(
                FishWarehousePropertyCaptureState.RecoveryRequired,
                service.RecordCaptureFailure(saveFolder, "generation-1", "initial capture failed").State);
            Assert.True(persistence.RequiresSnapshotProtection);

            service.ResetRuntimeReferences();

            Assert.Null(lifecycle.CurrentCheckpoint);
            Assert.True(persistence.RequiresSnapshotProtection);

            Assert.Equal(
                FishWarehousePropertyCaptureState.RecoveryRequired,
                service.RecordCaptureFailure(saveFolder, "generation-2", "next load capture failed").State);
            Assert.Equal("generation-2", lifecycle.CurrentCheckpoint!.GenerationId);
            Assert.Equal("generation-2", store.LastWrittenState!.Replay!.GenerationId);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    private static FishWarehousePropertyCaptureAttempt SuccessfulAttempt { get; } =
        new(
            ByteProtectionSucceeded: true,
            InspectionSucceeded: true,
            TypedPayloadRetentionSucceeded: true,
            CheckpointPersistenceSucceeded: true,
            FailureReason: null);

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CaptureSaveStore : IFishWarehouseSaveStore
    {
        public FishWarehouseSaveState? LastWrittenState { get; private set; }

        public bool TryRead(string saveFolder, out FishWarehouseSaveState state, out string? failureReason)
        {
            state = null!;
            failureReason = "not used";
            return false;
        }

        public bool TryWrite(string saveFolder, FishWarehouseSaveState state, out string? failureReason)
        {
            LastWrittenState = state;
            failureReason = null;
            return true;
        }
    }
}
