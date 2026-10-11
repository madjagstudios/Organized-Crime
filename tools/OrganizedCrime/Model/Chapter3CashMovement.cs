namespace OrganizedCrime.Model;

// Chapter 3 owns this receipt, independently of Release 1's mission journal.
internal sealed record Chapter3CashMovement(
    int Advance,
    string? Plan = null,
    int ConfirmedSlots = 0,
    bool PendingDecrement = false,
    bool Ambiguous = false,
    double? CreditBefore = null,
    bool Paid = false)
{
    internal static readonly string FamilyLockerGuid = SyndicateHqStorageContract.Definitions
        .Single(d => d.Name == SyndicateHqStorageContract.FamilyLockerDefinitionName).Guid.ToString("D");
    internal static int AdvanceForReward(int reward) => reward <= 17_000 ? 5_000 : 10_000;
    internal int Stacks => Advance / 1000;
    internal bool Reconciling => Plan is not null;

    internal void Validate(int reward, bool offered)
    {
        if (reward < 15_000 || reward > 20_000 || reward % 500 != 0 || Advance != AdvanceForReward(reward))
            throw new ArgumentException("Cash movement terms were invalid.");
        if (Plan is null)
        {
            if (ConfirmedSlots != 0 || PendingDecrement || Ambiguous || CreditBefore.HasValue || Paid)
                throw new ArgumentException("Cash movement progress had no plan.");
            return;
        }
        if (offered || !Release1TheEnvelopeClosetPlan.TryParse(Plan, FamilyLockerGuid, out var parsed) ||
            parsed!.ConsumedAmount != Advance || parsed.Slots.Count != Stacks ||
            parsed.Slots.Any(s => s.PreBalance != 1000 || s.PostBalance != 0) ||
            ConfirmedSlots < 0 || ConfirmedSlots > Stacks || (PendingDecrement && ConfirmedSlots == Stacks) ||
            (CreditBefore.HasValue && (ConfirmedSlots != Stacks || PendingDecrement ||
                !double.IsFinite(CreditBefore.Value) || CreditBefore.Value < 0)) ||
            (Paid && (!CreditBefore.HasValue || Ambiguous)))
            throw new ArgumentException("Cash movement transaction was inconsistent.");
    }

    internal static bool TryReadLocker(Release1HoldRoomSnapshot? room, out Release1HoldRoomClosetSnapshot locker)
    {
        locker = null!;
        if (room?.Readiness != Release1HoldRoomReadiness.Ready) return false;
        try
        {
            room.Validate();
            var match = room.Closets.SingleOrDefault(c => c.ClosetGuid == FamilyLockerGuid);
            if (match is null || !match.Slots.Select(s => s.SlotIndex).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, match.SlotCount))) return false;
            locker = match;
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    internal static bool IsCash(Release1SmallCourtesySlotSnapshot slot) =>
        slot.Quantity > 0 && string.Equals(slot.ProductId, "cash", StringComparison.Ordinal);
}
