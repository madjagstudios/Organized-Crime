using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1HoldRoomContractTests
{
    [Fact]
    public void A_ready_room_requires_every_closet_to_carry_a_guid_and_unique_slot_indices()
    {
        var room = new Release1HoldRoomSnapshot(
            Release1HoldRoomReadiness.Ready,
            new[] { Closet("a1b2", 2, new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f), new Release1SmallCourtesySlotSnapshot(1, null, null, 0, false, 0f)) });

        room.Validate();

        Assert.Equal(Release1HoldRoomReadiness.Ready, room.Readiness);
        Assert.Equal(2, room.Closets[0].SlotCount);
    }

    [Fact]
    public void Duplicate_slot_indices_inside_one_closet_are_refused()
    {
        var closet = Closet("a1b2", 2,
            new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f),
            new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f));

        Assert.Throws<ArgumentException>(() => closet.Validate());
    }

    [Fact]
    public void A_slot_count_that_disagrees_with_the_slot_list_is_refused()
    {
        var closet = Closet("a1b2", 3, new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f));

        Assert.Throws<ArgumentException>(() => closet.Validate());
    }

    [Fact]
    public void Duplicate_closet_guids_in_one_room_are_refused()
    {
        var room = new Release1HoldRoomSnapshot(
            Release1HoldRoomReadiness.Ready,
            new[]
            {
                Closet("a1b2", 1, new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f)),
                Closet("a1b2", 1, new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f))
            });

        Assert.Throws<ArgumentException>(() => room.Validate());
    }

    [Fact]
    public void A_not_ready_room_carries_no_closets_and_validates()
    {
        var room = Release1HoldRoomSnapshot.NotReady();

        room.Validate();

        Assert.Equal(Release1HoldRoomReadiness.NotReady, room.Readiness);
        Assert.Empty(room.Closets);
    }

    [Fact]
    public void A_not_ready_room_that_carries_closets_is_refused()
    {
        var room = new Release1HoldRoomSnapshot(
            Release1HoldRoomReadiness.NotReady,
            new[] { Closet("a1b2", 1, new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f)) });

        Assert.Throws<ArgumentException>(() => room.Validate());
    }

    private static Release1HoldRoomClosetSnapshot Closet(string guid, int slotCount, params Release1SmallCourtesySlotSnapshot[] slots) =>
        new(guid, slotCount, slots);
}
