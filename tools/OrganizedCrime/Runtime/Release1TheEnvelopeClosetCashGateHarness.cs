namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-61 owner QA harness, gate key F2. Proves, without any mission logic, that OC can read and
/// decrement a hold room closet slot's cash balance. The first press dumps every occupied slot in
/// the first closet, in ordinal GUID order, that has one. The second press locks the first slot whose
/// cash balance is at least <see cref="DecrementAmount"/>, decrements it by exactly that, reads back,
/// and reports expected against observed. Mirrors <see cref="Release1TheEnvelopeCashGateHarness"/>.
/// No Unity or S1API reference here, so it stays test linkable. It never inserts and always releases
/// the lock it took.
/// </summary>
public static class Release1TheEnvelopeClosetCashGateHarness
{
    public const float DecrementAmount = 100f;

    public static Release1StagingHarnessResult TryDumpClosetCashSlots(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });
        var lines = new List<string>();
        try
        {
            if (!TryReadReadyRoom(world, lines, out var room, out var failure)) return failure;
            lines.Add($"hold room readiness: {room!.Readiness}, closets {room.Closets.Count}");

            foreach (var closet in room.Closets.OrderBy(candidate => candidate.ClosetGuid, StringComparer.Ordinal))
            {
                var occupied = closet.Slots.Where(slot => slot.Quantity > 0).OrderBy(slot => slot.SlotIndex).ToArray();
                if (occupied.Length == 0) continue;
                lines.Add($"closet {closet.ClosetGuid} has {occupied.Length} occupied slot(s) of {closet.SlotCount}.");
                foreach (var slot in occupied)
                {
                    var cashStatus = world.TryReadHoldRoomSlotCashBalance(closet.ClosetGuid, slot.SlotIndex, out var balance);
                    var suffix = cashStatus == Release1SmallCourtesyWorldReadStatus.Ready ? $" value {balance}" : string.Empty;
                    lines.Add(
                        $"slot {slot.SlotIndex} product {slot.ProductId} quantity {slot.Quantity} " +
                        $"legacy monetaryValue {slot.MonetaryValue} cash balance read {cashStatus}{suffix}");
                }
                return new(Release1StagingHarnessStatus.Succeeded, lines);
            }

            lines.Add("no closet held an occupied slot.");
            return new(Release1StagingHarnessStatus.Unavailable, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping closet cash slots: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    public static Release1StagingHarnessResult TryDecrementFirstClosetCashSlot(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });
        var lines = new List<string>();
        string? lockedGuid = null;
        var lockedSlotIndex = -1;
        try
        {
            if (!TryReadReadyRoom(world, lines, out var room, out var failure)) return failure;

            foreach (var closet in room!.Closets.OrderBy(candidate => candidate.ClosetGuid, StringComparer.Ordinal))
            foreach (var slot in closet.Slots.Where(candidate => candidate.Quantity > 0).OrderBy(candidate => candidate.SlotIndex))
            {
                var cashStatus = world.TryReadHoldRoomSlotCashBalance(closet.ClosetGuid, slot.SlotIndex, out var preBalance);
                if (cashStatus != Release1SmallCourtesyWorldReadStatus.Ready || preBalance < DecrementAmount) continue;

                lines.Add($"closet {closet.ClosetGuid} slot {slot.SlotIndex} pre balance {preBalance}");
                var locked = world.TrySetHoldRoomSlotLocked(closet.ClosetGuid, slot.SlotIndex, true);
                if (locked != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                {
                    lines.Add($"the slot could not be locked, status {locked}.");
                    return new(MapMutationStatus(locked), lines);
                }
                lockedGuid = closet.ClosetGuid;
                lockedSlotIndex = slot.SlotIndex;

                var changed = world.TryChangeHoldRoomSlotCashBalance(closet.ClosetGuid, slot.SlotIndex, -DecrementAmount);
                lines.Add($"decrement by {DecrementAmount} result {changed}");
                if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded) return new(MapMutationStatus(changed), lines);

                var postStatus = world.TryReadHoldRoomSlotCashBalance(closet.ClosetGuid, slot.SlotIndex, out var postBalance);
                var ready = postStatus == Release1SmallCourtesyWorldReadStatus.Ready;
                var expected = preBalance - DecrementAmount;
                lines.Add($"post balance read {postStatus}{(ready ? $" value {postBalance}" : string.Empty)}");
                lines.Add($"expected post balance {expected}, observed {(ready ? postBalance.ToString() : "n/a")}");
                return new(
                    ready && postBalance == expected ? Release1StagingHarnessStatus.Succeeded : Release1StagingHarnessStatus.Faulted,
                    lines);
            }

            lines.Add($"no closet slot held a cash balance of at least {DecrementAmount}.");
            return new(Release1StagingHarnessStatus.Unavailable, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while decrementing: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
        finally
        {
            if (lockedGuid is not null && lockedSlotIndex >= 0)
            {
                try { world.TrySetHoldRoomSlotLocked(lockedGuid, lockedSlotIndex, false); }
                catch (Exception) { /* The next lifecycle teardown retries the exact lock identity. */ }
            }
        }
    }

    private static bool TryReadReadyRoom(
        IRelease1SmallCourtesyWorld world,
        List<string> lines,
        out Release1HoldRoomSnapshot? room,
        out Release1StagingHarnessResult failure)
    {
        var status = world.TryReadHoldRoom(out room);
        if (status == Release1SmallCourtesyWorldReadStatus.Ready && room is not null &&
            room.Readiness == Release1HoldRoomReadiness.Ready)
        {
            failure = null!;
            return true;
        }
        lines.Add($"the hold room could not be read, status {status}, readiness {room?.Readiness}.");
        failure = new(MapReadStatus(status), lines);
        return false;
    }

    private static Release1StagingHarnessStatus MapReadStatus(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldReadStatus.Pending => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        // Ready reaching this mapper means the room read itself succeeded but was not Ready readiness,
        // or no closet qualified: both are Unavailable, not Faulted.
        Release1SmallCourtesyWorldReadStatus.Ready => Release1StagingHarnessStatus.Unavailable,
        _ => Release1StagingHarnessStatus.Faulted
    };

    private static Release1StagingHarnessStatus MapMutationStatus(Release1SmallCourtesyWorldMutationStatus status) => status switch
    {
        Release1SmallCourtesyWorldMutationStatus.Succeeded => Release1StagingHarnessStatus.Succeeded,
        Release1SmallCourtesyWorldMutationStatus.Rejected => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldMutationStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        _ => Release1StagingHarnessStatus.Faulted
    };
}
