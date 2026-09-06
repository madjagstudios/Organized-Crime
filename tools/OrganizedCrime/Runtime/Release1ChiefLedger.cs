using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>What the pass should do with the world after a ledger decision. Effects live in the service.</summary>
public enum Release1ChiefAction { None, OpenDemand, ReplyDeclined, RecordWatchLine, AnnounceLockdown, LiftCooled, LiftPaid }

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
            ? new(next with { LockdownEngaged = false, PaidLifts = record.PaidLifts + 1 }, Release1ChiefAction.LiftPaid)
            : new(next, Release1ChiefAction.None);
    }

    public static Release1ChiefDecision CoolBelowWatched(Release1ChiefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !record.LockdownEngaged
            ? new(record, Release1ChiefAction.None)
            : new(record with { LockdownEngaged = false, CooledLifts = record.CooledLifts + 1, Revision = record.Revision + 1 }, Release1ChiefAction.LiftCooled);
    }
}

/// <summary>The one native effect Chief Campbell ever prepares.</summary>
public static class Release1ChiefEffect
{
    public const string Scope = "chief-campbell";
    public const string WalletSource = "player-wallet";
    public const string Kind = "CashTransfer";

    /// <summary>
    /// OC-73 review fix. round alone used to reuse the identical id every time a payoff cycle
    /// revisited the same round (round resets to 1 after Settled plus an arrest, but the native
    /// effect journal is append-only and keyed by this id forever), so a second payoff at the same
    /// round found the first cycle's already-Committed entry and was rejected as superseded. revision
    /// is the record's own Revision, which never resets (advances by exactly one on every ledger
    /// transition, the same non-resetting guarantee a mission's own attempt counter gives missions),
    /// so this id is unique for the life of the save even though round repeats 1, 2, 3 forever.
    /// </summary>
    public static string EffectId(int round, long revision) => $"chief-campbell-cash-v1-r{round}-c{revision}";
    public static string NativeReceiptId(int round, long revision) => $"chief-campbell-cash-native-v1-r{round}-c{revision}";
}
