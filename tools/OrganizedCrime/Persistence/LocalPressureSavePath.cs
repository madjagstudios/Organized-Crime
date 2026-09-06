namespace OrganizedCrime.Persistence;

public enum LocalPressureSavePathFailureReason
{
    None,
    ActiveSaveFolderMissing,
    InvalidActiveSaveFolder
}

public sealed record LocalPressureSavePathResult(
    bool Succeeded,
    LocalPressureSavePathFailureReason Reason,
    string Message)
{
    public static LocalPressureSavePathResult Success() =>
        new(true, LocalPressureSavePathFailureReason.None, string.Empty);

    public static LocalPressureSavePathResult Failure(LocalPressureSavePathFailureReason reason, string message) =>
        new(false, reason, message);
}

public sealed class LocalPressureSavePath
{
    private LocalPressureSavePath(string activeSaveFolder)
    {
        ActiveSaveFolder = activeSaveFolder;
        SidecarDirectory = Path.Combine(activeSaveFolder, "OrganizedCrime");
        SidecarFilePath = Path.Combine(SidecarDirectory, "local-pressure.json");
    }

    public string ActiveSaveFolder { get; }
    public string SidecarDirectory { get; }
    public string SidecarFilePath { get; }

    public static bool TryCreate(
        string? activeSaveFolder,
        out LocalPressureSavePath? path,
        out LocalPressureSavePathResult result)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
        {
            result = LocalPressureSavePathResult.Failure(
                LocalPressureSavePathFailureReason.InvalidActiveSaveFolder,
                "Active Schedule I save folder was empty.");
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(activeSaveFolder.Trim());
            if (!Directory.Exists(fullPath))
            {
                result = LocalPressureSavePathResult.Failure(
                    LocalPressureSavePathFailureReason.ActiveSaveFolderMissing,
                    $"Active Schedule I save folder did not exist: {fullPath}");
                return false;
            }

            path = new LocalPressureSavePath(fullPath);
            result = LocalPressureSavePathResult.Success();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            result = LocalPressureSavePathResult.Failure(
                LocalPressureSavePathFailureReason.InvalidActiveSaveFolder,
                $"Active Schedule I save folder could not be resolved: {ex.Message}");
            return false;
        }
    }
}
