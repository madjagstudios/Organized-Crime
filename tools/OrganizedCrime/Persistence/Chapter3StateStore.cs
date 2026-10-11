using OrganizedCrime.Core.Persistence;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public enum Chapter3StoreLoadStatus { Loaded, Empty, Failed }
public enum Chapter3StoreFailureReason { None, SidecarReadFailed, EmptyOrMalformedJson, InvalidState, StaleRevision, RevisionConflict, SerializationFailed, DirectoryCreationFailed, TemporaryFileWriteFailed, AtomicReplacementFailed }
public enum Chapter3StoreUpdateStatus { Updated, Idempotent, Rejected }
/// <summary>
/// RawContents carries the exact text a failed load could not parse, so a quarantined
/// runtime can hand it back to the store unchanged rather than the game's save routine
/// dropping a sidecar the mod never rewrote. It is populated only when the file was read
/// successfully but rejected by the codec; a missing file (Empty) or an unreadable file
/// (the SidecarReadFailed catch in the shared load core) leaves it null, since there are no bytes to keep.
/// </summary>
public sealed record Chapter3StoreLoadResult(bool Succeeded, Chapter3StoreLoadStatus Status, Chapter3SaveEnvelope? Envelope, Chapter3StoreFailureReason FailureReason, string Message, string? RawContents = null);
public sealed record Chapter3StoreUpdateResult(bool Succeeded, Chapter3StoreUpdateStatus Status, Chapter3SaveEnvelope? Envelope, Chapter3StoreFailureReason FailureReason, string Message);
public interface IChapter3FileSystem : ISidecarFileSystem
{
}
public sealed class Chapter3StateStore
{
    private readonly SidecarStore<Chapter3SaveEnvelope> _store;
    private readonly ISidecarFileSystem _fileSystem;

    public Chapter3StateStore(Chapter3SavePath savePath, IChapter3FileSystem? fileSystem = null)
    {
        SavePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
        _fileSystem = fileSystem ?? (ISidecarFileSystem)new SidecarDefaultFileSystem();
        // TryLoad and TryPreserveRawContents never invoke the serialize
        // delegates; the uncached forms below only satisfy the options.
        // TryUpdate builds its own store per call so the validated JSON
        // stays local to that update (see below).
        _store = new SidecarStore<Chapter3SaveEnvelope>(
            savePath.Inner,
            _fileSystem,
            new SidecarStoreOptions<Chapter3SaveEnvelope>
            {
                CreateEmpty = Chapter3SaveEnvelope.CreateEmpty,
                Deserialize = Deserialize,
                SerializeForValidation = Serialize,
                MergeCurrent = Merge,
                SerializeForWrite = Serialize,
            });
    }

    public Chapter3SavePath SavePath { get; }

    public bool TryLoad(out Chapter3StoreLoadResult result)
    {
        var ok = _store.TryLoad(out var load);
        result = MapLoad(load);
        return ok;
    }

    public bool TryUpdate(Chapter3State? state, out Chapter3StoreUpdateResult result) =>
        TryUpdate(new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, state), out result);

    public bool TryUpdate(Chapter3SaveEnvelope incoming, out Chapter3StoreUpdateResult result)
    {
        if (incoming is null)
        {
            result = new(false, Chapter3StoreUpdateStatus.Rejected, null, Chapter3StoreFailureReason.InvalidState, "Incoming chapter envelope was null.");
            return false;
        }

        // The validated JSON lives in this call's closure, not in an
        // instance field, so a nested update (e.g. from a filesystem
        // callback while loading current) cannot clear the outer update's
        // bytes. Still serialized exactly once: the write reuses it.
        string? validatedJson = null;
        var store = new SidecarStore<Chapter3SaveEnvelope>(
            SavePath.Inner,
            _fileSystem,
            new SidecarStoreOptions<Chapter3SaveEnvelope>
            {
                CreateEmpty = Chapter3SaveEnvelope.CreateEmpty,
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

    public bool TryPreserveRawContents(string rawContents, out Chapter3StoreUpdateResult result)
    {
        if (!_store.TryPreserveRawContents(rawContents, out var neutral))
        {
            result = MapUpdate(neutral);
            return false;
        }

        result = new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Quarantined chapter sidecar bytes were preserved.");
        return true;
    }

    private static SidecarDecodeOutcome<Chapter3SaveEnvelope> Deserialize(string json)
    {
        if (!Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var decoded))
            return SidecarDecodeOutcome<Chapter3SaveEnvelope>.Failed(MapKind(decoded.Reason), decoded.Message);
        return SidecarDecodeOutcome<Chapter3SaveEnvelope>.Succeeded(envelope!);
    }

    private static SidecarEncodeOutcome Serialize(Chapter3SaveEnvelope incoming)
    {
        if (!Chapter3SaveCodec.TrySerialize(incoming, out var json, out var validation))
            return SidecarEncodeOutcome.Failed(MapKind(validation.Reason), validation.Message);
        return SidecarEncodeOutcome.Succeeded(json);
    }

    private static SidecarMergeOutcome<Chapter3SaveEnvelope> Merge(Chapter3SaveEnvelope current, Chapter3SaveEnvelope incoming)
    {
        var currentRevision = current.State?.Revision ?? -1;
        var incomingRevision = incoming.State?.Revision ?? -1;
        if (current.State is not null && incoming.State is not null && incoming.State.PlayerId != current.State.PlayerId)
            return SidecarMergeOutcome<Chapter3SaveEnvelope>.Reject("RevisionConflict", "Chapter player identity conflicted.");
        if (incomingRevision < currentRevision)
            return SidecarMergeOutcome<Chapter3SaveEnvelope>.Reject("StaleRevision", "Incoming chapter revision was stale.");
        if (incomingRevision == currentRevision && current.Equals(incoming))
            return SidecarMergeOutcome<Chapter3SaveEnvelope>.ReplayCurrent(current);
        if (incomingRevision == currentRevision)
            return SidecarMergeOutcome<Chapter3SaveEnvelope>.Reject("RevisionConflict", "Incoming chapter revision conflicted with stored state.");
        return SidecarMergeOutcome<Chapter3SaveEnvelope>.ProceedWith(incoming);
    }

    private static Chapter3StoreLoadResult MapLoad(SidecarLoadResult<Chapter3SaveEnvelope> load) => load.Status switch
    {
        SidecarLoadStatus.Empty => new(true, Chapter3StoreLoadStatus.Empty, load.Envelope, Chapter3StoreFailureReason.None, "Chapter sidecar was absent; using empty envelope."),
        SidecarLoadStatus.Loaded => new(true, Chapter3StoreLoadStatus.Loaded, load.Envelope, Chapter3StoreFailureReason.None, "Chapter sidecar was loaded."),
        _ when load.IoFailure.Stage != SidecarIoStage.None => new(false, Chapter3StoreLoadStatus.Failed, null, Chapter3StoreFailureReason.SidecarReadFailed, load.IoFailure.Detail),
        _ => new(false, Chapter3StoreLoadStatus.Failed, null, MapReason(load.FailureKind), load.Message, load.RawContents),
    };

    private static Chapter3StoreUpdateResult MapUpdate(SidecarUpdateResult<Chapter3SaveEnvelope> update) => update.Status switch
    {
        SidecarUpdateStatus.Updated => new(true, Chapter3StoreUpdateStatus.Updated, update.Envelope, Chapter3StoreFailureReason.None, "Chapter update was stored."),
        SidecarUpdateStatus.Idempotent => new(true, Chapter3StoreUpdateStatus.Idempotent, update.Envelope, Chapter3StoreFailureReason.None, "Chapter update was already stored."),
        _ when update.IoFailure.Stage != SidecarIoStage.None => new(false, Chapter3StoreUpdateStatus.Rejected, null, MapIoReason(update.IoFailure.Stage), update.IoFailure.Detail),
        _ => new(false, Chapter3StoreUpdateStatus.Rejected, null, MapReason(update.FailureKind), update.Message),
    };

    private static string MapKind(Chapter3CodecFailureReason reason) =>
        reason == Chapter3CodecFailureReason.SerializationFailed ? "SerializationFailed" : "InvalidState";

    private static Chapter3StoreFailureReason MapReason(string kind) => kind switch
    {
        "SerializationFailed" => Chapter3StoreFailureReason.SerializationFailed,
        "InvalidState" => Chapter3StoreFailureReason.InvalidState,
        "StaleRevision" => Chapter3StoreFailureReason.StaleRevision,
        "RevisionConflict" => Chapter3StoreFailureReason.RevisionConflict,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sidecar failure kind."),
    };

    private static Chapter3StoreFailureReason MapIoReason(SidecarIoStage stage) => stage switch
    {
        SidecarIoStage.Read => Chapter3StoreFailureReason.SidecarReadFailed,
        SidecarIoStage.CreateDirectory => Chapter3StoreFailureReason.DirectoryCreationFailed,
        SidecarIoStage.CreateTemporaryPath or SidecarIoStage.WriteTemporary => Chapter3StoreFailureReason.TemporaryFileWriteFailed,
        SidecarIoStage.ReplaceAtomically => Chapter3StoreFailureReason.AtomicReplacementFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown sidecar IO stage."),
    };
}
