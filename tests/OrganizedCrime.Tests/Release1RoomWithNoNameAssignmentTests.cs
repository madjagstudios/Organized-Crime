using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameAssignmentTests
{
    private const string PlayerId = "player-one";

    [Fact]
    public void A_primary_assignment_is_frozen_with_the_room_contract_values()
    {
        var assignment = Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick");

        assignment.Validate();

        Assert.Equal(Release1MissionCatalog.RoomWithNoName, assignment.MissionKey);
        Assert.Equal("syndicate-hq", assignment.HoldRoomKey);
        Assert.Equal(9, assignment.ExpectedClosetCount);
        Assert.Equal(1_440d, assignment.HoldDurationGameMinutes);
        Assert.Equal(1.5d, assignment.RewardMultiplier);
        Assert.Equal(1, assignment.PackageQuantity);
    }

    [Theory]
    [InlineData("motel-room")]
    [InlineData("")]
    public void A_hold_room_key_other_than_the_hq_room_is_refused(string key)
    {
        Assert.Throws<ArgumentException>(() => Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick") with { HoldRoomKey = key });
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(0)]
    public void A_closet_count_other_than_nine_is_refused(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick") with { ExpectedClosetCount = count });
    }

    [Theory]
    [InlineData(720d)]
    [InlineData(1_441d)]
    [InlineData(double.NaN)]
    public void A_hold_duration_other_than_one_in_game_day_is_refused(double minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick") with { HoldDurationGameMinutes = minutes });
    }

    [Theory]
    [InlineData(1.25d)]
    [InlineData(2d)]
    public void A_reward_multiplier_other_than_one_and_a_half_is_refused(double multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick") with { RewardMultiplier = multiplier });
    }

    [Fact]
    public void The_source_and_handoff_drops_must_differ()
    {
        Assert.Throws<ArgumentException>(() => Assignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, "brick", "Brick") with { HandoffDropGuid = "drop-source" });
    }

    [Theory]
    [InlineData(Release1RoomWithNoNameAssignmentMode.Primary, Release1TransitionKind.MakeGoodAccepted)]
    [InlineData(Release1RoomWithNoNameAssignmentMode.MakeGood, Release1TransitionKind.MissionAccepted)]
    [InlineData(Release1RoomWithNoNameAssignmentMode.Recovery, Release1TransitionKind.MissionAccepted)]
    public void The_authorization_correlation_must_match_the_mode(
        Release1RoomWithNoNameAssignmentMode mode, Release1TransitionKind wrongKind)
    {
        var wrong = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.RoomWithNoName, 1, wrongKind, "r").Value;

        Assert.Throws<ArgumentException>(() => Assignment(mode, 1, "brick", "Brick") with { AuthorizationCorrelationId = wrong });
    }

    [Fact]
    public void The_selector_chooses_two_different_drops_from_the_shared_rule()
    {
        var correlation = Correlation(Release1RoomWithNoNameAssignmentMode.Primary, 1);

        Assert.True(Release1RoomWithNoNameAssignmentSelector.TrySelect(
            Products(), Drops("drop-a", "drop-b", "drop-c", "drop-d"),
            Release1RoomWithNoNameAssignmentMode.Primary, 1, correlation, "brick", "Brick",
            out var assignment, out var status));

        Assert.Equal(Release1RoomWithNoNameSelectionStatus.Selected, status);
        Assert.NotEqual(assignment!.SourceDropGuid, assignment.HandoffDropGuid);
        Assert.True(Release1DeadDropSelection.TryChoosePair(Drops("drop-a", "drop-b", "drop-c", "drop-d"), correlation, out var source, out var handoff));
        Assert.Equal(source!.Guid, assignment.SourceDropGuid);
        Assert.Equal(handoff!.Guid, assignment.HandoffDropGuid);
    }

    [Fact]
    public void The_selector_picks_the_highest_asking_price_with_an_ordinal_tie_break()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("meth", "Meth", 900d, true),
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("aaa", "Aaa", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("gold", "Gold", 5_000d, false)
        };

        Assert.True(Release1RoomWithNoNameAssignmentSelector.TrySelect(
            products, Drops("drop-a", "drop-b"), Release1RoomWithNoNameAssignmentMode.Primary, 1,
            Correlation(Release1RoomWithNoNameAssignmentMode.Primary, 1), "brick", "Brick",
            out var assignment, out _));

        Assert.Equal("aaa", assignment!.ProductId);
    }

    [Fact]
    public void Fewer_than_two_empty_drops_reports_the_typed_refusal_and_selects_nothing()
    {
        Assert.False(Release1RoomWithNoNameAssignmentSelector.TrySelect(
            Products(), Drops("drop-a"), Release1RoomWithNoNameAssignmentMode.Primary, 1,
            Correlation(Release1RoomWithNoNameAssignmentMode.Primary, 1), "brick", "Brick",
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1RoomWithNoNameSelectionStatus.InsufficientEmptyDeadDrops, status);
    }

    [Fact]
    public void No_discovered_product_reports_the_typed_refusal()
    {
        var products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, false) };

        Assert.False(Release1RoomWithNoNameAssignmentSelector.TrySelect(
            products, Drops("drop-a", "drop-b"), Release1RoomWithNoNameAssignmentMode.Primary, 1,
            Correlation(Release1RoomWithNoNameAssignmentMode.Primary, 1), "brick", "Brick",
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1RoomWithNoNameSelectionStatus.NoDiscoveredProduct, status);
    }

    [Fact]
    public void Null_inputs_report_invalid_input_and_never_throw()
    {
        Assert.False(Release1RoomWithNoNameAssignmentSelector.TrySelect(
            null, null, Release1RoomWithNoNameAssignmentMode.Primary, 1, null, null, null,
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1RoomWithNoNameSelectionStatus.InvalidInput, status);
    }

    [Fact]
    public void Progress_refuses_a_flag_that_runs_ahead_of_its_predecessor()
    {
        Assert.Throws<ArgumentException>(() => Progress(staged: false, custody: true, stowed: false, holdSatisfied: false).Validate());
        Assert.Throws<ArgumentException>(() => Progress(staged: true, custody: false, stowed: true, holdSatisfied: false).Validate());
        Assert.Throws<ArgumentException>(() => Progress(staged: true, custody: true, stowed: false, holdSatisfied: true).Validate());
    }

    [Fact]
    public void Progress_requires_a_stow_time_and_a_closet_when_stowed()
    {
        Assert.Throws<ArgumentException>(() => new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, null, "closet-a", null).Validate());
        Assert.Throws<ArgumentException>(() => new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, 10d, null, null).Validate());
    }

    [Fact]
    public void Progress_refuses_non_finite_or_negative_times()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, double.NaN, "closet-a", null).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Release1RoomWithNoNameProgress(
            Release1MissionCatalog.RoomWithNoName, 1, true, true, true, false, 10d, "closet-a", -1d).Validate());
    }

    [Fact]
    public void A_fresh_progress_row_validates_with_every_flag_false()
    {
        Progress(staged: false, custody: false, stowed: false, holdSatisfied: false).Validate();
    }

    private static Release1RoomWithNoNameProgress Progress(bool staged, bool custody, bool stowed, bool holdSatisfied) =>
        new(Release1MissionCatalog.RoomWithNoName, 1, staged, custody, stowed, holdSatisfied,
            stowed ? 100d : null, stowed ? "closet-a" : null, null);

    private static Release1SmallCourtesyProductCandidate[] Products() =>
        new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };

    private static Release1SmallCourtesyDropCandidate[] Drops(params string[] guids) =>
        guids.Select(guid => new Release1SmallCourtesyDropCandidate(guid, $"Drop {guid}", "A vanilla dead drop.", 1, 2, 3, true)).ToArray();

    private static string Correlation(Release1RoomWithNoNameAssignmentMode mode, int attempt) =>
        Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            mode switch
            {
                Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                _ => Release1TransitionKind.RecoveryAccepted
            },
            $"room-with-no-name-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}").Value;

    private static Release1RoomWithNoNameAssignment Assignment(
        Release1RoomWithNoNameAssignmentMode mode, int attempt, string packagingId, string packagingName) =>
        new(Release1MissionCatalog.RoomWithNoName, attempt, mode, Correlation(mode, attempt),
            "cocaine", "Cocaine", packagingId, packagingName, 1,
            "drop-source", "Source Drop", "Behind the laundromat.", 1, 2, 3,
            "drop-handoff", "Handoff Drop", "Under the pier.", 4, 5, 6,
            "syndicate-hq", 9, 1_440d, 1.5d);
}
