using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IWalletWorld
{
    Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance);
    Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount);
    /// <summary>
    /// OC-73 spec decision 13 and review amendment 1. Debits the on hand wallet by exactly
    /// <paramref name="amount"/> (negative, magnitude at most <c>MaximumWalletDebit</c>). Mirrors
    /// <see cref="TryChangeDeadDropSlotCashBalance"/>'s negative only convention rather than widening
    /// <see cref="TryChangeCashBalance"/>, whose <c>amount &lt;= 0f</c> guard the three live proven
    /// reward payouts rely on. Reads the pre balance itself and rejects a debit that would carry the
    /// wallet negative. Never called on a balance the caller has not just read.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount);
    /// <summary>
    /// Same debit with an explicit magnitude bound supplied by the caller. Chapter 3 pays
    /// make-goods and cuts through this with its own bound; the Chief keeps his ladder cap
    /// on the single-argument form. A default implementation so the existing fakes, which
    /// never take this path, need no change; production overrides it below.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount, float maximumDebit) =>
        TryDebitCashBalance(amount);
}
