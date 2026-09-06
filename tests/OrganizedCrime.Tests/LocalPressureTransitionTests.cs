using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureTransitionTests
{
    [Theory]
    [InlineData(0, LocalPressureTier.Quiet)]
    [InlineData(24, LocalPressureTier.Quiet)]
    [InlineData(25, LocalPressureTier.Noticed)]
    [InlineData(49, LocalPressureTier.Noticed)]
    [InlineData(50, LocalPressureTier.Watched)]
    [InlineData(74, LocalPressureTier.Watched)]
    [InlineData(75, LocalPressureTier.Critical)]
    [InlineData(100, LocalPressureTier.Critical)]
    public void GetTier_uses_moderate_boundaries(int heat, LocalPressureTier expected)
    {
        Assert.Equal(expected, LocalPressureTransitions.GetTier(heat, LocalPressureProfile.Moderate));
    }

    [Fact]
    public void ApplyEvidence_adds_profile_delta_and_clamps_at_one_hundred()
    {
        var state = new LocalPressureState(96, false, null, null, null, null, null, null, 0);
        var evidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.WitnessedCrime,
            GameTimeHours: 10);

        var result = LocalPressureTransitions.ApplyEvidence(state, evidence, LocalPressureProfile.Moderate);

        Assert.Equal(100, result.State.LocalHeat);
        Assert.Equal(4, result.HeatDelta);
        Assert.Equal(LocalPressureTier.Critical, result.PreviousTier);
        Assert.Equal(LocalPressureTier.Critical, result.CurrentTier);
    }

    [Fact]
    public void ApplyEvidence_starts_quiet_grace_and_records_context()
    {
        var evidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.WitnessedCrime,
            GameTimeHours: 10,
            Region: "Downtown",
            PropertyCode: "safehouse");

        var result = LocalPressureTransitions.ApplyEvidence(
            LocalPressureState.Quiet(),
            evidence,
            LocalPressureProfile.Moderate);

        Assert.Equal(10, result.State.LastEvidenceGameTime);
        Assert.Equal(10 + LocalPressureProfile.Moderate.QuietGraceHours, result.State.QuietGraceUntil);
        Assert.Equal("Downtown", result.State.Region);
        Assert.Equal("safehouse", result.State.PropertyCode);
    }

    [Fact]
    public void First_arrest_sets_known_offender_and_raises_heat_to_profile_floor()
    {
        var evidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.Arrest,
            GameTimeHours: 12);

        var result = LocalPressureTransitions.ApplyEvidence(
            LocalPressureState.Quiet(),
            evidence,
            LocalPressureProfile.Moderate);

        Assert.True(result.State.KnownOffender);
        Assert.Equal(LocalPressureProfile.Moderate.KnownOffenderFloor, result.State.LocalHeat);
        Assert.Equal(LocalPressureProfile.Moderate.NoticedLowerBound, result.State.LocalHeat);
    }

    [Fact]
    public void Arrest_does_not_lower_existing_heat_or_clear_known_offender()
    {
        var state = new LocalPressureState(80, true, null, null, null, null, null, null, 0);
        var evidence = new LocalPressureEvidenceEvent(LocalPressureReasonCode.Arrest, GameTimeHours: 12);

        var result = LocalPressureTransitions.ApplyEvidence(state, evidence, LocalPressureProfile.Moderate);

        Assert.Equal(100, result.State.LocalHeat);
        Assert.True(result.State.KnownOffender);
        Assert.Equal(20, result.HeatDelta);
    }

    [Fact]
    public void Non_arrest_evidence_does_not_create_known_offender()
    {
        var evidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.ContrabandDiscovered,
            GameTimeHours: 8);

        var result = LocalPressureTransitions.ApplyEvidence(
            LocalPressureState.Quiet(),
            evidence,
            LocalPressureProfile.Moderate);

        Assert.False(result.State.KnownOffender);
        Assert.Equal(LocalPressureProfile.Moderate.GetHeatDelta(LocalPressureReasonCode.ContrabandDiscovered), result.HeatDelta);
    }

}
