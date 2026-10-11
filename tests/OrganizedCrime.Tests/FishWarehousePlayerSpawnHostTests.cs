using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePlayerSpawnHostTests
{
    [Fact]
    public void Attach_assigns_both_spawn_fields_before_it_reports_ready()
    {
        // RuntimePropertyHost runs this attach in its player-spawns stage, ahead of
        // property-registration, so a true return means both fields are assigned
        // before the property registers or spawns.
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget();
        var log = new List<string>();

        var attached = host.TryAttach(target, log.Add);

        Assert.True(attached);
        Assert.True(host.IsReady);
        Assert.Equal(
            new[]
            {
                "create:OC_FishWarehouse_PlayerExteriorSpawn",
                "create:OC_FishWarehouse_PlayerInteriorSpawn",
                "set-exterior:OC_FishWarehouse_PlayerExteriorSpawn",
                "set-interior:OC_FishWarehouse_PlayerInteriorSpawn",
                "ground:OC_FishWarehouse_PlayerExteriorSpawn",
                "retained"
            },
            target.Calls);
        Assert.Same(target.Created[0], target.Exterior);
        Assert.Same(target.Created[1], target.Interior);
        Assert.Same(target.Exterior, host.CreatedExterior);
        Assert.Same(target.Interior, host.CreatedInterior);
        Assert.True(host.CreatedSpawnsRetained());
        Assert.Empty(log);
    }

    [Fact]
    public void Attach_places_the_definition_points_with_their_yaws()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget();

        Assert.True(host.TryAttach(target));

        Assert.Equal(2, target.Created.Count);
        var exterior = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;
        var interior = FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn;
        Assert.Equal((exterior.Name, exterior.X, exterior.Y, exterior.Z, exterior.YawDegrees),
            (target.Created[0].Name, target.Created[0].X, target.Created[0].Y, target.Created[0].Z, target.Created[0].YawDegrees));
        Assert.Equal((interior.Name, interior.X, interior.Y, interior.Z, interior.YawDegrees),
            (target.Created[1].Name, target.Created[1].X, target.Created[1].Y, target.Created[1].Z, target.Created[1].YawDegrees));
        Assert.Equal(1, target.GroundCalls);
    }

    [Fact]
    public void Missed_ground_keeps_the_height_warns_once_and_still_attaches()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget { Grounded = false };
        var log = new List<string>();

        var attached = host.TryAttach(target, log.Add);

        Assert.True(attached);
        Assert.True(host.IsReady);
        var warning = Assert.Single(log);
        Assert.Contains("no ground", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failed_read_back_fails_the_stage_and_tears_down()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget { Retain = false };
        var log = new List<string>();

        var attached = host.TryAttach(target, log.Add);

        Assert.False(attached);
        Assert.False(host.IsReady);
        Assert.True(target.TornDown);
        Assert.Null(host.CreatedExterior);
        Assert.Null(host.CreatedInterior);
        var warning = Assert.Single(log);
        Assert.Contains("failed safely", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Target_throw_fails_safely_and_tears_down()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget { ThrowOnCreate = true };
        var log = new List<string>();

        var attached = host.TryAttach(target, log.Add);

        Assert.False(attached);
        Assert.False(host.IsReady);
        Assert.True(target.TornDown);
        Assert.Single(log);
    }

    [Fact]
    public void Null_target_refuses_without_throwing()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();

        Assert.False(host.TryAttach(null));
        Assert.False(host.IsReady);
    }

    [Fact]
    public void Second_attach_is_free_and_keeps_the_first_points()
    {
        using var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget();

        Assert.True(host.TryAttach(target));
        Assert.True(host.TryAttach(target));

        Assert.Equal(2, target.Created.Count);
        Assert.False(target.TornDown);
    }

    [Fact]
    public void Dispose_tears_down_and_releases_both_spawns()
    {
        var host = new FishWarehousePlayerSpawnHost<FakeSpawnPoint>();
        var target = new FakeSpawnTarget();

        Assert.True(host.TryAttach(target));
        host.Dispose();

        Assert.True(target.TornDown);
        Assert.False(host.IsReady);
        Assert.Null(host.CreatedExterior);
        Assert.Null(host.CreatedInterior);
        Assert.False(host.CreatedSpawnsRetained());

        host.Dispose();
        Assert.False(host.IsReady);
    }

    private sealed record FakeSpawnPoint(string Name, float X, float Y, float Z, float YawDegrees);

    private sealed class FakeSpawnTarget : IFishWarehousePlayerSpawnTarget<FakeSpawnPoint>
    {
        public List<FakeSpawnPoint> Created { get; } = new();
        public List<string> Calls { get; } = new();
        public FakeSpawnPoint? Exterior { get; private set; }
        public FakeSpawnPoint? Interior { get; private set; }
        public bool Grounded { get; set; } = true;
        public bool Retain { get; set; } = true;
        public bool ThrowOnCreate { get; set; }
        public bool TornDown { get; private set; }
        public int GroundCalls { get; private set; }

        public FakeSpawnPoint CreateSpawnPoint(string name, float x, float y, float z, float yawDegrees)
        {
            if (ThrowOnCreate)
                throw new InvalidOperationException("Fake spawn creation failed.");
            Calls.Add($"create:{name}");
            var point = new FakeSpawnPoint(name, x, y, z, yawDegrees);
            Created.Add(point);
            return point;
        }

        public bool TryGroundExterior(FakeSpawnPoint point)
        {
            GroundCalls++;
            Calls.Add($"ground:{point.Name}");
            return Grounded;
        }

        public void SetExteriorSpawn(FakeSpawnPoint spawn)
        {
            Calls.Add($"set-exterior:{spawn.Name}");
            Exterior = spawn;
        }

        public void SetInteriorSpawn(FakeSpawnPoint spawn)
        {
            Calls.Add($"set-interior:{spawn.Name}");
            Interior = spawn;
        }

        public bool SpawnsRetained(FakeSpawnPoint exterior, FakeSpawnPoint interior)
        {
            Calls.Add("retained");
            return Retain && ReferenceEquals(Exterior, exterior) && ReferenceEquals(Interior, interior);
        }

        public void Teardown()
        {
            Calls.Add("teardown");
            TornDown = true;
        }
    }
}
