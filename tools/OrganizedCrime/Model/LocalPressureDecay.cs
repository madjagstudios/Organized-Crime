namespace OrganizedCrime.Model;

public sealed record LocalPressureDecayInput(
    LocalPressureState State,
    double CurrentGameTimeHours,
    bool ActivePursuit);

public enum LocalPressureDecayPauseReason
{
    None,
    QuietGrace,
    ActivePursuit,
    TimeRewound,
    NoElapsedTime,
    NoHeat,
    NoWholePointAccrued
}

public sealed record LocalPressureDecayResult(
    LocalPressureState State,
    int HeatDelta,
    double EligibleDecayHours,
    bool CatchUpWasCapped,
    LocalPressureDecayPauseReason PauseReason);

public static class LocalPressureDecay
{
    public static LocalPressureDecayResult Evaluate(
        LocalPressureDecayInput input,
        LocalPressureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.State);
        ArgumentNullException.ThrowIfNull(profile);
        ValidateGameTime(input.CurrentGameTimeHours);

        var state = input.State;
        if (state.LastDecayEvaluation is not null &&
            input.CurrentGameTimeHours < state.LastDecayEvaluation.Value)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.TimeRewound);
        }

        var minimumHeat = profile.GetHeatFloor(state.KnownOffender);
        var removableHeat = Math.Max(0, state.LocalHeat - minimumHeat);
        if (removableHeat == 0)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.NoHeat);
        }

        var lastEvaluation = state.LastDecayEvaluation ?? state.LastEvidenceGameTime;
        if (input.ActivePursuit)
        {
            return new LocalPressureDecayResult(
                AdvanceEvaluationMarker(state, input.CurrentGameTimeHours),
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.ActivePursuit);
        }

        if (lastEvaluation is null)
        {
            return new LocalPressureDecayResult(
                AdvanceEvaluationMarker(state, input.CurrentGameTimeHours),
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.NoElapsedTime);
        }

        if (input.CurrentGameTimeHours <= lastEvaluation.Value)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.NoElapsedTime);
        }

        if (state.QuietGraceUntil is not null &&
            input.CurrentGameTimeHours < state.QuietGraceUntil.Value)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: 0,
                CatchUpWasCapped: false,
                LocalPressureDecayPauseReason.QuietGrace);
        }

        var decayStart = Math.Max(lastEvaluation.Value, state.QuietGraceUntil ?? lastEvaluation.Value);
        var elapsedHours = Math.Max(0, input.CurrentGameTimeHours - decayStart);
        var eligibleDecayHours = Math.Min(elapsedHours, profile.MaximumDecayCatchUpHours);
        var catchUpWasCapped = elapsedHours > profile.MaximumDecayCatchUpHours;
        var pointsAvailable = (int)Math.Floor(eligibleDecayHours * profile.HeatDecayPerHour);
        if (profile.HeatDecayPerHour == 0)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: eligibleDecayHours,
                CatchUpWasCapped: catchUpWasCapped,
                LocalPressureDecayPauseReason.NoHeat);
        }

        var pointsCredited = Math.Min(pointsAvailable, removableHeat);
        if (pointsCredited == 0)
        {
            return new LocalPressureDecayResult(
                state,
                HeatDelta: 0,
                EligibleDecayHours: eligibleDecayHours,
                CatchUpWasCapped: catchUpWasCapped,
                LocalPressureDecayPauseReason.NoWholePointAccrued);
        }

        var heatDelta = -pointsCredited;
        var evaluationMarker = GetConsumedEvaluationMarker(
            state,
            input.CurrentGameTimeHours,
            decayStart,
            pointsCredited,
            removableHeat,
            catchUpWasCapped,
            profile);
        var resultState = new LocalPressureState(
            LocalHeat: state.LocalHeat + heatDelta,
            KnownOffender: state.KnownOffender,
            LastEvidenceGameTime: state.LastEvidenceGameTime,
            QuietGraceUntil: state.QuietGraceUntil,
            LastDecayEvaluation: evaluationMarker,
            PlayerId: state.PlayerId,
            Region: state.Region,
            PropertyCode: state.PropertyCode,
            Revision: checked(state.Revision + 1));
        var pauseReason = heatDelta != 0
            ? LocalPressureDecayPauseReason.None
            : LocalPressureDecayPauseReason.NoWholePointAccrued;

        return new LocalPressureDecayResult(
            resultState,
            heatDelta,
            eligibleDecayHours,
            catchUpWasCapped,
            pauseReason);
    }

    private static double GetConsumedEvaluationMarker(
        LocalPressureState state,
        double currentGameTimeHours,
        double decayStart,
        int pointsCredited,
        int removableHeat,
        bool catchUpWasCapped,
        LocalPressureProfile profile)
    {
        if (catchUpWasCapped)
            return currentGameTimeHours;
        if (removableHeat == 0 || profile.HeatDecayPerHour == 0)
            return currentGameTimeHours;
        if (pointsCredited == 0)
            return decayStart;
        if (pointsCredited >= removableHeat)
            return currentGameTimeHours;

        return Math.Min(
            currentGameTimeHours,
            decayStart + pointsCredited / profile.HeatDecayPerHour);
    }

    private static LocalPressureState AdvanceEvaluationMarker(LocalPressureState state, double currentGameTimeHours) =>
        state.LastDecayEvaluation == currentGameTimeHours
            ? state
            : new LocalPressureState(
                LocalHeat: state.LocalHeat,
                KnownOffender: state.KnownOffender,
                LastEvidenceGameTime: state.LastEvidenceGameTime,
                QuietGraceUntil: state.QuietGraceUntil,
                LastDecayEvaluation: currentGameTimeHours,
                PlayerId: state.PlayerId,
                Region: state.Region,
                PropertyCode: state.PropertyCode,
                Revision: checked(state.Revision + 1));

    private static void ValidateGameTime(double gameTimeHours)
    {
        if (double.IsNaN(gameTimeHours) || double.IsInfinity(gameTimeHours) || gameTimeHours < 0)
            throw new ArgumentOutOfRangeException(nameof(gameTimeHours));
    }
}
