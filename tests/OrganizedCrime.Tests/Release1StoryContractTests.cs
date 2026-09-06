using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryContractTests
{
    [Fact]
    public void Catalog_contains_exactly_the_six_approved_missions_in_order()
    {
        Assert.Equal(new[]
        {
            "release1.small-courtesy",
            "release1.wrong-address",
            "release1.room-with-no-name",
            "release1.short-notice",
            "release1.keep-the-lights-off",
            "release1.the-envelope"
        }, Release1MissionCatalog.All.Select(mission => mission.MissionKey));
        Assert.DoesNotContain(Release1MissionCatalog.All,
            mission => mission.MissionKey == Release1MissionCatalog.IntroScopeKey);
    }

    [Fact]
    public void Accepted_intro_state_starts_at_twenty_with_first_mission_offered()
    {
        var state = Release1StoryState.CreateAccepted(
            "76561190000000001",
            "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1");

        Assert.Equal(20, state.Standing);
        Assert.Equal(Release1StandingBand.Tolerated, state.StandingBand);
        Assert.Equal(Release1RelationshipState.Accepted, state.RelationshipState);
        Assert.False(state.Release1Recognized);
        Assert.Equal(Release1MissionState.Offered, state.Missions[0].State);
        Assert.All(state.Missions.Skip(1),
            mission => Assert.Equal(Release1MissionState.Locked, mission.State));
    }

    [Theory]
    [InlineData(0, "Cold")]
    [InlineData(19, "Cold")]
    [InlineData(20, "Tolerated")]
    [InlineData(39, "Tolerated")]
    [InlineData(40, "Useful")]
    [InlineData(59, "Useful")]
    [InlineData(60, "Trusted")]
    [InlineData(79, "Trusted")]
    [InlineData(80, "RegionalProspect")]
    [InlineData(100, "RegionalProspect")]
    public void Standing_band_is_derived_at_boundaries(int standing, string band)
    {
        var state = TestState.Create(standing: standing);
        Assert.Equal(Enum.Parse<Release1StandingBand>(band), state.StandingBand);
    }

    [Fact]
    public void Mission_and_story_collections_use_structural_value_equality()
    {
        var first = TestState.Create();
        var second = first with
        {
            Missions = first.Missions.ToArray(),
            IntroLogicalCorrelationIds = first.IntroLogicalCorrelationIds.ToArray(),
            RecognitionLogicalCorrelationIds = first.RecognitionLogicalCorrelationIds.ToArray(),
            NativeEffects = first.NativeEffects.ToArray()
        };

        Assert.Equal(first, second);
        Assert.Equal(first.Missions[0], second.Missions[0]);
    }

    private static class TestState
    {
        public static Release1StoryState Create(int standing = 20) =>
            new(
                "76561190000000001", standing, Release1RelationshipState.Accepted,
                false, new[] { "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1" }, Array.Empty<string>(),
                Release1MissionCatalog.All.Select((m, i) =>
                    new Release1MissionRecord(m.MissionKey,
                        i == 0 ? Release1MissionState.Offered : Release1MissionState.Locked,
                        1, null, null, null, Release1MissionOutcome.None, 0,
                        Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false,
                        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                        null, 0)).ToArray(), Array.Empty<Release1NativeEffectJournalEntry>(), 0);
    }
}
