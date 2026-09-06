using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeObservationTests
{
    [Fact]
    public void A_room_that_is_not_ready_is_RoomNotReady()
    {
        var observation = Release1TheEnvelopeClosetClassifier.Classify(
            Release1HoldRoomSnapshot.NotReady(), Array.Empty<Release1TheEnvelopeClosetCash>(), 1, 100d,
            out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.RoomNotReady, observation);
        Assert.Null(cashClosetGuid);
        Assert.Equal(0d, cashSum);
    }

    [Fact]
    public void A_room_with_eight_closets_is_RoomNotReady()
    {
        var closets = Enumerable.Range(0, 8).Select(index => RoomCloset($"closet-{index}")).ToArray();
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, closets);
        var cash = closets.Select(closet => new Release1TheEnvelopeClosetCash(
            closet.ClosetGuid, true, Array.Empty<Release1TheEnvelopeCashSlot>())).ToArray();

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 9, 100d, out _, out _);

        Assert.Equal(Release1TheEnvelopeClosetObservation.RoomNotReady, observation);
    }

    [Fact]
    public void A_closet_whose_slot_list_reads_back_empty_is_ClosetUnreadable()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready,
            new[] { new Release1HoldRoomClosetSnapshot("closet-a", 0, Array.Empty<Release1SmallCourtesySlotSnapshot>()) });
        var cash = new[] { new Release1TheEnvelopeClosetCash("closet-a", true, Array.Empty<Release1TheEnvelopeCashSlot>()) };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 100d, out _, out _);

        Assert.Equal(Release1TheEnvelopeClosetObservation.ClosetUnreadable, observation);
    }

    [Fact]
    public void A_closet_whose_cash_read_faulted_is_ClosetUnreadable()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { RoomCloset("closet-a") });
        var cash = new[] { new Release1TheEnvelopeClosetCash("closet-a", false, Array.Empty<Release1TheEnvelopeCashSlot>()) };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 100d, out _, out _);

        Assert.Equal(Release1TheEnvelopeClosetObservation.ClosetUnreadable, observation);
    }

    [Fact]
    public void No_cash_anywhere_is_NoCash()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { RoomCloset("closet-a") });
        var cash = new[] { new Release1TheEnvelopeClosetCash("closet-a", true, Array.Empty<Release1TheEnvelopeCashSlot>()) };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 100d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.NoCash, observation);
        Assert.Null(cashClosetGuid);
        Assert.Equal(0d, cashSum);
    }

    [Fact]
    public void One_closet_below_the_amount_is_Shortfall_and_reports_the_sum()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { RoomCloset("closet-a") });
        var cash = new[]
        {
            new Release1TheEnvelopeClosetCash("closet-a", true, new[]
            {
                new Release1TheEnvelopeCashSlot(0, 1000d),
                new Release1TheEnvelopeCashSlot(1, 1000d)
            })
        };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 5000d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Shortfall, observation);
        Assert.Equal(2000d, cashSum);
        Assert.Equal("closet-a", cashClosetGuid);
    }

    [Fact]
    public void One_closet_at_the_amount_is_Sufficient()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { RoomCloset("closet-a") });
        var cash = new[]
        {
            new Release1TheEnvelopeClosetCash("closet-a", true, Enumerable.Range(0, 5)
                .Select(index => new Release1TheEnvelopeCashSlot(index, 1000d)).ToArray())
        };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 5000d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Sufficient, observation);
        Assert.Equal(5000d, cashSum);
        Assert.Equal("closet-a", cashClosetGuid);
    }

    [Fact]
    public void One_closet_above_the_amount_is_Sufficient_and_reports_the_full_sum()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { RoomCloset("closet-a") });
        var cash = new[]
        {
            new Release1TheEnvelopeClosetCash("closet-a", true, Enumerable.Range(0, 6)
                .Select(index => new Release1TheEnvelopeCashSlot(index, 1000d)).ToArray())
        };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 5000d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Sufficient, observation);
        Assert.Equal(6000d, cashSum);
        Assert.Equal("closet-a", cashClosetGuid);
    }

    [Fact]
    public void Two_closets_holding_any_cash_at_all_is_Spread()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready,
            new[] { RoomCloset("closet-a"), RoomCloset("closet-b") });
        var cash = new[]
        {
            new Release1TheEnvelopeClosetCash("closet-a", true, new[]
            {
                new Release1TheEnvelopeCashSlot(0, 1000d),
                new Release1TheEnvelopeCashSlot(1, 1000d)
            }),
            new Release1TheEnvelopeClosetCash("closet-b", true, new[] { new Release1TheEnvelopeCashSlot(0, 1d) })
        };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 2, 5000d, out var cashClosetGuid, out _);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Spread, observation);
        Assert.Null(cashClosetGuid);
    }

    [Fact]
    public void Non_cash_items_are_ignored_and_never_counted_or_held()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[]
        {
            new Release1HoldRoomClosetSnapshot("closet-a", 3, new[]
            {
                new Release1SmallCourtesySlotSnapshot(0, "cash", "cash", 1, false, 1000f),
                new Release1SmallCourtesySlotSnapshot(1, "product-a", "brick", 1, true, 0f),
                new Release1SmallCourtesySlotSnapshot(2, "product-a", "brick", 1, true, 0f)
            })
        });
        // Only the cash-balance read (slot 0) is a cash slot; the product slots never appear here at
        // all, exactly as the classifier's contract describes: they are ignored, not held.
        var cash = new[] { new Release1TheEnvelopeClosetCash("closet-a", true, new[] { new Release1TheEnvelopeCashSlot(0, 1000d) }) };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 1, 5000d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Shortfall, observation);
        Assert.Equal(1000d, cashSum);
        Assert.Equal("closet-a", cashClosetGuid);
    }

    [Fact]
    public void Non_cash_items_in_a_second_closet_do_not_make_a_spread()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready,
            new[] { RoomCloset("closet-a"), RoomCloset("closet-b") });
        var cash = new[]
        {
            new Release1TheEnvelopeClosetCash("closet-a", true, new[] { new Release1TheEnvelopeCashSlot(0, 5000d) }),
            // Closet B carries only product; it reads back with no cash slots at all.
            new Release1TheEnvelopeClosetCash("closet-b", true, Array.Empty<Release1TheEnvelopeCashSlot>())
        };

        var observation = Release1TheEnvelopeClosetClassifier.Classify(room, cash, 2, 5000d, out var cashClosetGuid, out var cashSum);

        Assert.Equal(Release1TheEnvelopeClosetObservation.Sufficient, observation);
        Assert.Equal(5000d, cashSum);
        Assert.Equal("closet-a", cashClosetGuid);
    }

    private static Release1HoldRoomClosetSnapshot RoomCloset(string guid) => new(guid, 1,
        new[] { new Release1SmallCourtesySlotSnapshot(0, "cash", "cash", 1, false, 0f) });
}
