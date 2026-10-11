using OrganizedCrime.Core.Persistence;

namespace OrganizedCrime.Persistence;

public enum Chapter3SavePathFailureReason { None, ActiveSaveFolderMissing, InvalidActiveSaveFolder }
public sealed record Chapter3SavePathResult(bool Succeeded, Chapter3SavePathFailureReason Reason, string Message)
{
    public static Chapter3SavePathResult Success() => new(true, Chapter3SavePathFailureReason.None, string.Empty);
    public static Chapter3SavePathResult Failure(Chapter3SavePathFailureReason reason, string message) => new(false, reason, message);
}
public sealed class Chapter3SavePath
{
    private Chapter3SavePath(SidecarPath inner) { Inner = inner; }
    public string ActiveSaveFolder => Inner.ActiveSaveFolder;
    public string SidecarDirectory => Inner.SidecarDirectory;
    public string SidecarFilePath => Inner.SidecarFilePath;
    internal SidecarPath Inner { get; }
    public static bool TryCreate(string? folder, out Chapter3SavePath? path, out Chapter3SavePathResult result)
    {
        path = null;
        if (!SidecarPath.TryValidate(folder, "chapter3.json", SidecarPathWording.Short, out var validated, out var failure))
        {
            result = Chapter3SavePathResult.Failure(Map(failure.Reason), failure.Message);
            return false;
        }
        path = new Chapter3SavePath(validated!);
        result = Chapter3SavePathResult.Success();
        return true;
    }
    private static Chapter3SavePathFailureReason Map(SidecarPathFailureReason reason) => reason switch
    {
        SidecarPathFailureReason.Missing => Chapter3SavePathFailureReason.ActiveSaveFolderMissing,
        SidecarPathFailureReason.Empty or SidecarPathFailureReason.Unresolvable => Chapter3SavePathFailureReason.InvalidActiveSaveFolder,
        _ => Chapter3SavePathFailureReason.None,
    };
}
