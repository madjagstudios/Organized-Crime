using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.StoryPrep;

/// <summary>
/// A minimal in-memory host context, mirroring the shape
/// tests/OrganizedCrime.Tests/Release1TheEnvelopeMissionServiceTests.cs's FakeContext uses to drive
/// Release1StoryRuntimeService outside the game: a fixed session epoch, load epoch 1, and whichever
/// player id the caller supplies. Always reports Ready, since this tool runs with the game closed.
/// </summary>
internal sealed class StoryPrepHostContext : IRelease1StoryHostContext
{
    private static readonly Guid SessionEpoch = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public StoryPrepHostContext(string playerId)
    {
        Snapshot = new Release1StoryHostContextSnapshot(SessionEpoch, 1, playerId, Path.GetTempPath());
    }

    public Release1StoryHostContextSnapshot Snapshot { get; }

    public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
    {
        snapshot = Snapshot;
        return Release1StoryHostContextReadStatus.Ready;
    }
}

/// <summary>
/// A minimal in-memory repository, mirroring the shape
/// tests/OrganizedCrime.Tests/Release1TheEnvelopeMissionServiceTests.cs's FakeRepository uses: it
/// never touches disk, so Release1StoryRuntimeService can be driven purely in memory and the tool
/// can decide separately how and where to persist the resulting state.
/// </summary>
internal sealed class StoryPrepRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
{
    public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
    public Release1StoryState? StoredState { get; private set; }

    public Release1StoryStoreLoadResult Load() => new(
        true,
        StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
        new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
        Release1StoryStoreFailureReason.None,
        string.Empty);

    public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
    {
        StoredState = state;
        return new(
            true,
            Release1StoryStoreUpdateStatus.Updated,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
            Release1StoryStoreFailureReason.None,
            string.Empty);
    }
}
