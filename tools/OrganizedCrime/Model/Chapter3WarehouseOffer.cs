namespace OrganizedCrime.Model;

/// <summary>
/// Nell's standing offer of the Fish Warehouse. Recorded once, the day it
/// opens; it never closes until bought, and it never touches the refusal
/// streak. Purchased day is set exactly when Purchased is. A failed
/// overnight build clears Purchased and counts a fell-through instead of
/// closing the offer.
/// </summary>
public sealed record Chapter3WarehouseOffer(
    int OpenedDay,
    int LastReminderWeek = 0,
    bool Purchased = false,
    int? PurchasedDay = null,
    int NotReadyCount = 0,
    int FellThroughCount = 0)
{
    public void Validate()
    {
        if (OpenedDay < 0)
            throw new ArgumentException("Chapter 3 warehouse offer opened before day 0.");
        if (LastReminderWeek < 0)
            throw new ArgumentException("Chapter 3 warehouse last reminder week was negative.");
        if (Purchased != PurchasedDay.HasValue)
            throw new ArgumentException("Chapter 3 warehouse purchase day disagrees with the purchased flag.");
        if (PurchasedDay.HasValue && PurchasedDay.Value < OpenedDay)
            throw new ArgumentException("Chapter 3 warehouse was purchased before it was offered.");
        if (NotReadyCount < 0)
            throw new ArgumentException("Chapter 3 warehouse not-ready count was negative.");
        if (FellThroughCount < 0)
            throw new ArgumentException("Chapter 3 warehouse fell-through count was negative.");
    }
}

/// <summary>
/// The pure warehouse-offer transitions for the day tick. Ownership is a live
/// world fact, so the runtime passes it in; everything else is state.
/// </summary>
public static class Chapter3WarehouseOfferTransitions
{
    public const int ReminderIntervalDays = 7;

    /// <summary>
    /// Opens the offer at the first tick where the rules hold and the
    /// warehouse isn't already owned, and advances the reminder week while it
    /// stays unbought. Purchased offers never change.
    /// </summary>
    public static Chapter3WarehouseOffer? OnDayTick(
        Chapter3WarehouseOffer? offer, int chapterDay, bool eligible, bool owned)
    {
        if (chapterDay < 0)
            throw new ArgumentOutOfRangeException(nameof(chapterDay), "Chapter 3 chapter day was negative.");
        if (offer is not null && offer.Purchased)
            return offer;
        if (offer is null)
            return eligible && !owned ? new Chapter3WarehouseOffer(chapterDay) : null;
        var week = (chapterDay - offer.OpenedDay) / ReminderIntervalDays;
        return week > offer.LastReminderWeek ? offer with { LastReminderWeek = week } : offer;
    }
}
