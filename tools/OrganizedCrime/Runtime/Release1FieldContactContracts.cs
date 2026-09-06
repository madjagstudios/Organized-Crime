namespace OrganizedCrime.Runtime;

/// <summary>
/// The mod's own mirror of <c>S1API.Law.PursuitLevel</c>. The S1API enum never crosses
/// <see cref="IRelease1SmallCourtesyWorld"/>: every record there stays free of Unity and S1API types
/// so the harness above it stays test linkable. <see cref="Unknown"/> is the extra value the S1API
/// enum does not have, and it is what an unavailable read reports.
/// </summary>
public enum Release1FieldContactPursuitLevel
{
    Unknown, None, Investigating, NonLethal, Arresting, Lethal
}

/// <summary>
/// OC-69 spike observation record. One read of the contact, the player, and the law, printed a line
/// at a time by <see cref="Release1ArthurFieldContactHarness"/>. Contact only fields carry
/// <see cref="UnsetMeasure"/>, <see cref="UnsetCount"/>, false, or the empty string when the contact
/// does not resolve, while the player and law fields still read: that is the absent contact case the
/// verification gate requires. Every field is a primitive or this file's own enum, so nothing here is a
/// Unity or S1API type and no destination position crosses at all.
/// </summary>
public sealed record Release1FieldContactSnapshot(
    bool Present, bool Physical, bool Visible,
    float DistanceToPlayer, bool Moving,
    float ContactHealth, float ContactMaxHealth, bool Conscious, bool KnockedOut, bool Dead,
    float PlayerHealth, float PlayerMaxHealth,
    bool PlayerUnconscious, bool PlayerArrested, bool PlayerRagdolled, bool PlayerTased,
    Release1FieldContactPursuitLevel PursuitLevel, bool Wanted, bool LethalAuthorized, bool BodySearchPending,
    int ActiveOfficerCount, int DispatchOfficerCount,
    float Aggressiveness, float GiveUpRange, float GiveUpTime, string WeaponAssetPath)
{
    /// <summary>The defined unset value for every float field. Never confusable with a real reading.</summary>
    public const float UnsetMeasure = -1f;

    /// <summary>The defined unset value for both officer counts.</summary>
    public const int UnsetCount = -1;

    public static Release1FieldContactSnapshot Unavailable() => new(
        false, false, false, UnsetMeasure, false, UnsetMeasure, UnsetMeasure, false, false, false,
        UnsetMeasure, UnsetMeasure, false, false, false, false,
        Release1FieldContactPursuitLevel.Unknown, false, false, false, UnsetCount, UnsetCount,
        UnsetMeasure, UnsetMeasure, UnsetMeasure, string.Empty);

    public static Release1FieldContactSnapshot AbsentContact(
        float playerHealth, float playerMaxHealth,
        bool playerUnconscious, bool playerArrested, bool playerRagdolled, bool playerTased,
        Release1FieldContactPursuitLevel pursuitLevel, bool wanted, bool lethalAuthorized, bool bodySearchPending,
        int activeOfficerCount, int dispatchOfficerCount) => Unavailable() with
    {
        PlayerHealth = playerHealth,
        PlayerMaxHealth = playerMaxHealth,
        PlayerUnconscious = playerUnconscious,
        PlayerArrested = playerArrested,
        PlayerRagdolled = playerRagdolled,
        PlayerTased = playerTased,
        PursuitLevel = pursuitLevel,
        Wanted = wanted,
        LethalAuthorized = lethalAuthorized,
        BodySearchPending = bodySearchPending,
        ActiveOfficerCount = activeOfficerCount,
        DispatchOfficerCount = dispatchOfficerCount
    };

    public void Validate()
    {
        if (!Enum.IsDefined(PursuitLevel))
            throw new ArgumentException("Pursuit level is not defined.", nameof(PursuitLevel));
        if (WeaponAssetPath is null) throw new ArgumentNullException(nameof(WeaponAssetPath));
        foreach (var (name, measure) in new (string Name, float Measure)[]
                 {
                     (nameof(DistanceToPlayer), DistanceToPlayer), (nameof(ContactHealth), ContactHealth),
                     (nameof(ContactMaxHealth), ContactMaxHealth), (nameof(PlayerHealth), PlayerHealth),
                     (nameof(PlayerMaxHealth), PlayerMaxHealth), (nameof(Aggressiveness), Aggressiveness),
                     (nameof(GiveUpRange), GiveUpRange), (nameof(GiveUpTime), GiveUpTime)
                 })
        {
            if (!float.IsFinite(measure))
                throw new ArgumentException("Field contact measures must be finite.", name);
        }
        if (ActiveOfficerCount < UnsetCount)
            throw new ArgumentOutOfRangeException(nameof(ActiveOfficerCount), "Officer counts cannot be below the unset value.");
        if (DispatchOfficerCount < UnsetCount)
            throw new ArgumentOutOfRangeException(nameof(DispatchOfficerCount), "Officer counts cannot be below the unset value.");
        if (Present) return;
        if (Physical || Visible || Moving || Conscious || KnockedOut || Dead ||
            DistanceToPlayer != UnsetMeasure || ContactHealth != UnsetMeasure || ContactMaxHealth != UnsetMeasure ||
            Aggressiveness != UnsetMeasure || GiveUpRange != UnsetMeasure || GiveUpTime != UnsetMeasure ||
            WeaponAssetPath.Length != 0)
            throw new ArgumentException("An absent contact must carry unset contact fields.", nameof(Present));
    }
}

/// <summary>
/// The one place S1API entity, movement, combat, and law types are touched for the OC-69 spike, the
/// mirror of <see cref="ISyndicateHqStorageRuntime"/> for closets. Implemented in Task 2 by
/// <c>S1ApiRelease1FieldContactRuntime</c>; when it is absent the world access layer answers
/// Unavailable or Rejected, exactly as it does for a null hold room storage runtime.
/// </summary>
public interface IRelease1FieldContactRuntime : IDisposable
{
    Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot);

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Stops the contact's own movement, moves it to
    /// <see cref="Release1ArthurFieldContactHarness.ParkingPoint"/>, deactivates its game object, and
    /// forces the native NPC invisible. Used both by the load reconcile (parking the contact S1API's
    /// own load-time sweep already built, instead of despawning it) and by F4 when the contact is
    /// currently unparked.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Snaps a point <paramref name="aheadMetres"/> in front of the
    /// local player to the navmesh, sets the contact's position there, forces its game object active and
    /// the native NPC visible, re-checks <c>CanGetTo</c> from the contact's own actual position, and
    /// issues the approach if it now succeeds. Used by F4 when the contact is currently parked. The
    /// contact's own components already exist by the time this runs (it is always a previously parked,
    /// load constructed contact, never a fresh construction), so no native-resolve poll is needed the
    /// way the abandoned on-demand construct path once required.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason);

    /// <summary>
    /// Kept for the no-persistence fallback (a full despawn is the out of game cleanup path, never
    /// called from F4): removes the field contact and reports whether it still resolves afterwards.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason);

    Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason);
}
