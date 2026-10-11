using System.Globalization;
using System.Security.Cryptography;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public sealed class FishWarehousePropertySnapshotStore : IFishWarehousePropertySnapshotStore
{
    private const string SnapshotRelativePath = "OrganizedCrime/fish-warehouse.snapshot.json";

    // The game drops save-folder files OC does not rewrite during a save, so
    // the snapshot file may be gone by save-complete. The last protected
    // bytes stay in memory (defensive copy) so snapshot protection can still
    // restore and refresh after the wipe.
    private byte[]? _lastProtectedBytes;
    private FishWarehouseProtectedSnapshot? _lastProtectedDescriptor;
    private const string FailedInspectionFilePrefix = "fish-warehouse.failed-inspection.";
    private const string FailedInspectionFileSuffix = ".json";
    private const int FailedInspectionRetentionCount = 3;

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
            _lastProtectedBytes = (byte[])bytes.Clone();
            _lastProtectedDescriptor = snapshot;
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
            if (snapshot.ByteCount > 0 && bytes.Length != snapshot.ByteCount)
            {
                failureReason = "Fish Warehouse protected snapshot byte count did not match its descriptor.";
                bytes = Array.Empty<byte>();
                return false;
            }

            // A zero byte count means unknown (the replay checkpoint carries
            // no length): the SHA-256 check below still fully validates.
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
        // Disk-only on purpose: a corrupted or foreign snapshot must refuse
        // here. The in-memory fallback lives in TryReadRestorableBytes and is
        // only consulted by the blocked-save refresh path.
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

    public bool TryPreserveFailedInspection(
        string saveFolder,
        byte[] bytes,
        out string preservedPath,
        out string? failureReason)
    {
        preservedPath = string.Empty;
        failureReason = null;
        if (bytes is null || bytes.Length == 0)
        {
            failureReason = "Fish Warehouse failed-inspection bytes were unavailable for preservation.";
            return false;
        }

        if (!FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out var snapshotPath, out failureReason))
            return false;

        var directory = Path.GetDirectoryName(snapshotPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            failureReason = "Fish Warehouse failed-inspection directory could not be resolved.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var candidate = Path.Combine(directory, $"{FailedInspectionFilePrefix}{stamp}{FailedInspectionFileSuffix}");
            for (var duplicate = 2; duplicate < 100 && File.Exists(candidate); duplicate++)
                candidate = Path.Combine(directory, $"{FailedInspectionFilePrefix}{stamp}-{duplicate}{FailedInspectionFileSuffix}");
            WriteAtomically(candidate, bytes);
            preservedPath = candidate;
            PruneFailedInspections(directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse failed-inspection file could not be preserved: {ex.Message}";
            return false;
        }
    }

    private static void PruneFailedInspections(string directory)
    {
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(directory, $"{FailedInspectionFilePrefix}*{FailedInspectionFileSuffix}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"Fish Warehouse failed-inspection files could not be enumerated: {ex.Message}", ex);
        }

        // Timestamp-sortable names: ordinal-descending keeps the newest.
        Array.Sort(candidates, StringComparer.Ordinal);
        for (var index = 0; index + FailedInspectionRetentionCount < candidates.Length; index++)
        {
            try
            {
                File.Delete(candidates[index]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new IOException($"Fish Warehouse superseded failed-inspection file could not be deleted: {ex.Message}", ex);
            }
        }
    }

    public bool TryReadRestorableBytes(
        string saveFolder,
        FishWarehouseProtectedSnapshot snapshot,
        out byte[] bytes,
        out string? failureReason)
    {
        // Validate the descriptor against the folder first: the retained
        // bytes must never satisfy a cross-folder or malformed request.
        if (!TryValidateSnapshot(saveFolder, snapshot, out _, out failureReason))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        if (_lastProtectedBytes is not null &&
            _lastProtectedDescriptor is not null &&
            string.Equals(_lastProtectedDescriptor.Sha256, snapshot.Sha256, StringComparison.Ordinal) &&
            string.Equals(_lastProtectedDescriptor.SaveFolderKey, snapshot.SaveFolderKey, StringComparison.Ordinal))
        {
            bytes = (byte[])_lastProtectedBytes.Clone();
            failureReason = null;
            return true;
        }

        return TryRead(saveFolder, snapshot, out bytes, out failureReason);
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
