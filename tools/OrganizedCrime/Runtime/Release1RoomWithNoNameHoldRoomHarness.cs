namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-57 owner QA harness. Proves, without any mission logic, that OC can read item identity from
/// the nine OC-48 HQ closets from anywhere in the world, including while the player stands outside
/// the HQ interior. Gated in the shell on the OwnerQaKeys preference; no Unity or S1API reference
/// here so it stays test linkable. Reads only; it never mutates a closet.
/// </summary>
public static class Release1RoomWithNoNameHoldRoomHarness
{
    public static Release1StagingHarnessResult TryDump(IRelease1SmallCourtesyWorld world)
    {
        if (world is null)
            return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });

        var lines = new List<string>();
        try
        {
            var status = world.TryReadHoldRoom(out var room);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || room is null)
            {
                lines.Add($"the hold room could not be read, status {status}.");
                return new(MapReadStatus(status), lines);
            }

            lines.Add($"hold room readiness: {room.Readiness}, closets {room.Closets.Count}");
            if (room.Readiness != Release1HoldRoomReadiness.Ready)
                return new(Release1StagingHarnessStatus.Unavailable, lines);

            foreach (var closet in room.Closets.OrderBy(candidate => candidate.ClosetGuid, StringComparer.Ordinal))
            {
                var occupied = closet.Slots.Count(slot => slot.Quantity > 0);
                lines.Add($"closet {closet.ClosetGuid} slots {closet.SlotCount}, occupied {occupied}");
                foreach (var slot in closet.Slots.Where(candidate => candidate.Quantity > 0).OrderBy(candidate => candidate.SlotIndex))
                    lines.Add($"closet {closet.ClosetGuid} slot {slot.SlotIndex} product {slot.ProductId} packaging {slot.PackagingId} quantity {slot.Quantity} value {slot.MonetaryValue}");
            }

            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping the hold room: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    private static Release1StagingHarnessStatus MapReadStatus(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldReadStatus.Pending => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
        _ => Release1StagingHarnessStatus.Faulted
    };
}
