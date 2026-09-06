using OrganizedCrime.QuestLifecycleProof;
using Xunit;

namespace OrganizedCrime.QuestLifecycleProof.Tests;

public sealed class QuestProofProcessSessionTests
{
    [Fact]
    public void Duplicate_load_complete_after_active_save_is_an_accepted_noop()
    {
        var persisted = new[]
        {
            SavedObservation("run-chain-1", "process-a", "load-0")
        };
        var process = QuestProofProcessSession.Restore(
            persisted,
            processSessionId: "process-b",
            newRunChainId: "unused-new-chain");

        var first = process.ObserveLoadComplete(persisted);
        var duplicate = process.ObserveLoadComplete(persisted);

        Assert.True(first.Accepted, first.Reason);
        Assert.True(first.ShouldRecordReload);
        Assert.False(first.IsDuplicateNoOp);
        Assert.Equal("load-1", first.NextLoadEpoch);
        Assert.True(duplicate.Accepted, duplicate.Reason);
        Assert.False(duplicate.ShouldRecordReload);
        Assert.True(duplicate.IsDuplicateNoOp);
        Assert.Null(duplicate.NextLoadEpoch);
        Assert.False(duplicate.IsReconstruction);
        Assert.Equal(QuestReloadClassification.None, duplicate.ReloadClassification);
        Assert.Contains("duplicate", duplicate.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(process.EvaluationContext.CurrentProcessLoadBoundaryObserved);
        Assert.True(process.EvaluationContext.CurrentProcessLoadBoundaryValid);
    }

    [Fact]
    public void Duplicate_load_complete_after_no_pending_save_is_an_accepted_noop()
    {
        var process = QuestProofProcessSession.Restore(
            Array.Empty<QuestLifecycleObservation>(),
            processSessionId: "process-a",
            newRunChainId: "run-chain-1");

        var first = process.ObserveLoadComplete(Array.Empty<QuestLifecycleObservation>());
        var duplicate = process.ObserveLoadComplete(Array.Empty<QuestLifecycleObservation>());

        Assert.True(first.Accepted, first.Reason);
        Assert.False(first.ShouldRecordReload);
        Assert.False(first.IsDuplicateNoOp);
        Assert.True(duplicate.Accepted, duplicate.Reason);
        Assert.False(duplicate.ShouldRecordReload);
        Assert.True(duplicate.IsDuplicateNoOp);
        Assert.Null(duplicate.NextLoadEpoch);
        Assert.True(process.EvaluationContext.CurrentProcessLoadBoundaryObserved);
        Assert.True(process.EvaluationContext.CurrentProcessLoadBoundaryValid);
    }

    [Fact]
    public void Later_same_process_load_complete_cannot_create_a_second_reload_epoch()
    {
        var initialEvidence = new[]
        {
            SavedObservation("run-chain-1", "process-a", "load-0")
        };
        var process = QuestProofProcessSession.Restore(
            initialEvidence,
            processSessionId: "process-b",
            newRunChainId: "unused-new-chain");

        var first = process.ObserveLoadComplete(initialEvidence);
        var laterEvidence = new[]
        {
            initialEvidence[0],
            SavedObservation("run-chain-1", "process-b", "load-1")
        };
        var laterSameProcessBoundary = process.ObserveLoadComplete(laterEvidence);

        Assert.True(first.ShouldRecordReload);
        Assert.Equal("load-1", first.NextLoadEpoch);
        Assert.True(laterSameProcessBoundary.Accepted, laterSameProcessBoundary.Reason);
        Assert.False(laterSameProcessBoundary.ShouldRecordReload);
        Assert.True(laterSameProcessBoundary.IsDuplicateNoOp);
        Assert.Null(laterSameProcessBoundary.NextLoadEpoch);
        Assert.False(laterSameProcessBoundary.IsReconstruction);
        Assert.Equal(QuestReloadClassification.None, laterSameProcessBoundary.ReloadClassification);
        Assert.True(process.EvaluationContext.CurrentProcessLoadBoundaryValid);
    }

    [Fact]
    public void Missing_run_chain_marker_invalidates_persisted_evidence()
    {
        var persisted = new[]
        {
            SavedObservation(runChainId: null, "process-a", "load-0")
        };

        var process = QuestProofProcessSession.Restore(
            persisted,
            processSessionId: "process-b",
            newRunChainId: "new-chain");

        Assert.False(process.PersistedChainValid);
        Assert.Contains("run-chain", process.InvalidReason, StringComparison.OrdinalIgnoreCase);
    }

    private static QuestLifecycleObservation SavedObservation(
        string? runChainId,
        string processSessionId,
        string loadEpoch) =>
        new(
            "oc44.mission",
            1,
            loadEpoch,
            "opaque-quest",
            QuestNativeIdentity.Opaque,
            QuestLifecyclePhase.Save,
            QuestProofActor.Host,
            QuestTerminalOutcome.None,
            null,
            null,
            false,
            false,
            RunChainId: runChainId,
            ProcessSessionId: processSessionId,
            LoadBoundaryObserved: false);
}
