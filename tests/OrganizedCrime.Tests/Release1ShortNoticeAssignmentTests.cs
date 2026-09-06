using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeAssignmentTests
{
    private const string PlayerId = "player-one";

    [Theory]
    [InlineData(Release1ShortNoticeAssignmentMode.Primary, 3)]
    [InlineData(Release1ShortNoticeAssignmentMode.MakeGood, 2)]
    [InlineData(Release1ShortNoticeAssignmentMode.Recovery, 1)]
    public void Assignment_requires_three_bricks_for_the_primary_two_for_the_make_good_and_one_for_recovery(
        Release1ShortNoticeAssignmentMode mode, int quantity)
    {
        var assignment = Assignment(mode, 1, "brick", "Brick");

        assignment.Validate();

        Assert.Equal(quantity, assignment.RequiredQuantity);
    }

    [Theory]
    [InlineData(Release1ShortNoticeAssignmentMode.Primary, 2)]
    [InlineData(Release1ShortNoticeAssignmentMode.Primary, 1)]
    [InlineData(Release1ShortNoticeAssignmentMode.MakeGood, 3)]
    [InlineData(Release1ShortNoticeAssignmentMode.MakeGood, 1)]
    [InlineData(Release1ShortNoticeAssignmentMode.Recovery, 2)]
    [InlineData(Release1ShortNoticeAssignmentMode.Recovery, 3)]
    public void Every_other_pairing_of_mode_and_quantity_is_refused(Release1ShortNoticeAssignmentMode mode, int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(mode, 1, "brick", "Brick") with { RequiredQuantity = quantity });
    }

    [Fact]
    public void Assignment_requires_seven_hundred_and_twenty_game_minutes_for_timed_stages_and_none_for_recovery()
    {
        var primary = Assignment(Release1ShortNoticeAssignmentMode.Primary, 1, "brick", "Brick");
        Assert.Equal(720d, primary.DeadlineGameMinutes);

        var makeGood = Assignment(Release1ShortNoticeAssignmentMode.MakeGood, 1, "brick", "Brick");
        Assert.Equal(720d, makeGood.DeadlineGameMinutes);

        var recovery = Assignment(Release1ShortNoticeAssignmentMode.Recovery, 1, "brick", "Brick");
        Assert.Null(recovery.DeadlineGameMinutes);

        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = 60d });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = null });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = double.NaN });
        Assert.Throws<ArgumentException>(() => recovery with { DeadlineGameMinutes = 720d });
    }

    [Theory]
    [InlineData(1.25d)]
    [InlineData(2d)]
    [InlineData(0d)]
    public void Assignment_requires_the_one_point_seven_five_multiplier(double multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(Release1ShortNoticeAssignmentMode.Primary, 1, "brick", "Brick") with { RewardMultiplier = multiplier });
    }

    [Theory]
    [InlineData(Release1ShortNoticeAssignmentMode.Primary, Release1TransitionKind.MakeGoodAccepted)]
    [InlineData(Release1ShortNoticeAssignmentMode.MakeGood, Release1TransitionKind.MissionAccepted)]
    [InlineData(Release1ShortNoticeAssignmentMode.Recovery, Release1TransitionKind.MissionAccepted)]
    public void Assignment_requires_the_authorization_correlation_to_match_mission_attempt_and_mode(
        Release1ShortNoticeAssignmentMode mode, Release1TransitionKind wrongKind)
    {
        var wrong = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.ShortNotice, 1, wrongKind, "r").Value;

        Assert.Throws<ArgumentException>(() => Assignment(mode, 1, "brick", "Brick") with { AuthorizationCorrelationId = wrong });
    }

    [Fact]
    public void Assignment_rejects_a_non_finite_handoff_position_and_a_blank_stable_id()
    {
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, 1, "brick", "Brick");

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { HandoffDropX = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { HandoffDropY = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { HandoffDropZ = double.NegativeInfinity });
        Assert.Throws<ArgumentException>(() => assignment with { HandoffDropGuid = "" });
        Assert.Throws<ArgumentException>(() => assignment with { HandoffDropGuid = "   " });
    }

    [Fact]
    public void Assignment_rejects_an_undefined_value_convention()
    {
        var assignment = Assignment(Release1ShortNoticeAssignmentMode.Primary, 1, "brick", "Brick");

        Assert.Throws<ArgumentException>(() => assignment with { ValueConvention = (Release1ShortNoticeValueConvention)999 });
    }

    [Fact]
    public void Progress_rejects_a_negative_observed_quantity_and_a_shortfall_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, -1, null, false).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 0, 0, false).Validate());
    }

    [Fact]
    public void A_fresh_progress_row_validates_with_no_shortfall_or_spread()
    {
        Release1ShortNoticeProgress.Fresh(1).Validate();
    }

    [Fact]
    public void Selector_chooses_the_highest_asking_price_discovered_product_with_an_ordinal_tie_break()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("meth", "Meth", 900d, true),
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("aaa", "Aaa", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("gold", "Gold", 5_000d, false)
        };

        Assert.True(Release1ShortNoticeAssignmentSelector.TrySelect(
            products, Drops("drop-a", "drop-b"), Release1ShortNoticeAssignmentMode.Primary, 1,
            Correlation(Release1ShortNoticeAssignmentMode.Primary, 1), "brick", "Brick",
            Release1ShortNoticeValueConvention.PerUnit,
            out var assignment, out var status));

        Assert.Equal(Release1ShortNoticeSelectionStatus.Selected, status);
        Assert.Equal("aaa", assignment!.ProductId);
    }

    [Fact]
    public void Selector_reports_InsufficientEmptyDeadDrops_when_no_drop_is_empty()
    {
        Assert.False(Release1ShortNoticeAssignmentSelector.TrySelect(
            Products(), Array.Empty<Release1SmallCourtesyDropCandidate>(), Release1ShortNoticeAssignmentMode.Primary, 1,
            Correlation(Release1ShortNoticeAssignmentMode.Primary, 1), "brick", "Brick",
            Release1ShortNoticeValueConvention.PerUnit,
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1ShortNoticeSelectionStatus.NoEmptyDeadDrop, status);
    }

    [Fact]
    public void NoDiscoveredProduct_when_none_is_discovered()
    {
        var products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, false) };

        Assert.False(Release1ShortNoticeAssignmentSelector.TrySelect(
            products, Drops("drop-a"), Release1ShortNoticeAssignmentMode.Primary, 1,
            Correlation(Release1ShortNoticeAssignmentMode.Primary, 1), "brick", "Brick",
            Release1ShortNoticeValueConvention.PerUnit,
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1ShortNoticeSelectionStatus.NoDiscoveredProduct, status);
    }

    [Fact]
    public void Null_inputs_report_invalid_input_and_never_throw()
    {
        Assert.False(Release1ShortNoticeAssignmentSelector.TrySelect(
            null, null, Release1ShortNoticeAssignmentMode.Primary, 1, null, null, null,
            Release1ShortNoticeValueConvention.PerUnit,
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1ShortNoticeSelectionStatus.InvalidInput, status);
    }

    [Fact]
    public void Selector_chooses_the_same_drop_as_the_shared_single_rule_for_the_same_correlation()
    {
        var correlation = Correlation(Release1ShortNoticeAssignmentMode.Primary, 1);
        var drops = Drops("drop-a", "drop-b", "drop-c", "drop-d");

        Assert.True(Release1ShortNoticeAssignmentSelector.TrySelect(
            Products(), drops, Release1ShortNoticeAssignmentMode.Primary, 1, correlation, "brick", "Brick",
            Release1ShortNoticeValueConvention.PerUnit,
            out var assignment, out var status));

        Assert.Equal(Release1ShortNoticeSelectionStatus.Selected, status);
        Assert.True(Release1DeadDropSelection.TryChooseSingle(Drops("drop-a", "drop-b", "drop-c", "drop-d"), correlation, out var chosen));
        Assert.Equal(chosen!.Guid, assignment!.HandoffDropGuid);
    }

    private static Release1SmallCourtesyProductCandidate[] Products() =>
        new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };

    private static Release1SmallCourtesyDropCandidate[] Drops(params string[] guids) =>
        guids.Select(guid => new Release1SmallCourtesyDropCandidate(guid, $"Drop {guid}", "A vanilla dead drop.", 1, 2, 3, true)).ToArray();

    private static string Correlation(Release1ShortNoticeAssignmentMode mode, int attempt) =>
        Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.ShortNotice,
            attempt,
            mode switch
            {
                Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                _ => Release1TransitionKind.RecoveryAccepted
            },
            $"short-notice-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}").Value;

    private static Release1ShortNoticeAssignment Assignment(
        Release1ShortNoticeAssignmentMode mode, int attempt, string packagingId, string packagingName) =>
        new(Release1MissionCatalog.ShortNotice, attempt, mode, Correlation(mode, attempt),
            "cocaine", "Cocaine", packagingId, packagingName, Release1ShortNoticeAssignment.QuantityFor(mode),
            "drop-handoff", "Handoff Drop", "Under the pier.", 4, 5, 6,
            mode == Release1ShortNoticeAssignmentMode.Recovery ? null : Release1ShortNoticeAssignment.TimedStageGameMinutes,
            Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
            Release1ShortNoticeValueConvention.PerUnit);
}
