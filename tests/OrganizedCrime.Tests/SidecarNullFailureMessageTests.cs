using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// Succeeded=false with a null Message must still fail the save and quarantine
// the service with SidecarSaveFailed; a null failure message is not success.
public sealed class SidecarNullFailureMessageTests
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Story_failed_update_with_null_message_quarantines_with_sidecar_save_failed()
    {
        var service = new Release1StoryRuntimeService(new FakeContext(), new StoryNullMessageRepository());
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnSaveStart();

        var result = service.OnSaveComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.SidecarSaveFailed, result.RejectReason);
        Assert.Null(result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
    }

    [Fact]
    public void Chapter3_failed_update_with_null_message_quarantines_with_sidecar_save_failed()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new Chapter3NullMessageRepository(), new FakeGate(), new FakeClock());
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnSaveStart();

        var result = service.OnSaveComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.SidecarSaveFailed, result.RejectReason);
        Assert.Null(result.Message);
        Assert.Equal(Chapter3RuntimePhase.Quarantined, service.Phase);
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class StoryNullMessageRepository
        : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());

        public Release1StoryStoreLoadResult Load() =>
            new(true, Release1StoryStoreLoadStatus.Empty, Release1StorySaveEnvelope.CreateEmpty(),
                Release1StoryStoreFailureReason.None, string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) =>
            new(false, Release1StoryStoreUpdateStatus.Rejected, null,
                Release1StoryStoreFailureReason.AtomicReplacementFailed, null!);
    }

    private sealed class Chapter3NullMessageRepository
        : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());

        public Chapter3StoreLoadResult Load() =>
            new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(),
                Chapter3StoreFailureReason.None, string.Empty);

        public Chapter3StoreUpdateResult Update(Chapter3State? state) =>
            new(false, Chapter3StoreUpdateStatus.Rejected, null,
                Chapter3StoreFailureReason.AtomicReplacementFailed, null!);
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool IsEnvelopeSatisfied() => true;
    }

#pragma warning disable CS0067
    private sealed class FakeClock : IChapter3DayClock
    {
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = 11;
            return true;
        }
    }
#pragma warning restore CS0067
}
