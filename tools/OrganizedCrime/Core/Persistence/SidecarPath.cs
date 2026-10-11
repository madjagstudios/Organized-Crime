namespace OrganizedCrime.Core.Persistence;

/// <summary>
/// The per-service failure-message wording for sidecar path validation.
/// Short is the Chapter 3 / Release 1 Story wording ("Active save folder");
/// ScheduleI is the Local Pressure wording ("Active Schedule I save folder",
/// with the resolved path on a miss and a prefix on a resolution failure).
/// </summary>
public enum SidecarPathWording
{
    Short,
    ScheduleI
}

/// <summary>Why sidecar path validation failed. Never interpreted by Core callers beyond mapping.</summary>
public enum SidecarPathFailureReason
{
    None,
    Empty,
    Missing,
    Unresolvable
}

/// <summary>A sidecar path validation failure: the reason plus the exact service-worded message.</summary>
public readonly record struct SidecarPathFailure(SidecarPathFailureReason Reason, string Message);

/// <summary>
/// A validated sidecar location: the full save-folder path plus the
/// OrganizedCrime directory and file beneath it. Instances come only from
/// <see cref="TryValidate"/>; the per-service SavePath facades hold one.
/// </summary>
public sealed class SidecarPath
{
    private SidecarPath(string activeSaveFolder, string sidecarDirectory, string sidecarFilePath)
    {
        ActiveSaveFolder = activeSaveFolder;
        SidecarDirectory = sidecarDirectory;
        SidecarFilePath = sidecarFilePath;
    }

    public string ActiveSaveFolder { get; }
    public string SidecarDirectory { get; }
    public string SidecarFilePath { get; }

    /// <summary>
    /// Validates an active save folder exactly like the three per-service
    /// SavePath types always have: blank is Empty, a missing folder is
    /// Missing, a GetFullPath failure is Unresolvable (only ArgumentException,
    /// IOException, UnauthorizedAccessException and NotSupportedException are
    /// caught; anything else propagates).
    /// </summary>
    public static bool TryValidate(
        string? folder,
        string fileName,
        SidecarPathWording wording,
        out SidecarPath? path,
        out SidecarPathFailure failure)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        path = null;
        if (string.IsNullOrWhiteSpace(folder))
        {
            failure = wording == SidecarPathWording.ScheduleI
                ? new(SidecarPathFailureReason.Empty, "Active Schedule I save folder was empty.")
                : new(SidecarPathFailureReason.Empty, "Active save folder was empty.");
            return false;
        }

        try
        {
            var full = Path.GetFullPath(folder.Trim());
            if (!Directory.Exists(full))
            {
                failure = wording == SidecarPathWording.ScheduleI
                    ? new(SidecarPathFailureReason.Missing, $"Active Schedule I save folder did not exist: {full}")
                    : new(SidecarPathFailureReason.Missing, "Active save folder did not exist.");
                return false;
            }

            var directory = Path.Combine(full, "OrganizedCrime");
            path = new SidecarPath(full, directory, Path.Combine(directory, fileName));
            failure = new(SidecarPathFailureReason.None, string.Empty);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failure = wording == SidecarPathWording.ScheduleI
                ? new(SidecarPathFailureReason.Unresolvable, $"Active Schedule I save folder could not be resolved: {ex.Message}")
                : new(SidecarPathFailureReason.Unresolvable, ex.Message);
            return false;
        }
    }
}
