using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefStorySchemaTests
{
    private const string PlayerId = "76561190000000001";

    private static Release1StoryState BaseStory() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState StoryWithChiefRecord()
    {
        var story = BaseStory();
        var correlation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefPaymentAccepted, "chief-pay-r1").Value;
        var effect = new Release1NativeEffectJournalEntry(
            "chief-campbell-cash-v1-r1", Release1MissionCatalog.ChiefCampbell, 1, "CashTransfer",
            "player-wallet", "chief-campbell", "15000",
            Release1NativeEffectPhase.Prepared, null, story.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: 1);
        var chiefRecord = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
        {
            State = Release1ChiefState.Paying,
            DemandRound = 1,
            AcceptedLogicalCorrelations = new[] { correlation },
            NativeEffectIds = new[] { "chief-campbell-cash-v1-r1" },
            Revision = 2
        };
        return story with { NativeEffects = new[] { effect }, ChiefRecord = chiefRecord, Revision = story.Revision + 1 };
    }

    [Fact]
    public void CurrentSchemaVersion_is_11()
    {
        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);
    }

    [Fact]
    public void A_v10_document_round_trips_with_chief_record_null()
    {
        var story = BaseStory();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var writeResult), writeResult.Message);
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 10;
        ((JsonObject)root["story"]!).Remove("chiefRecord");

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.Null(envelope!.Story!.ChiefRecord);
    }

    [Fact]
    public void A_v11_document_with_a_chief_record_round_trips_intact()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var writeResult), writeResult.Message);

        var ok = Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.True(story.ChiefRecord!.ValueEquals(envelope!.Story!.ChiefRecord));
        Assert.True(story.ValueEquals(envelope.Story));
        var restoredEffect = Assert.Single(envelope.Story!.NativeEffects);
        Assert.Equal("chief-campbell-cash-v1-r1", restoredEffect.EffectId);
        Assert.Equal(Release1MissionCatalog.ChiefCampbell, restoredEffect.MissionKey);
        Assert.Contains(restoredEffect.EffectId, envelope.Story!.ChiefRecord!.NativeEffectIds);
    }

    [Fact]
    public void A_v11_document_missing_chief_record_fails_required_field()
    {
        var story = BaseStory();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        ((JsonObject)root["story"]!).Remove("chiefRecord");

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var result);

        Assert.False(ok);
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, result.Reason);
    }

    [Fact]
    public void A_chief_record_with_an_unknown_field_fails()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        ((JsonObject)root["story"]!["chiefRecord"]!.AsObject())["bogus"] = 1;

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var result);

        Assert.False(ok);
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void An_explicit_json_null_loads_as_null()
    {
        var story = BaseStory();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        ((JsonObject)root["story"]!)["chiefRecord"] = null;

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.Null(envelope!.Story!.ChiefRecord);
    }
}
