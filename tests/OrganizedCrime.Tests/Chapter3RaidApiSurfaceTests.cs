using S1API.Items;
using S1API.Products;
using S1API.Storages;
using Xunit;

namespace OrganizedCrime.Tests;

// Reflection against the referenced DLL, independent of Atlas's stale installed
// API snapshot. Reads metadata only; never runs native storage or singleton code.
public sealed class Chapter3RaidApiSurfaceTests
{
    [Fact]
    public void Native_lifetime_checks_distinguish_unity_objects_from_managed_and_item_wrappers()
    {
        Assert.True(typeof(UnityEngine.Object).IsAssignableFrom(typeof(Il2CppFishNet.Managing.Server.ServerManager)));
        Assert.True(typeof(UnityEngine.Object).IsAssignableFrom(typeof(Il2CppScheduleOne.Property.Property)));
        Assert.False(typeof(UnityEngine.Object).IsAssignableFrom(typeof(Il2CppScheduleOne.Product.ProductItemInstance)));
        Assert.False(typeof(UnityEngine.Object).IsAssignableFrom(typeof(StorageInstance)));
        Assert.False(typeof(UnityEngine.Object).IsAssignableFrom(typeof(ItemSlotInstance)));
        Assert.False(typeof(UnityEngine.Object).IsAssignableFrom(typeof(PackagingDefinition)));
    }

    [Fact]
    public void Referenced_api_has_read_only_storage_and_product_surface()
    {
        Assert.NotNull(typeof(StorageInstance).GetMethod("FromGameObject", new[] { typeof(UnityEngine.GameObject) }));
        Assert.Equal(typeof(ItemSlotInstance[]), typeof(StorageInstance).GetProperty("Slots")!.PropertyType);
        Assert.Equal(typeof(int), typeof(ItemSlotInstance).GetProperty("Quantity")!.PropertyType);
        Assert.Equal(typeof(ItemInstance), typeof(ItemSlotInstance).GetProperty("ItemInstance")!.PropertyType);
        Assert.Equal(typeof(bool), typeof(ProductInstance).GetProperty("IsPackaged")!.PropertyType);
        Assert.Equal(typeof(PackagingDefinition), typeof(ProductInstance).GetProperty("AppliedPackaging")!.PropertyType);
        Assert.Equal(typeof(int), typeof(PackagingDefinition).GetProperty("Quantity")!.PropertyType);
        Assert.NotNull(typeof(ProductInstance).GetProperty("Quality"));
    }
}
