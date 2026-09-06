namespace OrganizedCrime.Model;

public sealed record LocalPressurePursuitState(
    string PursuitId,
    double StartedGameTimeHours,
    double LastCreditedGameTimeHours,
    int CreditedHeat,
    bool IsActive)
{
    public static LocalPressurePursuitState Begin(string pursuitId, double gameTimeHours)
    {
        if (string.IsNullOrWhiteSpace(pursuitId))
            throw new ArgumentException("Pursuit identity is required.", nameof(pursuitId));
        if (double.IsNaN(gameTimeHours) || double.IsInfinity(gameTimeHours) || gameTimeHours < 0)
            throw new ArgumentOutOfRangeException(nameof(gameTimeHours));

        return new LocalPressurePursuitState(pursuitId, gameTimeHours, gameTimeHours, 0, true);
    }

    public LocalPressurePursuitState End() => this with { IsActive = false };
}

public sealed record LocalPressurePursuitEvaluation(
    LocalPressurePursuitState State,
    int HeatDelta);

public static class LocalPressurePursuitAccumulator
{
    public static LocalPressurePursuitEvaluation Evaluate(
        LocalPressurePursuitState state,
        double currentGameTimeHours,
        LocalPressureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(profile);
        if (double.IsNaN(currentGameTimeHours) || double.IsInfinity(currentGameTimeHours) || currentGameTimeHours < 0)
            throw new ArgumentOutOfRangeException(nameof(currentGameTimeHours));

        if (!state.IsActive ||
            currentGameTimeHours <= state.LastCreditedGameTimeHours ||
            state.CreditedHeat >= profile.PursuitHeatCap)
        {
            return new LocalPressurePursuitEvaluation(state, 0);
        }

        var elapsedHours = currentGameTimeHours - state.LastCreditedGameTimeHours;
        var completedIntervals = (int)Math.Floor(elapsedHours / profile.PursuitContributionIntervalHours);
        var pursuitEscalationHeatDelta = profile.GetHeatDelta(LocalPressureReasonCode.PursuitEscalation);
        if (completedIntervals <= 0 || pursuitEscalationHeatDelta <= 0)
            return new LocalPressurePursuitEvaluation(state, 0);

        var availableHeat = profile.PursuitHeatCap - state.CreditedHeat;
        var maximumIntervals = (availableHeat + pursuitEscalationHeatDelta - 1) /
                               pursuitEscalationHeatDelta;
        var intervalsToCredit = Math.Min(completedIntervals, maximumIntervals);
        var heatDelta = Math.Min(
            availableHeat,
            checked(intervalsToCredit * pursuitEscalationHeatDelta));
        var lastCredited = state.LastCreditedGameTimeHours +
                           intervalsToCredit * profile.PursuitContributionIntervalHours;

        return new LocalPressurePursuitEvaluation(
            state with
            {
                LastCreditedGameTimeHours = lastCredited,
                CreditedHeat = state.CreditedHeat + heatDelta
            },
            heatDelta);
    }
}
