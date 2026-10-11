using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc179RaidEntranceTests
{
    private static readonly Chapter3RaidBounds[] Boxes = { new(new(100, 0, 100), new(120, 10, 120)) };
    private static readonly Chapter3RaidPoint Door = new(101, 1, 98);
    private static readonly Chapter3RaidPoint Spawn = new(102, 1, 98);

    [Theory]
    [InlineData("rv")] [InlineData("dockswarehouse")] [InlineData("motelroom")]
    [InlineData("seweroffice")] [InlineData("sweatshop")] [InlineData("tacoticklers")]
    [InlineData("oc_fishwarehouse")]
    public void Every_site_can_bind_without_a_native_door_blocker(string code)
    {
        // Codes are tracker identities; geometry policy has no native whitelist.
        var entrance = Chapter3RaidEntranceGeometry.Bind(Array.Empty<Chapter3RaidPoint>(), Spawn, Boxes);
        Assert.Equal(Chapter3RaidEntranceSource.Spawn, entrance!.Source);
        var tracker = new Chapter3RaidAwayTracker();
        for (var i = 0; i <= 10; i++)
        {
            var presence = Chapter3RaidEntranceGeometry.Presence(new(200, 1, 200), false, false, entrance.Point, Boxes);
            Assert.Equal(Chapter3RaidPresence.Clear, presence);
            Assert.Equal(i == 10, tracker.Observe(code, 1080 + i, presence).CanRaid);
        }
    }

    [Theory]
    [InlineData("door", (int)Chapter3RaidEntranceSource.Door)]
    [InlineData("no-door", (int)Chapter3RaidEntranceSource.Spawn)]
    [InlineData("tied-doors", (int)Chapter3RaidEntranceSource.Spawn)]
    [InlineData("remote-door", (int)Chapter3RaidEntranceSource.Spawn)]
    [InlineData("bad-door", (int)Chapter3RaidEntranceSource.Spawn)]
    [InlineData("no-spawn", (int)Chapter3RaidEntranceSource.Bounds)]
    [InlineData("remote-spawn", (int)Chapter3RaidEntranceSource.Bounds)]
    [InlineData("bad-spawn", (int)Chapter3RaidEntranceSource.Bounds)]
    public void Fallback_chain_rejects_unbounded_candidates(string scenario, int expectedSource)
    {
        var expected = (Chapter3RaidEntranceSource)expectedSource;
        var doors = scenario switch
        {
            "door" => new[] { Door },
            "tied-doors" => new[] { new Chapter3RaidPoint(101, 1, 98), new Chapter3RaidPoint(103, 1, 98) },
            "remote-door" => new[] { new Chapter3RaidPoint(0, 0, 0) },
            "bad-door" => new[] { new Chapter3RaidPoint(double.NaN, 0, 0) },
            _ => Array.Empty<Chapter3RaidPoint>()
        };
        Chapter3RaidPoint? spawn = scenario switch
        {
            "no-spawn" => null, "remote-spawn" => new(0, 0, 0),
            "bad-spawn" => new(double.PositiveInfinity, 0, 0), _ => Spawn
        };
        var entrance = Chapter3RaidEntranceGeometry.Bind(doors, spawn, Boxes);
        Assert.Equal(expected, entrance!.Source);
        Assert.True(Chapter3RaidEntranceGeometry.Usable(entrance.Point, Boxes));
        if (expected == Chapter3RaidEntranceSource.Bounds) Assert.Equal(new(110, 0, 110), entrance.Point);
        Assert.Equal(Chapter3RaidPresence.Inside, Chapter3RaidEntranceGeometry.Presence(new(200, 1, 200), true, false, entrance.Point, Boxes));
        Assert.Equal(Chapter3RaidPresence.Inside, Chapter3RaidEntranceGeometry.Presence(new(110, 1, 110), false, true, entrance.Point, Boxes));
        Assert.Equal(Chapter3RaidPresence.Near, Chapter3RaidEntranceGeometry.Presence(new(entrance.Point.X, 1, 70), false, false, entrance.Point, Boxes));
        Assert.Equal(Chapter3RaidPresence.Clear, Chapter3RaidEntranceGeometry.Presence(new(200, 1, 200), false, false, entrance.Point, Boxes));
    }

    [Fact]
    public void Door_does_not_require_spawn_and_distant_spawn_does_not_discard_door()
    {
        Assert.Equal(Chapter3RaidEntranceSource.Door, Chapter3RaidEntranceGeometry.Bind(new[] { Door }, null, Boxes)!.Source);
        Assert.Equal(Chapter3RaidEntranceSource.Door, Chapter3RaidEntranceGeometry.Bind(new[] { Door }, new(0, 0, 0), Boxes)!.Source);
    }

    [Theory]
    [InlineData(50, (int)Chapter3RaidPresence.Near)] [InlineData(50.01, (int)Chapter3RaidPresence.Clear)]
    public void Entrance_distance_includes_exact_fifty_metres(double distance, int expectedPresence) =>
        Assert.Equal((Chapter3RaidPresence)expectedPresence, Chapter3RaidEntranceGeometry.Presence(new(Door.X + distance, Door.Y, Door.Z), false, false, Door, Boxes));

    [Theory]
    [InlineData(50, (int)Chapter3RaidPresence.Near)] [InlineData(50.01, (int)Chapter3RaidPresence.Clear)]
    public void No_entrance_uses_conservative_bounds_distance(double distance, int expectedPresence) =>
        Assert.Equal((Chapter3RaidPresence)expectedPresence, Chapter3RaidEntranceGeometry.Presence(new(120 + distance, 100, 110), false, false, null, Boxes));

    [Fact]
    public void Missing_geometry_blocks_outdoors_but_positive_current_property_still_means_inside()
    {
        var empty = Array.Empty<Chapter3RaidBounds>();
        Assert.Null(Chapter3RaidEntranceGeometry.Bind(new[] { Door }, Spawn, empty));
        Assert.Equal(Chapter3RaidPresence.Unknown, Chapter3RaidEntranceGeometry.Presence(Spawn, false, false, null, empty));
        Assert.Equal(Chapter3RaidPresence.Inside, Chapter3RaidEntranceGeometry.Presence(Spawn, true, false, null, empty));
        Assert.Equal(Chapter3RaidPresence.Unknown, Chapter3RaidEntranceGeometry.Presence(new(double.NaN, 0, 0), true, false, null, Boxes));
    }

    [Theory]
    [InlineData(0)] [InlineData(double.NaN)] [InlineData(1001)]
    public void Empty_nonfinite_and_unbounded_boxes_are_not_geometry(double width)
    {
        var boxes = new[] { new Chapter3RaidBounds(new(0, 0, 0), new(width, 10, 10)) };
        Assert.Null(Chapter3RaidEntranceGeometry.Bind(Array.Empty<Chapter3RaidPoint>(), null, boxes));
    }

    [Fact]
    public void Cached_local_offset_drift_uses_world_fallback_and_invalid_fallback_rebinds()
    {
        Assert.Equal(Door, Chapter3RaidEntranceGeometry.Rebind(new(0, 0, 0), Door, Boxes));
        Assert.Equal(Door, Chapter3RaidEntranceGeometry.Rebind(new(110, 1, 110), Door, Boxes));
        Assert.Equal(Door, Chapter3RaidEntranceGeometry.Rebind(null, Door, Boxes));
        Assert.Equal(new(102, 1, 98), Chapter3RaidEntranceGeometry.Rebind(new(102, 1, 98), Door, Boxes));
        Assert.Null(Chapter3RaidEntranceGeometry.Rebind(null, new(0, 0, 0), Boxes));
    }
    [Fact]
    public void Bounds_center_binding_uses_distance_to_perimeter_instead_of_center()
    {
        var boxes = new[] { new Chapter3RaidBounds(new(0, 0, 0), new(200, 10, 200)) };
        var entrance = Chapter3RaidEntranceGeometry.Bind(Array.Empty<Chapter3RaidPoint>(), null, boxes)!;
        Assert.Equal(Chapter3RaidEntranceSource.Bounds, entrance.Source);
        Assert.Equal(Chapter3RaidPresence.Near, Chapter3RaidEntranceGeometry.Presence(new(250, 1, 100), false, false, entrance.Point, boxes, boundsFallback: true));
        Assert.Equal(Chapter3RaidPresence.Clear, Chapter3RaidEntranceGeometry.Presence(new(250.01, 1, 100), false, false, entrance.Point, boxes, boundsFallback: true));
    }
    [Fact]
    public void Authored_disabled_box_corners_recover_ground_bounds_and_inside_occupancy()
    {
        var corners = new List<Chapter3RaidPoint>();
        foreach (var x in new[] { 100, 120 }) foreach (var y in new[] { 0, 10 }) foreach (var z in new[] { 100, 120 })
            corners.Add(new(x, y, z));
        Assert.Equal(Boxes[0], Chapter3RaidEntranceGeometry.FromCorners(corners));
        Assert.Equal(Chapter3RaidPresence.Inside, Chapter3RaidEntranceGeometry.Presence(new(110, 1, 110), false, false, null, Boxes));
        corners[0] = new(double.NaN, 0, 0);
        Assert.Null(Chapter3RaidEntranceGeometry.FromCorners(corners));
        Assert.Null(Chapter3RaidEntranceGeometry.FromCorners(corners.Skip(1).ToArray()));
    }
}
