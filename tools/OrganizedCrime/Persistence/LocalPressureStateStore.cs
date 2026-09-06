using System.Collections.Concurrent;
using System.Text;

namespace OrganizedCrime.Persistence;

public enum LocalPressureStoreLoadStatus
{
    Loaded,
    Empty,
    Failed
}

public enum LocalPressureStoreFailureReason
{
    None,
    SidecarReadFailed,
    EmptyOrMalformedJson,
    InvalidRoot,
    MissingRequiredField,
    InvalidJsonType,
    InvalidSchemaVersion,
    MissingPlayers,
    UnsupportedSchema,
    InvalidPlayerId,
    DuplicatePlayerId,
    InvalidHeat,
    InvalidGameTime,
    InvalidRevision,
    InvalidContext,
    InvalidCurrentState,
    InvalidIncomingRecord,
    StaleRevision,
    RevisionConflict,
    SerializationFailed,
    DirectoryCreationFailed,
    TemporaryFileWriteFailed,
    AtomicReplacementFailed
}

public enum LocalPressureStoreUpdateStatus
{
    Updated,
    Idempotent,
    Rejected
}

public sealed record LocalPressureStoreLoadResult(
    bool Succeeded,
    LocalPressureStoreLoadStatus Status,
    LocalPressureSaveEnvelope? Envelope,
    LocalPressureStoreFailureReason FailureReason,
    string Message);

public sealed record LocalPressureStoreUpdateResult(
    bool Succeeded,
    LocalPressureStoreUpdateStatus Status,
    LocalPressureSaveEnvelope? Envelope,
    LocalPressureStoreFailureReason FailureReason,
    string Message);

/// <summary>
/// Injectable filesystem boundary for testing read, flush/write, replacement,
/// and cleanup failure behavior without a game or a real save folder.
/// </summary>
public interface ILocalPressureFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void CreateDirectory(string path);
    string CreateTemporaryPath(string directory, string targetPath);
    void WriteAllTextAndFlush(string path, string contents);
    void ReplaceAtomically(string temporaryPath, string targetPath);
    void DeleteFile(string path);
}

public sealed class LocalPressureStateStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILocalPressureFileSystem _fileSystem;

    public LocalPressureStateStore(LocalPressureSavePath savePath, ILocalPressureFileSystem? fileSystem = null)
    {
        SavePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
        _fileSystem = fileSystem ?? new LocalPressureFileSystem();
    }

    public LocalPressureSavePath SavePath { get; }

    public bool TryLoad(out LocalPressureStoreLoadResult result)
    {
        lock (GetLock(SavePath.SidecarFilePath))
        {
            result = TryLoadLocked();
            return result.Succeeded;
        }
    }

    public bool TryUpdate(
        LocalPressurePlayerRecord incoming,
        out LocalPressureStoreUpdateResult result)
    {
        lock (GetLock(SavePath.SidecarFilePath))
        {
            if (!TryValidateIncoming(incoming, out var validationFailure))
            {
                result = new LocalPressureStoreUpdateResult(
                    false,
                    LocalPressureStoreUpdateStatus.Rejected,
                    null,
                    LocalPressureStoreFailureReason.InvalidIncomingRecord,
                    validationFailure.Message);
                return false;
            }

            var load = TryLoadLocked();
            if (!load.Succeeded)
            {
                result = new LocalPressureStoreUpdateResult(
                    false,
                    LocalPressureStoreUpdateStatus.Rejected,
                    null,
                    load.FailureReason,
                    load.Message);
                return false;
            }

            var current = load.Envelope!;
            var existing = current.Players.FirstOrDefault(player =>
                string.Equals(player.PlayerId, incoming.PlayerId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (incoming.Revision < existing.Revision)
                    return Reject(LocalPressureStoreFailureReason.StaleRevision, "Incoming Local Pressure revision was stale.", out result);
                if (incoming.Revision == existing.Revision)
                {
                    if (incoming == existing)
                    {
                        result = new LocalPressureStoreUpdateResult(
                            true,
                            LocalPressureStoreUpdateStatus.Idempotent,
                            current,
                            LocalPressureStoreFailureReason.None,
                            "Incoming Local Pressure update was already stored.");
                        return true;
                    }

                    return Reject(LocalPressureStoreFailureReason.RevisionConflict, "Incoming Local Pressure revision conflicted with stored state.", out result);
                }
            }

            var nextPlayers = current.Players
                .Where(player => !string.Equals(player.PlayerId, incoming.PlayerId, StringComparison.Ordinal))
                .Append(incoming)
                .ToArray();
            var next = new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, nextPlayers);
            if (!LocalPressureSaveCodec.TrySerialize(next, out var json, out var serializeResult))
            {
                result = new LocalPressureStoreUpdateResult(
                    false,
                    LocalPressureStoreUpdateStatus.Rejected,
                    null,
                    LocalPressureStoreFailureReason.SerializationFailed,
                    serializeResult.Message);
                return false;
            }

            if (!TryWriteAtomically(json, out var writeFailure))
            {
                result = new LocalPressureStoreUpdateResult(
                    false,
                    LocalPressureStoreUpdateStatus.Rejected,
                    null,
                    writeFailure.Reason,
                    writeFailure.Message);
                return false;
            }

            result = new LocalPressureStoreUpdateResult(
                true,
                LocalPressureStoreUpdateStatus.Updated,
                next,
                LocalPressureStoreFailureReason.None,
                "Local Pressure update was stored.");
            return true;
        }
    }

    private LocalPressureStoreLoadResult TryLoadLocked()
    {
        if (!_fileSystem.FileExists(SavePath.SidecarFilePath))
        {
            return new LocalPressureStoreLoadResult(
                true,
                LocalPressureStoreLoadStatus.Empty,
                LocalPressureSaveEnvelope.CreateEmpty(),
                LocalPressureStoreFailureReason.None,
                "Local Pressure sidecar was not found; using an empty v1 envelope.");
        }

        string json;
        try
        {
            json = _fileSystem.ReadAllText(SavePath.SidecarFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return FailedLoad(LocalPressureStoreFailureReason.SidecarReadFailed, $"Local Pressure sidecar could not be read: {ex.Message}");
        }

        if (!LocalPressureSaveCodec.TryDeserialize(json, out var envelope, out var decodeResult))
        {
            return FailedLoad(MapCodecFailure(decodeResult.Reason), decodeResult.Message);
        }

        return new LocalPressureStoreLoadResult(
            true,
            LocalPressureStoreLoadStatus.Loaded,
            envelope,
            LocalPressureStoreFailureReason.None,
            "Local Pressure sidecar was loaded.");
    }

    private bool TryWriteAtomically(string json, out WriteFailure failure)
    {
        string? temporaryPath = null;
        try
        {
            try
            {
                _fileSystem.CreateDirectory(SavePath.SidecarDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failure = new WriteFailure(LocalPressureStoreFailureReason.DirectoryCreationFailed, $"Local Pressure sidecar directory could not be created: {ex.Message}");
                return false;
            }

            temporaryPath = _fileSystem.CreateTemporaryPath(SavePath.SidecarDirectory, SavePath.SidecarFilePath);
            try
            {
                _fileSystem.WriteAllTextAndFlush(temporaryPath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failure = new WriteFailure(LocalPressureStoreFailureReason.TemporaryFileWriteFailed, $"Local Pressure temporary sidecar could not be written: {ex.Message}");
                return false;
            }

            try
            {
                _fileSystem.ReplaceAtomically(temporaryPath, SavePath.SidecarFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                failure = new WriteFailure(LocalPressureStoreFailureReason.AtomicReplacementFailed, $"Local Pressure sidecar could not be replaced atomically: {ex.Message}");
                return false;
            }

            failure = WriteFailure.None;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            failure = new WriteFailure(LocalPressureStoreFailureReason.TemporaryFileWriteFailed, $"Local Pressure sidecar write failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    _fileSystem.DeleteFile(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Never replace the primary failure with cleanup noise.
                }
            }
        }
    }

    private static bool TryValidateIncoming(LocalPressurePlayerRecord? incoming, out LocalPressureCodecResult result)
    {
        return LocalPressureSaveCodec.TrySerialize(
            new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { incoming! }),
            out _,
            out result);
    }

    private static bool Reject(
        LocalPressureStoreFailureReason reason,
        string message,
        out LocalPressureStoreUpdateResult result)
    {
        result = new LocalPressureStoreUpdateResult(false, LocalPressureStoreUpdateStatus.Rejected, null, reason, message);
        return false;
    }

    private static LocalPressureStoreLoadResult FailedLoad(LocalPressureStoreFailureReason reason, string message) =>
        new(false, LocalPressureStoreLoadStatus.Failed, null, reason, message);

    private static LocalPressureStoreFailureReason MapCodecFailure(LocalPressureCodecFailureReason reason) => reason switch
    {
        LocalPressureCodecFailureReason.EmptyJson or LocalPressureCodecFailureReason.MalformedJson => LocalPressureStoreFailureReason.EmptyOrMalformedJson,
        LocalPressureCodecFailureReason.InvalidRoot => LocalPressureStoreFailureReason.InvalidRoot,
        LocalPressureCodecFailureReason.MissingRequiredField => LocalPressureStoreFailureReason.MissingRequiredField,
        LocalPressureCodecFailureReason.InvalidJsonType => LocalPressureStoreFailureReason.InvalidJsonType,
        LocalPressureCodecFailureReason.InvalidSchemaVersion => LocalPressureStoreFailureReason.InvalidSchemaVersion,
        LocalPressureCodecFailureReason.MissingPlayers => LocalPressureStoreFailureReason.MissingPlayers,
        LocalPressureCodecFailureReason.UnsupportedSchema => LocalPressureStoreFailureReason.UnsupportedSchema,
        LocalPressureCodecFailureReason.InvalidPlayerId => LocalPressureStoreFailureReason.InvalidPlayerId,
        LocalPressureCodecFailureReason.DuplicatePlayerId => LocalPressureStoreFailureReason.DuplicatePlayerId,
        LocalPressureCodecFailureReason.InvalidHeat => LocalPressureStoreFailureReason.InvalidHeat,
        LocalPressureCodecFailureReason.InvalidGameTime => LocalPressureStoreFailureReason.InvalidGameTime,
        LocalPressureCodecFailureReason.InvalidRevision => LocalPressureStoreFailureReason.InvalidRevision,
        LocalPressureCodecFailureReason.InvalidContext => LocalPressureStoreFailureReason.InvalidContext,
        _ => LocalPressureStoreFailureReason.InvalidCurrentState
    };

    private static object GetLock(string path) => Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    private readonly record struct WriteFailure(LocalPressureStoreFailureReason Reason, string Message)
    {
        public static WriteFailure None => new(LocalPressureStoreFailureReason.None, string.Empty);
    }
}

internal sealed class LocalPressureFileSystem : ILocalPressureFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string CreateTemporaryPath(string directory, string targetPath) =>
        Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    public void WriteAllTextAndFlush(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public void ReplaceAtomically(string temporaryPath, string targetPath)
    {
        if (File.Exists(targetPath))
            File.Replace(temporaryPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(temporaryPath, targetPath);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
