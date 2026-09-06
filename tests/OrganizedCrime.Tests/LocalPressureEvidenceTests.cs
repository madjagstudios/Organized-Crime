using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureEvidenceTests
{
    [Fact]
    public void Every_foundation_reason_code_has_a_profile_delta()
    {
        foreach (var reasonCode in Enum.GetValues<LocalPressureReasonCode>())
            Assert.True(LocalPressureProfile.Moderate.GetHeatDelta(reasonCode) >= 0);
    }

    [Fact]
    public void Pursuit_shorter_than_credit_interval_adds_no_heat()
    {
        var pursuit = LocalPressurePursuitState.Begin("pursuit-1", gameTimeHours: 10);

        var result = LocalPressurePursuitAccumulator.Evaluate(
            pursuit,
            currentGameTimeHours: 10.9,
            LocalPressureProfile.Moderate);

        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(pursuit, result.State);
    }

    [Fact]
    public void Sustained_pursuit_credits_only_complete_intervals()
    {
        var pursuit = LocalPressurePursuitState.Begin("pursuit-1", gameTimeHours: 10);

        var result = LocalPressurePursuitAccumulator.Evaluate(
            pursuit,
            currentGameTimeHours: 12.9,
            LocalPressureProfile.Moderate);

        Assert.Equal(2, result.HeatDelta);
        Assert.Equal(12, result.State.LastCreditedGameTimeHours);
        Assert.Equal(2, result.State.CreditedHeat);
    }

    [Fact]
    public void Repeating_the_same_pursuit_time_is_idempotent()
    {
        var pursuit = LocalPressurePursuitState.Begin("pursuit-1", gameTimeHours: 10);
        var first = LocalPressurePursuitAccumulator.Evaluate(
            pursuit,
            currentGameTimeHours: 12,
            LocalPressureProfile.Moderate);

        var second = LocalPressurePursuitAccumulator.Evaluate(
            first.State,
            currentGameTimeHours: 12,
            LocalPressureProfile.Moderate);

        Assert.Equal(0, second.HeatDelta);
        Assert.Equal(first.State, second.State);
    }

    [Fact]
    public void Pursuit_contribution_is_capped_per_pursuit()
    {
        var pursuit = LocalPressurePursuitState.Begin("pursuit-1", gameTimeHours: 10);

        var result = LocalPressurePursuitAccumulator.Evaluate(
            pursuit,
            currentGameTimeHours: 100,
            LocalPressureProfile.Moderate);

        Assert.Equal(LocalPressureProfile.Moderate.PursuitHeatCap, result.HeatDelta);
        Assert.Equal(LocalPressureProfile.Moderate.PursuitHeatCap, result.State.CreditedHeat);
    }

    [Fact]
    public void Evasion_is_a_distinct_evidence_reason()
    {
        var result = LocalPressureTransitions.ApplyEvidence(
            LocalPressureState.Quiet(),
            new LocalPressureEvidenceEvent(LocalPressureReasonCode.EvadedPursuit, 20),
            LocalPressureProfile.Moderate);

        Assert.Equal(LocalPressureReasonCode.EvadedPursuit, result.ReasonCode);
        Assert.Equal(LocalPressureProfile.Moderate.GetHeatDelta(LocalPressureReasonCode.EvadedPursuit), result.HeatDelta);
    }

    [Fact]
    public void Ended_pursuit_does_not_credit_late_evaluation()
    {
        var pursuit = LocalPressurePursuitState.Begin("pursuit-1", gameTimeHours: 10).End();

        var result = LocalPressurePursuitAccumulator.Evaluate(
            pursuit,
            currentGameTimeHours: 20,
            LocalPressureProfile.Moderate);

        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(pursuit, result.State);
    }

    [Fact]
    public void Pursuit_aggregate_delta_is_applied_once_and_respects_cap()
    {
        var profile = new LocalPressureProfile(
            quietUpperBound: 24,
            noticedUpperBound: 49,
            watchedUpperBound: 74,
            knownOffenderFloor: 25,
            quietGraceHours: 0,
            heatDecayPerHour: 0,
            maximumDecayCatchUpHours: 24,
            pursuitContributionIntervalHours: 1,
            pursuitHeatCap: 5,
            heatDeltas: new[]
            {
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.WitnessedCrime, 8),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.ContrabandDiscovered, 12),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.Arrest, 20),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.ResistanceOrViolence, 12),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.PursuitEscalation, 2),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.EvadedPursuit, 5),
                new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.VerifiedCurfewOrExposure, 6)
            });
        var fixture = new Fixtures.LocalPressureScenarioFixture(profile);

        var result = fixture.Run(
            new Fixtures.LocalPressureScenarioStep(0, PursuitId: "pursuit-1", BeginPursuit: true, ActivePursuit: true),
            new Fixtures.LocalPressureScenarioStep(2, PursuitId: "pursuit-1", ActivePursuit: true),
            new Fixtures.LocalPressureScenarioStep(10, PursuitId: "pursuit-1", ActivePursuit: true));

        Assert.Equal(5, result.State.LocalHeat);
    }

    [Fact]
    public void Late_evidence_uses_receipt_time_for_quiet_grace()
    {
        var state = new LocalPressureState(
            LocalHeat: 40,
            KnownOffender: false,
            LastEvidenceGameTime: 0,
            QuietGraceUntil: 2,
            LastDecayEvaluation: 10,
            PlayerId: null,
            Region: null,
            PropertyCode: null,
            Revision: 0);

        var transition = LocalPressureTransitions.ApplyEvidence(
            state,
            new LocalPressureEvidenceEvent(
                LocalPressureReasonCode.WitnessedCrime,
                GameTimeHours: 5,
                ReceivedGameTimeHours: 10),
            LocalPressureProfile.Moderate);

        Assert.Equal(12, transition.State.QuietGraceUntil);

        var decay = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(transition.State, CurrentGameTimeHours: 12.5, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(10, decay.State.LastDecayEvaluation);
        Assert.Equal(LocalPressureDecayPauseReason.NoWholePointAccrued, decay.PauseReason);
    }

    [Fact]
    public void Evidence_rejects_a_different_player_identity()
    {
        var state = LocalPressureState.Quiet("player-a");
        var evidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.WitnessedCrime,
            GameTimeHours: 10,
            PlayerId: "player-b");

        Assert.Throws<ArgumentException>(() => LocalPressureTransitions.ApplyEvidence(
            state,
            evidence,
            LocalPressureProfile.Moderate));
    }

    [Fact]
    public void State_rejects_heat_outside_the_defined_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalPressureState(
            LocalHeat: 150,
            KnownOffender: false,
            LastEvidenceGameTime: null,
            QuietGraceUntil: null,
            LastDecayEvaluation: null,
            PlayerId: null,
            Region: null,
            PropertyCode: null,
            Revision: 0));
    }
}
