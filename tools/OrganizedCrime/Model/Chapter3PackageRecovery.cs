namespace OrganizedCrime.Model;

/// <summary>
/// One frozen end of a package recovery: the pickup drop OC stages into, or
/// the handoff drop the player delivers to.
/// </summary>
public sealed record Chapter3RecoveryDrop(
    string Guid,
    string Name,
    double X,
    double Y,
    double Z)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Guid))
            throw new ArgumentException("Chapter 3 recovery drop id was invalid.", nameof(Guid));
        if (Name is null)
            throw new ArgumentException("Chapter 3 recovery drop name was missing.", nameof(Name));
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z))
            throw new ArgumentException("Chapter 3 recovery drop position was invalid.", nameof(X));
    }
}

/// <summary>
/// A package-recovery job's frozen run: the pickup/handoff pair, the brick
/// product, and how many bricks. Frozen at accept; the stage flags advance
/// Staged then Custody, and RemovalConfirmed records that the handoff bricks
/// left the slot while the reward is still unpaid. StagedSlotIndex is the slot
/// the staging read-back verified, or -1 until staged.
/// </summary>
public sealed record Chapter3PackageRecovery(
    Chapter3RecoveryDrop Pickup,
    Chapter3RecoveryDrop Handoff,
    string ProductId,
    string ProductName,
    string PackagingId,
    string PackagingName,
    int BrickCount,
    int StagedSlotIndex = -1,
    bool Staged = false,
    bool Custody = false,
    bool RemovalConfirmed = false)
{
    public void Validate()
    {
        if (Pickup is null)
            throw new ArgumentException("Chapter 3 recovery pickup was missing.", nameof(Pickup));
        if (Handoff is null)
            throw new ArgumentException("Chapter 3 recovery handoff was missing.", nameof(Handoff));
        Pickup.Validate();
        Handoff.Validate();
        if (string.Equals(Pickup.Guid, Handoff.Guid, StringComparison.Ordinal))
            throw new ArgumentException("Chapter 3 recovery drops were not distinct.", nameof(Handoff));
        if (string.IsNullOrWhiteSpace(ProductId))
            throw new ArgumentException("Chapter 3 recovery product id was invalid.", nameof(ProductId));
        if (ProductName is null)
            throw new ArgumentException("Chapter 3 recovery product name was missing.", nameof(ProductName));
        if (string.IsNullOrWhiteSpace(PackagingId))
            throw new ArgumentException("Chapter 3 recovery packaging id was invalid.", nameof(PackagingId));
        if (PackagingName is null)
            throw new ArgumentException("Chapter 3 recovery packaging name was missing.", nameof(PackagingName));
        if (BrickCount < 1)
            throw new ArgumentException("Chapter 3 recovery brick count was invalid.", nameof(BrickCount));
        if (StagedSlotIndex < -1)
            throw new ArgumentException("Chapter 3 recovery staged slot was invalid.", nameof(StagedSlotIndex));
        if (Custody && !Staged)
            throw new ArgumentException("Chapter 3 recovery cannot be in custody before it is staged.", nameof(Custody));
        if (RemovalConfirmed && !Custody)
            throw new ArgumentException("Chapter 3 recovery cannot confirm a removal before custody.", nameof(RemovalConfirmed));
    }
}

/// <summary>
/// Why a package-recovery accept could not freeze a run. The offer stays open
/// either way.
/// </summary>
public enum Chapter3PackageRecoveryFreezeStatus
{
    Frozen,
    NoDiscoveredProduct,
    NoEmptyDeadDropPair,
    InvalidInput
}

/// <summary>
/// Pure freeze choice over already-read world candidates: the best discovered
/// product (Small Courtesy's rule: highest asking price, ProductId breaks
/// ties) and a deterministically chosen pair of distinct empty drops. Reads
/// and validation of the world itself stay with the caller.
/// </summary>
public static class Chapter3PackageRecoveryFreeze
{
    public static bool TryFreeze(
        IReadOnlyList<Release1SmallCourtesyProductCandidate>? products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        Release1SmallCourtesyPackagingCandidate? packaging,
        string? correlationId,
        int brickCount,
        out Chapter3PackageRecovery? recovery,
        out Chapter3PackageRecoveryFreezeStatus status)
    {
        recovery = null;
        status = Chapter3PackageRecoveryFreezeStatus.InvalidInput;
        if (products is null || drops is null || packaging is null || brickCount < 1)
            return false;

        try
        {
            foreach (var candidate in products)
                (candidate ?? throw new ArgumentException("Product candidates cannot be null.")).Validate();
            foreach (var candidate in drops)
                (candidate ?? throw new ArgumentException("Drop candidates cannot be null.")).Validate();
            packaging.Validate();

            // Small Courtesy's best-product rule, the same rule the dead-drop
            // freeze uses: discovered only, highest asking price first,
            // ProductId breaks ties.
            var product = products
                .Where(candidate => candidate.IsDiscovered)
                .OrderByDescending(candidate => candidate.AskingPrice)
                .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (product is null)
            {
                status = Chapter3PackageRecoveryFreezeStatus.NoDiscoveredProduct;
                return false;
            }
            if (!Release1DeadDropSelection.TryChoosePair(drops, correlationId, out var pickup, out var handoff) ||
                pickup is null || handoff is null)
            {
                status = Chapter3PackageRecoveryFreezeStatus.NoEmptyDeadDropPair;
                return false;
            }
            recovery = new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop(pickup.Guid, pickup.Name, pickup.X, pickup.Y, pickup.Z),
                new Chapter3RecoveryDrop(handoff.Guid, handoff.Name, handoff.X, handoff.Y, handoff.Z),
                product.ProductId, product.ProductName,
                packaging.PackagingId, packaging.PackagingName,
                brickCount);
            recovery.Validate();
            status = Chapter3PackageRecoveryFreezeStatus.Frozen;
            return true;
        }
        catch (ArgumentException)
        {
            recovery = null;
            status = Chapter3PackageRecoveryFreezeStatus.InvalidInput;
            return false;
        }
    }
}
