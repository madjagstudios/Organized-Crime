using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryPersistenceTests
{
    [Fact]
    public void Sidecar_path_is_separate_and_exact()
    {
        using var save = TemporarySaveFolder.Create();
        Assert.True(Release1StorySavePath.TryCreate(save.Path, out var path, out var result), result.Message);
        Assert.Equal(Path.Combine(save.Path, "OrganizedCrime", "release1-story.json"), path!.SidecarFilePath);
    }

    [Fact]
    public void Codec_round_trips_every_recovery_and_journal_field_deterministically()
    {
        var envelope = new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, Story());
        Assert.True(Release1StorySaveCodec.TrySerialize(envelope, out var first, out var firstResult), firstResult.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(first, out var decoded, out var decodeResult), decodeResult.Message);
        Assert.True(Release1StorySaveCodec.TrySerialize(decoded!, out var second, out var secondResult), secondResult.Message);
        Assert.Equal(first, second);
        Assert.Equal(envelope, decoded);
    }

    [Fact]
    public void Store_rejects_stale_and_equal_conflicts_but_accepts_equal_identical()
    {
        using var save = TemporarySaveFolder.Create();
        Assert.True(Release1StorySavePath.TryCreate(save.Path, out var path, out _));
        var store = new Release1StoryStateStore(path!);
        var revisionTwo = Story() with { Revision = 2 };
        Assert.True(store.TryUpdate(revisionTwo, out var updated));
        Assert.Equal(Release1StoryStoreUpdateStatus.Updated, updated.Status);
        Assert.True(store.TryUpdate(revisionTwo, out var idempotent));
        Assert.Equal(Release1StoryStoreUpdateStatus.Idempotent, idempotent.Status);
        Assert.False(store.TryUpdate(Story() with { Revision = 1 }, out var stale));
        Assert.Equal(Release1StoryStoreFailureReason.StaleRevision, stale.FailureReason);
        Assert.False(store.TryUpdate(revisionTwo with { Standing = 99 }, out var conflict));
        Assert.Equal(Release1StoryStoreFailureReason.RevisionConflict, conflict.FailureReason);
    }

    [Fact]
    public void Schema_one_without_phone_attempts_reads_as_current_schema_with_an_empty_attempt_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = 1;
        root["story"]!.AsObject().Remove("phonePresentationAttempts");
        root["story"]!.AsObject().Remove("smallCourtesyAssignments");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.PhonePresentationAttempts);
        Assert.Empty(envelope.Story.SmallCourtesyAssignments);
    }

    [Fact]
    public void Schema_two_without_assignments_reads_as_current_schema_with_an_empty_assignment_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = 2;
        root["story"]!.AsObject().Remove("smallCourtesyAssignments");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.SmallCourtesyAssignments);
    }

    [Fact]
    public void Schema_three_without_presentation_receipts_reads_as_current_schema_with_an_empty_receipt_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = 3;
        root["story"]!.AsObject().Remove("presentationReceipts");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.PresentationReceipts);
    }

    [Fact]
    public void Schema_four_without_wrong_address_reads_as_current_schema_with_empty_collections()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = 4;
        root["story"]!.AsObject().Remove("wrongAddressAssignments");
        root["story"]!.AsObject().Remove("wrongAddressProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.WrongAddressAssignments);
        Assert.Empty(envelope.Story.WrongAddressProgress);
    }

    [Fact]
    public void Current_serialization_writes_schema_five_and_round_trips_wrong_address_arrays_ordered_by_attempt()
    {
        var story = StoryWithWrongAddress(WrongAddressAssignment(2, Release1TransitionKind.MakeGoodAccepted), WrongAddressAssignment(1, Release1TransitionKind.MissionAccepted));
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var encoded), encoded.Message);
        using var document = JsonDocument.Parse(json);
        var assignments = document.RootElement.GetProperty("story").GetProperty("wrongAddressAssignments");
        Assert.Equal(1, assignments[0].GetProperty("attempt").GetInt32());
        Assert.Equal(2, assignments[1].GetProperty("attempt").GetInt32());

        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var decoded, out var decodeResult), decodeResult.Message);
        Assert.Equal(story.WrongAddressAssignments.OrderBy(a => a.Attempt), decoded!.Story!.WrongAddressAssignments);
        Assert.Equal(story.WrongAddressProgress, decoded.Story.WrongAddressProgress);
    }

    [Fact]
    public void Schema_five_requires_the_wrong_address_assignments_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["story"]!.AsObject().Remove("wrongAddressAssignments");

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, decoded.Reason);
    }

    [Fact]
    public void Schema_five_requires_the_wrong_address_progress_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["story"]!.AsObject().Remove("wrongAddressProgress");

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, decoded.Reason);
    }

    [Fact]
    public void An_unknown_field_inside_wrong_address_progress_fails_closed()
    {
        var story = StoryWithWrongAddress(WrongAddressAssignment(1, Release1TransitionKind.MissionAccepted));
        var withProgress = story with
        {
            WrongAddressProgress = new[] { new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, true, false) }
        };
        withProgress.Validate();
        Assert.True(Release1StorySaveCodec.TrySerialize(withProgress, out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["story"]!["wrongAddressProgress"]![0]!["bogus"] = "value";

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, decoded.Reason);
    }

    [Fact]
    public void Current_serialization_writes_schema_four_and_round_trips_assignments()
    {
        var assignment = Assignment();
        var story = StoryWithAssignments(assignment);
        story.Validate();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var encoded), encoded.Message);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);
        Assert.Equal(11, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var decoded, out var decodeResult), decodeResult.Message);
        Assert.Equal(assignment, Assert.Single(decoded!.Story!.SmallCourtesyAssignments));
    }

    [Fact]
    public void Current_serialization_writes_schema_five_and_round_trips_presentation_receipts()
    {
        var receipt = new Release1PresentationReceipt(
            Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "presentation-receipt").Value,
            1);
        var story = Story() with { PresentationReceipts = new[] { receipt } };
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var encoded), encoded.Message);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);
        Assert.Equal(11, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var decoded, out var decodeResult), decodeResult.Message);
        Assert.Equal(receipt, Assert.Single(decoded!.Story!.PresentationReceipts));
    }

    [Fact]
    public void Current_serialization_orders_assignments_by_attempt()
    {
        var primary = Assignment();
        var makeGood = new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy,
            2,
            Release1SmallCourtesyAssignmentMode.MakeGood,
            Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 2, Release1TransitionKind.MakeGoodAccepted, "make-good").Value,
            "product",
            "Product",
            500,
            "jar",
            "Jar",
            "drop-guid-2",
            "Drop 2",
            "Another dead drop",
            4,
            5,
            6,
            1.25);
        var story = StoryWithAssignments(makeGood, primary);

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var encoded), encoded.Message);
        using var document = JsonDocument.Parse(json);
        var assignments = document.RootElement.GetProperty("story").GetProperty("smallCourtesyAssignments");

        Assert.Equal(1, assignments[0].GetProperty("attempt").GetInt32());
        Assert.Equal(2, assignments[1].GetProperty("attempt").GetInt32());
    }

    [Fact]
    public void Schema_three_requires_the_assignment_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["story"]!.AsObject().Remove("smallCourtesyAssignments");

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, decoded.Reason);
    }

    [Fact]
    public void Schema_four_requires_the_presentation_receipts_collection()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["story"]!.AsObject().Remove("presentationReceipts");

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out _, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, decoded.Reason);
    }

    [Fact]
    public void The_current_schema_version_is_eleven_and_reads_one_through_eleven()
    {
        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);

        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        for (var version = 1; version <= 11; version++)
        {
            var root = JsonNode.Parse(currentJson)!.AsObject();
            root["schemaVersion"] = version;
            Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
            Assert.Equal(11, envelope!.SchemaVersion);
        }

        var twelve = JsonNode.Parse(currentJson)!.AsObject();
        twelve["schemaVersion"] = 12;
        Assert.False(Release1StorySaveCodec.TryDeserialize(twelve.ToJsonString(), out _, out var twelveResult));
        Assert.Equal(Release1StoryCodecFailureReason.UnsupportedSchema, twelveResult.Reason);
    }

    [Fact]
    public void Schema_v7_round_trips_assignments_and_progress()
    {
        var story = StoryWithShortNotice(out var assignment);
        var progress = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, assignment.Attempt, 2, 1, true);
        story = story with { ShortNoticeProgress = new[] { progress } };
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var write), write.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);

        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Equal(assignment, Assert.Single(envelope.Story!.ShortNoticeAssignments));
        Assert.Equal(progress, Assert.Single(envelope.Story.ShortNoticeProgress));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Schema_v1_to_v6_upgrade_normalizes_short_notice_collections_to_empty(int version)
    {
        var story = version == 6 ? StoryWithRoomWithNoName() : Story();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = version;
        root["story"]!.AsObject().Remove("shortNoticeAssignments");
        root["story"]!.AsObject().Remove("shortNoticeProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.ShortNoticeAssignments);
        Assert.Empty(envelope.Story.ShortNoticeProgress);
        if (version == 6)
            Assert.Single(envelope.Story.RoomWithNoNameAssignments);
    }

    [Fact]
    public void Schema_v7_read_refuses_a_missing_short_notice_array_and_an_unknown_field()
    {
        var story = StoryWithShortNotice(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var missingArray = JsonNode.Parse(json)!.AsObject();
        missingArray["story"]!.AsObject().Remove("shortNoticeAssignments");

        Assert.False(Release1StorySaveCodec.TryDeserialize(missingArray.ToJsonString(), out _, out var missingResult));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, missingResult.Reason);

        var unknownField = json.Replace("\"requiredQuantity\"", "\"requiredQuantityy\"", StringComparison.Ordinal);
        Assert.False(Release1StorySaveCodec.TryDeserialize(unknownField, out _, out var unknownResult));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, unknownResult.Reason);
    }

    [Fact]
    public void A_v10_round_trip_preserves_the_owned_property_count()
    {
        var story = StoryWithKeepTheLightsOff(out var assignment, ownedPropertyCount: 4);
        var progress = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, assignment.Attempt, 200d, 250d);
        story = story with { KeepTheLightsOffProgress = new[] { progress } };
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var write), write.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);

        Assert.Equal(11, envelope!.SchemaVersion);
        var restored = Assert.Single(envelope.Story!.KeepTheLightsOffAssignments);
        Assert.Equal(assignment, restored);
        Assert.Equal(4, restored.ExpectedOwnedPropertyCount);
        Assert.Equal(progress, Assert.Single(envelope.Story.KeepTheLightsOffProgress));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Schema_v1_to_v7_upgrade_normalizes_keep_the_lights_off_collections_to_empty(int version)
    {
        var story = version == 7 ? StoryWithShortNotice(out _) : Story();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = version;
        root["story"]!.AsObject().Remove("keepTheLightsOffAssignments");
        root["story"]!.AsObject().Remove("keepTheLightsOffProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.KeepTheLightsOffAssignments);
        Assert.Empty(envelope.Story.KeepTheLightsOffProgress);
        if (version == 7)
            Assert.Single(envelope.Story.ShortNoticeAssignments);
    }

    [Fact]
    public void A_v10_read_refuses_a_missing_keep_the_lights_off_array_and_an_unknown_field()
    {
        var story = StoryWithKeepTheLightsOff(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var missingArray = JsonNode.Parse(json)!.AsObject();
        missingArray["story"]!.AsObject().Remove("keepTheLightsOffAssignments");

        Assert.False(Release1StorySaveCodec.TryDeserialize(missingArray.ToJsonString(), out _, out var missingResult));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, missingResult.Reason);

        var unknownField = json.Replace("\"expectedOwnedPropertyCount\"", "\"expectedOwnedPropertyCountt\"", StringComparison.Ordinal);
        Assert.False(Release1StorySaveCodec.TryDeserialize(unknownField, out _, out var unknownResult));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, unknownResult.Reason);
    }

    [Fact]
    public void A_v10_document_carrying_a_v9_assignment_shape_is_refused()
    {
        var story = StoryWithKeepTheLightsOff(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        var assignments = root["story"]!["keepTheLightsOffAssignments"]!.AsArray();
        assignments[0]!["productId"] = "product";

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, decoded.Reason);
        Assert.Null(envelope);
    }

    [Fact]
    public void A_v9_document_normalizes_both_keep_the_lights_off_collections_to_empty()
    {
        var json = File.ReadAllText(KeepTheLightsOffFixturePath("release1-story-v9-keep-the-lights-off-active.json"));

        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var decoded), decoded.Message);

        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.KeepTheLightsOffAssignments);
        Assert.Empty(envelope.Story.KeepTheLightsOffProgress);
        Assert.Single(envelope.Story.WrongAddressAssignments);
        Assert.Single(envelope.Story.WrongAddressProgress);
        Assert.Single(envelope.Story.ShortNoticeAssignments);
        Assert.Single(envelope.Story.ShortNoticeProgress);
    }

    [Fact]
    public void A_v9_satisfied_attempt_stays_satisfied_with_its_collections_emptied()
    {
        var json = File.ReadAllText(KeepTheLightsOffFixturePath("release1-story-v9-keep-the-lights-off-satisfied.json"));

        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var decoded), decoded.Message);

        var mission = envelope!.Story!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        Assert.Equal(Release1MissionState.Satisfied, mission.State);
        Assert.Equal(1, mission.Attempt);
        Assert.Equal(
            "oc10/v1/76561190000000001/release1.keep-the-lights-off/1/MissionAccepted/ktlo-authorize-1",
            Assert.Single(mission.AcceptedLogicalCorrelations));
        Assert.Empty(envelope.Story.KeepTheLightsOffAssignments);
        Assert.Empty(envelope.Story.KeepTheLightsOffProgress);
    }

    private static string KeepTheLightsOffFixturePath(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", fileName);

    private static Release1KeepTheLightsOffAssignment KeepTheLightsOffAssignmentFor(string playerId, int attempt, int ownedPropertyCount = 1) => new(
        Release1MissionCatalog.KeepTheLightsOff,
        attempt,
        Release1KeepTheLightsOffAssignmentMode.Primary,
        Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.KeepTheLightsOff, attempt, Release1TransitionKind.MissionAccepted, $"ktlo-authorize-{attempt}").Value,
        ownedPropertyCount,
        Release1KeepTheLightsOffAssignment.WindowGameMinutes,
        Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);

    private static Release1StoryState StoryWithKeepTheLightsOff(out Release1KeepTheLightsOffAssignment assignment, int ownedPropertyCount = 1)
    {
        var story = Story();
        assignment = KeepTheLightsOffAssignmentFor(story.PlayerId, 1, ownedPropertyCount);
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        missions[index] = missions[index] with
        {
            State = Release1MissionState.Accepted,
            Attempt = 1,
            AcceptedLogicalCorrelations = new[] { assignment.AuthorizationCorrelationId }
        };
        return story with { Missions = missions, KeepTheLightsOffAssignments = new[] { assignment } };
    }

    [Fact]
    public void Schema_v9_round_trips_an_envelope_assignment_with_the_room_key_and_closet_count()
    {
        var story = StoryWithTheEnvelope(out var assignment);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 500d, 1500d, true);
        story = story with { TheEnvelopeProgress = new[] { progress } };
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var write), write.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);

        Assert.Equal(11, envelope!.SchemaVersion);
        var restored = Assert.Single(envelope.Story!.TheEnvelopeAssignments);
        Assert.Equal(assignment, restored);
        Assert.Equal(Release1RoomWithNoNameAssignment.SyndicateHqRoomKey, restored.HoldRoomKey);
        Assert.Equal(Release1RoomWithNoNameAssignment.SyndicateHqClosetCount, restored.ExpectedClosetCount);
        Assert.Equal(progress, Assert.Single(envelope.Story.TheEnvelopeProgress));
    }

    [Fact]
    public void A_v9_document_whose_envelope_assignment_carries_the_old_drop_fields_fails_closed()
    {
        var story = StoryWithTheEnvelope(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        var assignments = root["story"]!["theEnvelopeAssignments"]!.AsArray();
        var assignment = assignments[0]!.AsObject();
        assignment["handoffDropGuid"] = "drop-guid";

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, decoded.Reason);
        Assert.Null(envelope);
    }

    [Fact]
    public void A_v9_document_whose_envelope_assignment_omits_holdRoomKey_fails_closed()
    {
        var story = StoryWithTheEnvelope(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        var assignments = root["story"]!["theEnvelopeAssignments"]!.AsArray();
        var assignment = assignments[0]!.AsObject();
        assignment.Remove("holdRoomKey");

        Assert.False(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, decoded.Reason);
        Assert.Null(envelope);
    }

    [Fact]
    public void A_v8_document_upgrades_with_empty_envelope_collections()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = 8;
        root["story"]!.AsObject().Remove("theEnvelopeAssignments");
        root["story"]!.AsObject().Remove("theEnvelopeProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);

        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.TheEnvelopeAssignments);
        Assert.Empty(envelope.Story.TheEnvelopeProgress);
    }

    [Fact]
    public void The_envelope_progress_field_set_is_unchanged_at_v9()
    {
        var story = StoryWithTheEnvelope(out var assignment);
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, assignment.Attempt, 500d, 1500d, true);
        story = story with { TheEnvelopeProgress = new[] { progress } };

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var root = JsonNode.Parse(json)!.AsObject();
        var progressDto = root["story"]!["theEnvelopeProgress"]!.AsArray()[0]!.AsObject();

        var fieldNames = progressDto.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = new[] { "attempt", "lastShortfallNoticed", "missionKey", "observedBalance", "spreadNoticed" };
        Assert.Equal(expected, fieldNames);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void V1_through_v8_normalize_to_an_empty_TheEnvelope_collection(int version)
    {
        // Version 7 carries real Short Notice data forward: proof that upgrading past it to the
        // current schema neither loses nor disturbs an earlier mission's persisted state.
        var story = version switch
        {
            6 => StoryWithRoomWithNoName(),
            7 => StoryWithShortNotice(out _),
            _ => Story()
        };
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var currentJson, out var encoded), encoded.Message);
        var root = JsonNode.Parse(currentJson)!.AsObject();
        root["schemaVersion"] = version;
        root["story"]!.AsObject().Remove("theEnvelopeAssignments");
        root["story"]!.AsObject().Remove("theEnvelopeProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.TheEnvelopeAssignments);
        Assert.Empty(envelope.Story.TheEnvelopeProgress);
        if (version == 6) Assert.Single(envelope.Story.RoomWithNoNameAssignments);
        if (version == 7) Assert.Single(envelope.Story.ShortNoticeAssignments);
    }

    [Fact]
    public void A_v8_file_with_keep_the_lights_off_data_normalizes_it_and_the_envelope_to_empty()
    {
        // A v8 sidecar predates both the v9 Envelope collections and the v10 Keep the Lights Off
        // shape: reading one must normalize both to empty while every other mission's data survives.
        var json = File.ReadAllText(KeepTheLightsOffFixturePath("release1-story-v9-keep-the-lights-off-active.json"));
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = 8;
        root["story"]!.AsObject().Remove("theEnvelopeAssignments");
        root["story"]!.AsObject().Remove("theEnvelopeProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(root.ToJsonString(), out var envelope, out var decoded), decoded.Message);
        Assert.Equal(11, envelope!.SchemaVersion);
        Assert.Empty(envelope.Story!.KeepTheLightsOffAssignments);
        Assert.Empty(envelope.Story.KeepTheLightsOffProgress);
        Assert.Empty(envelope.Story.TheEnvelopeAssignments);
        Assert.Empty(envelope.Story.TheEnvelopeProgress);
        Assert.Single(envelope.Story.WrongAddressAssignments);
        Assert.Single(envelope.Story.ShortNoticeAssignments);
    }

    [Fact]
    public void Writing_always_emits_schema_version_11()
    {
        Assert.True(Release1StorySaveCodec.TrySerialize(Story(), out var json, out var encoded), encoded.Message);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);
        Assert.Equal(11, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Unknown_field_in_theEnvelopeAssignments_is_rejected()
    {
        var story = StoryWithTheEnvelope(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));

        var unknownField = json.Replace("\"amountWholeDollars\"", "\"amountWholeDollarsx\"", StringComparison.Ordinal);
        Assert.False(Release1StorySaveCodec.TryDeserialize(unknownField, out _, out var unknownResult));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, unknownResult.Reason);
    }

    private static Release1TheEnvelopeAssignment TheEnvelopeAssignmentFor(string playerId, int attempt) => new(
        Release1MissionCatalog.TheEnvelope,
        attempt,
        Release1TheEnvelopeAssignmentMode.Primary,
        Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.TheEnvelope, attempt, Release1TransitionKind.MissionAccepted, $"te-authorize-{attempt}").Value,
        Release1TheEnvelopeAssignment.PrimaryAmount,
        Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
        Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
        Release1TheEnvelopeAssignment.TimedStageGameMinutes);

    private static Release1StoryState StoryWithTheEnvelope(out Release1TheEnvelopeAssignment assignment)
    {
        var story = Story();
        assignment = TheEnvelopeAssignmentFor(story.PlayerId, 1);
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        missions[index] = missions[index] with
        {
            State = Release1MissionState.Accepted,
            Attempt = 1,
            AcceptedLogicalCorrelations = new[] { assignment.AuthorizationCorrelationId }
        };
        return story with { Missions = missions, TheEnvelopeAssignments = new[] { assignment } };
    }

    private static Release1ShortNoticeAssignment ShortNoticeAssignmentFor(string playerId, int attempt) => new(
        Release1MissionCatalog.ShortNotice,
        attempt,
        Release1ShortNoticeAssignmentMode.Primary,
        Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.ShortNotice, attempt, Release1TransitionKind.MissionAccepted, $"sn-authorize-{attempt}").Value,
        "product",
        "Product",
        "brick",
        "Brick",
        Release1ShortNoticeAssignment.QuantityFor(Release1ShortNoticeAssignmentMode.Primary),
        $"drop-handoff-{attempt}",
        $"Handoff {attempt}",
        "The right address.",
        attempt,
        2,
        3,
        Release1ShortNoticeAssignment.TimedStageGameMinutes,
        Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
        Release1ShortNoticeValueConvention.PerUnit);

    private static Release1StoryState StoryWithShortNotice(out Release1ShortNoticeAssignment assignment)
    {
        var story = Story();
        assignment = ShortNoticeAssignmentFor(story.PlayerId, 1);
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        missions[index] = missions[index] with
        {
            State = Release1MissionState.Accepted,
            Attempt = 1,
            AcceptedLogicalCorrelations = new[] { assignment.AuthorizationCorrelationId }
        };
        return story with { Missions = missions, ShortNoticeAssignments = new[] { assignment } };
    }

    private static Release1RoomWithNoNameAssignment RoomWithNoNameAssignmentFor(string playerId, int attempt) => new(
        Release1MissionCatalog.RoomWithNoName,
        attempt,
        Release1RoomWithNoNameAssignmentMode.Primary,
        Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.RoomWithNoName, attempt, Release1TransitionKind.MissionAccepted, $"rwnn-authorize-{attempt}").Value,
        "product",
        "Product",
        "brick",
        "Brick",
        1,
        $"drop-source-{attempt}",
        $"Source {attempt}",
        "A safehouse.",
        attempt,
        2,
        3,
        $"drop-handoff-{attempt}",
        $"Handoff {attempt}",
        "The room.",
        attempt + 10,
        2,
        3,
        "syndicate-hq",
        9,
        1_440d,
        1.5d);

    private static Release1StoryState StoryWithRoomWithNoName()
    {
        var story = Story();
        var assignment = RoomWithNoNameAssignmentFor(story.PlayerId, 1);
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        missions[index] = missions[index] with
        {
            State = Release1MissionState.Accepted,
            Attempt = 1,
            AcceptedLogicalCorrelations = new[] { assignment.AuthorizationCorrelationId }
        };
        return story with { Missions = missions, RoomWithNoNameAssignments = new[] { assignment } };
    }

    private static Release1WrongAddressAssignment WrongAddressAssignment(int attempt, Release1TransitionKind transition) => new(
        Release1MissionCatalog.WrongAddress,
        attempt,
        transition switch
        {
            Release1TransitionKind.MissionAccepted => Release1WrongAddressAssignmentMode.Primary,
            Release1TransitionKind.MakeGoodAccepted => Release1WrongAddressAssignmentMode.MakeGood,
            Release1TransitionKind.RecoveryAccepted => Release1WrongAddressAssignmentMode.Recovery,
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        },
        Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.WrongAddress, attempt, transition, $"wa-authorize-{attempt}").Value,
        "product",
        "Product",
        "brick",
        "Brick",
        1,
        $"drop-source-{attempt}",
        $"Source {attempt}",
        "A wrong address",
        attempt,
        2,
        3,
        $"drop-handoff-{attempt}",
        $"Handoff {attempt}",
        "The right address",
        attempt + 10,
        2,
        3,
        1.25);

    private static Release1StoryState StoryWithWrongAddress(params Release1WrongAddressAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        missions[index] = missions[index] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments.Select(assignment => assignment.AuthorizationCorrelationId).ToArray()
        };
        return story with { Missions = missions, WrongAddressAssignments = assignments };
    }

    private static Release1SmallCourtesyAssignment Assignment() => new(
        Release1MissionCatalog.SmallCourtesy,
        1,
        Release1SmallCourtesyAssignmentMode.Primary,
        Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "effect-authorize").Value,
        "product",
        "Product",
        500,
        "brick",
        "Brick",
        "drop-guid",
        "Drop",
        "A dead drop",
        1,
        2,
        3,
        1.25);

    private static Release1StoryState Story() => new(
        "76561190000000001", 20, Release1RelationshipState.Accepted, false, new[] { "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1" }, Array.Empty<string>(),
        Release1MissionCatalog.All.Select((m, i) => new Release1MissionRecord(m.MissionKey, i == 0 ? Release1MissionState.Accepted : Release1MissionState.Locked, 1, i == 0 ? "v1" : null, null, null, Release1MissionOutcome.None, 0, Array.Empty<string>(), 0, Release1RecoveryMode.None, null, false, i == 0 ? new[] { "effect-1" } : Array.Empty<string>(), Array.Empty<string>(), i == 0 ? new[] { Release1LogicalCorrelation.Create("76561190000000001", m.MissionKey, 1, Release1TransitionKind.MissionAccepted, "effect-authorize").Value } : Array.Empty<string>(), null, 0)).ToArray(),
        new[] { new Release1NativeEffectJournalEntry("effect-1", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", Release1NativeEffectPhase.Prepared, null, 0, AuthorizedStoryCorrelationId: Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "effect-authorize").Value, AuthorizedMissionRevision: 0) }, 0);

    private static Release1StoryState StoryWithAssignments(params Release1SmallCourtesyAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        missions[0] = missions[0] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments
                .Select(assignment => assignment.AuthorizationCorrelationId)
                .Append(Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionAccepted, "effect-authorize").Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
        return story with { Missions = missions, SmallCourtesyAssignments = assignments };
    }

    private sealed class TemporarySaveFolder : IDisposable
    {
        private TemporarySaveFolder(string path) => Path = path;
        public string Path { get; }
        public static TemporarySaveFolder Create() { var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrganizedCrimeTests", "Release1", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return new(path); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
