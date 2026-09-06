using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqStorageCompositionTests
{
    private static readonly string PatchSource = ReadSource("Runtime", "SyndicateHqStorageLoadPatch.cs");
    private static readonly string InteriorSource = ReadSource("Runtime", "SyndicateHqInteriorRuntime.cs");
    private static readonly string BoundarySource = ReadSource("Runtime", "SyndicateHqNativeStorageBoundary.cs");
    private static readonly string ModSource = ReadSource("Mod.cs");

    [Fact]
    public void Load_path_resolver_finds_the_exact_save_root_from_a_nested_native_path()
    {
        var path = @"C:\Users\Player\AppData\LocalLow\TVGS\Schedule I\Saves\76561190000000001\SaveGame_4\Storages";

        Assert.True(SyndicateHqStorageLoadContextAdapter.TryFindSaveRoot(path, out var root));
        Assert.Equal(
            @"C:\Users\Player\AppData\LocalLow\TVGS\Schedule I\Saves\76561190000000001\SaveGame_4",
            root);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Saves\not-a-steam-id\SaveGame_4\Storages")]
    [InlineData(@"C:\Saves\76561190000000001\Wrong_4\Storages")]
    public void Load_path_resolver_rejects_noncanonical_paths(string path)
    {
        Assert.False(SyndicateHqStorageLoadContextAdapter.TryFindSaveRoot(path, out _));
    }

    [Fact]
    public void Placeable_storage_loader_prepares_early_and_global_load_completion_reconciles_once()
    {
        Assert.Contains("typeof(PlaceableStorageEntityLoader)", PatchSource, StringComparison.Ordinal);
        Assert.Contains("nameof(PlaceableStorageEntityLoader.Load)", PatchSource, StringComparison.Ordinal);
        Assert.Contains("new[] { typeof(DynamicSaveData) }", PatchSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new[] { typeof(string) }", PatchSource, StringComparison.Ordinal);
        Assert.Contains("prefix: new HarmonyMethod", PatchSource, StringComparison.Ordinal);
        Assert.DoesNotContain("postfix: new HarmonyMethod", PatchSource, StringComparison.Ordinal);
        Assert.Contains("PrepareAtVanillaLoadBoundary", PatchSource, StringComparison.Ordinal);
        Assert.Contains("CompleteVanillaLoadBoundary", PatchSource, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqStorageLoadPatch.BeginLoadCycle()", ModSource, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqStorageLoadPatch.CompleteAfterVanillaLoad()", ModSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Load(", PatchSource, StringComparison.Ordinal);
        var prefixBody = PatchSource[PatchSource.IndexOf("private static void Prefix", StringComparison.Ordinal)..];
        Assert.DoesNotContain("return false", prefixBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Interior_uses_the_two_complete_native_closets_without_separate_visual_clones()
    {
        Assert.DoesNotContain("Registry.GetItem<BuildableItemDefinition>(SyndicateHqStorageContract.ItemId)", InteriorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildStorageClosetVisuals", InteriorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_Visual", InteriorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldStorageEntity", InteriorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageInstance", InteriorSource, StringComparison.Ordinal);
        Assert.Contains("_storage?.PlaceAtPocket(pocketRoot)", InteriorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_boundary_places_and_verifies_each_closet_at_the_requested_world_transform()
    {
        Assert.Contains("_grid!.transform.SetParent(null, true)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("closet.transform.SetPositionAndRotation(expectedPosition, expectedRotation)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("Vector3.Distance(closet.transform.position, expectedPosition)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("Quaternion.Angle(closet.transform.rotation, expectedRotation)", BoundarySource, StringComparison.Ordinal);
        Assert.Contains("closet.gameObject.activeInHierarchy", BoundarySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_composition_binds_storage_to_the_patch_and_interior_and_resets_it_on_teardown()
    {
        Assert.Contains("new SyndicateHqNativeStorageRuntime", ModSource, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqStorageLoadPatch.Apply", ModSource, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqStorageLoadPatch.Reset", ModSource, StringComparison.Ordinal);
        Assert.Contains("_syndicateHqStorageRuntime", ModSource, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root, "tools", "OrganizedCrime" }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "OrganizedCrime")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
