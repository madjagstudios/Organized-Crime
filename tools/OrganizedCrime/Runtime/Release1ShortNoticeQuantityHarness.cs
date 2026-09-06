using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-58 owner QA harness. Proves, without any mission logic, the one thing OC has never done: a
/// decrement of more than one unit from a vanilla dead drop slot that holds more than one unit. It
/// locks the first slot it finds holding at least two units in the first dead drop that has one,
/// logs the pre quantity and pre value, performs a single bounded decrement of two, logs the post
/// quantity and post value, and says which of the two monetary value readings it observed. Gated in
/// the shell on the OwnerQaKeys preference; no Unity or S1API reference here so it stays test
/// linkable. It never inserts, never fabricates, and always releases the lock it took.
/// </summary>
public static class Release1ShortNoticeQuantityHarness
{
    public const int DecrementAmount = 2;
    public const string PerUnitLine = "the slot value did not change with the quantity, so the read is per unit.";
    public const string PerStackLine = "the slot value changed with the quantity, so the read is per stack.";

    public static Release1StagingHarnessResult TryDecrementByTwo(IRelease1SmallCourtesyWorld world)
    {
        if (world is null)
            return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });

        var lines = new List<string>();
        string? lockedGuid = null;
        var lockedSlotIndex = -1;
        try
        {
            var dropsStatus = world.TryReadDeadDrops(out var drops);
            if (dropsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                lines.Add($"dead drops could not be read, status {dropsStatus}.");
                return new(MapReadStatus(dropsStatus), lines);
            }

            foreach (var drop in (drops ?? Array.Empty<Release1SmallCourtesyDropCandidate>())
                         .Where(candidate => candidate is not null)
                         .OrderBy(candidate => candidate.Guid, StringComparer.Ordinal))
            {
                var slotsStatus = world.TryReadDeadDropSlots(drop.Guid, out var slots);
                if (slotsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
                {
                    lines.Add($"drop {drop.Name} ({drop.Guid}) slots {slotsStatus}");
                    continue;
                }

                var chosen = (slots ?? Array.Empty<Release1SmallCourtesySlotSnapshot>())
                    .Where(slot => slot is not null && slot.Quantity >= DecrementAmount)
                    .OrderBy(slot => slot.SlotIndex)
                    .FirstOrDefault();
                if (chosen is null) continue;

                lines.Add($"drop {drop.Name} ({drop.Guid}) slot {chosen.SlotIndex} pre product {chosen.ProductId} packaging {chosen.PackagingId} quantity {chosen.Quantity} value {chosen.MonetaryValue}");

                var locked = world.TrySetSlotLocked(drop.Guid, chosen.SlotIndex, true);
                if (locked != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                {
                    lines.Add($"the slot could not be locked, status {locked}.");
                    return new(MapMutationStatus(locked), lines);
                }
                lockedGuid = drop.Guid;
                lockedSlotIndex = chosen.SlotIndex;

                var changed = world.TryChangeSlotQuantity(drop.Guid, chosen.SlotIndex, -DecrementAmount);
                lines.Add($"decrement by {DecrementAmount} result {changed}");
                if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                    return new(MapMutationStatus(changed), lines);

                var postStatus = world.TryReadDeadDropSlots(drop.Guid, out var postSlots);
                if (postStatus != Release1SmallCourtesyWorldReadStatus.Ready)
                {
                    lines.Add($"the drop could not be read back, status {postStatus}.");
                    return new(MapReadStatus(postStatus), lines);
                }

                var post = (postSlots ?? Array.Empty<Release1SmallCourtesySlotSnapshot>())
                    .FirstOrDefault(slot => slot is not null && slot.SlotIndex == chosen.SlotIndex);
                if (post is null)
                {
                    lines.Add("the slot disappeared from the read back.");
                    return new(Release1StagingHarnessStatus.Faulted, lines);
                }

                lines.Add($"drop {drop.Name} ({drop.Guid}) slot {post.SlotIndex} post product {post.ProductId} packaging {post.PackagingId} quantity {post.Quantity} value {post.MonetaryValue}");
                lines.Add($"expected post quantity {chosen.Quantity - DecrementAmount}, observed {post.Quantity}");
                if (post.Quantity > 0)
                    lines.Add(post.MonetaryValue == chosen.MonetaryValue ? PerUnitLine : PerStackLine);
                return new(
                    post.Quantity == chosen.Quantity - DecrementAmount
                        ? Release1StagingHarnessStatus.Succeeded
                        : Release1StagingHarnessStatus.Faulted,
                    lines);
            }

            lines.Add($"no dead drop slot held at least {DecrementAmount} units.");
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
                try { world.TrySetSlotLocked(lockedGuid, lockedSlotIndex, false); }
                catch (Exception) { /* The next lifecycle teardown retries the exact lock identity. */ }
            }
        }
    }

    private static Release1StagingHarnessStatus MapReadStatus(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1StagingHarnessStatus.Rejected,
        Release1SmallCourtesyWorldReadStatus.Pending => Release1StagingHarnessStatus.Unavailable,
        Release1SmallCourtesyWorldReadStatus.Unavailable => Release1StagingHarnessStatus.Unavailable,
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
