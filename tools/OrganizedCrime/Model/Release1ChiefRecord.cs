using System.Collections.Immutable;

namespace OrganizedCrime.Model;

/// <summary>Where Chief Campbell's account with this player stands.</summary>
public enum Release1ChiefState { Adopted, DemandOpen, Paying, Settled, Declined }

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
        IReadOnlyList<string> AcceptedLogicalCorrelations, IReadOnlyList<string> NativeEffectIds, long Revision)
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
    private IReadOnlyList<string> _acceptedLogicalCorrelations = ImmutableArray<string>.Empty;
    private IReadOnlyList<string> _nativeEffectIds = ImmutableArray<string>.Empty;
    public IReadOnlyList<string> AcceptedLogicalCorrelations { get => _acceptedLogicalCorrelations; init => _acceptedLogicalCorrelations = Freeze(value, nameof(AcceptedLogicalCorrelations)); }
    public IReadOnlyList<string> NativeEffectIds { get => _nativeEffectIds; init => _nativeEffectIds = Freeze(value, nameof(NativeEffectIds)); }
    public long Revision { get; init; }

    public static Release1ChiefRecord Adopt(LocalPressureTier tier, bool knownOffender) => new(
        true, tier, knownOffender, 0, Release1ChiefState.Adopted, 0, 0, false, 0, 0, 0, 0, null,
        Array.Empty<string>(), Array.Empty<string>(), 0);

    /// <summary>The whole dollar demand for the current round: 15000, then 20000, then 25000, capped.</summary>
    public int Demand => Release1ChiefDemandLadder.For(DemandRound);

    public void Validate()
    {
        if (!Adopted) throw new ArgumentException("A Chief record only exists once it has been adopted.", nameof(Adopted));
        if (!Enum.IsDefined(AdoptedTier)) throw new ArgumentException("Adopted tier is not defined.", nameof(AdoptedTier));
        if (!Enum.IsDefined(State)) throw new ArgumentException("Chief state is not defined.", nameof(State));
        if (DemandRound < 0 || DemandRound > MaximumDemandRound)
            throw new ArgumentException("Chief demand round must be between 0 and the cap.", nameof(DemandRound));
        if ((State == Release1ChiefState.Adopted) != (DemandRound == 0))
            throw new ArgumentException("Round zero is exactly the adopted state.", nameof(DemandRound));
        if (WatchLinesSent < 0 || LockdownAnnouncements < 0 || PaidLifts < 0 || CooledLifts < 0 ||
            DeclineRepliesSent < 0 || PaymentBlockedNotices < 0)
            throw new ArgumentException("Chief message counters cannot be negative.", nameof(WatchLinesSent));
        if (Revision < 0) throw new ArgumentException("Chief revision cannot be negative.", nameof(Revision));
        if (LockdownEngaged && LockdownAnnouncements < 1)
            throw new ArgumentException("A lockdown can only be engaged after it has been announced.", nameof(LockdownEngaged));
        if (LastShortfallNoticed is { } shortfall && (double.IsNaN(shortfall) || double.IsInfinity(shortfall) || shortfall < 0))
            throw new ArgumentException("A noticed shortfall must be finite and non negative.", nameof(LastShortfallNoticed));
        foreach (var correlation in AcceptedLogicalCorrelations)
            if (!Release1LogicalCorrelation.TryParse(correlation, out var parsed) ||
                parsed.MissionKey != Release1MissionCatalog.ChiefCampbell ||
                parsed.TransitionKind != Release1TransitionKind.ChiefPaymentAccepted ||
                parsed.Attempt < 1 || parsed.Attempt > DemandRound)
                throw new ArgumentException("Chief correlations must be canonical payment authorizations for a round already reached.", nameof(AcceptedLogicalCorrelations));
        foreach (var effectId in NativeEffectIds) Release1MissionRecord.ValidateId(effectId, nameof(NativeEffectIds));
        if (AcceptedLogicalCorrelations.Distinct(StringComparer.Ordinal).Count() != AcceptedLogicalCorrelations.Count ||
            NativeEffectIds.Distinct(StringComparer.Ordinal).Count() != NativeEffectIds.Count)
            throw new ArgumentException("Chief collections must be unique.", nameof(NativeEffectIds));
        if (State == Release1ChiefState.Paying && (AcceptedLogicalCorrelations.Count == 0 || NativeEffectIds.Count == 0))
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
        AcceptedLogicalCorrelations.SequenceEqual(other.AcceptedLogicalCorrelations, StringComparer.Ordinal) &&
        NativeEffectIds.SequenceEqual(other.NativeEffectIds, StringComparer.Ordinal);

    public bool Equals(Release1ChiefRecord? other) => ValueEquals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Adopted); hash.Add(AdoptedTier); hash.Add(AdoptedKnownOffender); hash.Add(DemandRound);
        hash.Add(State); hash.Add(WatchLinesSent); hash.Add(LockdownAnnouncements); hash.Add(LockdownEngaged);
        hash.Add(PaidLifts); hash.Add(CooledLifts); hash.Add(DeclineRepliesSent); hash.Add(PaymentBlockedNotices);
        hash.Add(LastShortfallNoticed); hash.Add(Revision);
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
/// definition of 15000, 20000, 25000 in the codebase and one copy test pins both.
/// </summary>
public static class Release1ChiefDemandLadder
{
    public static int For(int round) => round <= 1 ? 15_000 : round == 2 ? 20_000 : 25_000;
}
