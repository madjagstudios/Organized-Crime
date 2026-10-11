using System.Security.Cryptography;
using System.Text;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseInterruptedReplayRecoveryTests
{
    [Fact]
    public void Interrupted_captured_checkpoint_recovers_on_the_next_load()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // The interrupted session left Captured behind; the next load adopts
            // it, then its fresh capture supersedes the stale generation.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            WriteStuckSidecar(saveFolder, folderKey, FishWarehouseReplayPhase.Captured);
            var harness = CreateHarness(saveFolder);
            Assert.True(harness.Persistence.PrimeStateForLoad());
            Assert.True(harness.Lifecycle.Begin(harness.Persistence.CurrentState!.Replay!, out var beginFailure), beginFailure);

            var fresh = ProtectFreshBytes(harness, saveFolder, "fresh-generation-captured");
            var result = harness.Capture.AdoptCapturedCheckpoint(
                saveFolder,
                "fresh-generation-captured",
                new FishWarehousePropertyCaptureAttempt(true, true, true, false, null),
                null,
                null,
                null,
                fresh);

            Assert.Equal(FishWarehousePropertyCaptureState.Captured, result.State);
            Assert.Equal("fresh-generation-captured", harness.Lifecycle.CurrentCheckpoint!.GenerationId);
            Assert.Contains(harness.Logs, line => line.Contains("superseded a stale Captured", StringComparison.Ordinal));
            Assert.Contains("fresh-generation-captured", File.ReadAllText(SidecarPath(saveFolder)), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Stuck_failed_checkpoint_recovers_on_the_next_load()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // The OC-108 stuck state itself: Failed with failurePhase Captured.
            // The next load supersedes it the same way.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            WriteStuckSidecar(saveFolder, folderKey, FishWarehouseReplayPhase.Failed);
            var harness = CreateHarness(saveFolder);
            Assert.True(harness.Persistence.PrimeStateForLoad());
            Assert.True(harness.Lifecycle.Begin(harness.Persistence.CurrentState!.Replay!, out var beginFailure), beginFailure);

            var fresh = ProtectFreshBytes(harness, saveFolder, "fresh-generation-failed");
            var result = harness.Capture.AdoptCapturedCheckpoint(
                saveFolder,
                "fresh-generation-failed",
                new FishWarehousePropertyCaptureAttempt(true, true, true, false, null),
                null,
                null,
                null,
                fresh);

            Assert.Equal(FishWarehousePropertyCaptureState.Captured, result.State);
            Assert.Equal("fresh-generation-failed", harness.Lifecycle.CurrentCheckpoint!.GenerationId);
            Assert.Contains(harness.Logs, line => line.Contains("superseded a stale Failed", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Same_generation_conflict_still_fails()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // A same-generation conflict is an in-flight replay, not a stale
            // one: the fresh capture must not steal the lifecycle.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            WriteStuckSidecar(saveFolder, folderKey, FishWarehouseReplayPhase.Captured);
            var harness = CreateHarness(saveFolder);
            Assert.True(harness.Persistence.PrimeStateForLoad());
            Assert.True(harness.Lifecycle.Begin(harness.Persistence.CurrentState!.Replay!, out var beginFailure), beginFailure);
            var adopted = harness.Lifecycle.CurrentCheckpoint!;

            var fresh = ProtectFreshBytes(harness, saveFolder, adopted.GenerationId);
            var result = harness.Capture.AdoptCapturedCheckpoint(
                saveFolder,
                adopted.GenerationId,
                new FishWarehousePropertyCaptureAttempt(true, true, true, false, null),
                null,
                null,
                null,
                fresh);

            Assert.Equal(FishWarehousePropertyCaptureState.RecoveryRequired, result.State);
            Assert.Contains("already active", result.FailureReason, StringComparison.Ordinal);
            Assert.Same(adopted, harness.Lifecycle.CurrentCheckpoint);
            Assert.DoesNotContain(harness.Logs, line => line.Contains("superseded", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Fresh_capture_with_fewer_objects_restores_the_stale_snapshot_and_captures_it()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // A save-time wipe emptied the native file after the interrupted
            // session protected a richer snapshot: the fresh capture must
            // start from the restored stale content, not the thin file.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            var harness = CreateHarness(saveFolder);
            var richBytes = Encoding.UTF8.GetBytes(RichNativeJson);
            Assert.True(
                harness.SnapshotStore.TryProtect(saveFolder, "stale-rich", richBytes, out var descriptor, out var protectFailure),
                protectFailure);
            var stale = new FishWarehouseReplayCheckpoint("stale-rich", folderKey, FishWarehouseReplayPhase.Captured, descriptor.RelativePath, descriptor.Sha256, 1, 1);
            var owned = FishWarehouseSaveState.OwnedState() with { Replay = stale };
            Assert.True(harness.Store.TryWrite(saveFolder, owned, out var sidecarFailure), sidecarFailure);
            Assert.True(harness.Persistence.PrimeStateForLoad());
            Assert.True(harness.Lifecycle.Begin(harness.Persistence.CurrentState!.Replay!, out var beginFailure), beginFailure);

            var nativePath = WriteNativeBytes(saveFolder, EmptierObjectsJson);
            var result = harness.Capture.TryCaptureAgainstStaleCheckpoint(saveFolder, "fresh-from-restore", stale, typedPayloadRetained: true);

            Assert.Equal(FishWarehousePropertyCaptureState.Captured, result.State);
            Assert.Equal("fresh-from-restore", harness.Lifecycle.CurrentCheckpoint!.GenerationId);
            Assert.Equal(1, harness.Lifecycle.CurrentCheckpoint.CapturedObjectCount);
            Assert.Equal(1, harness.Lifecycle.CurrentCheckpoint.CapturedEmployeeCount);
            Assert.Equal(richBytes, File.ReadAllBytes(nativePath));
            Assert.Single(harness.Capture.CapturedEmployeeRecords);
            Assert.Contains(harness.Logs, line => line.Contains("restored from the stale snapshot", StringComparison.Ordinal));
            Assert.Contains(harness.Logs, line => line.Contains("superseded a stale Captured", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Fresh_capture_with_fewer_employees_and_no_stale_snapshot_fails_closed()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // Nothing to restore from: the thinner capture must not steal
            // the lifecycle, and saves stay blocked behind RecoveryRequired.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            var harness = CreateHarness(saveFolder);
            var missingSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RichNativeJson)));
            var stale = new FishWarehouseReplayCheckpoint("stale-unreadable", folderKey, FishWarehouseReplayPhase.Captured, "OrganizedCrime/fish-warehouse.snapshot.json", missingSha, 1, 1);
            Assert.True(harness.Lifecycle.Begin(stale, out var beginFailure), beginFailure);

            WriteNativeBytes(saveFolder, EmptierEmployeesJson);
            var result = harness.Capture.TryCaptureAgainstStaleCheckpoint(saveFolder, "fresh-thinner", stale, typedPayloadRetained: true);

            Assert.Equal(FishWarehousePropertyCaptureState.RecoveryRequired, result.State);
            Assert.Contains("stale objects 1, employees 1", result.FailureReason, StringComparison.Ordinal);
            Assert.Contains("fresh objects 1, employees 0", result.FailureReason, StringComparison.Ordinal);
            Assert.Same(stale, harness.Lifecycle.CurrentCheckpoint);
            Assert.DoesNotContain(harness.Logs, line => line.Contains("superseded", StringComparison.Ordinal));
            Assert.Contains(harness.Logs, line => line.Contains("refused to supersede", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Fresh_capture_with_more_content_supersedes_the_stale_checkpoint()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // The player added items and the save protected correctly: the
            // richer fresh capture replaces the stale generation as before.
            var folderKey = FishWarehouseSaveFolderKey.Create(saveFolder);
            WriteStuckSidecar(saveFolder, folderKey, FishWarehouseReplayPhase.Captured);
            var harness = CreateHarness(saveFolder);
            Assert.True(harness.Persistence.PrimeStateForLoad());
            var stale = harness.Persistence.CurrentState!.Replay!;
            Assert.True(harness.Lifecycle.Begin(stale, out var beginFailure), beginFailure);

            WriteNativeBytes(saveFolder, RicherObjectsJson);
            var result = harness.Capture.TryCaptureAgainstStaleCheckpoint(saveFolder, "fresh-richer", stale, typedPayloadRetained: true);

            Assert.Equal(FishWarehousePropertyCaptureState.Captured, result.State);
            Assert.Equal("fresh-richer", harness.Lifecycle.CurrentCheckpoint!.GenerationId);
            Assert.Equal(2, harness.Lifecycle.CurrentCheckpoint.CapturedObjectCount);
            Assert.Equal(0, harness.Lifecycle.CurrentCheckpoint.CapturedEmployeeCount);
            Assert.Contains(harness.Logs, line => line.Contains("superseded a stale Captured", StringComparison.Ordinal));
            Assert.DoesNotContain(harness.Logs, line => line.Contains("restored from the stale snapshot", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Blocked_save_rewrites_sidecar_and_refreshes_snapshot_after_wipe()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            // OC-128 reload 2: the save drops every file OC does not rewrite.
            // A blocked save must still restore the native file, refresh the
            // snapshot, and rewrite the sidecar from last-known-good.
            var store = new FishWarehouseSaveStore();
            var owned = FishWarehouseSaveState.OwnedState() with { EmployeeHomePlaced = true };
            Assert.True(store.TryWrite(saveFolder, owned, out var primeFailure), primeFailure);
            var harness = CreateHarness(saveFolder);
            Assert.True(harness.Persistence.PrimeStateForLoad());

            var protectedBytes = Encoding.UTF8.GetBytes("{\"protected\":true}");
            Assert.True(
                harness.SnapshotStore.TryProtect(saveFolder, "generation-wipe", protectedBytes, out var descriptor, out var protectFailure),
                protectFailure);
            var failed = new FishWarehouseReplayCheckpoint(
                "generation-wipe",
                FishWarehouseSaveFolderKey.Create(saveFolder),
                FishWarehouseReplayPhase.Failed,
                descriptor.RelativePath,
                descriptor.Sha256,
                1,
                0) with { FailurePhase = "Captured", FailureReason = "boom" };
            Assert.True(harness.Lifecycle.Begin(failed, out var beginFailure), beginFailure);

            Assert.True(
                FishWarehouseSavePath.TryResolveNativePropertyPath(saveFolder, RuntimePropertyDefinition.FishWarehouse, out var nativePath, out var nativeFailure),
                nativeFailure);
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            File.WriteAllBytes(nativePath, Encoding.UTF8.GetBytes("{\"live\":true}"));
            Assert.True(
                FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out var snapshotPath, out var snapshotFailure),
                snapshotFailure);
            File.Delete(SidecarPath(saveFolder));
            File.Delete(snapshotPath);

            harness.Persistence.HandleSaveComplete();

            Assert.True(store.TryRead(saveFolder, out var rewritten, out var readFailure), readFailure);
            Assert.True(rewritten.Owned);
            Assert.True(rewritten.EmployeeHomePlaced);
            Assert.Equal(FishWarehouseReplayPhase.Failed, rewritten.Replay!.Phase);
            Assert.Equal("generation-wipe", rewritten.Replay.GenerationId);
            Assert.Equal(protectedBytes, File.ReadAllBytes(snapshotPath));
            Assert.Equal(protectedBytes, File.ReadAllBytes(nativePath));
            Assert.Contains(harness.Logs, line => line.Contains("sidecar rewritten while snapshot protection is active", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    private static void WriteStuckSidecar(string saveFolder, string folderKey, FishWarehouseReplayPhase phase)
    {
        Assert.True(FishWarehouseSaveCodec.TryDeserialize(StuckSidecarJson, out var stuck, out var codecFailure), codecFailure);
        var rewritten = stuck with
        {
            Replay = stuck.Replay! with
            {
                Phase = phase,
                SaveFolderKey = folderKey,
                FailurePhase = phase == FishWarehouseReplayPhase.Failed ? stuck.Replay!.FailurePhase : null,
                FailureReason = phase == FishWarehouseReplayPhase.Failed ? stuck.Replay!.FailureReason : null,
            },
        };
        Assert.True(new FishWarehouseSaveStore().TryWrite(saveFolder, rewritten, out var writeFailure), writeFailure);
    }

    private static FishWarehouseReplayCheckpoint ProtectFreshBytes(Harness harness, string saveFolder, string generationId)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"fresh\":true}");
        Assert.True(
            harness.SnapshotStore.TryProtect(saveFolder, generationId, bytes, out var descriptor, out var protectFailure),
            protectFailure);
        return new FishWarehouseReplayCheckpoint(
            generationId,
            FishWarehouseSaveFolderKey.Create(saveFolder),
            FishWarehouseReplayPhase.Captured,
            descriptor.RelativePath,
            descriptor.Sha256,
            1,
            0);
    }

    private static Harness CreateHarness(string saveFolder)
    {
        var store = new FishWarehouseSaveStore();
        var snapshotStore = new FishWarehousePropertySnapshotStore();
        var lifecycle = new FishWarehouseReplayLifecycle(_ => new FishWarehouseCaptureRecoveryPrerequisites(false, false, false, false));
        var logs = new List<string>();
        var persistence = new FishWarehousePersistenceService(
            store,
            () => saveFolder,
            FishWarehouseSaveState.UnownedState,
            _ => { },
            logs.Add,
            snapshotStore,
            lifecycle);
        var capture = new FishWarehousePropertyCaptureService(
            snapshotStore,
            new FishWarehouseNativePropertySnapshotInspector(),
            persistence,
            lifecycle,
            logs.Add);
        return new Harness(store, snapshotStore, lifecycle, persistence, capture, logs);
    }

    private static string SidecarPath(string saveFolder)
    {
        Assert.True(FishWarehouseSavePath.TryResolveSidecarPath(saveFolder, out var sidecarPath, out var failure), failure);
        return sidecarPath;
    }

    private static string WriteNativeBytes(string saveFolder, string json)
    {
        Assert.True(FishWarehouseSavePath.TryResolveNativePropertyPath(saveFolder, RuntimePropertyDefinition.FishWarehouse, out var nativePath, out var failure), failure);
        Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
        File.WriteAllBytes(nativePath, Encoding.UTF8.GetBytes(json));
        return nativePath;
    }

    private static string CreateTemporaryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"oc-recovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed record Harness(
        FishWarehouseSaveStore Store,
        FishWarehousePropertySnapshotStore SnapshotStore,
        FishWarehouseReplayLifecycle Lifecycle,
        FishWarehousePersistenceService Persistence,
        FishWarehousePropertyCaptureService Capture,
        List<string> Logs);

    // Minimal native property docs in the measured (unwrapped) shape the
    // inspector tests pin. Synthetic GUIDs; only the counts vary.
    private const string RichNativeJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"GUID":"rich-locker-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":2,"ItemString":{"ID":"locker"}}
          ],
          "Employees": [
            {"DataType":"PackagerData","BaseData":{"GUID":"rich-packager-guid","Identity":"Packager","PaidForToday":false,"BedGUID":"rich-locker-guid"},"AdditionalDatas":[{"Name":"Configuration","Contents":{"StationGUIDs":[]}}]}
          ]
        }
        """;

    private const string EmptierObjectsJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [],
          "Employees": [
            {"DataType":"PackagerData","BaseData":{"GUID":"rich-packager-guid","Identity":"Packager","PaidForToday":false,"BedGUID":"rich-locker-guid"},"AdditionalDatas":[{"Name":"Configuration","Contents":{"StationGUIDs":[]}}]}
          ]
        }
        """;

    private const string EmptierEmployeesJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"GUID":"rich-locker-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":2,"ItemString":{"ID":"locker"}}
          ],
          "Employees": []
        }
        """;

    private const string RicherObjectsJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"GUID":"rich-locker-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":2,"ItemString":{"ID":"locker"}},
            {"GUID":"rich-packaging-station-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":5,"ItemString":{"ID":"packagingstation"}}
          ],
          "Employees": []
        }
        """;

    // Real bytes of SaveGame_5.oc108-stuck-replay-failed-20260926 sidecar, a
    // replay that never completed. Tests rewrite the saveFolderKey (and the
    // phase for the interrupted-Captured trigger state) for the temp folder.
    private const string StuckSidecarJson = """
        {
          "schemaVersion": 2,
          "propertyCode": "oc_fishwarehouse",
          "unlocked": true,
          "owned": true,
          "employeeHomePlaced": true,
          "employeeAgentTypeId": -1923039037,
          "replay": {
            "generationId": "b22fa3132e9b41818ffe3cf782870d1d",
            "saveFolderKey": "4682256A03DCDD46F87A38249C0D512DF385D711EED5D84C09109CB00479B724",
            "phase": 8,
            "snapshotRelativePath": "OrganizedCrime/fish-warehouse.snapshot.json",
            "snapshotSha256": "8F17AA01940793A2F3FACAFB6EB62B9E7DFE999B462031B6244F9D5EE91D732C",
            "capturedObjectCount": 1,
            "capturedEmployeeCount": 0,
            "replayedObjectCount": 0,
            "replayedEmployeeCount": 0,
            "failurePhase": "Captured",
            "failureReason": "Fish Warehouse recovered capture checkpoint persistence failed.",
            "inventoryRestoredEmployeeGuids": null
          },
          "unsupportedEmployees": []
        }
        """;
}
