namespace OrganizedCrime.Runtime;

/// <summary>
/// Every call returns a status and never throws, so the roster service can
/// drive native officers without a try/catch at each seam. Production is
/// <c>Il2CppOcOfficerWorld</c>; tests fake this interface.
/// </summary>
public enum OcOfficerWorldStatus
{
    Ok,
    NotHost,
    NotReady,
    Failed,
    TemplateFault,
    InvalidHandle,
    Rejected
}

/// <summary>
/// An opaque handle to one live native clone. The service never inspects it;
/// only the world that minted it interprets <see cref="Native"/>.
/// </summary>
public readonly record struct OcOfficerCloneRef(object? Native, long Pointer, int Serial);

/// <summary>
/// What the world knows about the session officer template.
/// </summary>
public sealed record OcOfficerTemplateStatus(bool Present, bool Registered, ushort PrefabId, string? TemplateId);

/// <summary>
/// Native officer list sizes for the roster QA status.
/// </summary>
public sealed record OcOfficerNativeCounts(int Officers, int Registry);

/// <summary>
/// Which checkpoint a clone's CheckpointBehaviour points at: ours, a native
/// one, none, or unreadable.
/// </summary>
public enum OcCheckpointKind
{
    Unknown,
    None,
    Ours,
    Native
}

/// <summary>
/// The bind decision inputs for one clone: the checkpoint behaviour's Active
/// and Enabled flags, which checkpoint it points at, and the behaviour's own
/// stand point when readable.
/// </summary>
public sealed record OcOfficerBoundState(bool CheckpointActive, bool CheckpointEnabled, OcCheckpointKind CheckpointKind, OcOfficerPosition? StandPoint);

/// <summary>
/// One behaviour on a clone's stack for QA: its display name, priority, and flags.
/// </summary>
public sealed record OcOfficerBehaviourInfo(string Name, int Priority, bool Enabled, bool Active);

/// <summary>
/// The checkpoint behaviour's QA detail: flags, assigned location name, and kind.
/// </summary>
public sealed record OcOfficerCheckpointDetail(bool Enabled, bool Active, string Assigned, OcCheckpointKind Kind);

/// <summary>
/// A clone's QA behaviour dump: enabled behaviours top-first, the active
/// name, and the checkpoint behaviour detail.
/// </summary>
public sealed record OcOfficerBehaviourDiagnostics(IReadOnlyList<OcOfficerBehaviourInfo> EnabledTopFirst, string ActiveName, OcOfficerCheckpointDetail Checkpoint);

public interface IOcOfficerWorld
{
    bool IsHost { get; }

    OcOfficerWorldStatus TryReadGameMinutes(out double minutes);

    OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail);

    OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail);

    OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut);

    OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position);
    OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive);

    OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state);

    OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics);

    OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail);

    OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail);

    OcOfficerWorldStatus TryDestroyTemplate(out string detail);

    OcOfficerWorldStatus TryClearSwapMap(out string detail);

    OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail);

    OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail);

    OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status);

    OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts);

    OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees);
}
