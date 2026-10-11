using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Core.Scheduling;
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
public sealed class Release1ChiefService : IDisposable, IRelease1ChiefProtectionQa, IRelease1ChiefForcedWantedQa
{
    // A cash-balance read is float precision while the demand ladder is whole dollars; this bounds
    // the float round-trip error, far below one cent, mirroring Release1TheEnvelopeMissionService's
    // own CashReadTolerance.
    private const double CashReadTolerance = 0.01d;


    private readonly Release1StoryRuntimeService _story;
    private readonly IChiefWorld _world;
    private readonly Release1ChiefTierObserver _observer;
    private readonly Release1ChiefLedgerReader _readLedger;
    private readonly Release1ChiefLedgerWiper _wipeLedger;
    private readonly Release1ChiefEpochReader _epochs;
    private readonly LocalPressureProfile _profile;
    private readonly GameHourGate _throttle = new();
    private readonly OnceLog _reportedHolds;
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
    // OC-118. The read-only Chapter 3 signal the protection offer fires on (null reads as zero, so
    // services constructed without it never offer), and the checkpoint world the cover start
    // disables (null skips the disable silently). Both trailing and optional so every existing
    // construction site compiles unchanged.
    private readonly Func<int>? _chapter3SuccessfulJobs;
    private readonly IRelease1ProtectionCheckpointWorld? _checkpointWorld;
    // OC-74. The forced-Wanted seams, all trailing and optional like the protection ones: a
    // null seam (or a null host read) skips the fire reconcile for that pass, so services
    // constructed without them behave exactly as before.
    private readonly Func<bool>? _isAuthoritativeHost;
    private readonly IChapter3PlayerExposureReader? _exposureReader;
    private readonly IChapter3CurfewReader? _curfewReader;
    private readonly IChapter3WantedWriter? _wantedWriter;
    private readonly IChapter3ForcedWantedDispatch? _forcedWantedDispatch;
    private readonly IChapter3PursuitHold? _pursuitHold;
    // OC-118. The cover end day whose checkpoints were already switched off, so the disable runs
    // once per cover period and retries on later passes until it lands. In-memory like the holds:
    // the native gates do not persist, so a load re-runs the idempotent disable on the first pass.
    private int? _checkpointsDisabledForUntilDay;
    private int? _checkpointDisableNoticedForUntilDay;
    private const string ForcedWantedHoldKey = "forced-wanted-hold";
    private readonly OnceLog _forcedWantedHoldLog;
    private bool _disposed;
    private bool? _qaLockdown;
    internal bool ReadLockdownEngaged() => !_disposed && (_story.State?.ChiefRecord?.LockdownEngaged ?? false);
    // Owner QA deliberately holds the requested Chief state across heat changes until
    // the next load or explicit switch. D4 still reads the real record/stamp.
    internal Chapter3QaResult QaSetLockdown(bool engaged)
    {
        if (_disposed || _isAuthoritativeHost?.Invoke() != true || _story.Phase != Release1StoryRuntimePhase.Active)
            return new(false, "lockdown refused: authoritative active host required.");
        var record = _story.State?.ChiefRecord;
        if (record is null) return new(false, "lockdown refused: prepare a save with a Chief record.");
        if (engaged && IsProtected()) return new(false, "lockdown refused: protection excludes lockdown.");
        if (_world.TryReadCanonicalTotalMinutes(out var now) != Release1SmallCourtesyWorldReadStatus.Ready || !double.IsFinite(now) || now < 0d)
            return new(false, "lockdown refused: clock unreadable.");
        var mutation = engaged ? _world.TryEngageLockdown(out _) : _world.TryReleaseLockdown(out _);
        if (mutation != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            return new(false, "lockdown refused: native state change failed.");
        var result = _story.TrySetChiefRecord(record with
        {
            LockdownEngaged = engaged,
            LockdownAnnouncements = engaged ? Math.Max(1, record.LockdownAnnouncements) : record.LockdownAnnouncements,
            LockdownEngagedAtMinutes = engaged ? record.LockdownEngagedAtMinutes ?? now : null,
            Revision = record.Revision + 1
        });
        if (!result.Accepted)
        {
            // Restore the native gate if the owning record rejects the write.
            if (record.LockdownEngaged) _world.TryEngageLockdown(out _);
            else _world.TryReleaseLockdown(out _);
            return new(false, $"lockdown refused: {result.Message}");
        }
        _qaLockdown = engaged;
        _throttle.Clear();
        return new(true, $"lockdown {(engaged ? "on" : "off")}: Chief record/native gate set; QA hold ends on load.");
    }

    public Release1ChiefService(
        Release1StoryRuntimeService story,
        IChiefWorld world,
        Release1ChiefTierObserver observer,
        Release1ChiefLedgerReader readLedger,
        Release1ChiefLedgerWiper wipeLedger,
        Release1ChiefEpochReader epochs,
        LocalPressureProfile profile,
        Func<int>? chapter3SuccessfulJobs = null,
        IRelease1ProtectionCheckpointWorld? checkpointWorld = null,
        Func<bool>? isAuthoritativeHost = null,
        IChapter3PlayerExposureReader? exposureReader = null,
        IChapter3CurfewReader? curfewReader = null,
        IChapter3WantedWriter? wantedWriter = null,
        IChapter3ForcedWantedDispatch? forcedWantedDispatch = null,
        IChapter3PursuitHold? pursuitHold = null)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _readLedger = readLedger ?? throw new ArgumentNullException(nameof(readLedger));
        _wipeLedger = wipeLedger ?? throw new ArgumentNullException(nameof(wipeLedger));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _chapter3SuccessfulJobs = chapter3SuccessfulJobs;
        _checkpointWorld = checkpointWorld;
        _isAuthoritativeHost = isAuthoritativeHost;
        _exposureReader = exposureReader;
        _curfewReader = curfewReader;
        _wantedWriter = wantedWriter;
        _forcedWantedDispatch = forcedWantedDispatch;
        _pursuitHold = pursuitHold;
        _reportedHolds = new OnceLog(line => OrganizedCrimeLog.Warning(line));
        _forcedWantedHoldLog = new OnceLog(line => OrganizedCrimeLog.Warning(line));
    }

    public void OnPreLoad() { if (!_disposed) { _qaLockdown = null; _throttle.Clear(); _reportedHolds.ClearAll(); _confirmedDebits.Clear(); _wipedEffectIds.Clear(); _checkpointsDisabledForUntilDay = null; _checkpointDisableNoticedForUntilDay = null; _forcedWantedHoldLog.ClearAll(); } }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        _qaLockdown = null;
        _throttle.Clear();
        _reportedHolds.ClearAll();
        _confirmedDebits.Clear();
        _wipedEffectIds.Clear();
        _checkpointsDisabledForUntilDay = null;
        _checkpointDisableNoticedForUntilDay = null;
        _forcedWantedHoldLog.ClearAll();
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
    /// OC-118 threads the protection retainer through the same pass: the in flight purchase
    /// continues first (holding like a demand payment), covered passes discard rather than drain,
    /// and the offer, warning, lapse and checkpoint reconciles run on the fresh record.
    /// OC-74 runs the forced-Wanted warning and fire reconciles last, after the lockdown.
    /// </summary>
    public void Update()
    {
        if (_disposed) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        if (_story.Phase != Release1StoryRuntimePhase.Active) return;
        var state = _story.State;
        if (state is null) return;

        var minutes = _world.TryReadCanonicalTotalMinutes(out var read) == Release1SmallCourtesyWorldReadStatus.Ready ? (double?)read : null;
        HoldForcedWanted(minutes);
        if (!_throttle.TryPass(minutes, state.Revision)) return;

        if (!_epochs(out var sessionEpoch, out var loadEpoch)) return;
        var ledger = _readLedger(context.PlayerId);
        if (ledger is null) return;

        var record = state.ChiefRecord;
        if (record is null) { Adopt(ledger); return; }

        if (ContinuePayment(record, context.PlayerId, ledger)) return;
        if (ContinueProtectionPayment(record)) return;
        CommitAppliedChiefCashEffects();
        var today = Today(minutes);
        if (today is { } day && Release1ChiefLedger.IsProtected(record, day))
            DiscardOne(context.PlayerId, sessionEpoch, loadEpoch);
        else
            DrainOne(record, context.PlayerId, sessionEpoch, loadEpoch);
        ReconcileProtectionOffer();
        ReconcileProtectionWarning(today);
        ReconcileProtectionLapse(today);
        ReconcileProtectionCheckpoints(today);
        ReconcileLateFee(minutes);
        ReconcileLockdown(_story.State?.ChiefRecord, ledger);
        ReconcileForcedWantedWarning(minutes);
        ReconcileForcedWanted(minutes);
    }

    /// <summary>
    /// OC-141. The player's current Local Pressure tier, for D3b's checkpoint
    /// planner. Fails closed (null) on a disposed service, no active context,
    /// or an unreadable ledger: callers skip the pass when the tier is unknown.
    /// </summary>
    public LocalPressureTier? ReadTier()
    {
        if (_disposed) return null;
        if (!_story.TryGetActiveContext(out var context, out _)) return null;
        var ledger = _readLedger(context.PlayerId);
        if (ledger is null) return null;
        return LocalPressureTransitions.GetTier(ledger.LocalHeat, _profile);
    }
    /// <summary>
    /// OC-118. Whether the Chief's protection covers this player right now. Fails closed (false)
    /// on a disposed service, an unreadable record, or an unreadable clock: every gate that consults
    /// this acts as if unprotected, never covered, when it cannot prove cover. OC-119's D6 pressure
    /// exemption reads this same method rather than re-deriving cover.
    /// </summary>
    public bool IsProtected()
    {
        if (_disposed) return false;
        var record = _story.State?.ChiefRecord;
        if (record is null) return false;
        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready) return false;
        var today = (int)Math.Floor(read / 1440d);
        return Release1ChiefLedger.IsProtected(record, today);
    }

    // D6 must distinguish unreadable cover from proven absence of protection.
    internal bool? ReadSweepProtection()
    {
        if (_disposed || !_story.TryGetActiveContext(out _, out _) || _story.State?.ChiefRecord is not { } record)
            return null;
        try
        {
            if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !double.IsFinite(read) || read < 0d || read / 1440d > int.MaxValue) return null;
            return Release1ChiefLedger.IsProtected(record, (int)Math.Floor(read / 1440d));
        }
        catch { return null; }
    }

    /// <summary>
    /// OC-118. The read-only protection view Chapter 3 presents and QA reports: the offer flag,
    /// the absolute cover end day, and whether this service's own clock calls it covered now. Fails
    /// closed like <see cref="IsProtected"/>: a disposed service or an unreadable record reads as
    /// never offered, never covered.
    /// </summary>
    public Release1ChiefProtectionSnapshot ReadProtectionSnapshot()
    {
        if (_disposed) return new(false, null, false);
        var record = _story.State?.ChiefRecord;
        if (record is null) return new(false, null, false);
        return new(record.ProtectionOffered, record.ProtectedUntilDay, IsProtected());
    }

    /// <summary>
    /// OC-118. Today on the protection clock, for the presentation: without it the standing
    /// decision cannot tell Buy from Extend or hide itself at the banking cap. Null when the clock
    /// is unreadable or the service is disposed, which presents as uncovered.
    /// </summary>
    public int? ReadToday()
    {
        if (_disposed) return null;
        return _world.TryReadCanonicalTotalMinutes(out var read) == Release1SmallCourtesyWorldReadStatus.Ready
            ? Today(read)
            : null;
    }

    // OC-118. Protection runs on the same clock as the late fee: whole days below the canonical
    // minute count. A null or negative read is no day at all, and every reconcile below skips it.
    private static int? Today(double? minutes)
    {
        if (minutes is not { } read) return null;
        var today = (int)Math.Floor(read / 1440d);
        return today < 0 ? null : today;
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

    /// <summary>
    /// OC-118. The player picked Buy (or Extend) protection. Settle first: a tap that arrives while
    /// a demand stands unpaid is refused and counted, never charged. Past that, the tap needs the
    /// offer, an unpaid record, a readable clock, room under the banking cap, and the cash; then it
    /// runs the same prepare, debit, read-back and mark-applied shape as RunPay, and the cover lands
    /// on the pass after Applied.
    /// </summary>
    public bool TryBuyProtection()
    {
        if (_disposed) return false;
        _throttle.Clear();
        return RunProtectionPurchase();
    }

    public void Dispose() => _disposed = true;

    /// <summary>
    /// Sets the offered flag through the ledger's own offer decision; the offer text and the
    /// Buy button go out through the normal presentation path on the next pass.
    /// </summary>
    public Chapter3QaResult QaProtectionOffer()
    {
        if (_disposed) return new(false, "Chapter 3 QA protection offer refused: the Chief service is disposed.");
        var record = _story.State?.ChiefRecord;
        if (record is null) return new(false, "Chapter 3 QA protection offer refused: no Chief record.");
        if (record.ProtectionOffered) return new(true, "Chapter 3 QA protection offer: already offered.");
        var decision = Release1ChiefLedger.OfferProtection(record);
        if (decision.Next.Revision == record.Revision) return new(true, "Chapter 3 QA protection offer: already offered.");
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted) return new(false, $"Chapter 3 QA protection offer refused: {result.RejectReason}: {result.Message}");
        OrganizedCrimeLog.Receipt("Chief Campbell made his protection offer (QA).");
        return new(true, "Chapter 3 QA protection offer: the offer text is on its way.");
    }

    /// <summary>
    /// Sets the cover end day to today, so the lapse (texts, quest line removal, checkpoint
    /// restore) runs on the next pass.
    /// </summary>
    public Chapter3QaResult QaProtectionLapse()
    {
        if (_disposed) return new(false, "Chapter 3 QA protection lapse refused: the Chief service is disposed.");
        var record = _story.State?.ChiefRecord;
        if (record?.ProtectedUntilDay is not { } until) return new(false, "Chapter 3 QA protection lapse refused: not covered.");
        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready)
            return new(false, "Chapter 3 QA protection lapse refused: the clock was unreadable.");
        var today = (int)Math.Floor(read / 1440d);
        if (today < 0) return new(false, "Chapter 3 QA protection lapse refused: the clock was unreadable.");
        if (today >= until) return new(false, "Chapter 3 QA protection lapse refused: not covered.");
        var result = _story.TrySetChiefRecord(record with { ProtectedUntilDay = today, Revision = record.Revision + 1 });
        if (!result.Accepted) return new(false, $"Chapter 3 QA protection lapse refused: {result.RejectReason}: {result.Message}");
        return new(true, "Chapter 3 QA protection lapse: the lapse runs on the next pass.");
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys): the status line's snapshot.
    /// </summary>
    public Release1ChiefProtectionSnapshot QaProtectionSnapshot() => ReadProtectionSnapshot();

    /// <summary>
    /// Backdates the lockdown stamp so the lockdown reads as the given hours old; refused
    /// without an engaged lockdown, a readable clock, or a non-negative age that keeps the
    /// stamp non-negative.
    /// </summary>
    public Chapter3QaResult QaLockdownAge(double hours)
    {
        if (_disposed) return new(false, "Chapter 3 QA lockdown age refused: the Chief service is disposed.");
        var record = _story.State?.ChiefRecord;
        if (record is null || !record.LockdownEngaged) return new(false, "Chapter 3 QA lockdown age refused: no lockdown is engaged.");
        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready)
            return new(false, "Chapter 3 QA lockdown age refused: the clock was unreadable.");
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0)
            return new(false, "Chapter 3 QA lockdown age refused: hours must be non-negative.");
        var stamp = read - hours * 60d;
        if (double.IsNaN(stamp) || double.IsInfinity(stamp) || stamp < 0)
            return new(false, "Chapter 3 QA lockdown age refused: the lockdown is not that old yet.");
        var result = _story.TrySetChiefRecord(record with { LockdownEngagedAtMinutes = stamp, Revision = record.Revision + 1 });
        if (!result.Accepted) return new(false, $"Chapter 3 QA lockdown age refused: {result.RejectReason}: {result.Message}");
        return new(true, $"Chapter 3 QA lockdown age: the lockdown is now {hours} hours old.");
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys): the wanted status snapshot.
    /// </summary>
    public Release1ChiefForcedWantedSnapshot QaForcedWantedSnapshot()
    {
        var record = _disposed ? null : _story.State?.ChiefRecord;
        if (record is null) return new(false, false, null, false, null, null, false, null, null, 0, 0, 0, false, false, null, null, null);
        double? ageHours = null;
        if (record.LockdownEngagedAtMinutes is { } stamped &&
            _world.TryReadCanonicalTotalMinutes(out var read) == Release1SmallCourtesyWorldReadStatus.Ready)
            ageHours = (read - stamped) / 60d;
        bool? curfew = null;
        try { if (_curfewReader?.TryReadActive(out var active) == true) curfew = active; }
        catch { }
        bool? outdoors = null;
        int? pursuit = null;
        bool? arrested = null;
        try
        {
            if (_exposureReader?.TryRead(out var exposure) == true)
            {
                outdoors = exposure.Outdoors;
                pursuit = exposure.PursuitLevel;
                arrested = exposure.Arrested;
            }
        }
        catch { }
        var protectedNow = IsProtected();
        var armed = false;
        var held = false;
        float? since = null;
        float? search = null;
        try
        {
            if (_world.TryReadCanonicalTotalMinutes(out var now) == Release1SmallCourtesyWorldReadStatus.Ready)
                armed = Release1ChiefLedger.IsForcedWantedArmed(record, now, protectedNow);
            if (_pursuitHold?.TryRead(out var level, out var readSince, out var readSearch) == true)
            {
                since = readSince;
                search = readSearch;
                held = armed && level >= Chapter3PursuitLevels.Arresting;
            }
        }
        catch { }
        return new(true, record.LockdownEngaged, ageHours, Release1ChiefLedger.IsForcedWantedWarned(record),
            curfew, outdoors, protectedNow, pursuit, record.LastForcedWantedNight,
            record.ForcedWantedFires, record.ForcedWantedDispatches, record.ForcedWantedDispatchRefusals,
            armed, held, since, search, arrested);
    }

    private bool RunPay()
    {
        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var state = _story.State;
        var record = state?.ChiefRecord;
        if (record is null || !Release1ChiefLedger.IsUnpaid(record)) return false;

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

    // OC-118. The protection mirror of RunPay: the same prepare, debit, read-back and mark-applied
    // shape, the same failure convention (Unavailable alone reopens, everything else blocks), but
    // the effect authorizes purchase ProtectionPurchases + 1 and the cover lands on a later pass,
    // never in this call. The banking cap is enforced here, before the prepare: once the debit runs
    // the cover is owed and ApplyProtectionPurchase no longer asks about the cap.
    private bool RunProtectionPurchase()
    {
        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var state = _story.State;
        var record = state?.ChiefRecord;
        if (record is null) return false;

        if (Release1ChiefLedger.IsUnpaid(record))
        {
            var refused = Release1ChiefLedger.RefuseProtectionWhileUnpaid(record);
            var refusedResult = _story.TrySetChiefRecord(refused.Next);
            if (!refusedResult.Accepted)
                OrganizedCrimeLog.Warning($"Chief protection refusal write was rejected: {refusedResult.RejectReason}: {refusedResult.Message}");
            return false;
        }

        if (!record.ProtectionOffered) return false;
        if (record.State is not (Release1ChiefState.Adopted or Release1ChiefState.Settled)) return false;
        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready) return false;
        var today = (int)Math.Floor(read / 1440d);
        if (today < 0) return false;
        if (Release1ChiefLedger.ProtectionRoom(record, today) < Release1ChiefProtection.CoverDaysPerPurchase) return false;

        if (_world.TryReadCashBalance(out var balance) != Release1SmallCourtesyWorldReadStatus.Ready) return false;

        var price = Release1ChiefProtection.ProtectionWeeklyPrice;
        if (balance < price)
        {
            var shortfall = (double)price - balance;
            if (record.LastShortfallNoticed != shortfall)
            {
                var shortfallResult = _story.TrySetChiefRecord(record with { LastShortfallNoticed = shortfall, Revision = record.Revision + 1 });
                if (!shortfallResult.Accepted)
                    OrganizedCrimeLog.Warning($"Chief protection shortfall notice was rejected: {shortfallResult.RejectReason}: {shortfallResult.Message}");
            }
            return false;
        }

        var purchase = record.ProtectionPurchases + 1;
        var receiptId = $"chief-protection-p{purchase}-c{record.Revision}";
        var correlationId = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.ChiefCampbell, purchase, Release1TransitionKind.ChiefProtectionAccepted, receiptId).Value;
        var effectId = Release1ChiefProtection.EffectId(purchase, record.Revision);
        var prepared = new Release1NativeEffectJournalEntry(
            effectId, Release1MissionCatalog.ChiefCampbell, purchase, Release1ChiefEffect.Kind,
            Release1ChiefEffect.WalletSource, Release1ChiefEffect.Scope, Release1ChiefCampbellCopy.Dollars(price),
            Release1NativeEffectPhase.Prepared, null, state!.Revision + 1,
            AuthorizedStoryCorrelationId: correlationId, AuthorizedMissionRevision: record.Revision);

        var prepareResult = _story.TryPrepareNativeEffect(prepared);
        if (!prepareResult.Accepted) return false;

        Release1SmallCourtesyWorldMutationStatus debitStatus;
        try { debitStatus = _world.TryDebitCashBalance(-(float)price); }
        catch { debitStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
        if (debitStatus != Release1SmallCourtesyWorldMutationStatus.Succeeded)
        {
            OrganizedCrimeLog.Warning($"Chief protection debit for purchase {purchase} did not succeed ({debitStatus}); blocking the effect, no cash confirmed moved.");
            BlockEffect(effectId, "initial", notifyPlayer: debitStatus != Release1SmallCourtesyWorldMutationStatus.Unavailable, protectionPayment: true);

            if (debitStatus == Release1SmallCourtesyWorldMutationStatus.Unavailable)
            {
                var payingRecord = _story.State?.ChiefRecord;
                if (payingRecord is not null)
                {
                    var reopened = Release1ChiefLedger.AbandonUnappliedProtectionPayment(payingRecord);
                    var reopenResult = _story.TrySetChiefRecord(reopened.Next);
                    if (!reopenResult.Accepted)
                        OrganizedCrimeLog.Warning($"Chief protection reopen after an unavailable debit was itself rejected: {reopenResult.RejectReason}: {reopenResult.Message}");
                }
            }
            return false;
        }

        _confirmedDebits.Add(effectId);

        if (_world.TryReadCashBalance(out var post) != Release1SmallCourtesyWorldReadStatus.Ready ||
            Math.Abs(post - (balance - price)) > CashReadTolerance)
        {
            OrganizedCrimeLog.Warning($"Chief protection read-back for purchase {purchase} did not confirm the debit; the payment will retry to complete.");
            return false;
        }

        var markResult = _story.TryMarkNativeEffectApplied(
            effectId, Release1ChiefProtection.NativeReceiptId(purchase, record.Revision), Release1NativeEffectPersistenceMode.RevertTolerant);
        if (!markResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection for purchase {purchase} could not be marked applied: {markResult.RejectReason}: {markResult.Message}; the payment will retry to complete.");
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
    /// OC-118. The pass for a record still PayingProtection: the ContinuePayment shape with the wipe
    /// and the lockdown taken out. A still-Prepared effect retries the confirm tail or the block
    /// exactly like a demand payment (the debit is never repeated); an Applied effect lands the
    /// cover and hands the record back to the unpaid state the buy started from, derived the same
    /// way AbandonUnappliedProtectionPayment derives it. Every branch returns true: a purchase is
    /// genuinely in flight and the rest of the pass must wait for it.
    /// </summary>
    private bool ContinueProtectionPayment(Release1ChiefRecord record)
    {
        if (record.State != Release1ChiefState.PayingProtection) return false;
        var effectId = record.NativeEffectIds.Count > 0 ? record.NativeEffectIds[^1] : null;
        if (effectId is null) return false;
        var effect = _story.State?.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (effect is null) return false;

        // A blocked protection payment holds forever, the same money-safety convention as a blocked
        // demand payment. Unlike the demand branch there is no lockdown to reconcile here: a buy can
        // only start from Adopted or Settled, neither of which can hold an engaged lockdown.
        if (effect.ExecutionBlocked) return true;

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            return _confirmedDebits.Contains(effect.EffectId)
                ? RetryUnconfirmedProtectionPayment(record, effect)
                : RetryUnrecordedBlock(effect.EffectId, protectionPayment: true);
        }

        if (effect.Phase != Release1NativeEffectPhase.Applied) return false;

        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready)
        {
            ReportHoldOnce("Chief protection cover write is holding for a readable clock.");
            return true;
        }
        var today = (int)Math.Floor(read / 1440d);
        if (today < 0)
        {
            ReportHoldOnce("Chief protection cover write is holding for a readable clock.");
            return true;
        }
        var applied = Release1ChiefLedger.ApplyProtectionPurchase(record, today).Next with
        {
            State = record.DemandRound == 0 ? Release1ChiefState.Adopted : Release1ChiefState.Settled
        };
        var coverResult = _story.TrySetChiefRecord(applied);
        if (!coverResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection cover write for purchase {record.ProtectionPurchases} was rejected: {coverResult.RejectReason}: {coverResult.Message}");
        else
            OrganizedCrimeLog.Receipt($"Chief Campbell protection purchase {record.ProtectionPurchases} covered through day {applied.ProtectedUntilDay}.");
        return true;
    }

    /// <summary>
    /// OC-118. The RetryUnconfirmedPayment shape for a protection debit: the debit already returned
    /// Succeeded (only the read-back or the MarkApplied after it can have failed), so retry exactly
    /// that tail, never the debit. The receipt id is recomputed from the live record the same way.
    /// </summary>
    private bool RetryUnconfirmedProtectionPayment(Release1ChiefRecord record, Release1NativeEffectJournalEntry effect)
    {
        if (_world.TryReadCashBalance(out _) != Release1SmallCourtesyWorldReadStatus.Ready)
        {
            OrganizedCrimeLog.Warning($"Chief protection retry for purchase {record.ProtectionPurchases} could not read the wallet back; holding for a later pass.");
            return true;
        }
        var nativeReceiptId = Release1ChiefProtection.NativeReceiptId(record.ProtectionPurchases, record.Revision - 1);
        var markResult = _story.TryMarkNativeEffectApplied(effect.EffectId, nativeReceiptId, Release1NativeEffectPersistenceMode.RevertTolerant);
        if (!markResult.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection retry for purchase {record.ProtectionPurchases} could not mark the effect applied: {markResult.RejectReason}: {markResult.Message}; holding for a later pass.");
        return true;
    }

    /// <summary>
    /// OC-73 settled drain fix. Runs every pass that ContinuePayment does not itself hold on (so never
    /// while a payment is genuinely Paying, since Update returns before reaching this call in that
    /// case). Sweeps the native effect journal directly for Chief cash effects (identified by
    /// Release1ChiefEffect.IsCashEffect: the id prefix plus the Chief mission key) that are
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
            // OC-118. Protection purchases commit through the same sweep: same post-save revision
            // gate, same journal-direct read (a reset can clear the record's link first here too).
            if (!Release1ChiefEffect.IsCashEffect(effect) && !Release1ChiefProtection.IsProtectionEffect(effect)) continue;
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
    private bool RetryUnrecordedBlock(string effectId, bool protectionPayment = false)
    {
        // Only ever reached for a record still Paying (ContinuePayment's own gate) or still
        // PayingProtection (ContinueProtectionPayment's own gate, OC-118), which for this dispatch
        // is exactly the permanently-stuck Ambiguous or Rejected case: Unavailable always
        // reopens the demand in the same RunPay call that blocks its own orphaned effect, so a record
        // whose latest effect is still Prepared and un-confirmed on a later pass was never Unavailable.
        BlockEffect(effectId, "retry", notifyPlayer: true, protectionPayment: protectionPayment);
        return true;
    }

    /// <summary>
    /// OC-73 review fix (finding 2). Blocks a prepared effect and, only the first time this exact
    /// effect is durably blocked (Status is Accepted, not the NoOp an already-blocked effect returns)
    /// and the caller asks for it, sends the one Chief message telling the player payment could not be
    /// confirmed and the demand still stands (PaymentBlockedNotices, the same durable once-only counter
    /// shape as WatchLinesSent and PaidLifts), so a permanently blocked payment is never silent.
    /// OC-118. Shared with protection purchases, which count their own notices instead: a blocked
    /// cover payment leaves no demand standing, so the demand's text would lie.
    /// </summary>
    private void BlockEffect(string effectId, string attempt, bool notifyPlayer, bool protectionPayment = false)
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
        var noticeResult = _story.TrySetChiefRecord(protectionPayment
            ? record with { ProtectionBlockedNotices = record.ProtectionBlockedNotices + 1, Revision = record.Revision + 1 }
            : record with { PaymentBlockedNotices = record.PaymentBlockedNotices + 1, Revision = record.Revision + 1 });
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

    // OC-118. The covered twin of DrainOne: dequeues at most one queued event and drops it without
    // ever calling the ledger, so an arrest or a heat crossing while covered opens no demand,
    // records no watch line, and announces no lockdown. Discarding (rather than leaving the event
    // queued) is the no-backdating rule: a covered arrest must not open a demand on lapse either,
    // and anything left queued would drain the moment cover ends.
    private void DiscardOne(string playerId, Guid sessionEpoch, long loadEpoch)
    {
        _observer.TryDequeue(playerId, sessionEpoch, loadEpoch, out _);
    }

    // OC-118. The offer fires once ever, on the first pass the Chapter 3 signal reads three
    // successful jobs. The flag (not a crossing detector) is the gate, so an already-qualified save
    // is offered on its first pass too; an unwired or throwing signal reads as zero and never fires.
    private void ReconcileProtectionOffer()
    {
        int jobs;
        try { jobs = _chapter3SuccessfulJobs?.Invoke() ?? 0; }
        catch { return; }
        if (jobs < Release1ChiefProtection.OfferSuccessfulJobs) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        var decision = Release1ChiefLedger.OfferProtection(record);
        if (decision.Next.Revision == record.Revision) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection offer write was rejected: {result.RejectReason}: {result.Message}");
        else
            OrganizedCrimeLog.Receipt("Chief Campbell made his protection offer.");
    }

    // OC-118. The day-before warning and the lapse, both on the fresh record and both skipping an
    // unreadable clock. The warning's once-per-cover-period marker and the lapse's clear-the-end-day
    // guard both live in the ledger; these only persist the decision.
    private void ReconcileProtectionWarning(int? today)
    {
        if (today is not { } day) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        var decision = Release1ChiefLedger.WarnProtectionEnding(record, day);
        if (decision.Next.Revision == record.Revision) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection warning write was rejected: {result.RejectReason}: {result.Message}");
    }

    private void ReconcileProtectionLapse(int? today)
    {
        if (today is not { } day) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        var decision = Release1ChiefLedger.NoticeProtectionLapse(record, day);
        if (decision.Next.Revision == record.Revision) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection lapse write was rejected: {result.RejectReason}: {result.Message}");
        else
            OrganizedCrimeLog.Receipt("Chief Campbell protection lapsed.");
    }

    // OC-118. Switches every road checkpoint off once per cover period, on the first covered pass,
    // and retries on later passes until the call lands. Best effort, never holding: the money has
    // already moved, so a failed disable logs once per cover period and tries again next pass. The
    // marker resets the moment cover ends, so the next cover disables again. Before the first
    // disable of a cover period the pass snapshots which checkpoints are enabled, durably on the
    // record, so the lapse restores exactly those; the disable waits for the snapshot, since
    // disabling first would snapshot a row of dark checkpoints and restore nothing.
    private void ReconcileProtectionCheckpoints(int? today)
    {
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        if (today is not { } day || !Release1ChiefLedger.IsProtected(record, day))
        {
            ReconcileProtectionCheckpointRestore(record);
            _checkpointsDisabledForUntilDay = null;
            _checkpointDisableNoticedForUntilDay = null;
            return;
        }
        if (_checkpointWorld is null) return;
        var until = record.ProtectedUntilDay!.Value;
        if (record.ProtectionCoverStartCheckpoints is null && !SnapshotProtectionCoverStartCheckpoints(record, until))
            return;
        if (_checkpointsDisabledForUntilDay == until) return;
        bool disabled;
        try { disabled = _checkpointWorld.TryDisableAllCheckpoints(); }
        catch (Exception exception)
        {
            disabled = false;
            if (_checkpointDisableNoticedForUntilDay != until)
            {
                _checkpointDisableNoticedForUntilDay = until;
                OrganizedCrimeLog.Warning($"Chief protection checkpoint disable threw and will retry: {exception.Message}");
            }
            return;
        }
        if (!disabled)
        {
            if (_checkpointDisableNoticedForUntilDay != until)
            {
                _checkpointDisableNoticedForUntilDay = until;
                OrganizedCrimeLog.Warning("Chief protection checkpoint disable was refused and will retry on the next pass.");
            }
            return;
        }
        _checkpointsDisabledForUntilDay = until;
    }

    // OC-118. Reads which checkpoints are enabled and persists the bitmask on the record, so a
    // reload mid-cover restores the same set the pre-disable world showed. A failed read, a thrown
    // read, or a rejected write all return false and retry on the next pass; the disable waits.
    private bool SnapshotProtectionCoverStartCheckpoints(Release1ChiefRecord record, int until)
    {
        int mask;
        try
        {
            if (!_checkpointWorld!.TryReadEnabled(out mask))
            {
                if (_checkpointDisableNoticedForUntilDay != until)
                {
                    _checkpointDisableNoticedForUntilDay = until;
                    OrganizedCrimeLog.Warning("Chief protection checkpoint snapshot was refused and will retry on the next pass.");
                }
                return false;
            }
        }
        catch (Exception exception)
        {
            if (_checkpointDisableNoticedForUntilDay != until)
            {
                _checkpointDisableNoticedForUntilDay = until;
                OrganizedCrimeLog.Warning($"Chief protection checkpoint snapshot threw and will retry: {exception.Message}");
            }
            return false;
        }
        var snapshot = _story.TrySetChiefRecord(record with
        {
            ProtectionCoverStartCheckpoints = mask & Release1ProtectionCheckpoints.MaskAll,
            Revision = record.Revision + 1
        });
        if (!snapshot.Accepted)
        {
            OrganizedCrimeLog.Warning($"Chief protection checkpoint snapshot write was rejected: {snapshot.RejectReason}: {snapshot.Message}");
            return false;
        }
        return true;
    }

    // OC-118. The lapse end of the snapshot: with cover gone, re-enables exactly the locations
    // that were on at cover start and read off now, then consumes the snapshot so this runs once.
    // The fresh read before every attempt bounds the work: a location the schedule already turned
    // back on is left alone, and a rejected clear-write retries harmlessly, re-reading first. An
    // unreadable world skips the restore but still consumes the snapshot, since the native
    // schedule re-enables its own windows regardless and a stale snapshot must never leak into a
    // future cover period.
    private void ReconcileProtectionCheckpointRestore(Release1ChiefRecord record)
    {
        if (record.ProtectionCoverStartCheckpoints is not { } snapshot) return;
        var pending = snapshot & Release1ProtectionCheckpoints.MaskAll;
        if (_checkpointWorld is not null)
        {
            try
            {
                if (_checkpointWorld.TryReadEnabled(out var current))
                    pending &= ~current;
                else
                    pending = 0;
            }
            catch
            {
                pending = 0;
            }
            if (pending != 0)
            {
                try
                {
                    if (!_checkpointWorld.TryRestoreEnabled(pending))
                        OrganizedCrimeLog.Warning("Chief protection checkpoint restore was refused; the native schedule still re-enables its own windows.");
                }
                catch (Exception exception)
                {
                    OrganizedCrimeLog.Warning($"Chief protection checkpoint restore threw; the native schedule still re-enables its own windows: {exception.Message}");
                }
            }
        }
        var cleared = _story.TrySetChiefRecord(record with { ProtectionCoverStartCheckpoints = null, Revision = record.Revision + 1 });
        if (!cleared.Accepted)
            OrganizedCrimeLog.Warning($"Chief protection checkpoint snapshot clear was rejected: {cleared.RejectReason}: {cleared.Message}");
        else
            OrganizedCrimeLog.Receipt("Chief Campbell protection restored its cover start checkpoints.");
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
    // OC-75: the daily late fee. Runs after the event drain on the same pass, on the fresh
    // record, so a fee can never go stale against an event the drain just wrote. A demand in
    // Paying never reaches here with work to do (ContinuePayment returns first), and the ledger
    // refuses every non-unpaid record anyway. An unreadable or negative clock skips the fee.
    private void ReconcileLateFee(double? minutes)
    {
        if (minutes is not { } read) return;
        var today = (int)Math.Floor(read / 1440d);
        if (today < 0) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        var decision = Release1ChiefLedger.ChargeLateFee(record, today);
        if (decision.Next.Revision == record.Revision) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
        {
            OrganizedCrimeLog.Warning($"Chief late fee write was rejected: {result.RejectReason}: {result.Message}");
            return;
        }
        if (decision.Action == Release1ChiefAction.ChargeLateFee)
            OrganizedCrimeLog.Receipt($"Chief Campbell late fee charged: round {decision.Next.DemandRound}, {decision.Next.Demand} in cash.");
    }

    /// <summary>
    /// OC-74. Stamps an engaged, unstamped lockdown at the first readable minute (older saves
    /// and engages on an unreadable clock), then warns once the lockdown has run a full day
    /// unpaid. The presentation plan sends the warning text from the counter this records.
    /// </summary>
    private void ReconcileForcedWantedWarning(double? minutes)
    {
        if (minutes is not { } now || double.IsNaN(now) || double.IsInfinity(now) || now < 0) return;
        var record = _story.State?.ChiefRecord;
        if (record is null || !record.LockdownEngaged) return;
        if (record.LockdownEngagedAtMinutes is null)
        {
            var stamped = Release1ChiefLedger.StampLockdownEngaged(record, now);
            if (stamped.Next.Revision == record.Revision) return;
            var stampResult = _story.TrySetChiefRecord(stamped.Next);
            if (!stampResult.Accepted)
            {
                OrganizedCrimeLog.Warning($"Chief lockdown stamp write was rejected: {stampResult.RejectReason}: {stampResult.Message}");
                return;
            }
            record = stamped.Next;
        }
        var decision = Release1ChiefLedger.WarnForcedWanted(record, now);
        if (decision.Action != Release1ChiefAction.WarnForcedWanted) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
            OrganizedCrimeLog.Warning($"Chief forced-Wanted warning write was rejected: {result.RejectReason}: {result.Message}");
        else
            OrganizedCrimeLog.Receipt($"Chief Campbell sent the forced-Wanted warning for the day-old lockdown (warning {decision.Next.ForcedWantedWarnings}).");
    }

    /// <summary>
    /// OC-74 amendment: the every-tick pursuit hold. Runs ahead of the throttle: while the
    /// lockdown is armed and the player is wanted, the sighted timer is clamped below the
    /// search time so Wanted never times out. Writes only when the clamp changes the value;
    /// never touches the pursuit level. A failed read or write warns once per session.
    /// </summary>
    private void HoldForcedWanted(double? minutes)
    {
        if (_disposed || _pursuitHold is null) return;
        if (minutes is not { } now || double.IsNaN(now) || double.IsInfinity(now) || now < 0) return;
        var record = _story.State?.ChiefRecord;
        if (record is null) return;
        bool isHost;
        try { isHost = _isAuthoritativeHost?.Invoke() == true; }
        catch { return; }
        if (!isHost) return;
        if (!Release1ChiefLedger.IsForcedWantedArmed(record, now, IsProtected())) return;
        int level;
        float since;
        float search;
        try
        {
            if (!_pursuitHold.TryRead(out level, out since, out search))
            {
                WarnHoldOnce("read");
                return;
            }
        }
        catch (Exception exception)
        {
            WarnHoldOnce("read", exception);
            return;
        }
        if (level < Chapter3PursuitLevels.Arresting) return;
        var clamp = Release1ChiefLedger.ClampTimeSinceSighted(since, search);
        if (float.IsNaN(since) || clamp == since) return;
        try
        {
            if (!_pursuitHold.TryWriteTimeSinceSighted(clamp))
                WarnHoldOnce("write");
        }
        catch (Exception exception)
        {
            WarnHoldOnce("write", exception);
        }
    }

    private void WarnHoldOnce(string what, Exception? exception = null)
    {
        _forcedWantedHoldLog.Report(ForcedWantedHoldKey, exception is null
            ? $"Chief forced-Wanted hold {what} failed; leaving the pursuit alone."
            : $"Chief forced-Wanted hold {what} threw {exception.GetType().Name}; leaving the pursuit alone.");
    }

    /// <summary>
    /// OC-74. Fires forced Wanted: armed, plus host, curfew active, outdoors, pursuit None,
    /// and not arrested. Sets Arresting, reads it back, then sends one two-officer dispatch
    /// through the OC-41 adapter. Every fire records once the set reads back, whether or not
    /// the dispatch succeeded; a set that does not read back retries on the next pass.
    /// </summary>
    private void ReconcileForcedWanted(double? minutes)
    {
        if (_isAuthoritativeHost is null || _exposureReader is null || _curfewReader is null ||
            _wantedWriter is null || _forcedWantedDispatch is null) return;
        if (minutes is not { } now || double.IsNaN(now) || double.IsInfinity(now) || now < 0) return;
        var record = _story.State?.ChiefRecord;
        if (record is null || !record.LockdownEngaged) return;
        var today = Today(now);
        if (today is null || Release1ChiefLedger.IsProtected(record, today.Value)) return;
        bool isHost;
        try { isHost = _isAuthoritativeHost(); }
        catch { return; }
        if (!isHost) return;
        if (!_curfewReader.TryReadActive(out var curfewActive) || !curfewActive) return;
        if (!_exposureReader.TryRead(out var exposure)) return;
        var night = Release1ChiefLedger.ForcedWantedNightKey(now);
        var trigger = new Chapter3ForcedWantedTrigger(true, true, exposure.Outdoors, exposure.PursuitLevel, night, exposure.Arrested);
        if (!Release1ChiefLedger.ShouldFireForcedWanted(record, now, trigger, Release1ChiefLedger.IsProtected(record, today.Value))) return;
        if (record.LockdownEngagedAtMinutes is not { } engagedAt) return; // Unreachable: the fire proves the stamp.
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        if (!_epochs(out var sessionEpoch, out var loadEpoch)) return;

        int readBack;
        try
        {
            if (!_wantedWriter.TrySetArresting(out readBack) || readBack != Chapter3PursuitLevels.Arresting) return;
        }
        catch (Exception exception)
        {
            OrganizedCrimeLog.Warning($"Chief Campbell forced-Wanted set failed: {exception.GetType().Name}.");
            return;
        }
        OrganizedCrimeLog.Receipt($"Chief Campbell forced Wanted: the player is Arresting for fire {record.ForcedWantedFires + 1}.");

        var correlationId = $"chief-forced-wanted-{(long)engagedAt}-{record.ForcedWantedFires + 1}";
        NativeLawResponseResult? dispatch;
        try
        {
            if (!_forcedWantedDispatch.TryDispatch(context.PlayerId, sessionEpoch, loadEpoch, correlationId, out dispatch))
                dispatch = null;
        }
        catch (Exception exception)
        {
            OrganizedCrimeLog.Warning($"Chief Campbell forced-Wanted dispatch threw: {exception.GetType().Name}.");
            dispatch = null;
        }
        var accepted = dispatch?.State == NativeLawResponseResultState.Accepted;
        if (accepted)
            OrganizedCrimeLog.Receipt($"Chief Campbell forced Wanted: dispatch accepted for {correlationId}.");
        else if (dispatch?.State == NativeLawResponseResultState.UnavailableCapacity)
            OrganizedCrimeLog.Warning($"Chief Campbell forced Wanted: pool short, no dispatch for {correlationId} ({dispatch.Reason}).");
        else
            OrganizedCrimeLog.Warning($"Chief Campbell forced Wanted: dispatch refused for {correlationId} ({dispatch?.Reason ?? "the dispatch request could not be built"}).");

        var decision = Release1ChiefLedger.RecordForcedWantedFire(record, night, accepted);
        if (decision.Action != Release1ChiefAction.FireForcedWanted) return;
        var result = _story.TrySetChiefRecord(decision.Next);
        if (!result.Accepted)
            OrganizedCrimeLog.Warning($"Chief forced-Wanted fire write was rejected: {result.RejectReason}: {result.Message}");
    }

    private void ReconcileLockdown(Release1ChiefRecord? record, LocalPressureState ledger)
    {
        if (_qaLockdown == false || (_qaLockdown == true && record?.LockdownEngaged == true && !IsProtected())) return;
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
        _reportedHolds.Report(message, $"Chief Campbell lockdown gate is holding: {message}");
    }
}
