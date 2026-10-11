namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-130: Arthur Selby as a persistent physical NPC (proof). Release 1's
/// Arthur phone calls carry only a caller label ("Arthur Selby"), never an
/// NPC id, so this is the one Arthur identity: the shipping id below plus
/// the "Arthur Selby" name, which matches the Release 1 caller spelling.
/// One Arthur, not two: C2/C3 build the visits and the beating on this id.
/// </summary>
public static class Chapter3ArthurIds
{
    /// <summary>
    /// The stable shipping NPC id. Deliberately not the OC-69 spike id
    /// (<c>oc_release1_arthur_spike</c>), which stays untouched so spike
    /// residue in a save is distinguishable from the shipped Arthur.
    /// </summary>
    public const string NpcId = "oc_chapter3_arthur";

    public const string FirstName = "Arthur";
    public const string LastName = "Selby";
}

/// <summary>
/// OC-130: Arthur's visit lifecycle. Parked is the only rest state: a
/// reload always resolves here, whatever was mid-flight, because the
/// load reconcile parks the load-constructed NPC before anything else.
/// OC-132 adds AtHq: standing at the HQ spot while Chapter 3 is
/// active and the player is inside the HQ. A visit overrides the HQ
/// presence, entering the HQ ends a visit, and a reload still
/// resolves to parked before the presence takes over.
/// </summary>
public enum Chapter3ArthurVisitState
{
    Parked,
    Coming,
    Arrived,
    Leaving,
    AtHq
}

/// <summary>
/// OC-130: what a <see cref="IChapter3ArthurWorld"/> read reports. All
/// positions are plain tuples so this file stays free of any engine or
/// mod-framework reference and stays test linkable.
/// </summary>
public enum Chapter3ArthurReadStatus
{
    Ready,
    Pending,
    Unavailable,
    Faulted
}

/// <summary>
/// OC-130: what a <see cref="IChapter3ArthurWorld"/> mutation reports.
/// </summary>
public enum Chapter3ArthurMutationStatus
{
    Succeeded,
    Rejected,
    Unavailable,
    Ambiguous
}

/// OC-132: <c>Health</c> is the framework's live current-health read
/// for the NPC, or NaN when unreadable; the pump logs an attack line
/// when it drops. <c>InPlayerView</c> is the runtime's horizontal
/// view-cone read, defaulting to visible: an unknown read must never
/// park a leaving Arthur early. <c>DistanceToPlayer</c> is measured
/// on the horizontal plane, so arrivals ignore height differences.
public sealed record Chapter3ArthurSnapshot(
    bool Present,
    bool Spawned,
    bool Visible,
    bool Moving,
    bool Conscious,
    (float X, float Y, float Z) Position,
    float DistanceToPlayer,
    int RegisteredCount,
    float Health = float.NaN,
    bool InPlayerView = true)
{
    public static Chapter3ArthurSnapshot Absent() =>
        new(false, false, false, false, false, (0f, 0f, 0f), float.NaN, 0);
}

/// <summary>
/// OC-130: the world boundary Arthur's proof hangs off. Implemented by
/// <c>S1ApiChapter3ArthurRuntime</c> (game framework and engine only,
/// never linked into tests); faked in tests. Every member is total: a
/// status plus a
/// reason, never an exception out. Host only: mutations refuse when
/// <see cref="HasAuthority"/> is false. Spawn-gated: every mutation
/// except <c>TryMoveOutOfSight</c>, <c>TryVerifyAppearancePaths</c>, and <c>TryCaptureHqSpot</c> (a read that never touches Arthur)
/// refuses until the snapshot reports spawned, because the framework drops a
/// custom NPC whose Avatar is inactive when its spawn guard runs.
/// No combat anywhere on this boundary: no damage, no aggression, no
/// provoke member at all.
/// </summary>
public interface IChapter3ArthurWorld
{
    bool HasAuthority { get; }
    Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot);
    Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason);
    Chapter3ArthurMutationStatus TryParkArthur(out string reason);
    Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason);
    Chapter3ArthurMutationStatus TryComeArthur(out string reason);
    Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason);
    Chapter3ArthurMutationStatus TryLeaveArthur(out string reason);
    Chapter3ArthurMutationStatus TryFacePlayer(out string reason);
    Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason);
    Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason);
    Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason);
    Chapter3ArthurMutationStatus TryResumeWalk(out string reason);
    Chapter3ArthurMutationStatus TryDedupeArthur(out string reason);
    // OC-135: the beating punches share the Punch trigger with the owner QA anim probe; the probe goes away before release.
    Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason);
    // OC-135: drains one suppressed native impact on Arthur: true once
    // per recorded hit, false when nothing was recorded since the last
    // poll. Arthur takes no damage, so this is the attack signal; the
    // health-drop polling stays as the fallback.
    bool PollArthurImpact();
}

/// <summary>
/// OC-130: one evaluated placement candidate. The runtime generates ring
/// points around the player, snaps each to the navmesh, and checks
/// reachability; <see cref="Chapter3ArthurPlacement"/> then picks purely
/// from these evaluated rows, so the selection is test linkable.
/// </summary>
public sealed record Chapter3ArthurCandidate(
    (float X, float Y, float Z) Point,
    float DistanceToPlayer,
    bool BehindPlayer,
    bool Reachable);
