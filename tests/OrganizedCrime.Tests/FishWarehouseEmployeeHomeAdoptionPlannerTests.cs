using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeHomeAdoptionPlannerTests
{
    private const string HomeA = "00000000-0000-0000-0000-0000000000a1";
    private const string HomeB = "00000000-0000-0000-0000-0000000000b2";
    private const string Stray = "00000000-0000-0000-0000-0000000000ff";

    [Fact]
    public void No_candidates_selects_none()
    {
        Assert.Null(FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { }, new[] { HomeA }));
    }

    [Fact]
    public void Single_candidate_is_adopted()
    {
        Assert.Equal(0, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { HomeA }, new[] { HomeA }));
    }

    [Fact]
    public void One_saved_match_among_many_is_preferred_over_a_stray()
    {
        // A stray dev-placed locker sits first; the real home matches a saved GUID.
        Assert.Equal(1, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { Stray, HomeA }, new[] { HomeA }));
    }

    [Fact]
    public void Multiple_lockers_each_matching_a_saved_home_adopt_one_not_ambiguous()
    {
        // OC-23: two employees, each with their own home locker — both match a saved
        // BedGuid. This previously returned "ambiguous" and failed the whole replay.
        // It must now deterministically adopt one (the first match), never fail.
        var index = FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { HomeA, HomeB }, new[] { HomeA, HomeB });

        Assert.Equal(0, index);
    }

    [Fact]
    public void Match_wins_regardless_of_order()
    {
        Assert.Equal(1, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { HomeB, HomeA, Stray }, new[] { HomeA }));
    }

    [Fact]
    public void No_saved_match_still_adopts_the_first_readable_candidate()
    {
        Assert.Equal(0, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { Stray, HomeA }, new[] { "11111111-1111-1111-1111-111111111111" }));
    }

    [Fact]
    public void No_saved_guids_known_adopts_the_first_readable_candidate()
    {
        Assert.Equal(0, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { HomeA, HomeB }, null));
    }

    [Fact]
    public void Unreadable_guids_are_not_preferred_over_a_saved_match()
    {
        Assert.Equal(2, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { null, "  ", HomeA }, new[] { HomeA }));
    }

    [Fact]
    public void Unreadable_first_candidate_still_yields_a_readable_representative()
    {
        // No saved match; the first candidate's GUID is unreadable, so the first
        // readable one is the representative (never fail, never skip adoption).
        Assert.Equal(1, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { null, Stray }, System.Array.Empty<string>()));
    }

    [Fact]
    public void All_unreadable_guids_still_adopt_the_first_candidate()
    {
        Assert.Equal(0, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { null, "", "  " }, new[] { HomeA }));
    }

    [Fact]
    public void Saved_guid_match_is_case_insensitive()
    {
        Assert.Equal(0, FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(
            new string?[] { HomeA.ToUpperInvariant() }, new[] { HomeA }));
    }
}
