namespace OrganizedCrime.Model;

/// <summary>
/// A dead-drop job's frozen delivery: the one named drop, the brick product,
/// and how many bricks. Frozen at accept; OC-122 projects it into the journal
/// entry and map marker. RemovalConfirmed records that the bricks left the
/// slot while the reward is still unpaid, so a retry never removes twice.
/// </summary>
public sealed record Chapter3DeadDropDelivery(
    string DropGuid,
    string DropName,
    double DropX,
    double DropY,
    double DropZ,
    string ProductId,
    string ProductName,
    string PackagingId,
    string PackagingName,
    int BrickCount,
    bool RemovalConfirmed = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DropGuid))
            throw new ArgumentException("Chapter 3 delivery drop id was invalid.", nameof(DropGuid));
        if (DropName is null)
            throw new ArgumentException("Chapter 3 delivery drop name was missing.", nameof(DropName));
        if (!double.IsFinite(DropX) || !double.IsFinite(DropY) || !double.IsFinite(DropZ))
            throw new ArgumentException("Chapter 3 delivery drop position was invalid.", nameof(DropX));
        if (string.IsNullOrWhiteSpace(ProductId))
            throw new ArgumentException("Chapter 3 delivery product id was invalid.", nameof(ProductId));
        if (ProductName is null)
            throw new ArgumentException("Chapter 3 delivery product name was missing.", nameof(ProductName));
        if (string.IsNullOrWhiteSpace(PackagingId))
            throw new ArgumentException("Chapter 3 delivery packaging id was invalid.", nameof(PackagingId));
        if (PackagingName is null)
            throw new ArgumentException("Chapter 3 delivery packaging name was missing.", nameof(PackagingName));
        if (BrickCount < 1)
            throw new ArgumentException("Chapter 3 delivery brick count was invalid.", nameof(BrickCount));
    }
}

/// <summary>
/// The last job Nell's crew finished: enough for her completed line after the
/// active job is gone.
/// </summary>
public sealed record Chapter3JobCompletion(int JobId, Chapter3JobType JobType, int Reward)
{
    internal Chapter3CashMovement? CashMovement { get; init; }
    internal Chapter3Shutdown? Shutdown { get; init; }
    public void Validate()
    {
        if (JobId <= 0)
            throw new ArgumentException("Chapter 3 completion job id was invalid.", nameof(JobId));
        if (!Enum.IsDefined(typeof(Chapter3JobType), JobType))
            throw new ArgumentException($"Chapter 3 completion job type was unknown: {(int)JobType}.", nameof(JobType));
        if (Reward < 0)
            throw new ArgumentException("Chapter 3 completion reward was negative.", nameof(Reward));
        if (Shutdown is not null)
        {
            Shutdown.Validate(Reward, offered: false);
            if (JobType != Chapter3JobType.OperationalShutdown || CashMovement is not null || !Shutdown.Paid)
                throw new ArgumentException("Shutdown completion requires a payment receipt.");
        }
        if (CashMovement is not null)
        {
            CashMovement.Validate(Reward, offered: false);
            if (JobType != Chapter3JobType.CashMovement || Shutdown is not null || !CashMovement.Paid)
                throw new ArgumentException("Cash completion requires a confirmed payment receipt.");
        }
    }
}

/// <summary>
/// Why a dead-drop accept could not freeze a delivery. The offer stays open
/// either way.
/// </summary>
public enum Chapter3DeadDropFreezeStatus
{
    Frozen,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    InvalidInput
}

/// <summary>
/// Pure freeze choice over already-read world candidates: the best discovered
/// product (Small Courtesy's rule: highest asking price, ProductId breaks
/// ties) and one deterministically chosen empty drop. Reads and validation of
/// the world itself stay with the caller.
/// </summary>
public static class Chapter3DeadDropFreeze
{
    /// <summary>
    /// The Emergency Supply twin of <see cref="TryFreeze"/>: the product is
    /// already named on the offer, so only the drop is chosen (one empty drop
    /// via <c>TryChooseSingle</c>). The named product is trusted as frozen;
    /// re-reading discovery at accept would only refuse a job the player
    /// already said yes to.
    /// </summary>
    public static bool TryFreezeNamed(
        string? productId,
        string? productName,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1SmallCourtesyPackagingCandidate? packaging,
        string? correlationId,
        int brickCount,
        out Chapter3DeadDropDelivery? delivery,
        out Chapter3DeadDropFreezeStatus status)
    {
        delivery = null;
        status = Chapter3DeadDropFreezeStatus.InvalidInput;
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(productName) ||
            drops is null || packaging is null || brickCount < 1)
            return false;

        try
        {
            foreach (var candidate in drops)
                (candidate ?? throw new ArgumentException("Drop candidates cannot be null.")).Validate();
            packaging.Validate();

            if (!Release1DeadDropSelection.TryChooseSingle(drops, correlationId, out var drop) || drop is null)
            {
                status = Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop;
                return false;
            }
            delivery = new Chapter3DeadDropDelivery(
                drop.Guid, drop.Name, drop.X, drop.Y, drop.Z,
                productId, productName,
                packaging.PackagingId, packaging.PackagingName,
                brickCount);
            delivery.Validate();
            status = Chapter3DeadDropFreezeStatus.Frozen;
            return true;
        }
        catch (ArgumentException)
        {
            delivery = null;
            status = Chapter3DeadDropFreezeStatus.InvalidInput;
            return false;
        }
    }

    public static bool TryFreeze(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1SmallCourtesyPackagingCandidate? packaging,
        string? correlationId,
        int brickCount,
        out Chapter3DeadDropDelivery? delivery,
        out Chapter3DeadDropFreezeStatus status)
    {
        delivery = null;
        status = Chapter3DeadDropFreezeStatus.InvalidInput;
        if (products is null || drops is null || packaging is null || brickCount < 1)
            return false;

        try
        {
            foreach (var candidate in products)
                (candidate ?? throw new ArgumentException("Product candidates cannot be null.")).Validate();
            foreach (var candidate in drops)
                (candidate ?? throw new ArgumentException("Drop candidates cannot be null.")).Validate();
            packaging.Validate();

            // Small Courtesy's best-product rule: discovered only, highest
            // asking price first, ProductId breaks ties.
            var product = products
                .Where(candidate => candidate.IsDiscovered)
                .OrderByDescending(candidate => candidate.AskingPrice)
                .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (product is null)
            {
                status = Chapter3DeadDropFreezeStatus.NoDiscoveredProduct;
                return false;
            }
            if (!Release1DeadDropSelection.TryChooseSingle(drops, correlationId, out var drop) || drop is null)
            {
                status = Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop;
                return false;
            }
            delivery = new Chapter3DeadDropDelivery(
                drop.Guid, drop.Name, drop.X, drop.Y, drop.Z,
                product.ProductId, product.ProductName,
                packaging.PackagingId, packaging.PackagingName,
                brickCount);
            delivery.Validate();
            status = Chapter3DeadDropFreezeStatus.Frozen;
            return true;
        }
        catch (ArgumentException)
        {
            delivery = null;
            status = Chapter3DeadDropFreezeStatus.InvalidInput;
            return false;
        }
    }
}

/// <summary>
/// Why a delivery check holds instead of completing. One log line per reason
/// per job; the bricks stay the player's in every case.
/// </summary>
public enum Chapter3DeliveryHoldReason
{
    Uninitialized,
    Empty,
    Short,
    Spread,
    WrongProduct,
    WrongPackaging
}

public abstract record Chapter3DeliveryVerdict;

public sealed record Chapter3DeliveryComplete(int SlotIndex) : Chapter3DeliveryVerdict;

public sealed record Chapter3DeliveryHold(Chapter3DeliveryHoldReason Reason, string Detail) : Chapter3DeliveryVerdict;

/// <summary>
/// Pure delivery classifier over one drop's slots: complete when exactly one
/// slot holds at least N units of the frozen product in brick packaging, hold
/// otherwise. Identity matching reuses Short Notice's rule; a slot above N
/// still completes (only N are removed) and any second identity match spreads.
/// </summary>
public static class Chapter3DeadDropDeliveryClassifier
{
    public static Chapter3DeliveryVerdict Classify(
        IReadOnlyList<Release1SmallCourtesySlotSnapshot>? slots,
        string productId,
        string packagingId,
        int brickCount)
    {
        // A zero-length read is never empty: the drop has not finished
        // initializing, so nothing is recorded and the pass retries.
        if (slots is null || slots.Count == 0)
            return new Chapter3DeliveryHold(
                Chapter3DeliveryHoldReason.Uninitialized,
                "the drop reported no slots; it has not finished initializing");
        var fingerprint = new Release1ConsignmentFingerprint(productId, packagingId, brickCount);
        var matches = Release1ConsignmentClassifier.CountIdentityMatches(slots, fingerprint, out var single);
        if (matches > 1)
            return new Chapter3DeliveryHold(
                Chapter3DeliveryHoldReason.Spread,
                $"matching bricks sit in {matches} slots; the whole delivery must sit in one slot");
        if (matches == 1)
        {
            if (single!.Quantity >= brickCount)
                return new Chapter3DeliveryComplete(single.SlotIndex);
            return new Chapter3DeliveryHold(
                Chapter3DeliveryHoldReason.Short,
                $"slot {single.SlotIndex} holds {single.Quantity} of {brickCount} bricks");
        }
        if (slots.Any(slot => slot is not null && slot.Quantity > 0 && slot.IsPackaged &&
                string.Equals(slot.ProductId, productId, StringComparison.Ordinal)))
            return new Chapter3DeliveryHold(
                Chapter3DeliveryHoldReason.WrongPackaging,
                "the product is there but not in brick packaging");
        if (slots.Any(slot => slot is not null && slot.Quantity > 0 && slot.IsPackaged))
            return new Chapter3DeliveryHold(
                Chapter3DeliveryHoldReason.WrongProduct,
                "no slot holds the frozen product");
        return new Chapter3DeliveryHold(Chapter3DeliveryHoldReason.Empty, "the drop holds nothing yet");
    }
}
