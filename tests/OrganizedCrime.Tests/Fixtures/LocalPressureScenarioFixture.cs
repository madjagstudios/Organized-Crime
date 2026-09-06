using OrganizedCrime.Model;

namespace OrganizedCrime.Tests.Fixtures;

public sealed record LocalPressureScenarioStep(
    double GameTimeHours,
    LocalPressureReasonCode? ReasonCode = null,
    string? PursuitId = null,
    bool BeginPursuit = false,
    bool ActivePursuit = false,
    bool EndPursuit = false);

public sealed record LocalPressureScenarioLedgerEntry(
    double GameTimeHours,
    LocalPressureReasonCode? ReasonCode,
    int HeatBefore,
    int HeatAfter,
    LocalPressureTier TierBefore,
    LocalPressureTier TierAfter);

public sealed record LocalPressureScenarioResult(
    LocalPressureState State,
    IReadOnlyList<LocalPressureScenarioLedgerEntry> Ledger);

public sealed class LocalPressureScenarioFixture
{
    private readonly LocalPressureProfile _profile;

    public LocalPressureScenarioFixture(LocalPressureProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public LocalPressureScenarioResult Run(params LocalPressureScenarioStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var state = LocalPressureState.Quiet();
        var pursuits = new Dictionary<string, LocalPressurePursuitState>(StringComparer.Ordinal);
        var ledger = new List<LocalPressureScenarioLedgerEntry>();

        foreach (var step in steps)
        {
            var heatBefore = state.LocalHeat;
            var tierBefore = LocalPressureTransitions.GetTier(state.LocalHeat, _profile);
            LocalPressureReasonCode? reasonCode = null;

            if (step.ReasonCode is { } explicitReason)
            {
                var evidence = new LocalPressureEvidenceEvent(explicitReason, step.GameTimeHours);
                var transition = LocalPressureTransitions.ApplyEvidence(state, evidence, _profile);
                state = transition.State;
                reasonCode = explicitReason;
            }

            if (step.PursuitId is not null)
            {
                if (step.BeginPursuit && !pursuits.ContainsKey(step.PursuitId))
                    pursuits[step.PursuitId] = LocalPressurePursuitState.Begin(step.PursuitId, step.GameTimeHours);

                if (pursuits.TryGetValue(step.PursuitId, out var pursuit))
                {
                    var evaluation = LocalPressurePursuitAccumulator.Evaluate(
                        pursuit,
                        step.GameTimeHours,
                        _profile);
                    var pursuitState = evaluation.State;
                    if (evaluation.HeatDelta > 0)
                    {
                        var transition = LocalPressureTransitions.ApplyEvidence(
                            state,
                            new LocalPressureEvidenceEvent(
                                LocalPressureReasonCode.PursuitEscalation,
                                step.GameTimeHours,
                                CorrelationId: step.PursuitId),
                            _profile,
                            evaluation.HeatDelta);
                        state = transition.State;
                        reasonCode = LocalPressureReasonCode.PursuitEscalation;
                    }

                    if (step.EndPursuit || pursuitState.CreditedHeat >= _profile.PursuitHeatCap)
                        pursuits.Remove(step.PursuitId);
                    else
                        pursuits[step.PursuitId] = pursuitState;
                }
            }

            var decay = LocalPressureDecay.Evaluate(
                new LocalPressureDecayInput(state, step.GameTimeHours, step.ActivePursuit),
                _profile);
            state = decay.State;

            ledger.Add(new LocalPressureScenarioLedgerEntry(
                step.GameTimeHours,
                reasonCode,
                heatBefore,
                state.LocalHeat,
                tierBefore,
                LocalPressureTransitions.GetTier(state.LocalHeat, _profile)));
        }

        return new LocalPressureScenarioResult(state, ledger);
    }
}
