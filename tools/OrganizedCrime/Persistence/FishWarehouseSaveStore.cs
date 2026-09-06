namespace OrganizedCrime.Persistence;

public interface IFishWarehouseSaveStore
{
    bool TryRead(string saveFolder, out FishWarehouseSaveState state, out string? failureReason);
    bool TryWrite(string saveFolder, FishWarehouseSaveState state, out string? failureReason);
}

public sealed class FishWarehouseSaveStore : IFishWarehouseSaveStore
{
    public bool TryRead(
        string saveFolder,
        out FishWarehouseSaveState state,
        out string? failureReason)
    {
        state = null!;
        failureReason = null;
        if (!FishWarehouseSavePath.TryResolveSidecarPath(saveFolder, out var sidecarPath, out failureReason))
            return false;

        if (!File.Exists(sidecarPath))
        {
            failureReason = $"Fish Warehouse sidecar was not found: {sidecarPath}";
            return false;
        }

        try
        {
            var json = File.ReadAllText(sidecarPath);
            return FishWarehouseSaveCodec.TryDeserialize(json, out state, out failureReason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = $"Fish Warehouse sidecar could not be read: {ex.Message}";
            return false;
        }
    }

    public bool TryWrite(
        string saveFolder,
        FishWarehouseSaveState state,
        out string? failureReason)
    {
        failureReason = null;
        if (!FishWarehouseSavePath.TryResolveSidecarPath(saveFolder, out var sidecarPath, out failureReason))
            return false;

        var directory = Path.GetDirectoryName(sidecarPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            failureReason = "Fish Warehouse sidecar directory could not be resolved.";
            return false;
        }

        var temporaryPath = $"{sidecarPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, FishWarehouseSaveCodec.Serialize(state));
            File.Move(temporaryPath, sidecarPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            failureReason = $"Fish Warehouse sidecar could not be written: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // Preserve the original write failure; a leftover temp file is harmless and diagnosable.
            }
        }
    }
}
