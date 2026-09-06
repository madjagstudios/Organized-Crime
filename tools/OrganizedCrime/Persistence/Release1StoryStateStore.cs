using System.Collections.Concurrent;
using System.Text;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public enum Release1StoryStoreLoadStatus { Loaded, Empty, Failed }
public enum Release1StoryStoreFailureReason { None, SidecarReadFailed, EmptyOrMalformedJson, InvalidStory, StaleRevision, RevisionConflict, SerializationFailed, DirectoryCreationFailed, TemporaryFileWriteFailed, AtomicReplacementFailed }
public enum Release1StoryStoreUpdateStatus { Updated, Idempotent, Rejected }
/// <summary>
/// RawContents (OC-10) carries the exact text a failed load could not parse, so a quarantined
/// story runtime can hand it back to the store unchanged rather than the game's save routine
/// dropping a sidecar the mod never rewrote. It is populated only when the file was read
/// successfully but rejected by the codec; a missing file (Empty) or an unreadable file
/// (the SidecarReadFailed catch in LoadLocked) leaves it null, since there are no bytes to keep.
/// </summary>
public sealed record Release1StoryStoreLoadResult(bool Succeeded, Release1StoryStoreLoadStatus Status, Release1StorySaveEnvelope? Envelope, Release1StoryStoreFailureReason FailureReason, string Message, string? RawContents = null);
public sealed record Release1StoryStoreUpdateResult(bool Succeeded, Release1StoryStoreUpdateStatus Status, Release1StorySaveEnvelope? Envelope, Release1StoryStoreFailureReason FailureReason, string Message);
public interface IRelease1StoryFileSystem
{
    bool FileExists(string path); string ReadAllText(string path); void CreateDirectory(string path); string CreateTemporaryPath(string directory, string targetPath); void WriteAllTextAndFlush(string path, string contents); void ReplaceAtomically(string temporaryPath, string targetPath); void DeleteFile(string path);
}

public sealed class Release1StoryStateStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRelease1StoryFileSystem _fileSystem;
    public Release1StoryStateStore(Release1StorySavePath savePath, IRelease1StoryFileSystem? fileSystem = null) { SavePath = savePath ?? throw new ArgumentNullException(nameof(savePath)); _fileSystem = fileSystem ?? new DefaultFileSystem(); }
    public Release1StorySavePath SavePath { get; }
    public bool TryLoad(out Release1StoryStoreLoadResult result) { lock (GetLock()) { try { result = LoadLocked(); } catch (Exception ex) { result = new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.SidecarReadFailed, ex.Message); } return result.Succeeded; } }
    public bool TryUpdate(Release1StoryState? story, out Release1StoryStoreUpdateResult result) => TryUpdate(new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, story), out result);
    public bool TryUpdate(Release1StorySaveEnvelope incoming, out Release1StoryStoreUpdateResult result)
    {
        lock (GetLock())
        {
            if (incoming is null)
                return Reject(Release1StoryStoreFailureReason.InvalidStory, "Incoming story envelope was null.", out result);
            // OC-63: the envelope is serialized exactly once per update. The validation pass and the
            // write pass used to serialize the whole story twice, and the story grows with every
            // mission attempt, assignment, progress row, effect journal entry and presentation
            // receipt, so the second pass was pure duplicated work on the save boundary. The
            // rejection ordering is unchanged: an envelope that cannot serialize is still rejected
            // before the current sidecar is read, with the same failure reason and message.
            if (!Release1StorySaveCodec.TrySerialize(incoming, out var json, out var validation))
                return Reject(Map(validation.Reason), validation.Message, out result);
            var currentResult = LoadLocked(); if (!currentResult.Succeeded) return Reject(currentResult.FailureReason, currentResult.Message, out result);
            var current = currentResult.Envelope!;
            var currentRevision = current.Story?.Revision ?? -1; var incomingRevision = incoming.Story?.Revision ?? -1;
            if (current.Story is not null && incoming.Story is not null && incoming.Story.PlayerId != current.Story.PlayerId) return Reject(Release1StoryStoreFailureReason.RevisionConflict, "Story player identity conflicted.", out result);
            if (incomingRevision < currentRevision) return Reject(Release1StoryStoreFailureReason.StaleRevision, "Incoming story revision was stale.", out result);
            if (incomingRevision == currentRevision && current.Equals(incoming)) { result = new(true, Release1StoryStoreUpdateStatus.Idempotent, current, Release1StoryStoreFailureReason.None, "Story update was already stored."); return true; }
            if (incomingRevision == currentRevision) return Reject(Release1StoryStoreFailureReason.RevisionConflict, "Incoming story revision conflicted with stored state.", out result);
            if (!TryWriteAtomically(json, out var failure)) return Reject(failure.Reason, failure.Message, out result);
            result = new(true, Release1StoryStoreUpdateStatus.Updated, incoming, Release1StoryStoreFailureReason.None, "Story update was stored."); return true;
        }
    }
    /// <summary>
    /// OC-10: rewrites exact previously-read bytes to the sidecar path without serializing or
    /// validating a story. Used only to keep a quarantined sidecar alive across a native save
    /// (the vanilla save routine appears to drop files the mod does not rewrite), never to store
    /// a fresh envelope.
    /// </summary>
    public bool TryPreserveRawContents(string rawContents, out Release1StoryStoreUpdateResult result)
    {
        lock (GetLock())
        {
            if (!TryWriteAtomically(rawContents, out var failure))
                return Reject(failure.Reason, failure.Message, out result);
            result = new(true, Release1StoryStoreUpdateStatus.Updated, null, Release1StoryStoreFailureReason.None, "Quarantined story sidecar bytes were preserved.");
            return true;
        }
    }
    private Release1StoryStoreLoadResult LoadLocked()
    {
        try
        {
            if (!_fileSystem.FileExists(SavePath.SidecarFilePath)) return new(true, Release1StoryStoreLoadStatus.Empty, Release1StorySaveEnvelope.CreateEmpty(), Release1StoryStoreFailureReason.None, "Story sidecar was absent; using empty envelope.");
            var json = _fileSystem.ReadAllText(SavePath.SidecarFilePath);
            if (!Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var decoded)) return new(false, Release1StoryStoreLoadStatus.Failed, null, Map(decoded.Reason), decoded.Message, json);
            return new(true, Release1StoryStoreLoadStatus.Loaded, envelope, Release1StoryStoreFailureReason.None, "Story sidecar was loaded.");
        }
        catch (Exception ex) { return new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.SidecarReadFailed, ex.Message); }
    }
    private bool TryWriteAtomically(string json, out (Release1StoryStoreFailureReason Reason, string Message) failure)
    {
        string? temp = null; Exception? primary = null;
        try { try { _fileSystem.CreateDirectory(SavePath.SidecarDirectory); } catch (Exception ex) { failure = (Release1StoryStoreFailureReason.DirectoryCreationFailed, ex.Message); return false; } try { temp = _fileSystem.CreateTemporaryPath(SavePath.SidecarDirectory, SavePath.SidecarFilePath); } catch (Exception ex) { primary = ex; failure = (Release1StoryStoreFailureReason.TemporaryFileWriteFailed, ex.Message); return false; } try { _fileSystem.WriteAllTextAndFlush(temp, json); } catch (Exception ex) { primary = ex; failure = (Release1StoryStoreFailureReason.TemporaryFileWriteFailed, ex.Message); return false; } try { _fileSystem.ReplaceAtomically(temp, SavePath.SidecarFilePath); temp = null; failure = (Release1StoryStoreFailureReason.None, string.Empty); return true; } catch (Exception ex) { primary = ex; failure = (Release1StoryStoreFailureReason.AtomicReplacementFailed, ex.Message); return false; } }
        finally { if (temp is not null) { try { _fileSystem.DeleteFile(temp); } catch when (primary is not null) { } } }
    }
    private static object GetLockFor(string path) => Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());
    private object GetLock() => GetLockFor(SavePath.SidecarFilePath);
    private static bool Reject(Release1StoryStoreFailureReason reason, string message, out Release1StoryStoreUpdateResult result) { result = new(false, Release1StoryStoreUpdateStatus.Rejected, null, reason, message); return false; }
    private static Release1StoryStoreFailureReason Map(Release1StoryCodecFailureReason reason) => reason == Release1StoryCodecFailureReason.SerializationFailed ? Release1StoryStoreFailureReason.SerializationFailed : Release1StoryStoreFailureReason.InvalidStory;
    private sealed class DefaultFileSystem : IRelease1StoryFileSystem
    {
        public bool FileExists(string path) => File.Exists(path); public string ReadAllText(string path) => File.ReadAllText(path); public void CreateDirectory(string path) => Directory.CreateDirectory(path); public string CreateTemporaryPath(string directory, string targetPath) => Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        public void WriteAllTextAndFlush(string path, string contents) { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(contents); writer.Flush(); stream.Flush(true); }
        public void ReplaceAtomically(string temporaryPath, string targetPath) { if (File.Exists(targetPath)) File.Replace(temporaryPath, targetPath, null, true); else File.Move(temporaryPath, targetPath); }
        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}
