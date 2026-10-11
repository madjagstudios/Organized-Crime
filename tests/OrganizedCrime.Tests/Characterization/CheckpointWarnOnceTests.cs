using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-147 characterization: checkpoint warnings fire once per cause, re-arm per
// cause when the step succeeds, and re-arm fully on pre-load. Pinned before
// OnceLog replaces WarnOnce/ClearWarn; passes unchanged after.
public sealed class CheckpointWarnOnceTests
{
    [Fact]
    public void Spawn_warns_once_per_label_and_rearms_per_cause_and_on_preload()
    {
        var logs = new List<string>();
        var checkpoints = new FakeCheckpointWorld { IsHost = true };
        var world = new UnusedWorld();
        var story = new Release1StoryRuntimeService(new StoryContext(), new StoryRepository());
        var ledger = new FakeLedger();
        var chief = new Release1ChiefService(
            story, world, new Release1ChiefTierObserver(), ledger.Read, ledger.Wipe, ledger.Epochs,
            LocalPressureProfile.Moderate);
        var runtime = new Chapter3RuntimeService(new Ch3Context(), new Ch3Repository(), new Ch3Gate(), new Ch3Clock());
        var roster = new OcOfficerRosterService(new FakeOfficerWorld(), "oc147", _ => { });
        using var service = new Chapter3CheckpointService(runtime, chief, world, checkpoints, roster, logs.Add);

        const string label = "oc147";
        var expected = $"Checkpoint spawn failed for {label}.";
        int Count() => logs.Count(line => line == expected);

        checkpoints.SpawnStatus = Chapter3CheckpointWorldStatus.Failed;
        Assert.False(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(1, Count());

        Assert.False(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(1, Count());

        checkpoints.SpawnStatus = Chapter3CheckpointWorldStatus.Ok;
        checkpoints.SpawnHandle = "handle-1";
        Assert.True(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(1, Count());
        Assert.True(service.QaClearTests().Ok);

        checkpoints.SpawnStatus = Chapter3CheckpointWorldStatus.Failed;
        checkpoints.SpawnHandle = null;
        Assert.False(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(2, Count());

        Assert.False(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(2, Count());

        service.OnPreLoad();
        Assert.False(service.QaSpawnTestAt(label, 1f, 2f, 3f, 0f).Ok);
        Assert.Equal(3, Count());
    }

    private sealed class FakeCheckpointWorld : IChapter3CheckpointWorld
    {
        public bool IsHost { get; set; } = true;
        public Chapter3CheckpointWorldStatus SpawnStatus { get; set; } = Chapter3CheckpointWorldStatus.Failed;
        public string? SpawnHandle { get; set; }
        public Chapter3CheckpointWorldStatus DespawnStatus { get; set; } = Chapter3CheckpointWorldStatus.Ok;

        public Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions)
        {
            positions = Array.Empty<Chapter3OwnedPropertyPosition>();
            return Chapter3CheckpointWorldStatus.Failed;
        }

        public Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode)
        {
            propertyCode = null;
            return Chapter3CheckpointWorldStatus.Failed;
        }

        public Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions)
        {
            positions = Array.Empty<Chapter3NativeCheckpointPosition>();
            return Chapter3CheckpointWorldStatus.Failed;
        }

        public Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yawDegrees, out string? handle)
        {
            handle = SpawnHandle;
            return SpawnStatus;
        }

        public Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle) => DespawnStatus;

        public Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? nativeCheckpoint)
        {
            nativeCheckpoint = null;
            return Chapter3CheckpointWorldStatus.Failed;
        }

        public Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled) =>
            Chapter3CheckpointWorldStatus.Failed;

        public Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose)
        {
            pose = new Chapter3CameraPose(0f, 0f, 0f, 0f);
            return Chapter3CheckpointWorldStatus.Failed;
        }

        public Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points)
        {
            points = Array.Empty<Chapter3StandPoint>();
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    private sealed class UnusedWorld : Release1ProductionMissionWorldStub, OrganizedCrime.Runtime.Release1.World.IChiefWorld
    {
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = null!;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
    }

    private sealed class FakeOfficerWorld : IOcOfficerWorld
    {
        public bool IsHost { get; set; } = true;

        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
        {
            minutes = 0d;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail)
        {
            status = null!;
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
        {
            clone = default;
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
        {
            knockedOut = false;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
        {
            position = default;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        {
            activeBehaviour = string.Empty;
            checkpointActive = false;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state)
        {
            state = null!;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        {
            diagnostics = null!;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
        {
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        {
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        {
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryClearSwapMap(out string detail)
        {
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
        {
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail)
        {
            leaked = false;
            detail = string.Empty;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        {
            status = null!;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
        {
            counts = null!;
            return OcOfficerWorldStatus.Failed;
        }

        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
        {
            position = default;
            yawDegrees = 0f;
            return OcOfficerWorldStatus.Failed;
        }
    }

    private sealed class StoryContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, "76561190000000001", Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class StoryRepository : IRelease1StoryRepository
    {
        public Release1StoryStoreLoadResult Load() => throw new NotSupportedException();

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) => throw new NotSupportedException();
    }

    private sealed class FakeLedger
    {
        public LocalPressureState? Read(string playerId) => null;

        public LocalPressureEvidenceWriteResult Wipe(string playerId) =>
            new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = Guid.Empty;
            loadEpoch = 0;
            return true;
        }
    }

    private sealed class Ch3Context : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new Release1StoryHostContextSnapshot(Guid.NewGuid(), 1, "76561190000000001", Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class Ch3Repository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() => throw new NotSupportedException();

        public Chapter3StoreUpdateResult Update(Chapter3State? state) => throw new NotSupportedException();
    }

    private sealed class Ch3Gate : IChapter3StoryGate
    {
        public bool IsEnvelopeSatisfied() => true;
    }

    private sealed class Ch3Clock : IChapter3DayClock
    {
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = 11;
            return true;
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }
}
