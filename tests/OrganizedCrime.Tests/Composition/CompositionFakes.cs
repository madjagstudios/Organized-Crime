// OC-151: shared test doubles behind the composition unit tests. Each fake defaults
// to throwing NotSupportedException; tests script only the members their path touches.
// The Chapter 3 lifecycle fakes mirror the Ch3Fake* quartet in
// Chapter3CheckpointServiceTests so a record-built service activates the same way.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Tests;

internal sealed class CompositionFakeStoryContext : IRelease1StoryHostContext
{
    public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
    public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("15115115-1151-5115-9151-151151151151"), 1, "oc151-test-player", Path.GetTempPath());

    public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
    {
        snapshot = Snapshot;
        return Status;
    }
}

internal sealed class CompositionFakeChapter3Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
{
    public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
    public Chapter3State? Stored { get; set; }

    public Chapter3StoreLoadResult Load() =>
        Stored is null
            ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
            : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");

    public Chapter3StoreUpdateResult Update(Chapter3State? state)
    {
        Stored = state;
        return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
    }

    public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) =>
        new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
}

internal sealed class CompositionFakeRelease1StoryRepository : IRelease1StoryRepository
{
    public Release1StoryStoreLoadResult Load() => throw new NotSupportedException();
    public Release1StoryStoreUpdateResult Update(Release1StoryState? state) => throw new NotSupportedException();
}

internal sealed class CompositionFakeGate : IChapter3StoryGate
{
    public bool Satisfied { get; set; }
    public bool IsEnvelopeSatisfied() => Satisfied;
}

internal sealed class CompositionFakeClock : IChapter3DayClock
{
    public int ElapsedDays { get; set; }
    public event Action? DayPassed;
    public event Action? WeekPassed;

    public bool TryReadElapsedDays(out int elapsedDays)
    {
        elapsedDays = ElapsedDays;
        return true;
    }

    public void RaiseDay() => DayPassed?.Invoke();
    public void RaiseWeek() => WeekPassed?.Invoke();
}

internal sealed class CompositionFakeOfficerWorld : IOcOfficerWorld
{
    public bool IsHostValue { get; set; }
    public int IsHostReads { get; private set; }
    public bool IsHost { get { IsHostReads++; return IsHostValue; } }
    public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryClearSwapMap(out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => throw new NotSupportedException();
    public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees) => throw new NotSupportedException();
}

internal sealed class CompositionFakeArthurWorld : IChapter3ArthurWorld
{
    public bool HasAuthority { get; set; }
    public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryParkArthur(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryComeArthur(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryFacePlayer(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryResumeWalk(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason) => throw new NotSupportedException();
    public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason) => throw new NotSupportedException();
    public bool PollArthurImpact() => throw new NotSupportedException();
}

internal sealed class CompositionFakeBeatProbeWorld : IChapter3BeatProbeWorld
{
    public bool IsHost { get; set; }
    public Func<(bool Ok, Chapter3BeatSnapshot Snapshot, string Reason)> Snapshot { get; set; } = () => throw new NotSupportedException();
    public Func<(bool Ok, bool On, string Reason)> ReadInvincible { get; set; } = () => throw new NotSupportedException();
    public Func<bool, (bool Ok, string Reason)> SetInvincible { get; set; } = _ => throw new NotSupportedException();
    public List<bool> SetInvincibleCalls { get; } = new();

    public bool TryReadSnapshot(out Chapter3BeatSnapshot snapshot, out string reason)
    {
        var result = Snapshot();
        snapshot = result.Snapshot;
        reason = result.Reason;
        return result.Ok;
    }

    public bool TryReadInvincible(out bool on, out string reason)
    {
        var result = ReadInvincible();
        on = result.On;
        reason = result.Reason;
        return result.Ok;
    }

    public bool TrySetInvincible(bool on, out string reason)
    {
        SetInvincibleCalls.Add(on);
        var result = SetInvincible(on);
        reason = result.Reason;
        return result.Ok;
    }
}

internal sealed class CompositionFakeBeatingWorld : IChapter3BeatingWorld
{
    public bool IsHost { get; set; }
    public bool IsPlayerSpawned { get; set; }
    public bool TryReadHealth(out float health, out float maxHealth, out string reason) => throw new NotSupportedException();
    public bool TrySetHealth(float health, out string reason) => throw new NotSupportedException();
    public bool TryLock(out string reason) => throw new NotSupportedException();
    public bool TryUnlock(out string reason) => throw new NotSupportedException();
    public bool TryFadeOut(out string reason) => throw new NotSupportedException();
    public bool TryFadeIn(out string reason) => throw new NotSupportedException();
    public bool TryJoltCamera(out string reason) => throw new NotSupportedException();
    public bool TryMoveToWake(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason) => throw new NotSupportedException();
    public bool TryOpenBill(out string reason) => throw new NotSupportedException();
}

internal sealed class CompositionFakeCheckpointHost : IChapter3CheckpointHost
{
    public Chapter3State? State { get; set; }
    public Chapter3CheckpointTarget Target { get; set; } = new(null, null);
    public List<Chapter3CheckpointTarget> Writes { get; } = new();
    public bool WriteResult { get; set; } = true;

    public Chapter3CheckpointTarget ReadCheckpointTarget() => Target;

    public bool TryWriteCheckpointTarget(Chapter3CheckpointTarget target)
    {
        Writes.Add(target);
        return WriteResult;
    }
}

internal sealed class CompositionFakeCheckpointWorld : IChapter3CheckpointWorld
{
    public bool IsHostValue { get; set; }
    public int IsHostReads { get; private set; }
    public bool IsHost { get { IsHostReads++; return IsHostValue; } }
    public Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yawDegrees, out string? handle) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? nativeCheckpoint) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose) => throw new NotSupportedException();
    public Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points) => throw new NotSupportedException();
}

internal sealed class CompositionThrowingCourtesyWorld : Release1FullWorldStub
{
    public Func<Release1SmallCourtesyWorldReadStatus> Minutes { get; set; } = () => throw new NotSupportedException();

    public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new NotSupportedException();

    public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
    {
        totalMinutes = 0d;
        return Minutes();
    }
}
