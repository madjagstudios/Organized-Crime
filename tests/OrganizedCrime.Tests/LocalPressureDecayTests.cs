using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureDecayTests
{
    [Fact]
    public void Decay_waits_until_quiet_grace_expires()
    {
        var state = new LocalPressureState(40, false, 10, 12, 10, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 11, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(40, result.State.LocalHeat);
        Assert.Same(state, result.State);
        Assert.Equal(0, result.State.Revision);
        Assert.Equal(10, result.State.LastDecayEvaluation);
        Assert.Equal(LocalPressureDecayPauseReason.QuietGrace, result.PauseReason);
    }

    [Fact]
    public void Quiet_without_evidence_is_a_permanent_full_state_no_op()
    {
        var state = LocalPressureState.Quiet("player-1");

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Same(state, result.State);
        Assert.Equal(0, result.State.Revision);
        Assert.Null(result.State.LastDecayEvaluation);
        Assert.Equal(LocalPressureDecayPauseReason.NoHeat, result.PauseReason);
    }

    [Fact]
    public void Heat_at_floor_is_a_full_state_no_op()
    {
        var state = new LocalPressureState(0, false, null, null, 4, "player-1", null, null, 3);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Same(state, result.State);
        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(LocalPressureDecayPauseReason.NoHeat, result.PauseReason);
    }

    [Fact]
    public void Known_offender_at_floor_is_a_full_state_no_op()
    {
        var state = new LocalPressureState(25, true, 10, 10, 10, "player-1", null, null, 4);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Same(state, result.State);
        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(LocalPressureDecayPauseReason.NoHeat, result.PauseReason);
    }

    [Fact]
    public void Decay_reduces_heat_after_grace_using_game_time()
    {
        var state = new LocalPressureState(40, false, 10, 12, 10, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 14, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(38, result.State.LocalHeat);
        Assert.Equal(-2, result.HeatDelta);
        Assert.Equal(2, result.EligibleDecayHours);
        Assert.Equal(14, result.State.LastDecayEvaluation);
        Assert.Equal(1, result.State.Revision);
    }

    [Fact]
    public void Active_pursuit_pauses_decay_and_moves_evaluation_marker_forward()
    {
        var state = new LocalPressureState(40, false, 10, 12, 10, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 20, ActivePursuit: true),
            LocalPressureProfile.Moderate);

        Assert.Equal(40, result.State.LocalHeat);
        Assert.Equal(20, result.State.LastDecayEvaluation);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(LocalPressureDecayPauseReason.ActivePursuit, result.PauseReason);
    }

    [Fact]
    public void Time_rewind_does_not_change_state_or_grant_decay()
    {
        var state = new LocalPressureState(40, false, 10, 12, 20, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 19, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Same(state, result.State);
        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(LocalPressureDecayPauseReason.TimeRewound, result.PauseReason);
    }

    [Fact]
    public void Decay_catch_up_is_capped()
    {
        var state = new LocalPressureState(80, false, 0, 0, 0, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(-LocalPressureProfile.Moderate.MaximumDecayCatchUpHours, result.HeatDelta);
        Assert.Equal(80 - LocalPressureProfile.Moderate.MaximumDecayCatchUpHours, result.State.LocalHeat);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(100, result.State.LastDecayEvaluation);
        Assert.True(result.CatchUpWasCapped);
    }

    [Fact]
    public void Capped_catch_up_is_idempotent_at_the_same_timestamp()
    {
        var state = new LocalPressureState(80, false, 0, 0, 0, null, null, null, 0);

        var first = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);
        var second = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(first.State, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(56, first.State.LocalHeat);
        Assert.Equal(1, first.State.Revision);
        Assert.Equal(100, first.State.LastDecayEvaluation);
        Assert.Equal(0, second.HeatDelta);
        Assert.Same(first.State, second.State);
        Assert.Equal(56, second.State.LocalHeat);
    }

    [Fact]
    public void Capped_catch_up_only_decays_time_after_the_capped_evaluation()
    {
        var state = new LocalPressureState(80, false, 0, 0, 0, null, null, null, 0);

        var first = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);
        var second = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(first.State, CurrentGameTimeHours: 101, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(55, second.State.LocalHeat);
        Assert.Equal(-1, second.HeatDelta);
        Assert.Equal(1, second.EligibleDecayHours);
        Assert.False(second.CatchUpWasCapped);
    }

    [Fact]
    public void Known_offender_floor_survives_complete_decay()
    {
        var state = new LocalPressureState(40, true, 10, 10, 10, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 100, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(LocalPressureProfile.Moderate.KnownOffenderFloor, result.State.LocalHeat);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(100, result.State.LastDecayEvaluation);
        Assert.True(result.State.KnownOffender);
    }

    [Fact]
    public void Moderate_decay_retains_sub_point_progress_between_half_hour_evaluations()
    {
        var state = new LocalPressureState(10, false, 0, 0, 0, null, null, null, 0);

        var first = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 0.5, ActivePursuit: false),
            LocalPressureProfile.Moderate);
        var second = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(first.State, CurrentGameTimeHours: 1, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(0, first.HeatDelta);
        Assert.Same(state, first.State);
        Assert.Equal(0, first.State.Revision);
        Assert.Equal(0, first.State.LastDecayEvaluation);
        Assert.Equal(LocalPressureDecayPauseReason.NoWholePointAccrued, first.PauseReason);
        Assert.Equal(9, second.State.LocalHeat);
        Assert.Equal(-1, second.HeatDelta);
        Assert.Equal(1, second.State.Revision);
        Assert.Equal(1, second.State.LastDecayEvaluation);
    }

    [Fact]
    public void Punishing_decay_retains_sub_point_progress_between_hourly_evaluations()
    {
        var state = new LocalPressureState(10, false, 0, 0, 0, null, null, null, 0);

        var first = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 1, ActivePursuit: false),
            LocalPressureProfile.Punishing);
        var second = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(first.State, CurrentGameTimeHours: 2, ActivePursuit: false),
            LocalPressureProfile.Punishing);

        Assert.Equal(0, first.HeatDelta);
        Assert.Equal(LocalPressureDecayPauseReason.NoWholePointAccrued, first.PauseReason);
        Assert.Equal(9, second.State.LocalHeat);
        Assert.Equal(-1, second.HeatDelta);
    }

    [Fact]
    public void Decay_reports_no_whole_point_when_heat_remains_above_floor()
    {
        var state = new LocalPressureState(80, false, 0, 0, 0, null, null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 0.5, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Equal(LocalPressureDecayPauseReason.NoWholePointAccrued, result.PauseReason);
    }

    [Fact]
    public void Heat_without_any_history_seeds_only_the_current_boundary()
    {
        var state = new LocalPressureState(10, false, null, null, null, "player-1", null, null, 0);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 20, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.NotSame(state, result.State);
        Assert.Equal(10, result.State.LocalHeat);
        Assert.Equal(20, result.State.LastDecayEvaluation);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(LocalPressureDecayPauseReason.NoElapsedTime, result.PauseReason);
    }

    [Fact]
    public void No_whole_point_keeps_fractional_progress_outside_the_state_marker()
    {
        var state = new LocalPressureState(10, false, 0, null, 0, "player-1", null, null, 7);

        var result = LocalPressureDecay.Evaluate(
            new LocalPressureDecayInput(state, CurrentGameTimeHours: 0.5, ActivePursuit: false),
            LocalPressureProfile.Moderate);

        Assert.Same(state, result.State);
        Assert.Equal(7, result.State.Revision);
        Assert.Equal(0, result.State.LastDecayEvaluation);
    }
}
