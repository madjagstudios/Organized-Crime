using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryIdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("7656119")]
    [InlineData("not-steam")]
    public void Placeholder_or_non_Steam_player_codes_remain_pending(string? playerCode)
    {
        var status = Release1StoryHostIdentity.Resolve(
            @"C:\game\Saves\76561190000000001\SaveGame_test",
            playerCode,
            out _);
        Assert.Equal(Release1StoryHostContextReadStatus.Pending, status);
    }

    [Fact]
    public void Canonical_identity_comes_from_the_validated_save_path_and_requires_matching_code()
    {
        var status = Release1StoryHostIdentity.Resolve(
            @"C:\game\Saves\76561190000000001\SaveGame_test",
            "76561190000000001",
            out var identity);
        Assert.Equal(Release1StoryHostContextReadStatus.Ready, status);
        Assert.Equal("76561190000000001", identity);
    }

    [Fact]
    public void Mismatched_or_ambiguous_save_identity_fails_closed()
    {
        Assert.Equal(
            Release1StoryHostContextReadStatus.AmbiguousIdentity,
            Release1StoryHostIdentity.Resolve(@"C:\game\Saves\76561190000000001\SaveGame_test", "76561197984645370", out _));
        Assert.Equal(
            Release1StoryHostContextReadStatus.AmbiguousIdentity,
            Release1StoryHostIdentity.Resolve(@"C:\game\Saves\not-steam\SaveGame_test", "76561190000000001", out _));
    }
}
