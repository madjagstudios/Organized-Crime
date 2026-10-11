using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePlayerSpawnBoundaryTests
{
    [Fact]
    public void Property_exposes_settable_player_spawn_fields()
    {
        var exterior = typeof(Property).GetProperty(nameof(Property.SpawnPoint));
        var interior = typeof(Property).GetProperty(nameof(Property.InteriorSpawnPoint));

        Assert.NotNull(exterior);
        Assert.NotNull(interior);
        Assert.Equal(typeof(Transform), exterior!.PropertyType);
        Assert.Equal(typeof(Transform), interior!.PropertyType);
        Assert.True(exterior.CanWrite);
        Assert.True(interior.CanWrite);
    }

    [Fact]
    public void Player_exposes_the_last_visited_property_and_the_free_call()
    {
        var lastVisited = typeof(Player).GetProperty(nameof(Player.LastVisitedProperty));
        var free = typeof(Player).GetMethod(nameof(Player.Free_Server), Type.EmptyTypes);

        Assert.NotNull(lastVisited);
        Assert.Equal(typeof(Property), lastVisited!.PropertyType);
        Assert.NotNull(free);
        Assert.Equal(typeof(void), free!.ReturnType);
    }
}
