using System.Security.Cryptography;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public sealed class FishWarehousePropertySnapshotStore : IFishWarehousePropertySnapshotStore
{
    private const string SnapshotRelativePath = "OrganizedCrime/fish-warehouse.snapshot.json";

    public bool TryProtect(
        string saveFolder,
        string generationId,
        byte[] bytes,
        out FishWarehouseProtectedSnapshot snapshot,
        out string? failureReason)
    {
        snapshot = null!;
        failureReason = null;
        if (string.IsNullOrWhiteSpace(generationId))
        {
            failureReason = "Fish Warehouse snapshot generation was unavailable.";
            return false;
        }

        if (bytes is null)
        {
            failureReason = "Fish Warehouse snapshot bytes were unavailable.";
            return false;
        }

        if (!FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out var snapshotPath, out failureReason))
            return false;

        try
        {
            WriteAtomically(snapshotPath, bytes);
            snapshot = new FishWarehouseProtectedSnapshot(
                generationId,
                FishWarehouseSaveFolderKey.Create(saveFolder),
                SnapshotRelativePath,
                Convert.ToHexString(SHA256.HashData(bytes)),
                bytes.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse snapshot could not be protected: {ex.Message}";
            return false;
        }
    }

    public bool TryRead(
        string saveFolder,
        FishWarehouseProtectedSnapshot snapshot,
        out byte[] bytes,
        out string? failureReason)
    {
        bytes = Array.Empty<byte>();
        if (!TryValidateSnapshot(saveFolder, snapshot, out var snapshotPath, out failureReason))
            return false;

        try
        {
            bytes = File.ReadAllBytes(snapshotPath);
            if (bytes.Length != snapshot.ByteCount)
            {
                failureReason = "Fish Warehouse protected snapshot byte count did not match its descriptor.";
                bytes = Array.Empty<byte>();
                return false;
            }

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.Sha256, StringComparison.Ordinal))
            {
                failureReason = "Fish Warehouse protected snapshot SHA-256 hash did not match its descriptor.";
                bytes = Array.Empty<byte>();
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = $"Fish Warehouse protected snapshot could not be read: {ex.Message}";
            return false;
        }
    }

    public bool TryRestoreNativeProperty(
        string saveFolder,
        FishWarehouseProtectedSnapshot snapshot,
        out string? failureReason)
    {
        if (!TryRead(saveFolder, snapshot, out var bytes, out failureReason))
            return false;

        if (!TryResolveNativePropertyPath(saveFolder, out var nativePropertyPath, out failureReason))
            return false;

        try
        {
            WriteAtomically(nativePropertyPath, bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse native property snapshot could not be restored: {ex.Message}";
            return false;
        }
    }

    private static bool TryValidateSnapshot(
        string saveFolder,
        FishWarehouseProtectedSnapshot? snapshot,
        out string snapshotPath,
        out string? failureReason)
    {
        snapshotPath = string.Empty;
        failureReason = null;
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.GenerationId) ||
            !string.Equals(snapshot.RelativePath, SnapshotRelativePath, StringComparison.Ordinal) ||
            snapshot.ByteCount < 0 ||
            !IsSha256(snapshot.Sha256))
        {
            failureReason = "Fish Warehouse protected snapshot descriptor was invalid.";
            return false;
        }

        try
        {
            if (!string.Equals(FishWarehouseSaveFolderKey.Create(saveFolder), snapshot.SaveFolderKey, StringComparison.Ordinal))
            {
                failureReason = "Fish Warehouse protected snapshot belongs to a different save folder.";
                return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse save folder key could not be validated: {ex.Message}";
            return false;
        }

        return FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out snapshotPath, out failureReason);
    }

    private static bool TryResolveNativePropertyPath(string saveFolder, out string nativePropertyPath, out string? failureReason)
    {
        nativePropertyPath = string.Empty;
        failureReason = null;
        if (!FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out var snapshotPath, out failureReason))
            return false;

        return FishWarehouseSavePath.TryResolveNativePropertyPath(
            saveFolder,
            RuntimePropertyDefinition.FishWarehouse,
            out nativePropertyPath,
            out failureReason);
    }

    private static void WriteAtomically(string destinationPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("The destination directory could not be resolved.");

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
            return false;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }
}
