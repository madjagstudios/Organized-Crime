using OrganizedCrime.QuestLifecycleProof;
using Xunit;

namespace OrganizedCrime.QuestLifecycleProof.Tests;

public sealed class QuestLifecycleRuntimeContractTests
{
    [Fact]
    public void Admission_accepts_active_save_before_any_terminal_outcome()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");
        AdmitLifecycleBeforeTerminal(session);

        var result = session.TryAdmit(Request(QuestLifecyclePhase.Save), HostContext());

        Assert.True(result.Accepted, result.Reason);
    }

    [Fact]
    public void Admission_accepts_terminal_and_terminal_save_only_after_active_reconstruction()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");
        AdmitLifecycleBeforeTerminal(session);
        Assert.True(session.TryAdmit(Request(QuestLifecyclePhase.Save), HostContext()).Accepted);
        Assert.True(session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-1",
            isReconstruction: true,
            reloadClassification: QuestReloadClassification.ActiveReconstruction), HostContext()).Accepted);

        var terminal = session.TryAdmit(Request(
            QuestLifecyclePhase.Terminal,
            loadEpoch: "load-1",
            terminalOutcome: QuestTerminalOutcome.Completed), HostContext());
        var save = session.TryAdmit(Request(QuestLifecyclePhase.Save, loadEpoch: "load-1"), HostContext());

        Assert.True(terminal.Accepted, terminal.Reason);
        Assert.True(save.Accepted, save.Reason);
        Assert.NotNull(terminal.StandingReceipt);
        Assert.NotNull(terminal.RewardReceipt);
        Assert.Null(save.StandingReceipt);
        Assert.Null(save.RewardReceipt);
    }

    [Fact]
    public void Admission_accepts_completed_absence_as_positive_second_reload()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");
        AdmitLifecycleBeforeTerminal(session);
        Assert.True(session.TryAdmit(Request(QuestLifecyclePhase.Save), HostContext()).Accepted);
        Assert.True(session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-1",
            isReconstruction: true,
            reloadClassification: QuestReloadClassification.ActiveReconstruction), HostContext()).Accepted);
        Assert.True(session.TryAdmit(Request(
            QuestLifecyclePhase.Terminal,
            loadEpoch: "load-1",
            terminalOutcome: QuestTerminalOutcome.Completed), HostContext()).Accepted);
        Assert.True(session.TryAdmit(Request(QuestLifecyclePhase.Save, loadEpoch: "load-1"), HostContext()).Accepted);

        var result = session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-2",
            isReconstruction: false,
            reloadClassification: QuestReloadClassification.CompletedQuestAbsentNoReplay), HostContext());

        Assert.True(result.Accepted, result.Reason);
    }

    [Fact]
    public void Admission_rejects_a_stale_load_epoch_before_native_action()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");

        var result = session.TryAdmit(
            Request(QuestLifecyclePhase.Begun, loadEpoch: "stale-epoch"),
            HostContext());

        Assert.False(result.Accepted);
        Assert.Contains("stale", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admission_rejects_a_wrong_mission_key_before_native_action()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");

        var result = session.TryAdmit(
            Request(QuestLifecyclePhase.Created) with { MissionKey = "wrong.mission" },
            HostContext());

        Assert.False(result.Accepted);
        Assert.Contains("mission key", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admission_rejects_client_or_ui_actions_and_non_host_context()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");

        var client = session.TryAdmit(
            Request(QuestLifecyclePhase.Created, actor: QuestProofActor.Client),
            HostContext());
        var nonHost = session.TryAdmit(
            Request(QuestLifecyclePhase.Created),
            new QuestProofHostContext(IsAuthoritativeHost: false, IsSinglePlayer: true));

        Assert.False(client.Accepted);
        Assert.Contains("client/UI", client.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(nonHost.Accepted);
        Assert.Contains("host", nonHost.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_terminal_reuses_one_proof_receipt_pair()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");
        AdmitLifecycleBeforeTerminal(session);

        Assert.True(session.TryAdmit(Request(QuestLifecyclePhase.Save), HostContext()).Accepted);
        Assert.True(session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-1",
            isReconstruction: true,
            reloadClassification: QuestReloadClassification.ActiveReconstruction), HostContext()).Accepted);

        var first = session.TryAdmit(Request(
            QuestLifecyclePhase.Terminal,
            loadEpoch: "load-1",
            terminalOutcome: QuestTerminalOutcome.Completed), HostContext());
        var duplicate = session.TryAdmit(Request(
            QuestLifecyclePhase.Terminal,
            loadEpoch: "load-1",
            terminalOutcome: QuestTerminalOutcome.Completed), HostContext());

        Assert.True(first.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.Contains("duplicate", duplicate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reconstruction_is_accepted_once_per_load_epoch()
    {
        using var session = new QuestLifecycleProofSession("oc44.mission", 1, "load-0");
        AdmitLifecycleBeforeTerminal(session);
        session.TryAdmit(Request(QuestLifecyclePhase.Save), HostContext());

        var first = session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-1",
            isReconstruction: true,
            reloadClassification: QuestReloadClassification.ActiveReconstruction), HostContext());
        var duplicate = session.TryAdmit(Request(
            QuestLifecyclePhase.FullReload,
            loadEpoch: "load-1",
            isReconstruction: true,
            reloadClassification: QuestReloadClassification.ActiveReconstruction), HostContext());

        Assert.True(first.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.Contains("reconstruction", duplicate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disposal_detaches_each_tracked_subscription_once()
    {
        using var ledger = new QuestProofSubscriptionLedger();
        var detachCount = 0;
        ledger.Track(() => detachCount++);

        ledger.Dispose();
        ledger.Dispose();

        Assert.True(ledger.IsDisposed);
        Assert.Equal(1, detachCount);
    }

    [Fact]
    public void Missing_native_state_is_inconclusive_without_promoting_an_identity()
    {
        var result = QuestLifecycleProofEvaluator.Evaluate(
            new[]
            {
                new QuestLifecycleObservation(
                    "oc44.mission", 1, "load-0", null, QuestNativeIdentity.Empty,
                    QuestLifecyclePhase.Created, QuestProofActor.Host, QuestTerminalOutcome.None,
                    null, null, false, false,
                    QuestRuntimeAvailability.Available, QuestRuntimeAvailability.MissingNativeState)
            });

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("native", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_s1api_state_is_inconclusive_without_native_calls()
    {
        var result = QuestLifecycleProofEvaluator.Evaluate(
            new[]
            {
                new QuestLifecycleObservation(
                    "oc44.mission", 1, "load-0", null, QuestNativeIdentity.Empty,
                    QuestLifecyclePhase.Created, QuestProofActor.Host, QuestTerminalOutcome.None,
                    null, null, false, false,
                    QuestRuntimeAvailability.MissingS1Api, QuestRuntimeAvailability.Available)
            });

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("S1API", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    private static QuestProofHostContext HostContext() =>
        new(IsAuthoritativeHost: true, IsSinglePlayer: true);

    private static QuestLifecycleActionRequest Request(
        QuestLifecyclePhase phase,
        string loadEpoch = "load-0",
        QuestProofActor actor = QuestProofActor.Host,
        QuestTerminalOutcome terminalOutcome = QuestTerminalOutcome.None,
        bool isReconstruction = false,
        QuestReloadClassification reloadClassification = QuestReloadClassification.None) =>
        new("oc44.mission", 1, loadEpoch, phase, actor, terminalOutcome, isReconstruction, false, reloadClassification);

    private static void AdmitLifecycleBeforeTerminal(QuestLifecycleProofSession session)
    {
        foreach (var phase in new[]
        {
            QuestLifecyclePhase.Created,
            QuestLifecyclePhase.Begun,
            QuestLifecyclePhase.ObjectiveEntry,
            QuestLifecyclePhase.ObjectiveUpdate
        })
        {
            var result = session.TryAdmit(Request(phase), HostContext());
            Assert.True(result.Accepted, result.Reason);
        }
    }
}
