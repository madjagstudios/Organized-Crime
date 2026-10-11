namespace OrganizedCrime.Runtime;

/// <summary>
/// The checkpoint world's total answer: every call returns one of these and
/// never throws, so the service drives native checkpoints without a try/catch
/// at each seam. Production is <c>Il2CppChapter3CheckpointWorld</c>; tests
/// fake this interface.
/// </summary>
public enum Chapter3CheckpointWorldStatus
{
    Ok,
    NotHost,
    NotReady,
    Failed,
    InvalidHandle,
}

/// <summary>One owned property's world position, keyed by property code.</summary>
public sealed record Chapter3OwnedPropertyPosition(string PropertyCode, float X, float Y, float Z);

/// <summary>One native checkpoint's world position, keyed by location label.</summary>
public sealed record Chapter3NativeCheckpointPosition(string Location, float X, float Y, float Z);

/// <summary>The player's camera pose: eye position plus the facing yaw in degrees.</summary>
public sealed record Chapter3CameraPose(float X, float Y, float Z, float YawDegrees);

/// <summary>One checkpoint stand point: a ground-snapped road position.</summary>
public sealed record Chapter3StandPoint(float X, float Y, float Z);

/// <summary>
/// The native checkpoint seam: owned-property and native-checkpoint reads,
/// OC checkpoint clone spawn and despawn, and the native schedule enable.
/// </summary>
public interface IChapter3CheckpointWorld
{
    bool IsHost { get; }

    Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions);

    Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode);

    Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions);

    Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yawDegrees, out string? handle);

    Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle);

    Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? nativeCheckpoint);

    Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled);

    Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose);

    Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points);
}
