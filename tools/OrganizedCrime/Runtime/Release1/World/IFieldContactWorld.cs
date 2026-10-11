using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IFieldContactWorld
{
    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05 (on-demand construction abandoned; see
    /// <see cref="Release1ArthurFieldContactHarness"/>'s own doc comment). Stops the contact's movement,
    /// moves it to <see cref="Release1ArthurFieldContactHarness.ParkingPoint"/>, deactivates its game
    /// object, and forces the native NPC invisible. Rejected on a non authoritative host or an invalid
    /// id; Unavailable when nothing resolves to that id.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Snaps a point <paramref name="aheadMetres"/> in front of the
    /// local player to the navmesh, sets the contact's position there, forces its game object active and
    /// the native NPC visible, re-checks <c>CanGetTo</c> from the contact's own actual position, and
    /// issues the approach if it now succeeds. Rejected on a non authoritative host, an invalid id, or a
    /// non finite, non positive, or out of bound distance. Unlike the abandoned on-demand construct
    /// path, the contact's own native components already exist by the time this ever runs (it always
    /// unparks a previously parked, load constructed contact), so a read right after this call is
    /// expected Ready immediately, with no native-resolve poll needed.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason);

    /// <summary>
    /// Kept for the no-persistence fallback: removes the field contact and reports whether it still
    /// resolves afterwards. Unavailable when nothing resolves to that id; Ambiguous when the removal ran
    /// but it still does. A full despawn is the out of game cleanup path; F4 never calls this member.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 spike. Points the contact's own combat behaviour at the local player through the one
    /// S1API member that names the game's combat path, and returns. OC sets no weapon, writes no
    /// aggression tuning, and never damages anything; whatever the game then does is measured by
    /// <see cref="TryReadFieldContact"/>, never configured here.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 spike. Reads the contact, the local player, and the law in one pass. Ready whenever the
    /// host is authoritative and the local player is available, whether or not the contact resolves:
    /// an unresolved contact reads Present false with every contact only field at its defined unset
    /// value while every player and law field still reads live. Unavailable keeps the meaning every
    /// other read member here gives it, that the host is not authoritative or the local player is
    /// unavailable, in which case no field in the snapshot is to be trusted.
    /// </summary>
    Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot);
}
