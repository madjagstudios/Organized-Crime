using System.Collections.Concurrent;
using System.Text;

namespace OrganizedCrime.Core.Persistence;

/// <summary>Neutral sidecar load status. Each service maps these to its own load-status enum.</summary>
public enum SidecarLoadStatus
{
    Loaded,
    Empty,
    Failed
}

/// <summary>Neutral sidecar update status. Each service maps these to its own update-status enum.</summary>
public enum SidecarUpdateStatus
{
    Updated,
    Idempotent,
    Rejected
}

/// <summary>
/// Which exceptions the store converts to failures. CatchAll (Chapter 3,
/// Release 1 Story) converts every Exception from every step; StorageFailures
/// (Local Pressure) converts only IOException, UnauthorizedAccessException and
/// InvalidOperationException (plus PlatformNotSupportedException on replace),
/// leaves FileExists and CreateTemporaryPath unguarded, and lets anything else
/// propagate to the caller.
/// </summary>
public enum SidecarExceptionPolicy
{
    CatchAll,
    StorageFailures
}

/// <summary>Which Core IO step failed. The per-service mapper turns the stage plus detail into its own reason and message.</summary>
public enum SidecarIoStage
{
    None,
    Read,
    CreateDirectory,
    CreateTemporaryPath,
    WriteTemporary,
    ReplaceAtomically
}

/// <summary>A Core IO failure: the stage plus the underlying exception message. No service wording here.</summary>
public readonly record struct SidecarIoFailure(SidecarIoStage Stage, string Detail)
{
    public static SidecarIoFailure None => new(SidecarIoStage.None, string.Empty);
}

/// <summary>
/// Neutral sidecar load result. FailureKind/Message are the service delegates'
/// opaque vocabulary (Core never interprets them); IoFailure carries Core IO
/// failures. Message is empty and FailureKind is empty on success: the mapper
/// supplies the fixed success wordings.
/// </summary>
public sealed record SidecarLoadResult<TEnvelope>(
    bool Succeeded,
    SidecarLoadStatus Status,
    TEnvelope? Envelope,
    string FailureKind,
    string Message,
    string? RawContents,
    SidecarIoFailure IoFailure);

/// <summary>
/// Neutral sidecar update result. Same failure vocabulary as
/// <see cref="SidecarLoadResult{TEnvelope}"/>; fixed success wordings come
/// from the mapper.
/// </summary>
public sealed record SidecarUpdateResult<TEnvelope>(
    bool Succeeded,
    SidecarUpdateStatus Status,
    TEnvelope? Envelope,
    string FailureKind,
    string Message,
    SidecarIoFailure IoFailure);

/// <summary>Outcome of the service Deserialize delegate: the envelope, or an opaque kind plus the codec message.</summary>
public sealed record SidecarDecodeOutcome<TEnvelope>(bool Ok, TEnvelope? Envelope, string FailureKind, string Message)
{
    public static SidecarDecodeOutcome<TEnvelope> Succeeded(TEnvelope envelope) =>
        new(true, envelope, string.Empty, string.Empty);

    public static SidecarDecodeOutcome<TEnvelope> Failed(string failureKind, string message) =>
        new(false, default, failureKind, message);
}

/// <summary>Outcome of a service serialize delegate: the JSON, or an opaque kind plus the codec message.</summary>
public sealed record SidecarEncodeOutcome(bool Ok, string? Json, string FailureKind, string Message)
{
    public static SidecarEncodeOutcome Succeeded(string json) =>
        new(true, json, string.Empty, string.Empty);

    public static SidecarEncodeOutcome Failed(string failureKind, string message) =>
        new(false, null, failureKind, message);
}

/// <summary>
/// Outcome of the service Merge delegate: reject (kind + message), replay the
/// current envelope idempotently without writing, or proceed to write the
/// merged envelope.
/// </summary>
public sealed record SidecarMergeOutcome<TEnvelope>(bool Proceed, TEnvelope? Envelope, bool Idempotent, string FailureKind, string Message)
{
    public static SidecarMergeOutcome<TEnvelope> ProceedWith(TEnvelope envelope) =>
        new(true, envelope, false, string.Empty, string.Empty);

    public static SidecarMergeOutcome<TEnvelope> ReplayCurrent(TEnvelope current) =>
        new(true, current, true, string.Empty, string.Empty);

    public static SidecarMergeOutcome<TEnvelope> Reject(string failureKind, string message) =>
        new(false, default, false, failureKind, message);
}

/// <summary>
/// Per-service behaviour behind a <see cref="SidecarStore{TEnvelope}"/>: the
/// empty envelope, the codec delegates, the merge policy, and the exception
/// policy. The whole-envelope services validate by serializing first (so a
/// rejection lands before the current sidecar is read) and reuse that JSON
/// for the write (serialized exactly once); the per-record service validates
/// to void and serializes the merged envelope after.
/// </summary>
public sealed class SidecarStoreOptions<TEnvelope> where TEnvelope : class
{
    public SidecarExceptionPolicy ExceptionPolicy { get; set; } = SidecarExceptionPolicy.CatchAll;
    public Func<TEnvelope> CreateEmpty { get; set; } = null!;
    public Func<string, SidecarDecodeOutcome<TEnvelope>> Deserialize { get; set; } = null!;
    public Func<TEnvelope, SidecarEncodeOutcome> SerializeForValidation { get; set; } = null!;
    public Func<TEnvelope, TEnvelope, SidecarMergeOutcome<TEnvelope>> MergeCurrent { get; set; } = null!;
    public Func<TEnvelope, SidecarEncodeOutcome> SerializeForWrite { get; set; } = null!;
}

/// <summary>
/// One lock object per sidecar path (ordinal-ignore-case on the full path),
/// shared by every sidecar store. Sidecar file names differ per service, so
/// the exclusion sets are exactly the old per-store tables.
/// </summary>
public static class SidecarLocks
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static object For(string sidecarFilePath) =>
        Locks.GetOrAdd(Path.GetFullPath(sidecarFilePath), _ => new object());
}

/// <summary>The single production file system: dot-temp files, CreateNew +
/// UTF8-no-BOM + flush-to-disk writes, File.Replace-or-Move replacement.</summary>
public sealed class SidecarDefaultFileSystem : ISidecarFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string CreateTemporaryPath(string directory, string targetPath) =>
        Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    public void WriteAllTextAndFlush(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(contents);
        writer.Flush();
        stream.Flush(true);
    }

    public void ReplaceAtomically(string temporaryPath, string targetPath)
    {
        if (File.Exists(targetPath))
            File.Replace(temporaryPath, targetPath, null, true);
        else
            File.Move(temporaryPath, targetPath);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

/// <summary>
/// The shared atomic sidecar write: create the directory, write a temp file,
/// replace the target, delete the temp file on failure. The two exception
/// policies keep their exact historical shapes (see
/// <see cref="SidecarExceptionPolicy"/>), including which call is left
/// unguarded and which cleanup exceptions propagate.
/// </summary>
public static class SidecarFileIO
{
    public static bool TryWriteAtomically(
        ISidecarFileSystem fileSystem,
        string directory,
        string targetPath,
        string contents,
        SidecarExceptionPolicy policy,
        out SidecarIoFailure failure)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return policy == SidecarExceptionPolicy.StorageFailures
            ? TryWriteAtomicallyFiltered(fileSystem, directory, targetPath, contents, out failure)
            : TryWriteAtomicallyCatchAll(fileSystem, directory, targetPath, contents, out failure);
    }

    private static bool TryWriteAtomicallyCatchAll(
        ISidecarFileSystem fileSystem,
        string directory,
        string targetPath,
        string contents,
        out SidecarIoFailure failure)
    {
        string? temp = null;
        Exception? primary = null;
        try
        {
            try
            {
                fileSystem.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                failure = new(SidecarIoStage.CreateDirectory, ex.Message);
                return false;
            }

            try
            {
                temp = fileSystem.CreateTemporaryPath(directory, targetPath);
            }
            catch (Exception ex)
            {
                primary = ex;
                failure = new(SidecarIoStage.CreateTemporaryPath, ex.Message);
                return false;
            }

            try
            {
                fileSystem.WriteAllTextAndFlush(temp, contents);
            }
            catch (Exception ex)
            {
                primary = ex;
                failure = new(SidecarIoStage.WriteTemporary, ex.Message);
                return false;
            }

            try
            {
                fileSystem.ReplaceAtomically(temp, targetPath);
                temp = null;
                failure = SidecarIoFailure.None;
                return true;
            }
            catch (Exception ex)
            {
                primary = ex;
                failure = new(SidecarIoStage.ReplaceAtomically, ex.Message);
                return false;
            }
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    fileSystem.DeleteFile(temp);
                }
                catch when (primary is not null)
                {
                }
            }
        }
    }

    private static bool TryWriteAtomicallyFiltered(
        ISidecarFileSystem fileSystem,
        string directory,
        string targetPath,
        string contents,
        out SidecarIoFailure failure)
    {
        string? temporaryPath = null;
        try
        {
            try
            {
                fileSystem.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failure = new(SidecarIoStage.CreateDirectory, ex.Message);
                return false;
            }

            // Deliberately unguarded, as it always was: a storage failure here
            // lands in the outer catch below (CreateTemporaryPath stage), and
            // anything else propagates to the caller.
            temporaryPath = fileSystem.CreateTemporaryPath(directory, targetPath);
            try
            {
                fileSystem.WriteAllTextAndFlush(temporaryPath, contents);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failure = new(SidecarIoStage.WriteTemporary, ex.Message);
                return false;
            }

            try
            {
                fileSystem.ReplaceAtomically(temporaryPath, targetPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                failure = new(SidecarIoStage.ReplaceAtomically, ex.Message);
                return false;
            }

            failure = SidecarIoFailure.None;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            failure = new(SidecarIoStage.CreateTemporaryPath, ex.Message);
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    fileSystem.DeleteFile(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Never replace the primary failure with cleanup noise.
                }
            }
        }
    }
}

/// <summary>
/// The shared sidecar load/update/preserve skeleton over one
/// <see cref="SidecarPath"/> and <see cref="ISidecarFileSystem"/>, driven by
/// per-service <see cref="SidecarStoreOptions{TEnvelope}"/> delegates. Load:
/// missing file loads the empty envelope, a codec rejection captures the raw
/// bytes. Update: validate first (a rejection lands before the current
/// sidecar is read), then load current, then the merge policy, then one
/// atomic write. Preserve rewrites exact bytes without serializing. Every
/// call serializes under this store's per-path lock.
/// </summary>
public sealed class SidecarStore<TEnvelope> where TEnvelope : class
{
    private readonly ISidecarFileSystem _fileSystem;
    private readonly SidecarStoreOptions<TEnvelope> _options;

    public SidecarStore(SidecarPath path, ISidecarFileSystem fileSystem, SidecarStoreOptions<TEnvelope> options)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(options.CreateEmpty);
        ArgumentNullException.ThrowIfNull(options.Deserialize);
        ArgumentNullException.ThrowIfNull(options.SerializeForValidation);
        ArgumentNullException.ThrowIfNull(options.MergeCurrent);
        ArgumentNullException.ThrowIfNull(options.SerializeForWrite);
    }

    public SidecarPath Path { get; }

    public bool TryLoad(out SidecarLoadResult<TEnvelope> result)
    {
        lock (SidecarLocks.For(Path.SidecarFilePath))
        {
            if (_options.ExceptionPolicy == SidecarExceptionPolicy.CatchAll)
            {
                try
                {
                    result = LoadCore();
                }
                catch (Exception ex)
                {
                    result = new(false, SidecarLoadStatus.Failed, null, string.Empty, string.Empty, null, new(SidecarIoStage.Read, ex.Message));
                }
            }
            else
            {
                result = LoadCore();
            }

            return result.Succeeded;
        }
    }

    public bool TryUpdate(TEnvelope incoming, out SidecarUpdateResult<TEnvelope> result)
    {
        lock (SidecarLocks.For(Path.SidecarFilePath))
        {
            var validation = _options.SerializeForValidation(incoming);
            if (!validation.Ok)
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, validation.FailureKind, validation.Message, SidecarIoFailure.None);
                return false;
            }

            var current = LoadCore();
            if (!current.Succeeded)
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, current.FailureKind, current.Message, current.IoFailure);
                return false;
            }

            var merge = _options.MergeCurrent(current.Envelope!, incoming);
            if (!merge.Proceed)
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, merge.FailureKind, merge.Message, SidecarIoFailure.None);
                return false;
            }

            if (merge.Idempotent)
            {
                result = new(true, SidecarUpdateStatus.Idempotent, merge.Envelope, string.Empty, string.Empty, SidecarIoFailure.None);
                return true;
            }

            var encoded = _options.SerializeForWrite(merge.Envelope!);
            if (!encoded.Ok)
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, encoded.FailureKind, encoded.Message, SidecarIoFailure.None);
                return false;
            }

            if (!SidecarFileIO.TryWriteAtomically(_fileSystem, Path.SidecarDirectory, Path.SidecarFilePath, encoded.Json!, _options.ExceptionPolicy, out var ioFailure))
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, string.Empty, string.Empty, ioFailure);
                return false;
            }

            result = new(true, SidecarUpdateStatus.Updated, merge.Envelope, string.Empty, string.Empty, SidecarIoFailure.None);
            return true;
        }
    }

    /// <summary>
    /// Rewrites exact previously-read bytes without serializing or validating,
    /// so a quarantined sidecar survives the native save routine that drops
    /// files the mod never rewrote. Only the services that capture
    /// RawContents expose this.
    /// </summary>
    public bool TryPreserveRawContents(string rawContents, out SidecarUpdateResult<TEnvelope> result)
    {
        lock (SidecarLocks.For(Path.SidecarFilePath))
        {
            if (!SidecarFileIO.TryWriteAtomically(_fileSystem, Path.SidecarDirectory, Path.SidecarFilePath, rawContents, _options.ExceptionPolicy, out var ioFailure))
            {
                result = new(false, SidecarUpdateStatus.Rejected, null, string.Empty, string.Empty, ioFailure);
                return false;
            }

            result = new(true, SidecarUpdateStatus.Updated, null, string.Empty, string.Empty, SidecarIoFailure.None);
            return true;
        }
    }

    private SidecarLoadResult<TEnvelope> LoadCore()
    {
        if (_options.ExceptionPolicy == SidecarExceptionPolicy.StorageFailures)
            return LoadCoreFiltered();

        try
        {
            if (!_fileSystem.FileExists(Path.SidecarFilePath))
                return new(true, SidecarLoadStatus.Empty, _options.CreateEmpty(), string.Empty, string.Empty, null, SidecarIoFailure.None);
            var json = _fileSystem.ReadAllText(Path.SidecarFilePath);
            var decoded = _options.Deserialize(json);
            if (!decoded.Ok)
                return new(false, SidecarLoadStatus.Failed, null, decoded.FailureKind, decoded.Message, json, SidecarIoFailure.None);
            return new(true, SidecarLoadStatus.Loaded, decoded.Envelope, string.Empty, string.Empty, null, SidecarIoFailure.None);
        }
        catch (Exception ex)
        {
            return new(false, SidecarLoadStatus.Failed, null, string.Empty, string.Empty, null, new(SidecarIoStage.Read, ex.Message));
        }
    }

    private SidecarLoadResult<TEnvelope> LoadCoreFiltered()
    {
        if (!_fileSystem.FileExists(Path.SidecarFilePath))
            return new(true, SidecarLoadStatus.Empty, _options.CreateEmpty(), string.Empty, string.Empty, null, SidecarIoFailure.None);

        string json;
        try
        {
            json = _fileSystem.ReadAllText(Path.SidecarFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(false, SidecarLoadStatus.Failed, null, string.Empty, string.Empty, null, new(SidecarIoStage.Read, ex.Message));
        }

        var decoded = _options.Deserialize(json);
        if (!decoded.Ok)
            return new(false, SidecarLoadStatus.Failed, null, decoded.FailureKind, decoded.Message, null, SidecarIoFailure.None);
        return new(true, SidecarLoadStatus.Loaded, decoded.Envelope, string.Empty, string.Empty, null, SidecarIoFailure.None);
    }
}
