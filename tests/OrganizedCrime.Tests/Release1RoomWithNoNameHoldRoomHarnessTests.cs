using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameHoldRoomHarnessTests
{
    [Fact]
    public void The_dump_lists_every_closet_in_ordinal_guid_order_with_its_slot_count()
    {
        var world = new FakeWorld
        {
            Room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[]
            {
                Closet("ccc", 20),
                Closet("aaa", 20),
                Closet("bbb", 20)
            })
        };

        var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Contains("hold room readiness: Ready, closets 3", result.Lines);
        var order = result.Lines.Where(line => line.StartsWith("closet ", StringComparison.Ordinal)).ToArray();
        Assert.Equal("closet aaa slots 20, occupied 0", order[0]);
        Assert.Equal("closet bbb slots 20, occupied 0", order[1]);
        Assert.Equal("closet ccc slots 20, occupied 0", order[2]);
    }

    [Fact]
    public void An_occupied_slot_is_dumped_with_its_exact_product_packaging_and_quantity()
    {
        var world = new FakeWorld
        {
            Room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[]
            {
                new Release1HoldRoomClosetSnapshot("aaa", 2, new[]
                {
                    new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 1_000f),
                    new Release1SmallCourtesySlotSnapshot(1, null, null, 0, false, 0f)
                })
            })
        };

        var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Contains("closet aaa slots 2, occupied 1", result.Lines);
        Assert.Contains("closet aaa slot 0 product cocaine packaging brick quantity 1 value 1000", result.Lines);
    }

    [Fact]
    public void An_empty_closet_reports_zero_occupied_slots_rather_than_an_error()
    {
        var world = new FakeWorld
        {
            Room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[] { Closet("aaa", 20) })
        };

        var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Contains("closet aaa slots 20, occupied 0", result.Lines);
        Assert.DoesNotContain(result.Lines, line => line.Contains(" product ", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unready_room_is_reported_without_faulting()
    {
        var world = new FakeWorld { Room = Release1HoldRoomSnapshot.NotReady() };

        var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.Contains("hold room readiness: NotReady, closets 0", result.Lines);
    }

    [Fact]
    public void A_faulted_read_is_reported_and_never_throws()
    {
        var world = new FakeWorld { ReadStatus = Release1SmallCourtesyWorldReadStatus.Faulted };

        var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains("the hold room could not be read, status Faulted.", result.Lines);
    }

    private static Release1HoldRoomClosetSnapshot Closet(string guid, int slotCount) =>
        new(guid, slotCount, Enumerable.Range(0, slotCount)
            .Select(index => new Release1SmallCourtesySlotSnapshot(index, null, null, 0, false, 0f))
            .ToArray());

    private sealed class FakeWorld : IHoldRoomWorld
    {
        public Release1HoldRoomSnapshot Room { get; set; } = Release1HoldRoomSnapshot.NotReady();
        public Release1SmallCourtesyWorldReadStatus ReadStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Room;
            return ReadStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

    }
}
