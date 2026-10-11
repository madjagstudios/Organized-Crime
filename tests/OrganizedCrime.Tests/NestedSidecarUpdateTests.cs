using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

// Validated JSON must be local to each update so a nested rejected update
// cannot clear the outer update's JSON or make it write empty contents.
public sealed class NestedSidecarUpdateTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Chapter3_nested_rejected_update_does_not_clear_the_outer_write()
    {
        var files = new CallbackFileSystem();
        Assert.True(Chapter3SavePath.TryCreate(Path.GetTempPath(), out var path, out _));
        var store = new Chapter3StateStore(path!, files);
        var outer = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;
        var nested = false;
        files.ExistsCallback = () =>
        {
            if (nested)
                return;
            nested = true;
            Assert.False(store.TryUpdate(new Chapter3SaveEnvelope(99, outer), out var rejected));
            Assert.Equal(Chapter3StoreUpdateStatus.Rejected, rejected.Status);
        };

        Assert.True(store.TryUpdate(outer, out var update));

        Assert.True(nested);
        Assert.Equal(Chapter3StoreUpdateStatus.Updated, update.Status);
        Assert.True(Chapter3SaveCodec.TrySerialize(
            new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, outer),
            out var expected, out _));
        Assert.Equal(expected, files.Seeded(store.SavePath.SidecarFilePath));
    }

    [Fact]
    public void Story_nested_rejected_update_does_not_clear_the_outer_write()
    {
        var files = new CallbackFileSystem();
        Assert.True(Release1StorySavePath.TryCreate(Path.GetTempPath(), out var path, out _));
        var store = new Release1StoryStateStore(path!, files);
        var outer = Release1StoryState.CreateAccepted(
            PlayerId,
            Release1LogicalCorrelation.Create(
                PlayerId, Release1MissionCatalog.IntroScopeKey, 0,
                Release1TransitionKind.IntroAccepted, "intro-oc-148-blocker3").Value);
        var nested = false;
        files.ExistsCallback = () =>
        {
            if (nested)
                return;
            nested = true;
            Assert.False(store.TryUpdate(new Release1StorySaveEnvelope(99, outer), out var rejected));
            Assert.Equal(Release1StoryStoreUpdateStatus.Rejected, rejected.Status);
        };

        Assert.True(store.TryUpdate(outer, out var update));

        Assert.True(nested);
        Assert.Equal(Release1StoryStoreUpdateStatus.Updated, update.Status);
        Assert.True(Release1StorySaveCodec.TrySerialize(
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, outer),
            out var expected, out _));
        Assert.Equal(expected, files.Seeded(store.SavePath.SidecarFilePath));
    }

    private sealed class CallbackFileSystem
        : IChapter3FileSystem, IRelease1StoryFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public Action? ExistsCallback { get; set; }

        public string? Seeded(string path) => _files.TryGetValue(path, out var contents) ? contents : null;

        public bool FileExists(string path)
        {
            ExistsCallback?.Invoke();
            return _files.ContainsKey(path);
        }

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
