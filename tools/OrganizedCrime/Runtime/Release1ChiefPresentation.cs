using System.Globalization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 spec decision 24. Pure projection from the Chief's own record to the native presentation: at
/// most one decision prompt, and every desired message his record has ever earned. Never touches
/// Unity or S1API, and never mutates its input. The projector, not this planner, dedupes against
/// <see cref="Release1StoryState.PresentationReceipts"/>, so a message whose receipt already exists is
/// still returned here.
/// </summary>
public static class Release1ChiefPresentation
{
    // OC-118. today is the whole game day the protection button's cap and Buy-vs-Extend label
    // read; null (an unreadable clock, or a caller that predates protection) shows the button
    // whenever the record otherwise earns it, and the service still refuses over-cap taps safely.
    // playerDay maps an absolute cover end day to the day number the player reads (Chapter 3's
    // chapter days, the frame of Nell's "by day N" and the protection quest line); null or an
    // unmapped day falls back to the absolute day.
    public static Release1PresentationPlan BuildPlan(Release1StoryState? state, int? today = null, Func<int, int?>? playerDay = null)
    {
        var record = state?.ChiefRecord;
        if (record is null) return Release1PresentationPlan.Empty;
        int Shown(int absoluteDay)
        {
            try { return playerDay?.Invoke(absoluteDay) ?? absoluteDay; }
            catch { return absoluteDay; }
        }

        var playerId = state!.PlayerId;
        var messages = new List<Release1DesiredMessage>();

        // OC-73 review fix. DemandOpen used to queue DemandText as a passive message as well as
        // using it for the decision prompt below, so the boundary's own TrySetDecision (which sends
        // the prompt as a message on the caller's behalf when nothing is bound yet) delivered it a
        // second time. The shipped missions never do this: Release1PresentationPlan's own
        // BuildSmallCourtesy comment says so verbatim ("The offer body is the review decision's
        // prompt; it is not also queued as a passive message"). DemandOpen now follows that same
        // convention and emits nothing here; the prompt below is the only place DemandText is sent.
        if (record.State == Release1ChiefState.Settled)
            messages.Add(Message(playerId, record, PaidReceipt(record.DemandRound, record.Revision), Release1ChiefCampbellCopy.PaidText));

        // OC-73 review fix (finding 1). The decline reply used to be its own switch case, keyed on
        // record.DemandRound alone (chief-decline-r{round}). Release1ChiefLedger.Observe's
        // Adopted-or-Settled arrest branch resets DemandRound to 1, so a decline in a later payoff
        // cycle minted the identical correlation an earlier cycle's decline already earned a receipt
        // for, and the projector silently suppressed it: the player picked "Not today" and got no
        // reply at all. DeclineRepliesSent never resets, so the reply now joins the six counter-keyed
        // lines below instead, exactly like WatchLinesSent and PaidLifts already do.
        //
        // These all carry a counter that never resets (DeclineRepliesSent, WatchLinesSent,
        // LockdownAnnouncements, PaidLifts, CooledLifts, PaymentBlockedNotices), so each already has a
        // unique receipt id for the life of the save on its own; folding record.DemandRound into their
        // correlation on top (through Message, as Settled above still correctly does) only ever hurt
        // them. DemandRound can advance or fall back to 1 while the counter itself holds still (an
        // arrest on a standing decline, or the Settled-plus-arrest ladder restart), which mints a
        // brand new correlation for a line that already has a receipt and resends it, or, for the
        // decline reply before this fix, silently drops the reply on the next cycle instead. CounterMessage
        // pins the correlation's round component to a fixed value instead, so round movement can never
        // touch these six again.
        if (record.DeclineRepliesSent >= 1)
            messages.Add(CounterMessage(playerId, DeclineReceipt(record.DeclineRepliesSent), Release1ChiefCampbellCopy.DeclineReplyText));
        if (record.WatchLinesSent >= 1)
            messages.Add(CounterMessage(playerId, WatchReceipt(record.WatchLinesSent), Release1ChiefCampbellCopy.WatchListText));
        if (record.LockdownAnnouncements >= 1)
            messages.Add(CounterMessage(playerId, LockdownReceipt(record.LockdownAnnouncements), Release1ChiefCampbellCopy.LockdownAnnouncementText));
        if (record.PaidLifts >= 1)
            messages.Add(CounterMessage(playerId, LiftPaidReceipt(record.PaidLifts), Release1ChiefCampbellCopy.LiftPaidText));
        if (record.CooledLifts >= 1)
            messages.Add(CounterMessage(playerId, LiftCooledReceipt(record.CooledLifts), Release1ChiefCampbellCopy.LiftCooledText));
        if (record.PaymentBlockedNotices >= 1)
            messages.Add(CounterMessage(playerId, PaymentBlockedReceipt(record.PaymentBlockedNotices), Release1ChiefCampbellCopy.PaymentBlockedText));
        // OC-74. The day-old-lockdown warning, on the never-resetting counter like the lines
        // above; the projector dedupes the older counts by their receipts.
        if (record.ForcedWantedWarnings >= 1)
            messages.Add(CounterMessage(playerId, ForcedWantedWarningReceipt(record.ForcedWantedWarnings), Release1ChiefCampbellCopy.ForcedWantedWarningText));
        // OC-118. The shortfall names the demand when one stands unpaid and the protection price
        // otherwise: a demand shortfall can only be set (and survives only) on an unpaid record,
        // since both prepares clear it, so a shortfall on Adopted or Settled is always a buy tap's.
        if (record.LastShortfallNoticed is { } shortfall)
            messages.Add(Message(playerId, record, ShortfallReceipt(shortfall), Release1ChiefCampbellCopy.ShortCashText(
                Release1ChiefLedger.IsUnpaid(record) ? record.Demand : Release1ChiefProtection.ProtectionWeeklyPrice)));
        if (record.LateFeesCharged >= 1 && record.LastLateFeeChargedDay is { } chargedDay)
            messages.Add(CounterMessage(playerId, LateFeeReceipt(chargedDay), Release1ChiefCampbellCopy.LateFeeText(record.Demand)));

        // OC-118. The protection cluster. Every key never resets or strictly increases (the offer
        // fires once ever; cover end days only move forward; the counters only climb), so all six
        // join the counter-keyed lines above: their correlations can never move under them.
        if (record.ProtectionOffered)
            messages.Add(CounterMessage(playerId, ProtectionOfferReceipt(), Release1ChiefCampbellCopy.ProtectionOfferText()));
        if (record.ProtectedUntilDay is { } untilDay)
            messages.Add(CounterMessage(playerId, ProtectionCoveredReceipt(untilDay), Release1ChiefCampbellCopy.ProtectionCoveredText(Shown(untilDay))));
        if (record.ProtectionWarnedForUntilDay is { } warnedDay && warnedDay == record.ProtectedUntilDay)
            messages.Add(CounterMessage(playerId, ProtectionWarningReceipt(warnedDay), Release1ChiefCampbellCopy.ProtectionWarningText()));
        if (record.ProtectionLapseNotices >= 1)
            messages.Add(CounterMessage(playerId, ProtectionLapseReceipt(record.ProtectionLapseNotices), Release1ChiefCampbellCopy.ProtectionLapseText()));
        if (record.ProtectionSettleFirstRefusals >= 1)
            messages.Add(CounterMessage(playerId, ProtectionRefusedReceipt(record.ProtectionSettleFirstRefusals), Release1ChiefCampbellCopy.ProtectionSettleFirstText()));
        if (record.ProtectionBlockedNotices >= 1)
            messages.Add(CounterMessage(playerId, ProtectionBlockedReceipt(record.ProtectionBlockedNotices), Release1ChiefCampbellCopy.ProtectionBlockedText));

        // OC-75: the demand stays payable after "Not today". DemandOpen offers Pay and
        // Decline; Declined keeps the Pay button standing on its own prompt and drops
        // Decline, which only ever appears on the first prompt. Both share the demand's
        // decision id; the option labels are what change, and the projector rebinds.
        Release1DesiredDecision? decision = record.State switch
        {
            Release1ChiefState.DemandOpen => new Release1DesiredDecision(
                DemandDecisionId(record.DemandRound),
                Copy(Release1ChiefCampbellCopy.DemandText(record.DemandRound)),
                new[]
                {
                    new Release1DecisionOption(Copy(Release1ChiefCampbellCopy.PayLabel(record.Demand)), Release1PresentationCommand.ChiefCampbellPay),
                    new Release1DecisionOption(Copy(Release1ChiefCampbellCopy.DeclineLabel), Release1PresentationCommand.ChiefCampbellDecline)
                }),
            Release1ChiefState.Declined => new Release1DesiredDecision(
                DemandDecisionId(record.DemandRound),
                Copy(Release1ChiefCampbellCopy.StandingText(record.Demand)),
                new[]
                {
                    new Release1DecisionOption(Copy(Release1ChiefCampbellCopy.PayLabel(record.Demand)), Release1PresentationCommand.ChiefCampbellPay)
                }),
            // OC-118. The standing protection button: offered and unpaid records only (a demand
            // withholds it; Paying and PayingProtection are mid-flight), hidden at the banking cap,
            // Buy before cover and Extend during it. One stable decision id; the prompt and the
            // label are what change, and the projector rebinds, the demand's own convention.
            Release1ChiefState.Adopted or Release1ChiefState.Settled when record.ProtectionOffered &&
                (today is null || Release1ChiefLedger.ProtectionRoom(record, today.Value) >= Release1ChiefProtection.CoverDaysPerPurchase) =>
                new Release1DesiredDecision(
                    ProtectionDecisionId,
                    Copy(today is { } day && Release1ChiefLedger.IsProtected(record, day) && record.ProtectedUntilDay is { } coveredUntil
                        ? Release1ChiefCampbellCopy.ProtectionCoveredText(Shown(coveredUntil))
                        : Release1ChiefCampbellCopy.ProtectionOfferText()),
                    new[]
                    {
                        new Release1DecisionOption(
                            Copy(today is { } optionDay && Release1ChiefLedger.IsProtected(record, optionDay)
                                ? Release1ChiefCampbellCopy.ExtendProtectionLabel()
                                : Release1ChiefCampbellCopy.BuyProtectionLabel()),
                            Release1PresentationCommand.ChiefCampbellBuyProtection)
                    }),
            _ => null
        };

        return new Release1PresentationPlan(messages, decision, Array.Empty<Release1DesiredQuest>());
    }

    /// <summary>
    /// The exact correlation Task 4's lockdown reconciler checks for in
    /// <see cref="Release1StoryState.PresentationReceipts"/> before it ever engages the native curfew
    /// (spec decision 18: the announcement is a precondition of engaging, never a consequence of it).
    /// </summary>
    // OC-73 review fix. Was Correlation(playerId, record.DemandRound, ...): an arrest on a standing
    // decline advances DemandRound while LockdownAnnouncements holds still, which minted a fresh
    // correlation for the exact same announcement and made HasAnnouncementReceipt (Release1ChiefService)
    // stop matching the one the projector already recorded, deferring the engage until a duplicate
    // announcement was sent and receipted. CounterCorrelation pins the round component instead, so the
    // announcement's own correlation cannot move out from under the receipt check that gates engaging.
    public static string LockdownAnnouncementCorrelation(string playerId, Release1ChiefRecord record) =>
        CounterCorrelation(playerId, LockdownReceipt(record.LockdownAnnouncements));

    private static Release1DesiredMessage Message(string playerId, Release1ChiefRecord record, string receiptId, string text) =>
        new(Correlation(playerId, record.DemandRound, receiptId), Copy(text));

    /// <summary>
    /// For the receipts (Decline, Watch, Lockdown, LiftPaid, LiftCooled, PaymentBlocked, LateFee,
    /// and OC-118's six protection lines, plus the forced-Wanted warning) whose own key never resets and so already carries the
    /// line's full identity: the round component of the correlation is fixed rather than read from
    /// the record, so a round change can never mint a fresh correlation for an already-delivered
    /// line. The fee's own key is the charged day, which only ever increases within a save, and so
    /// are the protection cover end days. See the OC-73 review fix comment on the call sites above.
    /// </summary>
    private static Release1DesiredMessage CounterMessage(string playerId, string receiptId, string text) =>
        new(CounterCorrelation(playerId, receiptId), Copy(text));

    private static string CounterCorrelation(string playerId, string receiptId) => Correlation(playerId, 1, receiptId);

    private static string Correlation(string playerId, int round, string receiptId) =>
        Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.ChiefCampbell, Math.Max(round, 1), Release1TransitionKind.MissionOffered, receiptId).Value;

    private static string DemandDecisionId(int round) => $"chief-campbell-demand-r{round}";
    private const string ProtectionDecisionId = "chief-campbell-protection";
    // OC-73 review fix (finding 1). DeclineReceipt used to be keyed on round alone
    // (chief-decline-r{round}), which reads as stable only within a single payoff cycle: the comment
    // this replaced argued a second collision was "impossible below the cap, since an arrest there
    // always advances the round". That argument only ever looked at DemandRound advancing forward; it
    // missed that Release1ChiefLedger.Observe's Adopted-or-Settled arrest branch resets DemandRound
    // back to 1 on every ladder restart, so a decline in cycle two at round 1 minted the identical
    // chief-decline-r1 correlation cycle one's decline already receipted, and the projector silently
    // dropped the reply. DeclineRepliesSent is now the key instead: it never resets (same shape as
    // WatchLinesSent, LockdownAnnouncements, PaidLifts and CooledLifts), so every decline across the
    // life of the save earns a distinct, always-delivered reply, round or cycle notwithstanding.
    private static string DeclineReceipt(int count) => $"chief-decline-{count}";
    private static string PaidReceipt(int round, long revision) => $"chief-paid-r{round}-c{revision}";
    private static string WatchReceipt(int count) => $"chief-watch-{count}";
    private static string LockdownReceipt(int count) => $"chief-lockdown-{count}";
    private static string LiftPaidReceipt(int count) => $"chief-lift-paid-{count}";
    private static string LiftCooledReceipt(int count) => $"chief-lift-cooled-{count}";
    private static string PaymentBlockedReceipt(int count) => $"chief-payment-blocked-{count}";
    private static string ForcedWantedWarningReceipt(int count) => $"chief-forced-wanted-warning-{count}";
    private static string ShortfallReceipt(double shortfall) => $"chief-short-{((long)Math.Round(shortfall)).ToString(CultureInfo.InvariantCulture)}";
    private static string LateFeeReceipt(int day) => $"chief-latefee-{day}";
    private static string ProtectionOfferReceipt() => "chief-protection-offer";
    private static string ProtectionCoveredReceipt(int untilDay) => $"chief-protection-covered-{untilDay}";
    private static string ProtectionWarningReceipt(int untilDay) => $"chief-protection-warning-{untilDay}";
    private static string ProtectionLapseReceipt(int count) => $"chief-protection-lapsed-{count}";
    private static string ProtectionRefusedReceipt(int count) => $"chief-protection-refused-{count}";
    private static string ProtectionBlockedReceipt(int count) => $"chief-protection-blocked-{count}";

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}
