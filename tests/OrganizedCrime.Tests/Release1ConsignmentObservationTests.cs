using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ConsignmentObservationTests
{
    private static readonly Release1ConsignmentFingerprint Brick = new("cocaine", "brick", 1);

    [Fact]
    public void A_null_or_zero_length_read_is_never_treated_as_empty()
    {
        Assert.Equal(Release1ContainerObservation.ZeroLength, Release1ConsignmentClassifier.Classify(null, Brick, out var nullIndex));
        Assert.Equal(-1, nullIndex);
        Assert.Equal(Release1ContainerObservation.ZeroLength, Release1ConsignmentClassifier.Classify(Array.Empty<Release1SmallCourtesySlotSnapshot>(), Brick, out var emptyIndex));
        Assert.Equal(-1, emptyIndex);
    }

    [Fact]
    public void All_empty_slots_classify_as_empty()
    {
        Assert.Equal(Release1ContainerObservation.Empty, Release1ConsignmentClassifier.Classify(new[] { Empty(0), Empty(1), Empty(2) }, Brick, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Exactly_one_match_with_every_other_slot_empty_classifies_as_exactly_one()
    {
        Assert.Equal(Release1ContainerObservation.ExactlyOne, Release1ConsignmentClassifier.Classify(new[] { Empty(0), Match(1), Empty(2) }, Brick, out var index));
        Assert.Equal(1, index);
    }

    [Fact]
    public void One_match_beside_another_item_is_contaminated()
    {
        var slots = new[] { Match(0), new Release1SmallCourtesySlotSnapshot(1, "meth", "jar", 1, true, 500f) };

        Assert.Equal(Release1ContainerObservation.Contaminated, Release1ConsignmentClassifier.Classify(slots, Brick, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Two_matches_classify_as_several()
    {
        Assert.Equal(Release1ContainerObservation.Several, Release1ConsignmentClassifier.Classify(new[] { Match(0), Match(1) }, Brick, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void No_match_with_another_item_present_is_contaminated()
    {
        var slots = new[] { Empty(0), new Release1SmallCourtesySlotSnapshot(1, "meth", "jar", 1, true, 500f) };

        Assert.Equal(Release1ContainerObservation.Contaminated, Release1ConsignmentClassifier.Classify(slots, Brick, out _));
    }

    [Fact]
    public void The_fingerprint_requires_exact_product_packaging_quantity_and_packaged_state()
    {
        Assert.False(Brick.Matches(null));
        Assert.False(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "jar", 1, true, 1_000f)));
        Assert.False(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "meth", "brick", 1, true, 1_000f)));
        Assert.False(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 2, true, 2_000f)));
        Assert.False(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, false, 1_000f)));
        Assert.True(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 1_000f)));
    }

    [Fact]
    public void Count_matches_tolerates_other_items_and_returns_the_single_match()
    {
        var slots = new[] { new Release1SmallCourtesySlotSnapshot(0, "meth", "jar", 1, true, 500f), Match(1) };

        Assert.Equal(1, Release1ConsignmentClassifier.CountMatches(slots, Brick, out var single));
        Assert.Equal(1, single!.SlotIndex);

        Assert.Equal(2, Release1ConsignmentClassifier.CountMatches(new[] { Match(0), Match(1) }, Brick, out var ambiguous));
        Assert.Null(ambiguous);

        Assert.Equal(0, Release1ConsignmentClassifier.CountMatches(new[] { Empty(0) }, Brick, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void A_fingerprint_with_a_blank_id_or_a_non_positive_quantity_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new Release1ConsignmentFingerprint(" ", "brick", 1).Validate());
        Assert.Throws<ArgumentException>(() => new Release1ConsignmentFingerprint("cocaine", " ", 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Release1ConsignmentFingerprint("cocaine", "brick", 0).Validate());
    }

    [Fact]
    public void Identity_match_ignores_quantity_and_still_requires_packaging_and_ids()
    {
        Assert.True(Brick.MatchesIdentity(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 5, true, 5_000f)));
        Assert.False(Brick.MatchesIdentity(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 5, false, 5_000f)));
        Assert.False(Brick.MatchesIdentity(new Release1SmallCourtesySlotSnapshot(0, "meth", "brick", 5, true, 5_000f)));
        Assert.False(Brick.MatchesIdentity(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "jar", 5, true, 5_000f)));
        Assert.False(Brick.MatchesIdentity(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 0, true, 0f)));
        Assert.False(Brick.MatchesIdentity(null));
    }

    [Fact]
    public void Identity_match_count_returns_the_single_slot_only_when_exactly_one_matches()
    {
        Assert.Equal(0, Release1ConsignmentClassifier.CountIdentityMatches(new[] { Empty(0) }, Brick, out var none));
        Assert.Null(none);

        var slots = new[] { new Release1SmallCourtesySlotSnapshot(0, "meth", "jar", 1, true, 500f), MatchQuantity(1, 5) };
        Assert.Equal(1, Release1ConsignmentClassifier.CountIdentityMatches(slots, Brick, out var single));
        Assert.Equal(1, single!.SlotIndex);

        Assert.Equal(3, Release1ConsignmentClassifier.CountIdentityMatches(
            new[] { MatchQuantity(0, 1), MatchQuantity(1, 5), MatchQuantity(2, 9) }, Brick, out var ambiguous));
        Assert.Null(ambiguous);
    }

    [Fact]
    public void Exact_quantity_match_is_unchanged()
    {
        Assert.Equal(Release1ContainerObservation.ExactlyOne, Release1ConsignmentClassifier.Classify(new[] { Empty(0), Match(1), Empty(2) }, Brick, out var index));
        Assert.Equal(1, index);

        var slots = new[] { new Release1SmallCourtesySlotSnapshot(0, "meth", "jar", 1, true, 500f), Match(1) };
        Assert.Equal(1, Release1ConsignmentClassifier.CountMatches(slots, Brick, out var single));
        Assert.Equal(1, single!.SlotIndex);

        Assert.True(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 1_000f)));
        Assert.False(Brick.Matches(new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 2, true, 2_000f)));
    }

    private static Release1SmallCourtesySlotSnapshot Empty(int index) => new(index, null, null, 0, false, 0f);
    private static Release1SmallCourtesySlotSnapshot Match(int index) => new(index, "cocaine", "brick", 1, true, 1_000f);
    private static Release1SmallCourtesySlotSnapshot MatchQuantity(int index, int quantity) => new(index, "cocaine", "brick", quantity, true, quantity * 1_000f);
}
