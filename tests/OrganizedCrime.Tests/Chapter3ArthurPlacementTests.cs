using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurPlacementTests
{
    [Fact]
    public void Come_prefers_behind_and_in_band()
    {
        var candidates = new[]
        {
            Candidate(25f, behind: false),
            Candidate(10f, behind: true),
            Candidate(25f, behind: true),
            Candidate(40f, behind: true),
        };

        var pick = Chapter3ArthurPlacement.SelectComeCandidate(candidates);

        Assert.NotNull(pick);
        Assert.Equal(25f, pick!.DistanceToPlayer);
        Assert.True(pick.BehindPlayer);
    }

    [Fact]
    public void Come_prefers_band_middle_when_several_behind()
    {
        var candidates = new[]
        {
            Candidate(21f, behind: true),
            Candidate(29f, behind: true),
            Candidate(25f, behind: true),
        };

        var pick = Chapter3ArthurPlacement.SelectComeCandidate(candidates);

        Assert.NotNull(pick);
        Assert.Equal(25f, pick!.DistanceToPlayer);
    }

    [Fact]
    public void Come_falls_back_outside_the_band_when_nothing_in_band_is_reachable()
    {
        var candidates = new[]
        {
            new Chapter3ArthurCandidate((0f, 0f, 0f), 25f, true, false),
            Candidate(40f, behind: false),
        };

        var pick = Chapter3ArthurPlacement.SelectComeCandidate(candidates);

        Assert.NotNull(pick);
        Assert.Equal(40f, pick!.DistanceToPlayer);
    }

    [Fact]
    public void Come_returns_null_when_nothing_is_reachable()
    {
        var candidates = new[]
        {
            new Chapter3ArthurCandidate((0f, 0f, 0f), 25f, true, false),
        };

        Assert.Null(Chapter3ArthurPlacement.SelectComeCandidate(candidates));
        Assert.Null(Chapter3ArthurPlacement.SelectComeCandidate(Array.Empty<Chapter3ArthurCandidate>()));
    }

    [Fact]
    public void Leave_prefers_behind_and_closest_to_thirty_metres()
    {
        var candidates = new[]
        {
            Candidate(30f, behind: false),
            Candidate(20f, behind: true),
            Candidate(35f, behind: true),
        };

        var pick = Chapter3ArthurPlacement.SelectLeaveCandidate(candidates);

        Assert.NotNull(pick);
        Assert.Equal(35f, pick!.DistanceToPlayer);
        Assert.True(pick.BehindPlayer);
    }

    [Fact]
    public void Leave_returns_null_when_nothing_is_reachable()
    {
        Assert.Null(Chapter3ArthurPlacement.SelectLeaveCandidate(Array.Empty<Chapter3ArthurCandidate>()));
    }

    private static Chapter3ArthurCandidate Candidate(float distance, bool behind) =>
        new((distance, 0f, 0f), distance, behind, true);
}
