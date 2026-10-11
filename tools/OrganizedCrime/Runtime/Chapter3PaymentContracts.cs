using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// What one wallet debit attempt did. The wallet moves money; the service
/// records state only on <see cref="Debited"/>.
/// </summary>
public enum Chapter3WalletDebit
{
    Debited,
    Short,
    Unavailable
}

/// <summary>
/// What one wallet credit attempt did. The wallet moves money and verifies the
/// delta; the service records state only on <see cref="Credited"/>.
/// </summary>
public enum Chapter3WalletCredit
{
    Credited,
    Unavailable
}

/// <summary>
/// Debits the on-hand wallet by exactly <paramref name="amount"/>. Reads the
/// pre balance itself and reports <see cref="Chapter3WalletDebit.Short"/>
/// without moving money when the wallet cannot cover the amount.
/// </summary>
public interface IChapter3Wallet
{
    Chapter3WalletDebit TryDebit(int amount);

    /// <summary>
    /// Credits the on-hand wallet by exactly <paramref name="amount"/>,
    /// verified by a before/after delta. A default implementation so the
    /// existing fakes, which never take this path, need no change.
    /// </summary>
    Chapter3WalletCredit TryCredit(int amount) => Chapter3WalletCredit.Unavailable;

    /// <summary>
    /// The explicit-bound twins, for the one payment above
    /// <see cref="Chapter3PaymentTuning.MaximumPayment"/>: the warehouse
    /// purchase debits (and refunds) at
    /// <see cref="Chapter3PaymentTuning.WarehousePrice"/>. Fakes inherit the
    /// unbounded forms; only the production wallet enforces.
    /// </summary>
    Chapter3WalletDebit TryDebit(int amount, int maximum) => TryDebit(amount);
    Chapter3WalletCredit TryCredit(int amount, int maximum) => TryCredit(amount);
}

/// <summary>
/// Debits the on-hand wallet through the small-courtesy world's bounded debit
/// form, with Chapter 3's own bound. Pre-reads the balance so a short wallet
/// reports <see cref="Chapter3WalletDebit.Short"/> without ever calling the
/// native mutation; a faulting or unreadable world reports
/// <see cref="Chapter3WalletDebit.Unavailable"/> and moves nothing.
/// </summary>
public sealed class Chapter3Wallet : IChapter3Wallet
{
    private readonly IWalletWorld _world;

    public Chapter3Wallet(IWalletWorld world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    public Chapter3WalletDebit TryDebit(int amount) => TryDebit(amount, Chapter3PaymentTuning.MaximumPayment);

    public Chapter3WalletDebit TryDebit(int amount, int maximum)
    {
        if (amount <= 0 || maximum <= 0 || amount > maximum)
            return Chapter3WalletDebit.Unavailable;
        float before;
        try
        {
            if (_world.TryReadCashBalance(out before) != Release1SmallCourtesyWorldReadStatus.Ready)
                return Chapter3WalletDebit.Unavailable;
        }
        catch
        {
            return Chapter3WalletDebit.Unavailable;
        }
        if (!float.IsFinite(before) || before < amount)
            return Chapter3WalletDebit.Short;
        Release1SmallCourtesyWorldMutationStatus status;
        try { status = _world.TryDebitCashBalance(-(float)amount, maximum); }
        catch
        {
            return Chapter3WalletDebit.Unavailable;
        }
        return status switch
        {
            Release1SmallCourtesyWorldMutationStatus.Succeeded => Chapter3WalletDebit.Debited,
            // Rejected after our own read means the balance moved under us (or
            // authority lapsed mid-press): the debt stays open either way, and
            // Nell's short line is the honest prompt.
            Release1SmallCourtesyWorldMutationStatus.Rejected => Chapter3WalletDebit.Short,
            // Ambiguous means the money may already be gone, so recording
            // nothing lets a retry press debit again. The Chief carries
            // prepared-effect machinery against exactly that; Chapter 3
            // deliberately does not: the window is one press, and a retry of a
            // debt the family still shows open is recoverable where silently
            // marking it paid is not.
            _ => Chapter3WalletDebit.Unavailable,
        };
    }

    /// <summary>
    /// Credits the on-hand wallet by exactly <paramref name="amount"/> through
    /// the small-courtesy world's positive-only credit form, verified by a
    /// before/after delta within the Release 1 cash tolerance. An ambiguous
    /// change whose delta matches still counts as credited: the money is
    /// observably there. Anything else reports
    /// <see cref="Chapter3WalletCredit.Unavailable"/> and the caller retries
    /// without re-removing the bricks.
    /// </summary>
    public Chapter3WalletCredit TryCredit(int amount) => TryCredit(amount, Chapter3PaymentTuning.MaximumPayment);

    public Chapter3WalletCredit TryCredit(int amount, int maximum)
    {
        if (amount <= 0 || maximum <= 0 || amount > maximum)
            return Chapter3WalletCredit.Unavailable;
        float before;
        try
        {
            if (_world.TryReadCashBalance(out before) != Release1SmallCourtesyWorldReadStatus.Ready)
                return Chapter3WalletCredit.Unavailable;
        }
        catch
        {
            return Chapter3WalletCredit.Unavailable;
        }
        if (!float.IsFinite(before) || before < 0f)
            return Chapter3WalletCredit.Unavailable;
        Release1SmallCourtesyWorldMutationStatus status;
        try { status = _world.TryChangeCashBalance(amount); }
        catch
        {
            return Chapter3WalletCredit.Unavailable;
        }
        if (status is not (Release1SmallCourtesyWorldMutationStatus.Succeeded or
                Release1SmallCourtesyWorldMutationStatus.Ambiguous))
            return Chapter3WalletCredit.Unavailable;
        float post;
        try
        {
            if (_world.TryReadCashBalance(out post) != Release1SmallCourtesyWorldReadStatus.Ready)
                return Chapter3WalletCredit.Unavailable;
        }
        catch
        {
            return Chapter3WalletCredit.Unavailable;
        }
        return float.IsFinite(post) &&
            MathF.Abs(post - (before + amount)) <= Release1SmallCourtesyRewardIdentity.FixedVerificationTolerance
            ? Chapter3WalletCredit.Credited
            : Chapter3WalletCredit.Unavailable;
    }
}

/// <summary>
/// What one Chapter 3 payment attempt did. Only <see cref="Applied"/> moved
/// money and recorded it; <see cref="Short"/> recorded a shortfall and kept
/// the debt; <see cref="Unavailable"/> and <see cref="NotPayable"/> recorded
/// nothing.
/// </summary>
public enum Chapter3MoneyOutcome
{
    Applied,
    Short,
    Unavailable,
    NotPayable
}

public sealed record Chapter3MoneyResult(Chapter3MoneyOutcome Outcome, int Amount);

public sealed record Chapter3OfferResult(bool Applied, int? JobId);

public sealed record Chapter3DeclineResult(bool Applied, int? OfferId);
