using OrganizedCrime.Core.Persistence;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148: the per-service facade mappings over SidecarStore. Each store keeps
// its typed results, reasons and messages; these pins prove the neutral
// outcomes map back exactly, through the real stores over a fake file system.
public sealed class SidecarStoreMappingTests
{
    [Fact]
    public void Story_wrong_schema_update_rejects_before_reading()
    {
        var files = new FakeFileSystem();
        var store = StoryStore(files);

        Assert.False(store.TryUpdate(new Release1StorySaveEnvelope(99, null), out var rejected));

        Assert.Equal(Release1StoryStoreUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(Release1StoryStoreFailureReason.InvalidStory, rejected.FailureReason);
        Assert.Equal("Release 1 story envelope schema was unsupported.", rejected.Message);
        Assert.Equal(0, files.ReadCalls);
    }

    [Fact]
    public void Story_corrupt_load_fails_with_raw_contents()
    {
        const string garbage = "{ not a story sidecar";
        var files = new FakeFileSystem();
        var store = StoryStore(files);
        files.Seed(store.SavePath.SidecarFilePath, garbage);

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(Release1StoryStoreLoadStatus.Failed, load.Status);
        Assert.Equal(Release1StoryStoreFailureReason.InvalidStory, load.FailureReason);
        Assert.NotEqual(string.Empty, load.Message);
        Assert.Equal(garbage, load.RawContents);
    }

    [Fact]
    public void Story_preserve_round_trips_and_failure_maps()
    {
        const string garbage = "{ quarantined story bytes";
        var files = new FakeFileSystem();
        var store = StoryStore(files);

        Assert.True(store.TryPreserveRawContents(garbage, out var preserved));
        Assert.Equal(Release1StoryStoreUpdateStatus.Updated, preserved.Status);
        Assert.Null(preserved.Envelope);
        Assert.Equal("Quarantined story sidecar bytes were preserved.", preserved.Message);
        Assert.Equal(garbage, files.Seeded(store.SavePath.SidecarFilePath));

        files.FailStage = FakeFileSystem.Stage.Write;
        Assert.False(store.TryPreserveRawContents(garbage, out var failed));
        Assert.Equal(Release1StoryStoreUpdateStatus.Rejected, failed.Status);
        Assert.Equal(Release1StoryStoreFailureReason.TemporaryFileWriteFailed, failed.FailureReason);
    }

    [Fact]
    public void Story_idempotent_update_replays_without_writing()
    {
        var files = new FakeFileSystem();
        var store = StoryStore(files);
        var empty = Release1StorySaveEnvelope.CreateEmpty();
        Assert.True(store.TryUpdate(empty, out _));
        files.WriteCalls = 0;

        Assert.True(store.TryUpdate(empty, out var idempotent));

        Assert.Equal(Release1StoryStoreUpdateStatus.Idempotent, idempotent.Status);
        Assert.Equal(empty, idempotent.Envelope);
        Assert.Equal("Story update was already stored.", idempotent.Message);
        Assert.Equal(0, files.WriteCalls);
    }

    [Theory]
    [InlineData("Read", Release1StoryStoreFailureReason.SidecarReadFailed)]
    [InlineData("CreateDirectory", Release1StoryStoreFailureReason.DirectoryCreationFailed)]
    [InlineData("TempPath", Release1StoryStoreFailureReason.TemporaryFileWriteFailed)]
    [InlineData("Write", Release1StoryStoreFailureReason.TemporaryFileWriteFailed)]
    [InlineData("Replace", Release1StoryStoreFailureReason.AtomicReplacementFailed)]
    public void Story_io_failures_map_per_stage(string stageName, Release1StoryStoreFailureReason reason)
    {
        var stage = Enum.Parse<FakeFileSystem.Stage>(stageName);
        var story = NonEmptyStory();
        var files = new FakeFileSystem();
        var store = StoryStore(files);
        if (stage == FakeFileSystem.Stage.Read)
            Assert.True(store.TryUpdate(story, out _));
        files.FailStage = stage;

        Assert.False(store.TryUpdate(story, out var rejected));

        Assert.Equal(Release1StoryStoreUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(reason, rejected.FailureReason);
        Assert.NotEqual(string.Empty, rejected.Message);
    }

    [Fact]
    public void Chapter_wrong_schema_update_rejects_before_reading()
    {
        var files = new FakeFileSystem();
        var store = ChapterStore(files);

        Assert.False(store.TryUpdate(new Chapter3SaveEnvelope(99, null), out var rejected));

        Assert.Equal(Chapter3StoreUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(Chapter3StoreFailureReason.InvalidState, rejected.FailureReason);
        Assert.Equal("Chapter 3 envelope schema was unsupported.", rejected.Message);
        Assert.Equal(0, files.ReadCalls);
    }

    [Fact]
    public void Chapter_idempotent_update_replays_without_writing()
    {
        var files = new FakeFileSystem();
        var store = ChapterStore(files);
        var state = new Chapter3State("76561190000000001", Chapter3Phase.NotStarted, null, 11, 5);
        Assert.True(store.TryUpdate(state, out _));
        files.WriteCalls = 0;

        Assert.True(store.TryUpdate(state, out var idempotent));

        Assert.Equal(Chapter3StoreUpdateStatus.Idempotent, idempotent.Status);
        Assert.Equal("Chapter update was already stored.", idempotent.Message);
        Assert.Equal(0, files.WriteCalls);
    }

    [Theory]
    [InlineData("Read", Chapter3StoreFailureReason.SidecarReadFailed)]
    [InlineData("CreateDirectory", Chapter3StoreFailureReason.DirectoryCreationFailed)]
    [InlineData("TempPath", Chapter3StoreFailureReason.TemporaryFileWriteFailed)]
    [InlineData("Write", Chapter3StoreFailureReason.TemporaryFileWriteFailed)]
    [InlineData("Replace", Chapter3StoreFailureReason.AtomicReplacementFailed)]
    public void Chapter_io_failures_map_per_stage(string stageName, Chapter3StoreFailureReason reason)
    {
        var stage = Enum.Parse<FakeFileSystem.Stage>(stageName);
        var files = new FakeFileSystem();
        var store = ChapterStore(files);
        var state = new Chapter3State("76561190000000001", Chapter3Phase.NotStarted, null, 11, 5);
        if (stage == FakeFileSystem.Stage.Read)
            Assert.True(store.TryUpdate(state, out _));
        files.FailStage = stage;

        Assert.False(store.TryUpdate(state, out var rejected));

        Assert.Equal(Chapter3StoreUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(reason, rejected.FailureReason);
        Assert.NotEqual(string.Empty, rejected.Message);
    }

    [Fact]
    public void Chapter_preserve_failure_maps()
    {
        var files = new FakeFileSystem { FailStage = FakeFileSystem.Stage.Write };
        var store = ChapterStore(files);

        Assert.False(store.TryPreserveRawContents("{ bytes", out var failed));

        Assert.Equal(Chapter3StoreUpdateStatus.Rejected, failed.Status);
        Assert.Equal(Chapter3StoreFailureReason.TemporaryFileWriteFailed, failed.FailureReason);
    }

    [Fact]
    public void Pressure_corrupt_load_fails_with_empty_or_malformed()
    {
        const string garbage = "{ not a pressure sidecar";
        var files = new FakeFileSystem();
        var store = PressureStore(files);
        files.Seed(store.SavePath.SidecarFilePath, garbage);

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(LocalPressureStoreLoadStatus.Failed, load.Status);
        Assert.Equal(LocalPressureStoreFailureReason.EmptyOrMalformedJson, load.FailureReason);
        Assert.NotEqual(string.Empty, load.Message);
    }

    [Theory]
    [InlineData("Read", LocalPressureStoreFailureReason.SidecarReadFailed, "Local Pressure sidecar could not be read: ")]
    [InlineData("CreateDirectory", LocalPressureStoreFailureReason.DirectoryCreationFailed, "Local Pressure sidecar directory could not be created: ")]
    [InlineData("TempPath", LocalPressureStoreFailureReason.TemporaryFileWriteFailed, "Local Pressure sidecar write failed: ")]
    [InlineData("Write", LocalPressureStoreFailureReason.TemporaryFileWriteFailed, "Local Pressure temporary sidecar could not be written: ")]
    [InlineData("Replace", LocalPressureStoreFailureReason.AtomicReplacementFailed, "Local Pressure sidecar could not be replaced atomically: ")]
    public void Pressure_io_failures_map_per_stage_with_wordings(string stageName, LocalPressureStoreFailureReason reason, string prefix)
    {
        var stage = Enum.Parse<FakeFileSystem.Stage>(stageName);
        var files = new FakeFileSystem();
        var store = PressureStore(files);
        var record = new LocalPressurePlayerRecord("player-1", 10, false, null, null, null, null, null, 1);
        if (stage == FakeFileSystem.Stage.Read)
            Assert.True(store.TryUpdate(record, out _));
        files.FailStage = stage;

        Assert.False(store.TryUpdate(record with { Revision = 2 }, out var rejected));

        Assert.Equal(LocalPressureStoreUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(reason, rejected.FailureReason);
        Assert.StartsWith(prefix, rejected.Message);
    }

    private static Release1StoryState NonEmptyStory() =>
        Release1StoryState.CreateAccepted("76561190000000001",
            Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryStateStore StoryStore(FakeFileSystem files)
    {
        Assert.True(Release1StorySavePath.TryCreate(Path.GetTempPath(), out var path, out _));
        return new Release1StoryStateStore(path!, files);
    }

    private static Chapter3StateStore ChapterStore(FakeFileSystem files)
    {
        Assert.True(Chapter3SavePath.TryCreate(Path.GetTempPath(), out var path, out _));
        return new Chapter3StateStore(path!, files);
    }

    private static LocalPressureStateStore PressureStore(FakeFileSystem files)
    {
        Assert.True(LocalPressureSavePath.TryCreate(Path.GetTempPath(), out var path, out _));
        return new LocalPressureStateStore(path!, files);
    }

    private sealed class FakeFileSystem : IChapter3FileSystem, IRelease1StoryFileSystem, ILocalPressureFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public enum Stage { Read, CreateDirectory, TempPath, Write, Replace }

        public Stage? FailStage { get; set; }
        public int ReadCalls { get; set; }
        public int WriteCalls { get; set; }

        public void Seed(string path, string contents) => _files[path] = contents;
        public string? Seeded(string path) => _files.TryGetValue(path, out var contents) ? contents : null;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public string ReadAllText(string path)
        {
            ReadCalls++;
            if (FailStage == Stage.Read) throw new IOException("read boom");
            return _files[path];
        }

        public void CreateDirectory(string path)
        {
            if (FailStage == Stage.CreateDirectory) throw new IOException("create-directory boom");
        }

        public string CreateTemporaryPath(string directory, string targetPath)
        {
            if (FailStage == Stage.TempPath) throw new IOException("temp boom");
            return targetPath + ".tmp";
        }

        public void WriteAllTextAndFlush(string path, string contents)
        {
            WriteCalls++;
            if (FailStage == Stage.Write) throw new IOException("write boom");
            _files[path] = contents;
        }

        public void ReplaceAtomically(string temporaryPath, string targetPath)
        {
            if (FailStage == Stage.Replace) throw new IOException("replace boom");
            _files[targetPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
        }

        public void DeleteFile(string path) => _files.Remove(path);
    }
}
