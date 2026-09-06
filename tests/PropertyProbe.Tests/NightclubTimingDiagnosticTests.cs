using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubTimingDiagnosticTests
{
    [Fact]
    public void Trigger_contract_uses_accessible_unused_F5_and_admits_one_host_run()
    {
        Assert.Equal("F5", NightclubProbeContract.TriggerLabel);
        Assert.True(NightclubProbeContract.TryAdmitTrigger(
            f5Pressed: true,
            hostCount: 1,
            serverStarted: true,
            clientStarted: true,
            alreadyRan: false,
            out var admission));
        Assert.Equal(NightclubProbeAdmissionDecision.Start, admission.Decision);

        Assert.False(NightclubProbeContract.TryAdmitTrigger(
            f5Pressed: true,
            hostCount: 1,
            serverStarted: true,
            clientStarted: true,
            alreadyRan: true,
            out admission));
        Assert.Equal(NightclubProbeAdmissionDecision.Stop, admission.Decision);
    }

    [Fact]
    public void Shell_match_requires_the_exact_approved_hierarchy_path()
    {
        Assert.True(NightclubProbeContract.IsExpectedShellPath(
            "Map/Hyland Point/Region_Northtown/Nightclub/desert town hall"));
        Assert.False(NightclubProbeContract.IsExpectedShellPath(
            "Map/Hyland Point/Region_Northtown/Nightclub"));
        Assert.False(NightclubProbeContract.IsExpectedShellPath(
            "Map/Hyland Point/Region_Docks/Nightclub/desert town hall"));
    }

    [Fact]
    public void Evaluator_passes_only_after_bounded_read_only_host_observation()
    {
        var evidence = NightclubProbeEvidence.Test(
            menuOutcome: NightclubMenuOutcome.NotObserved,
            duplicateInteractionTested: true,
            teardownObserved: true,
            observations: new[]
            {
                NightclubProbeObservation.Test(NightclubProbeEventKind.OwnerTriggered, 0.0),
                NightclubProbeObservation.Test(NightclubProbeEventKind.DoorInteractionObserved, 1.2),
                NightclubProbeObservation.Test(NightclubProbeEventKind.DuplicateInteractionObserved, 2.0),
                NightclubProbeObservation.Test(NightclubProbeEventKind.WindowElapsed, 10.0),
                NightclubProbeObservation.Test(NightclubProbeEventKind.TeardownObserved, 10.1)
            });

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Pass, result.Decision);
        Assert.Contains(result.Reasons, reason => reason.StartsWith("PASS:", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluator_is_inconclusive_when_menu_window_or_teardown_is_missing()
    {
        var evidence = NightclubProbeEvidence.Test(
            menuOutcome: NightclubMenuOutcome.Unknown,
            duplicateInteractionTested: false,
            teardownObserved: false,
            observations: new[]
            {
                NightclubProbeObservation.Test(NightclubProbeEventKind.OwnerTriggered, 0.0),
                NightclubProbeObservation.Test(NightclubProbeEventKind.DoorInteractionObserved, 1.2)
            });

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Inconclusive, result.Decision);
        Assert.Contains(result.Reasons, reason => reason.Contains("menu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluator_stops_on_authority_drift_or_vanilla_mutation()
    {
        var evidence = NightclubProbeEvidence.Test(
            authority: NightclubProbeAuthority.Client,
            mutationAttempted: true);

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Stop, result.Decision);
        Assert.Contains(result.Reasons, reason => reason.Contains("authority", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons, reason => reason.Contains("mutation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluator_stops_when_exact_shell_validation_is_not_safe()
    {
        var evidence = NightclubProbeEvidence.Test(exactShellValidated: false);

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Stop, result.Decision);
        Assert.Contains(result.Reasons, reason => reason.Contains("shell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluator_allows_vanilla_menu_transition_when_identity_invariants_hold()
    {
        var evidence = NightclubProbeEvidence.Test(
            menuOutcome: NightclubMenuOutcome.OpenedAndClosed,
            vanillaInvariantsUnchanged: true);
        evidence = evidence with
        {
            VanillaAfter = evidence.VanillaAfter with { MenuState = "opened-then-closed" }
        };

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Pass, result.Decision);
    }

    [Fact]
    public void Evaluator_is_inconclusive_when_the_menu_surface_is_unavailable()
    {
        var evidence = NightclubProbeEvidence.Test() with { MenuSurfaceObserved = false };

        var result = NightclubProbeEvaluator.Evaluate(evidence);

        Assert.Equal(NightclubProbeDecision.Inconclusive, result.Decision);
        Assert.Contains(result.Reasons, reason => reason.Contains("surface", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Text_report_keeps_runtime_facts_and_unknown_callback_order_separate()
    {
        var evidence = NightclubProbeEvidence.Test();

        var text = NightclubTimingFormatter.FormatText(evidence, NightclubProbeEvaluator.Evaluate(evidence));

        Assert.Contains("EXPECTED_SHELL_PATH: Map/Hyland Point/Region_Northtown/Nightclub/desert town hall", text);
        Assert.Contains("DOOR_PATH: Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Front Door", text);
        Assert.Contains("MENU_OUTCOME: NotObserved", text);
        Assert.Contains("CALLBACK_ORDER: UNKNOWN", text);
        Assert.Contains("MUTATION_ATTEMPTED: False", text);
        Assert.Contains("DECISION: PASS", text);
    }
}
