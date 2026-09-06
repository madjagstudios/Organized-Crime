namespace OrganizedCrime.Persistence;

public sealed record FishWarehouseProtectedSnapshot(
    string GenerationId,
    string SaveFolderKey,
    string RelativePath,
    string Sha256,
    int ByteCount);

public interface IFishWarehousePropertySnapshotStore
{
    bool TryProtect(string saveFolder, string generationId, byte[] bytes,
        out FishWarehouseProtectedSnapshot snapshot, out string? failureReason);
    bool TryRead(string saveFolder, FishWarehouseProtectedSnapshot snapshot,
        out byte[] bytes, out string? failureReason);
    bool TryRestoreNativeProperty(string saveFolder, FishWarehouseProtectedSnapshot snapshot,
        out string? failureReason);
}
