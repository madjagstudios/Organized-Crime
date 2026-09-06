using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

/// <summary>
/// The exact identity of one staged consignment: product, packaging, and quantity. No per instance
/// GUID exists in the game, so this fingerprint plus exact slot occupancy is the whole identity a
/// mission has to work with.
/// </summary>
public sealed record Release1ConsignmentFingerprint(string ProductId, string PackagingId, int Quantity)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
        if (Quantity < 1) throw new ArgumentOutOfRangeException(nameof(Quantity), "A consignment is at least one unit.");
    }

    public bool Matches(Release1SmallCourtesySlotSnapshot? slot) =>
        slot is not null &&
        slot.Quantity == Quantity &&
        slot.IsPackaged &&
        string.Equals(slot.ProductId, ProductId, StringComparison.Ordinal) &&
        string.Equals(slot.PackagingId, PackagingId, StringComparison.Ordinal);

    /// <summary>
    /// The Short Notice match: product ID plus packaging ID, quantity deliberately ignored, because
    /// a manifest of N is satisfied by one slot holding N or more and read as a shortfall by one
    /// slot holding fewer. An empty slot never matches.
    /// </summary>
    public bool MatchesIdentity(Release1SmallCourtesySlotSnapshot? slot) =>
        slot is not null &&
        slot.Quantity > 0 &&
        slot.IsPackaged &&
        string.Equals(slot.ProductId, ProductId, StringComparison.Ordinal) &&
        string.Equals(slot.PackagingId, PackagingId, StringComparison.Ordinal);
}

/// <summary>
/// What one container (a dead drop, one HQ closet, or the whole hold room) reads as against a
/// fingerprint. ZeroLength is never treated as empty: a storage entity that has not finished
/// initializing reads as zero slots, and staging into it would be a second insertion.
/// </summary>
public enum Release1ContainerObservation
{
    ZeroLength,
    Empty,
    ExactlyOne,
    Several,
    Contaminated
}

public static class Release1ConsignmentClassifier
{
    /// <summary>
    /// The whole container verdict that staging and the hold both need. matchedSlotIndex is the
    /// matching slot's index for ExactlyOne and -1 otherwise.
    /// </summary>
    public static Release1ContainerObservation Classify(
        IReadOnlyList<Release1SmallCourtesySlotSnapshot>? slots,
        Release1ConsignmentFingerprint fingerprint,
        out int matchedSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        matchedSlotIndex = -1;
        if (slots is null || slots.Count == 0) return Release1ContainerObservation.ZeroLength;

        var matches = slots.Where(fingerprint.Matches).ToArray();
        if (matches.Length > 1) return Release1ContainerObservation.Several;
        if (matches.Length == 1)
        {
            var only = matches[0];
            var restEmpty = slots.All(slot => slot is not null && (slot.SlotIndex == only.SlotIndex || slot.Quantity == 0));
            if (!restEmpty) return Release1ContainerObservation.Contaminated;
            matchedSlotIndex = only.SlotIndex;
            return Release1ContainerObservation.ExactlyOne;
        }
        return slots.All(slot => slot is not null && slot.Quantity == 0)
            ? Release1ContainerObservation.Empty
            : Release1ContainerObservation.Contaminated;
    }

    /// <summary>
    /// The delivery query. Unlike Classify it deliberately tolerates other items in the container: a
    /// handoff drop the player also uses for their own stock still delivers, as long as exactly one
    /// slot carries the fingerprint. Returns the match count and, when it is one, that slot.
    /// </summary>
    public static int CountMatches(
        IReadOnlyList<Release1SmallCourtesySlotSnapshot>? slots,
        Release1ConsignmentFingerprint fingerprint,
        out Release1SmallCourtesySlotSnapshot? single)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        single = null;
        if (slots is null) return 0;
        var matches = slots.Where(fingerprint.Matches).ToArray();
        if (matches.Length == 1) single = matches[0];
        return matches.Length;
    }

    /// <summary>
    /// The quantity N deposit query. Tolerates other items in the container, exactly as
    /// <see cref="CountMatches"/> does, but matches on identity alone so a slot above or below the
    /// manifest is still found. Returns the match count and, when it is one, that slot.
    /// </summary>
    public static int CountIdentityMatches(
        IReadOnlyList<Release1SmallCourtesySlotSnapshot>? slots,
        Release1ConsignmentFingerprint fingerprint,
        out Release1SmallCourtesySlotSnapshot? single)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        single = null;
        if (slots is null) return 0;
        var matches = slots.Where(fingerprint.MatchesIdentity).ToArray();
        if (matches.Length == 1) single = matches[0];
        return matches.Length;
    }
}
