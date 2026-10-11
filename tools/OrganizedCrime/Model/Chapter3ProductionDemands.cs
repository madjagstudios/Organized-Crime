using System.Security.Cryptography;
using System.Text;

namespace OrganizedCrime.Model;

/// <summary>
/// OC-136: a demand's product type, one per native drug type. The
/// adapter maps the definition's drug type name through
/// <see cref="Chapter3DemandPolicy.TryParseProductType"/>; unknown
/// future types refuse the issue rather than guessing a quantity.
/// </summary>
public enum Chapter3DemandProductType
{
    Weed,
    Shrooms,
    Meth,
    Cocaine,
    Mdma,
    Heroin
}

/// <summary>
/// OC-136: the owner-tunable demand policy. Quantities are about one
/// day's warehouse output per product type; the issue offset keeps the
/// demand off the weekly cut's morning.
/// </summary>
public static class Chapter3DemandPolicy
{
    /// <summary>
    /// Issue morning: the 3rd day after the weekly cut's week pass, so
    /// the cut and the demand never land together.
    /// </summary>
    public const int IssueOffsetDays = 3;

    /// <summary>
    /// Deadline: 3 in-game days from issue.
    /// </summary>
    public const int DurationDays = 3;

    /// <summary>
    /// The family locker keeps its name in the storage UI at all times,
    /// so the player learns which locker takes the hand-over.
    /// </summary>
    public const string FamilyLockerName = "Family locker";

    /// <summary>
    /// About one day's output per product type. MDMA runs the same lab
    /// line as meth; heroin values like cocaine.
    /// </summary>
    public static int QuantityFor(Chapter3DemandProductType type) => type switch
    {
        Chapter3DemandProductType.Weed => 250,
        Chapter3DemandProductType.Shrooms => 200,
        Chapter3DemandProductType.Meth => 200,
        Chapter3DemandProductType.Cocaine => 150,
        Chapter3DemandProductType.Mdma => 200,
        Chapter3DemandProductType.Heroin => 150,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Chapter 3 demand product type was unknown: {(int)type}.")
    };

    /// <summary>
    /// Maps a native drug type name (EDrugType) to the demand type.
    /// Unknown names refuse: a future drug type must get an explicit
    /// table row before it can be demanded.
    /// </summary>
    public static bool TryParseProductType(string? drugTypeName, out Chapter3DemandProductType type)
    {
        type = Chapter3DemandProductType.Weed;
        if (string.IsNullOrWhiteSpace(drugTypeName))
            return false;
        switch (drugTypeName.Trim())
        {
            case "Marijuana":
                type = Chapter3DemandProductType.Weed;
                return true;
            case "Shrooms":
                type = Chapter3DemandProductType.Shrooms;
                return true;
            case "Methamphetamine":
                type = Chapter3DemandProductType.Meth;
                return true;
            case "Cocaine":
                type = Chapter3DemandProductType.Cocaine;
                return true;
            case "MDMA":
                type = Chapter3DemandProductType.Mdma;
                return true;
            case "Heroin":
                type = Chapter3DemandProductType.Heroin;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The weekly pick, deterministic like the dead-drop rule: sort the
    /// eligibility set ordinally, hash the week with SHA-256, take hash
    /// mod count. The same week and set always pick the same product, so
    /// tests pin it and players cannot predict it.
    /// </summary>
    public static bool TryPickProduct(IReadOnlyList<string>? madeProductIds, int weekIndex, out string? productId)
    {
        productId = null;
        if (madeProductIds is null)
            return false;
        var ordered = madeProductIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            return false;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(weekIndex + "\n" + string.Join("\n", ordered)));
        var hash = 0UL;
        for (var i = 0; i < 8 && i < digest.Length; i++)
            hash = (hash << 8) | digest[i];
        productId = ordered[(int)(hash % (ulong)ordered.Length)];
        return true;
    }
}

/// <summary>
/// OC-136: one in-flight locker removal. Saved on the demand, so a
/// reload between the native change and the credit reconciles instead
/// of removing twice.
/// </summary>
public sealed record Chapter3DemandPendingRemoval(int SlotIndex, int PreQuantity, int PackageCount, int Units)
{
    public void Validate()
    {
        if (SlotIndex < 0)
            throw new ArgumentException("Chapter 3 demand pending removal slot was invalid.", nameof(SlotIndex));
        if (PreQuantity < 0)
            throw new ArgumentException("Chapter 3 demand pending removal pre-quantity was negative.", nameof(PreQuantity));
        if (PackageCount < 1)
            throw new ArgumentException("Chapter 3 demand pending removal package count was invalid.", nameof(PackageCount));
        if (Units < 1)
            throw new ArgumentException("Chapter 3 demand pending removal units were invalid.", nameof(Units));
        if (PackageCount > PreQuantity)
            throw new ArgumentException("Chapter 3 demand pending removal took more packages than the slot held.", nameof(PackageCount));
    }
}

/// <summary>
/// OC-136: the family's open weekly production demand. The unit value
/// is the product definition's price frozen at issue; takes are capped
/// so Delivered never passes Quantity. LastTakeUnits stores the burst
/// total since the last receipt shown; HandOverCount correlates the latest
/// take so delayed receipts keep unique, save-stable ids.
/// </summary>
public sealed record Chapter3ProductionDemand(
    string DemandId,
    string ProductId,
    string ProductName,
    Chapter3DemandProductType ProductType,
    int Quantity,
    int UnitValue,
    int IssueDay,
    int DueDay,
    int FreshCount,
    int Delivered,
    int PaidTotal,
    int LastTakeUnits,
    int HandOverCount,
    double? HqDoorX,
    double? HqDoorY,
    double? HqDoorZ,
    Chapter3DemandPendingRemoval? PendingRemoval)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DemandId))
            throw new ArgumentException("Chapter 3 demand id was invalid.", nameof(DemandId));
        if (string.IsNullOrWhiteSpace(ProductId))
            throw new ArgumentException("Chapter 3 demand product id was invalid.", nameof(ProductId));
        if (ProductName is null)
            throw new ArgumentException("Chapter 3 demand product name was missing.", nameof(ProductName));
        if (!Enum.IsDefined(typeof(Chapter3DemandProductType), ProductType))
            throw new ArgumentException($"Chapter 3 demand product type was unknown: {(int)ProductType}.", nameof(ProductType));
        if (Quantity < 1)
            throw new ArgumentException("Chapter 3 demand quantity was invalid.", nameof(Quantity));
        if (UnitValue < 0)
            throw new ArgumentException("Chapter 3 demand unit value was negative.", nameof(UnitValue));
        if (DueDay <= IssueDay)
            throw new ArgumentException("Chapter 3 demand due day was not after the issue day.", nameof(DueDay));
        if (FreshCount < 0)
            throw new ArgumentException("Chapter 3 demand fresh count was negative.", nameof(FreshCount));
        if (Delivered < 0 || Delivered > Quantity)
            throw new ArgumentException("Chapter 3 demand delivered count was out of range.", nameof(Delivered));
        if (PaidTotal < 0)
            throw new ArgumentException("Chapter 3 demand paid total was negative.", nameof(PaidTotal));
        if (LastTakeUnits < 0)
            throw new ArgumentException("Chapter 3 demand last take was negative.", nameof(LastTakeUnits));
        if (HandOverCount < 0)
            throw new ArgumentException("Chapter 3 demand hand-over count was negative.", nameof(HandOverCount));
        if (HqDoorX.HasValue && !double.IsFinite(HqDoorX.Value))
            throw new ArgumentException("Chapter 3 demand HQ door position was invalid.", nameof(HqDoorX));
        if (HqDoorY.HasValue && !double.IsFinite(HqDoorY.Value))
            throw new ArgumentException("Chapter 3 demand HQ door position was invalid.", nameof(HqDoorY));
        if (HqDoorZ.HasValue && !double.IsFinite(HqDoorZ.Value))
            throw new ArgumentException("Chapter 3 demand HQ door position was invalid.", nameof(HqDoorZ));
        PendingRemoval?.Validate();
    }
}

/// <summary>
/// OC-136: what the journal and Nell's closing texts need after a
/// demand closes. Completed demands thank; missed ones name the
/// shortfall and the make-good amount.
/// </summary>
public sealed record Chapter3DemandOutcome(
    string DemandId,
    string ProductName,
    int LeftUnits,
    int Amount,
    bool Completed)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DemandId))
            throw new ArgumentException("Chapter 3 demand outcome id was invalid.", nameof(DemandId));
        if (ProductName is null)
            throw new ArgumentException("Chapter 3 demand outcome product name was missing.", nameof(ProductName));
        if (LeftUnits < 0)
            throw new ArgumentException("Chapter 3 demand outcome shortfall was negative.", nameof(LeftUnits));
        if (Amount < 0)
            throw new ArgumentException("Chapter 3 demand outcome amount was negative.", nameof(Amount));
    }
}

/// <summary>
/// OC-136: one warehouse output slot read: the slot index, the product
/// id in it (empty when the slot is empty), and its quantity.
/// </summary>
public sealed record Chapter3DemandOutputSlot(int SlotIndex, string ProductId, int Quantity);

/// <summary>
/// OC-136: one warehouse station read per poll: the operation state off
/// the activity snapshot plus the kind-specific finishing latch and the
/// output slots. Only the five producing kinds are ever read; packaging
/// stations are excluded by station type at the adapter.
/// </summary>
public sealed record Chapter3DemandStationSnapshot(
    string StationGuid,
    Release1ProductionStationKind Kind,
    bool Running,
    string OperationIdentity,
    int Progress,
    bool Finishing,
    IReadOnlyList<Chapter3DemandOutputSlot> Outputs);

/// <summary>
/// OC-136: what one counter pass found: the fresh gain toward the open
/// demand, product ids new to the eligibility set, and one verdict line
/// per counted or skipped increase.
/// </summary>
public sealed record Chapter3DemandCountResult(
    int FreshGain,
    IReadOnlyList<string> EligibilityAdds,
    IReadOnlyList<string> Verdicts);

/// <summary>
/// OC-136: the pure fresh-output counter over consecutive station
/// snapshots. An output increase counts only when the station completed
/// an operation in the same window: the finishing latch on either tick,
/// a running-to-idle edge, an operation identity change, or a progress
/// restart. Decreases never count; increases without a completion are
/// player insertions and never count. A station seen for the first time
/// baselines silently even with its latch set: its output is either
/// already counted (a reload) or pre-existing stock, never fresh.
/// Output from a batch that started before the demand but lands after
/// it counts, because the completion edge is still observed.
/// </summary>
public static class Chapter3DemandCounter
{
    public static Chapter3DemandCountResult Process(
        IReadOnlyList<Chapter3DemandStationSnapshot>? previous,
        IReadOnlyList<Chapter3DemandStationSnapshot>? current,
        string? demandedProductId,
        IReadOnlyCollection<string>? knownEligible)
    {
        var freshGain = 0;
        var eligibilityAdds = new List<string>();
        var verdicts = new List<string>();
        if (current is null)
            return new Chapter3DemandCountResult(0, eligibilityAdds, verdicts);
        var before = new Dictionary<string, Chapter3DemandStationSnapshot>(StringComparer.Ordinal);
        if (previous is not null)
        {
            foreach (var snapshot in previous)
            {
                if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.StationGuid))
                    before[snapshot.StationGuid] = snapshot;
            }
        }
        var known = knownEligible is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(knownEligible.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        foreach (var station in current)
        {
            if (station is null || string.IsNullOrWhiteSpace(station.StationGuid))
                continue;
            before.TryGetValue(station.StationGuid, out var prior);
            if (prior is null)
                continue;
            var completed = station.Finishing ||
                prior.Finishing ||
                OperationEnded(prior, station);
            if (station.Outputs is null)
                continue;
            foreach (var output in station.Outputs)
            {
                if (output is null || string.IsNullOrWhiteSpace(output.ProductId) || output.Quantity < 0)
                    continue;
                var beforeQuantity = PriorOutputQuantity(prior, output.SlotIndex);
                var delta = output.Quantity - beforeQuantity;
                if (delta <= 0)
                    continue;
                if (!completed)
                {
                    if (!string.IsNullOrWhiteSpace(demandedProductId) &&
                        string.Equals(output.ProductId, demandedProductId, StringComparison.Ordinal))
                        verdicts.Add($"Station {station.StationGuid} output rose by {delta} with no batch finishing; insertion, not counted.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(demandedProductId) &&
                    string.Equals(output.ProductId, demandedProductId, StringComparison.Ordinal))
                {
                    freshGain += delta;
                    verdicts.Add($"Station {station.StationGuid} finished a batch: +{delta} {output.ProductId} counted.");
                }
                if (known.Add(output.ProductId))
                    eligibilityAdds.Add(output.ProductId);
            }
        }
        return new Chapter3DemandCountResult(freshGain, eligibilityAdds, verdicts);
    }

    private static bool OperationEnded(Chapter3DemandStationSnapshot? prior, Chapter3DemandStationSnapshot current)
    {
        if (prior is null || !prior.Running)
            return false;
        if (!current.Running)
            return true;
        if (!string.Equals(prior.OperationIdentity ?? string.Empty, current.OperationIdentity ?? string.Empty, StringComparison.Ordinal))
            return true;
        return current.Progress < prior.Progress;
    }

    private static int PriorOutputQuantity(Chapter3DemandStationSnapshot? prior, int slotIndex)
    {
        if (prior?.Outputs is null)
            return 0;
        foreach (var output in prior.Outputs)
        {
            if (output is not null && output.SlotIndex == slotIndex && !string.IsNullOrWhiteSpace(output.ProductId))
                return Math.Max(0, output.Quantity);
        }
        return 0;
    }
}

/// <summary>
/// OC-136: one family locker slot read: the demanded-product match
/// fields plus units per package (1 loose, the packaging definition's
/// quantity when packaged, 0 when the packaging is unknown and the slot
/// must be left alone).
/// </summary>
public sealed record Chapter3DemandLockerSlot(
    int SlotIndex,
    string ProductId,
    int Quantity,
    bool IsPackaged,
    string PackagingId,
    int UnitsPerPackage);

/// <summary>
/// OC-136: one planned locker take: whole packages from one slot and
/// the units they carry.
/// </summary>
public sealed record Chapter3DemandSlotTake(int SlotIndex, int Packages, int Units);

/// <summary>
/// OC-136: the pure hand-over planner over one locker read. Takes only
/// the demanded product, in slot order, in whole packages that fit the
/// remaining cap; the cap is min(remaining, fresh minus delivered), so
/// anything beyond fresh product stays in the locker untouched.
/// </summary>
public static class Chapter3DemandHandover
{
    public static IReadOnlyList<Chapter3DemandSlotTake> PlanTakes(
        IReadOnlyList<Chapter3DemandLockerSlot>? slots,
        string? demandedProductId,
        int maxUnits)
    {
        var takes = new List<Chapter3DemandSlotTake>();
        if (slots is null || string.IsNullOrWhiteSpace(demandedProductId) || maxUnits < 1)
            return takes;
        var remaining = maxUnits;
        foreach (var slot in slots)
        {
            if (remaining < 1)
                break;
            if (slot is null || slot.Quantity < 1 || slot.UnitsPerPackage < 1)
                continue;
            if (!string.Equals(slot.ProductId, demandedProductId, StringComparison.Ordinal))
                continue;
            var packages = Math.Min(slot.Quantity, remaining / slot.UnitsPerPackage);
            if (packages < 1)
                continue;
            takes.Add(new Chapter3DemandSlotTake(slot.SlotIndex, packages, packages * slot.UnitsPerPackage));
            remaining -= packages * slot.UnitsPerPackage;
        }
        return takes;
    }
}
