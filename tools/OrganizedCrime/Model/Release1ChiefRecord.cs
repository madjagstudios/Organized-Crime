using System.Collections.Immutable;

namespace OrganizedCrime.Model;

/// <summary>Where Chief Campbell's account with this player stands.</summary>
public enum Release1ChiefState { Adopted, DemandOpen, Paying, Settled, Declined, PayingProtection }

/// <summary>
/// OC-73 spec decision 5. The one story record Chief Campbell owns. He is not a mission, so this is
/// not a <see cref="Release1MissionRecord"/> and carries no mission field: no Standing, no outcome, no
/// deadline, no terms, no recovery mode. It hangs off <see cref="Release1StoryState"/> as a single
/// nullable value, and its absence is exactly the pre adoption state spec decision 7 wants, which is
/// why a v1 to v10 document normalizes to null rather than to a synthesized record.
///
/// The counters are what make "once per crossing" durable: each desired message's correlation
/// carries its counter value, so a receipt already recorded can never be re-sent and a genuinely new
/// crossing always gets a fresh correlation. DeclineRepliesSent and PaymentBlockedNotices are the
/// same shape (OC-73 review fix, finding 1 and finding 2): the decline reply used to be keyed on
/// DemandRound alone, which collides with itself the moment a later payoff cycle restarts the round
/// at 1, so it now carries its own non-resetting counter exactly like the other five.
/// </summary>
public sealed record Release1ChiefRecord
{
    public const int MaximumDemandRound = 3;

    public Release1ChiefRecord(
        bool Adopted, LocalPressureTier AdoptedTier, bool AdoptedKnownOffender, int DemandRound,
        Release1ChiefState State, int WatchLinesSent, int LockdownAnnouncements, bool LockdownEngaged,
        int PaidLifts, int CooledLifts, int DeclineRepliesSent, int PaymentBlockedNotices, double? LastShortfallNoticed,
        IReadOnlyList<string> AcceptedLogicalCorrelations, IReadOnlyList<string> NativeEffectIds, long Revision, int LateFeesCharged = 0, int? LastLateFeeChargedDay = null,
        int? ProtectedUntilDay = null, bool ProtectionOffered = false, int? ProtectionWarnedForUntilDay = null,
        int ProtectionLapseNotices = 0, int ProtectionSettleFirstRefusals = 0, int ProtectionPurchases = 0, int ProtectionBlockedNotices = 0,
        int? ProtectionCoverStartCheckpoints = null,
        double? LockdownEngagedAtMinutes = null, int ForcedWantedWarnings = 0, int? LastForcedWantedNight = null,
        int ForcedWantedFires = 0, int ForcedWantedDispatches = 0, int ForcedWantedDispatchRefusals = 0,
        double? ForcedWantedWarnedAtMinutes = null)
    {
        this.Adopted = Adopted;
        this.AdoptedTier = AdoptedTier;
        this.AdoptedKnownOffender = AdoptedKnownOffender;
        this.DemandRound = DemandRound;
        this.State = State;
        this.WatchLinesSent = WatchLinesSent;
        this.LockdownAnnouncements = LockdownAnnouncements;
        this.LockdownEngaged = LockdownEngaged;
        this.PaidLifts = PaidLifts;
        this.CooledLifts = CooledLifts;
        this.DeclineRepliesSent = DeclineRepliesSent;
        this.PaymentBlockedNotices = PaymentBlockedNotices;
        this.LastShortfallNoticed = LastShortfallNoticed;
        this.AcceptedLogicalCorrelations = Freeze(AcceptedLogicalCorrelations, nameof(AcceptedLogicalCorrelations));
        this.NativeEffectIds = Freeze(NativeEffectIds, nameof(NativeEffectIds));
        this.Revision = Revision;
        this.LastLateFeeChargedDay = LastLateFeeChargedDay;
        this.LateFeesCharged = LateFeesCharged;
        this.ProtectedUntilDay = ProtectedUntilDay;
        this.ProtectionOffered = ProtectionOffered;
        this.ProtectionWarnedForUntilDay = ProtectionWarnedForUntilDay;
        this.ProtectionLapseNotices = ProtectionLapseNotices;
        this.ProtectionSettleFirstRefusals = ProtectionSettleFirstRefusals;
        this.ProtectionPurchases = ProtectionPurchases;
        this.ProtectionBlockedNotices = ProtectionBlockedNotices;
        this.ProtectionCoverStartCheckpoints = ProtectionCoverStartCheckpoints;
        this.LockdownEngagedAtMinutes = LockdownEngagedAtMinutes;
        this.ForcedWantedWarnings = ForcedWantedWarnings;
        this.LastForcedWantedNight = LastForcedWantedNight;
        this.ForcedWantedFires = ForcedWantedFires;
        this.ForcedWantedDispatches = ForcedWantedDispatches;
        this.ForcedWantedDispatchRefusals = ForcedWantedDispatchRefusals;
        this.ForcedWantedWarnedAtMinutes = ForcedWantedWarnedAtMinutes;
        Validate();
    }

    public bool Adopted { get; init; }
    public LocalPressureTier AdoptedTier { get; init; }
    public bool AdoptedKnownOffender { get; init; }
    public int DemandRound { get; init; }
    public Release1ChiefState State { get; init; }
    public int WatchLinesSent { get; init; }
    public int LockdownAnnouncements { get; init; }
    public bool LockdownEngaged { get; init; }
    public int PaidLifts { get; init; }
    public int CooledLifts { get; init; }
    public int DeclineRepliesSent { get; init; }
    public int PaymentBlockedNotices { get; init; }
    public double? LastShortfallNoticed { get; init; }
    public int? LastLateFeeChargedDay { get; init; }
    public int LateFeesCharged { get; init; }
    /// <summary>
    /// OC-118. The absolute game day (the same clock as the late fee) protection runs until,
    /// exclusive: the player is covered while today is strictly below this. Null is unprotected.
    /// </summary>
    public int? ProtectedUntilDay { get; init; }
    /// <summary>OC-118. Whether the Chief has made his standing protection offer (once ever).</summary>
    public bool ProtectionOffered { get; init; }
    /// <summary>
    /// OC-118. The cover end day the day-before warning was already sent for, so the warning
    /// fires once per cover period and re-arms naturally when an extension moves the end day on.
    /// </summary>
    public int? ProtectionWarnedForUntilDay { get; init; }
    /// <summary>OC-118. Lapses noticed, the durable key of the once-per-lapse text.</summary>
    public int ProtectionLapseNotices { get; init; }
    /// <summary>OC-118. Stale buy taps refused while a demand stood unpaid, the key of that text.</summary>
    public int ProtectionSettleFirstRefusals { get; init; }
    /// <summary>
    /// OC-118. Protection purchases prepared, the purchase number new effects authorize against.
    /// Never resets (like the message counters), so ids stay unique for the life of the save.
    /// </summary>
    public int ProtectionPurchases { get; init; }
    /// <summary>OC-118. Blocked protection payments told to the player, the demand's blocked counter with the demand taken out.</summary>
    public int ProtectionBlockedNotices { get; init; }
    /// <summary>
    /// OC-118. The checkpoint bitmask read at cover start, before the disable-all: bit i is the
    /// S1API CheckpointLocation i that was enabled then (see Release1ProtectionCheckpoints bits).
    /// The lapse restores exactly those, never all four; null is no snapshot (never covered, or the
    /// lapse already consumed it). Durable, so a reload mid-cover restores the same set.
    /// </summary>
    public int? ProtectionCoverStartCheckpoints { get; init; }
    /// <summary>
    /// OC-74. Canonical game minutes when the current lockdown engaged. Set on the same pass as
    /// the engage (or backfilled at the first readable minute for older saves), cleared on both
    /// lifts. The 24 h forced-Wanted warning counts from this stamp.
    /// </summary>
    public double? LockdownEngagedAtMinutes { get; init; }
    /// <summary>
    /// OC-74. Forced-Wanted warnings sent, the durable key of the once-per-lockdown text. Never
    /// resets (like the message counters); the warning re-arms per lockdown by comparing against
    /// PaidLifts + CooledLifts instead (see Release1ChiefLedger.IsForcedWantedWarned).
    /// </summary>
    public int ForcedWantedWarnings { get; init; }
    /// <summary>
    /// OC-74. The curfew night last fired on (the game day its window started), cleared on both
    /// lifts. Any int: night -1 is the pre-21:00 slice of day zero.
    /// </summary>
    public int? LastForcedWantedNight { get; init; }

    /// <summary>
    /// OC-74 review and owner run: the canonical minute the day-old warning went out. It marks
    /// the lockdown as warned (so the warning goes once), and a fire needs an hour of game time
    /// after it, so the warning never lands in the same instant as the arrest. Cleared with the
    /// lockdown.
    /// </summary>
    public double? ForcedWantedWarnedAtMinutes { get; init; }

    /// <summary>OC-74. Forced-Wanted fires, never reset, for QA status.</summary>
    public int ForcedWantedFires { get; init; }
    /// <summary>OC-74. Fires whose two-officer dispatch was accepted, never reset, for QA status.</summary>
    public int ForcedWantedDispatches { get; init; }
    /// <summary>OC-74. Fires whose dispatch was refused or short, never reset, for QA status.</summary>
    public int ForcedWantedDispatchRefusals { get; init; }
    private IReadOnlyList<string> _acceptedLogicalCorrelations = ImmutableArray<string>.Empty;
    private IReadOnlyList<string> _nativeEffectIds = ImmutableArray<string>.Empty;
    public IReadOnlyList<string> AcceptedLogicalCorrelations { get => _acceptedLogicalCorrelations; init => _acceptedLogicalCorrelations = Freeze(value, nameof(AcceptedLogicalCorrelations)); }
    public IReadOnlyList<string> NativeEffectIds { get => _nativeEffectIds; init => _nativeEffectIds = Freeze(value, nameof(NativeEffectIds)); }
    public long Revision { get; init; }

    public static Release1ChiefRecord Adopt(LocalPressureTier tier, bool knownOffender) => new(
        true, tier, knownOffender, 0, Release1ChiefState.Adopted, 0, 0, false, 0, 0, 0, 0, null,
        Array.Empty<string>(), Array.Empty<string>(), 0);

    /// <summary>The whole dollar demand for the current round: 25000, then 30000, then 35000, capped.</summary>
    public int Demand => Release1ChiefDemandLadder.For(DemandRound);

    public void Validate()
    {
        if (!Adopted) throw new ArgumentException("A Chief record only exists once it has been adopted.", nameof(Adopted));
        if (!Enum.IsDefined(AdoptedTier)) throw new ArgumentException("Adopted tier is not defined.", nameof(AdoptedTier));
        if (!Enum.IsDefined(State)) throw new ArgumentException("Chief state is not defined.", nameof(State));
        if (DemandRound < 0 || DemandRound > MaximumDemandRound)
            throw new ArgumentException("Chief demand round must be between 0 and the cap.", nameof(DemandRound));
        // OC-118. A protection buy from Adopted moves through PayingProtection with the round
        // still zero (no demand was ever opened); it is the one non-Adopted state a zero-round
        // record may hold, since protection can only be bought off an unpaid record.
        var preDemand = DemandRound == 0 && State is not Release1ChiefState.PayingProtection;
        if ((State == Release1ChiefState.Adopted) != preDemand)
            throw new ArgumentException("Round zero is exactly the adopted state.", nameof(DemandRound));
        if (WatchLinesSent < 0 || LockdownAnnouncements < 0 || PaidLifts < 0 || CooledLifts < 0 ||
            DeclineRepliesSent < 0 || PaymentBlockedNotices < 0)
            throw new ArgumentException("Chief message counters cannot be negative.", nameof(WatchLinesSent));
        if (Revision < 0) throw new ArgumentException("Chief revision cannot be negative.", nameof(Revision));
        if (LockdownEngaged && LockdownAnnouncements < 1)
            throw new ArgumentException("A lockdown can only be engaged after it has been announced.", nameof(LockdownEngaged));
        if (LastShortfallNoticed is { } shortfall && (double.IsNaN(shortfall) || double.IsInfinity(shortfall) || shortfall < 0))
            throw new ArgumentException("A noticed shortfall must be finite and non negative.", nameof(LastShortfallNoticed));
        if (LastLateFeeChargedDay is { } chargedDay && chargedDay < 0)
            throw new ArgumentException("A charged late fee day cannot be negative.", nameof(LastLateFeeChargedDay));
        if (LateFeesCharged < 0)
            throw new ArgumentException("A late fee count cannot be negative.", nameof(LateFeesCharged));
        if (ProtectedUntilDay is { } untilDay && untilDay < 0)
            throw new ArgumentException("A protection end day cannot be negative.", nameof(ProtectedUntilDay));
        if (ProtectionWarnedForUntilDay is { } warnedDay && warnedDay < 0)
            throw new ArgumentException("A protection warning day cannot be negative.", nameof(ProtectionWarnedForUntilDay));
        if (ProtectionLapseNotices < 0 || ProtectionSettleFirstRefusals < 0 || ProtectionPurchases < 0 || ProtectionBlockedNotices < 0)
            throw new ArgumentException("Protection counters cannot be negative.", nameof(ProtectionLapseNotices));
        if (ProtectionCoverStartCheckpoints is { } checkpointMask && (checkpointMask < 0 || checkpointMask > 15))
            throw new ArgumentException("A cover start checkpoint snapshot holds exactly the four locations.", nameof(ProtectionCoverStartCheckpoints));
        if (LockdownEngagedAtMinutes is { } engagedAt && (double.IsNaN(engagedAt) || double.IsInfinity(engagedAt) || engagedAt < 0))
            throw new ArgumentException("A lockdown stamp must be finite and non negative.", nameof(LockdownEngagedAtMinutes));
        if (ForcedWantedWarnedAtMinutes is { } warnedAt && (double.IsNaN(warnedAt) || double.IsInfinity(warnedAt) || warnedAt < 0))
            throw new ArgumentException("A forced-Wanted warning time must be finite and non negative.", nameof(ForcedWantedWarnedAtMinutes));
        if (ForcedWantedWarnings < 0 || ForcedWantedFires < 0 || ForcedWantedDispatches < 0 || ForcedWantedDispatchRefusals < 0)
            throw new ArgumentException("Forced-Wanted counters cannot be negative.", nameof(ForcedWantedWarnings));
        foreach (var correlation in AcceptedLogicalCorrelations)
        {
            if (!Release1LogicalCorrelation.TryParse(correlation, out var parsed) ||
                parsed.MissionKey != Release1MissionCatalog.ChiefCampbell ||
                parsed.Attempt < 1)
                throw new ArgumentException("Chief correlations must be canonical authorizations for this record.", nameof(AcceptedLogicalCorrelations));
            // OC-118. Demand payments authorize against the ladder round they paid; protection
            // purchases authorize against the never-resetting purchase count instead, so a buy on an
            // Adopted record (round zero) is expressible at all.
            var payment = parsed.TransitionKind == Release1TransitionKind.ChiefPaymentAccepted && parsed.Attempt <= DemandRound;
            var protection = parsed.TransitionKind == Release1TransitionKind.ChiefProtectionAccepted && parsed.Attempt <= ProtectionPurchases;
            if (!payment && !protection)
                throw new ArgumentException("Chief correlations must be canonical payment authorizations for a round already reached, or protection authorizations for a purchase already prepared.", nameof(AcceptedLogicalCorrelations));
        }
        foreach (var effectId in NativeEffectIds) Release1MissionRecord.ValidateId(effectId, nameof(NativeEffectIds));
        if (AcceptedLogicalCorrelations.Distinct(StringComparer.Ordinal).Count() != AcceptedLogicalCorrelations.Count ||
            NativeEffectIds.Distinct(StringComparer.Ordinal).Count() != NativeEffectIds.Count)
            throw new ArgumentException("Chief collections must be unique.", nameof(NativeEffectIds));
        if (State is Release1ChiefState.Paying or Release1ChiefState.PayingProtection && (AcceptedLogicalCorrelations.Count == 0 || NativeEffectIds.Count == 0))
            throw new ArgumentException("A paying Chief record requires its authorization and its effect.", nameof(State));
    }

    public bool ValueEquals(Release1ChiefRecord? other) =>
        other is not null && Adopted == other.Adopted && AdoptedTier == other.AdoptedTier &&
        AdoptedKnownOffender == other.AdoptedKnownOffender && DemandRound == other.DemandRound &&
        State == other.State && WatchLinesSent == other.WatchLinesSent &&
        LockdownAnnouncements == other.LockdownAnnouncements && LockdownEngaged == other.LockdownEngaged &&
        PaidLifts == other.PaidLifts && CooledLifts == other.CooledLifts &&
        DeclineRepliesSent == other.DeclineRepliesSent && PaymentBlockedNotices == other.PaymentBlockedNotices &&
        Nullable.Equals(LastShortfallNoticed, other.LastShortfallNoticed) && Revision == other.Revision &&
        LastLateFeeChargedDay == other.LastLateFeeChargedDay && LateFeesCharged == other.LateFeesCharged &&
        ProtectedUntilDay == other.ProtectedUntilDay && ProtectionOffered == other.ProtectionOffered &&
        ProtectionWarnedForUntilDay == other.ProtectionWarnedForUntilDay &&
        ProtectionLapseNotices == other.ProtectionLapseNotices &&
        ProtectionSettleFirstRefusals == other.ProtectionSettleFirstRefusals &&
        ProtectionPurchases == other.ProtectionPurchases &&
        ProtectionBlockedNotices == other.ProtectionBlockedNotices &&
        ProtectionCoverStartCheckpoints == other.ProtectionCoverStartCheckpoints &&
        LockdownEngagedAtMinutes == other.LockdownEngagedAtMinutes &&
        ForcedWantedWarnings == other.ForcedWantedWarnings &&
        LastForcedWantedNight == other.LastForcedWantedNight &&
        ForcedWantedWarnedAtMinutes == other.ForcedWantedWarnedAtMinutes &&
        ForcedWantedFires == other.ForcedWantedFires &&
        ForcedWantedDispatches == other.ForcedWantedDispatches &&
        ForcedWantedDispatchRefusals == other.ForcedWantedDispatchRefusals &&
        AcceptedLogicalCorrelations.SequenceEqual(other.AcceptedLogicalCorrelations, StringComparer.Ordinal) &&
        NativeEffectIds.SequenceEqual(other.NativeEffectIds, StringComparer.Ordinal);

    public bool Equals(Release1ChiefRecord? other) => ValueEquals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Adopted); hash.Add(AdoptedTier); hash.Add(AdoptedKnownOffender); hash.Add(DemandRound);
        hash.Add(State); hash.Add(WatchLinesSent); hash.Add(LockdownAnnouncements); hash.Add(LockdownEngaged);
        hash.Add(PaidLifts); hash.Add(CooledLifts); hash.Add(DeclineRepliesSent); hash.Add(PaymentBlockedNotices);
        hash.Add(LastShortfallNoticed); hash.Add(Revision); hash.Add(LastLateFeeChargedDay); hash.Add(LateFeesCharged);
        hash.Add(ProtectedUntilDay); hash.Add(ProtectionOffered); hash.Add(ProtectionWarnedForUntilDay);
        hash.Add(ProtectionLapseNotices); hash.Add(ProtectionSettleFirstRefusals); hash.Add(ProtectionPurchases); hash.Add(ProtectionBlockedNotices); hash.Add(ProtectionCoverStartCheckpoints);
        hash.Add(LockdownEngagedAtMinutes); hash.Add(ForcedWantedWarnings); hash.Add(LastForcedWantedNight); hash.Add(ForcedWantedWarnedAtMinutes);
        hash.Add(ForcedWantedFires); hash.Add(ForcedWantedDispatches); hash.Add(ForcedWantedDispatchRefusals);
        foreach (var value in AcceptedLogicalCorrelations) hash.Add(value);
        foreach (var value in NativeEffectIds) hash.Add(value);
        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> Freeze(IReadOnlyList<string>? values, string parameterName) =>
        values is null ? throw new ArgumentNullException(parameterName) : ImmutableArray.CreateRange(values);
}

/// <summary>
/// The ladder, in the Model layer so the record can read it without depending on the Runtime layer's
/// copy table. <c>Release1ChiefCampbellCopy.DemandFor</c> delegates to this, so there is exactly one
/// definition of 25000, 30000, 35000 in the codebase and one copy test pins both.
/// </summary>
public static class Release1ChiefDemandLadder
{
    /// <summary>The capped demand: the most OC will ever ask the wallet to debit for the Chief.</summary>
    public const int Cap = 35_000;

    public static int For(int round) => round <= 1 ? 25_000 : round == 2 ? 30_000 : Cap;
}

/// <summary>
/// OC-118. The one tunable home of Campbell's protection retainer: the weekly price, the cover
/// arithmetic, the offer threshold, and the effect identity. The price sits at or under the demand
/// ladder's cap, so the wallet debit bound the Chief's pay path already enforces covers it unchanged.
/// </summary>
public static class Release1ChiefProtection
{
    /// <summary>The whole dollar price of one week of protection, debited per purchase.</summary>
    public const int ProtectionWeeklyPrice = 30_000;

    /// <summary>Cover days added by each purchase, counted from today or the current end day.</summary>
    public const int CoverDaysPerPurchase = 7;

    /// <summary>The most cover bankable ahead of today; the button hides at this cap.</summary>
    public const int MaxBankedDays = 14;

    /// <summary>Successful Chapter 3 jobs after which the Chief makes his offer, once.</summary>
    public const int OfferSuccessfulJobs = 3;

    public const string EffectIdPrefix = "chief-campbell-protection-v1-";

    /// <summary>
    /// The journal id of one protection purchase. purchase is the 1-based ProtectionPurchases
    /// count the prepare authorizes against, revision the record's own Revision, which never
    /// resets, so this id is unique for the life of the save exactly like the demand ids.
    /// </summary>
    public static string EffectId(int purchase, long revision) => $"{EffectIdPrefix}p{purchase}-c{revision}";

    /// <summary>
    /// OC-118. Whether a journal entry is one of the Chief's own protection cash effects. The
    /// commit sweep matches on this, not on the id prefix alone, so a demand effect can never be
    /// swept up as protection and a mission effect can never be swept up at all.
    /// </summary>
    public static bool IsProtectionEffect(Release1NativeEffectJournalEntry effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return effect.EffectId.StartsWith(EffectIdPrefix, StringComparison.Ordinal) &&
            string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal);
    }

    public static string NativeReceiptId(int purchase, long revision) => $"chief-campbell-protection-native-v1-p{purchase}-c{revision}";
}
