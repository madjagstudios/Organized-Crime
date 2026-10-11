using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CheckpointPlannerTests
{
    private static Chapter3CheckpointScoredProperty Owned(string code, int employees, int stations) =>
        new(code, employees, stations);

    private static Chapter3CheckpointSpot Spot(string label, float x, float z) =>
        new(label, x, 0f, z, 0f);

    private static Chapter3CheckpointNativeSite Native(string label, float x, float z) =>
        new(label, x, z);

    [Fact]
    public void Baked_spots_match_the_owner_table()
    {
        var spots = Chapter3CheckpointSpots.Baked;

        Assert.Equal(5, spots.Count);
        Assert.Equal(
            new[] { "outside-bungalow", "outside-storage-unit", "outside-barn", "outside-manor", "outside-hq" },
            spots.Select(spot => spot.Label).ToArray());
        var barn = spots.Single(spot => spot.Label == "outside-barn");
        Assert.Equal(115.56f, barn.X);
        Assert.Equal(1.75f, barn.Y);
        Assert.Equal(10.79f, barn.Z);
        Assert.Equal(88.24f, barn.YawDegrees);
        var hq = spots.Single(spot => spot.Label == "outside-hq");
        Assert.Equal(-38.57f, hq.X);
        Assert.Equal(148.59f, hq.Z);
        Assert.Equal(2.03f, hq.YawDegrees);
    }

    [Fact]
    public void Pick_target_scores_employees_plus_stations()
    {
        var owned = new[]
        {
            Owned("barn", employees: 2, stations: 1),
            Owned("sweatshop", employees: 1, stations: 1),
        };

        Assert.Equal("barn", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: "sweatshop"));
    }

    [Fact]
    public void Pick_target_tie_falls_back_to_the_last_visited_property()
    {
        var owned = new[]
        {
            Owned("barn", employees: 1, stations: 1),
            Owned("sweatshop", employees: 2, stations: 0),
        };

        Assert.Equal("sweatshop", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: "sweatshop"));
    }

    [Fact]
    public void Pick_target_tie_with_an_unowned_last_visited_falls_back_to_first_code()
    {
        var owned = new[]
        {
            Owned("sweatshop", employees: 2, stations: 0),
            Owned("barn", employees: 1, stations: 1),
        };

        Assert.Equal("barn", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: "manor"));
        Assert.Equal("barn", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: null));
    }

    [Fact]
    public void Pick_target_all_zero_falls_back_to_the_last_visited_property()
    {
        var owned = new[]
        {
            Owned("barn", employees: 0, stations: 0),
            Owned("sweatshop", employees: 0, stations: 0),
        };

        Assert.Equal("sweatshop", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: "sweatshop"));
        Assert.Equal("barn", Chapter3CheckpointPlanner.PickTarget(owned, lastVisitedCode: "manor"));
    }

    [Fact]
    public void Pick_target_without_owned_properties_is_null()
    {
        Assert.Null(Chapter3CheckpointPlanner.PickTarget(Array.Empty<Chapter3CheckpointScoredProperty>(), lastVisitedCode: "barn"));
        Assert.Null(Chapter3CheckpointPlanner.PickTarget(new[] { Owned(" ", employees: 3, stations: 3) }, lastVisitedCode: "barn"));
    }

    [Fact]
    public void Active_set_at_watched_is_the_nearest_spot_and_native()
    {
        var spots = new[]
        {
            Spot("outside-barn", 133.89f, 11.08f),
            Spot("outside-sweatshop", -52.90f, 130.57f),
        };
        var natives = new[]
        {
            Native("Docks", 100f, 100f),
            Native("Western", -100f, -100f),
        };

        var set = Chapter3CheckpointPlanner.ActiveSet(
            130f, 10f, LocalPressureTier.Watched, isProtected: false, spots, natives);

        Assert.Equal(new[] { "outside-barn" }, set.OcLabels);
        Assert.Equal("Docks", set.NativeLabel);
    }

    [Fact]
    public void Active_set_at_critical_is_the_3_nearest_spots()
    {
        var spots = new[]
        {
            Spot("d", 40f, 0f),
            Spot("a", 10f, 0f),
            Spot("c", 30f, 0f),
            Spot("b", 20f, 0f),
        };

        var set = Chapter3CheckpointPlanner.ActiveSet(
            0f, 0f, LocalPressureTier.Critical, isProtected: false, spots, Array.Empty<Chapter3CheckpointNativeSite>());

        Assert.Equal(new[] { "a", "b", "c" }, set.OcLabels);
        Assert.Null(set.NativeLabel);
    }

    [Fact]
    public void Active_set_is_empty_while_protected_or_below_watched()
    {
        var spots = new[] { Spot("outside-barn", 133.89f, 11.08f) };
        var natives = new[] { Native("Docks", 100f, 100f) };

        foreach (var tier in new[] { LocalPressureTier.Quiet, LocalPressureTier.Noticed })
        {
            var set = Chapter3CheckpointPlanner.ActiveSet(130f, 10f, tier, isProtected: false, spots, natives);
            Assert.Empty(set.OcLabels);
            Assert.Null(set.NativeLabel);
        }

        foreach (var tier in new[] { LocalPressureTier.Watched, LocalPressureTier.Critical })
        {
            var set = Chapter3CheckpointPlanner.ActiveSet(130f, 10f, tier, isProtected: true, spots, natives);
            Assert.Empty(set.OcLabels);
            Assert.Null(set.NativeLabel);
        }
    }

    [Fact]
    public void Active_set_breaks_distance_ties_by_label()
    {
        var spots = new[]
        {
            Spot("outside-barn", 10f, 0f),
            Spot("outside-manor", -10f, 0f),
        };
        var natives = new[]
        {
            Native("Western", 0f, -10f),
            Native("Docks", 0f, 10f),
        };

        var set = Chapter3CheckpointPlanner.ActiveSet(
            0f, 0f, LocalPressureTier.Watched, isProtected: false, spots, natives);

        Assert.Equal(new[] { "outside-barn" }, set.OcLabels);
        Assert.Equal("Docks", set.NativeLabel);
    }

    [Fact]
    public void Active_set_measures_horizontally()
    {
        var spots = new[]
        {
            new Chapter3CheckpointSpot("high", 5f, 500f, 0f, 0f),
            new Chapter3CheckpointSpot("far", 50f, 0f, 0f, 0f),
        };

        var set = Chapter3CheckpointPlanner.ActiveSet(
            0f, 0f, LocalPressureTier.Watched, isProtected: false, spots, Array.Empty<Chapter3CheckpointNativeSite>());

        Assert.Equal(new[] { "high" }, set.OcLabels);
    }
}
