using OrganizedCrime.Core.Persistence;
using OrganizedCrime.Model;

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
public interface ILocalPressureFileSystem : ISidecarFileSystem
{
}

public sealed class LocalPressureStateStore
{
    private readonly SidecarStore<LocalPressureSaveEnvelope> _store;

    public LocalPressureStateStore(LocalPressureSavePath savePath, ILocalPressureFileSystem? fileSystem = null)
    {
        SavePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
        _store = new SidecarStore<LocalPressureSaveEnvelope>(
            savePath.Inner,
            fileSystem ?? (ISidecarFileSystem)new SidecarDefaultFileSystem(),
            new SidecarStoreOptions<LocalPressureSaveEnvelope>
            {
                ExceptionPolicy = SidecarExceptionPolicy.StorageFailures,
                CreateEmpty = LocalPressureSaveEnvelope.CreateEmpty,
                Deserialize = Deserialize,
                SerializeForValidation = ValidateIncoming,
                MergeCurrent = Merge,
                SerializeForWrite = SerializeMerged,
            });
    }

    public LocalPressureSavePath SavePath { get; }

    public bool TryLoad(out LocalPressureStoreLoadResult result)
    {
        var ok = _store.TryLoad(out var load);
        result = MapLoad(load);
        return ok;
    }

    public bool TryUpdate(LocalPressurePlayerRecord incoming, out LocalPressureStoreUpdateResult result)
    {
        var ok = _store.TryUpdate(
            new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { incoming }),
            out var neutral);
        result = MapUpdate(neutral);
        return ok;
    }

    private static SidecarDecodeOutcome<LocalPressureSaveEnvelope> Deserialize(string json)
    {
        if (!LocalPressureSaveCodec.TryDeserialize(json, out var envelope, out var decodeResult))
            return SidecarDecodeOutcome<LocalPressureSaveEnvelope>.Failed(MapCodecKind(decodeResult.Reason), decodeResult.Message);
        return SidecarDecodeOutcome<LocalPressureSaveEnvelope>.Succeeded(envelope!);
    }

    private static SidecarEncodeOutcome ValidateIncoming(LocalPressureSaveEnvelope wrapped)
    {
        if (!LocalPressureSaveCodec.TrySerialize(wrapped, out _, out var validationFailure))
            return SidecarEncodeOutcome.Failed("InvalidIncomingRecord", validationFailure.Message);
        return SidecarEncodeOutcome.Succeeded(string.Empty);
    }

    private static SidecarMergeOutcome<LocalPressureSaveEnvelope> Merge(LocalPressureSaveEnvelope current, LocalPressureSaveEnvelope wrapped)
    {
        var incoming = wrapped.Players.Single();
        var existing = current.Players.FirstOrDefault(candidate =>
            string.Equals(candidate.PlayerId, incoming.PlayerId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (incoming.Revision < existing.Revision)
                return SidecarMergeOutcome<LocalPressureSaveEnvelope>.Reject("StaleRevision", "Incoming Local Pressure revision was stale.");
            if (incoming.Revision == existing.Revision)
            {
                if (incoming == existing)
                    return SidecarMergeOutcome<LocalPressureSaveEnvelope>.ReplayCurrent(current);
                return SidecarMergeOutcome<LocalPressureSaveEnvelope>.Reject("RevisionConflict", "Incoming Local Pressure revision conflicted with stored state.");
            }
        }

        var nextPlayers = current.Players
            .Where(candidate => !string.Equals(candidate.PlayerId, incoming.PlayerId, StringComparison.Ordinal))
            .Append(incoming)
            .ToArray();
        return SidecarMergeOutcome<LocalPressureSaveEnvelope>.ProceedWith(
            new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, nextPlayers));
    }

    private static SidecarEncodeOutcome SerializeMerged(LocalPressureSaveEnvelope merged)
    {
        if (!LocalPressureSaveCodec.TrySerialize(merged, out var mergedJson, out var serializeResult))
            return SidecarEncodeOutcome.Failed("SerializationFailed", serializeResult.Message);
        return SidecarEncodeOutcome.Succeeded(mergedJson);
    }

    private static LocalPressureStoreLoadResult MapLoad(SidecarLoadResult<LocalPressureSaveEnvelope> load) => load.Status switch
    {
        SidecarLoadStatus.Empty => new(true, LocalPressureStoreLoadStatus.Empty, load.Envelope, LocalPressureStoreFailureReason.None, "Local Pressure sidecar was not found; using an empty v1 envelope."),
        SidecarLoadStatus.Loaded => new(true, LocalPressureStoreLoadStatus.Loaded, load.Envelope, LocalPressureStoreFailureReason.None, "Local Pressure sidecar was loaded."),
        _ when load.IoFailure.Stage != SidecarIoStage.None => new(false, LocalPressureStoreLoadStatus.Failed, null, LocalPressureStoreFailureReason.SidecarReadFailed, $"Local Pressure sidecar could not be read: {load.IoFailure.Detail}"),
        _ => new(false, LocalPressureStoreLoadStatus.Failed, null, MapReason(load.FailureKind), load.Message),
    };

    private static LocalPressureStoreUpdateResult MapUpdate(SidecarUpdateResult<LocalPressureSaveEnvelope> update) => update.Status switch
    {
        SidecarUpdateStatus.Updated => new(true, LocalPressureStoreUpdateStatus.Updated, update.Envelope, LocalPressureStoreFailureReason.None, "Local Pressure update was stored."),
        SidecarUpdateStatus.Idempotent => new(true, LocalPressureStoreUpdateStatus.Idempotent, update.Envelope, LocalPressureStoreFailureReason.None, "Incoming Local Pressure update was already stored."),
        _ when update.IoFailure.Stage != SidecarIoStage.None => new(false, LocalPressureStoreUpdateStatus.Rejected, null, MapIoReason(update.IoFailure.Stage), FormatIoMessage(update.IoFailure)),
        _ => new(false, LocalPressureStoreUpdateStatus.Rejected, null, MapReason(update.FailureKind), update.Message),
    };

    private static string MapCodecKind(LocalPressureCodecFailureReason reason) => reason switch
    {
        LocalPressureCodecFailureReason.EmptyJson => "EmptyOrMalformedJson",
        LocalPressureCodecFailureReason.MalformedJson => "EmptyOrMalformedJson",
        LocalPressureCodecFailureReason.InvalidRoot => "InvalidRoot",
        LocalPressureCodecFailureReason.MissingRequiredField => "MissingRequiredField",
        LocalPressureCodecFailureReason.InvalidJsonType => "InvalidJsonType",
        LocalPressureCodecFailureReason.InvalidSchemaVersion => "InvalidSchemaVersion",
        LocalPressureCodecFailureReason.MissingPlayers => "MissingPlayers",
        LocalPressureCodecFailureReason.UnsupportedSchema => "UnsupportedSchema",
        LocalPressureCodecFailureReason.InvalidPlayerId => "InvalidPlayerId",
        LocalPressureCodecFailureReason.DuplicatePlayerId => "DuplicatePlayerId",
        LocalPressureCodecFailureReason.InvalidHeat => "InvalidHeat",
        LocalPressureCodecFailureReason.InvalidGameTime => "InvalidGameTime",
        LocalPressureCodecFailureReason.InvalidRevision => "InvalidRevision",
        LocalPressureCodecFailureReason.InvalidContext => "InvalidContext",
        _ => "InvalidCurrentState",
    };

    private static LocalPressureStoreFailureReason MapReason(string kind) => kind switch
    {
        "InvalidIncomingRecord" => LocalPressureStoreFailureReason.InvalidIncomingRecord,
        "SerializationFailed" => LocalPressureStoreFailureReason.SerializationFailed,
        "StaleRevision" => LocalPressureStoreFailureReason.StaleRevision,
        "RevisionConflict" => LocalPressureStoreFailureReason.RevisionConflict,
        "EmptyOrMalformedJson" => LocalPressureStoreFailureReason.EmptyOrMalformedJson,
        "InvalidRoot" => LocalPressureStoreFailureReason.InvalidRoot,
        "MissingRequiredField" => LocalPressureStoreFailureReason.MissingRequiredField,
        "InvalidJsonType" => LocalPressureStoreFailureReason.InvalidJsonType,
        "InvalidSchemaVersion" => LocalPressureStoreFailureReason.InvalidSchemaVersion,
        "MissingPlayers" => LocalPressureStoreFailureReason.MissingPlayers,
        "UnsupportedSchema" => LocalPressureStoreFailureReason.UnsupportedSchema,
        "InvalidPlayerId" => LocalPressureStoreFailureReason.InvalidPlayerId,
        "DuplicatePlayerId" => LocalPressureStoreFailureReason.DuplicatePlayerId,
        "InvalidHeat" => LocalPressureStoreFailureReason.InvalidHeat,
        "InvalidGameTime" => LocalPressureStoreFailureReason.InvalidGameTime,
        "InvalidRevision" => LocalPressureStoreFailureReason.InvalidRevision,
        "InvalidContext" => LocalPressureStoreFailureReason.InvalidContext,
        "InvalidCurrentState" => LocalPressureStoreFailureReason.InvalidCurrentState,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sidecar failure kind."),
    };

    private static LocalPressureStoreFailureReason MapIoReason(SidecarIoStage stage) => stage switch
    {
        SidecarIoStage.Read => LocalPressureStoreFailureReason.SidecarReadFailed,
        SidecarIoStage.CreateDirectory => LocalPressureStoreFailureReason.DirectoryCreationFailed,
        SidecarIoStage.CreateTemporaryPath or SidecarIoStage.WriteTemporary => LocalPressureStoreFailureReason.TemporaryFileWriteFailed,
        SidecarIoStage.ReplaceAtomically => LocalPressureStoreFailureReason.AtomicReplacementFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown sidecar IO stage."),
    };

    private static string FormatIoMessage(SidecarIoFailure failure) => failure.Stage switch
    {
        SidecarIoStage.Read => $"Local Pressure sidecar could not be read: {failure.Detail}",
        SidecarIoStage.CreateDirectory => $"Local Pressure sidecar directory could not be created: {failure.Detail}",
        SidecarIoStage.CreateTemporaryPath => $"Local Pressure sidecar write failed: {failure.Detail}",
        SidecarIoStage.WriteTemporary => $"Local Pressure temporary sidecar could not be written: {failure.Detail}",
        SidecarIoStage.ReplaceAtomically => $"Local Pressure sidecar could not be replaced atomically: {failure.Detail}",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Stage, "Unknown sidecar IO stage."),
    };
}
