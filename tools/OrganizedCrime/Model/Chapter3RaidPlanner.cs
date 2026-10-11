namespace OrganizedCrime.Model;

internal readonly record struct Chapter3RaidSlotIdentity(Guid StorageGuid, int SlotIndex);
internal sealed record Chapter3RaidSlot(Chapter3RaidSlotIdentity Identity, string Fingerprint,
    string ProductId, string Packaging, int Quantity, int UnitsPerPackage);
internal sealed record Chapter3RaidTake(Chapter3RaidSlotIdentity Identity, string Fingerprint,
    int PreQuantity, int Packages, long Units);
internal sealed record Chapter3RaidPlan(long TotalUnits, long Budget, long PlannedUnits,
    IReadOnlyList<Chapter3RaidTake> Takes);
internal sealed record Chapter3RaidSite
{
    internal Chapter3RaidSite(string code, string name, bool stockKnown,
        IReadOnlyList<Chapter3RaidSlot> slots, Chapter3RaidPresence presence)
    {
        Code = code; Name = name; StockKnown = stockKnown; Slots = slots; Presence = presence;
        TotalUnits = stockKnown ? Chapter3RaidPlanner.TotalUnits(slots) : 0;
    }
    internal string Code { get; }
    internal string Name { get; }
    internal bool StockKnown { get; }
    internal IReadOnlyList<Chapter3RaidSlot> Slots { get; }
    internal Chapter3RaidPresence Presence { get; }
    internal long TotalUnits { get; }
}

// Pure planning over a frozen, filtered stock snapshot. No writes or cooldown.
internal static class Chapter3RaidPlanner
{
    internal static long TotalUnits(IReadOnlyList<Chapter3RaidSlot> slots)
    {
        long total = 0;
        var identities = new HashSet<Chapter3RaidSlotIdentity>();
        foreach (var slot in slots)
        {
            if (slot.Identity.StorageGuid == Guid.Empty || slot.Identity.SlotIndex < 0 ||
                slot.Quantity < 0 || slot.UnitsPerPackage < 1 || string.IsNullOrWhiteSpace(slot.ProductId) ||
                string.IsNullOrWhiteSpace(slot.Packaging) || string.IsNullOrWhiteSpace(slot.Fingerprint) ||
                !identities.Add(slot.Identity))
                throw new ArgumentException("Raid stock is invalid or has duplicate slot identities.", nameof(slots));
            total = checked(total + (long)slot.Quantity * slot.UnitsPerPackage);
        }
        return total;
    }

    internal static Chapter3RaidPlan? Plan(IReadOnlyList<Chapter3RaidSlot> slots)
        => Plan(slots, TotalUnits(slots));

    internal static Chapter3RaidPlan? Plan(Chapter3RaidSite site) =>
        site.StockKnown ? Plan(site.Slots, site.TotalUnits) : null;

    private static Chapter3RaidPlan? Plan(IReadOnlyList<Chapter3RaidSlot> slots, long total)
    {
        var budget = total / 4;
        var remaining = budget;
        var takes = new List<Chapter3RaidTake>();
        foreach (var slot in slots.OrderBy(s => s.Identity.StorageGuid.ToString("D"), StringComparer.Ordinal)
                     .ThenBy(s => s.Identity.SlotIndex))
        {
            var packages = (int)Math.Min(slot.Quantity, remaining / slot.UnitsPerPackage);
            if (packages == 0) continue;
            var units = (long)packages * slot.UnitsPerPackage;
            takes.Add(new(slot.Identity, slot.Fingerprint, slot.Quantity, packages, units));
            remaining -= units;
        }
        return takes.Count == 0 ? null : new(total, budget, budget - remaining, takes.ToArray());
    }

    // Occupancy is a separate gate: select the richest stock first, then wait
    // for that site. Do not fall back to a poorer empty site while it is occupied.
    internal static Chapter3RaidSite? Pick(IReadOnlyList<Chapter3RaidSite> sites) => sites
        .Where(s => s.StockKnown && s.TotalUnits > 0)
        .OrderByDescending(s => s.TotalUnits).ThenBy(s => s.Code, StringComparer.Ordinal).FirstOrDefault();
}

internal sealed class Chapter3RaidExclusions
{
    internal HashSet<Guid> Storage { get; } = new();
    internal HashSet<Chapter3RaidSlotIdentity> Slots { get; } = new();
    internal HashSet<(string Product, string Packaging)> Products { get; } = new();

    internal static Chapter3RaidExclusions From(Chapter3State? state)
    {
        var result = new Chapter3RaidExclusions();
        var job = state?.JobBoard.ActiveJob;
        void Route(Chapter3PackageRecovery? route)
        {
            if (route is null) return;
            result.Products.Add((route.ProductId, route.PackagingId));
            if (Guid.TryParse(route.Pickup.Guid, out var pickup)) result.Storage.Add(pickup);
            if (Guid.TryParse(route.Handoff.Guid, out var handoff)) result.Storage.Add(handoff);
        }
        Route(job?.Recovery);
        Route(job?.StorageRun?.Route);
        Route(state?.JobBoard.OpenOffer?.StorageRun?.Route);
        if (job?.Delivery is { } delivery)
        {
            result.Products.Add((delivery.ProductId, delivery.PackagingId));
            if (Guid.TryParse(delivery.DropGuid, out var drop)) result.Storage.Add(drop);
        }
        // Storage-run PendingSlot belongs to Route.Handoff, already excluded by
        // storage GUID above. Products covers its stock through payout; it never
        // reserves that index in the personal closet recorded during the hold.
        if (job?.CashMovement?.Plan is { } cash &&
            Release1TheEnvelopeClosetPlan.TryParse(cash, Chapter3CashMovement.FamilyLockerGuid, out var plan))
            foreach (var slot in plan!.Slots)
                result.Slots.Add(new(Guid.Parse(plan.ClosetGuid), slot.SlotIndex));
        return result;
    }
}

internal static class Chapter3RaidEligibility
{
    private static readonly Guid Family = SyndicateHqStorageContract.Definitions
        .Single(d => d.Name == SyndicateHqStorageContract.FamilyLockerDefinitionName).Guid;

    internal static Chapter3RaidSlot? Capture(Chapter3RaidSlot candidate, Chapter3RaidExclusions exclusions) =>
        Allows(candidate, exclusions) ? candidate : null;

    // Product type must be verified at the adapter boundary before building a slot.
    internal static bool Allows(Chapter3RaidSlot slot, Chapter3RaidExclusions exclusions) =>
        slot.Identity.StorageGuid != Family &&
        slot.Identity.StorageGuid != Guid.Empty && slot.Identity.SlotIndex >= 0 &&
        slot.Quantity > 0 && slot.UnitsPerPackage > 0 && !string.IsNullOrWhiteSpace(slot.ProductId) &&
        !exclusions.Storage.Contains(slot.Identity.StorageGuid) && !exclusions.Slots.Contains(slot.Identity) &&
        !exclusions.Products.Contains((slot.ProductId, slot.Packaging));
}
