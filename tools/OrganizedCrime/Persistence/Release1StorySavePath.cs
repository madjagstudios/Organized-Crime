namespace OrganizedCrime.Persistence;

public enum Release1StorySavePathFailureReason { None, ActiveSaveFolderMissing, InvalidActiveSaveFolder }
public sealed record Release1StorySavePathResult(bool Succeeded, Release1StorySavePathFailureReason Reason, string Message)
{
    public static Release1StorySavePathResult Success() => new(true, Release1StorySavePathFailureReason.None, string.Empty);
    public static Release1StorySavePathResult Failure(Release1StorySavePathFailureReason reason, string message) => new(false, reason, message);
}
public sealed class Release1StorySavePath
{
    private Release1StorySavePath(string folder) { ActiveSaveFolder = folder; SidecarDirectory = Path.Combine(folder, "OrganizedCrime"); SidecarFilePath = Path.Combine(SidecarDirectory, "release1-story.json"); }
    public string ActiveSaveFolder { get; }
    public string SidecarDirectory { get; }
    public string SidecarFilePath { get; }
    public static bool TryCreate(string? folder, out Release1StorySavePath? path, out Release1StorySavePathResult result)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(folder)) { result = Release1StorySavePathResult.Failure(Release1StorySavePathFailureReason.InvalidActiveSaveFolder, "Active save folder was empty."); return false; }
        try { var full = Path.GetFullPath(folder.Trim()); if (!Directory.Exists(full)) { result = Release1StorySavePathResult.Failure(Release1StorySavePathFailureReason.ActiveSaveFolderMissing, "Active save folder did not exist."); return false; } path = new Release1StorySavePath(full); result = Release1StorySavePathResult.Success(); return true; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { result = Release1StorySavePathResult.Failure(Release1StorySavePathFailureReason.InvalidActiveSaveFolder, ex.Message); return false; }
    }
}
