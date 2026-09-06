#if OC_OWNER_SPIKES
namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-69 owner QA harness, gate keys F3, F4 and F5. Proves, without any mission logic and without
/// touching the story, whether OC can put a physical contact beside the player, walk him to the
/// player, take him out again, and provoke the game's own aggression. F3 is <see cref="TryReport"/>, a
/// standalone read pressable any number of times: without it the player attacks him case cannot be
/// measured, since a read reachable only by first provoking cannot observe a player initiated attack.
/// F5 is <see cref="TryProvoke"/>, which provokes then reports in the same press, but only when the
/// caller (Mod.cs) has already confirmed the contact is unparked; a parked contact refuses F5 with a
/// reason rather than provoking something the owner cannot see or reach. No engine or native library
/// reference here, so it stays test linkable, exactly as the five shipped harnesses do. It never
/// requests a save, never damages, kills, or knocks out the contact, and never writes aggressiveness,
/// give up range, give up time, or a weapon path: every one of those is read and printed so a later
/// tier design starts from observed defaults.
///
/// OC-69 parked lifecycle, 2026-09-05 (a seventh spec review round: on-demand construction abandoned,
/// the parked lifecycle adopted). Live evidence, 2026-09-05, build 12347e1: the Arthur the game
/// plugin's own load-time reflection sweep constructs comes up complete on every load, with no key
/// ever pressed: read Ready after three seconds, present, physical, visible, health 100 of 100,
/// conscious, aggressiveness 0.1. The load reconcile that used to despawn this load constructed
/// contact was throwing away a complete, working contact every single load, only for F4's own on
/// demand construction to then rebuild an incomplete one from nothing: the avatar donor lookup found
/// no donor because the plugin's own <c>NPC.All</c> holds only custom NPCs (a fresh physical custom NPC's
/// own load-time sweep sibling was never a member of it either, so the "first vanilla NPC in NPC.All"
/// fallback the sixth review round added had nothing to find), and <c>NPCHealth.Revive()</c> threw a
/// <c>NullReferenceException</c> in its own
/// <c>get_MaxHealth</c> because the component genuinely does not exist on a post-load, on-demand
/// construction the way it does on a load-time one; the owner never saw him at all.
///
/// F4's own on demand construct path is deleted along with the fix built to patch around its two
/// defects (the avatar donor preference and its clone code, and the health revive call); their own
/// tests are deleted with them, named in this ticket's own worknote and commit trailer. The new
/// lifecycle instead keeps the load-constructed contact alive across the whole session and only ever
/// toggles it between two states: <b>parked</b> (out of play, tracked in <c>Mod.cs</c>'s own
/// <c>_arthurFieldContactParked</c> flag, set true the moment the load reconcile parks a freshly
/// resolved contact) and <b>unparked</b> (positioned beside the player, active, visible). The load
/// reconcile now calls <see cref="TryPark"/> instead of despawning once its own retried read finally
/// resolves Ready and present, logging <c>OC-69 parked the load constructed field contact</c> once. F4
/// then toggles between <see cref="TryUnpark"/> and <see cref="TryPark"/> depending on that flag; when
/// no load constructed contact has ever been found present (a fresh save that has not yet reached its
/// next load), F4 logs one line saying so and does nothing else, per <see cref="ToggleFromSnapshot"/>.
///
/// <see cref="TryDespawn"/> is kept on this harness and on the world boundary for the no-persistence
/// fallback (OC-70's own out of game cleanup note), but nothing in Mod.cs's F4 handler calls it any
/// more: parking, not despawning, is the ticket's own everyday lifecycle now. Persistence of the parked
/// contact in the save's own <c>NPCs.json</c> is accepted for this spike and is explicitly forbidden for
/// release per OC-70, which must plan to exclude the physical spike entirely from the player build.
/// </summary>
public static class Release1ArthurFieldContactHarness
{
    /// <summary>
    /// Deliberately not <c>oc_release1_arthur</c>. A spike id keeps any residue in a save
    /// distinguishable from a future shipped Arthur and makes an out of game cleanup unambiguous.
    /// </summary>
    public const string ContactId = "oc_release1_arthur_spike";

    /// <summary>
    /// Frozen at three metres. The unparked contact is placed at the player's position plus the
    /// player's own forward direction times this, one point and one reachability call, never a
    /// candidate search.
    /// </summary>
    public const float AheadMetres = 3f;

    /// <summary>
    /// The fixed park coordinate: far beneath any player-reachable area of the map. A tuple of floats,
    /// not a native engine position type, so this file stays free of any engine or framework reference
    /// (see <c>The_harness_source_holds_no_unity_and_no_s1api_reference</c>); the field contact runtime
    /// is the one place this is converted into a native position when the parked contact is actually
    /// moved there.
    /// </summary>
    public static readonly (float X, float Y, float Z) ParkingPoint = (0f, -10000f, 0f);

    private delegate Release1SmallCourtesyWorldMutationStatus Mutation(out string reason);

    /// <summary>
    /// Kept for the no-persistence fallback (OC-70's own out of game cleanup note); a full despawn is
    /// never called from F4 any more. Removes the field contact and reports whether it still resolves
    /// afterward, exactly as it always has.
    /// </summary>
    public static Release1StagingHarnessResult TryDespawn(IRelease1SmallCourtesyWorld world) =>
        Run(world, $"despawn {ContactId}", "despawning", true,
            (out string reason) => world.TryDespawnFieldContact(ContactId, out reason));

    public static Release1StagingHarnessResult TryProvoke(IRelease1SmallCourtesyWorld world) =>
        Run(world, $"provoke {ContactId}", "provoking", false,
            (out string reason) => world.TryProvokeFieldContact(ContactId, out reason));

    /// <summary>
    /// Stops the contact's movement, moves it to <see cref="ParkingPoint"/>, deactivates its game
    /// object, and forces the native NPC invisible. Called by the load reconcile once its own retried
    /// read finally resolves Ready and present, and by F4 when the contact is currently unparked.
    /// </summary>
    public static Release1StagingHarnessResult TryPark(IRelease1SmallCourtesyWorld world) =>
        Run(world, $"park {ContactId}", "parking", false,
            (out string reason) => world.TryParkFieldContact(ContactId, out reason));

    /// <summary>
    /// Snaps a point <see cref="AheadMetres"/> in front of the player to the navmesh, sets the
    /// contact's position there, forces its game object active and the native NPC visible, re-checks
    /// <c>CanGetTo</c> from the contact's own actual position, and issues the approach. Called by F4
    /// when the contact is currently parked. The report this prints (through <see cref="Run"/>'s own
    /// confirm read) is expected Ready on the very same pass, since the contact's own native components
    /// already exist: unparking never constructs anything, unlike the abandoned on-demand spawn path.
    /// </summary>
    public static Release1StagingHarnessResult TryUnpark(IRelease1SmallCourtesyWorld world) =>
        Run(world, $"unpark {ContactId} ahead metres {AheadMetres}", "unparking", false,
            (out string reason) => world.TryUnparkFieldContact(ContactId, AheadMetres, out reason));

    /// <summary>
    /// F4's own entry point. When <paramref name="parked"/> is true, the contact is unparked straight
    /// away with no read at all: Mod.cs's own flag is the only place this ever becomes true, always
    /// right after a successful park, so trusting it here needs no confirming read first, the same way
    /// the pre-parked-lifecycle <c>TryToggle</c> trusted <c>spawnedByHarness</c> for an immediate
    /// despawn. When <paramref name="parked"/> is false, a presence read decides between parking a
    /// contact that is present and unparked (an active load-time construction, or one the owner
    /// unparked earlier this session) and reporting that no load constructed contact exists at all yet
    /// (see <see cref="ToggleFromSnapshot"/>). A presence read that is not immediately Ready reports the
    /// read status instead of guessing, so a caller can retry before deciding.
    /// </summary>
    public static Release1StagingHarnessResult TryToggle(IRelease1SmallCourtesyWorld world, ref bool parked)
    {
        if (world is null) return Faulted("the world was not available.");

        if (parked)
        {
            parked = false;
            return TryUnpark(world);
        }

        Release1SmallCourtesyWorldReadStatus readStatus;
        Release1FieldContactSnapshot? snapshot;
        try
        {
            readStatus = world.TryReadFieldContact(ContactId, out snapshot);
        }
        catch (Exception ex)
        {
            return Faulted("an exception was thrown while reading before toggling: " + ex.Message);
        }

        if (readStatus != Release1SmallCourtesyWorldReadStatus.Ready || snapshot is null)
        {
            return new(MapReadStatus(readStatus),
                new[] { $"read {ContactId} status {readStatus} before deciding whether to park or report no contact." });
        }

        return ToggleFromSnapshot(world, snapshot, ref parked);
    }

    /// <summary>
    /// The Ready half of <see cref="TryToggle"/>'s own decision, extracted so Mod.cs's F4 handler can
    /// reach it directly once a presence read it retried on its own resolves Ready, without asking
    /// <see cref="TryToggle"/> to read the contact a second time. An absent contact means no load
    /// constructed field contact has ever been found this session (a fresh save that has not yet
    /// reached its next load): this logs one line and does nothing else, per this ticket's own removal
    /// of the on-demand construct path. A present contact is parked.
    /// </summary>
    public static Release1StagingHarnessResult ToggleFromSnapshot(
        IRelease1SmallCourtesyWorld world, Release1FieldContactSnapshot snapshot, ref bool parked)
    {
        if (!snapshot.Present)
        {
            parked = false;
            return new(Release1StagingHarnessStatus.Unavailable,
                new[] { $"no load constructed field contact exists yet; {ContactId} is built by the game's own load-time sweep at the next load." });
        }

        parked = true;
        return TryPark(world);
    }

    private static Release1StagingHarnessResult Run(
        IRelease1SmallCourtesyWorld world,
        string header,
        string gerund,
        bool residueIsFailure,
        Mutation mutation)
    {
        if (world is null) return Faulted("the world was not available.");
        var lines = new List<string>();
        try
        {
            var status = mutation(out var reason);
            lines.Add($"{header} status {status}");
            lines.Add($"reason: {reason}");
            var present = AppendReport(world, lines);
            if (residueIsFailure && status == Release1SmallCourtesyWorldMutationStatus.Succeeded && present)
            {
                lines.Add("the contact still resolves after a succeeded despawn.");
                return new(Release1StagingHarnessStatus.Faulted, lines);
            }
            return new(MapMutationStatus(status), lines);
        }
        catch (Exception ex)
        {
            lines.Add($"an exception was thrown while {gerund}: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    /// <summary>
    /// F3's own entry point, pressable any number of times. <paramref name="parked"/> is Mod.cs's own
    /// bookkeeping flag, printed as a plain line rather than read from the world: parked-ness is not a
    /// native fact <see cref="Release1FieldContactSnapshot"/> observes, only a record of which of the
    /// two F4 mutations, <see cref="TryPark"/> or <see cref="TryUnpark"/>, ran last.
    /// </summary>
    public static Release1StagingHarnessResult TryReport(IRelease1SmallCourtesyWorld world, bool parked)
    {
        if (world is null) return Faulted("the world was not available.");
        var lines = new List<string>();
        try
        {
            var status = world.TryReadFieldContact(ContactId, out var snapshot);
            lines.Add($"read {ContactId} status {status}");
            lines.Add($"parked {parked}");
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || snapshot is null)
                return new(MapReadStatus(status), lines);
            AppendSnapshot(lines, snapshot);
            // Ready with an absent contact is a real, trustworthy read of the player and the law, so
            // every line above still stands; the harness level status simply says there is no contact
            // to look at yet.
            return new(
                snapshot.Present ? Release1StagingHarnessStatus.Succeeded : Release1StagingHarnessStatus.Unavailable,
                lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while reading: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    private static bool AppendReport(IRelease1SmallCourtesyWorld world, List<string> lines)
    {
        var status = world.TryReadFieldContact(ContactId, out var snapshot);
        lines.Add($"read {ContactId} status {status}");
        if (status != Release1SmallCourtesyWorldReadStatus.Ready || snapshot is null) return false;
        AppendSnapshot(lines, snapshot);
        return snapshot.Present;
    }

    private static void AppendSnapshot(List<string> lines, Release1FieldContactSnapshot s)
    {
        lines.Add($"present {s.Present} physical {s.Physical} visible {s.Visible}");
        lines.Add($"distance to player {s.DistanceToPlayer} moving {s.Moving}");
        lines.Add($"contact health {s.ContactHealth} of {s.ContactMaxHealth} conscious {s.Conscious} " +
                  $"knocked out {s.KnockedOut} dead {s.Dead}");
        lines.Add($"player health {s.PlayerHealth} of {s.PlayerMaxHealth} unconscious {s.PlayerUnconscious} " +
                  $"arrested {s.PlayerArrested} ragdolled {s.PlayerRagdolled} tased {s.PlayerTased}");
        lines.Add($"pursuit level {s.PursuitLevel} wanted {s.Wanted} lethal authorized {s.LethalAuthorized} " +
                  $"body search pending {s.BodySearchPending}");
        lines.Add($"active officers {s.ActiveOfficerCount} dispatch officers {s.DispatchOfficerCount}");
        lines.Add($"aggressiveness {s.Aggressiveness} give up range {s.GiveUpRange} give up time {s.GiveUpTime} " +
                  $"weapon {(s.WeaponAssetPath.Length == 0 ? "none" : s.WeaponAssetPath)}");
    }

    private static Release1StagingHarnessResult Faulted(string line) =>
        new(Release1StagingHarnessStatus.Faulted, new[] { line });

    // MapReadStatus is copied from Release1TheEnvelopeClosetCashGateHarness, minus that harness's own
    // Ready arm: both of this harness's callers, TryReport and TryToggle, already return before this
    // mapper runs whenever status is Ready, so a Ready arm here is unreachable, not merely unused.
    private static Release1StagingHarnessStatus MapReadStatus(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldReadStatus.Pending => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        _ => Release1StagingHarnessStatus.Faulted
    };

    // Ambiguous maps to its own arm, not the Faulted default: the world's Ambiguous status covers
    // several expected, non exceptional mutation outcomes (a CanGetTo refusal, a despawn whose contact
    // still resolves, a mutation that threw after partially mutating something), every one of which the
    // owner protocol's own PASS and INCONCLUSIVE bars name as a status the harness prints, not a fault.
    private static Release1StagingHarnessStatus MapMutationStatus(Release1SmallCourtesyWorldMutationStatus status) => status switch
    {
        Release1SmallCourtesyWorldMutationStatus.Succeeded => Release1StagingHarnessStatus.Succeeded,
        Release1SmallCourtesyWorldMutationStatus.Rejected => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldMutationStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldMutationStatus.Ambiguous => Release1StagingHarnessStatus.Ambiguous,
        _ => Release1StagingHarnessStatus.Faulted
    };
}
#endif
