using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IDeadDropCashSlotWorld
{
    /// <summary>
    /// Reads a dead drop slot's <c>CashInstance.Balance</c>. Ready only when the slot holds a
    /// <c>CashInstance</c> at a finite, positive balance; an empty slot, a non-cash item, or a
    /// non-finite or non-positive balance all read Unavailable, never zero. Unlike
    /// <see cref="TryReadDeadDropSlots"/>, this never guesses a value for a non-product item: it is
    /// the one member proven to see a cash slot's dollar amount at all.
    /// </summary>
    Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance);
    /// <summary>
    /// Decrements a locked dead drop slot's <c>CashInstance.Balance</c> in place by exactly
    /// <paramref name="amount"/> (negative, magnitude bounded by <see cref="S1ApiRelease1SmallCourtesyWorld.MaximumCashDecrement"/>,
    /// the largest Release 1 amount). Rejected on an unlocked slot, a non-negative or out-of-bound
    /// amount, or a decrement that would leave a negative balance. Unavailable on a non-cash slot.
    /// Never called on a balance the caller has not just read.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount);
}
