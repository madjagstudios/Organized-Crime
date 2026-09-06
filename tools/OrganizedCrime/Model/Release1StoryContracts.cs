using System.Collections.Immutable;

namespace OrganizedCrime.Model;

public enum Release1StandingBand
{
    Cold,
    Tolerated,
    Useful,
    Trusted,
    RegionalProspect
}

public enum Release1RelationshipState
{
    Unstarted,
    Deferred,
    Accepted
}

public enum Release1MissionState
{
    Locked,
    Offered,
    Deferred,
    Accepted,
    Active,
    Abandoned,
    MakeGoodOffered,
    MakeGoodActive,
    RecoveryAvailable,
    RecoveryActive,
    Satisfied
}

public enum Release1MissionOutcome
{
    None,
    OnTime,
    Late,
    Abandoned,
    RequiredFailure,
    MakeGoodFailure,
    RecoveryFailure
}

public enum Release1CompletionTiming
{
    OnTime,
    Late
}

public enum Release1RecoveryMode
{
    None,
    StandardMakeGood,
    GuaranteedRecovery
}

public sealed record Release1MissionRecord
{
    public Release1MissionRecord(
        string MissionKey,
        Release1MissionState State,
        int Attempt,
        string? TermsVersion,
        double? AcceptedGameTimeHours,
        double? DeadlineGameTimeHours,
        Release1MissionOutcome LastOutcome,
        int StandingPenaltyApplied,
        IReadOnlyList<string> PenaltyReceiptIds,
        int MakeGoodFailures,
        Release1RecoveryMode RecoveryMode,
        string? RewardAuthorizationReceiptId,
        bool QuietConditionAwarded,
        IReadOnlyList<string> NativeEffectIds,
        IReadOnlyList<string> PresentationCorrelationIds,
        IReadOnlyList<string> AcceptedLogicalCorrelations,
        string? NativePresentationRef,
        long Revision)
    {
        this.MissionKey = MissionKey;
        this.State = State;
        this.Attempt = Attempt;
        this.TermsVersion = TermsVersion;
        this.AcceptedGameTimeHours = AcceptedGameTimeHours;
        this.DeadlineGameTimeHours = DeadlineGameTimeHours;
        this.LastOutcome = LastOutcome;
        this.StandingPenaltyApplied = StandingPenaltyApplied;
        this.PenaltyReceiptIds = CopyAndValidateIds(PenaltyReceiptIds, nameof(PenaltyReceiptIds));
        this.MakeGoodFailures = MakeGoodFailures;
        this.RecoveryMode = RecoveryMode;
        this.RewardAuthorizationReceiptId = RewardAuthorizationReceiptId;
        this.QuietConditionAwarded = QuietConditionAwarded;
        this.NativeEffectIds = CopyAndValidateIds(NativeEffectIds, nameof(NativeEffectIds));
        this.PresentationCorrelationIds = CopyAndValidateIds(PresentationCorrelationIds, nameof(PresentationCorrelationIds), allowCorrelation: true);
        this.AcceptedLogicalCorrelations = CopyAndValidateIds(AcceptedLogicalCorrelations, nameof(AcceptedLogicalCorrelations), allowCorrelation: true);
        this.NativePresentationRef = NativePresentationRef;
        this.Revision = Revision;
        Validate();
    }

    public string MissionKey { get; init; }
    public Release1MissionState State { get; init; }
    public int Attempt { get; init; }
    public string? TermsVersion { get; init; }
    public double? AcceptedGameTimeHours { get; init; }
    public double? DeadlineGameTimeHours { get; init; }
    public Release1MissionOutcome LastOutcome { get; init; }
    public int StandingPenaltyApplied { get; init; }
    private IReadOnlyList<string> _penaltyReceiptIds = ImmutableArray<string>.Empty;
    public IReadOnlyList<string> PenaltyReceiptIds { get => _penaltyReceiptIds; init => _penaltyReceiptIds = Freeze(value, nameof(PenaltyReceiptIds)); }
    public int MakeGoodFailures { get; init; }
    public Release1RecoveryMode RecoveryMode { get; init; }
    public string? RewardAuthorizationReceiptId { get; init; }
    public bool QuietConditionAwarded { get; init; }
    private IReadOnlyList<string> _nativeEffectIds = ImmutableArray<string>.Empty;
    private IReadOnlyList<string> _presentationCorrelationIds = ImmutableArray<string>.Empty;
    private IReadOnlyList<string> _acceptedLogicalCorrelations = ImmutableArray<string>.Empty;
    public IReadOnlyList<string> NativeEffectIds { get => _nativeEffectIds; init => _nativeEffectIds = Freeze(value, nameof(NativeEffectIds)); }
    public IReadOnlyList<string> PresentationCorrelationIds { get => _presentationCorrelationIds; init => _presentationCorrelationIds = Freeze(value, nameof(PresentationCorrelationIds)); }
    public IReadOnlyList<string> AcceptedLogicalCorrelations { get => _acceptedLogicalCorrelations; init => _acceptedLogicalCorrelations = Freeze(value, nameof(AcceptedLogicalCorrelations)); }
    public string? NativePresentationRef { get; init; }
    public long Revision { get; init; }

    public void Validate()
    {
        if (!Release1MissionCatalog.IsMissionKey(MissionKey))
            throw new ArgumentException("Mission key is not one of the canonical Release 1 missions.", nameof(MissionKey));
        if (!Enum.IsDefined(State)) throw new ArgumentException("Mission state is not defined.", nameof(State));
        if (!Enum.IsDefined(LastOutcome)) throw new ArgumentException("Mission outcome is not defined.", nameof(LastOutcome));
        if (!Enum.IsDefined(RecoveryMode)) throw new ArgumentException("Recovery mode is not defined.", nameof(RecoveryMode));
        if (Attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(Attempt), "Mission attempts must be positive.");
        if (StandingPenaltyApplied is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(StandingPenaltyApplied), "Mission penalty must be between 0 and 20.");
        if (MakeGoodFailures is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MakeGoodFailures), "Only one standard make-good may fail.");
        if (Revision < 0)
            throw new ArgumentOutOfRangeException(nameof(Revision), "Mission revision cannot be negative.");
        if (TermsVersion is not null && string.IsNullOrWhiteSpace(TermsVersion))
            throw new ArgumentException("Terms version cannot be blank.", nameof(TermsVersion));
        ValidateGameTime(AcceptedGameTimeHours, nameof(AcceptedGameTimeHours));
        ValidateGameTime(DeadlineGameTimeHours, nameof(DeadlineGameTimeHours));
        if (AcceptedGameTimeHours is not null && DeadlineGameTimeHours is not null &&
            DeadlineGameTimeHours < AcceptedGameTimeHours)
            throw new ArgumentException("Mission deadline cannot precede acceptance.", nameof(DeadlineGameTimeHours));
        if (RewardAuthorizationReceiptId is not null)
            ValidateId(RewardAuthorizationReceiptId, nameof(RewardAuthorizationReceiptId));
        if (NativePresentationRef is not null)
            ValidateId(NativePresentationRef, nameof(NativePresentationRef));
        foreach (var correlation in PresentationCorrelationIds.Concat(AcceptedLogicalCorrelations))
            if (!Release1LogicalCorrelation.TryParse(correlation, out _))
                throw new ArgumentException("Persisted logical correlations must be canonical.");
        if (State == Release1MissionState.Satisfied && RewardAuthorizationReceiptId is null)
            throw new ArgumentException("Satisfied missions require reward authorization.", nameof(State));
        if (State == Release1MissionState.Satisfied && LastOutcome == Release1MissionOutcome.None)
            throw new ArgumentException("Satisfied missions require a completion outcome.", nameof(State));
    }

    public bool ValueEquals(Release1MissionRecord? other) =>
        other is not null &&
        MissionKey == other.MissionKey && State == other.State && Attempt == other.Attempt &&
        TermsVersion == other.TermsVersion && AcceptedGameTimeHours == other.AcceptedGameTimeHours &&
        DeadlineGameTimeHours == other.DeadlineGameTimeHours && LastOutcome == other.LastOutcome &&
        StandingPenaltyApplied == other.StandingPenaltyApplied && MakeGoodFailures == other.MakeGoodFailures &&
        RecoveryMode == other.RecoveryMode && RewardAuthorizationReceiptId == other.RewardAuthorizationReceiptId &&
        QuietConditionAwarded == other.QuietConditionAwarded && NativePresentationRef == other.NativePresentationRef &&
        Revision == other.Revision && SequenceEqual(PenaltyReceiptIds, other.PenaltyReceiptIds) &&
        SequenceEqual(NativeEffectIds, other.NativeEffectIds) &&
        SequenceEqual(PresentationCorrelationIds, other.PresentationCorrelationIds) &&
        SequenceEqual(AcceptedLogicalCorrelations, other.AcceptedLogicalCorrelations);

    public bool Equals(Release1MissionRecord? other) => ValueEquals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MissionKey); hash.Add(State); hash.Add(Attempt); hash.Add(TermsVersion);
        hash.Add(AcceptedGameTimeHours); hash.Add(DeadlineGameTimeHours); hash.Add(LastOutcome);
        hash.Add(StandingPenaltyApplied); hash.Add(MakeGoodFailures); hash.Add(RecoveryMode);
        hash.Add(RewardAuthorizationReceiptId); hash.Add(QuietConditionAwarded); hash.Add(NativePresentationRef);
        hash.Add(Revision);
        foreach (var value in PenaltyReceiptIds) hash.Add(value);
        foreach (var value in NativeEffectIds) hash.Add(value);
        foreach (var value in PresentationCorrelationIds) hash.Add(value);
        foreach (var value in AcceptedLogicalCorrelations) hash.Add(value);
        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyAndValidateIds(IReadOnlyList<string>? values, string parameterName, bool allowCorrelation = false)
    {
        if (values is null) throw new ArgumentNullException(parameterName);
        var copy = values.ToArray();
        foreach (var value in copy)
        {
            if (allowCorrelation) ValidateCorrelation(value, parameterName);
            else ValidateId(value, parameterName);
        }
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Collection IDs must be unique.", parameterName);
        return copy;
    }

    internal static void ValidateId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('/') || value.Length > 256)
            throw new ArgumentException("IDs must be nonblank, bounded, and single-segment.", parameterName);
    }

    internal static void ValidateCorrelation(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Length > 1024)
            throw new ArgumentException("Correlations must be nonblank, bounded, and control-character-free.", parameterName);
    }

    private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? values, string parameterName)
    {
        if (values is null) throw new ArgumentNullException(parameterName);
        return ImmutableArray.CreateRange(values);
    }

    private static void ValidateGameTime(double? value, string parameterName)
    {
        if (value is not null && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value < 0))
            throw new ArgumentOutOfRangeException(parameterName, "Game time must be finite and non-negative.");
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.Ordinal);
}

public sealed record Release1StoryState
{
    public Release1StoryState(
        string PlayerId,
        int Standing,
        Release1RelationshipState RelationshipState,
        bool Release1Recognized,
        IReadOnlyList<string> IntroLogicalCorrelationIds,
        IReadOnlyList<string> RecognitionLogicalCorrelationIds,
        IReadOnlyList<Release1MissionRecord> Missions,
        IReadOnlyList<Release1NativeEffectJournalEntry> NativeEffects,
        long Revision,
        // OC-73. Trailing and optional, so every existing construction site compiles unchanged: the
        // constructor's own Validate() call runs before any object-initializer property is applied,
        // and Release1ChiefRecord's cross-check against NativeEffects needs ChiefRecord to already be
        // set at that point (Release1StorySaveCodec.FromDto's read path is the case that needs this;
        // the runtime never reconstructs through this constructor for a live Chief transition, since
        // TrySetChiefRecord and PrepareChiefEffect both validate through a `with` copy instead).
        Release1ChiefRecord? ChiefRecord = null)
    {
        this.PlayerId = PlayerId;
        this.Standing = Standing;
        this.RelationshipState = RelationshipState;
        this.Release1Recognized = Release1Recognized;
        this.IntroLogicalCorrelationIds = CopyIds(IntroLogicalCorrelationIds, nameof(IntroLogicalCorrelationIds));
        this.RecognitionLogicalCorrelationIds = CopyIds(RecognitionLogicalCorrelationIds, nameof(RecognitionLogicalCorrelationIds));
        this.Missions = Missions?.ToArray() ?? throw new ArgumentNullException(nameof(Missions));
        this.NativeEffects = NativeEffects?.ToArray() ?? throw new ArgumentNullException(nameof(NativeEffects));
        this.Revision = Revision;
        this.ChiefRecord = ChiefRecord;
        Validate();
    }

    public string PlayerId { get; init; }
    public int Standing { get; init; }
    public Release1RelationshipState RelationshipState { get; init; }
    public bool Release1Recognized { get; init; }
    private IReadOnlyList<string> _introLogicalCorrelationIds = ImmutableArray<string>.Empty;
    private IReadOnlyList<string> _recognitionLogicalCorrelationIds = ImmutableArray<string>.Empty;
    private IReadOnlyList<Release1MissionRecord> _missions = ImmutableArray<Release1MissionRecord>.Empty;
    private IReadOnlyList<Release1NativeEffectJournalEntry> _nativeEffects = ImmutableArray<Release1NativeEffectJournalEntry>.Empty;
    private IReadOnlyList<Release1PhonePresentationAttempt> _phonePresentationAttempts = ImmutableArray<Release1PhonePresentationAttempt>.Empty;
    private IReadOnlyList<Release1SmallCourtesyAssignment> _smallCourtesyAssignments = ImmutableArray<Release1SmallCourtesyAssignment>.Empty;
    private IReadOnlyList<Release1PresentationReceipt> _presentationReceipts = ImmutableArray<Release1PresentationReceipt>.Empty;
    private IReadOnlyList<Release1WrongAddressAssignment> _wrongAddressAssignments = ImmutableArray<Release1WrongAddressAssignment>.Empty;
    private IReadOnlyList<Release1WrongAddressProgress> _wrongAddressProgress = ImmutableArray<Release1WrongAddressProgress>.Empty;
    private IReadOnlyList<Release1RoomWithNoNameAssignment> _roomWithNoNameAssignments = ImmutableArray<Release1RoomWithNoNameAssignment>.Empty;
    private IReadOnlyList<Release1RoomWithNoNameProgress> _roomWithNoNameProgress = ImmutableArray<Release1RoomWithNoNameProgress>.Empty;
    private IReadOnlyList<Release1ShortNoticeAssignment> _shortNoticeAssignments = ImmutableArray<Release1ShortNoticeAssignment>.Empty;
    private IReadOnlyList<Release1ShortNoticeProgress> _shortNoticeProgress = ImmutableArray<Release1ShortNoticeProgress>.Empty;
    private IReadOnlyList<Release1KeepTheLightsOffAssignment> _keepTheLightsOffAssignments = ImmutableArray<Release1KeepTheLightsOffAssignment>.Empty;
    private IReadOnlyList<Release1KeepTheLightsOffProgress> _keepTheLightsOffProgress = ImmutableArray<Release1KeepTheLightsOffProgress>.Empty;
    private IReadOnlyList<Release1TheEnvelopeAssignment> _theEnvelopeAssignments = ImmutableArray<Release1TheEnvelopeAssignment>.Empty;
    private IReadOnlyList<Release1TheEnvelopeProgress> _theEnvelopeProgress = ImmutableArray<Release1TheEnvelopeProgress>.Empty;
    public IReadOnlyList<string> IntroLogicalCorrelationIds { get => _introLogicalCorrelationIds; init => _introLogicalCorrelationIds = Freeze(value, nameof(IntroLogicalCorrelationIds)); }
    public IReadOnlyList<string> RecognitionLogicalCorrelationIds { get => _recognitionLogicalCorrelationIds; init => _recognitionLogicalCorrelationIds = Freeze(value, nameof(RecognitionLogicalCorrelationIds)); }
    public IReadOnlyList<Release1MissionRecord> Missions { get => _missions; init => _missions = Freeze(value, nameof(Missions)); }
    public IReadOnlyList<Release1NativeEffectJournalEntry> NativeEffects { get => _nativeEffects; init => _nativeEffects = Freeze(value, nameof(NativeEffects)); }
    public IReadOnlyList<Release1PhonePresentationAttempt> PhonePresentationAttempts { get => _phonePresentationAttempts; init => _phonePresentationAttempts = Freeze(value, nameof(PhonePresentationAttempts)); }
    public IReadOnlyList<Release1SmallCourtesyAssignment> SmallCourtesyAssignments { get => _smallCourtesyAssignments; init => _smallCourtesyAssignments = Freeze(value, nameof(SmallCourtesyAssignments)); }
    public IReadOnlyList<Release1PresentationReceipt> PresentationReceipts { get => _presentationReceipts; init => _presentationReceipts = Freeze(value, nameof(PresentationReceipts)); }
    public IReadOnlyList<Release1WrongAddressAssignment> WrongAddressAssignments { get => _wrongAddressAssignments; init => _wrongAddressAssignments = Freeze(value, nameof(WrongAddressAssignments)); }
    public IReadOnlyList<Release1WrongAddressProgress> WrongAddressProgress { get => _wrongAddressProgress; init => _wrongAddressProgress = Freeze(value, nameof(WrongAddressProgress)); }
    public IReadOnlyList<Release1RoomWithNoNameAssignment> RoomWithNoNameAssignments { get => _roomWithNoNameAssignments; init => _roomWithNoNameAssignments = Freeze(value, nameof(RoomWithNoNameAssignments)); }
    public IReadOnlyList<Release1RoomWithNoNameProgress> RoomWithNoNameProgress { get => _roomWithNoNameProgress; init => _roomWithNoNameProgress = Freeze(value, nameof(RoomWithNoNameProgress)); }
    public IReadOnlyList<Release1ShortNoticeAssignment> ShortNoticeAssignments { get => _shortNoticeAssignments; init => _shortNoticeAssignments = Freeze(value, nameof(ShortNoticeAssignments)); }
    public IReadOnlyList<Release1ShortNoticeProgress> ShortNoticeProgress { get => _shortNoticeProgress; init => _shortNoticeProgress = Freeze(value, nameof(ShortNoticeProgress)); }
    public IReadOnlyList<Release1KeepTheLightsOffAssignment> KeepTheLightsOffAssignments { get => _keepTheLightsOffAssignments; init => _keepTheLightsOffAssignments = Freeze(value, nameof(KeepTheLightsOffAssignments)); }
    public IReadOnlyList<Release1KeepTheLightsOffProgress> KeepTheLightsOffProgress { get => _keepTheLightsOffProgress; init => _keepTheLightsOffProgress = Freeze(value, nameof(KeepTheLightsOffProgress)); }
    public IReadOnlyList<Release1TheEnvelopeAssignment> TheEnvelopeAssignments { get => _theEnvelopeAssignments; init => _theEnvelopeAssignments = Freeze(value, nameof(TheEnvelopeAssignments)); }
    public IReadOnlyList<Release1TheEnvelopeProgress> TheEnvelopeProgress { get => _theEnvelopeProgress; init => _theEnvelopeProgress = Freeze(value, nameof(TheEnvelopeProgress)); }

    /// <summary>
    /// OC-73. Chief Campbell's one story record. Nullable, no constructor parameter: a v1 to v10
    /// document normalizes to null (the pre adoption state), and every existing construction site
    /// compiles unchanged. Unlike the six missions, he carries no entry in <see cref="Missions"/>: the
    /// mission ladder, unlock order, Standing table and quest projection never see him.
    /// </summary>
    public Release1ChiefRecord? ChiefRecord { get; init; }
    public long Revision { get; init; }

    public Release1StandingBand StandingBand => Standing switch
    {
        < 20 => Release1StandingBand.Cold,
        < 40 => Release1StandingBand.Tolerated,
        < 60 => Release1StandingBand.Useful,
        < 80 => Release1StandingBand.Trusted,
        _ => Release1StandingBand.RegionalProspect
    };

    public static Release1StoryState CreateAccepted(string playerId, string introCorrelationId)
    {
        if (!Release1LogicalCorrelation.TryParse(introCorrelationId, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.IntroScopeKey ||
            correlation.Attempt != 0 || correlation.TransitionKind != Release1TransitionKind.IntroAccepted ||
            correlation.PlayerId != playerId)
            throw new ArgumentException("Intro correlation is not canonical.", nameof(introCorrelationId));

        var missions = Release1MissionCatalog.All.Select((definition, index) =>
            NewMission(definition.MissionKey, index == 0 ? Release1MissionState.Offered : Release1MissionState.Locked)).ToArray();
        return new Release1StoryState(playerId, 20, Release1RelationshipState.Accepted, false,
            new[] { introCorrelationId }, Array.Empty<string>(), missions,
            Array.Empty<Release1NativeEffectJournalEntry>(), 1);
    }

    public void Validate()
    {
        ValidatePlayerId(PlayerId);
        if (Standing is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(Standing));
        if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        if (Missions.Count != Release1MissionCatalog.All.Count)
            throw new ArgumentException("Story state must contain exactly six missions.", nameof(Missions));
        for (var index = 0; index < Missions.Count; index++)
        {
            var mission = Missions[index] ?? throw new ArgumentException("Mission records cannot be null.", nameof(Missions));
            mission.Validate();
            if (mission.MissionKey != Release1MissionCatalog.All[index].MissionKey)
                throw new ArgumentException("Mission records must use canonical catalog order.", nameof(Missions));
            foreach (var value in mission.AcceptedLogicalCorrelations)
            {
                if (!Release1LogicalCorrelation.TryParse(value, out var parsed) ||
                    parsed.PlayerId != PlayerId || parsed.MissionKey != mission.MissionKey ||
                    parsed.Attempt < 1 || parsed.Attempt > mission.Attempt ||
                    parsed.TransitionKind is Release1TransitionKind.IntroAccepted or Release1TransitionKind.IntroDeferred or Release1TransitionKind.Release1Recognized)
                    throw new ArgumentException("Mission correlation did not match its containing player, mission, attempt, or transition.", nameof(Missions));
            }
            foreach (var value in mission.PresentationCorrelationIds)
            {
                if (!Release1LogicalCorrelation.TryParse(value, out var parsed) || parsed.PlayerId != PlayerId || parsed.MissionKey != mission.MissionKey || parsed.Attempt < 1 || parsed.Attempt > mission.Attempt)
                    throw new ArgumentException("Presentation correlation did not match its containing player, mission, or attempt.", nameof(Missions));
            }
        }
        if (Missions.Select(m => m.MissionKey).Distinct(StringComparer.Ordinal).Count() != Missions.Count)
            throw new ArgumentException("Mission keys must be unique.", nameof(Missions));
        if (Release1Recognized && RecognitionLogicalCorrelationIds.Count == 0)
            throw new ArgumentException("Recognized stories require a recognition correlation.", nameof(Release1Recognized));
        if (!Release1Recognized && RecognitionLogicalCorrelationIds.Count != 0)
            throw new ArgumentException("Unrecognized stories cannot contain recognition correlations.", nameof(RecognitionLogicalCorrelationIds));
        if (RelationshipState == Release1RelationshipState.Accepted && IntroLogicalCorrelationIds.Count == 0)
            throw new ArgumentException("Accepted stories require an intro correlation.", nameof(RelationshipState));
        if (IntroLogicalCorrelationIds.Concat(RecognitionLogicalCorrelationIds).Distinct(StringComparer.Ordinal).Count() !=
            IntroLogicalCorrelationIds.Count + RecognitionLogicalCorrelationIds.Count)
            throw new ArgumentException("Story correlations must be unique.");
        foreach (var correlation in IntroLogicalCorrelationIds)
        {
            if (!Release1LogicalCorrelation.TryParse(correlation, out var parsed) || parsed.PlayerId != PlayerId || parsed.MissionKey != Release1MissionCatalog.IntroScopeKey || parsed.TransitionKind is not (Release1TransitionKind.IntroAccepted or Release1TransitionKind.IntroDeferred))
                throw new ArgumentException("Intro correlations must be canonical intro correlations.", nameof(IntroLogicalCorrelationIds));
        }
        foreach (var correlation in RecognitionLogicalCorrelationIds)
        {
            if (!Release1LogicalCorrelation.TryParse(correlation, out var parsed) || parsed.PlayerId != PlayerId || parsed.MissionKey != Release1MissionCatalog.IntroScopeKey || parsed.TransitionKind != Release1TransitionKind.Release1Recognized)
                throw new ArgumentException("Recognition correlations must be canonical recognition correlations.", nameof(RecognitionLogicalCorrelationIds));
        }
        if (!Enum.IsDefined(RelationshipState)) throw new ArgumentException("Relationship state is not defined.", nameof(RelationshipState));
        var effectIds = NativeEffects.Select(effect => effect?.EffectId ?? throw new ArgumentException("Native effect records cannot be null.", nameof(NativeEffects))).ToArray();
        foreach (var effect in NativeEffects)
        {
            effect.Validate();
            if (string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal))
            {
                // OC-73 review fix. Release1ChiefLedger.Observe's Adopted-or-Settled reset clears
                // ChiefRecord's own AcceptedLogicalCorrelations/NativeEffectIds and DemandRound the
                // moment the next arrest reopens the demand (see Release1ChiefRecord's own doc
                // comment), so a payoff settled at round 2 or 3 leaves a Committed effect this check
                // can no longer find on the live record. A closed cycle's Committed effect is exactly
                // that: a historical, immutable receipt that no longer needs a live link, only its
                // own internal consistency and the ladder's cap. An ExecutionBlocked effect is the
                // second, later addition to this same relaxation: Release1ChiefService.RunPay blocks
                // (rather than reopens) any debit it cannot confirm as Succeeded, and reopens the
                // demand only for the one status (Unavailable) known to have moved no cash, leaving
                // that reopened round's now-superseded effect permanently ExecutionBlocked. It is just
                // as historical and immutable as a Committed effect (nothing ever clears
                // ExecutionBlocked except MarkApplied, which a blocked effect can no longer reach), so
                // it needs the same reset survival.
                //
                // OC-73 settled drain fix. Applied is the third addition, and the reasoning above ("a
                // Prepared-and-not-blocked nor an Applied Chief effect can ever outlive a reset")
                // no longer holds: Release1ChiefService.ContinuePayment used to hold the whole pass
                // (never reaching DrainOne, so never reaching the reset) until a Settled record's
                // Applied effect either committed or a save persisted it. That is exactly the live
                // defect this fix closes: a settled payoff must not block every arrest and tier
                // crossing until the next save. DrainOne can now run the very next pass, so an arrest
                // can reset ChiefRecord's own link out from under a still-Applied, not-yet-committed
                // effect before Release1ChiefService.CommitAppliedChiefCashEffects (which sweeps the
                // journal by id prefix, not by this link) gets a chance to commit it. An Applied effect
                // is exactly as historical from the live record's own point of view as a Committed or
                // ExecutionBlocked one the moment it stops being actively driven through this link; a
                // still-Prepared effect cannot reach here at all, since ContinuePayment still holds the
                // whole pass for that one remaining case.
                var closedCycle = effect.Phase is Release1NativeEffectPhase.Committed or Release1NativeEffectPhase.Applied || effect.ExecutionBlocked;
                if (ChiefRecord is null || effect.AuthorizedStoryCorrelationId is null || effect.AuthorizedMissionRevision < 0 ||
                    !Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var chiefAuthorization) ||
                    chiefAuthorization.PlayerId != PlayerId || chiefAuthorization.MissionKey != effect.MissionKey ||
                    chiefAuthorization.Attempt != effect.Attempt ||
                    chiefAuthorization.TransitionKind != Release1TransitionKind.ChiefPaymentAccepted ||
                    !string.Equals(effect.EffectKind, "CashTransfer", StringComparison.Ordinal) ||
                    effect.PreparedStoryRevision > Revision ||
                    effect.Attempt > (closedCycle ? Release1ChiefRecord.MaximumDemandRound : ChiefRecord.DemandRound) ||
                    effect.AuthorizedMissionRevision > ChiefRecord.Revision ||
                    (!closedCycle && !ChiefRecord.NativeEffectIds.Contains(effect.EffectId, StringComparer.Ordinal)) ||
                    (!closedCycle && !ChiefRecord.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal)))
                    throw new ArgumentException("Chief Campbell native effect was not linked to his record.", nameof(NativeEffects));
                if (effect.CommittedStoryCorrelationId is not null && effect.CommittedStoryCorrelationId != effect.AuthorizedStoryCorrelationId)
                    throw new ArgumentException("Committed effects must retain their authorization correlation.", nameof(NativeEffects));
                continue;
            }
            if (effect.AuthorizedStoryCorrelationId is null || effect.AuthorizedMissionRevision < 0 || !Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) ||
                authorization.PlayerId != PlayerId || authorization.MissionKey != effect.MissionKey || authorization.Attempt != effect.Attempt ||
                authorization.TransitionKind is not (Release1TransitionKind.MissionAccepted or Release1TransitionKind.MissionActivated or Release1TransitionKind.MissionCompleted or Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted) ||
                effect.PreparedStoryRevision > Revision)
                throw new ArgumentException("Native effect authorization did not match this story.", nameof(NativeEffects));
            var containingMission = Missions.SingleOrDefault(m => m.MissionKey == effect.MissionKey && m.Attempt >= effect.Attempt);
            if (containingMission is null || effect.AuthorizedMissionRevision > containingMission.Revision || !containingMission.NativeEffectIds.Contains(effect.EffectId, StringComparer.Ordinal) || !containingMission.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Native effect was not linked to its containing mission.", nameof(NativeEffects));
            if (authorization.TransitionKind == Release1TransitionKind.MissionCompleted && containingMission.State != Release1MissionState.Satisfied)
                throw new ArgumentException("Completion effects require a satisfied mission.", nameof(NativeEffects));
            if (string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal) && authorization.TransitionKind != Release1TransitionKind.MissionCompleted)
                throw new ArgumentException("Reward effects require a completion authorization.", nameof(NativeEffects));
            if (effect.CommittedStoryCorrelationId is not null && effect.CommittedStoryCorrelationId != effect.AuthorizedStoryCorrelationId)
                throw new ArgumentException("Committed effects must retain their authorization correlation.", nameof(NativeEffects));
        }
        if (effectIds.Distinct(StringComparer.Ordinal).Count() != effectIds.Length)
            throw new ArgumentException("Native effect IDs must be unique.", nameof(NativeEffects));
        if (ChiefRecord is not null)
        {
            ChiefRecord.Validate();
            foreach (var effectId in ChiefRecord.NativeEffectIds)
            {
                var chiefEffect = NativeEffects.SingleOrDefault(candidate => candidate.EffectId == effectId);
                if (chiefEffect is null ||
                    !string.Equals(chiefEffect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal) ||
                    chiefEffect.Attempt > ChiefRecord.DemandRound)
                    throw new ArgumentException("Chief Campbell record effect references were not durable and contextual.", nameof(ChiefRecord));
            }
        }
        var penaltyReceipts = Missions.SelectMany(mission => mission.PenaltyReceiptIds).ToArray();
        if (penaltyReceipts.Distinct(StringComparer.Ordinal).Count() != penaltyReceipts.Length)
            throw new ArgumentException("Penalty receipts must be globally unique.", nameof(Missions));
        foreach (var mission in Missions)
            foreach (var effectId in mission.NativeEffectIds)
            {
                var effect = NativeEffects.SingleOrDefault(candidate => candidate.EffectId == effectId);
                if (effect is null || effect.MissionKey != mission.MissionKey || effect.Attempt > mission.Attempt)
                    throw new ArgumentException("Mission native-effect references were not durable and contextual.", nameof(Missions));
        }
        foreach (var presentation in PhonePresentationAttempts)
        {
            presentation.Validate();
            if (!Release1LogicalCorrelation.TryParse(presentation.CorrelationId, out var correlation) ||
                correlation.PlayerId != PlayerId || correlation.MissionKey != presentation.MissionKey ||
                correlation.Attempt != presentation.Attempt ||
                !Missions.Any(mission => mission.MissionKey == presentation.MissionKey && mission.Attempt >= presentation.Attempt))
                throw new ArgumentException("Phone presentation attempt did not match story identity or mission state.", nameof(PhonePresentationAttempts));
        }
        if (PhonePresentationAttempts.Select(attempt => attempt.CorrelationId).Distinct(StringComparer.Ordinal).Count() != PhonePresentationAttempts.Count)
            throw new ArgumentException("Phone presentation correlations must be unique.", nameof(PhonePresentationAttempts));
        foreach (var receipt in PresentationReceipts)
        {
            if (receipt is null) throw new ArgumentException("Presentation receipts cannot be null.", nameof(PresentationReceipts));
            receipt.Validate();
            if (!Release1LogicalCorrelation.TryParse(receipt.CorrelationId, out var correlation) || correlation.PlayerId != PlayerId)
                throw new ArgumentException("Presentation receipt did not match story identity.", nameof(PresentationReceipts));
        }
        if (PresentationReceipts.Select(receipt => receipt.CorrelationId).Distinct(StringComparer.Ordinal).Count() != PresentationReceipts.Count)
            throw new ArgumentException("Presentation receipt correlations must be unique.", nameof(PresentationReceipts));
        ValidateSmallCourtesyAssignments();
        ValidateWrongAddress();
        ValidateRoomWithNoName();
        ValidateShortNotice();
        ValidateKeepTheLightsOff();
        ValidateTheEnvelope();
    }

    public bool ValueEquals(Release1StoryState? other) =>
        other is not null && PlayerId == other.PlayerId && Standing == other.Standing &&
        RelationshipState == other.RelationshipState && Release1Recognized == other.Release1Recognized &&
        Revision == other.Revision && SequenceEqual(IntroLogicalCorrelationIds, other.IntroLogicalCorrelationIds) &&
        SequenceEqual(RecognitionLogicalCorrelationIds, other.RecognitionLogicalCorrelationIds) &&
        Missions.Count == other.Missions.Count && Missions.Zip(other.Missions).All(pair => pair.First.ValueEquals(pair.Second)) &&
        NativeEffects.Count == other.NativeEffects.Count && NativeEffects.Zip(other.NativeEffects).All(pair => pair.First.ValueEquals(pair.Second)) &&
        PhonePresentationAttempts.Count == other.PhonePresentationAttempts.Count &&
        PhonePresentationAttempts.Zip(other.PhonePresentationAttempts).All(pair => pair.First == pair.Second) &&
        SmallCourtesyAssignments.Count == other.SmallCourtesyAssignments.Count &&
        SmallCourtesyAssignments.Zip(other.SmallCourtesyAssignments).All(pair => pair.First == pair.Second) &&
        PresentationReceipts.Count == other.PresentationReceipts.Count &&
        PresentationReceipts.Zip(other.PresentationReceipts).All(pair => pair.First == pair.Second) &&
        WrongAddressAssignments.Count == other.WrongAddressAssignments.Count &&
        WrongAddressAssignments.Zip(other.WrongAddressAssignments).All(pair => pair.First == pair.Second) &&
        WrongAddressProgress.Count == other.WrongAddressProgress.Count &&
        WrongAddressProgress.Zip(other.WrongAddressProgress).All(pair => pair.First == pair.Second) &&
        RoomWithNoNameAssignments.Count == other.RoomWithNoNameAssignments.Count &&
        RoomWithNoNameAssignments.Zip(other.RoomWithNoNameAssignments).All(pair => pair.First == pair.Second) &&
        RoomWithNoNameProgress.Count == other.RoomWithNoNameProgress.Count &&
        RoomWithNoNameProgress.Zip(other.RoomWithNoNameProgress).All(pair => pair.First == pair.Second) &&
        ShortNoticeAssignments.Count == other.ShortNoticeAssignments.Count &&
        ShortNoticeAssignments.Zip(other.ShortNoticeAssignments).All(pair => pair.First == pair.Second) &&
        ShortNoticeProgress.Count == other.ShortNoticeProgress.Count &&
        ShortNoticeProgress.Zip(other.ShortNoticeProgress).All(pair => pair.First == pair.Second) &&
        KeepTheLightsOffAssignments.Count == other.KeepTheLightsOffAssignments.Count &&
        KeepTheLightsOffAssignments.Zip(other.KeepTheLightsOffAssignments).All(pair => pair.First == pair.Second) &&
        KeepTheLightsOffProgress.Count == other.KeepTheLightsOffProgress.Count &&
        KeepTheLightsOffProgress.Zip(other.KeepTheLightsOffProgress).All(pair => pair.First == pair.Second) &&
        TheEnvelopeAssignments.Count == other.TheEnvelopeAssignments.Count &&
        TheEnvelopeAssignments.Zip(other.TheEnvelopeAssignments).All(pair => pair.First == pair.Second) &&
        TheEnvelopeProgress.Count == other.TheEnvelopeProgress.Count &&
        TheEnvelopeProgress.Zip(other.TheEnvelopeProgress).All(pair => pair.First == pair.Second) &&
        (ChiefRecord is null) == (other.ChiefRecord is null) &&
        (ChiefRecord is null || ChiefRecord.ValueEquals(other.ChiefRecord));

    public bool Equals(Release1StoryState? other) => ValueEquals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PlayerId); hash.Add(Standing); hash.Add(RelationshipState); hash.Add(Release1Recognized); hash.Add(Revision);
        foreach (var value in IntroLogicalCorrelationIds) hash.Add(value);
        foreach (var value in RecognitionLogicalCorrelationIds) hash.Add(value);
        foreach (var mission in Missions) hash.Add(mission);
        foreach (var effect in NativeEffects) hash.Add(effect);
        foreach (var presentation in PhonePresentationAttempts) hash.Add(presentation);
        foreach (var assignment in SmallCourtesyAssignments) hash.Add(assignment);
        foreach (var receipt in PresentationReceipts) hash.Add(receipt);
        foreach (var assignment in WrongAddressAssignments) hash.Add(assignment);
        foreach (var progress in WrongAddressProgress) hash.Add(progress);
        foreach (var assignment in RoomWithNoNameAssignments) hash.Add(assignment);
        foreach (var progress in RoomWithNoNameProgress) hash.Add(progress);
        foreach (var assignment in ShortNoticeAssignments) hash.Add(assignment);
        foreach (var progress in ShortNoticeProgress) hash.Add(progress);
        foreach (var assignment in KeepTheLightsOffAssignments) hash.Add(assignment);
        foreach (var progress in KeepTheLightsOffProgress) hash.Add(progress);
        foreach (var assignment in TheEnvelopeAssignments) hash.Add(assignment);
        foreach (var progress in TheEnvelopeProgress) hash.Add(progress);
        hash.Add(ChiefRecord);
        return hash.ToHashCode();
    }

    private void ValidateSmallCourtesyAssignments()
    {
        var smallCourtesyMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.SmallCourtesy);
        foreach (var assignment in SmallCourtesyAssignments)
        {
            if (assignment is null) throw new ArgumentException("Small Courtesy assignments cannot be null.", nameof(SmallCourtesyAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("Small Courtesy assignment did not match story identity.", nameof(SmallCourtesyAssignments));
            if (smallCourtesyMission.Attempt < assignment.Attempt ||
                !smallCourtesyMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Small Courtesy assignment was not authorized by its containing mission attempt.", nameof(SmallCourtesyAssignments));
        }
        if (SmallCourtesyAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != SmallCourtesyAssignments.Count)
            throw new ArgumentException("Small Courtesy mission-attempt assignments must be unique.", nameof(SmallCourtesyAssignments));
        if (SmallCourtesyAssignments.Count == 0) return;

        var first = SmallCourtesyAssignments[0];
        if (SmallCourtesyAssignments.Any(assignment =>
                !string.Equals(assignment.ProductId, first.ProductId, StringComparison.Ordinal) ||
                !string.Equals(assignment.ProductName, first.ProductName, StringComparison.Ordinal)))
            throw new ArgumentException("Small Courtesy product identity must remain frozen across attempts.", nameof(SmallCourtesyAssignments));

        var laterStage = SmallCourtesyAssignments.FirstOrDefault(assignment => assignment.Mode != Release1SmallCourtesyAssignmentMode.Primary);
        if (laterStage is not null && SmallCourtesyAssignments.Any(assignment =>
                assignment.Mode != Release1SmallCourtesyAssignmentMode.Primary &&
                (!string.Equals(assignment.PackagingId, laterStage.PackagingId, StringComparison.Ordinal) ||
                 !string.Equals(assignment.PackagingName, laterStage.PackagingName, StringComparison.Ordinal))))
            throw new ArgumentException("Small Courtesy make-good and recovery packaging must remain frozen.", nameof(SmallCourtesyAssignments));
    }

    private void ValidateWrongAddress()
    {
        var wrongAddressMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.WrongAddress);
        foreach (var assignment in WrongAddressAssignments)
        {
            if (assignment is null) throw new ArgumentException("Wrong Address assignments cannot be null.", nameof(WrongAddressAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("Wrong Address assignment did not match story identity.", nameof(WrongAddressAssignments));
            if (wrongAddressMission.Attempt < assignment.Attempt ||
                !wrongAddressMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Wrong Address assignment was not authorized by its containing mission attempt.", nameof(WrongAddressAssignments));
        }
        if (WrongAddressAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != WrongAddressAssignments.Count)
            throw new ArgumentException("Wrong Address mission-attempt assignments must be unique.", nameof(WrongAddressAssignments));

        if (WrongAddressAssignments.Count > 0)
        {
            var first = WrongAddressAssignments[0];
            if (WrongAddressAssignments.Any(assignment =>
                    !string.Equals(assignment.ProductId, first.ProductId, StringComparison.Ordinal) ||
                    !string.Equals(assignment.ProductName, first.ProductName, StringComparison.Ordinal)))
                throw new ArgumentException("Wrong Address product identity must remain frozen across attempts.", nameof(WrongAddressAssignments));

            var laterStage = WrongAddressAssignments.FirstOrDefault(assignment => assignment.Mode != Release1WrongAddressAssignmentMode.Primary);
            if (laterStage is not null && WrongAddressAssignments.Any(assignment =>
                    assignment.Mode != Release1WrongAddressAssignmentMode.Primary &&
                    (!string.Equals(assignment.PackagingId, laterStage.PackagingId, StringComparison.Ordinal) ||
                     !string.Equals(assignment.PackagingName, laterStage.PackagingName, StringComparison.Ordinal))))
                throw new ArgumentException("Wrong Address make-good and recovery packaging must remain frozen.", nameof(WrongAddressAssignments));
        }

        foreach (var progress in WrongAddressProgress)
        {
            if (progress is null) throw new ArgumentException("Wrong Address progress cannot be null.", nameof(WrongAddressProgress));
            progress.Validate();
            if (!WrongAddressAssignments.Any(assignment => assignment.Attempt == progress.Attempt))
                throw new ArgumentException("Wrong Address progress had no accepted assignment for its attempt.", nameof(WrongAddressProgress));
        }
        if (WrongAddressProgress.Select(progress => (progress.MissionKey, progress.Attempt)).Distinct().Count() != WrongAddressProgress.Count)
            throw new ArgumentException("Wrong Address mission-attempt progress must be unique.", nameof(WrongAddressProgress));
    }

    private void ValidateRoomWithNoName()
    {
        var roomMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.RoomWithNoName);
        foreach (var assignment in RoomWithNoNameAssignments)
        {
            if (assignment is null) throw new ArgumentException("Room With No Name assignments cannot be null.", nameof(RoomWithNoNameAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("Room With No Name assignment did not match story identity.", nameof(RoomWithNoNameAssignments));
            if (roomMission.Attempt < assignment.Attempt ||
                !roomMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Room With No Name assignment was not authorized by its containing mission attempt.", nameof(RoomWithNoNameAssignments));
        }
        if (RoomWithNoNameAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != RoomWithNoNameAssignments.Count)
            throw new ArgumentException("Room With No Name mission-attempt assignments must be unique.", nameof(RoomWithNoNameAssignments));

        if (RoomWithNoNameAssignments.Count > 0)
        {
            var first = RoomWithNoNameAssignments[0];
            if (RoomWithNoNameAssignments.Any(assignment =>
                    !string.Equals(assignment.ProductId, first.ProductId, StringComparison.Ordinal) ||
                    !string.Equals(assignment.ProductName, first.ProductName, StringComparison.Ordinal)))
                throw new ArgumentException("Room With No Name product identity must remain frozen across attempts.", nameof(RoomWithNoNameAssignments));

            var laterStage = RoomWithNoNameAssignments.FirstOrDefault(assignment => assignment.Mode != Release1RoomWithNoNameAssignmentMode.Primary);
            if (laterStage is not null && RoomWithNoNameAssignments.Any(assignment =>
                    assignment.Mode != Release1RoomWithNoNameAssignmentMode.Primary &&
                    (!string.Equals(assignment.PackagingId, laterStage.PackagingId, StringComparison.Ordinal) ||
                     !string.Equals(assignment.PackagingName, laterStage.PackagingName, StringComparison.Ordinal))))
                throw new ArgumentException("Room With No Name make-good and recovery packaging must remain frozen.", nameof(RoomWithNoNameAssignments));
        }

        foreach (var progress in RoomWithNoNameProgress)
        {
            if (progress is null) throw new ArgumentException("Room With No Name progress cannot be null.", nameof(RoomWithNoNameProgress));
            progress.Validate();
            if (!RoomWithNoNameAssignments.Any(assignment => assignment.Attempt == progress.Attempt))
                throw new ArgumentException("Room With No Name progress had no accepted assignment for its attempt.", nameof(RoomWithNoNameProgress));
        }
        if (RoomWithNoNameProgress.Select(progress => (progress.MissionKey, progress.Attempt)).Distinct().Count() != RoomWithNoNameProgress.Count)
            throw new ArgumentException("Room With No Name mission-attempt progress must be unique.", nameof(RoomWithNoNameProgress));
    }

    private void ValidateShortNotice()
    {
        var shortNoticeMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.ShortNotice);
        foreach (var assignment in ShortNoticeAssignments)
        {
            if (assignment is null) throw new ArgumentException("Short Notice assignments cannot be null.", nameof(ShortNoticeAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("Short Notice assignment did not match story identity.", nameof(ShortNoticeAssignments));
            if (shortNoticeMission.Attempt < assignment.Attempt ||
                !shortNoticeMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Short Notice assignment was not authorized by its containing mission attempt.", nameof(ShortNoticeAssignments));
        }
        if (ShortNoticeAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != ShortNoticeAssignments.Count)
            throw new ArgumentException("Short Notice mission-attempt assignments must be unique.", nameof(ShortNoticeAssignments));

        if (ShortNoticeAssignments.Count > 0)
        {
            var first = ShortNoticeAssignments[0];
            if (ShortNoticeAssignments.Any(assignment =>
                    !string.Equals(assignment.ProductId, first.ProductId, StringComparison.Ordinal) ||
                    !string.Equals(assignment.ProductName, first.ProductName, StringComparison.Ordinal) ||
                    !string.Equals(assignment.PackagingId, first.PackagingId, StringComparison.Ordinal) ||
                    !string.Equals(assignment.PackagingName, first.PackagingName, StringComparison.Ordinal) ||
                    assignment.ValueConvention != first.ValueConvention))
                throw new ArgumentException("Short Notice product, packaging, and value convention must remain frozen across attempts.", nameof(ShortNoticeAssignments));
        }

        foreach (var progress in ShortNoticeProgress)
        {
            if (progress is null) throw new ArgumentException("Short Notice progress cannot be null.", nameof(ShortNoticeProgress));
            progress.Validate();
            if (!ShortNoticeAssignments.Any(assignment => assignment.Attempt == progress.Attempt))
                throw new ArgumentException("Short Notice progress had no accepted assignment for its attempt.", nameof(ShortNoticeProgress));
        }
        if (ShortNoticeProgress.Select(progress => (progress.MissionKey, progress.Attempt)).Distinct().Count() != ShortNoticeProgress.Count)
            throw new ArgumentException("Short Notice mission-attempt progress must be unique.", nameof(ShortNoticeProgress));
    }

    private void ValidateKeepTheLightsOff()
    {
        var keepTheLightsOffMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.KeepTheLightsOff);
        foreach (var assignment in KeepTheLightsOffAssignments)
        {
            if (assignment is null) throw new ArgumentException("Keep the Lights Off assignments cannot be null.", nameof(KeepTheLightsOffAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("Keep the Lights Off assignment did not match story identity.", nameof(KeepTheLightsOffAssignments));
            if (keepTheLightsOffMission.Attempt < assignment.Attempt ||
                !keepTheLightsOffMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("Keep the Lights Off assignment was not authorized by its containing mission attempt.", nameof(KeepTheLightsOffAssignments));
        }
        if (KeepTheLightsOffAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != KeepTheLightsOffAssignments.Count)
            throw new ArgumentException("Keep the Lights Off mission-attempt assignments must be unique.", nameof(KeepTheLightsOffAssignments));

        foreach (var progress in KeepTheLightsOffProgress)
        {
            if (progress is null) throw new ArgumentException("Keep the Lights Off progress cannot be null.", nameof(KeepTheLightsOffProgress));
            progress.Validate();
            if (!KeepTheLightsOffAssignments.Any(assignment => assignment.Attempt == progress.Attempt))
                throw new ArgumentException("Keep the Lights Off progress had no accepted assignment for its attempt.", nameof(KeepTheLightsOffProgress));
        }
        if (KeepTheLightsOffProgress.Select(progress => (progress.MissionKey, progress.Attempt)).Distinct().Count() != KeepTheLightsOffProgress.Count)
            throw new ArgumentException("Keep the Lights Off mission-attempt progress must be unique.", nameof(KeepTheLightsOffProgress));
    }

    private void ValidateTheEnvelope()
    {
        var envelopeMission = Missions.Single(mission => mission.MissionKey == Release1MissionCatalog.TheEnvelope);
        foreach (var assignment in TheEnvelopeAssignments)
        {
            if (assignment is null) throw new ArgumentException("The Envelope assignments cannot be null.", nameof(TheEnvelopeAssignments));
            assignment.Validate();
            if (!Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var authorization) || authorization.PlayerId != PlayerId)
                throw new ArgumentException("The Envelope assignment did not match story identity.", nameof(TheEnvelopeAssignments));
            if (envelopeMission.Attempt < assignment.Attempt ||
                !envelopeMission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                throw new ArgumentException("The Envelope assignment was not authorized by its containing mission attempt.", nameof(TheEnvelopeAssignments));
        }
        if (TheEnvelopeAssignments.Select(assignment => (assignment.MissionKey, assignment.Attempt)).Distinct().Count() != TheEnvelopeAssignments.Count)
            throw new ArgumentException("The Envelope mission-attempt assignments must be unique.", nameof(TheEnvelopeAssignments));

        foreach (var progress in TheEnvelopeProgress)
        {
            if (progress is null) throw new ArgumentException("The Envelope progress cannot be null.", nameof(TheEnvelopeProgress));
            progress.Validate();
            if (!TheEnvelopeAssignments.Any(assignment => assignment.Attempt == progress.Attempt))
                throw new ArgumentException("The Envelope progress had no accepted assignment for its attempt.", nameof(TheEnvelopeProgress));
        }
        if (TheEnvelopeProgress.Select(progress => (progress.MissionKey, progress.Attempt)).Distinct().Count() != TheEnvelopeProgress.Count)
            throw new ArgumentException("The Envelope mission-attempt progress must be unique.", nameof(TheEnvelopeProgress));
    }

    private static Release1MissionRecord NewMission(string key, Release1MissionState state) => new(
        key, state, 1, null, null, null, Release1MissionOutcome.None, 0,
        Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0);

    private static IReadOnlyList<string> CopyIds(IReadOnlyList<string>? values, string name)
    {
        if (values is null) throw new ArgumentNullException(name);
        var copy = values.ToArray();
        foreach (var value in copy) Release1MissionRecord.ValidateCorrelation(value, name);
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("IDs must be unique.", name);
        return copy;
    }

    private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? values, string parameterName)
    {
        if (values is null) throw new ArgumentNullException(parameterName);
        return ImmutableArray.CreateRange(values);
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.Ordinal);

    internal static void ValidatePlayerId(string? playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128 || playerId.Any(char.IsControl) ||
            playerId.Any(char.IsWhiteSpace) || playerId.Contains('/'))
            throw new ArgumentException("Player identity is not canonical.", nameof(playerId));
    }
}
