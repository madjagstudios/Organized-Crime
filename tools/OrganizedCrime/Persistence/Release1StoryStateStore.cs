using OrganizedCrime.Core.Persistence;
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
/// (the SidecarReadFailed catch in the shared load core) leaves it null, since there are no bytes to keep.
/// </summary>
public sealed record Release1StoryStoreLoadResult(bool Succeeded, Release1StoryStoreLoadStatus Status, Release1StorySaveEnvelope? Envelope, Release1StoryStoreFailureReason FailureReason, string Message, string? RawContents = null);
public sealed record Release1StoryStoreUpdateResult(bool Succeeded, Release1StoryStoreUpdateStatus Status, Release1StorySaveEnvelope? Envelope, Release1StoryStoreFailureReason FailureReason, string Message);
public interface IRelease1StoryFileSystem : ISidecarFileSystem
{
}
public sealed class Release1StoryStateStore
{
    private readonly SidecarStore<Release1StorySaveEnvelope> _store;
    private readonly ISidecarFileSystem _fileSystem;

    public Release1StoryStateStore(Release1StorySavePath savePath, IRelease1StoryFileSystem? fileSystem = null)
    {
        SavePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
        _fileSystem = fileSystem ?? (ISidecarFileSystem)new SidecarDefaultFileSystem();
        // TryLoad and TryPreserveRawContents never invoke the serialize
        // delegates; the uncached forms below only satisfy the options.
        // TryUpdate builds its own store per call so the validated JSON
        // stays local to that update (see below).
        _store = new SidecarStore<Release1StorySaveEnvelope>(
            savePath.Inner,
            _fileSystem,
            new SidecarStoreOptions<Release1StorySaveEnvelope>
            {
                CreateEmpty = Release1StorySaveEnvelope.CreateEmpty,
                Deserialize = Deserialize,
                SerializeForValidation = Serialize,
                MergeCurrent = Merge,
                SerializeForWrite = Serialize,
            });
    }

    public Release1StorySavePath SavePath { get; }

    public bool TryLoad(out Release1StoryStoreLoadResult result)
    {
        var ok = _store.TryLoad(out var load);
        result = MapLoad(load);
        return ok;
    }

    public bool TryUpdate(Release1StoryState? state, out Release1StoryStoreUpdateResult result) =>
        TryUpdate(new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), out result);

    public bool TryUpdate(Release1StorySaveEnvelope incoming, out Release1StoryStoreUpdateResult result)
    {
        if (incoming is null)
        {
            result = new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.InvalidStory, "Incoming story envelope was null.");
            return false;
        }

        // OC-63: the envelope is serialized exactly once per update; the same
        // JSON is reused for the write through the captured value below. It
        // lives in this call's closure, not in an instance field, so a nested
        // update (e.g. from a filesystem callback while loading current)
        // cannot clear the outer update's bytes.
        string? validatedJson = null;
        var store = new SidecarStore<Release1StorySaveEnvelope>(
            SavePath.Inner,
            _fileSystem,
            new SidecarStoreOptions<Release1StorySaveEnvelope>
            {
                CreateEmpty = Release1StorySaveEnvelope.CreateEmpty,
                Deserialize = Deserialize,
                SerializeForValidation = envelope =>
                {
                    var outcome = Serialize(envelope);
                    validatedJson = outcome.Ok ? outcome.Json : null;
                    return outcome;
                },
                MergeCurrent = Merge,
                SerializeForWrite = _ => SidecarEncodeOutcome.Succeeded(validatedJson!),
            });
        var ok = store.TryUpdate(incoming, out var neutral);
        result = MapUpdate(neutral);
        return ok;
    }

    /// <summary>
    /// OC-10: rewrites the exact quarantined bytes so the native save-delete
    /// no longer wipes a sidecar the game would otherwise delete. Only wrapped
    /// by the save-start path while the runtime is quarantined.
    /// </summary>
    public bool TryPreserveRawContents(string rawContents, out Release1StoryStoreUpdateResult result)
    {
        if (!_store.TryPreserveRawContents(rawContents, out var neutral))
        {
            result = MapUpdate(neutral);
            return false;
        }

        result = new(true, Release1StoryStoreUpdateStatus.Updated, null, Release1StoryStoreFailureReason.None, "Quarantined story sidecar bytes were preserved.");
        return true;
    }

    private static SidecarDecodeOutcome<Release1StorySaveEnvelope> Deserialize(string json)
    {
        if (!Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var decoded))
            return SidecarDecodeOutcome<Release1StorySaveEnvelope>.Failed(MapKind(decoded.Reason), decoded.Message);
        return SidecarDecodeOutcome<Release1StorySaveEnvelope>.Succeeded(envelope!);
    }

    private static SidecarEncodeOutcome Serialize(Release1StorySaveEnvelope incoming)
    {
        if (!Release1StorySaveCodec.TrySerialize(incoming, out var json, out var validation))
            return SidecarEncodeOutcome.Failed(MapKind(validation.Reason), validation.Message);
        return SidecarEncodeOutcome.Succeeded(json);
    }

    private static SidecarMergeOutcome<Release1StorySaveEnvelope> Merge(Release1StorySaveEnvelope current, Release1StorySaveEnvelope incoming)
    {
        var currentRevision = current.Story?.Revision ?? -1;
        var incomingRevision = incoming.Story?.Revision ?? -1;
        if (current.Story is not null && incoming.Story is not null && incoming.Story.PlayerId != current.Story.PlayerId)
            return SidecarMergeOutcome<Release1StorySaveEnvelope>.Reject("RevisionConflict", "Story player identity conflicted.");
        if (incomingRevision < currentRevision)
            return SidecarMergeOutcome<Release1StorySaveEnvelope>.Reject("StaleRevision", "Incoming story revision was stale.");
        if (incomingRevision == currentRevision && current.Equals(incoming))
            return SidecarMergeOutcome<Release1StorySaveEnvelope>.ReplayCurrent(current);
        if (incomingRevision == currentRevision)
            return SidecarMergeOutcome<Release1StorySaveEnvelope>.Reject("RevisionConflict", "Incoming story revision conflicted with stored state.");
        return SidecarMergeOutcome<Release1StorySaveEnvelope>.ProceedWith(incoming);
    }

    private static Release1StoryStoreLoadResult MapLoad(SidecarLoadResult<Release1StorySaveEnvelope> load) => load.Status switch
    {
        SidecarLoadStatus.Empty => new(true, Release1StoryStoreLoadStatus.Empty, load.Envelope, Release1StoryStoreFailureReason.None, "Story sidecar was absent; using empty envelope."),
        SidecarLoadStatus.Loaded => new(true, Release1StoryStoreLoadStatus.Loaded, load.Envelope, Release1StoryStoreFailureReason.None, "Story sidecar was loaded."),
        _ when load.IoFailure.Stage != SidecarIoStage.None => new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.SidecarReadFailed, load.IoFailure.Detail),
        _ => new(false, Release1StoryStoreLoadStatus.Failed, null, MapReason(load.FailureKind), load.Message, load.RawContents),
    };

    private static Release1StoryStoreUpdateResult MapUpdate(SidecarUpdateResult<Release1StorySaveEnvelope> update) => update.Status switch
    {
        SidecarUpdateStatus.Updated => new(true, Release1StoryStoreUpdateStatus.Updated, update.Envelope, Release1StoryStoreFailureReason.None, "Story update was stored."),
        SidecarUpdateStatus.Idempotent => new(true, Release1StoryStoreUpdateStatus.Idempotent, update.Envelope, Release1StoryStoreFailureReason.None, "Story update was already stored."),
        _ when update.IoFailure.Stage != SidecarIoStage.None => new(false, Release1StoryStoreUpdateStatus.Rejected, null, MapIoReason(update.IoFailure.Stage), update.IoFailure.Detail),
        _ => new(false, Release1StoryStoreUpdateStatus.Rejected, null, MapReason(update.FailureKind), update.Message),
    };

    private static string MapKind(Release1StoryCodecFailureReason reason) =>
        reason == Release1StoryCodecFailureReason.SerializationFailed ? "SerializationFailed" : "InvalidStory";

    private static Release1StoryStoreFailureReason MapReason(string kind) => kind switch
    {
        "SerializationFailed" => Release1StoryStoreFailureReason.SerializationFailed,
        "InvalidStory" => Release1StoryStoreFailureReason.InvalidStory,
        "StaleRevision" => Release1StoryStoreFailureReason.StaleRevision,
        "RevisionConflict" => Release1StoryStoreFailureReason.RevisionConflict,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sidecar failure kind."),
    };

    private static Release1StoryStoreFailureReason MapIoReason(SidecarIoStage stage) => stage switch
    {
        SidecarIoStage.Read => Release1StoryStoreFailureReason.SidecarReadFailed,
        SidecarIoStage.CreateDirectory => Release1StoryStoreFailureReason.DirectoryCreationFailed,
        SidecarIoStage.CreateTemporaryPath or SidecarIoStage.WriteTemporary => Release1StoryStoreFailureReason.TemporaryFileWriteFailed,
        SidecarIoStage.ReplaceAtomically => Release1StoryStoreFailureReason.AtomicReplacementFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown sidecar IO stage."),
    };
}
