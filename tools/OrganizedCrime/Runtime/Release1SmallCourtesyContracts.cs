using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1SmallCourtesyWorldReadStatus
{
    Ready,
    Pending,
    NotAuthoritative,
    Unavailable,
    Faulted
}

public enum Release1SmallCourtesyPackageKind
{
    Brick,
    Jar
}

public sealed record Release1SmallCourtesyPackagingCandidate(string PackagingId, string PackagingName)
{
    public void Validate()
    {
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
        Release1SmallCourtesyAssignment.ValidatePresentation(PackagingName, 256, nameof(PackagingName));
    }
}

public interface IRelease1SmallCourtesyWorld
{
    Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context);
    Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes);
    Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products);
    Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops);
    Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging);
    Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots);
    Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room);
    Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked);
    Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount);
    Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason);
    Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription);
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
    /// OC-73 spec decision 17. Holds the town's own curfew active at every hour through the seam
    /// already proven under this key, <see cref="Release1LockdownGatePatch"/>. Idempotent: an already
    /// engaged lockdown reports Succeeded and changes nothing. Reports Unavailable, never a failure,
    /// when the gate reports NeedsOptionB (curfew is not enabled on this save and this session is not
    /// the authoritative host, or no local player connection is available) so the caller can hold and
    /// retry. <paramref name="reason"/> carries the gate's own diagnostic lines for the one line per
    /// load the caller logs. No new owner QA key: the shipped End key becomes a second caller of this
    /// same member.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason);

    /// <summary>
    /// The mirror of <see cref="TryEngageLockdown"/>. Clears the lockdown flag so the very next native
    /// per minute recompute restores the vanilla schedule, and disables curfew on the save through the
    /// game's own Disable path only when this gate itself enabled it. Idempotent on an already
    /// released lockdown.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason);
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
    /// <summary>One read of every owned property, its employees and its stations. Read only, one call per pass,
    /// and the only native surface this mission has. Grow containers, customers and dealers are never read.</summary>
    Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity);

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05 (on-demand construction abandoned; see
    /// <see cref="Release1ArthurFieldContactHarness"/>'s own doc comment). Stops the contact's movement,
    /// moves it to <see cref="Release1ArthurFieldContactHarness.ParkingPoint"/>, deactivates its game
    /// object, and forces the native NPC invisible. Rejected on a non authoritative host or an invalid
    /// id; Unavailable when nothing resolves to that id.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 parked lifecycle, 2026-09-05. Snaps a point <paramref name="aheadMetres"/> in front of the
    /// local player to the navmesh, sets the contact's position there, forces its game object active and
    /// the native NPC visible, re-checks <c>CanGetTo</c> from the contact's own actual position, and
    /// issues the approach if it now succeeds. Rejected on a non authoritative host, an invalid id, or a
    /// non finite, non positive, or out of bound distance. Unlike the abandoned on-demand construct
    /// path, the contact's own native components already exist by the time this ever runs (it always
    /// unparks a previously parked, load constructed contact), so a read right after this call is
    /// expected Ready immediately, with no native-resolve poll needed.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason);

    /// <summary>
    /// Kept for the no-persistence fallback: removes the field contact and reports whether it still
    /// resolves afterwards. Unavailable when nothing resolves to that id; Ambiguous when the removal ran
    /// but it still does. A full despawn is the out of game cleanup path; F4 never calls this member.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 spike. Points the contact's own combat behaviour at the local player through the one
    /// S1API member that names the game's combat path, and returns. OC sets no weapon, writes no
    /// aggression tuning, and never damages anything; whatever the game then does is measured by
    /// <see cref="TryReadFieldContact"/>, never configured here.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason);

    /// <summary>
    /// OC-69 spike. Reads the contact, the local player, and the law in one pass. Ready whenever the
    /// host is authoritative and the local player is available, whether or not the contact resolves:
    /// an unresolved contact reads Present false with every contact only field at its defined unset
    /// value while every player and law field still reads live. Unavailable keeps the meaning every
    /// other read member here gives it, that the host is not authoritative or the local player is
    /// unavailable, in which case no field in the snapshot is to be trusted.
    /// </summary>
    Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot);
}

public enum Release1SmallCourtesyWorldMutationStatus
{
    Succeeded,
    Rejected,
    Unavailable,
    Ambiguous
}

public sealed record Release1SmallCourtesySlotSnapshot(
    int SlotIndex,
    string? ProductId,
    string? PackagingId,
    int Quantity,
    bool IsPackaged,
    float MonetaryValue)
{
    public void Validate()
    {
        if (SlotIndex < 0 || Quantity < 0 || !float.IsFinite(MonetaryValue) || MonetaryValue < 0f)
            throw new ArgumentException("Slot snapshot values were invalid.");
        if (Quantity == 0) return;
        Release1SmallCourtesyAssignment.ValidateStableId(ProductId, nameof(ProductId));
        Release1SmallCourtesyAssignment.ValidateStableId(PackagingId, nameof(PackagingId));
    }
}

public interface IRelease1SmallCourtesyDropSubscription : IDisposable
{
    string DeadDropGuid { get; }
}

public enum Release1SmallCourtesyDepositStatus
{
    NoWork,
    AwaitingPreparedSave,
    Applied,
    AwaitingAppliedSave,
    Committed,
    Ambiguous,
    Rejected
}

public enum Release1SmallCourtesyRewardStatus
{
    NoWork,
    AwaitingPreparedSave,
    Applied,
    AwaitingAppliedSave,
    Committed,
    Ambiguous,
    Rejected
}

public enum Release1SmallCourtesyOfferStatus
{
    Inactive,
    Available,
    Ineligible,
    Pending,
    Saving,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    Unavailable,
    Dismissed,
    Disposed
}

public enum Release1SmallCourtesyReviewStatus
{
    Ready,
    Inactive,
    Ineligible,
    Pending,
    Saving,
    NoDiscoveredProduct,
    NoEmptyDeadDrop,
    Unavailable,
    Dismissed,
    Disposed
}

public enum Release1SmallCourtesyDecisionStatus
{
    Accepted,
    Deferred,
    Dismissed,
    ReviewRequired,
    QuoteChanged,
    PersistenceDeferred,
    Rejected,
    Inactive,
    Disposed
}

public sealed record Release1SmallCourtesyQuote(
    Release1SmallCourtesyAssignment Assignment,
    double? DeadlineDurationHours)
{
    public void Validate()
    {
        Assignment.Validate();
        if (Assignment.Mode == Release1SmallCourtesyAssignmentMode.Recovery)
        {
            if (DeadlineDurationHours is not null)
                throw new ArgumentException("Recovery quotes cannot have a deadline.", nameof(DeadlineDurationHours));
            return;
        }
        if (DeadlineDurationHours != 24d)
            throw new ArgumentException("Timed Small Courtesy stages require a 24-hour duration.", nameof(DeadlineDurationHours));
    }
}

public sealed record Release1SmallCourtesyReviewResult(
    Release1SmallCourtesyReviewStatus Status,
    Release1SmallCourtesyQuote? Quote,
    string Message);

public sealed record Release1SmallCourtesyDecisionResult(
    Release1SmallCourtesyDecisionStatus Status,
    string Message);
