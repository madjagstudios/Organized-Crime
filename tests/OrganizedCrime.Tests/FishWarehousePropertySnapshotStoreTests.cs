using System.Security.Cryptography;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePropertySnapshotStoreTests
{
    [Fact]
    public void Protect_and_read_preserve_native_bytes_and_sha256_exactly()
    {
        var saveFolder = CreateTemporaryFolder();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'\r', (byte)'\n', 0x00, 0xFF, (byte)'}', (byte)'\n' };
        var store = new FishWarehousePropertySnapshotStore();

        try
        {
            Assert.True(store.TryProtect(saveFolder, "generation-1", bytes, out var snapshot, out var protectFailure), protectFailure);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.Sha256);
            Assert.Equal(bytes.Length, snapshot.ByteCount);

            Assert.True(store.TryRead(saveFolder, snapshot, out var actual, out var readFailure), readFailure);
            Assert.Equal(bytes, actual);
            Assert.Empty(Directory.GetFiles(saveFolder, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Read_and_restore_reject_a_snapshot_for_a_different_save_folder()
    {
        var sourceFolder = CreateTemporaryFolder();
        var otherFolder = CreateTemporaryFolder();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' };
        var store = new FishWarehousePropertySnapshotStore();

        try
        {
            Assert.True(store.TryProtect(sourceFolder, "generation-1", bytes, out var snapshot, out var protectFailure), protectFailure);

            Assert.False(store.TryRead(otherFolder, snapshot, out _, out var readFailure));
            Assert.Contains("folder", readFailure, StringComparison.OrdinalIgnoreCase);
            Assert.False(store.TryRestoreNativeProperty(otherFolder, snapshot, out var restoreFailure));
            Assert.Contains("folder", restoreFailure, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(sourceFolder, recursive: true);
            Directory.Delete(otherFolder, recursive: true);
        }
    }

    [Fact]
    public void Restore_refuses_corrupted_snapshot_without_replacing_native_property()
    {
        var saveFolder = CreateTemporaryFolder();
        var protectedBytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'\r', (byte)'\n', (byte)'}' };
        var currentNativeBytes = new byte[] { (byte)'{', (byte)'"', (byte)'n', (byte)'e', (byte)'w', (byte)'"', (byte)'}' };
        var store = new FishWarehousePropertySnapshotStore();

        try
        {
            Assert.True(store.TryProtect(saveFolder, "generation-1", protectedBytes, out var snapshot, out var protectFailure), protectFailure);
            File.WriteAllBytes(Path.Combine(saveFolder, snapshot.RelativePath), new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });
            var nativePath = Path.Combine(saveFolder, "Properties", "Fish Warehouse.json");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            File.WriteAllBytes(nativePath, currentNativeBytes);

            Assert.False(store.TryRestoreNativeProperty(saveFolder, snapshot, out var restoreFailure));
            Assert.Contains("hash", restoreFailure, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(currentNativeBytes, File.ReadAllBytes(nativePath));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Restore_replaces_only_the_native_fish_warehouse_property_after_key_and_hash_validation()
    {
        var saveFolder = CreateTemporaryFolder();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'\r', (byte)'\n', (byte)'"', (byte)'o', (byte)'l', (byte)'d', (byte)'"', (byte)':', (byte)'1', (byte)'}' };
        var store = new FishWarehousePropertySnapshotStore();

        try
        {
            Assert.True(store.TryProtect(saveFolder, "generation-1", bytes, out var snapshot, out var protectFailure), protectFailure);
            var nativePath = Path.Combine(saveFolder, "Properties", "Fish Warehouse.json");
            var untouchedPath = Path.Combine(saveFolder, "Properties", "Other Property.json");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            File.WriteAllBytes(nativePath, new byte[] { (byte)'n', (byte)'e', (byte)'w' });
            File.WriteAllBytes(untouchedPath, new byte[] { (byte)'k', (byte)'e', (byte)'e', (byte)'p' });

            Assert.True(store.TryRestoreNativeProperty(saveFolder, snapshot, out var restoreFailure), restoreFailure);
            Assert.Equal(bytes, File.ReadAllBytes(nativePath));
            Assert.Equal(new byte[] { (byte)'k', (byte)'e', (byte)'e', (byte)'p' }, File.ReadAllBytes(untouchedPath));
            Assert.Empty(Directory.GetFiles(saveFolder, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    [Fact]
    public void Restore_replaces_the_renamed_native_property_when_the_display_name_file_exists()
    {
        var saveFolder = CreateTemporaryFolder();
        var bytes = new byte[] { (byte)'{', (byte)'"', (byte)'o', (byte)'l', (byte)'d', (byte)'"', (byte)':', (byte)'1', (byte)'}' };
        var store = new FishWarehousePropertySnapshotStore();

        try
        {
            Assert.True(store.TryProtect(saveFolder, "generation-1", bytes, out var snapshot, out var protectFailure), protectFailure);
            var renamedPath = Path.Combine(saveFolder, "Properties", "Syndicate Warehouse.json");
            Directory.CreateDirectory(Path.GetDirectoryName(renamedPath)!);
            File.WriteAllBytes(renamedPath, new byte[] { (byte)'n', (byte)'e', (byte)'w' });

            Assert.True(store.TryRestoreNativeProperty(saveFolder, snapshot, out var restoreFailure), restoreFailure);
            Assert.Equal(bytes, File.ReadAllBytes(renamedPath));
            Assert.False(File.Exists(Path.Combine(saveFolder, "Properties", "Fish Warehouse.json")));
        }
        finally
        {
            Directory.Delete(saveFolder, recursive: true);
        }
    }

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "OrganizedCrimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
