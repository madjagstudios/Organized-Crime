using OrganizedCrime.QuestLifecycleProof;
using Xunit;

namespace OrganizedCrime.QuestLifecycleProof.Tests;

public sealed class QuestLifecycleProofTests
{
    [Fact]
    public void Three_launch_lifecycle_saves_active_quest_before_reconstruction_and_passes()
    {
        var result = EvaluateInCurrentLaunch(ValidThreeLaunchRun());

        Assert.Equal(QuestLifecycleProofDecision.Pass, result.Decision);
    }

    [Fact]
    public void Completed_quest_absence_after_terminal_save_is_positive_no_replay_evidence()
    {
        var result = EvaluateInCurrentLaunch(ValidThreeLaunchRun());

        Assert.Equal(QuestLifecycleProofDecision.Pass, result.Decision);
        Assert.Contains("no replay", string.Join(" ", result.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_active_reconstruction_remains_inconclusive()
    {
        var observations = ValidThreeLaunchRun().Select(x => x.Phase == QuestLifecyclePhase.FullReload && x.LoadEpoch == "load-1"
            ? x with
            {
                NativeReference = null,
                NativeIdentity = QuestNativeIdentity.Empty,
                NativeTitle = null,
                NativeObjectiveState = null,
                ManagerMembership = null
            }
            : x).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("reconstruction", string.Join(" ", result.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completed_quest_reappearance_after_terminal_save_stops_the_proof()
    {
        var observations = ValidThreeLaunchRun().ToList();
        observations[^1] = observations[^1] with
        {
            NativeReference = "opaque-quest-3",
            NativeIdentity = QuestNativeIdentity.Opaque,
            IsReconstruction = true,
            ManagerMembership = true
        };

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("replay", string.Join(" ", result.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_active_reconstruction_stops_the_proof()
    {
        var observations = ValidThreeLaunchRun().ToList();
        observations.Insert(6, observations[5]);

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("reconstruction", string.Join(" ", result.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Host_owned_lifecycle_with_opaque_reconstruction_passes()
    {
        var result = EvaluateInCurrentLaunch(ValidRun());

        Assert.Equal(QuestLifecycleProofDecision.Pass, result.Decision);
    }

    [Fact]
    public void Duplicate_terminal_or_reload_observations_stop_the_proof()
    {
        var observations = ValidRun().ToList();
        observations.Add(observations.Single(x => x.Phase == QuestLifecyclePhase.Terminal));
        observations.Add(observations.First(x => x.Phase == QuestLifecyclePhase.FullReload));

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("duplicate", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_or_ui_completion_mutation_stops_the_proof()
    {
        var observations = ValidRun().ToList();
        observations.Add(Observation(QuestLifecyclePhase.Terminal, QuestProofActor.Ui,
            clientOrUiMutation: true, standingReceipt: null, rewardReceipt: null));

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("client/UI", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forbidden_mutation_stays_stop_when_native_state_is_also_missing()
    {
        var observations = ValidRun().Select(x => x with
        {
            NativeStateAvailability = QuestRuntimeAvailability.MissingNativeState,
            ClientOrUiMutation = x.Phase == QuestLifecyclePhase.Terminal
        }).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("client/UI", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_native_identity_is_inconclusive_not_a_durable_key()
    {
        var observations = ValidRun().Select(x => x with
        {
            NativeReference = x.Phase is QuestLifecyclePhase.Created or QuestLifecyclePhase.FullReload ? null : x.NativeReference,
            NativeIdentity = x.Phase is QuestLifecyclePhase.Created or QuestLifecyclePhase.FullReload
                ? QuestNativeIdentity.Empty
                : x.NativeIdentity
        }).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("reconstruction", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authority_drift_stops_the_proof()
    {
        var observations = ValidRun().ToList();
        observations.Add(Observation(QuestLifecyclePhase.ObjectiveUpdate, QuestProofActor.Client,
            clientOrUiMutation: false, standingReceipt: null, rewardReceipt: null));

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("authority", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_full_reload_observation_stops_the_proof()
    {
        var observations = ValidRun().Select(x => x.Phase == QuestLifecyclePhase.FullReload
            ? x with { Actor = QuestProofActor.Client }
            : x).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("authority", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stale_pre_reload_epoch_stops_the_proof()
    {
        var observations = ValidRun().Select(x => x.Phase == QuestLifecyclePhase.ObjectiveUpdate
            ? x with { LoadEpoch = "stale-epoch" }
            : x).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("epoch", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Full_reload_reusing_the_pre_reload_epoch_stops_the_proof()
    {
        var observations = ValidRun().Select(x => x.Phase == QuestLifecyclePhase.FullReload
            ? x with { LoadEpoch = "load-0" }
            : x).ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("epoch", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Additional_receipts_with_different_ids_stop_the_proof()
    {
        var observations = ValidRun().ToList();
        var saveIndex = observations.FindLastIndex(x => x.Phase == QuestLifecyclePhase.Save);
        observations[saveIndex] = observations[saveIndex] with
        {
            StandingReceipt = "standing-2",
            RewardReceipt = "reward-2"
        };

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("exactly-one", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_same_id_receipts_stop_the_proof()
    {
        var observations = ValidRun().ToList();
        var terminal = observations.Single(x => x.Phase == QuestLifecyclePhase.Terminal);
        var saveIndex = observations.FindLastIndex(x => x.Phase == QuestLifecyclePhase.Save);
        observations[saveIndex] = observations[saveIndex] with
        {
            StandingReceipt = terminal.StandingReceipt,
            RewardReceipt = terminal.RewardReceipt
        };

        var result = EvaluateInCurrentLaunch(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("exactly-one", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Optional_guid_instability_does_not_gate_contract_pass()
    {
        var guid = new QuestGuidExperimentObservation(
            RequestedGuid: "1f4c4e2b-5af4-4a34-9d5e-5f1a2b7c8d90",
            InitialStaticGuid: "1f4c4e2b-5af4-4a34-9d5e-5f1a2b7c8d90",
            ReloadStaticGuid: "different-native-guid",
            SurvivedFullReload: false,
            GetQuestByGuidMatchedAfterReload: false,
            Availability: QuestRuntimeAvailability.Available,
            Note: "native identity is opaque");

        var result = EvaluateInCurrentLaunch(ValidRun(), guid);

        Assert.Equal(QuestLifecycleProofDecision.Pass, result.Decision);
        Assert.Contains("optional", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_save_or_full_reload_is_inconclusive()
    {
        var observations = ValidRun()
            .Where(x => x.Phase is not QuestLifecyclePhase.Save and not QuestLifecyclePhase.FullReload)
            .ToArray();

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("reconstruction", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_creates_in_one_load_epoch_are_not_duplicate_reconstruction()
    {
        var observations = ValidRun().ToList();
        observations.Add(Observation(QuestLifecyclePhase.Created, QuestProofActor.Host,
            loadEpoch: "load-0", nativeReference: "quest-duplicate"));

        var result = QuestLifecycleProofEvaluator.Evaluate(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("duplicate", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completed_ledger_from_an_unrelated_process_cannot_pass()
    {
        var context = new QuestProofEvaluationContext(
            RunChainId: "run-chain-1",
            ProcessSessionId: "process-d",
            CurrentProcessLoadBoundaryObserved: true);

        var result = QuestLifecycleProofEvaluator.Evaluate(
            ValidThreeLaunchRun(),
            evaluationContext: context);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("current process", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("load-0", QuestLifecyclePhase.Save)]
    [InlineData("load-1", QuestLifecyclePhase.FullReload)]
    public void Missing_active_quest_state_at_save_or_reconstruction_is_inconclusive(
        string loadEpoch,
        QuestLifecyclePhase phase)
    {
        var observations = ValidThreeLaunchRun().Select(x =>
            x.LoadEpoch == loadEpoch && x.Phase == phase
                ? x with { NativeQuestState = null }
                : x).ToArray();

        var result = EvaluateInCurrentLaunch(observations);

        Assert.Equal(QuestLifecycleProofDecision.Inconclusive, result.Decision);
        Assert.Contains("Quest state", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("load-0", QuestLifecyclePhase.Save)]
    [InlineData("load-1", QuestLifecyclePhase.FullReload)]
    public void Non_active_quest_state_at_save_or_reconstruction_stops_the_proof(
        string loadEpoch,
        QuestLifecyclePhase phase)
    {
        var observations = ValidThreeLaunchRun().Select(x =>
            x.LoadEpoch == loadEpoch && x.Phase == phase
                ? x with { NativeQuestState = "Completed" }
                : x).ToArray();

        var result = EvaluateInCurrentLaunch(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("Active", result.Reasons.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("load-0", QuestLifecyclePhase.Save)]
    [InlineData("load-1", QuestLifecyclePhase.FullReload)]
    public void Non_active_objective_at_save_or_reconstruction_stops_the_proof(
        string loadEpoch,
        QuestLifecyclePhase phase)
    {
        var observations = ValidThreeLaunchRun().Select(x =>
            x.LoadEpoch == loadEpoch && x.Phase == phase
                ? x with { NativeObjectiveState = "Completed" }
                : x).ToArray();

        var result = EvaluateInCurrentLaunch(observations);

        Assert.Equal(QuestLifecycleProofDecision.Stop, result.Decision);
        Assert.Contains("objective", result.Reasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<QuestLifecycleObservation> ValidRun() => ValidThreeLaunchRun();

    private static IReadOnlyList<QuestLifecycleObservation> ValidThreeLaunchRun() =>
    [
        Observation(QuestLifecyclePhase.Created, QuestProofActor.Host, nativeReference: "opaque-quest-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            ManagerMembership = true,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-a"
        },
        Observation(QuestLifecyclePhase.Begun, QuestProofActor.Host, nativeReference: "opaque-quest-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            ManagerMembership = true,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-a"
        },
        Observation(QuestLifecyclePhase.ObjectiveEntry, QuestProofActor.Host, nativeReference: "opaque-quest-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeObjectiveState = "Active",
            ManagerMembership = true,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-a"
        },
        Observation(QuestLifecyclePhase.ObjectiveUpdate, QuestProofActor.Host, nativeReference: "opaque-quest-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeObjectiveState = "Active",
            ManagerMembership = true,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-a"
        },
        Observation(QuestLifecyclePhase.Save, QuestProofActor.Host, nativeReference: "opaque-quest-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeQuestState = "Active",
            NativeObjectiveState = "Active",
            ManagerMembership = true,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-a"
        },
        Observation(QuestLifecyclePhase.FullReload, QuestProofActor.Host, loadEpoch: "load-1", nativeReference: "opaque-quest-2", isReconstruction: true) with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeQuestState = "Active",
            NativeObjectiveState = "Active",
            ManagerMembership = true,
            ReloadClassification = QuestReloadClassification.ActiveReconstruction,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-b",
            LoadBoundaryObserved = true
        },
        Observation(QuestLifecyclePhase.Terminal, QuestProofActor.Host, loadEpoch: "load-1", nativeReference: "opaque-quest-2", terminal: QuestTerminalOutcome.Completed,
            standingReceipt: "standing-1", rewardReceipt: "reward-1") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeObjectiveState = "Active",
            ManagerMembership = false,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-b"
        },
        Observation(QuestLifecyclePhase.Save, QuestProofActor.Host, loadEpoch: "load-1", nativeReference: "opaque-quest-2") with
        {
            NativeTitle = "OC-44 typed Quest diagnostic",
            NativeObjectiveState = "Active",
            ManagerMembership = false,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-b"
        },
        Observation(QuestLifecyclePhase.FullReload, QuestProofActor.Host, loadEpoch: "load-2", nativeReference: null,
            nativeIdentity: QuestNativeIdentity.Empty, isReconstruction: false) with
        {
            NativeTitle = null,
            NativeObjectiveState = null,
            ManagerMembership = false,
            ReloadClassification = QuestReloadClassification.CompletedQuestAbsentNoReplay,
            RunChainId = "run-chain-1",
            ProcessSessionId = "process-c",
            LoadBoundaryObserved = true
        }
    ];

    private static QuestLifecycleProofResult EvaluateInCurrentLaunch(
        IReadOnlyList<QuestLifecycleObservation> observations,
        QuestGuidExperimentObservation? guidExperiment = null) =>
        QuestLifecycleProofEvaluator.Evaluate(
            observations,
            guidExperiment,
            new QuestProofEvaluationContext(
                RunChainId: "run-chain-1",
                ProcessSessionId: "process-c",
                CurrentProcessLoadBoundaryObserved: true));

    private static QuestLifecycleObservation Observation(
        QuestLifecyclePhase phase,
        QuestProofActor actor,
        string loadEpoch = "load-0",
        string? nativeReference = "opaque-quest-1",
        QuestNativeIdentity nativeIdentity = QuestNativeIdentity.Opaque,
        QuestTerminalOutcome terminal = QuestTerminalOutcome.None,
        string? standingReceipt = null,
        string? rewardReceipt = null,
        bool isReconstruction = false,
        bool clientOrUiMutation = false) =>
        new("oc10.dead-drop.small-courtesy", 1, loadEpoch, nativeReference, nativeIdentity,
            phase, actor, terminal, standingReceipt, rewardReceipt, isReconstruction, clientOrUiMutation);
}
