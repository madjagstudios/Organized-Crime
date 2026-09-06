using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73's convergence pass for Chief Campbell. Reads Foundation B's Local Pressure ledger, the
/// story's own Chief record, and his queued tier events, and moves the one record forward: adopt,
/// open a demand, decline, pay (prepare, debit, read back, mark applied), settle (wipe, then commit
/// once a save has persisted it), and the ladder lines. The lockdown engage and lift path on the
/// proven curfew seam is <see cref="ReconcileLockdown"/> (engage and the cooled lift) plus the paid
/// lift branch inside <see cref="ContinuePayment"/> (the payment settling is itself the other lift
/// cause), added in OC-73 Task 4.
///
/// OC-73 settled drain fix. Committing a settled payoff (<see cref="CommitAppliedChiefCashEffects"/>)
/// no longer gates the rest of the pass: a Settled record used to hold DrainOne and ReconcileLockdown
/// hostage for the entire window between the settle and the next save, so an arrest or a tier
/// crossing observed in that window queued silently instead of ever being acted on.
/// </summary>
public sealed class Release1ChiefService : IDisposable
{
    // A cash-balance read is float precision while the demand ladder is whole dollars; this bounds
    // the float round-trip error, far below one cent, mirroring Release1TheEnvelopeMissionService's
    // own CashReadTolerance.
    private const double CashReadTolerance = 0.01d;

    // OC-73 settled drain fix. Mirrors Release1ChiefEffect.EffectId's own prefix (see
    // Release1ChiefLedger.cs) so CommitAppliedChiefCashEffects can find a Chief cash effect straight
    // in the native effect journal, without going through the record's own NativeEffectIds, which a
    // later ArrestObserved reset can clear before the commit ever runs.
    private const string ChiefCashEffectIdPrefix = "chief-campbell-cash-v1-";

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1ChiefTierObserver _observer;
    private readonly Release1ChiefLedgerReader _readLedger;
    private readonly Release1ChiefLedgerWiper _wipeLedger;
    private readonly Release1ChiefEpochReader _epochs;
    private readonly LocalPressureProfile _profile;
    private readonly Release1ConvergenceThrottle _throttle = new();
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    // OC-73 review fix (finding 2). In-memory only, cleared on every load boundary like
    // _reportedHolds: _confirmedDebits remembers which prepared effect ids this session's own RunPay
    // call confirmed as a Succeeded debit, so a later pass's ContinuePayment can tell "retry the
    // confirm/apply tail" (RetryUnconfirmedPayment) apart from "the debit never returned Succeeded and
    // the blocking write itself was refused" (RetryUnrecordedBlock) instead of assuming every Prepared,
    // non-blocked effect on a Paying record was already a successful debit awaiting confirmation.
    // _wipedEffectIds remembers which effect id's wipe already succeeded, so a settle write that keeps
    // getting rejected does not re-run the Local Pressure wipe (and its revision bump) every pass.
    private readonly HashSet<string> _confirmedDebits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wipedEffectIds = new(StringComparer.Ordinal);
    private bool _disposed;

    public Release1ChiefService(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world,
        Release1ChiefTierObserver observer,
        Release1ChiefLedgerReader readLedger,
        Release1ChiefLedgerWiper wipeLedger,
        Release1ChiefEpochReader epochs,
        LocalPressureProfile profile)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _readLedger = readLedger ?? throw new ArgumentNullException(nameof(readLedger));
        _wipeLedger = wipeLedger ?? throw new ArgumentNullException(nameof(wipeLedger));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public void OnPreLoad() { if (!_disposed) { _throttle.Clear(); _reportedHolds.Clear(); _confirmedDebits.Clear(); _wipedEffectIds.Clear(); } }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        _throttle.Clear();
        _reportedHolds.Clear();
        _confirmedDebits.Clear();
        _wipedEffectIds.Clear();
        // OC-73 review fix (finding 5). DroppedCount's own class doc already claimed the convergence
        // pass logs it once per load when non-zero; nothing ever did. Logged here, the same load
        // boundary the doc names, only when there is something to report.
        if (_observer.DroppedCount > 0)
            OrganizedCrimeLog.Warning($"Chief Campbell's tier event queue dropped {_observer.DroppedCount} event(s) since load; the queue is bounded and the oldest were discarded.");
    }

    public void OnSaveStart() { if (!_disposed) _throttle.Clear(); }
    public void OnSaveComplete() { if (!_disposed) _throttle.Clear(); }

    /// <summary>
    /// The once per game minute convergence pass (OC-63), in order: adopt on a record free save and
    /// send nothing; continue an in flight payment (debit already applied, wipe, or the post save
    /// commit); otherwise drain at most one queued tier event; then reconcile the lockdown (Task 4).
    /// A clock that cannot be read fails open, same as every other mission service's own gate.
    /// </summary>
    public void Update()
    {
        if (_disposed) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        if (_story.Phase != Release1StoryRuntimePhase.Active) return;
        var state = _story.State;
        if (state is null) return;

        var minutes = _world.TryReadCanonicalTotalMinutes(out var read) == Release1SmallCourtesyWorldReadStatus.Ready ? (double?)read : null;
        if (!_throttle.TryBegin(minutes, state.Revision)) return;

        if (!_epochs(out var sessionEpoch, out var loadEpoch)) return;
        var ledger = _readLedger(context.PlayerId);
        if (ledger is null) return;

        var record = state.ChiefRecord;
        if (record is null) { Adopt(ledger); return; }

        if (ContinuePayment(record, context.PlayerId, ledger)) return;
        CommitAppliedChiefCashEffects();
        DrainOne(record, context.PlayerId, sessionEpoch, loadEpoch);
        ReconcileLockdown(_story.State?.ChiefRecord, ledger);
    }

    /// <summary>
    /// The player picked Pay. Clears the throttle stamp and runs one immediate pass: short cash holds
    /// with a shortfall line sent once per distinct amount; sufficient cash prepares the one
    /// CashTransfer, debits the wallet, reads the post balance back, and marks the effect Applied. The
    /// wipe and the settle are a separate pass, on the tick after Applied (spec decision 16).
    /// </summary>
    public bool TryPay()
    {
        if (_disposed) return false;
        _throttle.Clear();
        return RunPay();
    }

    /// <summary>The player picked the decline option. Clears the throttle stamp and runs one immediate pass.</summary>
    public bool TryDecline()
    {
        if (_disposed) return false;
        _throttle.Clear();
        if (!_story.TryGetActiveContext(out _, out _)) return false;
        var record = _story.State?.ChiefRecord;
        if (record is null) return false;
        var decision = Release1ChiefLedger.Decline(record);
        if (decision.Action != Release1ChiefAction.ReplyDeclined) return false;
        return _story.TrySetChiefRecord(decision.Next).Accepted;
    }

    public void Dispose() => _disposed = true;

    private bool RunPay()
    {
        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var state = _story.State;
        var record = state?.ChiefRecord;
        if (record is null || record.State != Release1ChiefState.DemandOpen) return false;

        if (_world.TryReadCashBalance(out var balance) != Release1SmallCourtesyWorldReadStatus.Ready) return false;

        var demand = record.Demand;
        if (balance < demand)
        {
            var shortfall = (double)demand - balance;
            if (record.LastShortfallNoticed != shortfall)
            {
                var shortfallResult = _story.TrySetChiefRecord(record with { LastShortfallNoticed = shortfall, Revision = record.Revision + 1 });
                if (!shortfallResult.Accepted)
                    OrganizedCrimeLog.Warning($"Chief shortfall notice for round {record.DemandRound} was rejected: {shortfallResult.RejectReason}: {shortfallResult.Message}");
            }
            return false;
        }

        var round = record.DemandRound;
        // OC-73 review fix. record.Revision never resets (unlike round, which restarts at 1 after
        // every Settled-plus-arrest cycle), so folding it into the receipt and effect ids keeps both
        // unique for the life of the save. See Release1ChiefEffect.EffectId.
        var receiptId = $"chief-pay-r{round}-c{record.Revision}";
        var correlationId = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.ChiefCampbell, round, Release1TransitionKind.ChiefPaymentAccepted, receiptId).Value;
        var effectId = Release1ChiefEffect.EffectId(round, record.Revision);
        var prepared = new Release1NativeEffectJournalEntry(
            effectId, Release1MissionCatalog.ChiefCampbell, round, Release1ChiefEffect.Kind,
            Release1ChiefEffect.WalletSource, Release1ChiefEffect.Scope, Release1ChiefCampbellCopy.Dollars(demand),
            Release1NativeEffectPhase.Prepared, null, state!.Revision + 1,
            AuthorizedStoryCorrelationId: correlationId, AuthorizedMissionRevision: record.Revision);

        var prepareResult = _story.TryPrepareNativeEffect(prepared);
        if (!prepareResult.Accepted) return false;

        // OC-73 review fix. The record is now durably Paying with its effect Prepared (the atomic
        // swap PrepareChiefEffect performs). TryDebitCashBalance's own contract mutates the wallet
        // only on Succeeded, but only Unavailable is known, synchronously, to mean the native call was
        // never even attempted (its own pre balance read failed): that is the one failure this method
        // can still safely undo by handing the demand straight back for a fresh Pay press. Ambiguous
        // means exactly the opposite: S1ApiRelease1SmallCourtesyWorld.TryDebitCashBalance returns it
        // only from the catch wrapping the native mutation call itself, so the money may already be
        // gone. Rejected and an exception here are treated the same as Ambiguous rather than as the
        // "definitely nothing moved" case the Rejected guards would suggest in isolation, matching the
        // convention Release1SmallCourtesyMissionService.ApplyPreparedReward already ships (Unavailable
        // alone retries; every other outcome blocks). Blocking calls TryRecordNativeEffectIssuance with
        // Ambiguous on the just-prepared effect *before* ever reopening, while the record is still
        // Paying and the effect's authorization is still current: this both stops a second Pay press
        // from ever debiting the wallet twice for this demand, and (since it applies identically to the
        // Unavailable path's own now-orphaned effect) leaves nothing but a permanently blocked effect
        // behind, so the widened closedCycle relaxation in Release1StoryState.Validate's Chief branch
        // covers it even after a later Settled-plus-arrest reset clears the record's own linkage.
        Release1SmallCourtesyWorldMutationStatus debitStatus;
        try { debitStatus = _world.TryDebitCashBalance(-(float)demand); }
        catch { debitStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
        if (debitStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
        {
            OrganizedCrimeLog.Warning($"Chief payment debit for round {round} did not succeed ({debitStatus}); blocking the effect, no cash confirmed moved.");
            // OC-73 review fix (finding 2). The block used to be a bare TryRecordNativeEffectIssuance
            // call with no further consequence if refused; BlockEffect both retries the same idea on a
            // later pass (see RetryUnrecordedBlock) and, the first time the effect is durably blocked,
            // sends the one Chief message telling the player the demand still stands. Unavailable is
            // excluded from that notice: it reopens the demand immediately below, so the prompt itself
            // already shows the demand still open and a second "it stands" line would be redundant.
            BlockEffect(effectId, "initial", notifyPlayer: debitStatus != Release1SmallCourtesyWorldMutationStatus.Unavailable);

            if (debitStatus == Release1SmallCourtesyWorldMutationStatus.Unavailable)
            {
                var payingRecord = _story.State?.ChiefRecord;
                if (payingRecord is not null)
                {
                    var reopened = Release1ChiefLedger.AbandonUnappliedPayment(payingRecord);
                    var reopenResult = _story.TrySetChiefRecord(reopened.Next);
                    if (!reopenResult.Accepted)
                        OrganizedCrimeLog.Warning($"Chief demand reopen after an unavailable debit was itself rejected: {reopenResult.RejectReason}: {reopenResult.Message}");
                }
            }
            return false;
        }

        // OC-73 review fix (finding 2). Marked the moment the debit itself is known Succeeded, before
        // the read-back below (which can still fail on its own and defer to RetryUnconfirmedPayment on
        // a later pass): this is the one authoritative signal that this exact effect id may later be
        // marked Applied by a retry, never a debit that RetryUnrecordedBlock is still trying to block.
        _confirmedDebits.Add(effectId);

        if (_world.TryReadCashBalance(out var post) != Release1SmallCourtesyWorldReadStatus.Ready ||
            Math.Abs(post - (balance - demand)) > CashReadTolerance)
        {
            OrganizedCrimeLog.Warning($"Chief payment read-back for round {round} did not confirm the debit; the payment will retry to complete.");
            return false;
        }

        var markResult = _story.TryMarkNativeEffectApplied(
            effectId, Release1ChiefEffect.NativeReceiptId(round, record.Revision), Release1NativeEffectPersistenceMode.RevertTolerant);
        if (!markResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief payment for round {round} could not be marked applied: {markResult.RejectReason}: {markResult.Message}; the payment will retry to complete.");
        return markResult.Accepted;
    }

    /// <summary>
    /// The pass for a record still Paying. A Paying record whose effect is still Prepared (RunPay's
    /// debit already succeeded, but the read-back or the MarkApplied call that follows it was
    /// interrupted) is handed to <see cref="RetryUnconfirmedPayment"/> rather than ever repeating the
    /// debit. Paying with the debit Applied tries the wipe (spec decision 16: never before Applied,
    /// never in the same call as the debit); a refused wipe holds and retries, and the debit is never
    /// repeated because the effect is already Applied. Every branch here returns true: a payment is
    /// genuinely in flight and the rest of the pass (DrainOne, ReconcileLockdown) must wait for it.
    ///
    /// OC-73 settled drain fix. This method used to also own the Settled branch: it waited here for
    /// the save that persists a settled payoff, committed it against the revision captured at the
    /// start of the pass, and returned true either way, which made a Settled record short circuit the
    /// whole convergence pass (skipping DrainOne and ReconcileLockdown) for the entire window between
    /// the settle and the next save. An arrest or a tier crossing observed in that window queued
    /// silently instead of ever being acted on. A Settled record now falls straight through to false
    /// below (never short circuits); the commit itself moved to
    /// <see cref="CommitAppliedChiefCashEffects"/>, which the caller runs unconditionally once this
    /// method returns false, and which no longer needs the record's own NativeEffectIds to find the
    /// effect (a later ArrestObserved reset clears that list before the commit gets a chance to run).
    /// </summary>
    private bool ContinuePayment(Release1ChiefRecord record, string playerId, LocalPressureState ledger)
    {
        if (record.State != Release1ChiefState.Paying) return false;
        // OC-73 review fix. The id can no longer be recomputed from round alone (it now folds in the
        // record's Revision, which has already advanced past what RunPay captured by the time this
        // pass reads the record); record.NativeEffectIds already carries the exact id the current
        // cycle prepared, durably, so read it back instead of trying to reconstruct it.
        var effectId = record.NativeEffectIds.Count > 0 ? record.NativeEffectIds[^1] : null;
        if (effectId is null) return false;
        var effect = _story.State?.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (effect is null) return false;

        // OC-73 review fix. A debit RunPay could not confirm as Succeeded (Ambiguous, Rejected, or a
        // thrown exception) is blocked in place there rather than reopened, so the record stays Paying
        // forever with this exact effect permanently ExecutionBlocked: never retried, never applied,
        // exactly the RoomWithNoName/ShortNotice/SmallCourtesy/TheEnvelope/WrongAddress convention for
        // an Ambiguous native outcome. Holding here (rather than falling into RetryUnconfirmedPayment,
        // which would try to mark it applied) is what makes that block permanent.
        //
        // OC-73 review fix (finding 2). This used to return straight to Update(), which stopped there
        // (ContinuePayment returning true skips both DrainOne and ReconcileLockdown for the pass), so a
        // lockdown engaged before the blocked payment could never lift again: not by payment (the
        // record is not DemandOpen), not by cooling (ReconcileLockdown was unreachable). Running it
        // here, still inside the one blocked-effect branch, restores the cooled-lift path without
        // touching the money-safety decision above at all.
        if (effect.ExecutionBlocked)
        {
            ReconcileLockdown(_story.State?.ChiefRecord, ledger);
            return true;
        }

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            // OC-73 review fix (finding 2). A Prepared, non-blocked effect on a Paying record used to
            // be assumed a Succeeded debit merely awaiting confirmation (RetryUnconfirmedPayment's own
            // former precondition). That assumption breaks the one time RunPay's own blocking write at
            // that non-Succeeded-debit site is itself refused (a DeferredSaving gate, most plausibly):
            // the effect is left exactly as a genuinely successful, unconfirmed debit would leave it,
            // and RetryUnconfirmedPayment would mark Applied for money that was never confirmed to have
            // moved. _confirmedDebits (set only by RunPay, only once its own debit call returned
            // Succeeded) is the one reliable signal that distinguishes the two; absent it, retry the
            // block, never the confirm.
            return _confirmedDebits.Contains(effect.EffectId)
                ? RetryUnconfirmedPayment(record, effect)
                : RetryUnrecordedBlock(effect.EffectId);
        }

        if (effect.Phase != Release1NativeEffectPhase.Applied) return false;

        // OC-73 review fix (finding 6). A settle write that keeps getting rejected used to re-run the
        // wipe on every later pass too (the whole branch re-entered from the top), bumping the Local
        // Pressure revision for nothing every game minute. _wipedEffectIds remembers a wipe that
        // already succeeded for this exact effect, so only the settle write is retried after.
        if (!_wipedEffectIds.Contains(effectId))
        {
            var wipeResult = _wipeLedger(playerId);
            if (!wipeResult.Accepted) return true;
            _wipedEffectIds.Add(effectId);
        }
        var decision = Release1ChiefLedger.Settle(record);
        // OC-73 spec decision 19: settling while under lockdown lifts it, its own line ("the payment
        // lands"). The pure ledger already clears the flag and counts the lift; the world release
        // itself gates the write, the same NeedsOptionB-holds-and-retries shape ReconcileLockdown's
        // own release path uses.
        if (decision.Action == Release1ChiefAction.LiftPaid)
        {
            if (_world.TryReleaseLockdown(out var releaseReason) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                ReportHoldOnce(releaseReason);
                return true;
            }
        }
        var settleResult = _story.TrySetChiefRecord(decision.Next);
        if (!settleResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief settle write for round {record.DemandRound} was rejected: {settleResult.RejectReason}: {settleResult.Message}");
        return true;
    }

    /// <summary>
    /// OC-73 settled drain fix. Runs every pass that ContinuePayment does not itself hold on (so never
    /// while a payment is genuinely Paying, since Update returns before reaching this call in that
    /// case). Sweeps the native effect journal directly for Chief cash effects (identified by
    /// ChiefCashEffectIdPrefix, which mirrors Release1ChiefEffect.EffectId's own prefix) that are
    /// Applied, not ExecutionBlocked, and still carry the correlation Prepare authorized, and commits
    /// each once the revision captured at the start of this sweep has actually persisted, exactly the
    /// check the former Settled branch of ContinuePayment performed. Reading the journal directly
    /// instead of record.NativeEffectIds is the point: Release1ChiefLedger.Observe's
    /// Adopted-or-Settled ArrestObserved branch clears that list (and AcceptedLogicalCorrelations) the
    /// moment a new arrest lands, which can now happen before this commit ever gets a chance to run.
    /// Release1StoryRuntimeService.IsCurrentEffectAuthorization's widened Applied branch is what still
    /// authorizes the commit once the live record has moved on to DemandOpen or a later Paying round.
    /// </summary>
    private void CommitAppliedChiefCashEffects()
    {
        var state = _story.State;
        if (state is null) return;
        var capturedRevision = state.Revision;
        if (capturedRevision > _story.LastPersistedRevision) return;
        foreach (var effect in state.NativeEffects)
        {
            if (effect.Phase != Release1NativeEffectPhase.Applied || effect.ExecutionBlocked) continue;
            if (effect.AuthorizedStoryCorrelationId is null) continue;
            if (!effect.EffectId.StartsWith(ChiefCashEffectIdPrefix, StringComparison.Ordinal)) continue;
            _story.TryCommitNativeEffect(effect.EffectId, effect.AuthorizedStoryCorrelationId, capturedRevision);
        }
    }

    /// <summary>
    /// OC-73 review fix (finding 2). The counterpart to RetryUnconfirmedPayment: retries recording the
    /// same effect as blocked (idempotent through TryRecordNativeEffectIssuance's own NoOp on an
    /// already-blocked effect) for a Prepared effect this session never confirmed a Succeeded debit
    /// for. Once the block finally lands, ContinuePayment's ExecutionBlocked branch takes over and this
    /// is never called again for that effect.
    /// </summary>
    private bool RetryUnrecordedBlock(string effectId)
    {
        // Only ever reached for a record still Paying (ContinuePayment's own gate), which for this
        // dispatch is exactly the permanently-stuck Ambiguous or Rejected case: Unavailable always
        // reopens the demand in the same RunPay call that blocks its own orphaned effect, so a record
        // whose latest effect is still Prepared and un-confirmed on a later pass was never Unavailable.
        BlockEffect(effectId, "retry", notifyPlayer: true);
        return true;
    }

    /// <summary>
    /// OC-73 review fix (finding 2). Blocks a prepared effect and, only the first time this exact
    /// effect is durably blocked (Status is Accepted, not the NoOp an already-blocked effect returns)
    /// and the caller asks for it, sends the one Chief message telling the player payment could not be
    /// confirmed and the demand still stands (PaymentBlockedNotices, the same durable once-only counter
    /// shape as WatchLinesSent and PaidLifts), so a permanently blocked payment is never silent.
    /// </summary>
    private void BlockEffect(string effectId, string attempt, bool notifyPlayer)
    {
        var blockResult = _story.TryRecordNativeEffectIssuance(effectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        if (!blockResult.Accepted)
        {
            OrganizedCrimeLog.Warning($"Chief payment block ({attempt}) for effect {effectId} was itself refused: {blockResult.RejectReason}: {blockResult.Message}; holding for a later pass.");
            return;
        }
        if (!notifyPlayer || blockResult.Status != Release1StoryCommandStatus.Accepted) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        var noticeResult = _story.TrySetChiefRecord(record with { PaymentBlockedNotices = record.PaymentBlockedNotices + 1, Revision = record.Revision + 1 });
        if (!noticeResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief payment blocked notice could not be recorded: {noticeResult.RejectReason}: {noticeResult.Message}");
    }

    /// <summary>
    /// OC-73 review fix. The only way a Paying record's effect can still be Prepared on a later pass
    /// is that RunPay's own debit already returned Succeeded (a non-Succeeded debit is reopened
    /// synchronously inside RunPay itself, before the record is ever left here for a later pass to
    /// find); only the read-back or the MarkApplied call after it can have failed. Retries exactly
    /// that tail, never the debit, every convergence pass until it completes, so an interrupted
    /// payment finishes instead of freezing the record in Paying forever with the wallet already
    /// short and no effect ever reaching Applied. record.Revision minus one is the exact revision
    /// RunPay captured before PrepareChiefEffect advanced it, matching the receipt id RunPay itself
    /// would have used (Release1ChiefEffect.NativeReceiptId), so a receipt this retry finally applies
    /// carries the identical value a same-pass success would have.
    /// </summary>
    private bool RetryUnconfirmedPayment(Release1ChiefRecord record, Release1NativeEffectJournalEntry effect)
    {
        if (_world.TryReadCashBalance(out _) != Release1SmallCourtesyWorldReadStatus.Ready)
        {
            OrganizedCrimeLog.Warning($"Chief payment retry for round {record.DemandRound} could not read the wallet back; holding for a later pass.");
            return true;
        }
        var nativeReceiptId = Release1ChiefEffect.NativeReceiptId(record.DemandRound, record.Revision - 1);
        var markResult = _story.TryMarkNativeEffectApplied(effect.EffectId, nativeReceiptId, Release1NativeEffectPersistenceMode.RevertTolerant);
        if (!markResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief payment retry for round {record.DemandRound} could not mark the effect applied: {markResult.RejectReason}: {markResult.Message}; holding for a later pass.");
        return true;
    }

    // OC-73 review fix. This used to be the one Chief transition with no log line at all, so the
    // owner protocol's own step 1 ("Confirm the Melon log shows the Chief record adopted, with the
    // tier and Known Offender flag it observed") asked for evidence the code never produced. Logged
    // through the Receipt channel (an ordinary MelonLogger.Msg line, not a warning), matching the
    // convention the Local Pressure and native law response runtimes already use for their own
    // expected-event lines, and only once the write is actually accepted.
    private void Adopt(LocalPressureState ledger)
    {
        var tier = LocalPressureTransitions.GetTier(ledger.LocalHeat, _profile);
        var result = _story.TrySetChiefRecord(Release1ChiefRecord.Adopt(tier, ledger.KnownOffender));
        if (result.Accepted)
            OrganizedCrimeLog.Receipt($"Chief Campbell record adopted: tier {tier}, Known Offender {ledger.KnownOffender}.");
    }

    private void DrainOne(Release1ChiefRecord record, string playerId, Guid sessionEpoch, long loadEpoch)
    {
        if (!_observer.TryDequeue(playerId, sessionEpoch, loadEpoch, out var queued)) return;
        var decision = Release1ChiefLedger.Observe(record, queued.Kind);
        if (decision.Next.Revision == record.Revision) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        // OC-73 review fix. This write used to be fired and forgotten: a rejection here (a stale
        // revision, a failed Validate) left the queued event silently consumed and the record frozen
        // with no signal anywhere. Surface it the same way Release1StoryLifecycleLogging does for a
        // rejected lifecycle result, so a future regression here fails loudly instead of quietly
        // wedging the story state.
        if (!result.Accepted)
            OrganizedCrimeLog.Warning(
                $"Chief record write was rejected on {queued.Kind}: {result.RejectReason}: {result.Message}");
    }

    // OC-73 spec decisions 18 to 21. Engaging is gated on the announcement's own presentation
    // receipt already existing in story state, so the announcement is a precondition of the
    // lockdown, never a consequence of it, and engaging necessarily happens on a later pass than
    // announcing. A fresh engage also requires the currently observed tier to still be Critical
    // (OC-73 settled drain fix, tightened): LockdownAnnouncements never resets, and it is only ever
    // minted on a rising crossing into Critical, so without this a record reopened long after its
    // tier cooled off, even to Watched, would engage anyway on an announcement that crossing never
    // sent. Lifting has two independent causes with their own lines: the payment settles (handled by
    // Release1ChiefLedger.Settle on the pay path), or the observed tier falls below Watched, which
    // is read here from the ledger because decay publishes no tier transition.
    private void ReconcileLockdown(Release1ChiefRecord? record, LocalPressureState ledger)
    {
        if (record is null) return;
        var tier = LocalPressureTransitions.GetTier(ledger.LocalHeat, _profile);

        if (record.LockdownEngaged)
        {
            // OC-73 review fix (finding 2). Was "tier >= Watched && IsUnpaid(record)": IsUnpaid is
            // false while the record is Paying, including the permanently blocked Paying a refused
            // debit leaves behind, so this used to fall straight to the cooled-lift branch below and
            // release the lockdown immediately on the very next pass regardless of whether the tier had
            // actually cooled at all. Tier alone is the correct re-engage test: a settled record always
            // clears LockdownEngaged in the same write as its state change (Release1ChiefLedger.Settle),
            // so this branch is never reached with State == Settled, and a Paying record with the tier
            // still at or above Watched must keep holding, not release, until it genuinely cools.
            if (tier >= LocalPressureTier.Watched)
            {
                // A restart re engages: the flag persisted, the native gate did not.
                if (!TryEngageLockdownOnce(out var holdReason)) ReportHoldOnce(holdReason);
                return;
            }

            var cooled = Release1ChiefLedger.CoolBelowWatched(record);
            if (cooled.Action != Release1ChiefAction.LiftCooled) return;
            if (_world.TryReleaseLockdown(out var releaseReason) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                ReportHoldOnce(releaseReason);
                return;
            }
            var cooledResult = _story.TrySetChiefRecord(cooled.Next);
            if (!cooledResult.Accepted)
                OrganizedCrimeLog.Warning($"Chief cooled-lift write was rejected: {cooledResult.RejectReason}: {cooledResult.Message}");
            return;
        }

        // OC-73 settled drain fix, tightened. LockdownAnnouncements never resets, and the announcement
        // it counts is minted in exactly one place: Release1ChiefLedger.Observe's RoseToCritical
        // branch, which only fires on a rising crossing into Critical. A stale Watched-or-above test
        // here was not tight enough: a demand reopened after a paid or cooled lift climbs its own
        // ladder again, and heat can sit at Watched, announcement free, for as long as an arrest keeps
        // reopening the ladder without a fresh crossing all the way to Critical. Engaging there would
        // hold Chief Campbell to an announcement that was never sent this cycle, breaking the owner's
        // rule that the announcement is always a precondition of the lockdown, never a consequence of
        // it. Every path back to Critical after either kind of lift, paid or cooled, necessarily
        // recrosses into Critical from below and so mints a fresh announcement on the way; requiring
        // the observed tier to still be Critical here is what keeps "announce first" true across
        // cycles, not merely across a single one. The branch above this one is unrelated: it decides
        // whether an already engaged lockdown keeps holding, and Watched remains the right floor for
        // that, since holding through Watched does not claim a fresh announcement the way engaging
        // fresh here would.
        if (tier < LocalPressureTier.Critical) return;

        // Release1ChiefTierObserver drains at most one queued event per pass (arrest first, its
        // crossing second, by its own design), while the ledger heat above is read live and already
        // reflects a crossing's evidence write before either half of it is ever drained. On the pass
        // that drains only the arrest half, the tier can already read Critical while LockdownAnnouncements
        // still holds the count from before this crossing. For a demand reopened after an earlier full
        // cycle, that earlier count already has its own receipt on file, so HasAnnouncementReceipt below
        // would pass on that stale receipt and engage a full pass before this crossing's own RoseToCritical
        // half ever mints its announcement. Waiting for the queue to drain closes that gap: once it is
        // empty, LockdownAnnouncements reflects everything this crossing has to say, and the receipt
        // check below is judging this cycle's own announcement, never an older one.
        if (_observer.QueueDepth > 0) return;

        if (record.LockdownAnnouncements < 1 || !Release1ChiefLedger.IsUnpaid(record)) return;
        if (!HasAnnouncementReceipt(record)) return;                 // decision 18, the precondition
        if (!TryEngageLockdownOnce(out var reason))
        {
            ReportHoldOnce(reason);                                   // decision 21, NeedsOptionB holds
            return;
        }
        var engageResult = _story.TrySetChiefRecord(record with { LockdownEngaged = true, Revision = record.Revision + 1 });
        if (!engageResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief lockdown engage write was rejected: {engageResult.RejectReason}: {engageResult.Message}");
    }

    /// <summary>
    /// Shared by the first engage (announcement precondition met) and the restart re engage (the
    /// flag persisted across a save but the native gate did not) so both paths make the exact same
    /// call and report a hold the exact same way.
    /// </summary>
    private bool TryEngageLockdownOnce(out string reason) =>
        _world.TryEngageLockdown(out reason) == Release1SmallCourtesyWorldMutationStatus.Succeeded;

    /// <summary>
    /// Spec decision 18: the announcement is a precondition of the lockdown, so this looks for the
    /// exact correlation the projector will have recorded once the desired lockdown message actually
    /// reached the player, never a queued or in flight one.
    /// </summary>
    private bool HasAnnouncementReceipt(Release1ChiefRecord record)
    {
        var state = _story.State;
        if (state is null) return false;
        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(state.PlayerId, record);
        return state.PresentationReceipts.Any(receipt => receipt.CorrelationId == correlation);
    }

    /// <summary>
    /// The shipped one line per load pattern the five mission services already use, keyed on the
    /// message text itself (this service has no per-attempt counter to key on) so a repeated hold
    /// logs once per load rather than once per convergence pass.
    /// </summary>
    private void ReportHoldOnce(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (!_reportedHolds.Add(message)) return;
        OrganizedCrimeLog.Warning($"Chief Campbell lockdown gate is holding: {message}");
    }
}
