using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-151 raised the Chapter 3 QA facade with the checkpoint members; OC-152
// moved the remaining QA members here. Bodies are verbatim out of
// Chapter3RuntimeService (same locks on the coordinator's shared gate, same
// refusals and messages); only the receiver changed. Disposal reads the
// injected predicate, like the checkpoint members always did. The two
// parameter constructor keeps the checkpoint tests working with a default
// context; production uses the full constructor.
// Owner QA only (OwnerQaKeys): every member here is owner QA. The oc_ch3 command
// is registered only when OwnerQaKeys is on.
public sealed class Chapter3QaFacade
{
    private readonly object _gate;
    private readonly Func<bool> _isDisposed;
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3OffersFeature _offers;
    private readonly Chapter3DeliveryFeature _delivery;
    private readonly Chapter3DemandFeature _demand;
    private readonly Chapter3PaymentsFeature _payments;
    private readonly Chapter3ArthurFeature _arthur;
    private Chapter3CheckpointService? _checkpoints;
    // Internal owner QA runs on the main thread alongside patrol convergence.
    internal Chapter3PatrolService? Patrols { get; set; }
    internal Func<LocalPressureTier, Chapter3QaResult>? PatrolHeat { get; set; }
    internal Func<bool, Chapter3QaResult>? PatrolLockdown { get; set; }
    internal Chapter3QaResult SetPatrolAuto(bool enabled) => _isDisposed() || Patrols is null
        ? new(false, "patrol auto refused: service not wired or disposed.") : Patrols.SetAuto(enabled);
    internal Chapter3QaResult SetPatrolHeat(LocalPressureTier tier) => _isDisposed() || Patrols?.IsHost != true || PatrolHeat is null
        ? new(false, "heat refused: host only, live service required.") : PatrolHeat(tier);
    internal Chapter3QaResult SetPatrolLockdown(bool enabled) => _isDisposed() || Patrols?.IsHost != true || PatrolLockdown is null
        ? new(false, "lockdown refused: host only, live service required.") : PatrolLockdown(enabled);

    public Chapter3QaFacade(object gate, Func<bool> isDisposed)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
        _ctx = new Chapter3FeatureContext
        {
            Log = static _ => { },
            LoggedDeliveryHolds = new Core.Diagnostics.OnceLog(static _ => { }),
            ArthurPump = new Chapter3ArthurVisitPump(static () => 0f)
        };
        _delivery = new Chapter3DeliveryFeature(_ctx, static _ => { });
        _demand = new Chapter3DemandFeature(_ctx);
        _payments = new Chapter3PaymentsFeature(_ctx);
        _arthur = new Chapter3ArthurFeature(_ctx);
        _offers = new Chapter3OffersFeature(_ctx, _delivery);
    }

    internal Chapter3QaFacade(
        object gate,
        Func<bool> isDisposed,
        Chapter3FeatureContext ctx,
        Chapter3OffersFeature offers,
        Chapter3DeliveryFeature delivery,
        Chapter3DemandFeature demand,
        Chapter3PaymentsFeature payments,
        Chapter3ArthurFeature arthur)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _offers = offers ?? throw new ArgumentNullException(nameof(offers));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _demand = demand ?? throw new ArgumentNullException(nameof(demand));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _arthur = arthur ?? throw new ArgumentNullException(nameof(arthur));
    }

    public Chapter3CheckpointService? Checkpoints
    {
        get { lock (_gate) return _checkpoints; }
        set { lock (_gate) _checkpoints = value; }
    }

    // Drives the checkpoint service through the wired link; without it every subcommand
    // refuses.
    public IReadOnlyList<string> QaCheckpointStatus()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return new[] { "checkpoints: Chapter 3 is not active." };
            if (_checkpoints is null)
                return new[] { "checkpoints: the service is not wired." };
            try { return _checkpoints.QaStatus(); }
            catch (Exception ex) { return new[] { $"checkpoints: the service threw ({ex.Message})." }; }
        }
    }

    public Chapter3QaResult QaCheckpointSpotHere(string label)
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA checkpoint spot here refused: Chapter 3 is not active.");
            if (_checkpoints is null)
                return QaRefused("Chapter 3 QA checkpoint spot here refused: the service is not wired.");
            try { return _checkpoints.QaSpotHere(label); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA checkpoint spot here refused: the service threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaCheckpointTest(string label)
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA checkpoint test refused: Chapter 3 is not active.");
            if (_checkpoints is null)
                return QaRefused("Chapter 3 QA checkpoint test refused: the service is not wired.");
            try { return _checkpoints.QaSpawnTest(label); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA checkpoint test refused: the service threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaCheckpointClear()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA checkpoint clear refused: Chapter 3 is not active.");
            if (_checkpoints is null)
                return QaRefused("Chapter 3 QA checkpoint clear refused: the service is not wired.");
            try { return _checkpoints.QaClearTests(); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA checkpoint clear refused: the service threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaCheckpointPick()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA checkpoint pick refused: Chapter 3 is not active.");
            if (_checkpoints is null)
                return QaRefused("Chapter 3 QA checkpoint pick refused: the service is not wired.");
            try { return _checkpoints.QaPickNow(); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA checkpoint pick refused: the service threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaAcceptOpenOffer()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA accept refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA accept refused: Chapter 3 has not started.");
            var offer = _ctx.State.JobBoard.OpenOffer;
            if (offer is null)
                return QaRefused("Chapter 3 QA accept refused: there is no open offer.");
            var outcome = _offers.AcceptOfferLocked(offer, chapterDay.Value);
            if (!outcome.Applied)
                return _ctx.State.JobBoard.LastRefusedOfferId == offer.OfferId
                    ? QaRefused($"Chapter 3 QA accept refused: Nell refused offer {offer.OfferId}; the offer stays open.")
                    : QaRefused("Chapter 3 QA accept refused: there is no open offer.");
            var job = _ctx.State.JobBoard.ActiveJob!;
            if (job.JobType == Chapter3JobType.EmergencySupply && job.ExpiresGameMinutes.HasValue)
                return QaOk($"Chapter 3 QA: accepted offer {offer.OfferId} ({offer.JobType}); job {job.JobId} expires in {(int)Chapter3JobTuning.EmergencySupplyWindowMinutes} minutes");
            return QaOk($"Chapter 3 QA: accepted offer {offer.OfferId} ({offer.JobType}); job {job.JobId} due day {job.DeadlineDay}");
        }
    }

    public Chapter3QaResult QaFailActiveJob()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA fail refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA fail refused: Chapter 3 has not started.");
            var job = _ctx.State.JobBoard.ActiveJob;
            if (job is null)
                return QaRefused("Chapter 3 QA fail refused: there is no active job.");
            if (job.Shutdown is not null)
            {
                if (!_ctx.Shutdown.Ready() || job.Shutdown.Reconciling) return QaRefused("Shutdown failure suppressed: canonical host required and reconciliation must not have begun.");
                _ctx.Shutdown.Fail(chapterDay.Value);
                return QaOk($"Chapter 3 QA: failed shutdown job {job.JobId}; reward-sized make-good opened.");
            }
            if (job.CashMovement is not null)
            {
                if (!_ctx.Cash.Ready() || job.CashMovement.Reconciling) return QaRefused("Cash failure suppressed: canonical host required and reconciliation must not have begun.");
                _ctx.Cash.Fail(chapterDay.Value);
                return QaOk($"Chapter 3 QA: failed cash job {job.JobId}; reward-only make-good opened.");
            }
            if (job.StorageRun is not null)
            {
                if (job.StorageRun.Reconciling) return QaRefused("Storage failure suppressed: payout reconciliation has begun.");
                _ctx.Storage.Fail(chapterDay.Value);
                return QaOk($"Chapter 3 QA: failed storage job {job.JobId}; reward-sized make-good opened.");
            }
            var completed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, succeeded: false, chapterDay.Value);
            _ctx.State = _ctx.State with { JobBoard = completed.Board, Revision = _ctx.State.Revision + 1 };
            _delivery.DisposeDropSubscriptionsLocked();
            _ctx.LoggedDeliveryHolds.ClearAll();
            _ctx.PendingRemoval = null;
            var ledgerEffects = _delivery.ApplyLedgerTickLocked(completed.Effects, chapterDay);
            if (job.JobType == Chapter3JobType.PackageRecovery && job.Recovery is not null)
                _delivery.CleanupRecoveryLocked(job.JobId, job.Recovery);
            foreach (var ledgerEffect in ledgerEffects)
                _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(ledgerEffect));
            var entry = _ctx.State.Ledger.Entries.FirstOrDefault(candidate => candidate.Source is Chapter3FailedJobSource failed && failed.JobId == job.JobId);
            return entry is null
                ? QaOk($"Chapter 3 QA: failed job {job.JobId} ({job.JobType})")
                : QaOk($"Chapter 3 QA: failed job {job.JobId} ({job.JobType}); make-good {entry.Id} opened (${entry.Amount:N0})");
        }
    }

    public Chapter3QaResult QaPayMakeGood(int id)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA pay refused: Chapter 3 is not active.");
            var paid = Chapter3MakeGoods.Pay(_ctx.State.Ledger, id);
            if (ReferenceEquals(paid.Ledger, _ctx.State.Ledger))
                return QaRefused($"Chapter 3 QA pay refused: make-good {id} is not open.");
            _ctx.State = _ctx.State with { Ledger = paid.Ledger, Revision = _ctx.State.Revision + 1 };
            foreach (var effect in paid.Effects)
                _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(effect));
            var entry = paid.Ledger.Entries.First(candidate => candidate.Id == id);
            return QaOk($"Chapter 3 QA: paid make-good {id} (${entry.Amount:N0})");
        }
    }

    public Chapter3QaResult QaRecordWarning(int id)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA warn refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA warn refused: Chapter 3 has not started.");
            var warned = Chapter3MakeGoods.RecordWarningDelivered(_ctx.State.Ledger, id, chapterDay.Value);
            if (ReferenceEquals(warned.Ledger, _ctx.State.Ledger))
                return QaRefused($"Chapter 3 QA warn refused: no warning is due for make-good {id}.");
            _ctx.State = _ctx.State with { Ledger = warned.Ledger, Revision = _ctx.State.Revision + 1 };
            return QaOk($"Chapter 3 QA: recorded Arthur warning delivered for make-good {id}");
        }
    }

    public Chapter3QaResult QaSimulateWeekPass()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA weekpass refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA weekpass refused: Chapter 3 has not started.");
            if (!_ctx.ReadOwnership())
                return QaRefused("Chapter 3 QA weekpass refused: the Fish Warehouse is not owned.");
            // QA simulates the next unrecorded week, starting from the clock-derived
            // week, so the owner run can take cut after cut without sleeping across a
            // week boundary; the real week pass keys strictly by elapsed days.
            var weekIndex = Chapter3FeatureContext.WeekIndexAt(_ctx.State.LastProcessedElapsedDay);
            while (_ctx.State.Cuts.Entries.Any(entry => entry.WeekIndex == weekIndex))
                weekIndex++;
            _payments.ProcessWeekPassLocked(weekIndex, chapterDay.Value);
            var cut = _ctx.State.Cuts.Entries.First(entry => entry.WeekIndex == weekIndex);
            return QaOk($"Chapter 3 QA: week {weekIndex} cut due (${cut.Amount:N0}, grace until day {cut.GraceUntilDay})");
        }
    }

    /// <summary>
    /// Stages the frozen delivery's bricks into the frozen drop's first slot
    /// and runs the delivery check, so the owner loop needs no grinding. A
    /// confirmed removal skips staging: those bricks are already taken. A
    /// package recovery is refused: its delivery needs real custody, which no
    /// console command can fake. An emergency supply is refused too: no
    /// command may create product, and staging is creating product.
    /// </summary>
    public Chapter3QaResult QaDeliverActiveJob()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA deliver refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA deliver refused: Chapter 3 has not started.");
            var job = _ctx.State.JobBoard.ActiveJob;
            if (job?.StorageRun is not null)
            {
                var storageOutcome = _ctx.Storage.Poll(chapterDay.Value);
                return QaOk($"Chapter 3 QA: storage delivery {storageOutcome}; collect, stow and deliver the real stack in the world.");
            }
            if (job is not null && job.JobType == Chapter3JobType.PackageRecovery)
                return QaRefused($"Chapter 3 QA deliver refused: job {job.JobId} is a package recovery; collect the staged bricks and deliver them in the world.");
            if (job is not null && job.JobType == Chapter3JobType.EmergencySupply)
                return QaRefused($"Chapter 3 QA deliver refused: job {job.JobId} is an emergency supply run; deliver the named bricks in the world.");
            if (job is null || job.Delivery is null)
                return QaRefused("Chapter 3 QA deliver refused: there is no frozen delivery.");
            var world = _ctx.DeadDropWorld;
            if (world is null)
                return QaRefused("Chapter 3 QA deliver refused: the dead-drop world is not wired.");
            var delivery = job.Delivery;
            string prefix;
            if (delivery.RemovalConfirmed)
            {
                prefix = "Chapter 3 QA: the removal is already confirmed; ";
            }
            else
            {
                Release1SmallCourtesyWorldMutationStatus staged;
                try
                {
                    staged = world.TryInsertPackagedProduct(
                        delivery.DropGuid, 0, delivery.ProductId, delivery.PackagingId,
                        delivery.BrickCount, out _);
                }
                catch (Exception exception)
                {
                    return QaRefused($"Chapter 3 QA deliver refused: staging faulted ({exception.GetType().Name}).");
                }
                prefix = staged == Release1SmallCourtesyWorldMutationStatus.Succeeded
                    ? $"Chapter 3 QA: staged {delivery.BrickCount} bricks of {delivery.ProductName} at {delivery.DropName}; "
                    : $"Chapter 3 QA: staging {delivery.BrickCount} bricks was {staged}; ";
            }
            var outcome = _delivery.CheckDeadDropDeliveryLocked(chapterDay.Value);
            return outcome == Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Completed
                ? QaOk(prefix + $"job {job.JobId} completed (${job.Reward:N0}).")
                : QaOk(prefix + "delivery holding.");
        }
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys; <c>oc_ch3 offer emergency</c>): forces the next
    /// offer to be Emergency Supply. With an open offer the force lands now,
    /// freezing the named product; with none it arms until the next tick
    /// creates one. A force that cannot name a product refuses and touches
    /// nothing.
    /// </summary>
    public Chapter3QaResult QaOfferEmergency()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA offer refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA offer refused: Chapter 3 has not started.");
            var board = _ctx.State.JobBoard;
            if (board.OpenOffer is null)
            {
                _ctx.ForceNextEmergencyOffer = true;
                return QaOk("Chapter 3 QA: the next offer will be Emergency Supply.");
            }
            if (board.OpenOffer.JobType == Chapter3JobType.EmergencySupply &&
                board.OpenOffer.ProductId is not null)
                return QaOk($"Chapter 3 QA: offer {board.OpenOffer.OfferId} is already Emergency Supply.");
            var forced = Chapter3JobScheduler.ForceEmergencyOffer(board);
            if (!_offers.TryFreezeOfferProductLocked(forced.OpenOffer!, out var frozen))
                return QaRefused($"Chapter 3 QA offer refused: no discovered product could be read for offer {board.OpenOffer.OfferId}.");
            _ctx.State = _ctx.State with { JobBoard = forced with { OpenOffer = frozen }, Revision = _ctx.State.Revision + 1 };
            _ctx.Log(Chapter3FeatureContext.FormatEffect(new Chapter3OfferCreatedEffect(frozen)));
            return QaOk($"Chapter 3 QA: offer {frozen.OfferId} is now Emergency Supply " +
                $"({Chapter3JobTuning.BricksForReward(frozen.Reward)} bricks of {frozen.ProductName}).");
        }
    }

    /// <summary>
    /// Whole minutes left on the active Emergency Supply window, for
    /// <c>oc_ch3 status</c>. Null when there is no windowed job or the clock
    /// is unreadable.
    /// </summary>
    public int? QaEmergencyMinutesLeft()
    {
        lock (_gate)
        {
            var job = _isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null
                ? null
                : _ctx.State.JobBoard.ActiveJob;
            if (job is null || job.JobType != Chapter3JobType.EmergencySupply || !job.ExpiresGameMinutes.HasValue)
                return null;
            if (!_ctx.TryReadCanonicalMinutesLocked(out var now))
                return null;
            return Math.Max(0, (int)Math.Floor(job.ExpiresGameMinutes.Value - now));
        }
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys; <c>oc_ch3 warehouse</c>): opens Nell's warehouse
    /// offer now, ignoring eligibility, so the owner doesn't need 18 days and
    /// 5 successes. Grants nothing: no money moves and the warehouse stays
    /// unowned.
    /// </summary>
    public Chapter3QaResult QaOfferWarehouse()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA warehouse refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA warehouse refused: Chapter 3 has not started.");
            var offer = _ctx.State.WarehouseOffer;
            if (offer is not null && offer.Purchased)
                return QaOk($"Chapter 3 QA: the warehouse was already purchased day {offer.PurchasedDay}.");
            if (offer is not null)
                return QaOk($"Chapter 3 QA: the warehouse offer is already open since day {offer.OpenedDay}.");
            if (_ctx.WarehouseAcquisition is not null && _ctx.WarehouseAcquisition.IsOwned())
                return QaRefused("Chapter 3 QA warehouse refused: the warehouse is already owned.");
            _ctx.State = _ctx.State with
            {
                WarehouseOffer = new Chapter3WarehouseOffer(chapterDay.Value),
                Revision = _ctx.State.Revision + 1
            };
            _ctx.Log($"Chapter 3 warehouse offer force-opened day {chapterDay.Value} by owner QA.");
            return QaOk($"Chapter 3 QA: the warehouse offer is now open since day {chapterDay.Value}.");
        }
    }

    public bool QaWarehouseOwned()
    {
        lock (_gate)
        {
            return _ctx.WarehouseAcquisition is not null && _ctx.WarehouseAcquisition.IsOwned();
        }
    }

    // Reports the tier, the tier capacity, the lifetime completed demands, the open offer,
    // and the Property's live capacity and idle-point count.
    public IReadOnlyList<string> QaCapacityStatus()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new[] { "Chapter 3 capacity: Chapter 3 is not active." };
            var tier = _ctx.State.WarehouseCapacityTier;
            var staff = Chapter3WarehouseCapacityTiers.CapacityForTier(tier);
            var offer = Chapter3WarehouseCapacityTiers.OpenOfferTier(
                _ctx.State.WarehouseOffer?.Purchased == true, tier, _ctx.State.DemandsCompleted);
            var offerText = offer.HasValue
                ? $"tier {offer.Value} ({Chapter3WarehouseCapacityTiers.CapacityForTier(offer.Value)} staff for ${Chapter3PaymentTuning.CapacityPriceForTier(offer.Value):N0})"
                : "none";
            var live = _ctx.WarehouseCapacity is not null &&
                _ctx.WarehouseCapacity.TryReadLive(out var liveCapacity, out var liveIdlePoints)
                ? $"live capacity={liveCapacity}, live idle points={liveIdlePoints}"
                : "live capacity unknown (warehouse not attached)";
            return new[]
            {
                $"Chapter 3 capacity: tier {tier} ({staff} staff), completed demands {_ctx.State.DemandsCompleted}, open offer {offerText}, {live}."
            };
        }
    }

    // Raises the completed count to the next tier's threshold, so the offer opens through the
    // normal derived path.
    public Chapter3QaResult QaCapacityUnlock()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA capacity refused: Chapter 3 is not active.");
            var tier = _ctx.State.WarehouseCapacityTier;
            if (tier >= Chapter3WarehouseCapacityTiers.MaxTier)
                return QaRefused("Chapter 3 QA capacity refused: already at the top tier.");
            var threshold = Chapter3WarehouseCapacityTiers.UnlockThresholdForTier(tier + 1);
            _ctx.State = _ctx.State with
            {
                DemandsCompleted = Math.Max(_ctx.State.DemandsCompleted, threshold),
                Revision = _ctx.State.Revision + 1
            };
            return QaOk($"Chapter 3 QA: completed demands now {_ctx.State.DemandsCompleted}; tier {tier + 1} opens through the normal path.");
        }
    }

    // Sets the tier with no charge and no text, and applies it live for the 20-employee test.
    public Chapter3QaResult QaCapacitySet(int tier)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA capacity refused: Chapter 3 is not active.");
            if (!Chapter3WarehouseCapacityTiers.IsValidTier(tier))
                return QaRefused($"Chapter 3 QA capacity refused: tier {tier} is not 1, 2, or 3.");
            _ctx.State = _ctx.State with { WarehouseCapacityTier = tier, Revision = _ctx.State.Revision + 1 };
            var staff = Chapter3WarehouseCapacityTiers.CapacityForTier(tier);
            var applied = _ctx.WarehouseCapacity is not null && _ctx.WarehouseCapacity.TryApplyCapacity(staff);
            return QaOk(applied
                ? $"Chapter 3 QA: capacity tier {tier} set and applied live ({staff} staff)."
                : $"Chapter 3 QA: capacity tier {tier} set; the warehouse is not attached, so it applies at attach.");
        }
    }

    // Moves the warehouse idle points into a block at the player and logs their positions.
    public Chapter3QaResult QaCapacityHere()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA capacity here refused: Chapter 3 is not active.");
            var capacity = _ctx.WarehouseCapacity;
            if (capacity is null || !capacity.IsAttached())
                return QaRefused("Chapter 3 QA capacity here refused: the warehouse is not attached.");
            var moved = capacity.TryMoveIdleGroupToPlayer(out var report);
            _ctx.Log(report);
            return moved ? QaOk(report) : QaRefused(report);
        }
    }

    // Reports the runtime player spawn points and whether the Property fields still point at
    // them.
    public IReadOnlyList<string> QaWarehouseSpawns()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new[] { "warehouse spawns: Chapter 3 is not active." };
            var capacity = _ctx.WarehouseCapacity;
            if (capacity is null || !capacity.IsAttached())
                return new[] { "warehouse spawns: the warehouse is not attached." };
            if (!capacity.TryReadPlayerSpawns(out var snapshot))
                return new[] { "warehouse spawns: the spawn points could not be read." };
            return new[]
            {
                $"warehouse spawns: exterior {DescribeSpawnPoint(snapshot.ExteriorX, snapshot.ExteriorY, snapshot.ExteriorZ, snapshot.ExteriorAssigned, snapshot.ExteriorRetained)}, " +
                $"interior {DescribeSpawnPoint(snapshot.InteriorX, snapshot.InteriorY, snapshot.InteriorZ, snapshot.InteriorAssigned, snapshot.InteriorRetained)}"
            };
        }
    }

    // Moves one player spawn point to the local player for the session, like
    // `oc_ch3 capacity here`.
    public Chapter3QaResult QaWarehouseSpawnHere(bool exterior)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA spawn here refused: Chapter 3 is not active.");
            var capacity = _ctx.WarehouseCapacity;
            if (capacity is null || !capacity.IsAttached())
                return QaRefused("Chapter 3 QA spawn here refused: the warehouse is not attached.");
            var moved = capacity.TryMovePlayerSpawnToPlayer(exterior, out var report);
            _ctx.Log(report);
            return moved ? QaOk(report) : QaRefused(report);
        }
    }

    // Drives the Chief's protection QA through the wired service; without it every subcommand
    // refuses. A read-only signal the other way too: these delegate, never duplicate, the
    // Chief's own logic.
    public Chapter3QaResult QaProtectionOffer()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA protection offer refused: Chapter 3 is not active.");
            if (_ctx.ChiefProtectionQa is null)
                return QaRefused("Chapter 3 QA protection offer refused: the Chief service is not wired.");
            try { return _ctx.ChiefProtectionQa.QaProtectionOffer(); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA protection offer refused: the Chief service threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaProtectionLapse()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA protection lapse refused: Chapter 3 is not active.");
            if (_ctx.ChiefProtectionQa is null)
                return QaRefused("Chapter 3 QA protection lapse refused: the Chief service is not wired.");
            try { return _ctx.ChiefProtectionQa.QaProtectionLapse(); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA protection lapse refused: the Chief service threw ({ex.Message})."); }
        }
    }

    public Release1ChiefProtectionSnapshot? QaProtectionSnapshot()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.ChiefProtectionQa is null) return null;
            try { return _ctx.ChiefProtectionQa.QaProtectionSnapshot(); }
            catch { return null; }
        }
    }

    // Drives the shared officer roster through the wired service; without it every subcommand
    // refuses.
    public IReadOnlyList<string> QaOfficersStatus()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return new[] { "officers: Chapter 3 is not active." };
            if (_ctx.OfficerRoster is null)
                return new[] { "officers: the roster is not wired." };
            try { return _ctx.OfficerRoster.QaStatus(); }
            catch (Exception ex) { return new[] { $"officers: the roster threw ({ex.Message})." }; }
        }
    }

    public Chapter3QaResult QaOfficersSpawn(int count)
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA officers spawn refused: Chapter 3 is not active.");
            if (_ctx.OfficerRoster is null)
                return QaRefused("Chapter 3 QA officers spawn refused: the roster is not wired.");
            try { return _ctx.OfficerRoster.QaSpawn(count); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA officers spawn refused: the roster threw ({ex.Message})."); }
        }
    }

    public Chapter3QaResult QaOfficersClear()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA officers clear refused: Chapter 3 is not active.");
            if (_ctx.OfficerRoster is null)
                return QaRefused("Chapter 3 QA officers clear refused: the roster is not wired.");
            try { return _ctx.OfficerRoster.QaClear(); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA officers clear refused: the roster threw ({ex.Message})."); }
        }
    }

    // Drives the Chief's forced-Wanted QA through the wired service; without it
    // every subcommand refuses.
    public Chapter3QaResult QaLockdownAge(double hours)
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA lockdown age refused: Chapter 3 is not active.");
            if (_ctx.ChiefForcedWantedQa is null)
                return QaRefused("Chapter 3 QA lockdown age refused: the Chief service is not wired.");
            try { return _ctx.ChiefForcedWantedQa.QaLockdownAge(hours); }
            catch (Exception ex) { return QaRefused($"Chapter 3 QA lockdown age refused: the Chief service threw ({ex.Message})."); }
        }
    }

    public Release1ChiefForcedWantedSnapshot? QaForcedWantedSnapshot()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.ChiefForcedWantedQa is null) return null;
            try { return _ctx.ChiefForcedWantedQa.QaForcedWantedSnapshot(); }
            catch { return null; }
        }
    }

    // Shows and hides a sample staff-return banner through the live sender.
    public Chapter3QaResult QaNoticeShow()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA notice refused: Chapter 3 is not active.");
            var sender = _ctx.StaffReturnBannerSender;
            if (sender is null)
                return QaRefused("Chapter 3 QA notice refused: the banner sender is not wired.");
            bool shown;
            try
            {
                shown = sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(5, 20));
            }
            catch (Exception ex)
            {
                return QaRefused($"Chapter 3 QA notice refused: the banner sender threw ({ex.Message}).");
            }
            if (!shown)
                return QaRefused("Chapter 3 QA notice refused: the banner sender was unavailable.");
            return QaOk("Chapter 3 QA: sample staff-return banner shown (5/20).");
        }
    }

    public Chapter3QaResult QaNoticeHide()
    {
        lock (_gate)
        {
            if (_isDisposed())
                return QaRefused("Chapter 3 QA notice refused: Chapter 3 is not active.");
            var sender = _ctx.StaffReturnBannerSender;
            if (sender is null)
                return QaRefused("Chapter 3 QA notice refused: the banner sender is not wired.");
            bool hidden;
            try
            {
                hidden = sender.TryHideOwn();
            }
            catch (Exception ex)
            {
                return QaRefused($"Chapter 3 QA notice refused: the banner sender threw ({ex.Message}).");
            }
            if (!hidden)
                return QaRefused("Chapter 3 QA notice refused: the banner sender was unavailable.");
            return QaOk("Chapter 3 QA: staff-return banner hidden.");
        }
    }

    public Chapter3QaResult QaPayCut(int weekIndex)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA paycut refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA paycut refused: Chapter 3 has not started.");
            var paid = Chapter3WeeklyCutTransitions.Pay(_ctx.State.Cuts, weekIndex, chapterDay.Value);
            if (ReferenceEquals(paid.Cuts, _ctx.State.Cuts))
                return QaRefused($"Chapter 3 QA paycut refused: week {weekIndex} cut is not due.");
            _ctx.State = _ctx.State with { Cuts = paid.Cuts, Revision = _ctx.State.Revision + 1 };
            foreach (var effect in paid.Effects)
                _ctx.Log(Chapter3FeatureContext.FormatCutEffect(effect));
            var cut = paid.Cuts.Entries.First(entry => entry.WeekIndex == weekIndex);
            return QaOk($"Chapter 3 QA: paid week {weekIndex} cut (${cut.Amount:N0})");
        }
    }

    /// <summary>
    /// Forces this week's demand now, skipping the cut-anchored timing but nothing else.
    /// </summary>
    public Chapter3QaResult QaDemandNow()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 is not active.");
            if (!_ctx.Clock.TryReadElapsedDays(out var elapsedDays))
                return QaRefused("Chapter 3 QA demand refused: the host clock was unreadable.");
            var chapterDay = _ctx.State.ChapterDayAt(elapsedDays);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 has not started.");
            if (!_demand.TryIssueDemandLocked(Chapter3FeatureContext.WeekIndexAt(elapsedDays), chapterDay.Value, anchorCut: null, quiet: true, out var message))
                return QaRefused(message);
            return QaOk(message);
        }
    }

    public IReadOnlyList<string> QaDemandEligible()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new[] { "Chapter 3 demand eligible: Chapter 3 is not active." };
            if (_ctx.State.MadeProductIds.Count == 0)
                return new[] { "Chapter 3 demand eligible: nothing has been made in the warehouse yet." };
            return _ctx.State.MadeProductIds
                .Select(id => $"Chapter 3 demand eligible: {id}")
                .ToArray();
        }
    }

    public IReadOnlyList<string> QaDemandFresh()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new[] { "Chapter 3 demand fresh: Chapter 3 is not active." };
            var demand = _ctx.State.ActiveDemand;
            if (demand is null)
                return new[] { "Chapter 3 demand fresh: no open demand." };
            var cap = Math.Min(demand.Quantity - demand.Delivered, Math.Max(0, demand.FreshCount - demand.Delivered));
            return new[]
            {
                $"Chapter 3 demand {demand.DemandId} fresh: made {demand.FreshCount}, handed {demand.Delivered} of {demand.Quantity}, take cap {cap}."
            };
        }
    }

    // Seeds fresh units onto the open demand without running stations.
    public Chapter3QaResult QaDemandFreshAdd(int units)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 is not active.");
            if (units <= 0)
                return QaRefused("Chapter 3 QA demand refused: units must be positive.");
            var demand = _ctx.State.ActiveDemand;
            if (demand is null)
                return QaRefused("Chapter 3 QA demand refused: no open demand.");
            var fresh = demand with { FreshCount = demand.FreshCount + units };
            _ctx.State = _ctx.State with { ActiveDemand = fresh, Revision = _ctx.State.Revision + 1 };
            return QaOk($"Chapter 3 QA: demand {fresh.DemandId} fresh +{units} (made {fresh.FreshCount}, handed {fresh.Delivered} of {fresh.Quantity}).");
        }
    }

    // Adds one product id to the demand eligibility set.
    public Chapter3QaResult QaDemandEligibleAdd(string productId)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 is not active.");
            if (string.IsNullOrWhiteSpace(productId))
                return QaRefused("Chapter 3 QA demand refused: a product id is required.");
            if (_ctx.State.MadeProductIds.Contains(productId))
                return QaOk($"Chapter 3 QA: {productId} is already demand eligible.");
            _ctx.State = _ctx.State with { MadeProductIds = _ctx.State.MadeProductIds.Append(productId).ToArray(), Revision = _ctx.State.Revision + 1 };
            return QaOk($"Chapter 3 QA: {productId} is now demand eligible.");
        }
    }

    // Runs the tick's own close evaluation: completes a filled demand, misses a past-due one,
    // and otherwise reports it still open.
    public Chapter3QaResult QaDemandExpire()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 is not active.");
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return QaRefused("Chapter 3 QA demand refused: Chapter 3 has not started.");
            var before = _ctx.State.ActiveDemand;
            if (before is null)
                return QaRefused("Chapter 3 QA demand refused: no open demand.");
            _demand.EvaluateDemandLocked(chapterDay.Value, forceDeadline: true);
            var outcome = _ctx.State.LastDemandOutcome;
            if (_ctx.State.ActiveDemand is null && outcome is not null && outcome.DemandId == before.DemandId)
                return outcome.Completed
                    ? QaOk($"Chapter 3 QA: demand {before.DemandId} completed ({before.Quantity} {before.ProductName} handed over).")
                    : QaOk($"Chapter 3 QA: demand {before.DemandId} missed ({outcome.LeftUnits} short, ${outcome.Amount:N0} make-good).");
            return QaOk($"Chapter 3 QA: demand {before.DemandId} still open ({before.Delivered}/{before.Quantity} handed, due day {before.DueDay}).");
        }
    }

    // The oc_ch3 console command prints the returned message, so these don't log it again.
    private static Chapter3QaResult QaOk(string message) => new(true, message);

    // Deliberately not phase-gated: the proof visit must answer even before Chapter 3 starts,
    // and parking at load is unconditional.
    public Chapter3QaResult QaArthurCome()
    {
        lock (_gate)
            return Chapter3ArthurHarness.TryCome(_ctx.ArthurWorld, _ctx.ArthurPump);
    }

    public Chapter3QaResult QaArthurSay(string text)
    {
        lock (_gate)
            return Chapter3ArthurHarness.TrySay(_ctx.ArthurWorld, _ctx.ArthurPump, text);
    }

    public Chapter3QaResult QaArthurLeave()
    {
        lock (_gate)
            return Chapter3ArthurHarness.TryLeave(_ctx.ArthurWorld, _ctx.ArthurPump);
    }

    public IReadOnlyList<string> QaArthurStatus()
    {
        lock (_gate)
        {
            var lines = Chapter3ArthurHarness.TryStatus(_ctx.ArthurWorld, _ctx.ArthurPump).ToList();
            var beating = _ctx.Beating;
            lines.Add(beating is null
                ? "beating: none"
                : $"beating: {beating.Kind.ToString().ToLowerInvariant()} running");
            if (_ctx.State is not null)
            {
                var desired = Chapter3ArthurVisitScheduler.DesiredPending(
                    _ctx.State.Ledger, _ctx.State.JobBoard.CooperationVisitIssued && !_ctx.State.ArthurCooperationDelivered);
                var widened = Chapter3ArthurVisitScheduler.IsWidened(desired, _ctx.State.ArthurVisitMissedEvenings);
                lines.Add($"visit pending: {desired.ToString().ToLowerInvariant()} (missed evenings: {_ctx.State.ArthurVisitMissedEvenings}, widened: {(widened ? "yes" : "no")})");
                var days = _ctx.State.Ledger.Entries
                    .Where(entry => entry.BeatingDeliveredDay.HasValue)
                    .Select(entry => entry.BeatingDeliveredDay!.Value)
                    .OrderBy(day => day)
                    .ToList();
                lines.Add(days.Count == 0 ? "beating days: none" : $"beating days: {string.Join(", ", days)}");
                lines.Add($"beatings delivered: {_ctx.State.ArthurBeatings}");
            }
            return lines;
        }
    }

    public Chapter3QaResult QaArthurHq(string mode)
    {
        lock (_gate)
            return Chapter3ArthurHarness.TryHq(_ctx.ArthurWorld, _ctx.ArthurPump, mode);
    }

    public Chapter3QaResult QaArthurHqHere()
    {
        lock (_gate)
            return Chapter3ArthurHarness.TryHqHere(_ctx.ArthurWorld, _ctx.ArthurPump);
    }

    // Host only, needs Chapter 3 Active, and changes no Chapter 3 state and no persistence:
    // the snapshot only reads.
    public IReadOnlyList<string> QaBeatSnapshot()
    {
        lock (_gate)
        {
            if (!BeatProbeReady(out var world, out var refusal))
                return new[] { refusal };
            if (!world.TryReadSnapshot(out var snapshot, out var reason))
                return new[] { $"Chapter 3 QA beat probe snapshot refused: {reason}" };
            return Chapter3BeatProbe.DescribeSnapshot(snapshot);
        }
    }

    // Toggles the S1API invincibility seam; off restores exactly what was there before on,
    // and a reload or quit always clears it.
    public IReadOnlyList<string> QaBeatInvincible(bool on)
    {
        lock (_gate)
        {
            if (!BeatProbeReady(out _, out var refusal))
                return new[] { refusal };
            var probe = _ctx.BeatInvincible;
            if (probe is null)
                return new[] { "Chapter 3 QA beat probe invincible refused: the probe world is not wired up." };
            return on ? probe.TurnOn() : probe.TurnOff();
        }
    }

    // With no trigger, lists the known attack triggers; otherwise Arthur, out on a visit,
    // plays that trigger at the player.
    public IReadOnlyList<string> QaBeatAnim(string? trigger)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new[] { "Chapter 3 QA beat probe anim refused: Chapter 3 is not active." };
            if (string.IsNullOrWhiteSpace(trigger))
                return new[] { "Chapter 3 QA beat probe anim triggers: " + string.Join(", ", Chapter3BeatProbe.KnownTriggers) };
            var world = _ctx.ArthurWorld;
            if (world is null)
                return new[] { "Chapter 3 QA beat probe anim refused: Arthur is not wired up." };
            if (!world.HasAuthority)
                return new[] { "Chapter 3 QA beat probe anim refused: host only." };
            if (_ctx.ArthurPump.State is not (Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived))
                return new[] { $"Chapter 3 QA beat probe anim refused: Arthur is {_ctx.ArthurPump.State}, not out on a visit." };
            if (world.TryPlayAnimationTrigger(trigger, out var reason) != Chapter3ArthurMutationStatus.Succeeded)
                return new[] { $"Chapter 3 QA beat probe anim refused: {reason}" };
            return new[] { $"Chapter 3 QA beat probe anim: {reason}" };
        }
    }

    // Starts that visit now, ignoring the time window but still checking the on-foot,
    // outdoors and not-in-pursuit conditions, or forces the phone fallback for whatever is
    // pending. Unlike the pump primitives, engine visits need an active Chapter 3.
    public Chapter3QaResult QaArthurVisit(string kind)
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA arthur visit refused: Chapter 3 is not active.");
            if (_ctx.ArthurWorld is null)
                return QaRefused("Chapter 3 QA arthur visit refused: Arthur is not wired up.");
            if (!_ctx.ArthurWorld.HasAuthority)
                return QaRefused("Chapter 3 QA arthur visit refused: host only.");
            var mode = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (mode == "call")
                return QaArthurVisitCallLocked();
            var visitKind = mode switch
            {
                "warn" or "warning" => Chapter3ArthurVisitKind.Warning,
                "coop" or "cooperation" => Chapter3ArthurVisitKind.Cooperation,
                "beat" or "beating" => Chapter3ArthurVisitKind.Beating,
                _ => Chapter3ArthurVisitKind.None
            };
            if (visitKind == Chapter3ArthurVisitKind.None)
                return QaRefused("Usage: oc_ch3 arthur visit <warn | coop | beat | call>");
            if (visitKind == Chapter3ArthurVisitKind.Warning && !_arthur.HasWarningDueLocked())
                return QaRefused("Chapter 3 QA arthur visit refused: no warning visit is pending.");
            if (visitKind == Chapter3ArthurVisitKind.Beating && !_arthur.HasBeatingDueLocked())
                return QaRefused("Chapter 3 QA arthur visit refused: no beating visit is pending.");
            if (visitKind == Chapter3ArthurVisitKind.Cooperation &&
                !(_ctx.State.JobBoard.CooperationVisitIssued && !_ctx.State.ArthurCooperationDelivered))
                return QaRefused("Chapter 3 QA arthur visit refused: no cooperation visit is pending.");
            if (_ctx.VisitProgress is not null)
                return QaRefused("Chapter 3 QA arthur visit refused: a visit is already running.");
            if (_ctx.ArthurPump.State != Chapter3ArthurVisitState.Parked && _ctx.ArthurPump.State != Chapter3ArthurVisitState.AtHq)
                return QaRefused($"Chapter 3 QA arthur visit refused: Arthur is {_ctx.ArthurPump.State}, not parked.");
            if (!_arthur.TryReadVisitConditionsLocked(out var detail))
                return QaRefused($"Chapter 3 QA arthur visit refused: {detail}");
            if (!_arthur.TryReadVisitDayLocked(out var chapterDay, out _))
                return QaRefused("Chapter 3 QA arthur visit refused: the game clock was unreadable.");
            if (!_arthur.StartVisitLocked(visitKind, chapterDay, out var reason))
                return QaRefused($"Chapter 3 QA arthur visit refused: {reason}");
            var missed = _ctx.State.ArthurVisitPending == visitKind ? _ctx.State.ArthurVisitMissedEvenings : 0;
            _arthur.SetVisitSeriesLocked(visitKind, missed, chapterDay);
            return QaOk($"Chapter 3 QA arthur visit: forced the {Chapter3FeatureContext.DescribeVisitKind(visitKind)} visit.");
        }
    }

    // Starts the attack beating on the spot: arrived-gated, needs no visit and no debts, and
    // speaks the attack line.
    public Chapter3QaResult QaArthurBeat()
    {
        lock (_gate)
        {
            if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return QaRefused("Chapter 3 QA arthur beat refused: Chapter 3 is not active.");
            if (_ctx.ArthurWorld is null || _ctx.BeatingWorld is null || _ctx.BeatingHq is null)
                return QaRefused("Chapter 3 QA arthur beat refused: the beating is not wired up.");
            if (!_ctx.ArthurWorld.HasAuthority)
                return QaRefused("Chapter 3 QA arthur beat refused: host only.");
            if (_ctx.Beating is not null)
                return QaRefused("Chapter 3 QA arthur beat refused: a beating is already running.");
            if (_ctx.ArthurPump.State != Chapter3ArthurVisitState.Arrived)
                return QaRefused($"Chapter 3 QA arthur beat refused: Arthur is {_ctx.ArthurPump.State}, not arrived.");
            if (!_arthur.TryReadVisitDayLocked(out var chapterDay, out _))
                return QaRefused("Chapter 3 QA arthur beat refused: the game clock was unreadable.");
            _arthur.BeginBeatingLocked(Chapter3BeatingKind.Attack, chapterDay, parkArthur: true);
            if (_ctx.Beating is null)
                return QaRefused("Chapter 3 QA arthur beat refused: the beating did not start; see the log.");
            return QaOk("Chapter 3 QA arthur beat: the attack beating starts.");
        }
    }

    private Chapter3QaResult QaArthurVisitCallLocked()
    {
        var desired = Chapter3ArthurVisitScheduler.DesiredPending(
            _ctx.State!.Ledger, _ctx.State.JobBoard.CooperationVisitIssued && !_ctx.State.ArthurCooperationDelivered);
        if (desired == Chapter3ArthurVisitKind.None)
            return QaRefused("Chapter 3 QA arthur visit refused: no visit is pending.");
        if (desired == Chapter3ArthurVisitKind.Beating)
            return QaRefused("Chapter 3 QA arthur visit refused: beatings never phone.");
        if (_ctx.ArthurPhone is null)
            return QaRefused("Chapter 3 QA arthur visit refused: the phone is not wired up.");
        if (!_arthur.TryReadVisitDayLocked(out var chapterDay, out _))
            return QaRefused("Chapter 3 QA arthur visit refused: the game clock was unreadable.");
        if (!_arthur.PhoneFallbackLocked(desired, chapterDay))
            return QaRefused($"Chapter 3 QA arthur visit refused: the {Chapter3FeatureContext.DescribeVisitKind(desired)} call failed.");
        return QaOk($"Chapter 3 QA arthur visit: forced the {Chapter3FeatureContext.DescribeVisitKind(desired)} call.");
    }

    private bool BeatProbeReady(out IChapter3BeatProbeWorld world, out string refusal)
    {
        world = _ctx.BeatProbeWorld!;
        if (_isDisposed() || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
        {
            refusal = "Chapter 3 QA beat probe refused: Chapter 3 is not active.";
            return false;
        }
        if (world is null)
        {
            refusal = "Chapter 3 QA beat probe refused: the probe world is not wired up.";
            return false;
        }
        if (!world.IsHost)
        {
            refusal = "Chapter 3 QA beat probe refused: host only.";
            return false;
        }
        refusal = string.Empty;
        return true;
    }

    private static string DescribeSpawnPoint(float x, float y, float z, bool assigned, bool retained) =>
        !assigned ? "unassigned" : $"({x:F2}, {y:F2}, {z:F2}) retained {(retained ? "yes" : "no")}";

    private static Chapter3QaResult QaRefused(string message) => new(false, message);
}
