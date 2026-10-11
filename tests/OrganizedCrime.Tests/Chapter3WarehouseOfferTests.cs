using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-125: Nell's warehouse offer opens once, reminds weekly, and never
/// closes until bought.
/// </summary>
public sealed class Chapter3WarehouseOfferTests
{
    [Fact]
    public void Offer_opens_when_eligible_and_unowned()
    {
        var offer = Chapter3WarehouseOfferTransitions.OnDayTick(null, chapterDay: 18, eligible: true, owned: false);

        Assert.NotNull(offer);
        Assert.Equal(18, offer!.OpenedDay);
        Assert.Equal(0, offer.LastReminderWeek);
        Assert.False(offer.Purchased);
        Assert.Null(offer.PurchasedDay);
        Assert.Equal(0, offer.NotReadyCount);
    }

    [Fact]
    public void Offer_stays_closed_without_eligibility_or_when_owned()
    {
        Assert.Null(Chapter3WarehouseOfferTransitions.OnDayTick(null, 18, eligible: false, owned: false));
        Assert.Null(Chapter3WarehouseOfferTransitions.OnDayTick(null, 18, eligible: true, owned: true));
    }

    [Fact]
    public void Open_offer_never_closes_and_advances_the_reminder_week()
    {
        var offer = new Chapter3WarehouseOffer(OpenedDay: 18);

        var sameWeek = Chapter3WarehouseOfferTransitions.OnDayTick(offer, 19, eligible: false, owned: false);
        Assert.Same(offer, sameWeek);

        var weekOne = Chapter3WarehouseOfferTransitions.OnDayTick(offer, 25, eligible: false, owned: true);
        Assert.NotNull(weekOne);
        Assert.Equal(1, weekOne!.LastReminderWeek);
        Assert.False(weekOne.Purchased);

        // A later make-good (eligibility lost) doesn't close it.
        var weekTwo = Chapter3WarehouseOfferTransitions.OnDayTick(weekOne, 32, eligible: false, owned: false);
        Assert.Equal(2, weekTwo!.LastReminderWeek);
    }

    [Fact]
    public void Purchased_offer_never_changes()
    {
        var offer = new Chapter3WarehouseOffer(OpenedDay: 18, Purchased: true, PurchasedDay: 20);

        Assert.Same(offer, Chapter3WarehouseOfferTransitions.OnDayTick(offer, 60, eligible: false, owned: true));
    }

    [Fact]
    public void Negative_chapter_day_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Chapter3WarehouseOfferTransitions.OnDayTick(null, -1, eligible: true, owned: false));
    }

    [Fact]
    public void Validate_rejects_inconsistent_offers()
    {
        Assert.Throws<ArgumentException>(() => new Chapter3WarehouseOffer(OpenedDay: -1).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3WarehouseOffer(OpenedDay: 3, LastReminderWeek: -1).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3WarehouseOffer(OpenedDay: 3, Purchased: true).Validate());
        Assert.Throws<ArgumentException>(() =>
            new Chapter3WarehouseOffer(OpenedDay: 3, Purchased: true, PurchasedDay: 2).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3WarehouseOffer(OpenedDay: 3, NotReadyCount: -1).Validate());
    }
}
