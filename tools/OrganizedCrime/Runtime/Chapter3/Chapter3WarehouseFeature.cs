using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 warehouse feature (purchase, capacity, and the
// behind-cover build). Moved verbatim out of Chapter3RuntimeService; only
// the receiver changed. Called only while the coordinator holds its gate;
// takes no locks itself.
internal sealed class Chapter3WarehouseFeature
{
    private readonly Chapter3FeatureContext _ctx;

    public Chapter3WarehouseFeature(Chapter3FeatureContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    internal Chapter3MoneyResult TryBuyWarehouseLocked()
    {
        if (!_ctx.PresentationCommandReady(out var chapterDay))
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var offer = _ctx.State!.WarehouseOffer;
        if (offer is null || offer.Purchased)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        if (_ctx.State.Ledger.HasOpenMakeGood)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var acquisition = _ctx.WarehouseAcquisition;
        if (acquisition is not null && acquisition.IsOwned())
        {
            // Owned out from under the offer (today only the dev keys can
            // do that): adopt it into the story without charging, so the
            // button clears and the offer never re-opens.
            _ctx.Log("Chapter 3 warehouse purchase adopted the already-owned warehouse without charging.");
            _ctx.LoadBuildArmed = false;
            _ctx.State = _ctx.State with
            {
                WarehouseOffer = offer with { Purchased = true, PurchasedDay = chapterDay },
                Revision = _ctx.State.Revision + 1
            };
            return new(Chapter3MoneyOutcome.Applied, 0);
        }
        if (acquisition is null || !acquisition.CanAcquire())
        {
            _ctx.State = _ctx.State with
            {
                WarehouseOffer = offer with { NotReadyCount = offer.NotReadyCount + 1 },
                Revision = _ctx.State.Revision + 1
            };
            _ctx.Log("Chapter 3 warehouse purchase refused: the warehouse is not ready.");
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        }
        const int price = Chapter3PaymentTuning.WarehousePrice;
        var debit = _ctx.DebitLocked(price, price);
        if (debit == Chapter3WalletDebit.Unavailable)
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        if (debit == Chapter3WalletDebit.Short)
        {
            _ctx.State = _ctx.State with { PaymentShortfallCount = _ctx.State.PaymentShortfallCount + 1, Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Chapter 3 warehouse purchase short: the wallet cannot cover ${price:N0}.");
            return new(Chapter3MoneyOutcome.Short, 0);
        }
        // OC-126: the tap takes the money and records the purchase, but it
        // never spawns: the 1.5 s spawn runs behind the sleep screen or
        // the load screen, via PollPendingWarehouseBuild.
        _ctx.Log($"Chapter 3 warehouse purchased day {chapterDay} for ${price:N0}; the build runs behind the next sleep or load.");
        _ctx.LoadBuildArmed = false;
        _ctx.State = _ctx.State with
        {
            WarehouseOffer = offer with { Purchased = true, PurchasedDay = chapterDay },
            Revision = _ctx.State.Revision + 1
        };
        return new(Chapter3MoneyOutcome.Applied, price);
    }

    internal Chapter3MoneyResult TryBuyWarehouseCapacityLocked()
    {
        if (!_ctx.PresentationCommandReady(out _))
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var offer = Chapter3WarehouseCapacityTiers.OpenOfferTier(
            _ctx.State!.WarehouseOffer?.Purchased == true,
            _ctx.State.WarehouseCapacityTier,
            _ctx.State.DemandsCompleted);
        if (!offer.HasValue)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        if (_ctx.State.Ledger.HasOpenMakeGood)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var acquisition = _ctx.WarehouseAcquisition;
        if (acquisition is null || !acquisition.IsOwned())
        {
            _ctx.Log("Chapter 3 warehouse capacity purchase refused: the warehouse is not owned.");
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        }
        var capacity = _ctx.WarehouseCapacity;
        if (capacity is null || !capacity.IsAttached())
        {
            _ctx.Log("Chapter 3 warehouse capacity purchase refused: the warehouse is not attached.");
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        }
        var tier = offer.Value;
        var price = Chapter3PaymentTuning.CapacityPriceForTier(tier);
        var debit = _ctx.DebitLocked(price);
        if (debit == Chapter3WalletDebit.Unavailable)
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        if (debit == Chapter3WalletDebit.Short)
        {
            _ctx.State = _ctx.State with { PaymentShortfallCount = _ctx.State.PaymentShortfallCount + 1, Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Chapter 3 warehouse capacity purchase short: the wallet cannot cover ${price:N0}.");
            return new(Chapter3MoneyOutcome.Short, 0);
        }
        var staff = Chapter3WarehouseCapacityTiers.CapacityForTier(tier);
        _ctx.Log($"Chapter 3 warehouse capacity tier {tier} purchased for ${price:N0}; the warehouse takes {staff} now.");
        _ctx.State = _ctx.State with
        {
            WarehouseCapacityTier = tier,
            Revision = _ctx.State.Revision + 1
        };
        if (!capacity.TryApplyCapacity(staff))
            _ctx.Log("Chapter 3 warehouse capacity purchase applied at record time only: the live raise missed, and the tier applies at the next attach.");
        return new(Chapter3MoneyOutcome.Applied, price);
    }

    /// <summary>
    /// OC-117: the warehouse can attach before Chapter 3 turns Active, and
    /// then it reads tier 1, so the 1 Hz host poll raises a live capacity
    /// that sits below the saved tier. Raise-only: a replay-lifted capacity
    /// is never pulled down here, and a detached warehouse is skipped (its
    /// Property still holds the native default until attach).
    /// </summary>
    internal void ReconcileWarehouseCapacityLocked()
    {
        var capacity = _ctx.WarehouseCapacity;
        if (capacity is null || !capacity.IsAttached() || !capacity.TryReadLive(out var live, out _))
            return;
        var tier = _ctx.State!.WarehouseCapacityTier;
        var staff = Chapter3WarehouseCapacityTiers.CapacityForTier(tier);
        if (live >= staff)
            return;
        if (capacity.TryApplyCapacity(staff))
            _ctx.Log($"Chapter 3 warehouse capacity reconciled: live {live} was below tier {tier}, raised to {staff}.");
    }

    internal int ReadWarehouseTierCapacityLocked()
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            return Chapter3WarehouseCapacityTiers.Tier1Capacity;
        return Chapter3WarehouseCapacityTiers.CapacityForTier(_ctx.State.WarehouseCapacityTier);
    }

    internal bool ReadHasPendingWarehouseBuildLocked()
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            return false;
        var offer = _ctx.State.WarehouseOffer;
        var acquisition = _ctx.WarehouseAcquisition;
        return offer is not null && offer.Purchased && acquisition is not null && acquisition.IsOwnershipSettled() && !acquisition.IsOwned();
    }

    internal void PollPendingWarehouseBuildLocked(Func<bool> sleepEventsRunning)
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            return;
        if (!HasPendingWarehouseBuildLocked())
        {
            if (HasUnsettledWarehouseBuildLocked())
            {
                // Purchased but ownership unknown: the load window never
                // arms for this (it arms from the pending check at load),
                // so the build waits for sleep cover. Say so once.
                if (!_ctx.UnsettledBuildNotified)
                {
                    _ctx.UnsettledBuildNotified = true;
                    _ctx.Log("Chapter 3 warehouse build waits: warehouse ownership is unsettled while persistence recovers.");
                }
                return;
            }
            _ctx.UnsettledBuildNotified = false;
            return;
        }
        _ctx.UnsettledBuildNotified = false;
        bool sleeping;
        try
        {
            sleeping = sleepEventsRunning();
        }
        catch
        {
            return;
        }
        if (sleeping)
        {
            _ctx.LoadBuildArmed = false;
            TryBuildPendingWarehouseLocked();
            return;
        }
        if (!_ctx.LoadBuildArmed)
            return;
        _ctx.LoadBuildArmed = false;
        if (_ctx.WarehouseAcquisition is null || !_ctx.WarehouseAcquisition.CanAcquire())
        {
            _ctx.Log("Chapter 3 load window missed the pending warehouse build: unacquirable on the first poll; it waits for the next sleep.");
            return;
        }
        TryBuildPendingWarehouseLocked();
    }

    internal void NotifySleepStartedLocked()
    {
        _ctx.LoadBuildArmed = false;
    }

    internal bool HasPendingWarehouseBuildLocked()
    {
        var offer = _ctx.State!.WarehouseOffer;
        var acquisition = _ctx.WarehouseAcquisition;
        return offer is not null && offer.Purchased && acquisition is not null && acquisition.IsOwnershipSettled() && !acquisition.IsOwned();
    }

    private bool HasUnsettledWarehouseBuildLocked()
    {
        var offer = _ctx.State!.WarehouseOffer;
        var acquisition = _ctx.WarehouseAcquisition;
        return offer is not null && offer.Purchased && acquisition is not null && !acquisition.IsOwned() && !acquisition.IsOwnershipSettled();
    }

    internal bool TryBuildPendingWarehouseLocked()
    {
        var offer = _ctx.State!.WarehouseOffer;
        var acquisition = _ctx.WarehouseAcquisition;
        if (offer is null || !offer.Purchased || acquisition is null || acquisition.IsOwned())
            return false;
        // Unsettled ownership waits for the next poll; it never fails the
        // build, so there is no refund and no fell-through here.
        if (!acquisition.IsOwnershipSettled())
            return false;
        if (!acquisition.CanAcquire())
        {
            FailPendingWarehouseBuildLocked(offer, "the warehouse is not acquirable");
            return false;
        }
        if (!acquisition.TryAcquire())
        {
            FailPendingWarehouseBuildLocked(offer, "the acquire failed");
            return false;
        }
        _ctx.Log("Chapter 3 warehouse built behind cover; Nell's built text is queued.");
        return true;
    }

    private void FailPendingWarehouseBuildLocked(Chapter3WarehouseOffer offer, string reason)
    {
        const int price = Chapter3PaymentTuning.WarehousePrice;
        var refund = _ctx.CreditLocked(price, price);
        if (refund != Chapter3WalletCredit.Credited)
            OrganizedCrimeLog.Warning($"Chapter 3 warehouse build failed ({reason}) AND failed to refund ${price:N0}; the money is unaccounted.");
        else
            OrganizedCrimeLog.Warning($"Chapter 3 warehouse build failed ({reason}); the ${price:N0} debit was refunded and the offer is open again.");
        _ctx.State = _ctx.State! with
        {
            WarehouseOffer = offer with { Purchased = false, PurchasedDay = null, FellThroughCount = offer.FellThroughCount + 1 },
            Revision = _ctx.State!.Revision + 1
        };
    }

    /// <summary>
    /// Opens Nell's warehouse offer at the first tick where the rules hold and
    /// the warehouse isn't already owned, and advances the reminder week while
    /// it stays unbought. Needs the acquisition seam for live ownership;
    /// without it the offer never opens. Jobs are untouched: the warehouse
    /// offer runs alongside them.
    /// </summary>
    internal void UpdateWarehouseOfferLocked(int? chapterDay)
    {
        var acquisition = _ctx.WarehouseAcquisition;
        if (!chapterDay.HasValue || acquisition is null)
            return;
        var before = _ctx.State!.WarehouseOffer;
        var after = Chapter3WarehouseOfferTransitions.OnDayTick(
            before, chapterDay.Value, _ctx.State.IsWarehouseOfferEligible(), acquisition.IsOwned());
        if (ReferenceEquals(after, before))
            return;
        _ctx.State = _ctx.State with { WarehouseOffer = after, Revision = _ctx.State.Revision + 1 };
        if (before is null && after is not null)
            _ctx.Log($"Chapter 3 warehouse offer opened day {chapterDay.Value}.");
        else if (after is not null && after.LastReminderWeek != before!.LastReminderWeek)
            _ctx.Log($"Chapter 3 warehouse reminder week {after.LastReminderWeek} due.");
    }
}
