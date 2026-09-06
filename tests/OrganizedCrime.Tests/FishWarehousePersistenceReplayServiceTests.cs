using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePersistenceReplayServiceTests
{
    [Fact]
    public void Non_authoritative_persistence_lifecycle_gate_skips_callback_orchestration()
    {
        var calls = new List<string>();

        FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
            isAuthoritativeHost: false,
            callback: () => calls.Add("save"));
        FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
            isAuthoritativeHost: false,
            callback: () => calls.Add("pre-load"));
        FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
            isAuthoritativeHost: false,
            callback: () => calls.Add("load-complete"));

        Assert.Empty(calls);

        FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(
            isAuthoritativeHost: true,
            callback: () => calls.Add("save"));
        Assert.Equal(new[] { "save" }, calls);
    }

    [Fact]
    public void Capture_does_not_replay_before_load_completion()
    {
        var operations = new FakeOperations();
        var service = CreateService(operations);

        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.Inactive, service.Phase);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void Successful_generation_runs_each_phase_once_in_dependency_order()
    {
        var operations = new FakeOperations { RuntimeReady = true, NavigationReady = true };
        var order = new List<string>();
        operations.Order = order;
        var persisted = new List<FishWarehouseReplayPhase>();
        var service = CreateService(operations, checkpoint =>
        {
            persisted.Add(checkpoint.Phase);
            order.Add($"persist:{checkpoint.Phase}");
            return true;
        });

        service.HandleLoadComplete();
        service.Update();
        service.Update();
        service.Update();
        service.Update();
        service.Update();
        service.Update();

        Assert.Equal(
            new[]
            {
                "configure",
                "request-restore",
                "replay-objects",
                "mark-objects",
                "release-deliveries",
                "replay-employees"
            },
            operations.Calls);
        Assert.Equal(
            new[]
            {
                FishWarehouseReplayPhase.RuntimeReady,
                FishWarehouseReplayPhase.ObjectsReplayed,
                FishWarehouseReplayPhase.NavigationReady,
                FishWarehouseReplayPhase.DeliveriesReleased,
                FishWarehouseReplayPhase.Complete
            },
            persisted);
        Assert.Equal(
            new[]
            {
                "configure",
                "request-restore",
                "persist:RuntimeReady",
                "replay-objects",
                "mark-objects",
                "persist:ObjectsReplayed",
                "persist:NavigationReady",
                "release-deliveries",
                "persist:DeliveriesReleased",
                "replay-employees",
                "persist:Complete"
            },
            order);
        Assert.Equal(FishWarehouseReplayPhase.Complete, service.Phase);

        service.Update();

        Assert.Equal(1, operations.Calls.Count(call => call == "replay-objects"));
        Assert.Equal(1, operations.Calls.Count(call => call == "replay-employees"));
        Assert.Equal(1, operations.Calls.Count(call => call == "release-deliveries"));
    }

    [Fact]
    public void Navigation_and_employee_replay_are_gated_by_prior_phases()
    {
        var operations = new FakeOperations { RuntimeReady = true, NavigationReady = false };
        var service = CreateService(operations);

        service.HandleLoadComplete();
        service.Update();
        service.Update();
        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.ObjectsReplayed, service.Phase);
        Assert.DoesNotContain("replay-employees", operations.Calls);

        operations.NavigationReady = true;
        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.NavigationReady, service.Phase);
        Assert.DoesNotContain("replay-employees", operations.Calls);
    }

    [Fact]
    public void Exception_records_failed_phase_before_control_is_released()
    {
        var operations = new FakeOperations
        {
            RuntimeReady = true,
            ObjectReplay = FishWarehouseReplayOperationResult.Failed("object boom")
        };
        var persisted = new List<FishWarehouseReplayCheckpoint>();
        var service = CreateService(operations, checkpoint =>
        {
            persisted.Add(checkpoint);
            return true;
        });

        service.HandleLoadComplete();
        service.Update();
        service.Update();

        var failed = Assert.Single(persisted, checkpoint => checkpoint.Phase == FishWarehouseReplayPhase.Failed);
        Assert.Equal(nameof(FishWarehouseReplayPhase.RuntimeReady), failed.FailurePhase);
        Assert.Contains("object boom", failed.FailureReason);
        Assert.Equal(FishWarehouseReplayPhase.Failed, service.Phase);
        Assert.True(service.RequiresSnapshotProtection);
    }

    [Fact]
    public void Prepare_for_load_clears_runtime_references_in_order_but_retains_checkpoint_metadata()
    {
        var operations = new FakeOperations();
        var lifecycle = new FishWarehouseReplayLifecycle();
        var checkpoint = Checkpoint("generation-a", "folder-a");
        Assert.True(lifecycle.Begin(checkpoint, out var beginFailure), beginFailure);
        var order = new List<string>();
        var service = CreateService(
            operations,
            _ => true,
            lifecycle,
            dropTypedReferences: () => order.Add("typed"),
            resetDeliveries: () => order.Add("deliveries"),
            teardownRuntime: () => order.Add("runtime"));

        service.PrepareForLoad();

        Assert.Equal(new[] { "typed", "deliveries", "runtime" }, order);
        Assert.Equal(FishWarehouseReplayPhase.Inactive, service.Phase);
        Assert.Equal(checkpoint, lifecycle.CurrentCheckpoint);
    }

    [Fact]
    public void A_different_save_folder_key_cannot_reuse_the_generation()
    {
        var operations = new FakeOperations();
        var lifecycle = new FishWarehouseReplayLifecycle();
        var checkpoint = Checkpoint("generation-a", "folder-a");
        Assert.True(lifecycle.Begin(checkpoint, out var beginFailure), beginFailure);
        var service = CreateService(operations, lifecycle: lifecycle, activeFolder: "folder-b");

        service.HandleLoadComplete();

        Assert.Equal(FishWarehouseReplayPhase.Failed, service.Phase);
        Assert.Contains("save folder", service.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request-restore", operations.Calls);
    }

    [Fact]
    public void An_active_generation_fails_closed_if_the_save_folder_changes_on_a_later_tick()
    {
        var activeFolder = "folder-a";
        var operations = new FakeOperations { RuntimeReady = true };
        var service = CreateService(operations, activeFolderProvider: () => activeFolder);

        service.HandleLoadComplete();
        activeFolder = "folder-b";
        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.Failed, service.Phase);
        Assert.Contains("different active save folder", service.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replay-objects", operations.Calls);
    }

    [Fact]
    public void No_capture_fresh_ownership_keeps_immediate_runtime_restore_behavior()
    {
        var operations = new FakeOperations();
        var service = CreateService(
            operations,
            captureResult: new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Inactive),
            state: FishWarehouseSaveState.OwnedState());

        service.HandleLoadComplete();

        Assert.Equal(FishWarehouseReplayPhase.Inactive, service.Phase);
        Assert.Contains("configure", operations.Calls);
        Assert.Contains("request-restore", operations.Calls);
        Assert.DoesNotContain("replay-objects", operations.Calls);
    }

    [Fact]
    public void Failed_checkpoint_is_rearmed_when_load_capture_is_valid()
    {
        var operations = new FakeOperations();
        var lifecycle = new FishWarehouseReplayLifecycle(_ => new FishWarehouseCaptureRecoveryPrerequisites(true, true, true, true));
        var failedCheckpoint = Checkpoint("generation-a", "folder-a") with
        {
            Phase = FishWarehouseReplayPhase.Failed,
            FailurePhase = nameof(FishWarehouseReplayPhase.DeliveriesReleased),
            FailureReason = "active haul failed"
        };
        Assert.True(lifecycle.Begin(failedCheckpoint, out var beginFailure), beginFailure);

        var service = new FishWarehousePersistenceReplayService(
            lifecycle,
            () => "folder-a",
            _ => new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Captured),
            () => true,
            () => FishWarehouseSaveState.OwnedState() with { Replay = failedCheckpoint },
            operations,
            _ => true,
            saveFolderKey: folder => folder);

        service.HandleLoadComplete();

        Assert.Equal(FishWarehouseReplayPhase.Captured, service.Phase);
        Assert.Contains("configure", operations.Calls);
        Assert.Contains("request-restore", operations.Calls);
    }

    [Fact]
    public void Employee_inventory_marker_is_persisted_before_employee_phase_can_complete()
    {
        var operations = new FakeOperations
        {
            RuntimeReady = true,
            NavigationReady = true,
            EmployeeReplay = new FishWarehouseReplayOperationResult(
                FishWarehouseReplayOperationState.Succeeded,
                1,
                null,
                new[] { "00000000-0000-0000-0000-000000000111" })
        };
        var persisted = new List<FishWarehouseReplayCheckpoint>();
        var service = CreateService(operations, checkpoint =>
        {
            persisted.Add(checkpoint);
            return true;
        });

        service.HandleLoadComplete();
        service.Update();
        service.Update();
        service.Update();
        service.Update();
        service.Update();

        var markerCheckpoints = persisted.Where(checkpoint =>
            checkpoint.InventoryRestoredEmployeeGuids?.Contains("00000000-0000-0000-0000-000000000111") == true).ToArray();
        Assert.NotEmpty(markerCheckpoints);
        Assert.All(markerCheckpoints, checkpoint =>
            Assert.Contains("00000000-0000-0000-0000-000000000111", checkpoint.InventoryRestoredEmployeeGuids!));
    }

    [Fact]
    public void Retained_delivery_failure_after_a_noop_update_fails_the_generation()
    {
        var operations = new FakeOperations
        {
            RuntimeReady = true,
            NavigationReady = true,
            DeliveryReplay = new FishWarehouseDeliveryRestoreFlushResult(1, "retained delivery failure")
        };
        var service = CreateService(operations);

        service.HandleLoadComplete();
        service.Update();
        service.Update();
        service.Update();
        service.Update();
        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.Failed, service.Phase);
        Assert.Contains("retained delivery failure", service.FailureReason);
    }

    [Fact]
    public void Multiple_restored_employee_homes_fail_closed_before_authored_fallback()
    {
        var operations = new FakeOperations
        {
            RuntimeReady = true,
            MarkObjectsResult = FishWarehouseReplayOperationResult.Failed(
                $"restored employee-home adoption returned {FishWarehouseEmployeeHomeAdoptionResult.MultipleFound}")
        };
        var service = CreateService(operations);

        service.HandleLoadComplete();
        service.Update();
        service.Update();

        Assert.Equal(FishWarehouseReplayPhase.Failed, service.Phase);
        Assert.Contains("MultipleFound", service.FailureReason, StringComparison.Ordinal);
        Assert.DoesNotContain("replay-employees", operations.Calls);
    }

    [Fact]
    public void Captured_employee_replay_descriptors_preserve_the_protected_raw_json()
    {
        const string rawJson = "{\"DataType\":\"OpaqueEmployeeData\",\"BaseData\":{\"GUID\":\"employee-guid-1\"}}";
        var records = new[]
        {
            new FishWarehouseNativePropertyEmployeeSnapshot(
                "employee-guid-1",
                "OpaqueEmployeeData",
                "Opaque Employee",
                false,
                "locker-guid",
                Array.Empty<string>(),
                rawJson)
        };

        var descriptors = FishWarehousePropertyCaptureService.CreateEmployeeReplayDescriptors(records);

        Assert.Equal(rawJson, Assert.Single(descriptors).RawJson);
    }

    private static FishWarehousePersistenceReplayService CreateService(
        FakeOperations operations,
        Func<FishWarehouseReplayCheckpoint, bool>? persistCheckpoint = null,
        FishWarehouseReplayLifecycle? lifecycle = null,
        string activeFolder = "folder-a",
        Func<string>? activeFolderProvider = null,
        FishWarehousePropertyCaptureResult? captureResult = null,
        FishWarehouseSaveState? state = null,
        Action? dropTypedReferences = null,
        Action? resetDeliveries = null,
        Action? teardownRuntime = null)
    {
        lifecycle ??= new FishWarehouseReplayLifecycle();
        var checkpoint = Checkpoint("generation-a", "folder-a");
        if (lifecycle.CurrentCheckpoint is null)
            Assert.True(lifecycle.Begin(checkpoint, out var beginFailure), beginFailure);

        state ??= FishWarehouseSaveState.OwnedState() with { Replay = checkpoint };
        captureResult ??= new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Captured);
        return new FishWarehousePersistenceReplayService(
            lifecycle,
            () => activeFolderProvider?.Invoke() ?? activeFolder,
            _ => captureResult,
            () => true,
            () => state,
            operations,
            checkpointToPersist =>
            {
                return persistCheckpoint?.Invoke(checkpointToPersist) ?? true;
            },
            dropTypedReferences,
            resetDeliveries,
            teardownRuntime,
            saveFolderKey: folder => folder);
    }

    private static FishWarehouseReplayCheckpoint Checkpoint(string generationId, string folderKey) =>
        new(
            generationId,
            folderKey,
            FishWarehouseReplayPhase.Captured,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            2,
            1);

    private sealed class FakeOperations : IFishWarehousePersistenceReplayOperations
    {
        public bool RuntimeReady { get; set; }
        public bool NavigationReady { get; set; }
        public FishWarehouseReplayOperationResult ObjectReplay { get; set; } = FishWarehouseReplayOperationResult.Succeeded(2);
        public FishWarehouseReplayOperationResult MarkObjectsResult { get; set; } = FishWarehouseReplayOperationResult.Succeeded(0);
        public FishWarehouseReplayOperationResult EmployeeReplay { get; set; } = FishWarehouseReplayOperationResult.Succeeded(1);
        public FishWarehouseDeliveryRestoreFlushResult DeliveryReplay { get; set; } = new(0, null);
        public List<string> Calls { get; } = new();
        public List<string>? Order { get; set; }

        bool IFishWarehousePersistenceReplayOperations.IsRuntimeReady => RuntimeReady;
        bool IFishWarehousePersistenceReplayOperations.IsNavigationReady => NavigationReady;

        FishWarehouseReplayOperationResult IFishWarehousePersistenceReplayOperations.ReplayObjects()
        {
            Calls.Add("replay-objects");
            Order?.Add("replay-objects");
            return ObjectReplay;
        }

        FishWarehouseReplayOperationResult IFishWarehousePersistenceReplayOperations.MarkObjectsReplayed()
        {
            Calls.Add("mark-objects");
            Order?.Add("mark-objects");
            return MarkObjectsResult;
        }

        FishWarehouseReplayOperationResult IFishWarehousePersistenceReplayOperations.ReplayEmployees()
        {
            Calls.Add("replay-employees");
            Order?.Add("replay-employees");
            return EmployeeReplay;
        }

        FishWarehouseDeliveryRestoreFlushResult IFishWarehousePersistenceReplayOperations.ReleaseDeliveries()
        {
            Calls.Add("release-deliveries");
            Order?.Add("release-deliveries");
            return DeliveryReplay;
        }

        void IFishWarehousePersistenceReplayOperations.ConfigureReplay(bool objectReplayRequired, int? employeeAgentTypeId)
        {
            Calls.Add("configure");
            Order?.Add("configure");
        }

        void IFishWarehousePersistenceReplayOperations.RequestOwnedRestore()
        {
            Calls.Add("request-restore");
            Order?.Add("request-restore");
        }
    }
}
