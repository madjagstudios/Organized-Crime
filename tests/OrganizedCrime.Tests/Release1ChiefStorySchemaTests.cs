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
            "player-wallet", "chief-campbell", "25000",
            Release1NativeEffectPhase.Prepared, null, story.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: 1);
        var chiefRecord = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
        {
            State = Release1ChiefState.Paying,
            DemandRound = 1,
            AcceptedLogicalCorrelations = new[] { correlation },
            NativeEffectIds = new[] { "chief-campbell-cash-v1-r1" },
            Revision = 2,
            LastLateFeeChargedDay = 6,
            LateFeesCharged = 2
        };
        return story with { NativeEffects = new[] { effect }, ChiefRecord = chiefRecord, Revision = story.Revision + 1 };
    }

    [Fact]
    public void CurrentSchemaVersion_is_14()
    {
        Assert.Equal(14, Release1StorySaveCodec.CurrentSchemaVersion);
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
    public void A_v12_document_with_a_chief_record_round_trips_intact()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var writeResult), writeResult.Message);

        var ok = Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.True(story.ChiefRecord!.ValueEquals(envelope!.Story!.ChiefRecord));
        Assert.True(story.ValueEquals(envelope.Story));
        Assert.Equal(6, envelope.Story!.ChiefRecord!.LastLateFeeChargedDay);
        Assert.Equal(2, envelope.Story!.ChiefRecord!.LateFeesCharged);
        var restoredEffect = Assert.Single(envelope.Story!.NativeEffects);
        Assert.Equal("chief-campbell-cash-v1-r1", restoredEffect.EffectId);
        Assert.Equal(Release1MissionCatalog.ChiefCampbell, restoredEffect.MissionKey);
        Assert.Contains(restoredEffect.EffectId, envelope.Story!.ChiefRecord!.NativeEffectIds);
    }

    [Fact]
    public void A_v12_document_missing_chief_record_fails_required_field()
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

    [Fact]
    public void A_v11_document_without_late_fee_fields_migrates_them_to_defaults()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 11;
        var chiefRecord = ((JsonObject)root["story"]!["chiefRecord"]!.AsObject());
        chiefRecord.Remove("lastLateFeeChargedDay");
        chiefRecord.Remove("lateFeesCharged");

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.Null(envelope!.Story!.ChiefRecord!.LastLateFeeChargedDay);
        Assert.Equal(0, envelope!.Story!.ChiefRecord!.LateFeesCharged);
        Assert.Equal(Release1ChiefState.Paying, envelope!.Story!.ChiefRecord!.State);
        Assert.Equal(1, envelope!.Story!.ChiefRecord!.DemandRound);
    }

    [Fact]
    public void A_v12_document_without_protection_fields_loads_unprotected_and_unoffered()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 12;
        var chiefRecord = ((JsonObject)root["story"]!["chiefRecord"]!.AsObject());
        chiefRecord.Remove("protectedUntilDay");
        chiefRecord.Remove("protectionOffered");
        chiefRecord.Remove("protectionWarnedForUntilDay");
        chiefRecord.Remove("protectionLapseNotices");
        chiefRecord.Remove("protectionSettleFirstRefusals");
        chiefRecord.Remove("protectionPurchases");
        chiefRecord.Remove("protectionBlockedNotices");
        chiefRecord.Remove("protectionCoverStartCheckpoints");

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        var restored = envelope!.Story!.ChiefRecord!;
        Assert.Null(restored.ProtectedUntilDay);
        Assert.False(restored.ProtectionOffered);
        Assert.Null(restored.ProtectionWarnedForUntilDay);
        Assert.Equal(0, restored.ProtectionLapseNotices);
        Assert.Equal(0, restored.ProtectionSettleFirstRefusals);
        Assert.Equal(0, restored.ProtectionPurchases);
        Assert.Equal(0, restored.ProtectionBlockedNotices);
        Assert.Null(restored.ProtectionCoverStartCheckpoints);
        Assert.Equal(Release1ChiefState.Paying, restored.State);
    }

    [Fact]
    public void A_v13_document_with_protection_round_trips_intact()
    {
        var chief = StoryWithChiefRecord().ChiefRecord! with
        {
            ProtectedUntilDay = 21,
            ProtectionOffered = true,
            ProtectionWarnedForUntilDay = 21,
            ProtectionLapseNotices = 1,
            ProtectionSettleFirstRefusals = 2,
            ProtectionPurchases = 1,
            ProtectionBlockedNotices = 1,
            ProtectionCoverStartCheckpoints = 5
        };
        var story = StoryWithChiefRecord() with { ChiefRecord = chief };
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var writeResult), writeResult.Message);

        var ok = Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.True(story.ChiefRecord!.ValueEquals(envelope!.Story!.ChiefRecord));
    }

    [Fact]
    public void A_v13_document_without_forced_wanted_fields_loads_unwarned_and_unfired()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 13;
        var chiefRecord = ((JsonObject)root["story"]!["chiefRecord"]!.AsObject());
        chiefRecord.Remove("lockdownEngagedAtMinutes");
        chiefRecord.Remove("forcedWantedWarnings");
        chiefRecord.Remove("lastForcedWantedNight");
        chiefRecord.Remove("forcedWantedFires");
        chiefRecord.Remove("forcedWantedDispatches");
        chiefRecord.Remove("forcedWantedDispatchRefusals");

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        var restored = envelope!.Story!.ChiefRecord!;
        Assert.Null(restored.LockdownEngagedAtMinutes);
        Assert.Equal(0, restored.ForcedWantedWarnings);
        Assert.Null(restored.LastForcedWantedNight);
        Assert.Equal(0, restored.ForcedWantedFires);
        Assert.Equal(0, restored.ForcedWantedDispatches);
        Assert.Equal(0, restored.ForcedWantedDispatchRefusals);
        Assert.Equal(Release1ChiefState.Paying, restored.State);
    }

    [Fact]
    public void A_v14_document_with_forced_wanted_round_trips_intact()
    {
        var chief = StoryWithChiefRecord().ChiefRecord! with
        {
            LockdownEngagedAtMinutes = 500d,
            ForcedWantedWarnings = 1,
            LastForcedWantedNight = 1,
            ForcedWantedFires = 2,
            ForcedWantedDispatches = 1,
            ForcedWantedDispatchRefusals = 1
        };
        var story = StoryWithChiefRecord() with { ChiefRecord = chief };
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var writeResult), writeResult.Message);

        var ok = Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.True(story.ChiefRecord!.ValueEquals(envelope!.Story!.ChiefRecord));
    }

    [Fact]
    public void A_v1_document_loads_without_a_chief_record()
    {
        var story = StoryWithChiefRecord();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 1;
        ((JsonObject)root["story"]!).Remove("chiefRecord");
        ((JsonObject)root["story"]!)["nativeEffects"] = new JsonArray();

        var ok = Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var readResult);

        Assert.True(ok, readResult.Message);
        Assert.Null(envelope!.Story!.ChiefRecord);
    }
}
