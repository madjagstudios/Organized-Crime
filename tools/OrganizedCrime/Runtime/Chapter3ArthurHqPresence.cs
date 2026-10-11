using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-132: what Arthur's visit pump needs to know about the Syndicate
/// HQ. A two-boolean boundary so the pump stays test linkable: the
/// live adapter reads the Chapter 3 phase and the HQ service's own
/// inside state, and tests fake both.
/// </summary>
public interface IChapter3HqPresence
{
    /// <summary>Chapter 3 is the active chapter.</summary>
    bool ChapterActive { get; }

    /// <summary>The local player is inside the HQ pocket room.</summary>
    bool PlayerInsideHq { get; }
}

/// <summary>
/// The override for Arthur's HQ presence, behind <c>oc_ch3 arthur hq on | off | auto</c>.
/// Auto is the shipped behaviour: present while Chapter 3 is active and the player is inside
/// the HQ.
/// </summary>
public enum Chapter3ArthurHqForce
{
    Auto,
    On,
    Off
}

/// <summary>
/// <summary>
/// OC-132: Arthur's HQ spot, in the interior frame from
/// <c>SyndicateHqInteriorDefinition</c>: beside his own desk on the
/// west side, facing the door (yaw 180). One place so the owner can
/// retune the numbers after the run. The runtime adds the local spot
/// to the interior root position, which the HQ service sets to
/// <c>GetPocketRoot(exteriorAnchor)</c> at creation, and snaps the
/// height to the Floor primitive's top from the definition. He is
/// never parented to the interior root, so the save-time interior
/// rebuild cannot take him with it. The owner-QA <c>hq here</c>
/// command overrides this for the session, in memory only.
/// </summary>
public static class Chapter3ArthurHqSpot
{
    // Clearance at (-2.9, 0, 3.7): 1.25 m to the enforcer armchair
    // (nearest floor-furniture centre), 1.18 m to the north wall face,
    // 1.57 m to the boss armchair. Whisky/radio decor excluded: small
    // items riding on desks, not floor blockers.
    public static readonly (float X, float Y, float Z) Local = (-2.9f, 0f, 3.7f);

    public const float YawDegrees = 180f;

    /// <summary>
    /// Turns to face the player while inside and within this range,
    /// measured on the horizontal plane like every other Arthur range.
    /// </summary>
    public const float FaceRangeMetres = 6f;

    /// <summary>
    /// The HQ maintain cadence: place-ensure, face, and attack
    /// checks run this often while he stands at the spot. Fast
    /// enough to hold him through a save-time interior rebuild
    /// without a visible fall, slow enough that the interior-root
    /// lookup stays cheap.
    /// </summary>
    public const float MaintainIntervalSeconds = 0.25f;

    /// <summary>
    /// Adds the local spot to a pocket root, mirroring the runtime's
    /// own conversion (interior root position plus local spot, with
    /// the definition's floor top added to the root height).
    /// </summary>
    public static (float X, float Y, float Z) ToWorld((float X, float Y, float Z) pocketRoot, float floorTopY) =>
        (pocketRoot.X + Local.X, pocketRoot.Y + floorTopY, pocketRoot.Z + Local.Z);

    /// <summary>
    /// The inverse: a world position minus the pocket root, into the
    /// interior's local frame. Behind the owner-QA <c>hq here</c>
    /// tuning command.
    /// </summary>
    public static (float X, float Y, float Z) ToLocal((float X, float Y, float Z) world, (float X, float Y, float Z) pocketRoot) =>
        (world.X - pocketRoot.X, world.Y - pocketRoot.Y, world.Z - pocketRoot.Z);

    /// <summary>
    /// Whether a local point reads as inside the HQ pocket room: the
    /// interior frame with a small height allowance for a standing
    /// player. Behind the <c>hq here</c> outside-HQ refusal.
    /// </summary>
    public static bool IsInsideFrame((float X, float Y, float Z) local) =>
        Math.Abs(local.X) <= SyndicateHqInteriorDefinition.Width / 2f &&
        Math.Abs(local.Z) <= SyndicateHqInteriorDefinition.Depth / 2f &&
        local.Y >= -2f && local.Y <= SyndicateHqInteriorDefinition.Height;
}


/// OC-132: the live <see cref="IChapter3HqPresence"/>. Reads are
/// total (a throw reads as not present) and it holds only
/// delegates, so it stays test linkable: Mod wires the Chapter 3
/// phase on one side and <c>SyndicateHqRuntimeService.IsInside</c>
/// -- the HQ service's own entry-state machine -- on the other.
/// </summary>
public sealed class Chapter3SyndicateHqPresence : IChapter3HqPresence
{
    private readonly Func<bool> _chapterActive;
    private readonly Func<bool> _playerInsideHq;

    public Chapter3SyndicateHqPresence(Func<bool> chapterActive, Func<bool> playerInsideHq)
    {
        _chapterActive = chapterActive ?? throw new ArgumentNullException(nameof(chapterActive));
        _playerInsideHq = playerInsideHq ?? throw new ArgumentNullException(nameof(playerInsideHq));
    }

    public bool ChapterActive
    {
        get
        {
            try
            {
                return _chapterActive();
            }
            catch
            {
                return false;
            }
        }
    }

    public bool PlayerInsideHq
    {
        get
        {
            try
            {
                return _playerInsideHq();
            }
            catch
            {
                return false;
            }
        }
    }
}
