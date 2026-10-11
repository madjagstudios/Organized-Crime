using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-129: previously preserved employees whose type the replay now
// supports are restored through fallback creation, while still-
// unsupported records are kept. GUID dedupe: a GUID the native file
// already has replays from the native record; the stale copy is consumed.
public sealed class FishWarehousePreservedEmployeeRestoreTests
{
    [Fact]
    public void Supported_preserved_records_restore_while_still_unsupported_are_kept()
    {
        var preserved = new[]
        {
            new FishWarehouseUnsupportedEmployeeRecord("chemist-guid", "ChemistData", "Chemist", "{}"),
            new FishWarehouseUnsupportedEmployeeRecord("handler-guid", "HandlerData", "Handler", "{}"),
        };
        var native = new[]
        {
            new FishWarehouseNativePropertyEmployeeSnapshot(
                "packager-guid", "PackagerData", "Packager", false, "locker-guid",
                Array.Empty<string>(), "{}"),
        };

        var nativeDescriptors = FishWarehousePropertyCaptureService.CreateEmployeeReplayDescriptors(native);
        var restore = FishWarehousePropertyCaptureService.CreatePreservedEmployeeReplayDescriptors(
            preserved,
            nativeDescriptors,
            out var kept);

        Assert.Single(restore);
        Assert.Equal("chemist-guid", restore[0].Guid);
        Assert.True(restore[0].RequiresFallbackCreation);
        Assert.True(restore[0].IsSupportedPackager);
        Assert.Single(kept);
        Assert.Equal("handler-guid", kept[0].Guid);
    }

    [Fact]
    public void Native_guid_wins_over_stale_preserved_copy()
    {
        var preserved = new[]
        {
            new FishWarehouseUnsupportedEmployeeRecord("shared-guid", "ChemistData", "Chemist", "{}"),
        };
        var native = new[]
        {
            new FishWarehouseNativePropertyEmployeeSnapshot(
                "shared-guid", "ChemistData", "Chemist", false, "locker-guid",
                Array.Empty<string>(), "{}"),
        };

        var nativeDescriptors = FishWarehousePropertyCaptureService.CreateEmployeeReplayDescriptors(native);
        var restore = FishWarehousePropertyCaptureService.CreatePreservedEmployeeReplayDescriptors(
            preserved,
            nativeDescriptors,
            out var kept);

        Assert.Empty(restore);
        Assert.Empty(kept);
    }

    [Fact]
    public void Replace_after_successful_replay_drops_only_replayed_records()
    {
        var saveFolder = CreateTemporaryFolder();
        try
        {
            var store = new FishWarehouseSaveStore();
            var snapshotStore = new FishWarehousePropertySnapshotStore();
            var lifecycle = new FishWarehouseReplayLifecycle(_ => new FishWarehouseCaptureRecoveryPrerequisites(false, false, false, false));
            var persistence = new FishWarehousePersistenceService(
                store,
                () => saveFolder,
                FishWarehouseSaveState.UnownedState,
                _ => { },
                _ => { },
                snapshotStore,
                lifecycle);

            var chemist = new FishWarehouseUnsupportedEmployeeRecord("chemist-guid", "ChemistData", "Chemist", "{}");
            var handler = new FishWarehouseUnsupportedEmployeeRecord("handler-guid", "HandlerData", "Handler", "{}");
            var owned = FishWarehouseSaveState.OwnedState() with
            {
                UnsupportedEmployees = new[] { chemist, handler },
            };
            Assert.True(store.TryWrite(saveFolder, owned, out var writeFailure), writeFailure);
            Assert.True(persistence.PrimeStateForLoad());

            // The replay put the chemist back; only the still-unsupported
            // handler remains in the result list.
            Assert.True(persistence.TryReplaceUnsupportedEmployees(new[] { handler }));

            Assert.True(store.TryRead(saveFolder, out var reloaded, out var readFailure), readFailure);
            Assert.NotNull(reloaded);
            Assert.Single(reloaded!.UnsupportedEmployees);
            Assert.Equal("handler-guid", reloaded.UnsupportedEmployees[0].Guid);
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    private static string CreateTemporaryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"oc-restore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
