using OrganizedCrime.Model;
using OrganizedCrime.Tests.Fixtures;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureScenarioTests
{
    [Fact]
    public void Quiet_profitable_operation_without_evidence_leaves_heat_unchanged()
    {
        var result = new LocalPressureScenarioFixture(LocalPressureProfile.Moderate).Run(
            new LocalPressureScenarioStep(0),
            new LocalPressureScenarioStep(4),
            new LocalPressureScenarioStep(8));

        Assert.Equal(0, result.State.LocalHeat);
        Assert.All(result.Ledger, entry => Assert.Null(entry.ReasonCode));
    }

    [Fact]
    public void Witnessed_evidence_raises_heat_then_quiet_time_allows_decay_after_grace()
    {
        var result = new LocalPressureScenarioFixture(LocalPressureProfile.Moderate).Run(
            new LocalPressureScenarioStep(0, LocalPressureReasonCode.WitnessedCrime),
            new LocalPressureScenarioStep(1),
            new LocalPressureScenarioStep(3));

        Assert.Equal(7, result.State.LocalHeat);
        Assert.Equal(8, result.Ledger[0].HeatAfter);
        Assert.Equal(7, result.Ledger[^1].HeatAfter);
    }

    [Fact]
    public void Short_pursuit_adds_no_persistent_heat_but_sustained_pursuit_adds_slowly()
    {
        var result = new LocalPressureScenarioFixture(LocalPressureProfile.Moderate).Run(
            new LocalPressureScenarioStep(0, PursuitId: "short", BeginPursuit: true, ActivePursuit: true),
            new LocalPressureScenarioStep(0.5, PursuitId: "short", ActivePursuit: true),
            new LocalPressureScenarioStep(1),
            new LocalPressureScenarioStep(2, PursuitId: "long", BeginPursuit: true, ActivePursuit: true),
            new LocalPressureScenarioStep(3, PursuitId: "long", ActivePursuit: true),
            new LocalPressureScenarioStep(5, PursuitId: "long", ActivePursuit: true));

        Assert.Equal(3, result.State.LocalHeat);
        Assert.DoesNotContain(result.Ledger.Take(3), entry => entry.ReasonCode == LocalPressureReasonCode.PursuitEscalation);
        Assert.Equal(2, result.Ledger.Count(entry => entry.ReasonCode == LocalPressureReasonCode.PursuitEscalation));
    }

    [Fact]
    public void First_arrest_makes_known_offender_permanent_at_profile_floor()
    {
        var result = new LocalPressureScenarioFixture(LocalPressureProfile.Moderate).Run(
            new LocalPressureScenarioStep(0, LocalPressureReasonCode.Arrest),
            new LocalPressureScenarioStep(100));

        Assert.True(result.State.KnownOffender);
        Assert.Equal(LocalPressureProfile.Moderate.KnownOffenderFloor, result.State.LocalHeat);
    }

    [Fact]
    public void Replaying_the_same_scenario_under_profiles_changes_tuning_not_semantics()
    {
        static LocalPressureScenarioResult Run(LocalPressureProfile profile) =>
            new LocalPressureScenarioFixture(profile).Run(
                new LocalPressureScenarioStep(0, LocalPressureReasonCode.WitnessedCrime),
                new LocalPressureScenarioStep(4));

        var forgiving = Run(LocalPressureProfile.Forgiving);
        var moderate = Run(LocalPressureProfile.Moderate);
        var punishing = Run(LocalPressureProfile.Punishing);

        Assert.True(forgiving.State.LocalHeat < moderate.State.LocalHeat);
        Assert.True(moderate.State.LocalHeat < punishing.State.LocalHeat);
        Assert.Equal(
            forgiving.Ledger.Select(entry => entry.ReasonCode),
            moderate.Ledger.Select(entry => entry.ReasonCode));
        Assert.Equal(
            moderate.Ledger.Select(entry => entry.ReasonCode),
            punishing.Ledger.Select(entry => entry.ReasonCode));
    }

    [Fact]
    public void Rewinding_scenario_time_does_not_grant_additional_decay()
    {
        var result = new LocalPressureScenarioFixture(LocalPressureProfile.Moderate).Run(
            new LocalPressureScenarioStep(0, LocalPressureReasonCode.WitnessedCrime),
            new LocalPressureScenarioStep(4),
            new LocalPressureScenarioStep(3));

        Assert.Equal(6, result.State.LocalHeat);
        Assert.Equal(6, result.Ledger[^1].HeatAfter);
    }
}
