using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeAssignmentTests
{
    private const string PlayerId = "player-one";

    [Fact]
    public void Assignment_requires_twenty_thousand_for_primary_ten_thousand_for_make_good_and_five_thousand_for_recovery()
    {
        var primary = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1);
        Assert.Equal(20000d, primary.AmountWholeDollars);
        var makeGood = Assignment(Release1TheEnvelopeAssignmentMode.MakeGood, 1);
        Assert.Equal(10000d, makeGood.AmountWholeDollars);
        var recovery = Assignment(Release1TheEnvelopeAssignmentMode.Recovery, 1);
        Assert.Equal(5000d, recovery.AmountWholeDollars);

        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { AmountWholeDollars = 10000d });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { AmountWholeDollars = 5000d });
        Assert.Throws<ArgumentOutOfRangeException>(() => makeGood with { AmountWholeDollars = 20000d });
        Assert.Throws<ArgumentOutOfRangeException>(() => makeGood with { AmountWholeDollars = 5000d });
        Assert.Throws<ArgumentOutOfRangeException>(() => recovery with { AmountWholeDollars = 20000d });
        Assert.Throws<ArgumentOutOfRangeException>(() => recovery with { AmountWholeDollars = 10000d });
    }

    [Fact]
    public void Assignment_requires_fourteen_hundred_forty_game_minutes_for_timed_stages_and_none_for_recovery()
    {
        var primary = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1);
        Assert.Equal(1440d, primary.DeadlineGameMinutes);

        var makeGood = Assignment(Release1TheEnvelopeAssignmentMode.MakeGood, 1);
        Assert.Equal(1440d, makeGood.DeadlineGameMinutes);

        var recovery = Assignment(Release1TheEnvelopeAssignmentMode.Recovery, 1);
        Assert.Null(recovery.DeadlineGameMinutes);

        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = 60d });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = null });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = double.NaN });
        Assert.Throws<ArgumentException>(() => recovery with { DeadlineGameMinutes = 1440d });
    }

    [Theory]
    [InlineData(Release1TheEnvelopeAssignmentMode.Primary, Release1TransitionKind.MakeGoodAccepted)]
    [InlineData(Release1TheEnvelopeAssignmentMode.MakeGood, Release1TransitionKind.MissionAccepted)]
    [InlineData(Release1TheEnvelopeAssignmentMode.Recovery, Release1TransitionKind.MissionAccepted)]
    public void Assignment_requires_the_authorization_correlation_to_match_mission_attempt_and_mode(
        Release1TheEnvelopeAssignmentMode mode, Release1TransitionKind wrongKind)
    {
        var wrong = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.TheEnvelope, 1, wrongKind, "r").Value;

        Assert.Throws<ArgumentException>(() => Assignment(mode, 1) with { AuthorizationCorrelationId = wrong });
    }

    [Fact]
    public void Progress_rejects_a_negative_observed_balance_and_a_shortfall_at_or_below_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, -1d, null, false).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 0d, 0d, false).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 0d, -1d, false).Validate());
    }

    [Fact]
    public void An_assignment_carries_the_room_key_and_nine_closets_and_no_drop()
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1);

        Assert.Equal("syndicate-hq", assignment.HoldRoomKey);
        Assert.Equal(9, assignment.ExpectedClosetCount);
        Assert.Equal(20000d, assignment.AmountWholeDollars);
        Assert.Equal(1440d, assignment.DeadlineGameMinutes);
    }

    [Fact]
    public void A_room_key_that_is_not_the_syndicate_hq_is_rejected()
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1);

        Assert.Throws<ArgumentException>(() => assignment with { HoldRoomKey = "some-other-room" });
        Assert.Throws<ArgumentException>(() => assignment with { HoldRoomKey = "" });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(10)]
    public void A_closet_count_that_is_not_nine_is_rejected(int closetCount)
    {
        var assignment = Assignment(Release1TheEnvelopeAssignmentMode.Primary, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { ExpectedClosetCount = closetCount });
    }

    [Fact]
    public void Recovery_carries_no_deadline_and_five_thousand()
    {
        var recovery = Assignment(Release1TheEnvelopeAssignmentMode.Recovery, 1);

        Assert.Null(recovery.DeadlineGameMinutes);
        Assert.Equal(5000d, recovery.AmountWholeDollars);
        Assert.Equal("syndicate-hq", recovery.HoldRoomKey);
        Assert.Equal(9, recovery.ExpectedClosetCount);
    }

    [Fact]
    public void The_selector_needs_no_world_read_and_selects_every_mode()
    {
        foreach (var mode in new[]
                 {
                     Release1TheEnvelopeAssignmentMode.Primary,
                     Release1TheEnvelopeAssignmentMode.MakeGood,
                     Release1TheEnvelopeAssignmentMode.Recovery
                 })
        {
            var correlation = Correlation(mode, 1);

            Assert.True(Release1TheEnvelopeAssignmentSelector.TrySelect(
                mode, 1, correlation, out var assignment, out var status));

            Assert.Equal(Release1TheEnvelopeSelectionStatus.Selected, status);
            Assert.NotNull(assignment);
            Assert.Equal(correlation, assignment!.AuthorizationCorrelationId);
            Assert.True(Release1LogicalCorrelation.TryParse(assignment.AuthorizationCorrelationId, out var parsed));
            Assert.Equal(Release1MissionCatalog.TheEnvelope, parsed.MissionKey);
            Assert.Equal(1, parsed.Attempt);
            Assert.Equal(mode, assignment.Mode);
        }
    }

    [Fact]
    public void The_selector_rejects_a_correlation_that_does_not_match_the_mode_and_attempt()
    {
        var wrongAttemptCorrelation = Correlation(Release1TheEnvelopeAssignmentMode.Primary, 2);

        Assert.False(Release1TheEnvelopeAssignmentSelector.TrySelect(
            Release1TheEnvelopeAssignmentMode.Primary, 1, wrongAttemptCorrelation,
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1TheEnvelopeSelectionStatus.InvalidInput, status);
    }

    [Fact]
    public void The_assignment_type_has_no_drop_member()
    {
        var properties = typeof(Release1TheEnvelopeAssignment).GetProperties();

        Assert.DoesNotContain(properties, property => property.Name.StartsWith("HandoffDrop", StringComparison.Ordinal));
    }

    private static string Correlation(Release1TheEnvelopeAssignmentMode mode, int attempt) =>
        Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.TheEnvelope,
            attempt,
            mode switch
            {
                Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                _ => Release1TransitionKind.RecoveryAccepted
            },
            $"the-envelope-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}").Value;

    private static Release1TheEnvelopeAssignment Assignment(Release1TheEnvelopeAssignmentMode mode, int attempt) =>
        new(Release1MissionCatalog.TheEnvelope, attempt, mode, Correlation(mode, attempt),
            Release1TheEnvelopeAssignment.AmountFor(mode),
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : Release1TheEnvelopeAssignment.TimedStageGameMinutes);
}
