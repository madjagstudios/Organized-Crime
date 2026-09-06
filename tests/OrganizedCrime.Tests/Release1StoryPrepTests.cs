using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.StoryPrep;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryPrepTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Through_small_courtesy_satisfies_small_courtesy_and_leaves_wrong_address_untouched()
    {
        var result = StoryPrepBuilder.Build(PlayerId, Release1MissionCatalog.SmallCourtesy);

        Assert.True(result.Succeeded);
        Assert.False(result.State!.Release1Recognized);
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.SmallCourtesy));
        Assert.NotEqual(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.WrongAddress));
    }

    [Fact]
    public void Through_keep_the_lights_off_satisfies_the_first_five_missions_and_leaves_the_envelope_untouched()
    {
        var result = StoryPrepBuilder.Build(PlayerId, Release1MissionCatalog.KeepTheLightsOff);

        Assert.True(result.Succeeded);
        Assert.False(result.State!.Release1Recognized);
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.SmallCourtesy));
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.WrongAddress));
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.RoomWithNoName));
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.ShortNotice));
        Assert.Equal(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.KeepTheLightsOff));
        Assert.NotEqual(Release1MissionState.Satisfied, Mission(result.State, Release1MissionCatalog.TheEnvelope));
    }

    [Fact]
    public void None_accepts_the_relationship_and_satisfies_no_mission()
    {
        var result = StoryPrepBuilder.Build(PlayerId, "none");

        Assert.True(result.Succeeded);
        Assert.False(result.State!.Release1Recognized);
        Assert.Equal(Release1RelationshipState.Accepted, result.State.RelationshipState);
        Assert.All(result.State.Missions, mission => Assert.NotEqual(Release1MissionState.Satisfied, mission.State));
    }

    [Fact]
    public void The_prepared_story_round_trips_through_the_codec_at_the_current_schema_version()
    {
        var result = StoryPrepBuilder.Build(PlayerId, Release1MissionCatalog.KeepTheLightsOff);
        Assert.True(result.Succeeded);

        Assert.True(Release1StorySaveCodec.TrySerialize(result.State, out var json, out var serializeResult));
        Assert.True(serializeResult.Succeeded);

        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var deserializeResult));
        Assert.True(deserializeResult.Succeeded);
        Assert.Equal(Release1StorySaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.True(envelope.Story!.ValueEquals(result.State));
    }

    [Fact]
    public void An_unknown_mission_key_is_rejected()
    {
        var result = StoryPrepBuilder.Build(PlayerId, "release1.not-a-real-mission");

        Assert.False(result.Succeeded);
        Assert.Equal(StoryPrepBuildStatus.UnknownMissionKey, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void The_envelope_is_rejected_up_front_because_completing_it_would_recognize_the_story()
    {
        var result = StoryPrepBuilder.Build(PlayerId, Release1MissionCatalog.TheEnvelope);

        Assert.False(result.Succeeded);
        Assert.Equal(StoryPrepBuildStatus.MissionRequiresRecognition, result.Status);
        Assert.Null(result.State);
    }

    private static Release1MissionState Mission(Release1StoryState state, string missionKey) =>
        state.Missions[Release1MissionCatalog.IndexOf(missionKey)].State;
}
