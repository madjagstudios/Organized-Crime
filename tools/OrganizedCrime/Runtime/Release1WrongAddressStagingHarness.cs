using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1StagingHarnessStatus
{
    Succeeded,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    Unavailable,
    Rejected,
    Faulted,
    // OC-69: a world mutation genuinely ran and changed something, but its post state could not be
    // confirmed clean (for example a despawn whose contact still resolves, or a spawn placed but
    // refused an approach). Distinct from Faulted, which means the call threw or hit an unhandled
    // case; Ambiguous is an expected, non exceptional outcome the owner protocol's own PASS bar
    // already names, so it must be a status this enum can actually print.
    Ambiguous,
    // OC-73: the lockdown gate could not safely take a host side action (specifically, enabling
    // curfew on a save where it was not yet unlocked by the story) because this session is not the
    // authoritative host. This is not a fault: the read succeeded and nothing was left half changed,
    // there is simply a second, owner driven path needed instead (see
    // docs/worknotes/evidence/oc-73-lockdown-gate-owner-protocol.md).
    NeedsOptionB
}

public sealed record Release1StagingHarnessResult(Release1StagingHarnessStatus Status, IReadOnlyList<string> Lines);

/// <summary>
/// OC-52 owner QA harness. Proves, without any mission logic, that OC can create one packaged
/// product instance inside an empty vanilla dead drop slot and that the world reports it back
/// exactly. Gated in the shell on the OwnerQaKeys preference; no Unity or S1API reference here so
/// it stays test linkable.
/// </summary>
public static class Release1WrongAddressStagingHarness
{
    public static Release1StagingHarnessResult TryStage(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return Faulted("the world was not available.");
        var lines = new List<string>();
        try
        {
            var productsStatus = world.TryReadProducts(out var products);
            if (productsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                lines.Add($"products could not be read, status {productsStatus}.");
                return new(MapReadStatus(productsStatus), lines);
            }

            var product = (products ?? Array.Empty<Release1SmallCourtesyProductCandidate>())
                .Where(candidate => candidate is not null && candidate.IsDiscovered)
                .OrderByDescending(candidate => candidate.AskingPrice)
                .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (product is null)
            {
                lines.Add("no discovered product was available to stage.");
                return new(Release1StagingHarnessStatus.NoDiscoveredProduct, lines);
            }
            lines.Add($"selected product {product.ProductId} at asking price {product.AskingPrice}.");

            var packagingStatus = world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging);
            if (packagingStatus != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                lines.Add($"brick packaging could not be read, status {packagingStatus}.");
                return new(MapReadStatus(packagingStatus), lines);
            }
            lines.Add($"selected packaging {packaging.PackagingId}.");

            var dropsStatus = world.TryReadDeadDrops(out var drops);
            if (dropsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                lines.Add($"dead drops could not be read, status {dropsStatus}.");
                return new(MapReadStatus(dropsStatus), lines);
            }

            var drop = (drops ?? Array.Empty<Release1SmallCourtesyDropCandidate>())
                .Where(candidate => candidate is not null && candidate.IsEmpty)
                .OrderBy(candidate => candidate.Guid, StringComparer.Ordinal)
                .FirstOrDefault();
            if (drop is null)
            {
                lines.Add("no empty dead drop was available to stage into.");
                return new(Release1StagingHarnessStatus.NoEmptyDeadDrop, lines);
            }
            lines.Add($"selected dead drop {drop.Name} with GUID {drop.Guid}.");

            var mutationStatus = world.TryInsertPackagedProduct(drop.Guid, 0, product.ProductId, packaging.PackagingId, 1, out var reason);
            lines.Add($"insert result {mutationStatus}, reason: {reason}");
            return new(MapMutationStatus(mutationStatus), lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while staging: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    public static Release1StagingHarnessResult TryDump(IRelease1SmallCourtesyWorld world)
    {
        if (world is null) return Faulted("the world was not available.");
        var lines = new List<string>();
        try
        {
            var dropsStatus = world.TryReadDeadDrops(out var drops);
            if (dropsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                lines.Add($"dead drops could not be read, status {dropsStatus}.");
                return new(MapReadStatus(dropsStatus), lines);
            }

            var dropList = (drops ?? Array.Empty<Release1SmallCourtesyDropCandidate>())
                .Where(candidate => candidate is not null)
                .OrderBy(candidate => candidate.Guid, StringComparer.Ordinal)
                .ToList();

            lines.Add($"dead drops read: {dropList.Count}");

            foreach (var drop in dropList)
            {
                var slotsStatus = world.TryReadDeadDropSlots(drop.Guid, out var slots);
                if (slotsStatus != Release1SmallCourtesyWorldReadStatus.Ready)
                {
                    lines.Add($"drop {drop.Name} ({drop.Guid}) slots {slotsStatus}");
                    continue;
                }

                var slotList = (slots ?? Array.Empty<Release1SmallCourtesySlotSnapshot>())
                    .Where(candidate => candidate is not null)
                    .OrderBy(candidate => candidate.SlotIndex)
                    .ToList();

                var totalSlots = slotList.Count;
                var occupiedSlots = slotList.Count(s => s.Quantity > 0);

                lines.Add($"drop {drop.Name} ({drop.Guid}) slots {totalSlots}, occupied {occupiedSlots}");

                foreach (var slot in slotList.Where(candidate => candidate.Quantity > 0))
                {
                    lines.Add($"drop {drop.Name} ({drop.Guid}) slot {slot.SlotIndex} product {slot.ProductId} packaging {slot.PackagingId} quantity {slot.Quantity} value {slot.MonetaryValue}");
                }
            }

            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    private static Release1StagingHarnessResult Faulted(string message) =>
        new(Release1StagingHarnessStatus.Faulted, new[] { message });

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
