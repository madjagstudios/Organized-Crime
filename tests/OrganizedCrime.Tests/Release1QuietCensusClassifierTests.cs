using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1QuietCensusClassifierTests
{
    private const string ProductId = "cocaine";
    private const string DropA = "8ec9d63b-f0f7-4af9-86fb-f1c73c7af481";
    private const string DropB = "dcf4ca2a-4d27-47c4-a10a-2819ea298fe3";
    private const string ClosetA = "b6d1b0a1-7c3a-4f1e-9a1a-3e3f7a1c9b21";
    private const string ClosetB = "1f2e3d4c-5b6a-4978-8c1d-2e3f4a5b6c7d";

    [Fact]
    public void NotReady_when_the_drop_count_does_not_match_the_expected_count()
    {
        var drops = new[] { Drop(DropA, empty: true) };
        var room = Room(Closet(ClosetA, empty: true));

        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 2, 1));
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(null, room, ProductId, 1, 1));
    }

    [Fact]
    public void NotReady_when_any_drop_read_status_is_not_ready()
    {
        var room = Room(Closet(ClosetA, empty: true));

        var midListUnready = new[] { NotReadyDrop(DropA), Drop(DropB, empty: true) };
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(midListUnready, room, ProductId, 2, 1));

        var lastUnready = new[] { Drop(DropA, empty: true), NotReadyDrop(DropB) };
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(lastUnready, room, ProductId, 2, 1));
    }

    [Fact]
    public void NotReady_when_the_hold_room_is_not_ready_or_the_closet_count_is_wrong()
    {
        var drops = new[] { Drop(DropA, empty: true) };

        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(drops, null, ProductId, 1, 1));
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(drops, Release1HoldRoomSnapshot.NotReady(), ProductId, 1, 1));
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(drops, Release1HoldRoomSnapshot.Unavailable(), ProductId, 1, 1));

        var wrongClosetCount = Room(Closet(ClosetA, empty: true), Closet(ClosetB, empty: true));
        Assert.Equal(Release1QuietCensusObservation.NotReady,
            Release1QuietCensusClassifier.Classify(drops, wrongClosetCount, ProductId, 1, 1));
    }

    [Fact]
    public void Clear_when_every_drop_and_every_closet_is_ready_and_none_carries_the_product()
    {
        var drops = new[] { Drop(DropA, empty: true), Drop(DropB, empty: true) };
        var room = Room(Closet(ClosetA, empty: true), Closet(ClosetB, empty: true));

        Assert.Equal(Release1QuietCensusObservation.Clear,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 2, 2));
    }

    [Fact]
    public void Present_when_the_product_is_in_a_drop_slot_regardless_of_packaging()
    {
        var drops = new[] { Drop(DropA, empty: true), DropWith(DropB, ProductId, "brick", 1, isPackaged: true) };
        var room = Room(Closet(ClosetA, empty: true));

        Assert.Equal(Release1QuietCensusObservation.Present,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 2, 1));
    }

    [Fact]
    public void Present_when_the_product_is_in_a_closet_slot_regardless_of_packaging()
    {
        var drops = new[] { Drop(DropA, empty: true) };
        var room = Room(Closet(ClosetA, empty: true), ClosetWith(ClosetB, ProductId, "jar", 1, isPackaged: true));

        Assert.Equal(Release1QuietCensusObservation.Present,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 1, 2));
    }

    [Fact]
    public void Present_when_the_product_is_unpackaged_and_loose_in_a_slot()
    {
        var drops = new[] { DropWith(DropA, ProductId, null, 1, isPackaged: false) };
        var room = Room(Closet(ClosetA, empty: true));

        Assert.Equal(Release1QuietCensusObservation.Present,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 1, 1));
    }

    [Fact]
    public void A_different_product_id_in_every_slot_is_Clear()
    {
        var drops = new[] { DropWith(DropA, "meth", "jar", 1, isPackaged: true) };
        var room = Room(ClosetWith(ClosetA, "weed", "brick", 1, isPackaged: true));

        Assert.Equal(Release1QuietCensusObservation.Clear,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 1, 1));
    }

    [Fact]
    public void A_zero_quantity_slot_carrying_the_product_id_is_not_a_match()
    {
        var drops = new[]
        {
            new Release1QuietCensusDropSnapshot(DropA, Release1SmallCourtesyWorldReadStatus.Ready,
                new[] { new Release1SmallCourtesySlotSnapshot(0, ProductId, "brick", 0, true, 0f) })
        };
        var room = Room(Closet(ClosetA, empty: true));

        Assert.Equal(Release1QuietCensusObservation.Clear,
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 1, 1));
    }

    [Fact]
    public void Classify_throws_on_a_blank_product_id_and_a_non_positive_expected_count()
    {
        var drops = new[] { Drop(DropA, empty: true) };
        var room = Room(Closet(ClosetA, empty: true));

        Assert.Throws<ArgumentException>(() =>
            Release1QuietCensusClassifier.Classify(drops, room, " ", 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Release1QuietCensusClassifier.Classify(drops, room, ProductId, 1, 0));
    }

    private static Release1QuietCensusDropSnapshot Drop(string guid, bool empty) =>
        new(guid, Release1SmallCourtesyWorldReadStatus.Ready,
            empty
                ? new[] { new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f) }
                : Array.Empty<Release1SmallCourtesySlotSnapshot>());

    private static Release1QuietCensusDropSnapshot NotReadyDrop(string guid) =>
        new(guid, Release1SmallCourtesyWorldReadStatus.Pending,
            new[] { new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f) });

    private static Release1QuietCensusDropSnapshot DropWith(string guid, string productId, string? packagingId, int quantity, bool isPackaged) =>
        new(guid, Release1SmallCourtesyWorldReadStatus.Ready,
            new[] { new Release1SmallCourtesySlotSnapshot(0, productId, packagingId, quantity, isPackaged, 1_000f) });

    private static Release1HoldRoomSnapshot Room(params Release1HoldRoomClosetSnapshot[] closets) =>
        new(Release1HoldRoomReadiness.Ready, closets);

    private static Release1HoldRoomClosetSnapshot Closet(string guid, bool empty) =>
        new(guid, 1,
            empty
                ? new[] { new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f) }
                : Array.Empty<Release1SmallCourtesySlotSnapshot>());

    private static Release1HoldRoomClosetSnapshot ClosetWith(string guid, string productId, string? packagingId, int quantity, bool isPackaged) =>
        new(guid, 1, new[] { new Release1SmallCourtesySlotSnapshot(0, productId, packagingId, quantity, isPackaged, 1_000f) });
}
