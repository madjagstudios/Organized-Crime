using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.World;

public interface IHoldRoomWorld
{
    Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room);
    /// <summary>
    /// Reads a hold room closet slot's <c>CashInstance.Balance</c>. The closet-level mirror of
    /// <see cref="TryReadDeadDropSlotCashBalance"/>: Ready only when the slot holds a
    /// <c>CashInstance</c> at a finite, positive balance; an empty slot, a non-cash item, or a
    /// non-finite or non-positive balance all read Unavailable, never zero.
    /// </summary>
    Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance);
    /// <summary>
    /// Locks or unlocks a hold room closet slot, guarding the write that
    /// <see cref="TryChangeHoldRoomSlotCashBalance"/> requires. The closet-level mirror of
    /// <see cref="TrySetSlotLocked"/>: never takes a lock the game already holds, never releases one
    /// this mod does not own.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked);
    /// <summary>
    /// Decrements a locked hold room closet slot's <c>CashInstance.Balance</c> in place by exactly
    /// <paramref name="amount"/> (negative, magnitude bounded by <see cref="S1ApiRelease1SmallCourtesyWorld.MaximumCashDecrement"/>,
    /// the largest Release 1 amount). Rejected on an unlocked slot, a non-negative or out-of-bound
    /// amount, or a decrement that would leave a negative balance. Unavailable on a non-cash slot.
    /// Never called on a balance the caller has not just read. The closet-level mirror of
    /// <see cref="TryChangeDeadDropSlotCashBalance"/>.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount);
}
