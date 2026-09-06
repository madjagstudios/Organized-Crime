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

    private sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        public Release1HoldRoomSnapshot Room { get; set; } = Release1HoldRoomSnapshot.NotReady();
        public Release1SmallCourtesyWorldReadStatus ReadStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Room;
            return ReadStatus;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            context = new(Guid.NewGuid(), 1, "player", Path.GetTempPath());
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = Array.Empty<Release1SmallCourtesyProductCandidate>();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = Array.Empty<Release1SmallCourtesyDropCandidate>();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = new("brick", "Brick");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            reason = "not supported by this fake.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            subscription = null;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        // The mission under test never reads production activity.
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = Release1ProductionActivitySnapshot.Empty;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { reason = "this fake never despawns a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = "this fake never provokes a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { reason = "this fake never parks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { reason = "this fake never unparks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
    }
}
