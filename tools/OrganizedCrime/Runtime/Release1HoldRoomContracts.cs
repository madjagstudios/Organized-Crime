using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Whether the nine OC-48 HQ closets are all resolvable and readable this pass. Only
/// <see cref="Ready"/> permits a hold decision; every other value makes the hold check hold with a
/// status line and change nothing, so an unready room can never fail the player.
/// </summary>
public enum Release1HoldRoomReadiness
{
    Ready,
    NotReady,
    Unavailable
}

/// <summary>One contract closet's whole slot list, read only, never mutated by OC.</summary>
public sealed record Release1HoldRoomClosetSnapshot(
    string ClosetGuid,
    int SlotCount,
    IReadOnlyList<Release1SmallCourtesySlotSnapshot> Slots)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(ClosetGuid, nameof(ClosetGuid));
        if (SlotCount < 0) throw new ArgumentOutOfRangeException(nameof(SlotCount));
        if (Slots is null) throw new ArgumentNullException(nameof(Slots));
        if (Slots.Count != SlotCount)
            throw new ArgumentException("Closet slot count did not match its slot list.", nameof(SlotCount));
        foreach (var slot in Slots)
        {
            if (slot is null) throw new ArgumentException("Closet slots cannot be null.", nameof(Slots));
            slot.Validate();
        }
        if (Slots.Select(slot => slot.SlotIndex).Distinct().Count() != Slots.Count)
            throw new ArgumentException("Closet slot indices must be unique.", nameof(Slots));
    }
}

/// <summary>
/// The whole hold room as one read: readiness plus every contract closet's slots, in ordinal GUID
/// order. OC never writes any part of this.
/// </summary>
public sealed record Release1HoldRoomSnapshot(
    Release1HoldRoomReadiness Readiness,
    IReadOnlyList<Release1HoldRoomClosetSnapshot> Closets)
{
    public static Release1HoldRoomSnapshot NotReady() =>
        new(Release1HoldRoomReadiness.NotReady, Array.Empty<Release1HoldRoomClosetSnapshot>());

    public static Release1HoldRoomSnapshot Unavailable() =>
        new(Release1HoldRoomReadiness.Unavailable, Array.Empty<Release1HoldRoomClosetSnapshot>());

    public void Validate()
    {
        if (!Enum.IsDefined(Readiness)) throw new ArgumentException("Hold room readiness is not defined.", nameof(Readiness));
        if (Closets is null) throw new ArgumentNullException(nameof(Closets));
        if (Readiness != Release1HoldRoomReadiness.Ready && Closets.Count != 0)
            throw new ArgumentException("A room that is not Ready cannot carry closets.", nameof(Closets));
        foreach (var closet in Closets)
        {
            if (closet is null) throw new ArgumentException("Closets cannot be null.", nameof(Closets));
            closet.Validate();
        }
        if (Closets.Select(closet => closet.ClosetGuid).Distinct(StringComparer.Ordinal).Count() != Closets.Count)
            throw new ArgumentException("Closet GUIDs must be unique.", nameof(Closets));
    }
}

/// <summary>
/// What the whole hold room reads as for one consignment fingerprint this pass. Only Held and
/// Missing are actionable; RoomNotReady and Ambiguous always hold, so an unready room or a second
/// matching consignment can never fail the player.
/// </summary>
public enum Release1HoldObservation
{
    RoomNotReady,
    Held,
    Missing,
    Ambiguous
}

public static class Release1HoldRoomClassifier
{
    /// <summary>
    /// Scans the nine contract closets in ordinal GUID order and counts matching slots across the
    /// whole room. More than one exact match anywhere (two closets, or two slots in one closet) is
    /// Ambiguous; exactly one is Held with that closet's GUID; none is Missing. A slot that carries
    /// the same product and packaging as the fingerprint but a different quantity, for example two
    /// bricks stacked into one slot, is a stacked ambiguity signal: the consignment has not left the
    /// room, so it forces Ambiguous rather than reading as Missing, even when it is the only slot
    /// touching that product and packaging anywhere in the room. This rule lives here, not in the
    /// shared <see cref="Release1ConsignmentClassifier"/>, so Wrong Address staging classification is
    /// unaffected. A snapshot that is not Ready, or that does not present exactly
    /// expectedClosetCount closets, is RoomNotReady.
    /// </summary>
    public static Release1HoldObservation Classify(
        Release1HoldRoomSnapshot? room,
        Release1ConsignmentFingerprint fingerprint,
        int expectedClosetCount,
        out string? holdingClosetGuid)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        holdingClosetGuid = null;
        if (room is null ||
            room.Readiness != Release1HoldRoomReadiness.Ready ||
            room.Closets.Count != expectedClosetCount)
            return Release1HoldObservation.RoomNotReady;

        var matchCount = 0;
        var stackedAmbiguity = false;
        string? firstMatch = null;
        foreach (var closet in room.Closets.OrderBy(candidate => candidate.ClosetGuid, StringComparer.Ordinal))
        {
            var inCloset = closet.Slots.Count(fingerprint.Matches);
            if (inCloset > 0)
            {
                matchCount += inCloset;
                firstMatch ??= closet.ClosetGuid;
            }
            if (closet.Slots.Any(slot => IsStackedAmbiguity(fingerprint, slot))) stackedAmbiguity = true;
        }

        if (stackedAmbiguity || matchCount > 1) return Release1HoldObservation.Ambiguous;
        if (matchCount == 0) return Release1HoldObservation.Missing;
        holdingClosetGuid = firstMatch;
        return Release1HoldObservation.Held;
    }

    /// <summary>
    /// A slot that carries the fingerprint's exact product and packaging but not its quantity, such
    /// as two bricks stacked into a slot that expects one. Deliberately not reused from
    /// <see cref="Release1ConsignmentFingerprint.Matches"/>, which stays exact-quantity-only for
    /// every other caller.
    /// </summary>
    private static bool IsStackedAmbiguity(Release1ConsignmentFingerprint fingerprint, Release1SmallCourtesySlotSnapshot? slot) =>
        slot is not null &&
        slot.IsPackaged &&
        slot.Quantity != fingerprint.Quantity &&
        string.Equals(slot.ProductId, fingerprint.ProductId, StringComparison.Ordinal) &&
        string.Equals(slot.PackagingId, fingerprint.PackagingId, StringComparison.Ordinal);
}
