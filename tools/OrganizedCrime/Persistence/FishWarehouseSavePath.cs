using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public static class FishWarehouseSavePath
{
    public static bool TryResolve(
        string? activeSaveFolder,
        out string sidecarPath,
        out string? failureReason)
        => TryResolveSidecarPath(activeSaveFolder, out sidecarPath, out failureReason);

    public static bool TryResolveSidecarPath(
        string? activeSaveFolder,
        out string sidecarPath,
        out string? failureReason)
        => TryResolveUnderSaveFolder(
            activeSaveFolder,
            "OrganizedCrime",
            "fish-warehouse.json",
            out sidecarPath,
            out failureReason);

    public static bool TryResolveSnapshotPath(
        string? activeSaveFolder,
        out string snapshotPath,
        out string? failureReason)
        => TryResolveUnderSaveFolder(
            activeSaveFolder,
            "OrganizedCrime",
            "fish-warehouse.snapshot.json",
            out snapshotPath,
            out failureReason);

    public static bool TryResolveNativePropertyPath(
        string? activeSaveFolder,
        RuntimePropertyDefinition definition,
        out string propertyPath,
        out string? failureReason)
    {
        propertyPath = string.Empty;
        if (definition is null)
        {
            failureReason = "Runtime Property definition was unavailable.";
            return false;
        }

        if (!TryResolveSaveFolder(activeSaveFolder, out var saveFolder, out failureReason))
            return false;

        // Schedule I derives the current file name from the player-facing PropertyName.
        // NativeName remains the pre-OC-16 file name for saves created before the label
        // changed, so an existing current file wins and legacy saves still replay.
        var displayNamePath = Path.Combine(saveFolder, "Properties", $"{definition.DisplayName}.json");
        var legacyNativeNamePath = Path.Combine(saveFolder, "Properties", $"{definition.NativeName}.json");
        propertyPath = File.Exists(displayNamePath)
            ? displayNamePath
            : legacyNativeNamePath;
        return true;
    }

    private static bool TryResolveUnderSaveFolder(
        string? activeSaveFolder,
        string directoryName,
        string fileName,
        out string path,
        out string? failureReason)
    {
        path = string.Empty;
        if (!TryResolveSaveFolder(activeSaveFolder, out var saveFolder, out failureReason))
            return false;

        path = Path.Combine(saveFolder, directoryName, fileName);
        return true;
    }

    private static bool TryResolveSaveFolder(
        string? activeSaveFolder,
        out string saveFolder,
        out string? failureReason)
    {
        saveFolder = string.Empty;
        failureReason = null;
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
        {
            failureReason = "Active Schedule I save folder was unavailable.";
            return false;
        }

        try
        {
            saveFolder = Path.GetFullPath(activeSaveFolder.Trim());
            if (Directory.Exists(saveFolder))
                return true;

            failureReason = $"Active Schedule I save folder did not exist: {saveFolder}";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failureReason = $"Active Schedule I save folder could not be resolved: {ex.Message}";
            return false;
        }
    }
}
