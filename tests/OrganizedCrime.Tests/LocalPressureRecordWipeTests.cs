using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureRecordWipeTests
{
    private static LocalPressureState DirtyState() => new(
        LocalHeat: 80,
        KnownOffender: true,
        LastEvidenceGameTime: 120.5d,
        QuietGraceUntil: 122.5d,
        LastDecayEvaluation: 121d,
        PlayerId: "player-1",
        Region: "Northtown",
        PropertyCode: "motel",
        Revision: 7);

    [Fact]
    public void Wipe_zeroes_heat_and_clears_known_offender()
    {
        var result = LocalPressureTransitions.ApplyRecordWipe(DirtyState(), LocalPressureProfile.Moderate);

        Assert.Equal(0, result.State.LocalHeat);
        Assert.False(result.State.KnownOffender);
        Assert.Equal(-80, result.HeatDelta);
        Assert.Equal(LocalPressureTier.Critical, result.PreviousTier);
        Assert.Equal(LocalPressureTier.Quiet, result.CurrentTier);
    }

    [Fact]
    public void Wipe_preserves_time_and_identity_fields_and_bumps_revision()
    {
        var dirty = DirtyState();

        var result = LocalPressureTransitions.ApplyRecordWipe(dirty, LocalPressureProfile.Moderate);

        Assert.Equal(dirty.LastEvidenceGameTime, result.State.LastEvidenceGameTime);
        Assert.Equal(dirty.QuietGraceUntil, result.State.QuietGraceUntil);
        Assert.Equal(dirty.LastDecayEvaluation, result.State.LastDecayEvaluation);
        Assert.Equal(dirty.PlayerId, result.State.PlayerId);
        Assert.Equal(dirty.Region, result.State.Region);
        Assert.Equal(dirty.PropertyCode, result.State.PropertyCode);
        Assert.Equal(8, result.State.Revision);
    }

    [Fact]
    public void Wipe_of_an_already_quiet_state_is_a_zero_delta_no_op()
    {
        var quiet = LocalPressureState.Quiet("player-1");

        var result = LocalPressureTransitions.ApplyRecordWipe(quiet, LocalPressureProfile.Moderate);

        Assert.Equal(0, result.State.LocalHeat);
        Assert.False(result.State.KnownOffender);
        Assert.Equal(0, result.HeatDelta);
        Assert.Equal(LocalPressureTier.Quiet, result.PreviousTier);
        Assert.Equal(LocalPressureTier.Quiet, result.CurrentTier);
        Assert.Equal(1, result.State.Revision);
    }

    [Fact]
    public void Clearing_known_offender_is_what_lets_heat_decay_out_of_noticed()
    {
        var wiped = LocalPressureTransitions.ApplyRecordWipe(DirtyState(), LocalPressureProfile.Moderate).State;

        Assert.Equal(25, LocalPressureProfile.Moderate.KnownOffenderFloor);
        Assert.Equal(LocalPressureTier.Noticed, LocalPressureTransitions.GetTier(25, LocalPressureProfile.Moderate));
        Assert.Equal(0, LocalPressureProfile.Moderate.GetHeatFloor(wiped.KnownOffender));
    }

    [Fact]
    public void Null_state_or_null_profile_throws_instead_of_silently_wiping()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LocalPressureTransitions.ApplyRecordWipe(null!, LocalPressureProfile.Moderate));
        Assert.Throws<ArgumentNullException>(() =>
            LocalPressureTransitions.ApplyRecordWipe(DirtyState(), null!));
    }
}
