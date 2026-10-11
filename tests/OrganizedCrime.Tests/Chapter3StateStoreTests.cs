using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StateStoreTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Round_trip_preserves_state()
    {
        var store = CreateStore(out _);
        var state = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;

        Assert.True(store.TryUpdate(state, out var update));
        Assert.Equal(Chapter3StoreUpdateStatus.Updated, update.Status);
        Assert.True(store.TryLoad(out var load));

        Assert.Equal(Chapter3StoreLoadStatus.Loaded, load.Status);
        Assert.Equal(state, load.Envelope!.State);
    }

    [Fact]
    public void Same_revision_with_different_content_conflicts()
    {
        var store = CreateStore(out _);
        var first = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 10, envelopeSatisfied: false).State;
        var rival = new Chapter3State(PlayerId, Chapter3Phase.NotStarted, null, 12, first.Revision);
        Assert.True(store.TryUpdate(first, out _));

        Assert.False(store.TryUpdate(rival, out var conflict));

        Assert.Equal(Chapter3StoreUpdateStatus.Rejected, conflict.Status);
        Assert.Equal(Chapter3StoreFailureReason.RevisionConflict, conflict.FailureReason);
    }

    [Fact]
    public void Stale_revision_is_rejected()
    {
        var store = CreateStore(out _);
        var first = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 10, envelopeSatisfied: false).State;
        var second = Chapter3State.ProcessDayTick(first, elapsedDays: 11, envelopeSatisfied: false).State;
        Assert.True(store.TryUpdate(second, out _));

        Assert.False(store.TryUpdate(first, out var stale));

        Assert.Equal(Chapter3StoreFailureReason.StaleRevision, stale.FailureReason);
    }

    [Fact]
    public void Different_player_id_conflicts()
    {
        var store = CreateStore(out _);
        var first = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 10, envelopeSatisfied: false).State;
        var other = new Chapter3State("76561190000000002", Chapter3Phase.NotStarted, null, 11, first.Revision + 1);
        Assert.True(store.TryUpdate(first, out _));

        Assert.False(store.TryUpdate(other, out var conflict));

        Assert.Equal(Chapter3StoreFailureReason.RevisionConflict, conflict.FailureReason);
    }

    [Fact]
    public void Corrupt_file_fails_load_with_raw_contents_preserved()
    {
        const string garbage = "{ this is not valid chapter 3 json";
        var store = CreateStore(out var files);
        files.Seed(store.SavePath.SidecarFilePath, garbage);

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(Chapter3StoreLoadStatus.Failed, load.Status);
        Assert.Equal(garbage, load.RawContents);

        Assert.True(store.TryPreserveRawContents(load.RawContents!, out var preserved));
        Assert.Equal(Chapter3StoreUpdateStatus.Updated, preserved.Status);
        Assert.Equal(garbage, files.Seeded(store.SavePath.SidecarFilePath));
    }

    [Fact]
    public void Unknown_future_schema_is_rejected()
    {
        var store = CreateStore(out var files);
        files.Seed(store.SavePath.SidecarFilePath, """{"schemaVersion":99,"state":null}""");

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(Chapter3StoreLoadStatus.Failed, load.Status);
        Assert.Equal(Chapter3StoreFailureReason.InvalidState, load.FailureReason);
    }

    [Fact]
    public void Missing_sidecar_loads_an_empty_envelope()
    {
        var store = CreateStore(out _);

        Assert.True(store.TryLoad(out var load));

        Assert.Equal(Chapter3StoreLoadStatus.Empty, load.Status);
        Assert.Null(load.Envelope!.State);
    }

    [Fact]
    public void Uses_its_own_sidecar_file()
    {
        var store = CreateStore(out _);

        Assert.EndsWith("chapter3.json", store.SavePath.SidecarFilePath);
    }

    private static Chapter3StateStore CreateStore(out FakeFileSystem files)
    {
        files = new FakeFileSystem();
        Assert.True(Chapter3SavePath.TryCreate(System.IO.Path.GetTempPath(), out var path, out _));
        return new Chapter3StateStore(path!, files);
    }

    private sealed class FakeFileSystem : IChapter3FileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string path, string contents) => _files[path] = contents;
        public string? Seeded(string path) => _files.TryGetValue(path, out var contents) ? contents : null;

        public bool FileExists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public string CreateTemporaryPath(string directory, string targetPath) => targetPath + ".tmp";
        public void WriteAllTextAndFlush(string path, string contents) => _files[path] = contents;
        public void ReplaceAtomically(string temporaryPath, string targetPath)
        {
            _files[targetPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
        }
        public void DeleteFile(string path) => _files.Remove(path);
    }
}
