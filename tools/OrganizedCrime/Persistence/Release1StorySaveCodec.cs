using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Persistence;

public enum Release1StoryCodecFailureReason
{
    None, EmptyJson, MalformedJson, InvalidRoot, UnknownField, MissingRequiredField,
    InvalidJsonType, InvalidSchemaVersion, UnsupportedSchema, InvalidStory, InvalidMission,
    InvalidEffect, DuplicateId, SerializationFailed
}

public sealed record Release1StoryCodecResult(bool Succeeded, Release1StoryCodecFailureReason Reason, string Message)
{
    public static Release1StoryCodecResult Success() => new(true, Release1StoryCodecFailureReason.None, string.Empty);
    public static Release1StoryCodecResult Failure(Release1StoryCodecFailureReason reason, string message) => new(false, reason, message);
}

public static class Release1StorySaveCodec
{
    public const int CurrentSchemaVersion = 11;
    private static readonly HashSet<int> SupportedReadSchemaVersions = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, CurrentSchemaVersion };
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };
    private static readonly HashSet<string> EnvelopeFields = Set("schemaVersion", "story");
    private static readonly HashSet<string> StoryFields = Set("playerId", "standing", "relationshipState", "release1Recognized", "introLogicalCorrelationIds", "recognitionLogicalCorrelationIds", "missions", "nativeEffects", "phonePresentationAttempts", "smallCourtesyAssignments", "presentationReceipts", "wrongAddressAssignments", "wrongAddressProgress", "roomWithNoNameAssignments", "roomWithNoNameProgress", "shortNoticeAssignments", "shortNoticeProgress", "keepTheLightsOffAssignments", "keepTheLightsOffProgress", "theEnvelopeAssignments", "theEnvelopeProgress", "chiefRecord", "revision");
    private static readonly HashSet<string> RequiredStoryFields = Set("playerId", "standing", "relationshipState", "release1Recognized", "introLogicalCorrelationIds", "recognitionLogicalCorrelationIds", "missions", "nativeEffects", "revision");
    private static readonly HashSet<string> MissionFields = Set("missionKey", "state", "attempt", "termsVersion", "acceptedGameTimeHours", "deadlineGameTimeHours", "lastOutcome", "standingPenaltyApplied", "penaltyReceiptIds", "makeGoodFailures", "recoveryMode", "rewardAuthorizationReceiptId", "quietConditionAwarded", "nativeEffectIds", "presentationCorrelationIds", "acceptedLogicalCorrelations", "nativePresentationRef", "revision");
    private static readonly HashSet<string> EffectFields = Set("effectId", "missionKey", "attempt", "effectKind", "sourceIdentity", "destinationIdentity", "amountOrCargoIdentity", "phase", "nativeReceiptId", "preparedStoryRevision", "committedStoryCorrelationId", "authorizedStoryCorrelationId", "executionBlocked", "authorizedMissionRevision");
    private static readonly HashSet<string> PhonePresentationAttemptFields = Set("correlationId", "missionKey", "attempt", "role", "requiredPriorCorrelationId", "state", "revision");
    private static readonly HashSet<string> SmallCourtesyAssignmentFields = Set("missionKey", "attempt", "mode", "authorizationCorrelationId", "productId", "productName", "selectionAskingPrice", "packagingId", "packagingName", "deadDropGuid", "deadDropName", "deadDropDescription", "deadDropX", "deadDropY", "deadDropZ", "rewardMultiplier");
    private static readonly HashSet<string> PresentationReceiptFields = Set("correlationId", "revision");
    private static readonly HashSet<string> WrongAddressAssignmentFields = Set("missionKey", "attempt", "mode", "authorizationCorrelationId", "productId", "productName", "packagingId", "packagingName", "packageQuantity", "sourceDropGuid", "sourceDropName", "sourceDropDescription", "sourceDropX", "sourceDropY", "sourceDropZ", "handoffDropGuid", "handoffDropName", "handoffDropDescription", "handoffDropX", "handoffDropY", "handoffDropZ", "rewardMultiplier");
    private static readonly HashSet<string> WrongAddressProgressFields = Set("missionKey", "attempt", "staged", "custody");
    private static readonly HashSet<string> RoomWithNoNameAssignmentFields = Set("missionKey", "attempt", "mode", "authorizationCorrelationId", "productId", "productName", "packagingId", "packagingName", "packageQuantity", "sourceDropGuid", "sourceDropName", "sourceDropDescription", "sourceDropX", "sourceDropY", "sourceDropZ", "handoffDropGuid", "handoffDropName", "handoffDropDescription", "handoffDropX", "handoffDropY", "handoffDropZ", "holdRoomKey", "expectedClosetCount", "holdDurationGameMinutes", "rewardMultiplier");
    private static readonly HashSet<string> RoomWithNoNameProgressFields = Set("missionKey", "attempt", "staged", "custody", "stowed", "holdSatisfied", "stowedAtGameMinutes", "holdingClosetGuid", "missingSincePassGameMinutes");
    private static readonly HashSet<string> ShortNoticeAssignmentFields = Set("missionKey", "attempt", "mode", "authorizationCorrelationId", "productId", "productName", "packagingId", "packagingName", "requiredQuantity", "handoffDropGuid", "handoffDropName", "handoffDropDescription", "handoffDropX", "handoffDropY", "handoffDropZ", "deadlineGameMinutes", "rewardMultiplier", "valueConvention");
    private static readonly HashSet<string> ShortNoticeProgressFields = Set("missionKey", "attempt", "observedQuantity", "lastShortfallNoticed", "spreadNoticed");
    private static readonly HashSet<string> KeepTheLightsOffAssignmentFields = Set(
        "missionKey", "attempt", "mode", "authorizationCorrelationId",
        "expectedOwnedPropertyCount", "windowDurationGameMinutes", "deadlineGameMinutes");
    private static readonly HashSet<string> KeepTheLightsOffProgressFields = Set("missionKey", "attempt", "clearConfirmedAtGameMinutes", "breachSincePassGameMinutes");
    private static readonly HashSet<string> TheEnvelopeAssignmentFields = Set("missionKey", "attempt", "mode", "authorizationCorrelationId", "amountWholeDollars", "holdRoomKey", "expectedClosetCount", "deadlineGameMinutes");
    private static readonly HashSet<string> TheEnvelopeProgressFields = Set("missionKey", "attempt", "observedBalance", "lastShortfallNoticed", "spreadNoticed");
    private static readonly HashSet<string> ChiefRecordFields = Set(
        "adopted", "adoptedTier", "adoptedKnownOffender", "demandRound", "state", "watchLinesSent",
        "lockdownAnnouncements", "lockdownEngaged", "paidLifts", "cooledLifts", "declineRepliesSent",
        "paymentBlockedNotices", "lastShortfallNoticed",
        "acceptedLogicalCorrelations", "nativeEffectIds", "revision");

    public static bool TrySerialize(Release1StorySaveEnvelope? envelope, out string json, out Release1StoryCodecResult result)
    {
        json = string.Empty;
        if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion)
        {
            result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.UnsupportedSchema, "Release 1 story envelope schema was unsupported.");
            return false;
        }
        try
        {
            envelope.Story?.Validate();
            var dto = new EnvelopeDto
            {
                SchemaVersion = CurrentSchemaVersion,
                Story = envelope.Story is null ? null : ToDto(envelope.Story)
            };
            json = JsonSerializer.Serialize(dto, Options);
            result = Release1StoryCodecResult.Success();
            return true;
        }
        catch (ArgumentException ex)
        {
            result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.InvalidStory, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.SerializationFailed, ex.Message);
            return false;
        }
    }

    public static bool TrySerialize(Release1StoryState? story, out string json, out Release1StoryCodecResult result) =>
        TrySerialize(new Release1StorySaveEnvelope(CurrentSchemaVersion, story), out json, out result);

    public static bool TryDeserialize(string? json, out Release1StorySaveEnvelope? envelope, out Release1StoryCodecResult result)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(json)) { result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.EmptyJson, "Story sidecar JSON was empty."); return false; }
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Fail(out result, Release1StoryCodecFailureReason.InvalidRoot, "Story sidecar root was not an object.");
            if (!ValidateFields(document.RootElement, EnvelopeFields, out result)) return false;
            if (!RequireFields(document.RootElement, EnvelopeFields, out result)) return false;
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version)) return Fail(out result, Release1StoryCodecFailureReason.InvalidSchemaVersion, "schemaVersion was invalid.");
            if (!SupportedReadSchemaVersions.Contains(version)) return Fail(out result, Release1StoryCodecFailureReason.UnsupportedSchema, "Story sidecar schema was unsupported.");
            var dto = JsonSerializer.Deserialize<EnvelopeDto>(json, Options);
            if (dto is null || dto.SchemaVersion != version) return Fail(out result, Release1StoryCodecFailureReason.InvalidStory, "Story envelope was invalid.");
            if (dto.Story is null) { envelope = Release1StorySaveEnvelope.CreateEmpty(); result = Release1StoryCodecResult.Success(); return true; }
            if (!ValidateDtoFields(document.RootElement.GetProperty("story"), StoryFields, out result)) return false;
            var storyElement = document.RootElement.GetProperty("story");
            if (!RequireFields(storyElement, RequiredStoryFields, out result)) return false;
            if (version >= 3 && !storyElement.TryGetProperty("smallCourtesyAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: smallCourtesyAssignments.");
            if (version >= 4 && !storyElement.TryGetProperty("presentationReceipts", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: presentationReceipts.");
            if (version >= 5 && !storyElement.TryGetProperty("wrongAddressAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: wrongAddressAssignments.");
            if (version >= 5 && !storyElement.TryGetProperty("wrongAddressProgress", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: wrongAddressProgress.");
            if (version >= 6 && !storyElement.TryGetProperty("roomWithNoNameAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: roomWithNoNameAssignments.");
            if (version >= 6 && !storyElement.TryGetProperty("roomWithNoNameProgress", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: roomWithNoNameProgress.");
            if (version >= 7 && !storyElement.TryGetProperty("shortNoticeAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: shortNoticeAssignments.");
            if (version >= 7 && !storyElement.TryGetProperty("shortNoticeProgress", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: shortNoticeProgress.");
            if (version >= 8 && !storyElement.TryGetProperty("keepTheLightsOffAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: keepTheLightsOffAssignments.");
            if (version >= 8 && !storyElement.TryGetProperty("keepTheLightsOffProgress", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: keepTheLightsOffProgress.");
            if (version >= 9 && !storyElement.TryGetProperty("theEnvelopeAssignments", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: theEnvelopeAssignments.");
            if (version >= 9 && !storyElement.TryGetProperty("theEnvelopeProgress", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: theEnvelopeProgress.");
            if (version >= 11 && !storyElement.TryGetProperty("chiefRecord", out _))
                return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, "Required field was missing: chiefRecord.");
            if (!ValidateArrays(storyElement, version, out result)) return false;
            var story = FromDto(dto.Story, version);
            story.Validate();
            envelope = new Release1StorySaveEnvelope(CurrentSchemaVersion, story);
            result = Release1StoryCodecResult.Success();
            return true;
        }
        catch (JsonException ex) { result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.MalformedJson, ex.Message); return false; }
        catch (ArgumentException ex) { result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.InvalidStory, ex.Message); return false; }
        catch (InvalidOperationException ex) { result = Release1StoryCodecResult.Failure(Release1StoryCodecFailureReason.InvalidJsonType, ex.Message); return false; }
    }

    private static bool ValidateArrays(JsonElement story, int version, out Release1StoryCodecResult result)
    {
        foreach (var name in new[] { "introLogicalCorrelationIds", "recognitionLogicalCorrelationIds", "missions", "nativeEffects" })
            if (!story.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, $"{name} was not an array.");
        foreach (var mission in story.GetProperty("missions").EnumerateArray())
        {
            if (mission.ValueKind != JsonValueKind.Object)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Mission was not an object.");
            if (!ValidateDtoFields(mission, MissionFields, out result)) return false;
            if (!RequireFields(mission, MissionFields, out result)) return false;
        }
        foreach (var effect in story.GetProperty("nativeEffects").EnumerateArray())
        {
            if (effect.ValueKind != JsonValueKind.Object)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Native effect was not an object.");
            if (!ValidateDtoFields(effect, EffectFields, out result)) return false;
            if (!RequireFields(effect, EffectFields, out result)) return false;
        }
        if (story.TryGetProperty("phonePresentationAttempts", out var presentations))
        {
            if (presentations.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "phonePresentationAttempts was not an array.");
            foreach (var presentation in presentations.EnumerateArray())
            {
                if (presentation.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Phone presentation attempt was not an object.");
                if (!ValidateDtoFields(presentation, PhonePresentationAttemptFields, out result)) return false;
                if (!RequireFields(presentation, PhonePresentationAttemptFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("smallCourtesyAssignments", out var assignments))
        {
            if (assignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "smallCourtesyAssignments was not an array.");
            foreach (var assignment in assignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Small Courtesy assignment was not an object.");
                if (!ValidateDtoFields(assignment, SmallCourtesyAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, SmallCourtesyAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("presentationReceipts", out var receipts))
        {
            if (receipts.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "presentationReceipts was not an array.");
            foreach (var receipt in receipts.EnumerateArray())
            {
                if (receipt.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Presentation receipt was not an object.");
                if (!ValidateDtoFields(receipt, PresentationReceiptFields, out result)) return false;
                if (!RequireFields(receipt, PresentationReceiptFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("wrongAddressAssignments", out var wrongAddressAssignments))
        {
            if (wrongAddressAssignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "wrongAddressAssignments was not an array.");
            foreach (var assignment in wrongAddressAssignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Wrong Address assignment was not an object.");
                if (!ValidateDtoFields(assignment, WrongAddressAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, WrongAddressAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("wrongAddressProgress", out var wrongAddressProgress))
        {
            if (wrongAddressProgress.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "wrongAddressProgress was not an array.");
            foreach (var progress in wrongAddressProgress.EnumerateArray())
            {
                if (progress.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Wrong Address progress was not an object.");
                if (!ValidateDtoFields(progress, WrongAddressProgressFields, out result)) return false;
                if (!RequireFields(progress, WrongAddressProgressFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("roomWithNoNameAssignments", out var roomWithNoNameAssignments))
        {
            if (roomWithNoNameAssignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "roomWithNoNameAssignments was not an array.");
            foreach (var assignment in roomWithNoNameAssignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Room With No Name assignment was not an object.");
                if (!ValidateDtoFields(assignment, RoomWithNoNameAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, RoomWithNoNameAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("roomWithNoNameProgress", out var roomWithNoNameProgress))
        {
            if (roomWithNoNameProgress.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "roomWithNoNameProgress was not an array.");
            foreach (var progress in roomWithNoNameProgress.EnumerateArray())
            {
                if (progress.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Room With No Name progress was not an object.");
                if (!ValidateDtoFields(progress, RoomWithNoNameProgressFields, out result)) return false;
                if (!RequireFields(progress, RoomWithNoNameProgressFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("shortNoticeAssignments", out var shortNoticeAssignments))
        {
            if (shortNoticeAssignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "shortNoticeAssignments was not an array.");
            foreach (var assignment in shortNoticeAssignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Short Notice assignment was not an object.");
                if (!ValidateDtoFields(assignment, ShortNoticeAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, ShortNoticeAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("shortNoticeProgress", out var shortNoticeProgress))
        {
            if (shortNoticeProgress.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "shortNoticeProgress was not an array.");
            foreach (var progress in shortNoticeProgress.EnumerateArray())
            {
                if (progress.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Short Notice progress was not an object.");
                if (!ValidateDtoFields(progress, ShortNoticeProgressFields, out result)) return false;
                if (!RequireFields(progress, ShortNoticeProgressFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("keepTheLightsOffAssignments", out var keepTheLightsOffAssignments))
        {
            if (keepTheLightsOffAssignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "keepTheLightsOffAssignments was not an array.");
            foreach (var assignment in keepTheLightsOffAssignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Keep the Lights Off assignment was not an object.");
                // A v9 document's assignment describes the retired product condition and cannot be
                // expressed in the v10 shape at all; FromDto normalizes it to empty below, so the
                // field set is only enforced from v10 onward.
                if (version < 10) continue;
                if (!ValidateDtoFields(assignment, KeepTheLightsOffAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, KeepTheLightsOffAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("keepTheLightsOffProgress", out var keepTheLightsOffProgress))
        {
            if (keepTheLightsOffProgress.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "keepTheLightsOffProgress was not an array.");
            foreach (var progress in keepTheLightsOffProgress.EnumerateArray())
            {
                if (progress.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "Keep the Lights Off progress was not an object.");
                if (!ValidateDtoFields(progress, KeepTheLightsOffProgressFields, out result)) return false;
                if (!RequireFields(progress, KeepTheLightsOffProgressFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("theEnvelopeAssignments", out var theEnvelopeAssignments))
        {
            if (theEnvelopeAssignments.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "theEnvelopeAssignments was not an array.");
            foreach (var assignment in theEnvelopeAssignments.EnumerateArray())
            {
                if (assignment.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "The Envelope assignment was not an object.");
                if (!ValidateDtoFields(assignment, TheEnvelopeAssignmentFields, out result)) return false;
                if (!RequireFields(assignment, TheEnvelopeAssignmentFields, out result)) return false;
            }
        }
        if (story.TryGetProperty("theEnvelopeProgress", out var theEnvelopeProgress))
        {
            if (theEnvelopeProgress.ValueKind != JsonValueKind.Array)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "theEnvelopeProgress was not an array.");
            foreach (var progress in theEnvelopeProgress.EnumerateArray())
            {
                if (progress.ValueKind != JsonValueKind.Object)
                    return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "The Envelope progress was not an object.");
                if (!ValidateDtoFields(progress, TheEnvelopeProgressFields, out result)) return false;
                if (!RequireFields(progress, TheEnvelopeProgressFields, out result)) return false;
            }
        }
        if (version >= 11 && story.TryGetProperty("chiefRecord", out var chiefRecord) && chiefRecord.ValueKind != JsonValueKind.Null)
        {
            if (chiefRecord.ValueKind != JsonValueKind.Object)
                return Fail(out result, Release1StoryCodecFailureReason.InvalidJsonType, "chiefRecord was not an object.");
            if (!ValidateDtoFields(chiefRecord, ChiefRecordFields, out result)) return false;
            if (!RequireFields(chiefRecord, ChiefRecordFields, out result)) return false;
        }
        result = Release1StoryCodecResult.Success(); return true;
    }

    private static bool ValidateDtoFields(JsonElement element, HashSet<string> fields, out Release1StoryCodecResult result) => ValidateFields(element, fields, out result);
    private static bool ValidateFields(JsonElement element, HashSet<string> fields, out Release1StoryCodecResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.Contains(property.Name) || !seen.Add(property.Name)) return Fail(out result, Release1StoryCodecFailureReason.UnknownField, $"Unknown or duplicate field: {property.Name}.");
        }
        result = Release1StoryCodecResult.Success(); return true;
    }
    private static bool RequireFields(JsonElement element, HashSet<string> fields, out Release1StoryCodecResult result)
    {
        foreach (var field in fields)
            if (!element.TryGetProperty(field, out _)) return Fail(out result, Release1StoryCodecFailureReason.MissingRequiredField, $"Required field was missing: {field}.");
        result = Release1StoryCodecResult.Success(); return true;
    }
    private static bool Fail(out Release1StoryCodecResult result, Release1StoryCodecFailureReason reason, string message) { result = Release1StoryCodecResult.Failure(reason, message); return false; }
    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static StoryDto ToDto(Release1StoryState story) => new()
    {
        PlayerId = story.PlayerId, Standing = story.Standing, RelationshipState = story.RelationshipState, Release1Recognized = story.Release1Recognized,
        IntroLogicalCorrelationIds = story.IntroLogicalCorrelationIds.ToArray(), RecognitionLogicalCorrelationIds = story.RecognitionLogicalCorrelationIds.ToArray(),
        Missions = story.Missions.Select(ToDto).ToArray(), NativeEffects = story.NativeEffects.OrderBy(e => e.EffectId, StringComparer.Ordinal).Select(ToDto).ToArray(), PhonePresentationAttempts = story.PhonePresentationAttempts.Select(ToDto).ToArray(), SmallCourtesyAssignments = story.SmallCourtesyAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(), PresentationReceipts = story.PresentationReceipts.Select(ToDto).ToArray(), WrongAddressAssignments = story.WrongAddressAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(), WrongAddressProgress = story.WrongAddressProgress.OrderBy(progress => progress.Attempt).Select(ToDto).ToArray(),
        RoomWithNoNameAssignments = story.RoomWithNoNameAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(),
        RoomWithNoNameProgress = story.RoomWithNoNameProgress.OrderBy(progress => progress.Attempt).Select(ToDto).ToArray(),
        ShortNoticeAssignments = story.ShortNoticeAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(),
        ShortNoticeProgress = story.ShortNoticeProgress.OrderBy(progress => progress.Attempt).Select(ToDto).ToArray(),
        KeepTheLightsOffAssignments = story.KeepTheLightsOffAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(),
        KeepTheLightsOffProgress = story.KeepTheLightsOffProgress.OrderBy(progress => progress.Attempt).Select(ToDto).ToArray(),
        TheEnvelopeAssignments = story.TheEnvelopeAssignments.OrderBy(assignment => assignment.Attempt).Select(ToDto).ToArray(),
        TheEnvelopeProgress = story.TheEnvelopeProgress.OrderBy(progress => progress.Attempt).Select(ToDto).ToArray(),
        ChiefRecord = story.ChiefRecord is null ? null : ToDto(story.ChiefRecord),
        Revision = story.Revision
    };
    private static MissionDto ToDto(Release1MissionRecord m) => new()
    {
        MissionKey = m.MissionKey, State = m.State, Attempt = m.Attempt, TermsVersion = m.TermsVersion, AcceptedGameTimeHours = m.AcceptedGameTimeHours, DeadlineGameTimeHours = m.DeadlineGameTimeHours, LastOutcome = m.LastOutcome, StandingPenaltyApplied = m.StandingPenaltyApplied, PenaltyReceiptIds = m.PenaltyReceiptIds.ToArray(), MakeGoodFailures = m.MakeGoodFailures, RecoveryMode = m.RecoveryMode, RewardAuthorizationReceiptId = m.RewardAuthorizationReceiptId, QuietConditionAwarded = m.QuietConditionAwarded, NativeEffectIds = m.NativeEffectIds.ToArray(), PresentationCorrelationIds = m.PresentationCorrelationIds.ToArray(), AcceptedLogicalCorrelations = m.AcceptedLogicalCorrelations.ToArray(), NativePresentationRef = m.NativePresentationRef, Revision = m.Revision
    };
    private static EffectDto ToDto(Release1NativeEffectJournalEntry e) => new() { EffectId = e.EffectId, MissionKey = e.MissionKey, Attempt = e.Attempt, EffectKind = e.EffectKind, SourceIdentity = e.SourceIdentity, DestinationIdentity = e.DestinationIdentity, AmountOrCargoIdentity = e.AmountOrCargoIdentity, Phase = e.Phase, NativeReceiptId = e.NativeReceiptId, PreparedStoryRevision = e.PreparedStoryRevision, CommittedStoryCorrelationId = e.CommittedStoryCorrelationId, AuthorizedStoryCorrelationId = e.AuthorizedStoryCorrelationId, ExecutionBlocked = e.ExecutionBlocked, AuthorizedMissionRevision = e.AuthorizedMissionRevision };
    private static Release1PhonePresentationAttemptDto ToDto(Release1PhonePresentationAttempt attempt) => new() { CorrelationId = attempt.CorrelationId, MissionKey = attempt.MissionKey, Attempt = attempt.Attempt, Role = attempt.Role, RequiredPriorCorrelationId = attempt.RequiredPriorCorrelationId, State = attempt.State, Revision = attempt.Revision };
    private static SmallCourtesyAssignmentDto ToDto(Release1SmallCourtesyAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, ProductId = assignment.ProductId, ProductName = assignment.ProductName, SelectionAskingPrice = assignment.SelectionAskingPrice, PackagingId = assignment.PackagingId, PackagingName = assignment.PackagingName, DeadDropGuid = assignment.DeadDropGuid, DeadDropName = assignment.DeadDropName, DeadDropDescription = assignment.DeadDropDescription, DeadDropX = assignment.DeadDropX, DeadDropY = assignment.DeadDropY, DeadDropZ = assignment.DeadDropZ, RewardMultiplier = assignment.RewardMultiplier };
    private static PresentationReceiptDto ToDto(Release1PresentationReceipt receipt) => new() { CorrelationId = receipt.CorrelationId, Revision = receipt.Revision };
    private static WrongAddressAssignmentDto ToDto(Release1WrongAddressAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, ProductId = assignment.ProductId, ProductName = assignment.ProductName, PackagingId = assignment.PackagingId, PackagingName = assignment.PackagingName, PackageQuantity = assignment.PackageQuantity, SourceDropGuid = assignment.SourceDropGuid, SourceDropName = assignment.SourceDropName, SourceDropDescription = assignment.SourceDropDescription, SourceDropX = assignment.SourceDropX, SourceDropY = assignment.SourceDropY, SourceDropZ = assignment.SourceDropZ, HandoffDropGuid = assignment.HandoffDropGuid, HandoffDropName = assignment.HandoffDropName, HandoffDropDescription = assignment.HandoffDropDescription, HandoffDropX = assignment.HandoffDropX, HandoffDropY = assignment.HandoffDropY, HandoffDropZ = assignment.HandoffDropZ, RewardMultiplier = assignment.RewardMultiplier };
    private static WrongAddressProgressDto ToDto(Release1WrongAddressProgress progress) => new() { MissionKey = progress.MissionKey, Attempt = progress.Attempt, Staged = progress.Staged, Custody = progress.Custody };
    private static RoomWithNoNameAssignmentDto ToDto(Release1RoomWithNoNameAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, ProductId = assignment.ProductId, ProductName = assignment.ProductName, PackagingId = assignment.PackagingId, PackagingName = assignment.PackagingName, PackageQuantity = assignment.PackageQuantity, SourceDropGuid = assignment.SourceDropGuid, SourceDropName = assignment.SourceDropName, SourceDropDescription = assignment.SourceDropDescription, SourceDropX = assignment.SourceDropX, SourceDropY = assignment.SourceDropY, SourceDropZ = assignment.SourceDropZ, HandoffDropGuid = assignment.HandoffDropGuid, HandoffDropName = assignment.HandoffDropName, HandoffDropDescription = assignment.HandoffDropDescription, HandoffDropX = assignment.HandoffDropX, HandoffDropY = assignment.HandoffDropY, HandoffDropZ = assignment.HandoffDropZ, HoldRoomKey = assignment.HoldRoomKey, ExpectedClosetCount = assignment.ExpectedClosetCount, HoldDurationGameMinutes = assignment.HoldDurationGameMinutes, RewardMultiplier = assignment.RewardMultiplier };
    private static RoomWithNoNameProgressDto ToDto(Release1RoomWithNoNameProgress progress) => new() { MissionKey = progress.MissionKey, Attempt = progress.Attempt, Staged = progress.Staged, Custody = progress.Custody, Stowed = progress.Stowed, HoldSatisfied = progress.HoldSatisfied, StowedAtGameMinutes = progress.StowedAtGameMinutes, HoldingClosetGuid = progress.HoldingClosetGuid, MissingSincePassGameMinutes = progress.MissingSincePassGameMinutes };
    private static ShortNoticeAssignmentDto ToDto(Release1ShortNoticeAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, ProductId = assignment.ProductId, ProductName = assignment.ProductName, PackagingId = assignment.PackagingId, PackagingName = assignment.PackagingName, RequiredQuantity = assignment.RequiredQuantity, HandoffDropGuid = assignment.HandoffDropGuid, HandoffDropName = assignment.HandoffDropName, HandoffDropDescription = assignment.HandoffDropDescription, HandoffDropX = assignment.HandoffDropX, HandoffDropY = assignment.HandoffDropY, HandoffDropZ = assignment.HandoffDropZ, DeadlineGameMinutes = assignment.DeadlineGameMinutes, RewardMultiplier = assignment.RewardMultiplier, ValueConvention = assignment.ValueConvention };
    private static ShortNoticeProgressDto ToDto(Release1ShortNoticeProgress progress) => new() { MissionKey = progress.MissionKey, Attempt = progress.Attempt, ObservedQuantity = progress.ObservedQuantity, LastShortfallNoticed = progress.LastShortfallNoticed, SpreadNoticed = progress.SpreadNoticed };
    private static KeepTheLightsOffAssignmentDto ToDto(Release1KeepTheLightsOffAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, ExpectedOwnedPropertyCount = assignment.ExpectedOwnedPropertyCount, WindowDurationGameMinutes = assignment.WindowDurationGameMinutes, DeadlineGameMinutes = assignment.DeadlineGameMinutes };
    private static KeepTheLightsOffProgressDto ToDto(Release1KeepTheLightsOffProgress progress) => new() { MissionKey = progress.MissionKey, Attempt = progress.Attempt, ClearConfirmedAtGameMinutes = progress.ClearConfirmedAtGameMinutes, BreachSincePassGameMinutes = progress.BreachSincePassGameMinutes };
    private static TheEnvelopeAssignmentDto ToDto(Release1TheEnvelopeAssignment assignment) => new() { MissionKey = assignment.MissionKey, Attempt = assignment.Attempt, Mode = assignment.Mode, AuthorizationCorrelationId = assignment.AuthorizationCorrelationId, AmountWholeDollars = assignment.AmountWholeDollars, HoldRoomKey = assignment.HoldRoomKey, ExpectedClosetCount = assignment.ExpectedClosetCount, DeadlineGameMinutes = assignment.DeadlineGameMinutes };
    private static TheEnvelopeProgressDto ToDto(Release1TheEnvelopeProgress progress) => new() { MissionKey = progress.MissionKey, Attempt = progress.Attempt, ObservedBalance = progress.ObservedBalance, LastShortfallNoticed = progress.LastShortfallNoticed, SpreadNoticed = progress.SpreadNoticed };
    private static ChiefRecordDto ToDto(Release1ChiefRecord record) => new()
    {
        Adopted = record.Adopted, AdoptedTier = record.AdoptedTier, AdoptedKnownOffender = record.AdoptedKnownOffender,
        DemandRound = record.DemandRound, State = record.State, WatchLinesSent = record.WatchLinesSent,
        LockdownAnnouncements = record.LockdownAnnouncements, LockdownEngaged = record.LockdownEngaged,
        PaidLifts = record.PaidLifts, CooledLifts = record.CooledLifts, DeclineRepliesSent = record.DeclineRepliesSent,
        PaymentBlockedNotices = record.PaymentBlockedNotices, LastShortfallNoticed = record.LastShortfallNoticed,
        AcceptedLogicalCorrelations = record.AcceptedLogicalCorrelations.ToArray(), NativeEffectIds = record.NativeEffectIds.ToArray(),
        Revision = record.Revision
    };
    // ChiefRecord is passed as a constructor argument, not through the trailing object initializer:
    // the constructor's own Validate() call runs before any object-initializer property is applied,
    // and the Chief native-effect cross-check inside Validate() needs ChiefRecord already set at
    // that point whenever NativeEffects carries a Chief entry (a v11+ document that paid at least
    // once always does).
    private static Release1StoryState FromDto(StoryDto dto, int version) => new(dto.PlayerId!, dto.Standing, dto.RelationshipState, dto.Release1Recognized, dto.IntroLogicalCorrelationIds!, dto.RecognitionLogicalCorrelationIds!, dto.Missions!.Select(FromDto).ToArray(), dto.NativeEffects!.Select(FromDto).ToArray(), dto.Revision,
        ChiefRecord: version >= 11 && dto.ChiefRecord is not null ? FromDto(dto.ChiefRecord) : null)
    { PhonePresentationAttempts = dto.PhonePresentationAttempts?.Select(FromDto).ToArray() ?? Array.Empty<Release1PhonePresentationAttempt>(), SmallCourtesyAssignments = dto.SmallCourtesyAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1SmallCourtesyAssignment>(), PresentationReceipts = dto.PresentationReceipts?.Select(FromDto).ToArray() ?? Array.Empty<Release1PresentationReceipt>(), WrongAddressAssignments = dto.WrongAddressAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1WrongAddressAssignment>(), WrongAddressProgress = dto.WrongAddressProgress?.Select(FromDto).ToArray() ?? Array.Empty<Release1WrongAddressProgress>(),
        RoomWithNoNameAssignments = dto.RoomWithNoNameAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1RoomWithNoNameAssignment>(),
        RoomWithNoNameProgress = dto.RoomWithNoNameProgress?.Select(FromDto).ToArray() ?? Array.Empty<Release1RoomWithNoNameProgress>(),
        ShortNoticeAssignments = dto.ShortNoticeAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1ShortNoticeAssignment>(),
        ShortNoticeProgress = dto.ShortNoticeProgress?.Select(FromDto).ToArray() ?? Array.Empty<Release1ShortNoticeProgress>(),
        // A v9 document's Keep the Lights Off assignment describes the retired product condition and
        // cannot be expressed in the v10 shape at all, so both collections normalize to empty rather
        // than being half translated. The mission record, its attempt, its Standing and its accepted
        // correlations are all untouched: a Satisfied attempt stays Satisfied and renders through the
        // assignment free path, and an attempt still in flight has its assignment re frozen on the next
        // convergence pass by Release1KeepTheLightsOffMissionService, with no story transition.
        KeepTheLightsOffAssignments = version >= 10
            ? dto.KeepTheLightsOffAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1KeepTheLightsOffAssignment>()
            : Array.Empty<Release1KeepTheLightsOffAssignment>(),
        KeepTheLightsOffProgress = version >= 10
            ? dto.KeepTheLightsOffProgress?.Select(FromDto).ToArray() ?? Array.Empty<Release1KeepTheLightsOffProgress>()
            : Array.Empty<Release1KeepTheLightsOffProgress>(),
        TheEnvelopeAssignments = dto.TheEnvelopeAssignments?.Select(FromDto).ToArray() ?? Array.Empty<Release1TheEnvelopeAssignment>(),
        TheEnvelopeProgress = dto.TheEnvelopeProgress?.Select(FromDto).ToArray() ?? Array.Empty<Release1TheEnvelopeProgress>() };
    private static Release1MissionRecord FromDto(MissionDto dto) => new(dto.MissionKey!, dto.State, dto.Attempt, dto.TermsVersion, dto.AcceptedGameTimeHours, dto.DeadlineGameTimeHours, dto.LastOutcome, dto.StandingPenaltyApplied, dto.PenaltyReceiptIds!, dto.MakeGoodFailures, dto.RecoveryMode, dto.RewardAuthorizationReceiptId, dto.QuietConditionAwarded, dto.NativeEffectIds!, dto.PresentationCorrelationIds!, dto.AcceptedLogicalCorrelations!, dto.NativePresentationRef, dto.Revision);
    private static Release1NativeEffectJournalEntry FromDto(EffectDto dto) => new(dto.EffectId!, dto.MissionKey!, dto.Attempt, dto.EffectKind!, dto.SourceIdentity!, dto.DestinationIdentity!, dto.AmountOrCargoIdentity!, dto.Phase, dto.NativeReceiptId, dto.PreparedStoryRevision, dto.CommittedStoryCorrelationId, dto.AuthorizedStoryCorrelationId, dto.ExecutionBlocked, dto.AuthorizedMissionRevision);
    private static Release1PhonePresentationAttempt FromDto(Release1PhonePresentationAttemptDto dto) => new(dto.CorrelationId!, dto.MissionKey!, dto.Attempt, dto.Role!, dto.RequiredPriorCorrelationId, dto.State, dto.Revision);
    private static Release1SmallCourtesyAssignment FromDto(SmallCourtesyAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.ProductId!, dto.ProductName!, dto.SelectionAskingPrice, dto.PackagingId!, dto.PackagingName!, dto.DeadDropGuid!, dto.DeadDropName!, dto.DeadDropDescription!, dto.DeadDropX, dto.DeadDropY, dto.DeadDropZ, dto.RewardMultiplier);
    private static Release1PresentationReceipt FromDto(PresentationReceiptDto dto) => new(dto.CorrelationId!, dto.Revision);
    private static Release1WrongAddressAssignment FromDto(WrongAddressAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.ProductId!, dto.ProductName!, dto.PackagingId!, dto.PackagingName!, dto.PackageQuantity, dto.SourceDropGuid!, dto.SourceDropName!, dto.SourceDropDescription!, dto.SourceDropX, dto.SourceDropY, dto.SourceDropZ, dto.HandoffDropGuid!, dto.HandoffDropName!, dto.HandoffDropDescription!, dto.HandoffDropX, dto.HandoffDropY, dto.HandoffDropZ, dto.RewardMultiplier);
    private static Release1WrongAddressProgress FromDto(WrongAddressProgressDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Staged, dto.Custody);
    private static Release1RoomWithNoNameAssignment FromDto(RoomWithNoNameAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.ProductId!, dto.ProductName!, dto.PackagingId!, dto.PackagingName!, dto.PackageQuantity, dto.SourceDropGuid!, dto.SourceDropName!, dto.SourceDropDescription!, dto.SourceDropX, dto.SourceDropY, dto.SourceDropZ, dto.HandoffDropGuid!, dto.HandoffDropName!, dto.HandoffDropDescription!, dto.HandoffDropX, dto.HandoffDropY, dto.HandoffDropZ, dto.HoldRoomKey!, dto.ExpectedClosetCount, dto.HoldDurationGameMinutes, dto.RewardMultiplier);
    private static Release1RoomWithNoNameProgress FromDto(RoomWithNoNameProgressDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Staged, dto.Custody, dto.Stowed, dto.HoldSatisfied, dto.StowedAtGameMinutes, dto.HoldingClosetGuid, dto.MissingSincePassGameMinutes);
    private static Release1ShortNoticeAssignment FromDto(ShortNoticeAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.ProductId!, dto.ProductName!, dto.PackagingId!, dto.PackagingName!, dto.RequiredQuantity, dto.HandoffDropGuid!, dto.HandoffDropName!, dto.HandoffDropDescription!, dto.HandoffDropX, dto.HandoffDropY, dto.HandoffDropZ, dto.DeadlineGameMinutes, dto.RewardMultiplier, dto.ValueConvention);
    private static Release1ShortNoticeProgress FromDto(ShortNoticeProgressDto dto) => new(dto.MissionKey!, dto.Attempt, dto.ObservedQuantity, dto.LastShortfallNoticed, dto.SpreadNoticed);
    private static Release1KeepTheLightsOffAssignment FromDto(KeepTheLightsOffAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.ExpectedOwnedPropertyCount, dto.WindowDurationGameMinutes, dto.DeadlineGameMinutes);
    private static Release1KeepTheLightsOffProgress FromDto(KeepTheLightsOffProgressDto dto) => new(dto.MissionKey!, dto.Attempt, dto.ClearConfirmedAtGameMinutes, dto.BreachSincePassGameMinutes);
    private static Release1TheEnvelopeAssignment FromDto(TheEnvelopeAssignmentDto dto) => new(dto.MissionKey!, dto.Attempt, dto.Mode, dto.AuthorizationCorrelationId!, dto.AmountWholeDollars, dto.HoldRoomKey!, dto.ExpectedClosetCount, dto.DeadlineGameMinutes);
    private static Release1TheEnvelopeProgress FromDto(TheEnvelopeProgressDto dto) => new(dto.MissionKey!, dto.Attempt, dto.ObservedBalance, dto.LastShortfallNoticed, dto.SpreadNoticed);
    private static Release1ChiefRecord FromDto(ChiefRecordDto dto) => new(
        dto.Adopted, dto.AdoptedTier, dto.AdoptedKnownOffender, dto.DemandRound, dto.State, dto.WatchLinesSent,
        dto.LockdownAnnouncements, dto.LockdownEngaged, dto.PaidLifts, dto.CooledLifts, dto.DeclineRepliesSent,
        dto.PaymentBlockedNotices, dto.LastShortfallNoticed,
        dto.AcceptedLogicalCorrelations ?? Array.Empty<string>(), dto.NativeEffectIds ?? Array.Empty<string>(), dto.Revision);

    private sealed class EnvelopeDto { public int SchemaVersion { get; set; } public StoryDto? Story { get; set; } }
    private sealed class StoryDto { public string? PlayerId { get; set; } public int Standing { get; set; } public Release1RelationshipState RelationshipState { get; set; } public bool Release1Recognized { get; set; } public string[]? IntroLogicalCorrelationIds { get; set; } public string[]? RecognitionLogicalCorrelationIds { get; set; } public MissionDto[]? Missions { get; set; } public EffectDto[]? NativeEffects { get; set; } public Release1PhonePresentationAttemptDto[]? PhonePresentationAttempts { get; set; } public SmallCourtesyAssignmentDto[]? SmallCourtesyAssignments { get; set; } public PresentationReceiptDto[]? PresentationReceipts { get; set; } public WrongAddressAssignmentDto[]? WrongAddressAssignments { get; set; } public WrongAddressProgressDto[]? WrongAddressProgress { get; set; } public RoomWithNoNameAssignmentDto[]? RoomWithNoNameAssignments { get; set; } public RoomWithNoNameProgressDto[]? RoomWithNoNameProgress { get; set; } public ShortNoticeAssignmentDto[]? ShortNoticeAssignments { get; set; } public ShortNoticeProgressDto[]? ShortNoticeProgress { get; set; } public KeepTheLightsOffAssignmentDto[]? KeepTheLightsOffAssignments { get; set; } public KeepTheLightsOffProgressDto[]? KeepTheLightsOffProgress { get; set; } public TheEnvelopeAssignmentDto[]? TheEnvelopeAssignments { get; set; } public TheEnvelopeProgressDto[]? TheEnvelopeProgress { get; set; } public ChiefRecordDto? ChiefRecord { get; set; } public long Revision { get; set; } }
    private sealed class MissionDto { public string? MissionKey { get; set; } public Release1MissionState State { get; set; } public int Attempt { get; set; } public string? TermsVersion { get; set; } public double? AcceptedGameTimeHours { get; set; } public double? DeadlineGameTimeHours { get; set; } public Release1MissionOutcome LastOutcome { get; set; } public int StandingPenaltyApplied { get; set; } public string[]? PenaltyReceiptIds { get; set; } public int MakeGoodFailures { get; set; } public Release1RecoveryMode RecoveryMode { get; set; } public string? RewardAuthorizationReceiptId { get; set; } public bool QuietConditionAwarded { get; set; } public string[]? NativeEffectIds { get; set; } public string[]? PresentationCorrelationIds { get; set; } public string[]? AcceptedLogicalCorrelations { get; set; } public string? NativePresentationRef { get; set; } public long Revision { get; set; } }
    private sealed class Release1PhonePresentationAttemptDto { public string? CorrelationId { get; set; } public string? MissionKey { get; set; } public int Attempt { get; set; } public string? Role { get; set; } public string? RequiredPriorCorrelationId { get; set; } public Release1PhonePresentationAttemptState State { get; set; } public long Revision { get; set; } }
    private sealed class SmallCourtesyAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1SmallCourtesyAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public string? ProductId { get; set; } public string? ProductName { get; set; } public double SelectionAskingPrice { get; set; } public string? PackagingId { get; set; } public string? PackagingName { get; set; } public string? DeadDropGuid { get; set; } public string? DeadDropName { get; set; } public string? DeadDropDescription { get; set; } public double DeadDropX { get; set; } public double DeadDropY { get; set; } public double DeadDropZ { get; set; } public double RewardMultiplier { get; set; } }
    private sealed class PresentationReceiptDto { public string? CorrelationId { get; set; } public long Revision { get; set; } }
    private sealed class WrongAddressAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1WrongAddressAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public string? ProductId { get; set; } public string? ProductName { get; set; } public string? PackagingId { get; set; } public string? PackagingName { get; set; } public int PackageQuantity { get; set; } public string? SourceDropGuid { get; set; } public string? SourceDropName { get; set; } public string? SourceDropDescription { get; set; } public double SourceDropX { get; set; } public double SourceDropY { get; set; } public double SourceDropZ { get; set; } public string? HandoffDropGuid { get; set; } public string? HandoffDropName { get; set; } public string? HandoffDropDescription { get; set; } public double HandoffDropX { get; set; } public double HandoffDropY { get; set; } public double HandoffDropZ { get; set; } public double RewardMultiplier { get; set; } }
    private sealed class WrongAddressProgressDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public bool Staged { get; set; } public bool Custody { get; set; } }
    private sealed class RoomWithNoNameAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1RoomWithNoNameAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public string? ProductId { get; set; } public string? ProductName { get; set; } public string? PackagingId { get; set; } public string? PackagingName { get; set; } public int PackageQuantity { get; set; } public string? SourceDropGuid { get; set; } public string? SourceDropName { get; set; } public string? SourceDropDescription { get; set; } public double SourceDropX { get; set; } public double SourceDropY { get; set; } public double SourceDropZ { get; set; } public string? HandoffDropGuid { get; set; } public string? HandoffDropName { get; set; } public string? HandoffDropDescription { get; set; } public double HandoffDropX { get; set; } public double HandoffDropY { get; set; } public double HandoffDropZ { get; set; } public string? HoldRoomKey { get; set; } public int ExpectedClosetCount { get; set; } public double HoldDurationGameMinutes { get; set; } public double RewardMultiplier { get; set; } }
    private sealed class RoomWithNoNameProgressDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public bool Staged { get; set; } public bool Custody { get; set; } public bool Stowed { get; set; } public bool HoldSatisfied { get; set; } public double? StowedAtGameMinutes { get; set; } public string? HoldingClosetGuid { get; set; } public double? MissingSincePassGameMinutes { get; set; } }
    private sealed class ShortNoticeAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1ShortNoticeAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public string? ProductId { get; set; } public string? ProductName { get; set; } public string? PackagingId { get; set; } public string? PackagingName { get; set; } public int RequiredQuantity { get; set; } public string? HandoffDropGuid { get; set; } public string? HandoffDropName { get; set; } public string? HandoffDropDescription { get; set; } public double HandoffDropX { get; set; } public double HandoffDropY { get; set; } public double HandoffDropZ { get; set; } public double? DeadlineGameMinutes { get; set; } public double RewardMultiplier { get; set; } public Release1ShortNoticeValueConvention ValueConvention { get; set; } }
    private sealed class ShortNoticeProgressDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public int ObservedQuantity { get; set; } public int? LastShortfallNoticed { get; set; } public bool SpreadNoticed { get; set; } }
    private sealed class KeepTheLightsOffAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1KeepTheLightsOffAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public int ExpectedOwnedPropertyCount { get; set; } public double WindowDurationGameMinutes { get; set; } public double? DeadlineGameMinutes { get; set; } }
    private sealed class KeepTheLightsOffProgressDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public double? ClearConfirmedAtGameMinutes { get; set; } public double? BreachSincePassGameMinutes { get; set; } }
    private sealed class TheEnvelopeAssignmentDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public Release1TheEnvelopeAssignmentMode Mode { get; set; } public string? AuthorizationCorrelationId { get; set; } public double AmountWholeDollars { get; set; } public string? HoldRoomKey { get; set; } public int ExpectedClosetCount { get; set; } public double? DeadlineGameMinutes { get; set; } }
    private sealed class TheEnvelopeProgressDto { public string? MissionKey { get; set; } public int Attempt { get; set; } public double ObservedBalance { get; set; } public double? LastShortfallNoticed { get; set; } public bool SpreadNoticed { get; set; } }
    private sealed class ChiefRecordDto { public bool Adopted { get; set; } public LocalPressureTier AdoptedTier { get; set; } public bool AdoptedKnownOffender { get; set; } public int DemandRound { get; set; } public Release1ChiefState State { get; set; } public int WatchLinesSent { get; set; } public int LockdownAnnouncements { get; set; } public bool LockdownEngaged { get; set; } public int PaidLifts { get; set; } public int CooledLifts { get; set; } public int DeclineRepliesSent { get; set; } public int PaymentBlockedNotices { get; set; } public double? LastShortfallNoticed { get; set; } public string[]? AcceptedLogicalCorrelations { get; set; } public string[]? NativeEffectIds { get; set; } public long Revision { get; set; } }
    private sealed class EffectDto { public string? EffectId { get; set; } public string? MissionKey { get; set; } public int Attempt { get; set; } public string? EffectKind { get; set; } public string? SourceIdentity { get; set; } public string? DestinationIdentity { get; set; } public string? AmountOrCargoIdentity { get; set; } public Release1NativeEffectPhase Phase { get; set; } public string? NativeReceiptId { get; set; } public long PreparedStoryRevision { get; set; } public string? CommittedStoryCorrelationId { get; set; } public string? AuthorizedStoryCorrelationId { get; set; } public bool ExecutionBlocked { get; set; } public long AuthorizedMissionRevision { get; set; } }
}
