using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>What the pass should do with the world after a ledger decision. Effects live in the service.</summary>
public enum Release1ChiefAction { None, OpenDemand, ReplyDeclined, RecordWatchLine, AnnounceLockdown, LiftCooled, LiftPaid, ChargeLateFee, OfferProtection, RefuseProtection, ApplyProtection, WarnProtectionEnding, NoticeProtectionLapse, WarnForcedWanted, FireForcedWanted }

public sealed record Release1ChiefDecision(Release1ChiefRecord Next, Release1ChiefAction Action);

/// <summary>
/// OC-73's pure state machine: the whole Transitions table with no world, no story runtime, no clock
/// and no state of its own. Every method returns a new record whose Revision is exactly one past its
/// input's, so <see cref="Release1StoryRuntimeService.TrySetChiefRecord"/>'s advance by one rule is
/// satisfied by construction and a stale pass can never win.
/// </summary>
public static class Release1ChiefLedger
{
    public static bool IsUnpaid(Release1ChiefRecord record) =>
        record.State is Release1ChiefState.DemandOpen or Release1ChiefState.Declined;

    // OC-74. The forced-Wanted clock and trigger. The warning age is a full day in canonical
    // minutes; the night anchor is the 21:00 curfew start (S1API CurfewManager.CurfewStartTime
    // 2100, pinned by the boundary tests) in minutes past midnight.
    public const double ForcedWantedWarningAgeMinutes = 1440d;
    public const int ForcedWantedCurfewStartMinutes = 1260;

    /// <summary>
    /// OC-74 owner run: the lockdown holds curfew on all day, so a night boundary was the wrong
    /// guard (a morning warning held the fire back until 21:00). A fire needs this many game
    /// minutes after the warning instead, so the warning always lands first.
    /// </summary>
    public const double ForcedWantedWarningGraceMinutes = 60d;

    /// <summary>
    /// OC-74 amendment: how far below the search time the pursuit hold clamps the sighted
    /// timer, so the timeout never lands while an armed lockdown holds.
    /// </summary>
    public const float ForcedWantedHoldMarginSeconds = 10f;

    /// <summary>
    /// OC-74 amendment: the clamped sighted timer: the lower of now and search time minus
    /// the hold margin, floored at zero. A non-finite input is returned unchanged.
    /// </summary>
    public static float ClampTimeSinceSighted(float timeSinceSighted, float searchTime)
    {
        if (!float.IsFinite(timeSinceSighted) || !float.IsFinite(searchTime))
            return timeSinceSighted;
        return Math.Min(timeSinceSighted, Math.Max(0f, searchTime - ForcedWantedHoldMarginSeconds));
    }

    /// <summary>
    /// OC-74 amendment: whether the lockdown's forced Wanted is armed: engaged, unpaid,
    /// unprotected, stamped a full day ago, warned, and an hour past the warning (a null
    /// warned time on a counter-warned record does not block). Any NaN, infinite, or
    /// negative clock reads as unarmed. Shared by the fire predicate and the pursuit hold.
    /// </summary>
    public static bool IsForcedWantedArmed(Release1ChiefRecord record, double nowMinutes, bool isProtected)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (isProtected) return false;
        if (!record.LockdownEngaged || !IsUnpaid(record) || !IsForcedWantedWarned(record)) return false;
        if (record.LockdownEngagedAtMinutes is not { } stamped) return false;
        if (double.IsNaN(nowMinutes) || double.IsInfinity(nowMinutes) || nowMinutes < 0) return false;
        if (nowMinutes - stamped < ForcedWantedWarningAgeMinutes) return false;
        return record.ForcedWantedWarnedAtMinutes is not { } warnedAt || nowMinutes - warnedAt >= ForcedWantedWarningGraceMinutes;
    }

    /// <summary>
    /// OC-74. The curfew night containing these canonical minutes, as the game day its window
    /// started, so a window crossing midnight stays one night.
    /// </summary>
    public static int ForcedWantedNightKey(double minutes) =>
        (int)Math.Floor((minutes - ForcedWantedCurfewStartMinutes) / 1440d);

    /// <summary>
    /// OC-74. Whether the current lockdown was already warned: its warning time is recorded
    /// (both lifts clear it, so the next lockdown re-arms by itself). OC-74 owner run: this used
    /// to compare warnings against lifts, so a save with earlier lifts sent the warning once per
    /// lift before it caught up. The counter form stays only for records warned before the
    /// timestamp existed.
    /// </summary>
    public static bool IsForcedWantedWarned(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.ForcedWantedWarnedAtMinutes is not null ||
            record.ForcedWantedWarnings > record.PaidLifts + record.CooledLifts;
    }

    /// <summary>
    /// OC-74. Stamps an engaged, unstamped lockdown at now: the load backfill for older saves
    /// and engages on an unreadable clock. Silent (no action): it only starts the 24 h clock.
    /// </summary>
    public static Release1ChiefDecision StampLockdownEngaged(Release1ChiefRecord record, double nowMinutes)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.LockdownEngaged || record.LockdownEngagedAtMinutes is not null ||
            double.IsNaN(nowMinutes) || double.IsInfinity(nowMinutes) || nowMinutes < 0)
            return new(record, Release1ChiefAction.None);
        return new(record with { LockdownEngagedAtMinutes = nowMinutes, Revision = record.Revision + 1 }, Release1ChiefAction.None);
    }

    /// <summary>
    /// OC-74. Warns once per lockdown when it has run a full day unpaid. The never-resetting
    /// counter is the receipt key; the presentation plan sends the text from it.
    /// </summary>
    public static Release1ChiefDecision WarnForcedWanted(Release1ChiefRecord record, double nowMinutes)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.LockdownEngaged || !IsUnpaid(record) || IsForcedWantedWarned(record) ||
            record.LockdownEngagedAtMinutes is not { } stamped ||
            double.IsNaN(nowMinutes) || double.IsInfinity(nowMinutes) || nowMinutes - stamped < ForcedWantedWarningAgeMinutes)
            return new(record, Release1ChiefAction.None);
        return new(record with
        {
            ForcedWantedWarnings = record.ForcedWantedWarnings + 1,
            ForcedWantedWarnedAtMinutes = nowMinutes,
            Revision = record.Revision + 1
        }, Release1ChiefAction.WarnForcedWanted);
    }

    /// <summary>
    /// OC-74. Records a fire: the night marker plus the fire and exactly one outcome counter.
    /// OC-74 amendment: every fire records; the night marker is information only.
    /// </summary>
    public static Release1ChiefDecision RecordForcedWantedFire(Release1ChiefRecord record, int night, bool dispatched)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.LockdownEngaged)
            return new(record, Release1ChiefAction.None);
        return new(record with
        {
            LastForcedWantedNight = night,
            ForcedWantedFires = record.ForcedWantedFires + 1,
            ForcedWantedDispatches = record.ForcedWantedDispatches + (dispatched ? 1 : 0),
            ForcedWantedDispatchRefusals = record.ForcedWantedDispatchRefusals + (dispatched ? 0 : 1),
            Revision = record.Revision + 1
        }, Release1ChiefAction.FireForcedWanted);
    }

    /// <summary>
    /// OC-74. The pure fire predicate: armed, plus host, curfew active, outdoors, pursuit
    /// None, and not arrested (null reads as arrested). Any unreadable or out-of-range
    /// input reads as no fire. OC-74 amendment: the per-night limit is gone, so a player
    /// back to None re-fires on the next pass.
    /// </summary>
    public static bool ShouldFireForcedWanted(Release1ChiefRecord record, double nowMinutes, Chapter3ForcedWantedTrigger trigger, bool isProtected)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(trigger);
        return IsForcedWantedArmed(record, nowMinutes, isProtected) &&
            trigger.IsHost &&
            trigger.CurfewActive &&
            trigger.Outdoors &&
            trigger.PursuitLevel == Chapter3PursuitLevels.None &&
            trigger.Arrested == false;
    }

    /// <summary>
    /// OC-118. Whether the record's protection covers today: the end day is set and today is
    /// strictly below it. A negative or unreadable clock reads as unprotected, never covered.
    /// </summary>
    public static bool IsProtected(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        return today >= 0 && record.ProtectedUntilDay is { } until && today < until;
    }

    /// <summary>
    /// OC-118. Days still bankable under the cap from today: the full 14 while unprotected or
    /// lapsed, shrinking as cover stacks up. The service shows the button only at seven or more,
    /// so a purchase can never push the end day past the cap; at six or below the button hides
    /// and a stale tap is refused outright.
    /// </summary>
    public static int ProtectionRoom(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        var start = Math.Max(today, record.ProtectedUntilDay ?? today);
        return Release1ChiefProtection.MaxBankedDays - (start - today);
    }

    public static Release1ChiefDecision Observe(Release1ChiefRecord record, Release1ChiefObservedEvent observed)
    {
        ArgumentNullException.ThrowIfNull(record);
        var next = record with { Revision = record.Revision + 1 };

        if (observed == Release1ChiefObservedEvent.ArrestObserved)
        {
            return record.State switch
            {
                // OC-73 review fix. A settled record used to retain its AcceptedLogicalCorrelations
                // and NativeEffectIds across this reset (spec decision: "history retained"), but both
                // collections are validated against DemandRound, which this same branch resets to 1.
                // A payoff settled at round 2 or 3 left a correlation/effect whose Attempt (2 or 3)
                // permanently exceeded the reset round, so Release1ChiefRecord.Validate threw forever
                // after and the demand could never reopen. Clearing on this exact transition is safe:
                // BeginPaying/PrepareChiefEffect only ever add an entry while paying the round that is
                // still open, so nothing outside a closed cycle is lost, and Release1ChiefEffect's
                // ids already carry the record's own Revision (never resets) so a later cycle can
                // never collide with the cleared one even though DemandRound repeats 1, 2, 3.
                Release1ChiefState.Adopted or Release1ChiefState.Settled =>
                    new(next with
                    {
                        State = Release1ChiefState.DemandOpen,
                        DemandRound = 1,
                        LastShortfallNoticed = null,
                        LastLateFeeChargedDay = null,
                        LateFeesCharged = 0,
                        AcceptedLogicalCorrelations = Array.Empty<string>(),
                        NativeEffectIds = Array.Empty<string>()
                    }, Release1ChiefAction.OpenDemand),
                // Spec decision 12: the price advances only when an arrest lands on a standing decline.
                Release1ChiefState.Declined =>
                    new(next with
                    {
                        State = Release1ChiefState.DemandOpen,
                        DemandRound = Math.Min(record.DemandRound + 1, Release1ChiefRecord.MaximumDemandRound),
                        LastShortfallNoticed = null
                    }, Release1ChiefAction.OpenDemand),
                _ => new(record, Release1ChiefAction.None)
            };
        }

        // A crossing observed while still Adopted opens the demand in the same decision, so no
        // message can ever carry a round zero correlation, which would not parse.
        if (record.State == Release1ChiefState.Adopted)
            next = next with { State = Release1ChiefState.DemandOpen, DemandRound = 1 };

        if (!IsUnpaid(next)) return new(record, Release1ChiefAction.None);

        return observed == Release1ChiefObservedEvent.RoseToWatched
            ? new(next with { WatchLinesSent = next.WatchLinesSent + 1 }, Release1ChiefAction.RecordWatchLine)
            : new(next with { LockdownAnnouncements = next.LockdownAnnouncements + 1 }, Release1ChiefAction.AnnounceLockdown);
    }

    // OC-75: the daily late fee. Starting the in-game day after a demand opens, each
    // in-game day the demand is still unpaid raises DemandRound by one, to the same cap
    // the arrest steps push. The marker is the whole schedule: an unset marker is
    // established at today without charging (so a fresh demand and an older save both
    // start clean), and any day at or behind the marker is already charged. At the cap,
    // or off unpaid, this is a no-op.
    public static Release1ChiefDecision ChargeLateFee(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsUnpaid(record)) return new(record, Release1ChiefAction.None);
        if (record.LastLateFeeChargedDay is not { } chargedDay)
            return new(record with { LastLateFeeChargedDay = today, Revision = record.Revision + 1 }, Release1ChiefAction.None);
        if (today <= chargedDay || record.DemandRound >= Release1ChiefRecord.MaximumDemandRound)
            return new(record, Release1ChiefAction.None);
        return new(record with
        {
            DemandRound = record.DemandRound + 1,
            LastLateFeeChargedDay = today,
            LateFeesCharged = record.LateFeesCharged + 1,
            Revision = record.Revision + 1
        }, Release1ChiefAction.ChargeLateFee);
    }

    // OC-73 review fix (finding 1). DeclineRepliesSent never resets (like WatchLinesSent and
    // PaidLifts), so the reply's own correlation can be pinned to this counter instead of DemandRound:
    // DemandRound resets to 1 on the Adopted-or-Settled arrest branch above, which used to collide a
    // later cycle's decline reply with an earlier cycle's already-delivered one at the same round.
    public static Release1ChiefDecision Decline(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.State != Release1ChiefState.DemandOpen
            ? new(record, Release1ChiefAction.None)
            : new(record with
            {
                State = Release1ChiefState.Declined,
                DeclineRepliesSent = record.DeclineRepliesSent + 1,
                Revision = record.Revision + 1
            }, Release1ChiefAction.ReplyDeclined);
    }

    /// <summary>
    /// OC-73 review fix. RunPay used to leave a Paying record frozen forever the moment its debit came
    /// back non-Succeeded: the prepared effect never reaches Applied, ContinuePayment only ever looks
    /// at Applied effects, and nothing else ever revisits a Paying record. A non-Succeeded debit is the
    /// one failure mode a caller can know, synchronously, moved no cash (TryDebitCashBalance's own
    /// contract only mutates the wallet on Succeeded), so it is safe to hand the demand straight back
    /// rather than merely retry: the player can simply press Pay again. Bumping Revision here, exactly
    /// like every other transition, also strips the orphaned Prepared effect of its authorization
    /// (Release1StoryRuntimeService.IsCurrentEffectAuthorization requires both the matching revision
    /// and a Paying or Settled state), so it can never be marked applied or committed later, even if a
    /// fresh attempt at the same round later succeeds and leaves two prepared effects on the journal.
    /// A debit that did succeed must never call this: see
    /// <see cref="Release1ChiefService"/>'s own RunPay and ContinuePayment for that retry path instead.
    /// </summary>
    public static Release1ChiefDecision AbandonUnappliedPayment(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.State != Release1ChiefState.Paying
            ? new(record, Release1ChiefAction.None)
            : new(record with { State = Release1ChiefState.DemandOpen, Revision = record.Revision + 1 }, Release1ChiefAction.None);
    }

    public static Release1ChiefDecision Settle(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.State != Release1ChiefState.Paying) return new(record, Release1ChiefAction.None);
        var next = record with { State = Release1ChiefState.Settled, Revision = record.Revision + 1 };
        return record.LockdownEngaged
            ? new(next with { LockdownEngaged = false, PaidLifts = record.PaidLifts + 1, LockdownEngagedAtMinutes = null, LastForcedWantedNight = null, ForcedWantedWarnedAtMinutes = null }, Release1ChiefAction.LiftPaid)
            : new(next, Release1ChiefAction.None);
    }

    public static Release1ChiefDecision CoolBelowWatched(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !record.LockdownEngaged
            ? new(record, Release1ChiefAction.None)
            : new(record with { LockdownEngaged = false, CooledLifts = record.CooledLifts + 1, Revision = record.Revision + 1, LockdownEngagedAtMinutes = null, LastForcedWantedNight = null, ForcedWantedWarnedAtMinutes = null }, Release1ChiefAction.LiftCooled);
    }

    // OC-118. The offer fires once ever, on the first pass the Chapter 3 signal reads three
    // successful jobs (already-qualified saves included: the flag, not the crossing, is the gate).
    public static Release1ChiefDecision OfferProtection(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.ProtectionOffered
            ? new(record, Release1ChiefAction.None)
            : new(record with { ProtectionOffered = true, Revision = record.Revision + 1 }, Release1ChiefAction.OfferProtection);
    }

    // OC-118. A buy tap that arrives while a demand stands unpaid (a stale tap: the button is
    // withheld then) is refused and counted, never charged. Each refusal carries its own counter
    // value, so the text is sent once per tap exactly like the other once-only lines.
    public static Release1ChiefDecision RefuseProtectionWhileUnpaid(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record with
        {
            ProtectionSettleFirstRefusals = record.ProtectionSettleFirstRefusals + 1,
            Revision = record.Revision + 1
        }, Release1ChiefAction.RefuseProtection);
    }

    // OC-118. Applies one paid purchase: seven days from today, or from the current end day when
    // already covered. The banking cap is enforced before the debit, by the service refusing the
    // tap, never here: by the time this runs the money has already moved and the cover is owed.
    public static Release1ChiefDecision ApplyProtectionPurchase(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        var start = Math.Max(today, record.ProtectedUntilDay ?? today);
        return new(record with
        {
            ProtectedUntilDay = start + Release1ChiefProtection.CoverDaysPerPurchase,
            Revision = record.Revision + 1
        }, Release1ChiefAction.ApplyProtection);
    }

    // OC-118. The day-before warning, once per cover period: the marker names the end day warned
    // for, and an extension moves the end day strictly forward, so the warning re-arms by itself.
    public static Release1ChiefDecision WarnProtectionEnding(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ProtectedUntilDay is not { } until || today != until - 1 || record.ProtectionWarnedForUntilDay == until)
            return new(record, Release1ChiefAction.None);
        return new(record with { ProtectionWarnedForUntilDay = until, Revision = record.Revision + 1 }, Release1ChiefAction.WarnProtectionEnding);
    }

    // OC-118. The lapse: no penalty and no demand, just the cover cleared and the farewell
    // counted. The cleared end day is itself the once-only guard: a second call finds nothing.
    public static Release1ChiefDecision NoticeProtectionLapse(Release1ChiefRecord record, int today)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ProtectedUntilDay is not { } until || today < until)
            return new(record, Release1ChiefAction.None);
        return new(record with
        {
            ProtectedUntilDay = null,
            ProtectionWarnedForUntilDay = null,
            ProtectionLapseNotices = record.ProtectionLapseNotices + 1,
            Revision = record.Revision + 1
        }, Release1ChiefAction.NoticeProtectionLapse);
    }

    // OC-118. The protection mirror of AbandonUnappliedPayment: a debit known, synchronously, to
    // have moved no cash (Unavailable alone) hands the record straight back for a fresh tap. The
    // prior state is derived, not stored: round zero is exactly Adopted, anything above it the
    // Settled a buy requires, since protection can only ever be bought off an unpaid record.
    public static Release1ChiefDecision AbandonUnappliedProtectionPayment(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.State != Release1ChiefState.PayingProtection
            ? new(record, Release1ChiefAction.None)
            : new(record with
            {
                State = record.DemandRound == 0 ? Release1ChiefState.Adopted : Release1ChiefState.Settled,
                Revision = record.Revision + 1
            }, Release1ChiefAction.None);
    }
}

/// <summary>The one native effect Chief Campbell ever prepares.</summary>
public static class Release1ChiefEffect
{
    public const string Scope = "chief-campbell";
    public const string WalletSource = "player-wallet";
    public const string Kind = "CashTransfer";
    public const string EffectIdPrefix = "chief-campbell-cash-v1-";

    /// <summary>
    /// OC-73 review fix. round alone used to reuse the identical id every time a payoff cycle
    /// revisited the same round (round resets to 1 after Settled plus an arrest, but the native
    /// effect journal is append-only and keyed by this id forever), so a second payoff at the same
    /// round found the first cycle's already-Committed entry and was rejected as superseded. revision
    /// is the record's own Revision, which never resets (advances by exactly one on every ledger
    /// transition, the same non-resetting guarantee a mission's own attempt counter gives missions),
    /// so this id is unique for the life of the save even though round repeats 1, 2, 3 forever.
    /// </summary>
    public static string EffectId(int round, long revision) => $"{EffectIdPrefix}r{round}-c{revision}";

    /// <summary>
    /// OC-77: whether a journal entry is one of the Chief's own cash effects. The commit sweep
    /// matches on this, not on the id prefix alone, so a mission effect can never be swept up
    /// even if its id ever shared the prefix.
    /// </summary>
    public static bool IsCashEffect(Release1NativeEffectJournalEntry effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return effect.EffectId.StartsWith(EffectIdPrefix, StringComparison.Ordinal) &&
            string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal);
    }
    public static string NativeReceiptId(int round, long revision) => $"chief-campbell-cash-native-v1-r{round}-c{revision}";
}
