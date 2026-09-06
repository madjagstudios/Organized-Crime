namespace OrganizedCrime.Model;

public enum Release1TransitionKind
{
    IntroAccepted,
    IntroDeferred,
    MissionOffered,
    MissionDeferred,
    MissionReoffered,
    MissionAccepted,
    MissionActivated,
    MissionCompleted,
    MissionAbandoned,
    RequiredFailure,
    MakeGoodOffered,
    MakeGoodDeferred,
    MakeGoodAccepted,
    MakeGoodFailed,
    RecoveryAccepted,
    RecoveryFailed,
    Release1Recognized,

    /// <summary>
    /// OC-73. Authorizes Chief Campbell's one CashTransfer and nothing else. Never added to the five
    /// case switch in Release1StoryRuntimeService.IsCurrentEffectAuthorization: none of those five
    /// cases describes a mission state that exists for a scope key absent from
    /// Release1MissionCatalog.All.
    /// </summary>
    ChiefPaymentAccepted
}

public sealed record Release1StoryCommand(
    Guid SessionEpoch,
    long LoadEpoch,
    string PlayerId,
    string MissionKey,
    int Attempt,
    Release1TransitionKind TransitionKind,
    string ReceiptId,
    string CorrelationId,
    string? TermsVersion = null,
    double? AcceptedGameTimeHours = null,
    double? DeadlineGameTimeHours = null,
    Release1CompletionTiming? CompletionTiming = null,
    string? RewardAuthorizationReceiptId = null,
    string? QuietConditionReceiptId = null,
    string? RecognitionReceiptId = null,
    Release1NativeEffectJournalEntry? PreparedNativeEffect = null);

public enum Release1StoryTransitionRejectReason
{
    None,
    MissingState,
    InvalidCommand,
    InvalidCorrelation,
    CorrelationMismatch,
    UnknownMission,
    WrongState,
    WrongAttempt,
    InvalidReceipt,
    DuplicateReceipt,
    InvalidTerms,
    InvalidTime,
    MissingCompletionTiming,
    MissingRewardAuthorization,
    MissingQuietReceipt,
    MissingRecognitionReceipt,
    TerminalState
}

public sealed record Release1StoryTransitionResult(
    bool Accepted,
    bool Idempotent,
    Release1StoryState? State,
    Release1StoryTransitionRejectReason RejectReason,
    string Message)
{
    public bool Changed => Accepted && !Idempotent;

    public static Release1StoryTransitionResult Reject(Release1StoryTransitionRejectReason reason, string message, Release1StoryState? state = null) =>
        new(false, false, state, reason, message);

    public static Release1StoryTransitionResult AcceptedState(Release1StoryState state, bool idempotent = false, string? message = null) =>
        new(true, idempotent, state, Release1StoryTransitionRejectReason.None, message ?? string.Empty);
}

public static class Release1StoryTransitions
{
    public static Release1StoryTransitionResult Apply(Release1StoryState? state, Release1StoryCommand command)
    {
        if (command is null) return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.InvalidCommand, "Command was null.", state);
        if (!Release1LogicalCorrelation.TryParse(command.CorrelationId, out var correlation))
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.InvalidCorrelation, "Correlation was not canonical.", state);
        if (correlation.PlayerId != command.PlayerId || correlation.MissionKey != command.MissionKey ||
            correlation.Attempt != command.Attempt || correlation.TransitionKind != command.TransitionKind ||
            correlation.ReceiptId != command.ReceiptId)
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.CorrelationMismatch, "Correlation did not match the command.", state);
        try { Release1StoryState.ValidatePlayerId(command.PlayerId); Release1MissionRecord.ValidateId(command.ReceiptId, nameof(command.ReceiptId)); }
        catch (ArgumentException ex) { return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.InvalidReceipt, ex.Message, state); }

        if (state is null)
        {
            if (command.TransitionKind == Release1TransitionKind.IntroAccepted && command.MissionKey == Release1MissionCatalog.IntroScopeKey && command.Attempt == 0)
                return Release1StoryTransitionResult.AcceptedState(Release1StoryState.CreateAccepted(command.PlayerId, command.CorrelationId));
            if (command.TransitionKind == Release1TransitionKind.IntroDeferred && command.MissionKey == Release1MissionCatalog.IntroScopeKey && command.Attempt == 0)
                return Release1StoryTransitionResult.AcceptedState(CreateDeferred(command.PlayerId, command.CorrelationId));
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.MissingState, "Intro acceptance is required before mission transitions.");
        }
        if (state.PlayerId != command.PlayerId) return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.CorrelationMismatch, "Player identity did not match state.", state);
        if (state.IntroLogicalCorrelationIds.Contains(command.CorrelationId, StringComparer.Ordinal) ||
            state.RecognitionLogicalCorrelationIds.Contains(command.CorrelationId, StringComparer.Ordinal))
            return Release1StoryTransitionResult.AcceptedState(state, idempotent: true, "Duplicate logical correlation was already accepted.");
        if (command.MissionKey == Release1MissionCatalog.IntroScopeKey)
        {
            if (command.TransitionKind == Release1TransitionKind.IntroAccepted && state.RelationshipState == Release1RelationshipState.Deferred)
            {
                var missions = state.Missions.Select((mission, i) => i == 0 && mission.State == Release1MissionState.Locked ? mission with { State = Release1MissionState.Offered, Revision = mission.Revision + 1 } : mission).ToArray();
                return Release1StoryTransitionResult.AcceptedState(state with
                {
                    RelationshipState = Release1RelationshipState.Accepted,
                    IntroLogicalCorrelationIds = state.IntroLogicalCorrelationIds.Append(command.CorrelationId).ToArray(),
                    Missions = missions,
                    Revision = state.Revision + 1
                });
            }
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.TerminalState, "Intro can only be accepted once.", state);
        }
        if (!Release1MissionCatalog.IsMissionKey(command.MissionKey))
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.UnknownMission, "Mission key is not canonical.", state);
        if (command.Attempt < 1) return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.WrongAttempt, "Mission attempt must be positive.", state);

        var index = Release1MissionCatalog.IndexOf(command.MissionKey);
        var mission = state.Missions[index];
        if (mission.AcceptedLogicalCorrelations.Contains(command.CorrelationId, StringComparer.Ordinal))
            return Release1StoryTransitionResult.AcceptedState(state, idempotent: true, "Duplicate logical correlation was already accepted.");
        var acceptsNextAttempt = command.TransitionKind is Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted;
        if (mission.Attempt != command.Attempt && (!acceptsNextAttempt || mission.Attempt + 1 != command.Attempt))
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.WrongAttempt, "Mission attempt did not match state.", state);
        if (index > 0 && state.Missions[index - 1].State != Release1MissionState.Satisfied &&
            command.TransitionKind is Release1TransitionKind.MissionOffered or Release1TransitionKind.MissionReoffered or Release1TransitionKind.MissionAccepted or Release1TransitionKind.MissionActivated or Release1TransitionKind.MissionCompleted)
            return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.WrongState, "Prior mission is not satisfied.", state);

        try
        {
            return command.TransitionKind switch
            {
                Release1TransitionKind.MissionOffered => ChangeMission(state, index, mission.State == Release1MissionState.Locked ? mission with { State = Release1MissionState.Offered } : throw WrongState(), command.CorrelationId),
                Release1TransitionKind.MissionDeferred => ChangeMission(state, index, Require(mission, Release1MissionState.Offered) with { State = Release1MissionState.Deferred }, command.CorrelationId),
                Release1TransitionKind.MissionReoffered => Reoffer(state, index, mission, command),
                Release1TransitionKind.MissionAccepted => AcceptMission(state, index, mission, command),
                Release1TransitionKind.MissionActivated => ChangeMission(state, index, Require(mission, Release1MissionState.Accepted) with { State = Release1MissionState.Active }, command.CorrelationId),
                Release1TransitionKind.MissionAbandoned => FailMission(state, index, mission, command, Release1MissionOutcome.Abandoned, 10, Release1MissionState.Abandoned, Release1MissionState.Accepted, Release1MissionState.Active, Release1MissionState.MakeGoodActive),
                Release1TransitionKind.RequiredFailure => FailMission(state, index, mission, command, Release1MissionOutcome.RequiredFailure, 12, Release1MissionState.MakeGoodOffered, Release1MissionState.Active, Release1MissionState.MakeGoodActive),
                Release1TransitionKind.MakeGoodOffered => mission.State == Release1MissionState.MakeGoodOffered ? Release1StoryTransitionResult.AcceptedState(state, idempotent: true, "Make-good was already offered.") : ChangeMission(state, index, mission.State == Release1MissionState.Abandoned ? mission with { State = Release1MissionState.MakeGoodOffered, RecoveryMode = Release1RecoveryMode.StandardMakeGood } : throw WrongState(), command.CorrelationId),
                Release1TransitionKind.MakeGoodDeferred => mission.State == Release1MissionState.MakeGoodOffered ? Release1StoryTransitionResult.AcceptedState(state, idempotent: true, "Make-good remains available.") : throw WrongState(),
                Release1TransitionKind.MakeGoodAccepted => AcceptMakeGood(state, index, mission, command),
                Release1TransitionKind.MakeGoodFailed => FailMission(state, index, mission, command, Release1MissionOutcome.MakeGoodFailure, 8, Release1MissionState.RecoveryAvailable, Release1MissionState.MakeGoodActive),
                Release1TransitionKind.RecoveryAccepted => AcceptRecovery(state, index, mission, command),
                Release1TransitionKind.RecoveryFailed => ChangeMission(state, index, Require(mission, Release1MissionState.RecoveryActive) with { State = Release1MissionState.RecoveryAvailable, Attempt = mission.Attempt + 1, LastOutcome = Release1MissionOutcome.RecoveryFailure }, command.CorrelationId),
                Release1TransitionKind.MissionCompleted => CompleteMission(state, index, mission, command),
                _ => Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.InvalidCommand, "Transition kind is not valid for a mission.", state)
            };
        }
        catch (TransitionException ex) { return Release1StoryTransitionResult.Reject(ex.Reason, ex.Message, state); }
        catch (ArgumentException ex) { return Release1StoryTransitionResult.Reject(Release1StoryTransitionRejectReason.InvalidCommand, ex.Message, state); }
    }

    private static Release1StoryState CreateDeferred(string playerId, string correlation) =>
        new(playerId, 20, Release1RelationshipState.Deferred, false, new[] { correlation }, Array.Empty<string>(),
            Release1MissionCatalog.All.Select(m => NewMission(m.MissionKey, Release1MissionState.Locked)).ToArray(), Array.Empty<Release1NativeEffectJournalEntry>(), 1);

    private static Release1MissionRecord NewMission(string key, Release1MissionState state) => new(key, state, 1, null, null, null, Release1MissionOutcome.None, 0, Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, 0);

    private static Release1StoryTransitionResult AcceptMission(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command)
    {
        if (mission.State != Release1MissionState.Offered) throw WrongState();
        if (string.IsNullOrWhiteSpace(command.TermsVersion)) throw new TransitionException(Release1StoryTransitionRejectReason.InvalidTerms, "Mission terms must be immutable and nonblank.");
        ValidateTimes(command.AcceptedGameTimeHours, command.DeadlineGameTimeHours);
        return ChangeMission(state, index, mission with { State = Release1MissionState.Accepted, TermsVersion = command.TermsVersion, AcceptedGameTimeHours = command.AcceptedGameTimeHours, DeadlineGameTimeHours = command.DeadlineGameTimeHours }, command.CorrelationId);
    }

    private static Release1StoryTransitionResult AcceptMakeGood(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command)
    {
        ValidateTimes(command.AcceptedGameTimeHours, command.DeadlineGameTimeHours);
        return ChangeMission(state, index, Require(mission, Release1MissionState.MakeGoodOffered) with { State = Release1MissionState.MakeGoodActive, Attempt = mission.Attempt + 1, RecoveryMode = Release1RecoveryMode.StandardMakeGood, AcceptedGameTimeHours = command.AcceptedGameTimeHours, DeadlineGameTimeHours = command.DeadlineGameTimeHours }, command.CorrelationId);
    }

    private static Release1StoryTransitionResult AcceptRecovery(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command)
    {
        if (command.DeadlineGameTimeHours is not null) throw new TransitionException(Release1StoryTransitionRejectReason.InvalidTime, "Guaranteed recovery cannot have a deadline.");
        ValidateTimes(command.AcceptedGameTimeHours, null);
        return ChangeMission(state, index, Require(mission, Release1MissionState.RecoveryAvailable) with { State = Release1MissionState.RecoveryActive, Attempt = mission.Attempt + 1, RecoveryMode = Release1RecoveryMode.GuaranteedRecovery, AcceptedGameTimeHours = command.AcceptedGameTimeHours, DeadlineGameTimeHours = null }, command.CorrelationId);
    }

    private static Release1StoryTransitionResult FailMission(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command, Release1MissionOutcome outcome, int penalty, Release1MissionState nextState, params Release1MissionState[] allowedStates)
    {
        if (!allowedStates.Contains(mission.State)) throw WrongState();
        if (state.Missions.Any(candidate => candidate.PenaltyReceiptIds.Contains(command.ReceiptId, StringComparer.Ordinal)))
            throw new TransitionException(Release1StoryTransitionRejectReason.DuplicateReceipt, "Penalty receipt was already recorded in story state.");
        if (mission.State == Release1MissionState.MakeGoodActive)
        {
            outcome = Release1MissionOutcome.MakeGoodFailure;
            penalty = 8;
            nextState = Release1MissionState.RecoveryAvailable;
        }
        if (outcome == Release1MissionOutcome.MakeGoodFailure && mission.MakeGoodFailures >= 1) throw new TransitionException(Release1StoryTransitionRejectReason.WrongState, "The standard make-good was already consumed.");
        var credited = Math.Min(penalty, 20 - mission.StandingPenaltyApplied);
        var updated = mission with
        {
            State = nextState,
            LastOutcome = outcome,
            StandingPenaltyApplied = mission.StandingPenaltyApplied + credited,
            PenaltyReceiptIds = mission.PenaltyReceiptIds.Append(command.ReceiptId).ToArray(),
            MakeGoodFailures = outcome == Release1MissionOutcome.MakeGoodFailure ? 1 : mission.MakeGoodFailures,
            RecoveryMode = nextState == Release1MissionState.RecoveryAvailable ? Release1RecoveryMode.GuaranteedRecovery : Release1RecoveryMode.StandardMakeGood,
            AcceptedGameTimeHours = nextState == Release1MissionState.RecoveryAvailable ? null : mission.AcceptedGameTimeHours,
            DeadlineGameTimeHours = nextState == Release1MissionState.RecoveryAvailable ? null : mission.DeadlineGameTimeHours,
            AcceptedLogicalCorrelations = mission.AcceptedLogicalCorrelations.Append(command.CorrelationId).ToArray()
        };
        return ChangeMission(state with { Standing = Math.Max(0, state.Standing - credited) }, index, updated);
    }

    private static Release1StoryTransitionResult CompleteMission(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command)
    {
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)) throw WrongState();
        if (command.PreparedNativeEffect is not null && (state.NativeEffects.Any(effect => effect.EffectId == command.PreparedNativeEffect.EffectId) || mission.NativeEffectIds.Contains(command.PreparedNativeEffect.EffectId, StringComparer.Ordinal)))
            throw new TransitionException(Release1StoryTransitionRejectReason.InvalidCommand, "Native effect ID was already recorded.");
        if (command.CompletionTiming is null) throw new TransitionException(Release1StoryTransitionRejectReason.MissingCompletionTiming, "Completion timing is required.");
        if (string.IsNullOrWhiteSpace(command.RewardAuthorizationReceiptId)) throw new TransitionException(Release1StoryTransitionRejectReason.MissingRewardAuthorization, "Reward authorization is required.");
        Release1MissionRecord.ValidateId(command.RewardAuthorizationReceiptId, nameof(command.RewardAuthorizationReceiptId));
        var quiet = command.QuietConditionReceiptId is not null;
        if (quiet && (command.QuietConditionReceiptId == command.ReceiptId || command.QuietConditionReceiptId == command.RewardAuthorizationReceiptId)) throw new TransitionException(Release1StoryTransitionRejectReason.MissingQuietReceipt, "Quiet receipt must be distinct.");
        if (command.QuietConditionReceiptId is not null) Release1MissionRecord.ValidateId(command.QuietConditionReceiptId, nameof(command.QuietConditionReceiptId));
        var isFinal = index == Release1MissionCatalog.All.Count - 1;
        string? recognition = null;
        if (isFinal)
        {
            if (string.IsNullOrWhiteSpace(command.RecognitionReceiptId) || command.RecognitionReceiptId == command.ReceiptId) throw new TransitionException(Release1StoryTransitionRejectReason.MissingRecognitionReceipt, "Final mission requires a distinct recognition receipt.");
            Release1MissionRecord.ValidateId(command.RecognitionReceiptId, nameof(command.RecognitionReceiptId));
            recognition = Release1LogicalCorrelation.Create(state.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.Release1Recognized, command.RecognitionReceiptId).Value;
        }
        var award = command.CompletionTiming == Release1CompletionTiming.OnTime ? 10 : 5;
        var standing = Math.Clamp(state.Standing + mission.StandingPenaltyApplied + award + (quiet ? 2 : 0), 0, 100);
        if (isFinal) standing = Math.Max(standing, 80);
        var updated = mission with
        {
            State = Release1MissionState.Satisfied,
            LastOutcome = command.CompletionTiming == Release1CompletionTiming.OnTime ? Release1MissionOutcome.OnTime : Release1MissionOutcome.Late,
            StandingPenaltyApplied = 0,
            RewardAuthorizationReceiptId = command.RewardAuthorizationReceiptId,
            QuietConditionAwarded = mission.QuietConditionAwarded || quiet,
            AcceptedLogicalCorrelations = mission.AcceptedLogicalCorrelations.Append(command.CorrelationId).ToArray(),
            NativeEffectIds = command.PreparedNativeEffect is null ? mission.NativeEffectIds : mission.NativeEffectIds.Append(command.PreparedNativeEffect.EffectId).ToArray()
        };
        if (command.PreparedNativeEffect is not null &&
            (command.PreparedNativeEffect.MissionKey != mission.MissionKey || command.PreparedNativeEffect.Attempt != mission.Attempt ||
             command.PreparedNativeEffect.Phase != Release1NativeEffectPhase.Prepared))
            throw new TransitionException(Release1StoryTransitionRejectReason.InvalidCommand, "Prepared native effect did not match the completing mission.");
        if (command.PreparedNativeEffect is not null && command.PreparedNativeEffect.AuthorizedStoryCorrelationId is not null && command.PreparedNativeEffect.AuthorizedStoryCorrelationId != command.CorrelationId)
            throw new TransitionException(Release1StoryTransitionRejectReason.InvalidCommand, "Prepared native effect authorization did not match the completing transition.");
        if (command.PreparedNativeEffect is not null && command.PreparedNativeEffect.AuthorizedMissionRevision >= 0 && command.PreparedNativeEffect.AuthorizedMissionRevision != mission.Revision)
            throw new TransitionException(Release1StoryTransitionRejectReason.InvalidCommand, "Prepared native effect mission revision did not match the completing transition.");
        var next = state with
        {
            Standing = standing,
            Release1Recognized = state.Release1Recognized || isFinal,
            RecognitionLogicalCorrelationIds = isFinal ? state.RecognitionLogicalCorrelationIds.Append(recognition!).ToArray() : state.RecognitionLogicalCorrelationIds,
            NativeEffects = command.PreparedNativeEffect is null ? state.NativeEffects : state.NativeEffects.Append(command.PreparedNativeEffect with { PreparedStoryRevision = state.Revision + 1, AuthorizedStoryCorrelationId = command.CorrelationId, AuthorizedMissionRevision = mission.Revision + 1 }).ToArray()
        };
        if (!isFinal)
        {
            var nextMission = state.Missions[index + 1];
            if (nextMission.State == Release1MissionState.Locked)
                next = next with { Missions = state.Missions.Select((m, i) => i == index + 1 ? m with { State = Release1MissionState.Offered, Revision = m.Revision + 1 } : m).ToArray() };
        }
        return ChangeMission(next, index, updated);
    }

    private static Release1StoryTransitionResult Reoffer(Release1StoryState state, int index, Release1MissionRecord mission, Release1StoryCommand command)
    {
        if (mission.State != Release1MissionState.Deferred) throw WrongState();
        return ChangeMission(state, index, mission with { State = Release1MissionState.Offered, Attempt = mission.Attempt + 1, TermsVersion = null, AcceptedGameTimeHours = null, DeadlineGameTimeHours = null, LastOutcome = Release1MissionOutcome.None, RecoveryMode = Release1RecoveryMode.None }, command.CorrelationId);
    }

    private static Release1StoryTransitionResult ChangeMission(Release1StoryState state, int index, Release1MissionRecord mission, string? correlationId = null)
    {
        var nextRevision = state.Revision + 1;
        if (correlationId is not null && !mission.AcceptedLogicalCorrelations.Contains(correlationId, StringComparer.Ordinal))
            mission = mission with { AcceptedLogicalCorrelations = mission.AcceptedLogicalCorrelations.Append(correlationId).ToArray() };
        var nextMission = mission with { Revision = mission.Revision + 1 };
        var missions = state.Missions.Select((current, i) => i == index ? nextMission : current).ToArray();
        return Release1StoryTransitionResult.AcceptedState(state with { Missions = missions, Revision = nextRevision });
    }

    private static Release1MissionRecord Require(Release1MissionRecord mission, Release1MissionState expected) =>
        mission.State == expected ? mission : throw WrongState();

    private static TransitionException WrongState() => new(Release1StoryTransitionRejectReason.WrongState, "Mission state does not allow this transition.");

    private static void ValidateTimes(double? accepted, double? deadline)
    {
        if (accepted is not null && (double.IsNaN(accepted.Value) || double.IsInfinity(accepted.Value) || accepted < 0) ||
            deadline is not null && (double.IsNaN(deadline.Value) || double.IsInfinity(deadline.Value) || deadline < 0) ||
            accepted is not null && deadline is not null && deadline < accepted)
            throw new TransitionException(Release1StoryTransitionRejectReason.InvalidTime, "Mission times must be finite, non-negative, and ordered.");
    }

    private sealed class TransitionException : Exception
    {
        public TransitionException(Release1StoryTransitionRejectReason reason, string message) : base(message) => Reason = reason;
        public Release1StoryTransitionRejectReason Reason { get; }
    }
}
