using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePlayerSpawnDefinitionTests
{
    [Fact]
    public void Runtime_attachment_contract_accepts_the_spawn_layout()
    {
        Assert.True(FishWarehousePlayerSpawnDefinition.HasValidLayout());
    }

    [Fact]
    public void Spawn_points_have_stable_names()
    {
        Assert.Equal("OC_FishWarehouse_PlayerExteriorSpawn", FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn.Name);
        Assert.Equal("OC_FishWarehouse_PlayerInteriorSpawn", FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn.Name);
    }

    [Fact]
    public void Interior_spawn_is_inside_the_room_a_metre_from_the_employee_spawn()
    {
        var spawn = FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn;

        Assert.Equal(-8.4f, spawn.X);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, spawn.Y);
        Assert.Equal(4.8f, spawn.Z);
        Assert.True(FishWarehousePlayerSpawnDefinition.IsInsideRoom(spawn));

        var dx = spawn.X - FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.X;
        var dz = spawn.Z - FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint.Z;
        Assert.Equal(1f, MathF.Sqrt(dx * dx + dz * dz), precision: 5);
    }

    [Fact]
    public void Exterior_spawn_is_outside_the_room_between_the_building_and_the_idle_block()
    {
        var spawn = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;

        Assert.Equal(1.2f, spawn.X);
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, spawn.Y);
        Assert.Equal(9.9f, spawn.Z);
        Assert.False(FishWarehousePlayerSpawnDefinition.IsInsideRoom(spawn));
        Assert.True(spawn.Z > FishWarehouseInteriorRoomDefinition.HalfDepth);
        Assert.True(spawn.Z < FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Min(point => point.Z));
    }

    [Fact]
    public void Exterior_spawn_is_outside_the_gameplay_bounds_like_the_idle_block()
    {
        // OC-139 report: the prompt's (1.2, 0, 9.9) sits past MaxZ 6.9. Kept as
        // specified; the owner moves it live with `oc_ch3 warehouse spawn here`.
        var spawn = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;

        Assert.False(FishWarehousePropertyBoundsDefinition.Contains(spawn.X, spawn.Y, spawn.Z));
        var idle = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints[0];
        Assert.False(FishWarehousePropertyBoundsDefinition.Contains(idle.X, 0f, idle.Z));
    }

    [Fact]
    public void Exterior_spawn_clears_every_idle_point_by_eight_decimetres()
    {
        var spawn = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;

        foreach (var idle in FishWarehouseEmployeeInfrastructureDefinition.IdlePoints)
        {
            var dx = spawn.X - idle.X;
            var dz = spawn.Z - idle.Z;
            Assert.True(
                MathF.Sqrt(dx * dx + dz * dz) >= FishWarehousePlayerSpawnDefinition.ExteriorIdleClearance,
                $"idle {idle.Name} is too close to the exterior spawn");
        }

        Assert.True(FishWarehousePlayerSpawnDefinition.ExteriorClearsIdleBlock());
    }

    [Fact]
    public void Spawn_yaws_face_away_from_the_building_and_into_the_room()
    {
        // Exterior yaw 0 faces +z: the idle block sits at larger +z than the room
        // edge, so +z is away from the building. Interior yaw 90 faces +x off
        // the west wall, down the long axis of the room.
        Assert.Equal(0f, FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn.YawDegrees);
        Assert.Equal(90f, FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn.YawDegrees);
    }
}
