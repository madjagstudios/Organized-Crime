using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-63 Part 3: the save and load boundary cost guards. The Syndicate HQ interior is rebuilt on
/// every save boundary and its rebuild used to walk every loaded GameObject six times; the story
/// sidecar used to serialize the whole story twice per write; and the OC-52 staging proof at load
/// reads all twenty five dead drops, so production loads must never reach it. Each guard pins the
/// shape of the fix, not only its effect, because the two Unity paths cannot run headless.
/// </summary>
[Collection("OC-151 composition scope")]
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
    public void The_hq_interior_retains_only_safe_saves_and_still_tears_down_at_load_and_scene_boundaries()
    {
        // OC-76: only a verified outside player and live, ready interior may retain across saves.
        // Inside, transitions, or any doubt keep player-return teardown; retained saves disable
        // native closet interactions and entry until completion. Load/scene teardown stays exact.
        var source = ReadRuntime("SyndicateHqRuntimeService.cs");

        Assert.Contains("if (CanRetainForSave() && _interior.TrySetSaveInteractions(false))", source, StringComparison.Ordinal);
        Assert.Contains("public bool OnPreLoad() => TeardownAtBoundary();", source, StringComparison.Ordinal);
        Assert.Contains("public bool OnPreSceneChange() => TeardownAtBoundary();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Retained_save_readiness_uses_the_owned_hierarchy_without_a_scene_snapshot()
    {
        var source = ReadRuntime("SyndicateHqInteriorRuntime.cs");
        Assert.Contains("ValidateOwnedRuntime(out _, checkSceneIdentity: false)", source, StringComparison.Ordinal);
        Assert.Contains("if (checkSceneIdentity)", source, StringComparison.Ordinal);
        var service = ReadRuntime("SyndicateHqRuntimeService.cs");
        Assert.Contains("!_interior.IsReadyForSave", service, StringComparison.Ordinal);
        Assert.Contains("_interior.IsReadyForSave && _interior.TrySetSaveInteractions(true)", service, StringComparison.Ordinal);
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
        using var disabled = new CompositionScope();
        var disabledBefore = disabled.Logs.Count;
        LifecycleTables.ForRoot(disabled.Root).LoadCompleteTail!();
        Assert.Equal(disabledBefore, disabled.Logs.Count);

        using var enabled = new CompositionScope(engine => engine.OwnerQaKeysResult = true);
        enabled.Root.CourtesyWorld = new ScriptableCourtesyWorld
        {
            DropsStatus = Release1SmallCourtesyWorldReadStatus.Ready,
            Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-1", "Drop 1", "staged", 0d, 0d, 0d, false) },
            SlotsStatus = Release1SmallCourtesyWorldReadStatus.Ready,
            Slots = new[] { new Release1SmallCourtesySlotSnapshot(0, "product-a", null, 2, false, 1f) },
        };
        var enabledBefore = enabled.Logs.Count;
        LifecycleTables.ForRoot(enabled.Root).LoadCompleteTail!();
        Assert.Contains("[Organized Crime] OC-52 staging proof at load: dead drops read: 1", enabled.Logs.Skip(enabledBefore));
    }

    [Fact]
    public void The_composition_reads_the_timing_threshold_from_a_melon_preference()
    {
        var melon = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));
        Assert.Contains("TimingReceiptThresholdPreference.Read()", melon, StringComparison.Ordinal);

        using var scope = new CompositionScope();
        Assert.Equal(new[] { "read:OwnerQaKeys", "read:TimingThreshold" }, scope.Engine.Trace.Take(2).ToList());

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
