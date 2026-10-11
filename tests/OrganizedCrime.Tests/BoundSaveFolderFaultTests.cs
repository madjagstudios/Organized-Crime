using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// A throwing BoundSaveFolder getter must quarantine the load with
// RepositoryFaulted; the getter must run inside the save-folder check's try.
public sealed class BoundSaveFolderFaultTests
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Story_throwing_bound_folder_getter_quarantines_with_repository_faulted()
    {
        var service = new Release1StoryRuntimeService(new FakeContext(), new StoryThrowingBoundRepository());
        service.OnPreLoad();

        var result = service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Equal("bound boom", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
    }

    [Fact]
    public void Chapter3_throwing_bound_folder_getter_quarantines_with_repository_faulted()
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new Chapter3ThrowingBoundRepository(), new FakeGate(), new FakeClock());
        service.OnPreLoad();

        var result = service.OnLoadComplete();

        Assert.Equal(Chapter3RuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Equal("bound boom", result.Message);
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

    private sealed class StoryThrowingBoundRepository
        : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder => throw new InvalidOperationException("bound boom");

        public Release1StoryStoreLoadResult Load() =>
            new(true, Release1StoryStoreLoadStatus.Empty, Release1StorySaveEnvelope.CreateEmpty(),
                Release1StoryStoreFailureReason.None, string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) =>
            new(true, Release1StoryStoreUpdateStatus.Updated,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
                Release1StoryStoreFailureReason.None, string.Empty);
    }

    private sealed class Chapter3ThrowingBoundRepository
        : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => throw new InvalidOperationException("bound boom");

        public Chapter3StoreLoadResult Load() =>
            new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(),
                Chapter3StoreFailureReason.None, string.Empty);

        public Chapter3StoreUpdateResult Update(Chapter3State? state) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, string.Empty);
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
