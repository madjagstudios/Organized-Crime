using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseInteriorShellDefinitionTests
{
    [Theory]
    [InlineData("Map/Hyland Point/Region_Docks/Fish Warehouse", true)]
    [InlineData("Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse", true)]
    [InlineData("Map/Hyland Point/Region_Docks/Docks Warehouse", false)]
    public void Source_root_matcher_only_accepts_fish_warehouse_paths(string path, bool expected)
    {
        Assert.Equal(expected, FishWarehouseInteriorShellDefinition.IsFishWarehouseSourcePath(path));
    }

    [Theory]
    [InlineData("UnityEngine.MeshRenderer", true, true)]
    [InlineData("UnityEngine.Renderer", true, true)]
    [InlineData("UnityEngine.MeshRenderer", false, false)]
    [InlineData("UnityEngine.SkinnedMeshRenderer", true, false)]
    public void Renderer_filter_accepts_mesh_bearing_renderer_wrappers(
        string componentType,
        bool hasMesh,
        bool expected)
    {
        Assert.Equal(expected, FishWarehouseInteriorShellDefinition.IsRendererCloneCandidate(componentType, hasMesh));
    }

    [Theory]
    [InlineData(3, 2, true)]
    [InlineData(3, 0, false)]
    [InlineData(0, 0, false)]
    public void Material_slots_are_prepareable_when_at_least_one_fallback_exists(
        int slotCount,
        int usableSlotCount,
        bool expected)
    {
        Assert.Equal(expected, FishWarehouseInteriorShellDefinition.CanPrepareMaterialSlots(slotCount, usableSlotCount));
    }

    [Theory]
    [InlineData("_Cull,_Surface", false)]
    [InlineData("_CullMode", false)]
    [InlineData("", true)]
    public void Material_fallback_is_required_when_culling_cannot_be_configured(
        string supportedProperties,
        bool expected)
    {
        string[] properties = string.IsNullOrEmpty(supportedProperties)
            ? Array.Empty<string>()
            : supportedProperties.Split(',');

        Assert.Equal(expected, FishWarehouseInteriorShellDefinition.RequiresMaterialFallback(properties));
    }

}
