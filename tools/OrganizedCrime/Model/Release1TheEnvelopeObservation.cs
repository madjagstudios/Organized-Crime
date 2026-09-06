using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

/// <summary>One closet's cash this pass. Readable is false when any cash read for that closet returned a status other than Ready or Unavailable, so a transient fault is never read as "no cash".</summary>
public sealed record Release1TheEnvelopeClosetCash(string ClosetGuid, bool Readable, IReadOnlyList<Release1TheEnvelopeCashSlot> CashSlots);

public enum Release1TheEnvelopeClosetObservation
{
    RoomNotReady,
    ClosetUnreadable,
    NoCash,
    Shortfall,
    Sufficient,
    Spread
}

public static class Release1TheEnvelopeClosetClassifier
{
    /// <summary>The whole room in one decision. A cash slot is a slot whose cash balance read returned Ready; non cash items are ignored entirely, never counted and never a hold. Cash in two or more closets is always Spread, whatever the sums. A closet whose slot list reads back empty, or whose cash reads faulted, is ClosetUnreadable and the pass changes nothing.</summary>
    public static Release1TheEnvelopeClosetObservation Classify(
        Release1HoldRoomSnapshot? room,
        IReadOnlyList<Release1TheEnvelopeClosetCash>? closets,
        int expectedClosetCount,
        double amountWholeDollars,
        out string? cashClosetGuid,
        out double cashSum)
    {
        cashClosetGuid = null;
        cashSum = 0d;
        if (expectedClosetCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedClosetCount));
        if (!double.IsFinite(amountWholeDollars) || amountWholeDollars <= 0d)
            throw new ArgumentOutOfRangeException(nameof(amountWholeDollars));
        if (room is null || room.Readiness != Release1HoldRoomReadiness.Ready || room.Closets.Count != expectedClosetCount)
            return Release1TheEnvelopeClosetObservation.RoomNotReady;
        if (closets is null || closets.Count != expectedClosetCount)
            return Release1TheEnvelopeClosetObservation.RoomNotReady;

        foreach (var closet in room.Closets)
            if (closet is null || closet.SlotCount == 0 || closet.Slots.Count == 0)
                return Release1TheEnvelopeClosetObservation.ClosetUnreadable;
        foreach (var closet in closets)
            if (closet is null || !closet.Readable || closet.CashSlots is null)
                return Release1TheEnvelopeClosetObservation.ClosetUnreadable;

        var bearing = closets
            .Where(closet => closet.CashSlots.Any(slot => slot is not null && slot.Balance > 0d))
            .OrderBy(closet => closet.ClosetGuid, StringComparer.Ordinal)
            .ToArray();
        if (bearing.Length == 0) return Release1TheEnvelopeClosetObservation.NoCash;
        if (bearing.Length > 1) return Release1TheEnvelopeClosetObservation.Spread;

        cashClosetGuid = bearing[0].ClosetGuid;
        cashSum = bearing[0].CashSlots.Where(slot => slot is not null).Sum(slot => slot.Balance);
        return cashSum >= amountWholeDollars
            ? Release1TheEnvelopeClosetObservation.Sufficient
            : Release1TheEnvelopeClosetObservation.Shortfall;
    }
}
