namespace OrganizedCrime.Core.Persistence;

/// <summary>
/// The injectable file-system boundary behind every sidecar store: read,
/// flush/write, atomic replacement, and temp cleanup. The three per-service
/// interfaces are identical to this one and extend it; tests fake this.
/// </summary>
public interface ISidecarFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void CreateDirectory(string path);
    string CreateTemporaryPath(string directory, string targetPath);
    void WriteAllTextAndFlush(string path, string contents);
    void ReplaceAtomically(string temporaryPath, string targetPath);
    void DeleteFile(string path);
}
