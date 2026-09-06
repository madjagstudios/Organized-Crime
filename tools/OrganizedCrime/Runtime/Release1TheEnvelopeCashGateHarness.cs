using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-60 owner QA harness, gate key F1. Proves, without any mission logic, the one thing OC has
/// never done: reading a dead drop slot's <c>CashInstance.Balance</c> and decrementing it in place.
/// <see cref="TryDumpCashSlots"/> (the first F1 press) lists every occupied slot in the first dead
/// drop that has one, alongside both the legacy <see cref="Release1SmallCourtesySlotSnapshot.MonetaryValue"/>
/// (expected zero for a cash item, proving the read gap) and the new cash-balance read.
/// <see cref="TryDecrementFirstCashSlot"/> (the second F1 press) locks the first slot whose cash
/// balance is at least <see cref="DecrementAmount"/>, decrements it by exactly that, and reads back
/// the exact remainder. Gated in the shell on the OwnerQaKeys preference; no Unity or S1API
/// reference here so it stays test linkable. It never inserts, never fabricates, and always
/// releases the lock it took.
/// </summary>
public static class Release1TheEnvelopeCashGateHarness
{
    public const float DecrementAmount = 100f;

    public static Release1StagingHarnessResult TryDumpCashSlots(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });

        var lines = new List<string>();
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
                var occupied = (slots ?? Array.Empty<Release1SmallCourtesySlotSnapshot>())
                    .Where(slot => slot is not null && slot.Quantity > 0)
                    .OrderBy(slot => slot.SlotIndex)
                    .ToArray();
                if (occupied.Length == 0) continue;

                lines.Add($"drop {drop.Name} ({drop.Guid}) has {occupied.Length} occupied slot(s).");
                foreach (var slot in occupied)
                {
                    var cashStatus = world.TryReadDeadDropSlotCashBalance(drop.Guid, slot.SlotIndex, out var balance);
                    var suffix = cashStatus == Release1SmallCourtesyWorldReadStatus.Ready ? $" value {balance}" : string.Empty;
                    lines.Add(
                        $"slot {slot.SlotIndex} product {slot.ProductId} quantity {slot.Quantity} " +
                        $"legacy monetaryValue {slot.MonetaryValue} cash balance read {cashStatus}{suffix}");
                }
                return new(Release1StagingHarnessStatus.Succeeded, lines);
            }

            lines.Add("no dead drop held an occupied slot.");
            return new(Release1StagingHarnessStatus.Unavailable, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping cash slots: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    public static Release1StagingHarnessResult TryDecrementFirstCashSlot(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return new(Release1StagingHarnessStatus.Faulted, new[] { "the world was not available." });

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
                if (slotsStatus != Release1SmallCourtesyWorldReadStatus.Ready) continue;

                foreach (var slot in (slots ?? Array.Empty<Release1SmallCourtesySlotSnapshot>())
                             .Where(candidate => candidate is not null && candidate.Quantity > 0)
                             .OrderBy(candidate => candidate.SlotIndex))
                {
                    var cashStatus = world.TryReadDeadDropSlotCashBalance(drop.Guid, slot.SlotIndex, out var preBalance);
                    if (cashStatus != Release1SmallCourtesyWorldReadStatus.Ready || preBalance < DecrementAmount) continue;

                    lines.Add($"drop {drop.Name} ({drop.Guid}) slot {slot.SlotIndex} pre balance {preBalance}");

                    var locked = world.TrySetSlotLocked(drop.Guid, slot.SlotIndex, true);
                    if (locked != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                    {
                        lines.Add($"the slot could not be locked, status {locked}.");
                        return new(MapMutationStatus(locked), lines);
                    }
                    lockedGuid = drop.Guid;
                    lockedSlotIndex = slot.SlotIndex;

                    var changed = world.TryChangeDeadDropSlotCashBalance(drop.Guid, slot.SlotIndex, -DecrementAmount);
                    lines.Add($"decrement by {DecrementAmount} result {changed}");
                    if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                        return new(MapMutationStatus(changed), lines);

                    var postStatus = world.TryReadDeadDropSlotCashBalance(drop.Guid, slot.SlotIndex, out var postBalance);
                    var suffix = postStatus == Release1SmallCourtesyWorldReadStatus.Ready ? $" value {postBalance}" : string.Empty;
                    lines.Add($"post balance read {postStatus}{suffix}");
                    var expected = preBalance - DecrementAmount;
                    lines.Add($"expected post balance {expected}, observed {(postStatus == Release1SmallCourtesyWorldReadStatus.Ready ? postBalance.ToString() : "n/a")}");

                    return new(
                        postStatus == Release1SmallCourtesyWorldReadStatus.Ready && postBalance == expected
                            ? Release1StagingHarnessStatus.Succeeded
                            : Release1StagingHarnessStatus.Faulted,
                        lines);
                }
            }

            lines.Add($"no dead drop slot held a cash balance of at least {DecrementAmount}.");
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
