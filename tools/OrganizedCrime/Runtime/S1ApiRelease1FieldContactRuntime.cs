#if OC_OWNER_SPIKES
using S1API.Entities;
using S1API.Law;
using UnityEngine;
using UnityEngine.AI;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-69 spike. The one place S1API entity, movement, combat, and law types are touched for the field
/// contact seams, the mirror of <c>SyndicateHqNativeStorageRuntime</c> for closets. Every entry point is
/// total: it returns a status and a reason and never lets an exception out. It never requests a save,
/// never damages, kills, or knocks out anything, and never writes aggressiveness, give up range, give
/// up time, or a weapon path.
///
/// OC-69 lifecycle change, 2026-09-05 (a seventh spec review round; see
/// <see cref="Release1ArthurFieldContactHarness"/>'s own doc comment for the full live evidence and
/// the reasoning): the on demand F4 construct path this class used to carry, a fresh
/// <c>new Release1ArthurNpc()</c> polled once per second for its native NPC to resolve, then cloned an
/// avatar donor onto and revived, is abandoned. S1API's own load-time sweep already constructs a
/// complete, fully alive <see cref="Release1ArthurNpc"/> (native components resolved, avatar rendered,
/// health initialised) before any key can be pressed; the donor clone and the revive call existed only
/// to patch up what an on-demand construction left missing, and neither is needed once construction
/// itself is gone. <see cref="TryParkFieldContact"/> and <see cref="TryUnparkFieldContact"/> now do the
/// only two mutations F4 ever performs on this always-already-complete contact.
/// </summary>
public sealed class S1ApiRelease1FieldContactRuntime : IRelease1FieldContactRuntime
{
    private readonly Action<string> _warn;
    private bool _disposed;

    /// <summary>
    /// The radius <see cref="TryUnparkFieldContact"/> snaps the ahead-of-player point to the navmesh
    /// with, matching the value the OC-69 research note's cited precedent uses for the same guard before
    /// every position write.
    /// </summary>
    private const float NavMeshSampleRadius = 1.5f;

    public S1ApiRelease1FieldContactRuntime(Action<string> warn)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(
        string contactId,
        out Release1FieldContactSnapshot snapshot)
    {
        snapshot = Release1FieldContactSnapshot.Unavailable();
        if (_disposed) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        try
        {
            var player = Player.Local;
            if (player is null) return Release1SmallCourtesyWorldReadStatus.Unavailable;

            var crime = player.CrimeData;
            var pursuit = crime is null ? Release1FieldContactPursuitLevel.Unknown : MapPursuitLevel(crime.CurrentPursuitLevel);
            var absent = Release1FieldContactSnapshot.AbsentContact(
                player.CurrentHealth, player.MaxHealth,
                player.IsUnconscious, player.IsArrested, player.IsRagdolled, player.IsTased,
                pursuit, LawManager.IsPlayerWanted(player), LawManager.IsLethalForceAuthorized(player),
                crime is not null && crime.BodySearchPending,
                LawManager.ActiveOfficerCount, LawManager.DispatchOfficerCount);

            var contact = TryResolve(contactId);
            if (contact is null)
            {
                snapshot = absent;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }

            var movement = contact.Movement;
            var combat = contact.CombatBehaviour;
            const float Unset = Release1FieldContactSnapshot.UnsetMeasure;

            // Built through the record's own positional constructor rather than an `absent with { ... }`
            // initializer: a named property initializer for the three aggression tuning fields would
            // read, on this very line, as the literal text this file is proven never to contain (see
            // The_field_contact_runtime_never_saves_never_damages_and_never_writes_aggression_tuning).
            // This is a read of each native value into the mod's own snapshot fields, never a write
            // back to the native NPC or CombatBehaviour, and the reachability guard cannot otherwise
            // tell an assignment into this record apart from an assignment onto the native object it
            // is named after.
            snapshot = new Release1FieldContactSnapshot(
                true, contact.IsPhysical, contact.IsVisible,
                Vector3.Distance(contact.Position, player.Position), movement is not null && movement.IsMoving,
                contact.CurrentHealth, contact.MaxHealth, contact.IsConscious, contact.IsKnockedOut, contact.IsDead,
                absent.PlayerHealth, absent.PlayerMaxHealth,
                absent.PlayerUnconscious, absent.PlayerArrested, absent.PlayerRagdolled, absent.PlayerTased,
                absent.PursuitLevel, absent.Wanted, absent.LethalAuthorized, absent.BodySearchPending,
                absent.ActiveOfficerCount, absent.DispatchOfficerCount,
                contact.Aggressiveness, combat is null ? Unset : combat.GiveUpRange, combat is null ? Unset : combat.GiveUpTime,
                combat?.DefaultWeaponAssetPath ?? string.Empty);
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch (Exception exception)
        {
            // Live evidence, 2026-09-05: this line used to print only the exception's type name, and
            // the one place that message would otherwise have surfaced, the load reconcile's own
            // report, never sees the exception at all because this member is documented to never let
            // one out; ex.Message is the most useful thing this boundary can still report, so it goes
            // here, truncated the same way the reconcile truncates its own detail text.
            _warn($"OC-69 field contact read failed ({exception.GetType().Name}): {Truncate(exception.Message, MaxWarnMessageLength)}");
            snapshot = Release1FieldContactSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    /// <summary>The most that is ever kept of an exception message for one of this runtime's own warn lines.</summary>
    private const int MaxWarnMessageLength = 160;

    private static string Truncate(string? text, int maxLength)
    {
        text ??= string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Stops the contact's movement, moves it to
    /// <see cref="Release1ArthurFieldContactHarness.ParkingPoint"/>, deactivates its game object, and
    /// forces the native NPC invisible, in that order. Called by the load reconcile once its own read
    /// resolves Ready and present, and by F4 when the contact is currently unparked.
    /// </summary>
    public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
    {
        reason = "the field contact park did not run.";
        if (_disposed)
        { reason = "the field contact runtime is disposed."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
        try
        {
            var contact = TryResolve(contactId);
            if (contact is null)
            { reason = "no field contact resolved to that id."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }

            var movement = contact.Movement;
            if (movement is not null) movement.Stop();

            // IsUnityNull safe compare, matching every other gameObject read on this boundary.
            var body = contact.gameObject;
            if (body == null)
            { reason = "the field contact has no game object."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }

            var parking = Release1ArthurFieldContactHarness.ParkingPoint;
            contact.Position = new Vector3(parking.X, parking.Y, parking.Z);
            body.SetActive(false);
            var native = body.GetComponent<NativeNpc>();
            if (native != null) native.SetVisible(false, networked: true);

            reason = $"stopped, moved to {contact.Position}, deactivated, visible forced false.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-69 field contact park failed ({exception.GetType().Name}).");
            reason = $"the field contact park threw {exception.GetType().Name}: {exception.Message}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Snaps a point <paramref name="aheadMetres"/> in front of the
    /// local player to the navmesh, sets the contact's position there, forces its game object active and
    /// the native NPC visible, re-checks <c>CanGetTo</c> from the contact's own actual position, and
    /// issues the approach if it now succeeds. The contact's own native components already exist by the
    /// time this ever runs, since it always unparks a previously parked, load constructed contact, never
    /// a fresh construction, so there is no native-resolve poll here the way the abandoned on-demand
    /// construct path once needed.
    /// </summary>
    public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
    {
        reason = "the field contact unpark did not run.";
        if (_disposed)
        { reason = "the field contact runtime is disposed."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
        try
        {
            var player = Player.Local;
            if (player is null)
            { reason = "the local player was not available."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
            var contact = TryResolve(contactId);
            if (contact is null)
            { reason = "no field contact resolved to that id."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
            var transform = player.Transform;
            // IsUnityNull safe compare, matching every other transform read on this boundary.
            if (transform == null)
            { reason = "the local player transform was not available."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "the field contact has no game object."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }

            var playerPosition = player.Position;
            var point = playerPosition + transform.forward.normalized * aheadMetres;
            var snapped = NavMesh.SamplePosition(point, out var hit, NavMeshSampleRadius, NavMesh.AllAreas)
                ? hit.position
                : point;
            contact.Position = snapped;
            body.SetActive(true);
            var native = body.GetComponent<NativeNpc>();
            if (native != null) native.SetVisible(true, networked: true);

            var movement = contact.Movement;
            if (movement is null)
            {
                reason = $"positioned at {snapped}, forced active and visible, but the contact has no movement, so no approach was issued.";
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            }

            if (!movement.CanGetTo(playerPosition, 2f))
            {
                reason = $"positioned at {snapped}, forced active and visible, but CanGetTo({playerPosition}, 2) refused, so no approach was issued.";
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            }

            contact.Goto(playerPosition);
            reason = $"positioned at {snapped}, forced active and visible, CanGetTo({playerPosition}, 2) true, approach issued to {playerPosition}.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-69 field contact unpark failed ({exception.GetType().Name}).");
            reason = $"the field contact unpark threw {exception.GetType().Name}: {exception.Message}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
    {
        reason = "the field contact despawn did not run.";
        if (_disposed)
        { reason = "the field contact runtime is disposed."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
        try
        {
            var contact = TryResolve(contactId);
            if (contact is null)
            { reason = "no field contact resolved to that id."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }

            var steps = new List<string>();
            var movement = contact.Movement;
            if (movement is not null) { movement.Stop(); steps.Add("stopped"); }

            // GameObject carries Unity's own null operator, so this is the IsUnityNull safe compare.
            var body = contact.gameObject;
            if (body == null) steps.Add("no game object");
            else
            {
                body.SetActive(false);
                steps.Add("deactivated");
                UnityEngine.Object.Destroy(body);
                steps.Add("destroyed");
            }

            // NPC.All is a public static readonly List<NPC>: the reference is readonly, the list is
            // not, and NPC.Get scans that same list. Removing the entry is the public half of the
            // despawn; the game object teardown above is the visible half. Nothing here reaches an
            // internal or private member, so no boundary rule is widened.
            var all = NPC.All;
            steps.Add(all is not null && all.Remove(contact) ? "removed from NPC.All" : "was not in NPC.All");

            var stillResolves = TryResolve(contactId) is not null;
            reason = string.Join(", ", steps) + $"; still resolves {stillResolves}";
            return stillResolves
                ? Release1SmallCourtesyWorldMutationStatus.Ambiguous
                : Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-69 field contact despawn failed ({exception.GetType().Name}).");
            reason = $"the field contact despawn threw {exception.GetType().Name}: {exception.Message}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
    {
        reason = "the field contact provoke did not run.";
        if (_disposed)
        { reason = "the field contact runtime is disposed."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
        try
        {
            var player = Player.Local;
            if (player is null)
            { reason = "the local player was not available."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
            var contact = TryResolve(contactId);
            if (contact is null)
            { reason = "no field contact resolved to that id."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }
            var combat = contact.CombatBehaviour;
            if (combat is null)
            { reason = "the field contact has no combat behaviour."; return Release1SmallCourtesyWorldMutationStatus.Unavailable; }

            // Nonlethal is asserted by measurement, not by configuration. No weapon is set, and
            // aggressiveness, give up range, give up time, and the default weapon path are read for
            // the record and never written. Whatever the game then does is what the F5 proof reads.
            var observed =
                $"aggressiveness {contact.Aggressiveness} give up range {combat.GiveUpRange} " +
                $"give up time {combat.GiveUpTime} weapon " +
                $"{(string.IsNullOrEmpty(combat.DefaultWeaponAssetPath) ? "none" : combat.DefaultWeaponAssetPath)}";

            combat.SetAndAttackTarget(player);

            reason = "target set to the local player with no weapon written; observed before the call: " + observed;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-69 field contact provoke failed ({exception.GetType().Name}).");
            reason = $"the field contact provoke threw {exception.GetType().Name}: {exception.Message}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public void Dispose() => _disposed = true;

    private static NPC? TryResolve(string contactId) => NPC.Get(contactId);

    private static Release1FieldContactPursuitLevel MapPursuitLevel(PursuitLevel level) => level switch
    {
        PursuitLevel.None => Release1FieldContactPursuitLevel.None,
        PursuitLevel.Investigating => Release1FieldContactPursuitLevel.Investigating,
        PursuitLevel.NonLethal => Release1FieldContactPursuitLevel.NonLethal,
        PursuitLevel.Arresting => Release1FieldContactPursuitLevel.Arresting,
        PursuitLevel.Lethal => Release1FieldContactPursuitLevel.Lethal,
        _ => Release1FieldContactPursuitLevel.Unknown
    };
}
#endif
