using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc179RaidConservativePresenceTests
{
    private static readonly Chapter3RaidBounds[] Large = { new(new(0, 0, 0), new(200, 10, 200)) };
    private static readonly Chapter3RaidPoint Door = new(200, 1, 100);
    private static readonly Chapter3RaidPoint Spawn = new(0, 1, 100);

    private static Chapter3RaidPoint? Anchor(int source) => source switch
    {
        0 => Door, 1 => Spawn, 2 => Large[0].GroundCenter, _ => null
    };
    private static Chapter3RaidPresence Observe(Chapter3RaidPoint player, int source,
        bool current = false, bool inside = false) => Chapter3RaidEntranceGeometry.PropertyPresence(
            player, current, inside, Anchor(source), Large, source < 0 ? null : (Chapter3RaidEntranceSource)source);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Near_real_door_far_from_spawn_never_accumulates_away(int source)
    {
        var player = new Chapter3RaidPoint(220, 1, 100);
        Assert.True(player.Distance(Spawn) > 50);
        Assert.True(player.Distance(Door) < 50);
        var tracker = new Chapter3RaidAwayTracker();
        for (var minute = 0; minute <= 20; minute++)
        {
            var presence = Observe(player, source);
            Assert.Equal(Chapter3RaidPresence.Near, presence);
            var result = tracker.Observe("large", 1080 + minute, presence);
            Assert.False(result.CanRaid);
            Assert.Equal(0, result.AwayMinutes);
        }
    }

    [Theory]
    [InlineData(1, 0.01)] [InlineData(1, 50.01)] [InlineData(1, 64.99)]
    [InlineData(1, 65)]
    [InlineData(2, 0.01)] [InlineData(2, 50.01)] [InlineData(2, 64.99)]
    [InlineData(2, 65)]
    [InlineData(-1, 0.01)] [InlineData(-1, 50.01)] [InlineData(-1, 64.99)]
    [InlineData(-1, 65)]
    public void Every_non_door_source_blocks_within_sixty_five_metres_of_box(int source, double distance) =>
        Assert.Equal(Chapter3RaidPresence.Near, Observe(new(200 + distance, 100, 100), source));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Beyond_sixty_five_metres_requires_ten_continuous_minutes(int source)
    {
        var tracker = new Chapter3RaidAwayTracker();
        for (var minute = 0; minute <= 10; minute++)
        {
            var presence = Observe(new(265.01, 1, 100), source);
            Assert.Equal(Chapter3RaidPresence.Clear, presence);
            var result = tracker.Observe("large", 1080 + minute, presence);
            Assert.Equal(minute, result.AwayMinutes);
            Assert.Equal(minute == 10, result.CanRaid);
        }
        Assert.False(tracker.Observe("large", 1091, Observe(new(210, 1, 100), source)).CanRaid);
        Assert.False(tracker.Observe("large", 1092, Observe(new(265.01, 1, 100), source)).CanRaid);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Property_identity_and_containment_block_every_source(int source)
    {
        Assert.Equal(Chapter3RaidPresence.Inside, Observe(new(400, 1, 100), source, current: true));
        Assert.Equal(Chapter3RaidPresence.Inside, Observe(new(400, 1, 100), source, inside: true));
        Assert.Equal(Chapter3RaidPresence.Inside, Observe(new(100, 1, 100), source));
        Assert.Equal(Chapter3RaidPresence.Unknown, Observe(new(double.NaN, 1, 100), source));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Selected_source_survives_rebind_and_drives_conservative_observation(int source)
    {
        var entrance = Chapter3RaidEntranceGeometry.Bind(source == 0 ? new[] { Door } : Array.Empty<Chapter3RaidPoint>(),
            source == 2 ? null : Spawn, Large)!;
        Assert.Equal((Chapter3RaidEntranceSource)source, entrance.Source);
        var rebound = Chapter3RaidEntranceGeometry.Rebind(null, entrance.Point, Large);
        Assert.Equal(entrance.Point, rebound);
        Assert.Equal(Chapter3RaidPresence.Near, Chapter3RaidEntranceGeometry.PropertyPresence(
            new(220, 1, 100), false, false, rebound, Large, entrance.Source));
        Assert.Equal(Chapter3RaidPresence.Clear, Chapter3RaidEntranceGeometry.PropertyPresence(
            new(265.01, 1, 100), false, false, rebound, Large, entrance.Source));
    }

    [Fact]
    public void Unusable_door_cannot_shrink_conservative_box_envelope()
    {
        foreach (Chapter3RaidPoint? door in new Chapter3RaidPoint?[] { null, new(-500, 0, 0), new(double.NaN, 0, 0) })
            Assert.Equal(Chapter3RaidPresence.Near, Chapter3RaidEntranceGeometry.PropertyPresence(
                new(260, 1, 100), false, false, door, Large, Chapter3RaidEntranceSource.Door));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Missing_or_partly_unreadable_bounds_never_clear(int source)
    {
        foreach (var boxes in new[] { Array.Empty<Chapter3RaidBounds>(), new[] { Large[0], default(Chapter3RaidBounds) } })
        {
            var presence = Chapter3RaidEntranceGeometry.PropertyPresence(new(400, 1, 100), false, false,
                Anchor(source), boxes, source < 0 ? null : (Chapter3RaidEntranceSource)source);
            Assert.Equal(Chapter3RaidPresence.Unknown, presence);
            var tracker = new Chapter3RaidAwayTracker();
            for (var minute = 0; minute <= 10; minute++)
                Assert.False(tracker.Observe("large", 1080 + minute, presence).CanRaid);
        }
    }

    [Theory]
    [InlineData(0.01)] [InlineData(14.99)] [InlineData(15)]
    public void Door_source_blocks_near_far_side_of_property_even_when_door_is_remote(double distance)
    {
        var player = new Chapter3RaidPoint(-distance, 100, 100);
        Assert.True(player.Distance(Door) > 50);
        Assert.Equal(Chapter3RaidPresence.Near, Observe(player, 0));
    }

    [Fact]
    public void Door_source_keeps_fifty_metre_radius_outside_bounds_allowance()
    {
        Assert.Equal(Chapter3RaidPresence.Near, Observe(new(250, 1, 100), 0));
        Assert.Equal(Chapter3RaidPresence.Clear, Observe(new(250.01, 1, 100), 0));
        Assert.Equal(Chapter3RaidPresence.Clear, Observe(new(-15.01, 1, 100), 0));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Non_door_distance_is_horizontal_and_uses_nearest_of_all_boxes(int source)
    {
        var boxes = new[] { Large[0], new Chapter3RaidBounds(new(300, 0, 0), new(320, 10, 20)) };
        Assert.Equal(Chapter3RaidPresence.Near, Chapter3RaidEntranceGeometry.PropertyPresence(
            new(380, 100, 10), false, false, Anchor(source), boxes,
            source < 0 ? null : (Chapter3RaidEntranceSource)source));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)]
    public void Returning_to_near_resets_previously_accumulated_absence(int source)
    {
        var tracker = new Chapter3RaidAwayTracker();
        for (var minute = 0; minute <= 9; minute++)
            Assert.False(tracker.Observe("large", 1080 + minute, Observe(new(300, 1, 100), source)).CanRaid);
        Assert.False(tracker.Observe("large", 1090, Observe(new(210, 1, 100), source)).CanRaid);
        Assert.Equal(0, tracker.Observe("large", 1091, Observe(new(300, 1, 100), source)).AwayMinutes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Interior_lone_door_without_usable_spawn_falls_back_to_bounds(int spawnKind)
    {
        Chapter3RaidPoint? spawn = spawnKind switch { 0 => null, 1 => new(-500, 1, 0), _ => new(double.NaN, 0, 0) };
        var bound = Chapter3RaidEntranceGeometry.Bind(new[] { new Chapter3RaidPoint(100, 1, 100) }, spawn, Large)!;
        Assert.Equal(Chapter3RaidEntranceSource.Bounds, bound.Source);
    }

    [Theory]
    [InlineData(200)] [InlineData(205)]
    public void Boundary_or_exterior_lone_door_without_spawn_remains_door(double x)
    {
        var door = new Chapter3RaidPoint(x, 1, 100);
        var bound = Chapter3RaidEntranceGeometry.Bind(new[] { door }, null, Large)!;
        Assert.Equal(Chapter3RaidEntranceSource.Door, bound.Source);
        Assert.Equal(door, bound.Point);
    }

    [Fact]
    public void Boundary_of_one_box_inside_another_is_not_proof_of_exterior_door()
    {
        var boxes = new[] { Large[0], new Chapter3RaidBounds(new(150, 0, 50), new(250, 10, 150)) };
        Assert.Equal(Chapter3RaidEntranceSource.Bounds, Chapter3RaidEntranceGeometry.Bind(new[] { Door }, null, boxes)!.Source);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Fish_warehouse_authored_threshold_still_binds_same_door(bool withSpawn)
    {
        var boxes = new[] { new Chapter3RaidBounds(
            new(FishWarehousePropertyBoundsDefinition.MinX, FishWarehousePropertyBoundsDefinition.MinY, FishWarehousePropertyBoundsDefinition.MinZ),
            new(FishWarehousePropertyBoundsDefinition.MaxX, FishWarehousePropertyBoundsDefinition.MaxY, FishWarehousePropertyBoundsDefinition.MaxZ)) };
        var door = new Chapter3RaidPoint(-10.96f, FishWarehouseInteriorRoomDefinition.FloorY, FishWarehouseInteriorRoomDefinition.HalfDepth);
        var authored = FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn;
        Chapter3RaidPoint? spawn = withSpawn ? new(authored.X, authored.Y, authored.Z) : null;
        var bound = Chapter3RaidEntranceGeometry.Bind(new[] { door }, spawn, boxes)!;
        Assert.Equal(Chapter3RaidEntranceSource.Door, bound.Source);
        Assert.Equal(door, bound.Point);
    }
}
