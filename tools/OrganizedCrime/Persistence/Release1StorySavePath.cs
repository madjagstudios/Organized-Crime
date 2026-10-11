using OrganizedCrime.Core.Persistence;

namespace OrganizedCrime.Persistence;

public enum Release1StorySavePathFailureReason { None, ActiveSaveFolderMissing, InvalidActiveSaveFolder }
public sealed record Release1StorySavePathResult(bool Succeeded, Release1StorySavePathFailureReason Reason, string Message)
{
    public static Release1StorySavePathResult Success() => new(true, Release1StorySavePathFailureReason.None, string.Empty);
    public static Release1StorySavePathResult Failure(Release1StorySavePathFailureReason reason, string message) => new(false, reason, message);
}
public sealed class Release1StorySavePath
{
    private Release1StorySavePath(SidecarPath inner) { Inner = inner; }
    public string ActiveSaveFolder => Inner.ActiveSaveFolder;
    public string SidecarDirectory => Inner.SidecarDirectory;
    public string SidecarFilePath => Inner.SidecarFilePath;
    internal SidecarPath Inner { get; }
    public static bool TryCreate(string? folder, out Release1StorySavePath? path, out Release1StorySavePathResult result)
    {
        path = null;
        if (!SidecarPath.TryValidate(folder, "release1-story.json", SidecarPathWording.Short, out var validated, out var failure))
        {
            result = Release1StorySavePathResult.Failure(Map(failure.Reason), failure.Message);
            return false;
        }
        path = new Release1StorySavePath(validated!);
        result = Release1StorySavePathResult.Success();
        return true;
    }
    private static Release1StorySavePathFailureReason Map(SidecarPathFailureReason reason) => reason switch
    {
        SidecarPathFailureReason.Missing => Release1StorySavePathFailureReason.ActiveSaveFolderMissing,
        SidecarPathFailureReason.Empty or SidecarPathFailureReason.Unresolvable => Release1StorySavePathFailureReason.InvalidActiveSaveFolder,
        _ => Release1StorySavePathFailureReason.None,
    };
}
