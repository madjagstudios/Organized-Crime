using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurResponseFilterTests
{
    [Theory]
    [InlineData("oc_chapter3_arthur", true)]
    [InlineData("weaver", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("OC_CHAPTER3_ARTHUR", false)]
    public void ShouldSuppress_matches_only_arthurs_id(string? npcId, bool expected)
    {
        Assert.Equal(expected, Chapter3ArthurResponseFilter.ShouldSuppress(npcId));
    }

    [Fact]
    public void Impact_poll_fires_once_per_recorded_hit()
    {
        Assert.False(Chapter3ArthurResponseFilter.PollImpact());

        Chapter3ArthurResponseFilter.NoteImpact();
        Assert.True(Chapter3ArthurResponseFilter.PollImpact());
        Assert.False(Chapter3ArthurResponseFilter.PollImpact());

        Chapter3ArthurResponseFilter.NoteImpact();
        Chapter3ArthurResponseFilter.NoteImpact();
        Assert.True(Chapter3ArthurResponseFilter.PollImpact());
        Assert.False(Chapter3ArthurResponseFilter.PollImpact());
    }
}
