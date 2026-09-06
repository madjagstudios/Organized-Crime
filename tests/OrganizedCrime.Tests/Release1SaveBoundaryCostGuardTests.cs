using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-63 Part 3: the save and load boundary cost guards. The Syndicate HQ interior is rebuilt on
/// every save boundary and its rebuild used to walk every loaded GameObject six times; the story
/// sidecar used to serialize the whole story twice per write; and the OC-52 staging proof at load
/// reads all twenty five dead drops, so production loads must never reach it. Each guard pins the
/// shape of the fix, not only its effect, because the two Unity paths cannot run headless.
/// </summary>
public sealed class Release1SaveBoundaryCostGuardTests
{
    [Fact]
    public void The_hq_interior_rebuild_takes_one_scene_snapshot_and_shares_it()
    {
        var source = ReadRuntime("SyndicateHqInteriorRuntime.cs");

        // Exactly two full scene enumerations remain in the whole file: the shared snapshot the
        // rebuild takes, and the independent one owned-runtime validation needs because it has to
        // see the root the rebuild just created.
        Assert.Equal(2, Count(source, "Resources.FindObjectsOfTypeAll<GameObject>()"));
        Assert.Contains("private static GameObject[] SnapshotSceneObjects()", source, StringComparison.Ordinal);
        Assert.Contains("var sceneObjects = SnapshotSceneObjects();", source, StringComparison.Ordinal);
        Assert.Contains("CreateMaterialPalette(sceneObjects);", source, StringComparison.Ordinal);
        Assert.Contains("ResolveNativePropSources(sceneObjects)", source, StringComparison.Ordinal);

        // The nightclub shell is resolved once per rebuild rather than once per material role.
        Assert.Contains("private static GameObject? ResolveNightclubShellRoot(GameObject[] sceneObjects)", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "ResolveNightclubShellRoot(sceneObjects)"));
        Assert.Contains("FindNativeMaterial(role, shellRoot)", source, StringComparison.Ordinal);

        // A hierarchy path is only built for an object whose leaf name can match a catalog path.
        Assert.Contains("private static string LeafNameOf(string hierarchyPath)", source, StringComparison.Ordinal);
        Assert.Contains("catalogLeafNames.Contains(gameObject.name)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hq_interior_still_tears_down_at_every_boundary()
    {
        // OC-63 deliberately leaves the boundary teardown alone: the save must not capture a player
        // standing inside a pocket interior that will not exist on the next load, and the native
        // hold room closets are taken out of interaction by the same teardown. Keeping the interior
        // alive across a save is not something a door fingerprint check can make safe on its own.
        var source = ReadRuntime("SyndicateHqRuntimeService.cs");

        Assert.Contains("public bool OnSaveStart() => TeardownAtBoundary();", source, StringComparison.Ordinal);
        Assert.Contains("public bool OnPreLoad() => TeardownAtBoundary();", source, StringComparison.Ordinal);
        Assert.Contains("public bool OnPreSceneChange() => TeardownAtBoundary();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_story_sidecar_is_serialized_exactly_once_per_update()
    {
        var source = ReadPersistence("Release1StoryStateStore.cs");

        Assert.Equal(1, Count(source, "Release1StorySaveCodec.TrySerialize(incoming"));
    }

    [Fact]
    public void A_story_update_writes_the_sidecar_exactly_once()
    {
        using var folder = new TemporaryFolder();
        Assert.True(Release1StorySavePath.TryCreate(folder.Path, out var savePath, out _));
        var fileSystem = new CountingFileSystem();
        var store = new Release1StoryStateStore(savePath!, fileSystem);

        Assert.True(store.TryUpdate(NewStory(revision: 0), out var first));
        Assert.Equal(Release1StoryStoreUpdateStatus.Updated, first.Status);
        Assert.Equal(1, fileSystem.Writes);
        Assert.Equal(1, fileSystem.Replacements);

        Assert.True(store.TryUpdate(NewStory(revision: 1), out var second));
        Assert.Equal(Release1StoryStoreUpdateStatus.Updated, second.Status);
        Assert.Equal(2, fileSystem.Writes);
    }

    [Fact]
    public void An_idempotent_story_update_writes_nothing()
    {
        using var folder = new TemporaryFolder();
        Assert.True(Release1StorySavePath.TryCreate(folder.Path, out var savePath, out _));
        var fileSystem = new CountingFileSystem();
        var store = new Release1StoryStateStore(savePath!, fileSystem);

        Assert.True(store.TryUpdate(NewStory(revision: 0), out _));
        fileSystem.ResetCounters();

        Assert.True(store.TryUpdate(NewStory(revision: 0), out var repeat));

        Assert.Equal(Release1StoryStoreUpdateStatus.Idempotent, repeat.Status);
        Assert.Equal(0, fileSystem.Writes);
    }

    [Fact]
    public void The_oc_52_staging_proof_at_load_stays_behind_the_owner_qa_preference()
    {
        var mod = ReadMod();
        var index = mod.IndexOf("OC-52 staging proof at load", StringComparison.Ordinal);
        Assert.True(index > 0, "The OC-52 staging proof at load receipt was not found.");

        var gate = mod.LastIndexOf("if (_ownerQaKeysEnabled", index, StringComparison.Ordinal);
        Assert.True(gate > 0, "The OC-52 staging proof at load was not gated on the owner QA preference.");
    }

    [Fact]
    public void The_mod_reads_the_timing_threshold_from_a_melon_preference()
    {
        var mod = ReadMod();
        Assert.Contains("TimingReceiptThresholdPreference.Read()", mod, StringComparison.Ordinal);

        var preference = ReadRuntime("TimingReceiptThresholdPreference.cs");
        Assert.Contains("\"OrganizedCrime\"", preference, StringComparison.Ordinal);
        Assert.Contains("\"TimingReceiptThresholdMs\"", preference, StringComparison.Ordinal);
        Assert.Contains("OrganizedCrimeTimingReceipts.DefaultThresholdMs", preference, StringComparison.Ordinal);
    }

    private static Release1StoryState NewStory(long revision) =>
        Release1StoryState.CreateAccepted(
            "76561190000000001",
            "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1") with { Revision = revision };

    private static int Count(string source, string needle)
    {
        var total = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            total++;
            index += needle.Length;
        }
        return total;
    }

    private static string ReadMod() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Mod.cs"));

    private static string ReadRuntime(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", fileName));

    private static string ReadPersistence(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Persistence", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-63-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class CountingFileSystem : IRelease1StoryFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }
        public int Replacements { get; private set; }

        public void ResetCounters()
        {
            Writes = 0;
            Replacements = 0;
        }

        public bool FileExists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public string CreateTemporaryPath(string directory, string targetPath) =>
            System.IO.Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        public void WriteAllTextAndFlush(string path, string contents)
        {
            Writes++;
            _files[path] = contents;
        }
        public void ReplaceAtomically(string temporaryPath, string targetPath)
        {
            Replacements++;
            _files[targetPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
        }
        public void DeleteFile(string path) => _files.Remove(path);
    }
}
