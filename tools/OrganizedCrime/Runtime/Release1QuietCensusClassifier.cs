using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// What the whole census reads as this pass: NotReady when any drop or the hold room could not be
/// read, or the observed counts do not match the frozen expectation; Present when the frozen product
/// is found anywhere, in any packaging, in any quantity; Clear only when every expected drop and
/// every expected closet read Ready and none of them carries the product at all. A snapshot list with
/// the wrong count is never partially trusted: a world mid layout change holds rather than reporting a
/// false Clear.
/// </summary>
public enum Release1QuietCensusObservation
{
    NotReady,
    Clear,
    Present
}

/// <summary>One dead drop's slot read this pass, carrying its own read status so a drop that failed to read cannot be silently treated as empty.</summary>
public sealed record Release1QuietCensusDropSnapshot(
    string DropGuid,
    Release1SmallCourtesyWorldReadStatus SlotsStatus,
    IReadOnlyList<Release1SmallCourtesySlotSnapshot> Slots);

public static class Release1QuietCensusClassifier
{
    public static Release1QuietCensusObservation Classify(
        IReadOnlyList<Release1QuietCensusDropSnapshot>? drops,
        Release1HoldRoomSnapshot? room,
        string productId,
        int expectedDropCount,
        int expectedClosetCount)
    {
        Release1SmallCourtesyAssignment.ValidateStableId(productId, nameof(productId));
        if (expectedDropCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedDropCount));
        if (expectedClosetCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedClosetCount));

        if (drops is null || drops.Count != expectedDropCount) return Release1QuietCensusObservation.NotReady;
        if (room is null || room.Readiness != Release1HoldRoomReadiness.Ready || room.Closets.Count != expectedClosetCount)
            return Release1QuietCensusObservation.NotReady;

        foreach (var drop in drops)
        {
            if (drop is null || drop.SlotsStatus != Release1SmallCourtesyWorldReadStatus.Ready || drop.Slots is null)
                return Release1QuietCensusObservation.NotReady;
            if (drop.Slots.Any(slot => MatchesProduct(slot, productId)))
                return Release1QuietCensusObservation.Present;
        }

        foreach (var closet in room.Closets)
        {
            if (closet is null) return Release1QuietCensusObservation.NotReady;
            if (closet.Slots.Any(slot => MatchesProduct(slot, productId)))
                return Release1QuietCensusObservation.Present;
        }

        return Release1QuietCensusObservation.Clear;
    }

    private static bool MatchesProduct(Release1SmallCourtesySlotSnapshot? slot, string productId) =>
        slot is not null &&
        slot.Quantity > 0 &&
        string.Equals(slot.ProductId, productId, StringComparison.Ordinal);
}
