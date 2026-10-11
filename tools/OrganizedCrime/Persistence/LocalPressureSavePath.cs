using OrganizedCrime.Core.Persistence;

namespace OrganizedCrime.Persistence;

public enum LocalPressureSavePathFailureReason
{
    None,
    ActiveSaveFolderMissing,
    InvalidActiveSaveFolder
}

public sealed record LocalPressureSavePathResult(bool Succeeded, LocalPressureSavePathFailureReason Reason, string Message)
{
    public static LocalPressureSavePathResult Success() =>
        new(true, LocalPressureSavePathFailureReason.None, string.Empty);

    public static LocalPressureSavePathResult Failure(LocalPressureSavePathFailureReason reason, string message) =>
        new(false, reason, message);
}

public sealed class LocalPressureSavePath
{
    private LocalPressureSavePath(SidecarPath inner)
    {
        Inner = inner;
    }

    public string ActiveSaveFolder => Inner.ActiveSaveFolder;
    public string SidecarDirectory => Inner.SidecarDirectory;
    public string SidecarFilePath => Inner.SidecarFilePath;
    internal SidecarPath Inner { get; }

    public static bool TryCreate(
        string? folder,
        out LocalPressureSavePath? path,
        out LocalPressureSavePathResult result)
    {
        path = null;
        if (!SidecarPath.TryValidate(folder, "local-pressure.json", SidecarPathWording.ScheduleI, out var validated, out var failure))
        {
            result = LocalPressureSavePathResult.Failure(Map(failure.Reason), failure.Message);
            return false;
        }

        path = new LocalPressureSavePath(validated!);
        result = LocalPressureSavePathResult.Success();
        return true;
    }

    private static LocalPressureSavePathFailureReason Map(SidecarPathFailureReason reason) => reason switch
    {
        SidecarPathFailureReason.Missing => LocalPressureSavePathFailureReason.ActiveSaveFolderMissing,
        SidecarPathFailureReason.Empty or SidecarPathFailureReason.Unresolvable => LocalPressureSavePathFailureReason.InvalidActiveSaveFolder,
        _ => LocalPressureSavePathFailureReason.None,
    };
}
