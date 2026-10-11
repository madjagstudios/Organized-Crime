using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 offer feature (accept, decline, and the dead-drop
// freeze behind them). Moved verbatim out of Chapter3RuntimeService; only
// the receiver changed. Called only while the coordinator holds its gate;
// takes no locks itself. The delivery feature handles the drop
// subscriptions the accept refreshes.
internal sealed class Chapter3OffersFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3DeliveryFeature _delivery;

    public Chapter3OffersFeature(Chapter3FeatureContext ctx, Chapter3DeliveryFeature delivery)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    }

    /// <summary>
    /// The shared accept core behind the reply button and the QA command. A
    /// DeadDrop accept freezes its delivery first, a PackageRecovery accept its
    /// pair plus the staged bricks, an EmergencySupply accept its named drop
    /// plus the 12-hour window: when the world has nothing to move, Nell
    /// refuses, the offer stays open, and the refusal marker carries her line.
    /// Without a world the job stays unfrozen, as before.
    /// </summary>
    internal Chapter3OfferResult AcceptOfferLocked(Chapter3JobOffer offer, int chapterDay)
    {
        if (offer.JobType is (Chapter3JobType.TemporaryStorageSafehouse or Chapter3JobType.CashMovement or Chapter3JobType.OperationalShutdown) &&
            (_ctx.State!.JobBoard.OpenOffer != offer || _ctx.State.JobBoard.ActiveJob is not null || chapterDay > offer.RespondByDay))
            return new(false, null);
        if (offer.JobType == Chapter3JobType.CashMovement && !_ctx.Cash.Ready())
            return new(false, null);
        if (offer.JobType == Chapter3JobType.CashMovement && !_ctx.Cash.TryAccept(offer))
        {
            _ctx.State = _ctx.State! with
            {
                JobBoard = _ctx.State.JobBoard with { LastRefusedOfferId = offer.OfferId },
                Revision = _ctx.State.Revision + 1
            };
            _ctx.Log($"Chapter 3 cash offer {offer.OfferId} could not be revalidated; quoted terms unchanged.");
            return new(false, null);
        }
        if (offer.JobType == Chapter3JobType.OperationalShutdown)
        {
            if (!_ctx.Shutdown.Ready()) return new(false, null);
            if (!_ctx.Shutdown.TryAccept(offer))
            {
                _ctx.State = _ctx.State! with
                {
                    JobBoard = _ctx.State.JobBoard with { LastRefusedOfferId = offer.OfferId },
                    Revision = _ctx.State.Revision + 1
                };
                _ctx.Log($"Chapter 3 shutdown offer {offer.OfferId} could not be revalidated; quoted terms unchanged.");
                return new(false, null);
            }
        }
        Chapter3DeadDropDelivery? delivery = null;
        Chapter3PackageRecovery? recovery = null;
        Chapter3StorageRun? storage = null;
        double? acceptedMinutes = null;
        if (offer.JobType == Chapter3JobType.TemporaryStorageSafehouse &&
            !_ctx.Storage.TryAccept(offer, out storage))
        {
            _ctx.State = _ctx.State! with
            {
                JobBoard = _ctx.State.JobBoard with { LastRefusedOfferId = offer.OfferId },
                Revision = _ctx.State.Revision + 1
            };
            _ctx.Log($"Chapter 3 storage offer {offer.OfferId} could not be revalidated; quoted terms unchanged.");
            return new(false, null);
        }
        if (_ctx.DeadDropWorld is not null)
        {
            string? refusal = null;
            if (offer.JobType == Chapter3JobType.DeadDrop)
            {
                if (!TryFreezeDeliveryLocked(offer, out delivery, out var deliveryRefusal))
                    refusal = deliveryRefusal;
            }
            else if (offer.JobType == Chapter3JobType.PackageRecovery)
            {
                if (!TryFreezeRecoveryLocked(offer, out recovery, out var recoveryRefusal))
                    refusal = recoveryRefusal;
            }
            else if (offer.JobType == Chapter3JobType.EmergencySupply)
            {
                if (!TryFreezeEmergencyLocked(offer, out delivery, out acceptedMinutes, out var emergencyRefusal))
                    refusal = emergencyRefusal;
            }
            if (refusal is not null)
            {
                _ctx.State = _ctx.State! with
                {
                    JobBoard = _ctx.State.JobBoard with { LastRefusedOfferId = offer.OfferId },
                    Revision = _ctx.State.Revision + 1
                };
                _ctx.Log(refusal);
                return new(false, null);
            }
        }
        var accepted = Chapter3JobScheduler.Accept(_ctx.State!.JobBoard, offer.OfferId, chapterDay);
        if (ReferenceEquals(accepted.Board, _ctx.State.JobBoard))
            return new(false, null);
        var board = accepted.Board;
        if (delivery is not null)
            board = board with
            {
                ActiveJob = board.ActiveJob! with
                {
                    Delivery = delivery,
                    AcceptedGameMinutes = acceptedMinutes,
                    ExpiresGameMinutes = acceptedMinutes.HasValue
                        ? acceptedMinutes.Value + Chapter3JobTuning.EmergencySupplyWindowMinutes
                        : null
                }
            };
        if (storage is not null)
            board = board with { ActiveJob = board.ActiveJob! with { StorageRun = storage } };
        if (recovery is not null)
            board = board with { ActiveJob = board.ActiveJob! with { Recovery = recovery } };
        _ctx.State = _ctx.State with { JobBoard = board, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in accepted.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        if (delivery is not null)
        {
            var frozen =
                $"Chapter 3 job {board.ActiveJob!.JobId} frozen: {delivery.BrickCount} bricks of {delivery.ProductName} at {delivery.DropName}";
            _ctx.Log(board.ActiveJob.JobType == Chapter3JobType.EmergencySupply
                ? frozen + $"; {(int)Chapter3JobTuning.EmergencySupplyWindowMinutes} minutes to deliver."
                : frozen + ".");
            _delivery.RefreshDropSubscriptionsLocked();
        }
        if (storage is not null)
            _delivery.RefreshDropSubscriptionsLocked();
        if (recovery is not null)
        {
            _ctx.Log($"Chapter 3 job {board.ActiveJob!.JobId} frozen: {recovery.BrickCount} bricks of {recovery.ProductName} from {recovery.Pickup.Name} to {recovery.Handoff.Name}.");
            _ctx.Log($"Chapter 3 job {board.ActiveJob.JobId} staged {recovery.BrickCount} bricks of {recovery.ProductName} at {recovery.Pickup.Name}.");
            _delivery.RefreshDropSubscriptionsLocked();
        }
        return new(true, board.ActiveJob!.JobId);
    }

    /// <summary>
    /// Names the frozen product on an Emergency Supply offer from a live
    /// product read. False when no discovered product can be read; the caller
    /// falls back to a paid type (the tick) or refuses (QA, accept).
    /// </summary>
    internal bool TryFreezeOfferProductLocked(Chapter3JobOffer offer, out Chapter3JobOffer frozen)
    {
        frozen = offer;
        var world = _ctx.DeadDropWorld;
        if (world is null)
            return false;
        try
        {
            if (world.TryReadProducts(out var products) != Release1SmallCourtesyWorldReadStatus.Ready || products is null)
                return false;
            var picked = Chapter3JobScheduler.SelectEmergencyProduct(products, _ctx.State!.PlayerId, offer.OfferId);
            if (picked is null)
                return false;
            frozen = offer with { ProductId = picked.ProductId, ProductName = picked.ProductName };
            return true;
        }
        catch
        {
            frozen = offer;
            return false;
        }
    }

    private bool TryFreezeEmergencyLocked(
        Chapter3JobOffer offer,
        out Chapter3DeadDropDelivery? delivery,
        out double? acceptedMinutes,
        out string refusal)
    {
        string Refuse(string reason) => $"Chapter 3 offer {offer.OfferId} refused: {reason}.";
        delivery = null;
        acceptedMinutes = null;
        refusal = string.Empty;
        try
        {
            var world = _ctx.DeadDropWorld!;
            var productId = offer.ProductId;
            var productName = offer.ProductName;
            if (productId is null || productName is null)
            {
                // A stale unfrozen offer (only QA can make one, and it refuses
                // instead): name the product now, deterministically the same
                // pick the tick would have frozen.
                if (!TryFreezeOfferProductLocked(offer, out var frozen) ||
                    frozen.ProductId is null || frozen.ProductName is null)
                {
                    refusal = Refuse("there is no discovered product to name");
                    return false;
                }
                productId = frozen.ProductId;
                productName = frozen.ProductName;
            }
            if (world.TryReadDeadDrops(out var drops) != Release1SmallCourtesyWorldReadStatus.Ready || drops is null)
            {
                refusal = Refuse("the dead drops were unreadable");
                return false;
            }
            if (world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready ||
                packaging is null)
            {
                refusal = Refuse("the brick packaging was unreadable");
                return false;
            }
            if (!_ctx.TryReadCanonicalMinutesLocked(out var now))
            {
                refusal = Refuse("the game clock was unreadable");
                return false;
            }
            var bricks = Chapter3JobTuning.BricksForReward(offer.Reward);
            if (!Chapter3DeadDropFreeze.TryFreezeNamed(
                    productId, productName, drops, packaging,
                    $"chapter3-emergency-supply|{_ctx.State!.PlayerId}|{offer.OfferId}", bricks,
                    out delivery, out var status))
            {
                refusal = Refuse(status switch
                {
                    Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop => "there is no empty dead drop",
                    _ => "the freeze input was invalid"
                });
                return false;
            }
            acceptedMinutes = now;
            return true;
        }
        catch (Exception exception)
        {
            delivery = null;
            acceptedMinutes = null;
            refusal = Refuse($"the world faulted ({exception.GetType().Name})");
            return false;
        }
    }

    private bool TryFreezeDeliveryLocked(
        Chapter3JobOffer offer, out Chapter3DeadDropDelivery? delivery, out string refusal)
    {
        string Refuse(string reason) => $"Chapter 3 offer {offer.OfferId} refused: {reason}.";
        delivery = null;
        refusal = string.Empty;
        try
        {
            var world = _ctx.DeadDropWorld!;
            if (world.TryReadProducts(out var products) != Release1SmallCourtesyWorldReadStatus.Ready || products is null)
            {
                refusal = Refuse("the product list was unreadable");
                return false;
            }
            if (world.TryReadDeadDrops(out var drops) != Release1SmallCourtesyWorldReadStatus.Ready || drops is null)
            {
                refusal = Refuse("the dead drops were unreadable");
                return false;
            }
            if (world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready ||
                packaging is null)
            {
                refusal = Refuse("the brick packaging was unreadable");
                return false;
            }
            var bricks = Chapter3JobTuning.BricksForReward(offer.Reward);
            if (!Chapter3DeadDropFreeze.TryFreeze(
                    products, drops, packaging,
                    $"chapter3-dead-drop|{_ctx.State!.PlayerId}|{offer.OfferId}", bricks,
                    out delivery, out var status))
            {
                refusal = Refuse(status switch
                {
                    Chapter3DeadDropFreezeStatus.NoDiscoveredProduct => "there is no discovered product to move",
                    Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop => "there is no empty dead drop",
                    _ => "the freeze input was invalid"
                });
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            delivery = null;
            refusal = Refuse($"the world faulted ({exception.GetType().Name})");
            return false;
        }
    }

    private bool TryFreezeRecoveryLocked(
        Chapter3JobOffer offer, out Chapter3PackageRecovery? recovery, out string refusal)
    {
        string Refuse(string reason) => $"Chapter 3 offer {offer.OfferId} refused: {reason}.";
        recovery = null;
        refusal = string.Empty;
        try
        {
            var world = _ctx.DeadDropWorld!;
            if (world.TryReadProducts(out var products) != Release1SmallCourtesyWorldReadStatus.Ready || products is null)
            {
                refusal = Refuse("the product list was unreadable");
                return false;
            }
            if (world.TryReadDeadDrops(out var drops) != Release1SmallCourtesyWorldReadStatus.Ready || drops is null)
            {
                refusal = Refuse("the dead drops were unreadable");
                return false;
            }
            if (world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready ||
                packaging is null)
            {
                refusal = Refuse("the brick packaging was unreadable");
                return false;
            }
            var bricks = Chapter3JobTuning.BricksForReward(offer.Reward);
            if (!Chapter3PackageRecoveryFreeze.TryFreeze(
                    products, drops, packaging,
                    $"chapter3-recovery|{_ctx.State!.PlayerId}|{offer.OfferId}", bricks,
                    out var frozen, out var status))
            {
                refusal = Refuse(status switch
                {
                    Chapter3PackageRecoveryFreezeStatus.NoDiscoveredProduct => "there is no discovered product to move",
                    Chapter3PackageRecoveryFreezeStatus.NoEmptyDeadDropPair => "there is no empty dead drop pair",
                    _ => "the freeze input was invalid"
                });
                return false;
            }
            if (!_delivery.TryStageRecoveryLocked(frozen!, out var staged, out var stageReason))
            {
                refusal = Refuse(stageReason);
                return false;
            }
            recovery = staged;
            return true;
        }
        catch (Exception exception)
        {
            recovery = null;
            refusal = Refuse($"the world faulted ({exception.GetType().Name})");
            return false;
        }
    }

    internal Chapter3JobBoard FreezeFreshStorageOfferLocked(Chapter3JobBoard before, Chapter3JobBoard board)
    {
        var offer = board.OpenOffer;
        if (offer is null || offer.OfferId != before.NextOfferId) return board;
        // Preserve the shipped introductory sequence; later eligible paid work can be storage.
        if (offer.JobType == Chapter3JobType.EmergencySupply || offer.OfferId <= 5 ||
            before.LastTypeOffered == Chapter3JobType.TemporaryStorageSafehouse ||
            offer.OfferId % 3 != 0) return board;
        if (!_ctx.Storage.TryFreezeOffer(offer, out var frozen)) return board;
        return board with
        {
            OpenOffer = frozen, LastTypeOffered = frozen.JobType,
            TypesSeen = before.TypesSeen.Contains(frozen.JobType) ? before.TypesSeen : before.TypesSeen.Append(frozen.JobType).ToArray(),
            LastEmergencySupplyOfferId = before.LastEmergencySupplyOfferId
        };
    }

    internal Chapter3DeclineResult TryDeclineOfferLocked()
    {
        if (!_ctx.PresentationCommandReady(out var chapterDay))
            return new(false, null);
        var offer = _ctx.State!.JobBoard.OpenOffer;
        if (offer is null)
            return new(false, null);
        var refused = Chapter3JobScheduler.Refuse(_ctx.State.JobBoard, offer.OfferId, chapterDay);
        if (ReferenceEquals(refused.Board, _ctx.State.JobBoard))
            return new(false, null);
        _ctx.State = _ctx.State with { JobBoard = refused.Board, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in refused.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        return new(true, offer.OfferId);
    }

    internal Chapter3JobBoard FreezeFreshCashOfferLocked(Chapter3JobBoard before, Chapter3JobBoard board)
    {
        var offer = board.OpenOffer;
        // Keep the introductory and storage sequences intact; only promote eligible later paid work.
        if (offer is null || offer.OfferId != before.NextOfferId || offer.OfferId <= 5 || offer.OfferId % 3 != 1 ||
            offer.JobType is Chapter3JobType.EmergencySupply or Chapter3JobType.TemporaryStorageSafehouse ||
            before.LastTypeOffered == Chapter3JobType.CashMovement || !_ctx.Cash.TryFreezeOffer(offer, out var frozen)) return board;
        return board with
        {
            OpenOffer = frozen, LastTypeOffered = frozen.JobType,
            TypesSeen = before.TypesSeen.Contains(frozen.JobType) ? before.TypesSeen : before.TypesSeen.Append(frozen.JobType).ToArray(),
            LastEmergencySupplyOfferId = before.LastEmergencySupplyOfferId
        };
    }

    /// <summary>
    /// The Emergency Supply product seam: the pure scheduler cannot read the
    /// world, so a fresh Emergency Supply offer gets its named product (or a
    /// paid-type fallback when none can be read) right after the tick creates
    /// it. Also consumes the owner-QA force flag. Returns the board to keep;
    /// the same reference when nothing needed freezing.
    /// </summary>
    internal Chapter3JobBoard FreezeFreshEmergencyOfferLocked(Chapter3JobBoard boardBeforeTick, Chapter3JobBoard board)
    {
        var open = board.OpenOffer;
        if (open is null || open.OfferId != boardBeforeTick.NextOfferId)
            return board;
        if (_ctx.ForceNextEmergencyOffer && open.JobType != Chapter3JobType.EmergencySupply)
        {
            _ctx.ForceNextEmergencyOffer = false;
            board = Chapter3JobScheduler.ForceEmergencyOffer(board);
            open = board.OpenOffer!;
        }
        else if (open.JobType == Chapter3JobType.EmergencySupply)
        {
            _ctx.ForceNextEmergencyOffer = false;
        }
        else
        {
            return board;
        }
        if (TryFreezeOfferProductLocked(open, out var frozen))
        {
            _ctx.Log($"Chapter 3 offer {open.OfferId} names {Chapter3JobTuning.BricksForReward(open.Reward)} bricks of {frozen.ProductName}.");
            return board with { OpenOffer = frozen };
        }
        _ctx.Log($"Chapter 3 offer {open.OfferId} fell back to a paid type: no discovered product could be read.");
        return Chapter3JobScheduler.FallbackEmergencyOffer(board, boardBeforeTick, _ctx.State!.PlayerId);
    }

    internal Chapter3JobBoard FreezeFreshShutdownOfferLocked(Chapter3JobBoard before, Chapter3JobBoard board)
    {
        var offer = board.OpenOffer;
        if (offer is null || offer.OfferId != before.NextOfferId || offer.OfferId <= 5 || offer.OfferId % 3 != 2 ||
            offer.JobType is Chapter3JobType.EmergencySupply or Chapter3JobType.TemporaryStorageSafehouse or Chapter3JobType.CashMovement ||
            before.LastTypeOffered == Chapter3JobType.OperationalShutdown || !_ctx.Shutdown.TryFreezeOffer(offer, out var frozen)) return board;
        return board with
        {
            OpenOffer = frozen, LastTypeOffered = frozen.JobType,
            TypesSeen = before.TypesSeen.Contains(frozen.JobType) ? before.TypesSeen : before.TypesSeen.Append(frozen.JobType).ToArray(),
            LastEmergencySupplyOfferId = before.LastEmergencySupplyOfferId
        };
    }
}
