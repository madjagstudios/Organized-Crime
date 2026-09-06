using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameStoryTests
{
    [Fact]
    public void The_schema_is_version_eleven_and_reads_versions_one_to_eleven()
    {
        // Re-pinned for OC-73 Task 3: OC-73 advanced the schema to v11 (the Chief record), which this
        // pre-existing OC-57-era test never followed. The v11 bump itself is out of this task's
        // narrower scope; only the stale literal here is updated, to the same current-version pattern
        // every other read-every-supported-version assertion in this suite already uses.
        Assert.Equal(11, Release1StorySaveCodec.CurrentSchemaVersion);
        foreach (var version in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
        {
            var json = $"{{\"schemaVersion\": {version}, \"story\": null}}";
            Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var result), result.Message);
            Assert.Equal(11, envelope!.SchemaVersion);
        }
    }

    [Fact]
    public void A_version_five_story_normalizes_to_empty_room_with_no_name_collections()
    {
        var story = Story();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var v5 = json
            .Replace($"\"schemaVersion\": {Release1StorySaveCodec.CurrentSchemaVersion}", "\"schemaVersion\": 5", StringComparison.Ordinal);
        var stripped = StripArrays(v5, "roomWithNoNameAssignments", "roomWithNoNameProgress");

        Assert.True(Release1StorySaveCodec.TryDeserialize(stripped, out var envelope, out var result), result.Message);
        Assert.Empty(envelope!.Story!.RoomWithNoNameAssignments);
        Assert.Empty(envelope.Story.RoomWithNoNameProgress);
    }

    [Fact]
    public void A_version_six_story_missing_the_new_arrays_is_refused()
    {
        var story = Story();
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var stripped = StripArrays(json, "roomWithNoNameAssignments");

        Assert.False(Release1StorySaveCodec.TryDeserialize(stripped, out _, out var result));
        Assert.Equal(Release1StoryCodecFailureReason.MissingRequiredField, result.Reason);
    }

    [Fact]
    public void An_assignment_and_its_progress_round_trip_at_version_six()
    {
        var story = StoryWithAcceptedRoomStage(out var assignment);
        var progress = new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, assignment.Attempt, true, true, true, false, 12_345d, "closet-a", 60d);
        story = story with { RoomWithNoNameProgress = new[] { progress } };
        story.Validate();

        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out var write), write.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);

        Assert.Equal(assignment, Assert.Single(envelope!.Story!.RoomWithNoNameAssignments));
        Assert.Equal(progress, Assert.Single(envelope.Story.RoomWithNoNameProgress));
    }

    [Fact]
    public void An_unknown_field_inside_the_new_arrays_is_refused()
    {
        var story = StoryWithAcceptedRoomStage(out _);
        Assert.True(Release1StorySaveCodec.TrySerialize(story, out var json, out _));
        var corrupted = json.Replace("\"holdRoomKey\"", "\"holdRoomKeyy\"", StringComparison.Ordinal);

        Assert.False(Release1StorySaveCodec.TryDeserialize(corrupted, out _, out var result));
        Assert.Equal(Release1StoryCodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Progress_without_an_accepted_assignment_for_its_attempt_is_refused()
    {
        var story = Story();

        var withProgress = story with
        {
            RoomWithNoNameProgress = new[]
            {
                new Release1RoomWithNoNameProgress(Release1MissionCatalog.RoomWithNoName, 1, true, false, false, false, null, null, null)
            }
        };
        Assert.Throws<ArgumentException>(withProgress.Validate);
    }

    [Fact]
    public void An_assignment_the_mission_never_authorized_is_refused()
    {
        var story = Story();
        var assignment = AssignmentFor(story.PlayerId, Release1RoomWithNoNameAssignmentMode.Primary, 1);

        var withAssignment = story with { RoomWithNoNameAssignments = new[] { assignment } };
        Assert.Throws<ArgumentException>(withAssignment.Validate);
    }

    private static string StripArrays(string json, params string[] fields)
    {
        var result = json;
        foreach (var field in fields)
        {
            var start = result.IndexOf($"\"{field}\":", StringComparison.Ordinal);
            if (start < 0) continue;
            var close = result.IndexOf(']', start);
            var end = close + 1;
            if (end < result.Length && result[end] == ',') end++;
            result = result.Remove(start, end - start);
        }
        return result;
    }

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted("player-one", Release1LogicalCorrelation.Create(
            "player-one", Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-r").Value);

    private static Release1RoomWithNoNameAssignment AssignmentFor(string playerId, Release1RoomWithNoNameAssignmentMode mode, int attempt)
    {
        var kind = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var correlation = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.RoomWithNoName, attempt, kind,
            $"room-with-no-name-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}").Value;
        return new(Release1MissionCatalog.RoomWithNoName, attempt, mode, correlation,
            "cocaine", "Cocaine", "brick", "Brick", 1,
            "drop-source", "Source Drop", "Behind the laundromat.", 1, 2, 3,
            "drop-handoff", "Handoff Drop", "Under the pier.", 4, 5, 6,
            "syndicate-hq", 9, 1_440d, 1.5d);
    }

    // A story whose Small Courtesy and Wrong Address missions are Satisfied and whose Room With No
    // Name mission has accepted a primary stage, which is the only shape the new assignment
    // validation admits.
    private static Release1StoryState StoryWithAcceptedRoomStage(out Release1RoomWithNoNameAssignment assignment)
    {
        var story = Story();
        story = Satisfy(story, Release1MissionCatalog.SmallCourtesy);
        story = Satisfy(story, Release1MissionCatalog.WrongAddress);
        assignment = AssignmentFor(story.PlayerId, Release1RoomWithNoNameAssignmentMode.Primary, 1);
        var accept = new Release1StoryCommand(
            Guid.NewGuid(), 1, story.PlayerId, Release1MissionCatalog.RoomWithNoName, 1,
            Release1TransitionKind.MissionAccepted, "room-with-no-name-primary-accept-v1-a1",
            assignment.AuthorizationCorrelationId, "room-with-no-name-v1", 100d, 172d);
        var result = Release1StoryTransitions.Apply(story, accept);
        Assert.True(result.Accepted, result.Message);
        return result.State! with { RoomWithNoNameAssignments = new[] { assignment } };
    }

    private static Release1StoryState Satisfy(Release1StoryState story, string missionKey)
    {
        var index = Release1MissionCatalog.IndexOf(missionKey);
        var mission = story.Missions[index];
        var acceptReceipt = $"{missionKey}-accept";
        var accept = new Release1StoryCommand(
            Guid.NewGuid(), 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted,
            acceptReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
            "terms-v1");
        var accepted = Release1StoryTransitions.Apply(story, accept);
        Assert.True(accepted.Accepted, accepted.Message);
        var activateReceipt = $"{missionKey}-activate";
        var activate = new Release1StoryCommand(
            Guid.NewGuid(), 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated,
            activateReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
        var activated = Release1StoryTransitions.Apply(accepted.State!, activate);
        Assert.True(activated.Accepted, activated.Message);
        var completeReceipt = $"{missionKey}-complete";
        var complete = new Release1StoryCommand(
            Guid.NewGuid(), 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted,
            completeReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
            CompletionTiming: Release1CompletionTiming.OnTime,
            RewardAuthorizationReceiptId: $"{missionKey}-reward");
        var completed = Release1StoryTransitions.Apply(activated.State!, complete);
        Assert.True(completed.Accepted, completed.Message);
        return completed.State!;
    }
}
