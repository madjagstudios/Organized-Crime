using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeStoryTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void TheEnvelopeAssignments_and_progress_default_to_empty_on_a_fresh_state()
    {
        var story = Story();

        Assert.Empty(story.TheEnvelopeAssignments);
        Assert.Empty(story.TheEnvelopeProgress);
    }

    [Fact]
    public void Validate_rejects_a_null_assignment_and_a_duplicate_mission_attempt_pair()
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "authorize-1"));

        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(assignment) with
        {
            TheEnvelopeAssignments = new Release1TheEnvelopeAssignment[] { assignment, null! }
        }).Validate());

        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(assignment) with
        {
            TheEnvelopeAssignments = new[] { assignment, assignment }
        }).Validate());
    }

    [Fact]
    public void Validate_rejects_an_assignment_whose_authorization_correlation_does_not_match_story_identity()
    {
        var otherPlayerCorrelation = Release1LogicalCorrelation.Create(
            "other-player", Release1MissionCatalog.TheEnvelope, 1, Release1TransitionKind.MissionAccepted, "authorize-1").Value;
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1, otherPlayerCorrelation);

        Assert.Throws<ArgumentException>(() => StoryWithAssignments(assignment).Validate());
    }

    [Fact]
    public void Validate_rejects_an_assignment_not_covered_by_its_mission_attempts_accepted_correlations()
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "authorize-1"));
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        missions[index] = missions[index] with { Attempt = 1 };
        var broken = story with { Missions = missions, TheEnvelopeAssignments = new[] { assignment } };

        Assert.Throws<ArgumentException>(() => broken.Validate());
    }

    [Fact]
    public void Validate_rejects_progress_with_no_accepted_assignment_for_its_attempt()
    {
        var progress = Release1TheEnvelopeProgress.Fresh(1);

        Assert.Throws<ArgumentException>(() => (Story() with { TheEnvelopeProgress = new[] { progress } }).Validate());
    }

    [Fact]
    public void ValueEquals_and_GetHashCode_cover_TheEnvelopeAssignments_and_TheEnvelopeProgress()
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MissionAccepted, "authorize-1"));
        var progress = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 500d, 1500d, true);
        var story = StoryWithAssignments(assignment) with { TheEnvelopeProgress = new[] { progress } };
        var clone = StoryWithAssignments(assignment) with { TheEnvelopeProgress = new[] { progress } };

        Assert.True(story.ValueEquals(clone));
        Assert.Equal(story.GetHashCode(), clone.GetHashCode());

        var differentProgress = story with { TheEnvelopeProgress = new[] { progress with { ObservedBalance = 1000d } } };
        Assert.False(story.ValueEquals(differentProgress));

        var differentCorrelation = Correlation(1, Release1TransitionKind.MissionAccepted, "authorize-2");
        var differentAssignment = StoryWithAssignments(assignment with { AuthorizationCorrelationId = differentCorrelation }) with { TheEnvelopeProgress = new[] { progress } };
        Assert.False(story.ValueEquals(differentAssignment));
    }

    private static Release1TheEnvelopeAssignment Assignment(Release1TheEnvelopeAssignmentMode mode, int attempt, string authorization) =>
        new(
            Release1MissionCatalog.TheEnvelope,
            attempt,
            mode,
            authorization,
            Release1TheEnvelopeAssignment.AmountFor(mode),
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : Release1TheEnvelopeAssignment.TimedStageGameMinutes);

    private static string Correlation(int attempt, Release1TransitionKind transition, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.TheEnvelope, attempt, transition, receipt).Value;

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState StoryWithAssignments(params Release1TheEnvelopeAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        missions[index] = missions[index] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments.Select(assignment => assignment.AuthorizationCorrelationId).ToArray()
        };
        return story with { Missions = missions, TheEnvelopeAssignments = assignments };
    }
}
