using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePersistenceTests
{
    [Fact]
    public void Codec_round_trips_owned_fish_warehouse_state()
    {
        var json = FishWarehouseSaveCodec.Serialize(FishWarehouseSaveState.OwnedState());
        var result = FishWarehouseSaveCodec.TryDeserialize(json, out var decoded, out var failure);

        Assert.True(result, failure);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.SchemaVersion);
        Assert.Equal("oc_fishwarehouse", decoded.PropertyCode);
        Assert.True(decoded.Unlocked);
        Assert.True(decoded.Owned);
    }

    [Fact]
    public void Codec_migrates_v1_to_v2_without_losing_ownership_or_home_state()
    {
        const string v1 = """
        {
          "schemaVersion": 1,
          "propertyCode": "oc_fishwarehouse",
          "unlocked": true,
          "owned": true,
          "employeeHomePlaced": true,
          "employeeHomeAssignedEmployeeName": "Daniel Adams (Handler)"
        }
        """;

        Assert.True(FishWarehouseSaveCodec.TryDeserialize(v1, out var state, out var failure), failure);
        Assert.Equal(2, state.SchemaVersion);
        Assert.True(state.Owned);
        Assert.True(state.EmployeeHomePlaced);
        Assert.Null(state.EmployeeAgentTypeId);
        Assert.Null(state.Replay);
        Assert.Empty(state.UnsupportedEmployees);
    }

    [Fact]
    public void Codec_round_trips_v2_metadata_and_unsupported_employee_raw_json()
    {
        const string rawJson = "{\n  \"displayName\": \"Unsupported\",\n  \"value\": 7\n}";
        var expected = FishWarehouseSaveState.OwnedState() with
        {
            EmployeeHomePlaced = true,
            EmployeeAgentTypeId = -1923039037,
            Replay = new FishWarehouseReplayCheckpoint(
                "generation-1",
                "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                FishWarehouseReplayPhase.Captured,
                "OrganizedCrime/fish-warehouse.snapshot.json",
                "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
                3,
                2),
            UnsupportedEmployees = new[]
            {
                new FishWarehouseUnsupportedEmployeeRecord(
                    "employee-guid-1",
                    "UnsupportedEmployee",
                    "Opaque Employee",
                    rawJson)
            }
        };
        var json = FishWarehouseSaveCodec.Serialize(expected);

        Assert.True(FishWarehouseSaveCodec.TryDeserialize(json, out var decoded, out var failure), failure);
        Assert.True(decoded.EmployeeHomePlaced);
        Assert.Equal(expected.EmployeeAgentTypeId, decoded.EmployeeAgentTypeId);
        Assert.Equal(expected.Replay, decoded.Replay);
        Assert.Equal(rawJson, decoded.UnsupportedEmployees.Single().RawJson);
        Assert.DoesNotContain("employeeHomeAssignedEmployeeName", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Codec_round_trips_the_idempotent_inventory_restore_marker()
    {
        var employeeGuid = "00000000-0000-0000-0000-000000000111";
        var expected = FishWarehouseSaveState.OwnedState() with
        {
            Replay = new FishWarehouseReplayCheckpoint(
                "generation-1",
                "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                FishWarehouseReplayPhase.DeliveriesReleased,
                "OrganizedCrime/fish-warehouse.snapshot.json",
                "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
                3,
                2,
                InventoryRestoredEmployeeGuids: new[] { employeeGuid })
        };

        Assert.True(FishWarehouseSaveCodec.TryDeserialize(
            FishWarehouseSaveCodec.Serialize(expected), out var decoded, out var failure), failure);
        Assert.Equal(new[] { employeeGuid }, decoded.Replay!.InventoryRestoredEmployeeGuids);
    }

    [Fact]
    public void Codec_round_trips_ten_employee_checkpoint_with_owned_state_and_opaque_records()
    {
        var expected = FishWarehouseSaveState.OwnedState() with
        {
            Replay = new FishWarehouseReplayCheckpoint(
                "generation-ten",
                "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                FishWarehouseReplayPhase.EmployeesReplayed,
                "OrganizedCrime/fish-warehouse.snapshot.json",
                "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
                CapturedObjectCount: 12,
                CapturedEmployeeCount: 10,
                ReplayedObjectCount: 12,
                ReplayedEmployeeCount: 10),
            UnsupportedEmployees = new[]
            {
                new FishWarehouseUnsupportedEmployeeRecord(
                    "00000000-0000-0000-0000-000000000999",
                    "PackagerData",
                    "Packager",
                    "{\"opaque\":true}")
            }
        };

        Assert.True(FishWarehouseSaveCodec.TryDeserialize(
            FishWarehouseSaveCodec.Serialize(expected), out var decoded, out var failure), failure);

        Assert.True(decoded.Owned);
        Assert.Equal(10, decoded.Replay!.CapturedEmployeeCount);
        Assert.Equal(10, decoded.Replay.ReplayedEmployeeCount);
        Assert.Equal("{\"opaque\":true}", Assert.Single(decoded.UnsupportedEmployees).RawJson);
    }

    [Fact]
    public void Codec_rejects_wrong_property_identity()
    {
        var json = FishWarehouseSaveCodec.Serialize(FishWarehouseSaveState.OwnedState())
            .Replace("oc_fishwarehouse", "oc_other", StringComparison.Ordinal);

        var result = FishWarehouseSaveCodec.TryDeserialize(json, out _, out var failure);

        Assert.False(result);
        Assert.Contains("identity", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codec_rejects_unsupported_schema_version()
    {
        var json = FishWarehouseSaveCodec.Serialize(FishWarehouseSaveState.OwnedState())
            .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99", StringComparison.Ordinal);

        var result = FishWarehouseSaveCodec.TryDeserialize(json, out _, out var failure);

        Assert.False(result);
        Assert.Contains("schema", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codec_rejects_v2_sidecar_missing_employee_home_placed()
    {
        const string incompleteV2 = """
        {
          "schemaVersion": 2,
          "propertyCode": "oc_fishwarehouse",
          "unlocked": true,
          "owned": true,
          "employeeAgentTypeId": null,
          "replay": null,
          "unsupportedEmployees": []
        }
        """;

        var result = FishWarehouseSaveCodec.TryDeserialize(incompleteV2, out _, out var failure);

        Assert.False(result);
        Assert.Contains("employeeHomePlaced", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codec_rejects_malformed_json()
    {
        var result = FishWarehouseSaveCodec.TryDeserialize("{not-json", out _, out var failure);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Fact]
    public void Save_path_rejects_missing_active_folder()
    {
        var result = FishWarehouseSavePath.TryResolve(null, out var sidecarPath, out var failure);

        Assert.False(result);
        Assert.Equal(string.Empty, sidecarPath);
        Assert.NotNull(failure);
    }

    [Fact]
    public void Save_folder_key_normalizes_full_windows_path_case_and_trailing_separator()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", "FishWarehouse", Guid.NewGuid().ToString("N"));

        var lowerVariant = folder.ToLowerInvariant();
        var upperVariantWithSeparator = folder.ToUpperInvariant() + Path.DirectorySeparatorChar;

        Assert.Equal(
            FishWarehouseSaveFolderKey.Create(lowerVariant),
            FishWarehouseSaveFolderKey.Create(upperVariantWithSeparator));
    }

    [Fact]
    public void Save_folder_key_distinguishes_different_full_save_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", "FishWarehouse", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "SlotA");
        var second = Path.Combine(root, "SlotB");

        Assert.NotEqual(FishWarehouseSaveFolderKey.Create(first), FishWarehouseSaveFolderKey.Create(second));
    }

    [Fact]
    public void Native_property_path_falls_back_to_the_legacy_name_when_no_property_file_exists()
    {
        var root = CreateTemporaryFolder();
        try
        {
            Assert.True(
                FishWarehouseSavePath.TryResolveNativePropertyPath(
                    root,
                    RuntimePropertyDefinition.FishWarehouse,
                    out var propertyPath,
                    out var failure),
                failure);

            Assert.Equal(Path.Combine(root, "Properties", "Fish Warehouse.json"), propertyPath);
            Assert.True(Path.IsPathFullyQualified(propertyPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Native_property_path_prefers_the_existing_display_name_file_after_the_label_rename()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var renamedPath = Path.Combine(root, "Properties", "Syndicate Warehouse.json");
            Directory.CreateDirectory(Path.GetDirectoryName(renamedPath)!);
            File.WriteAllText(renamedPath, "{}");

            Assert.True(
                FishWarehouseSavePath.TryResolveNativePropertyPath(
                    root,
                    RuntimePropertyDefinition.FishWarehouse,
                    out var propertyPath,
                    out var failure),
                failure);

            Assert.Equal(renamedPath, propertyPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Native_property_path_falls_back_to_the_legacy_name_for_a_pre_rename_save()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var legacyPath = Path.Combine(root, "Properties", "Fish Warehouse.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, "{}");

            Assert.True(
                FishWarehouseSavePath.TryResolveNativePropertyPath(
                    root,
                    RuntimePropertyDefinition.FishWarehouse,
                    out var propertyPath,
                    out var failure),
                failure);

            Assert.Equal(legacyPath, propertyPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_path_resolves_sidecar_and_snapshot_under_the_active_folder()
    {
        var root = CreateTemporaryFolder();
        try
        {
            Assert.True(FishWarehouseSavePath.TryResolveSidecarPath(root, out var sidecarPath, out var sidecarFailure), sidecarFailure);
            Assert.True(FishWarehouseSavePath.TryResolveSnapshotPath(root, out var snapshotPath, out var snapshotFailure), snapshotFailure);

            Assert.Equal(Path.Combine(root, "OrganizedCrime", "fish-warehouse.json"), sidecarPath);
            Assert.Equal(Path.Combine(root, "OrganizedCrime", "fish-warehouse.snapshot.json"), snapshotPath);
            Assert.True(Path.IsPathFullyQualified(sidecarPath));
            Assert.True(Path.IsPathFullyQualified(snapshotPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_store_round_trips_state_under_active_folder()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var store = new FishWarehouseSaveStore();
            Assert.True(store.TryWrite(root, FishWarehouseSaveState.OwnedState(), out var writeFailure), writeFailure);
            Assert.True(store.TryRead(root, out var state, out var readFailure), readFailure);
            Assert.Equal(FishWarehouseSaveState.CurrentSchemaVersion, state.SchemaVersion);
            Assert.Equal(FishWarehouseSaveState.ExpectedPropertyCode, state.PropertyCode);
            Assert.True(state.Unlocked);
            Assert.True(state.Owned);
            Assert.Empty(state.UnsupportedEmployees);
            Assert.True(Directory.Exists(Path.Combine(root, "OrganizedCrime")));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "OrganizedCrime"), "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_store_rejects_malformed_sidecar_without_throwing()
    {
        var root = CreateTemporaryFolder();
        try
        {
            var directory = Path.Combine(root, "OrganizedCrime");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "fish-warehouse.json"), "{not-json");

            var store = new FishWarehouseSaveStore();
            var result = store.TryRead(root, out var state, out var failure);

            Assert.False(result);
            Assert.Null(state);
            Assert.NotNull(failure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Persistence_load_applies_valid_state_once()
    {
        var expected = FishWarehouseSaveState.OwnedState();
        var store = new FakeSaveStore { StateToRead = expected };
        FishWarehouseSaveState? applied = null;
        var service = new FishWarehousePersistenceService(
            store,
            () => "active-save",
            () => throw new Xunit.Sdk.XunitException("Load must not capture state."),
            state => applied = state);

        service.HandleLoadComplete();

        Assert.Equal(expected, applied);
        Assert.Equal("active-save", store.LastReadFolder);
    }

    [Fact]
    public void Persistence_pre_load_prime_retains_a_failed_replay_for_capture_recovery()
    {
        var key = FishWarehouseSaveFolderKey.Create("active-save");
        var failedCheckpoint = new FishWarehouseReplayCheckpoint(
            "generation-failed",
            key,
            FishWarehouseReplayPhase.Failed,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            42,
            10,
            FailurePhase: nameof(FishWarehouseReplayPhase.DeliveriesReleased),
            FailureReason: "active haul failed");
        var state = FishWarehouseSaveState.OwnedState() with { Replay = failedCheckpoint };
        var store = new FakeSaveStore { StateToRead = state };
        var lifecycle = new FishWarehouseReplayLifecycle();
        var applied = false;
        var service = new FishWarehousePersistenceService(
            store,
            () => "active-save",
            FishWarehouseSaveState.OwnedState,
            _ => applied = true,
            replayLifecycle: lifecycle);

        Assert.True(service.PrimeStateForLoad());
        Assert.Equal(state, service.CurrentState);
        Assert.Equal(FishWarehouseReplayPhase.Failed, lifecycle.CurrentCheckpoint!.Phase);
        Assert.False(applied);
    }

    [Fact]
    public void Persistence_load_does_not_apply_when_sidecar_is_missing()
    {
        var store = new FakeSaveStore { ReadResult = false, ReadFailure = "missing" };
        var applied = false;
        var service = new FishWarehousePersistenceService(
            store,
            () => "active-save",
            () => throw new Xunit.Sdk.XunitException("Load must not capture state."),
            _ => applied = true);

        service.HandleLoadComplete();

        Assert.False(applied);
    }

    [Fact]
    public void Persistence_save_writes_captured_state()
    {
        var expected = FishWarehouseSaveState.OwnedState();
        var store = new FakeSaveStore();
        var service = new FishWarehousePersistenceService(
            store,
            () => "active-save",
            () => expected,
            _ => throw new Xunit.Sdk.XunitException("Save must not apply state."));

        service.HandleSaveComplete();

        Assert.Equal(expected, store.LastWrittenState);
        Assert.Equal("active-save", store.LastWriteFolder);
    }

    [Fact]
    public void Persistence_save_guard_restores_the_protected_native_property_during_pending_replay()
    {
        var saveFolder = CreateTemporaryFolder();
        var key = FishWarehouseSaveFolderKey.Create(saveFolder);
        var originalBytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'\n', (byte)'}' };
        var nativePath = Path.Combine(saveFolder, "Properties", "Fish Warehouse.json");
        var snapshotPath = Path.Combine(saveFolder, "OrganizedCrime", "fish-warehouse.snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllBytes(nativePath, originalBytes);
        File.WriteAllBytes(snapshotPath, originalBytes);

        var checkpoint = new FishWarehouseReplayCheckpoint(
            "generation",
            key,
            FishWarehouseReplayPhase.Captured,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalBytes)),
            1,
            1);
        var lifecycle = new FishWarehouseReplayLifecycle();
        Assert.True(lifecycle.Begin(checkpoint, out var beginFailure), beginFailure);
        var store = new FakeSaveStore();
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);

        try
        {
            Assert.True(service.TryPersistReplayCheckpoint(checkpoint));
            File.WriteAllBytes(nativePath, new byte[] { (byte)'{', (byte)'}' });

            service.HandleSaveComplete();

            Assert.Equal(originalBytes, File.ReadAllBytes(nativePath));
            Assert.Equal(1, store.WriteCount);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Cold_process_capture_checkpoint_preserves_existing_sidecar_state_before_load_complete()
    {
        var saveFolder = CreateTemporaryFolder();
        var key = FishWarehouseSaveFolderKey.Create(saveFolder);
        const string opaqueRawJson = "{\"DataType\":\"OpaqueEmployeeData\",\"value\":7}";
        var existingState = FishWarehouseSaveState.OwnedState() with
        {
            Unlocked = true,
            Owned = true,
            EmployeeHomePlaced = true,
            EmployeeAgentTypeId = -1923039037,
            UnsupportedEmployees = new[]
            {
                new FishWarehouseUnsupportedEmployeeRecord(
                    "employee-guid-1",
                    "OpaqueEmployeeData",
                    "Opaque Employee",
                    opaqueRawJson)
            }
        };
        var store = new ColdProcessSaveStore(existingState);
        var checkpoint = Checkpoint("generation-cold", key);
        var captureState = FishWarehouseSaveState.UnownedState();
        var captureLifecycle = new FishWarehouseReplayLifecycle();
        var capturePersistence = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            () => captureState,
            _ => { },
            replayLifecycle: captureLifecycle);

        try
        {
            Assert.True(capturePersistence.TryPersistReplayCheckpoint(checkpoint));
            Assert.Equal(existingState.Owned, store.LastWrittenState!.Owned);
            Assert.Equal(existingState.Unlocked, store.LastWrittenState.Unlocked);
            Assert.Equal(existingState.EmployeeHomePlaced, store.LastWrittenState.EmployeeHomePlaced);
            Assert.Equal(existingState.EmployeeAgentTypeId, store.LastWrittenState.EmployeeAgentTypeId);
            Assert.Equal(opaqueRawJson, Assert.Single(store.LastWrittenState.UnsupportedEmployees).RawJson);

            var loadLifecycle = new FishWarehouseReplayLifecycle();
            FishWarehouseSaveState? loadedState = null;
            var loadPersistence = new FishWarehousePersistenceService(
                store,
                () => saveFolder,
                () => FishWarehouseSaveState.UnownedState(),
                state => loadedState = state,
                replayLifecycle: loadLifecycle);

            Assert.True(loadPersistence.HandleLoadComplete());
            Assert.NotNull(loadedState);
            Assert.True(loadedState!.Owned);
            Assert.True(loadedState.Unlocked);
            Assert.True(loadedState.EmployeeHomePlaced);
            Assert.Equal(-1923039037, loadedState.EmployeeAgentTypeId);
            Assert.Equal(opaqueRawJson, Assert.Single(loadedState.UnsupportedEmployees).RawJson);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Persistence_callbacks_tolerate_unavailable_active_save_folder()
    {
        var store = new FakeSaveStore();
        var service = new FishWarehousePersistenceService(
            store,
            () => null,
            () => FishWarehouseSaveState.OwnedState(),
            _ => throw new Xunit.Sdk.XunitException("Missing save folder must not apply state."));

        service.HandleLoadComplete();
        service.HandleSaveComplete();

        Assert.Null(store.LastReadFolder);
        Assert.Null(store.LastWriteFolder);
    }

    [Fact]
    public void Persistence_checkpoint_rejects_a_different_active_save_folder_key_without_writing()
    {
        var saveFolder = CreateTemporaryFolder();
        var store = new FakeSaveStore();
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { });

        try
        {
            var checkpoint = Checkpoint("generation-1", "different-folder-key");

            Assert.False(service.TryPersistReplayCheckpoint(checkpoint));
            Assert.Null(store.LastWrittenState);
            Assert.Null(service.CurrentState);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Persistence_checkpoint_rejects_a_stale_checkpoint_that_is_not_the_lifecycle_current_phase()
    {
        var saveFolder = CreateTemporaryFolder();
        var key = FishWarehouseSaveFolderKey.Create(saveFolder);
        var store = new FakeSaveStore();
        var lifecycle = new FishWarehouseReplayLifecycle();
        var captured = Checkpoint("generation-1", key);
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);

        try
        {
            Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
            Assert.True(service.TryPersistReplayCheckpoint(captured));
            Assert.True(lifecycle.TryAdvance(FishWarehouseReplayPhase.RuntimeReady, out var advanceFailure), advanceFailure);

            Assert.False(service.TryPersistReplayCheckpoint(captured));
            Assert.Equal(1, store.WriteCount);
            Assert.Equal(FishWarehouseReplayPhase.Captured, store.LastWrittenState!.Replay!.Phase);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Persistence_checkpoint_persists_the_active_terminal_complete_checkpoint()
    {
        var saveFolder = CreateTemporaryFolder();
        var key = FishWarehouseSaveFolderKey.Create(saveFolder);
        var store = new FakeSaveStore();
        var lifecycle = new FishWarehouseReplayLifecycle();
        var captured = Checkpoint("generation-1", key);
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => { },
            replayLifecycle: lifecycle);

        try
        {
            Assert.True(lifecycle.Begin(captured, out var beginFailure), beginFailure);
            AdvanceToComplete(lifecycle);
            var complete = lifecycle.CurrentCheckpoint!;

            Assert.True(service.TryPersistReplayCheckpoint(complete));
            Assert.Equal(1, store.WriteCount);
            Assert.Equal(FishWarehouseReplayPhase.Complete, store.LastWrittenState!.Replay!.Phase);
            Assert.Equal(complete, store.LastWrittenState.Replay);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Persistence_load_rejects_a_checkpoint_that_conflicts_with_the_active_lifecycle()
    {
        var saveFolder = CreateTemporaryFolder();
        var key = FishWarehouseSaveFolderKey.Create(saveFolder);
        var lifecycle = new FishWarehouseReplayLifecycle();
        Assert.True(lifecycle.Begin(Checkpoint("active-generation", key), out var beginFailure), beginFailure);
        var store = new FakeSaveStore
        {
            StateToRead = FishWarehouseSaveState.OwnedState() with
            {
                Replay = Checkpoint("loaded-generation", key)
            }
        };
        var applied = false;
        var service = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.OwnedState,
            _ => applied = true,
            replayLifecycle: lifecycle);

        try
        {
            Assert.False(service.HandleLoadComplete());
            Assert.False(applied);
            Assert.Null(service.CurrentState);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    private sealed class FakeSaveStore : IFishWarehouseSaveStore
    {
        public FishWarehouseSaveState? StateToRead { get; init; }
        public bool ReadResult { get; init; } = true;
        public string? ReadFailure { get; init; }
        public string? LastReadFolder { get; private set; }
        public string? LastWriteFolder { get; private set; }
        public FishWarehouseSaveState? LastWrittenState { get; private set; }
        public int WriteCount { get; private set; }

        public bool TryRead(string saveFolder, out FishWarehouseSaveState state, out string? failureReason)
        {
            LastReadFolder = saveFolder;
            state = StateToRead!;
            failureReason = ReadFailure;
            return ReadResult;
        }

        public bool TryWrite(string saveFolder, FishWarehouseSaveState state, out string? failureReason)
        {
            LastWriteFolder = saveFolder;
            LastWrittenState = state;
            WriteCount++;
            failureReason = null;
            return true;
        }
    }

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ColdProcessSaveStore : IFishWarehouseSaveStore
    {
        private readonly FishWarehouseSaveState _initialState;

        public ColdProcessSaveStore(FishWarehouseSaveState initialState) => _initialState = initialState;

        public FishWarehouseSaveState? LastWrittenState { get; private set; }

        public bool TryRead(string saveFolder, out FishWarehouseSaveState state, out string? failureReason)
        {
            state = LastWrittenState ?? _initialState;
            failureReason = null;
            return true;
        }

        public bool TryWrite(string saveFolder, FishWarehouseSaveState state, out string? failureReason)
        {
            LastWrittenState = state;
            failureReason = null;
            return true;
        }
    }

    private static FishWarehouseReplayCheckpoint Checkpoint(string generationId, string saveFolderKey) =>
        new(
            generationId,
            saveFolderKey,
            FishWarehouseReplayPhase.Captured,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            2,
            1);

    private static void AdvanceToComplete(FishWarehouseReplayLifecycle lifecycle)
    {
        foreach (var phase in new[]
                 {
                     FishWarehouseReplayPhase.RuntimeReady,
                      FishWarehouseReplayPhase.ObjectsReplayed,
                      FishWarehouseReplayPhase.NavigationReady,
                      FishWarehouseReplayPhase.DeliveriesReleased,
                     FishWarehouseReplayPhase.Complete
                 })
        {
            Assert.True(lifecycle.TryAdvance(phase, out var failureReason), failureReason);
        }
    }

}
