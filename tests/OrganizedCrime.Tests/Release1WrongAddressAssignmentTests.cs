using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressAssignmentTests
{
    private const string PlayerId = "76561190000000001";

    [Theory]
    [InlineData(Release1WrongAddressAssignmentMode.Primary, Release1TransitionKind.MissionAccepted)]
    [InlineData(Release1WrongAddressAssignmentMode.MakeGood, Release1TransitionKind.MakeGoodAccepted)]
    [InlineData(Release1WrongAddressAssignmentMode.Recovery, Release1TransitionKind.RecoveryAccepted)]
    public void Each_mode_requires_its_own_authorization_transition(
        Release1WrongAddressAssignmentMode mode, Release1TransitionKind kind)
    {
        var assignment = Assignment(mode, 1, Correlation(1, kind));
        assignment.Validate();
        Assert.Equal(Release1MissionCatalog.WrongAddress, assignment.MissionKey);
        Assert.Equal(1.25d, assignment.RewardMultiplier);
        Assert.Equal(1, assignment.PackageQuantity);
    }

    [Fact]
    public void A_mismatched_authorization_transition_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Assignment(
            Release1WrongAddressAssignmentMode.Primary, 1, Correlation(1, Release1TransitionKind.MakeGoodAccepted)));
    }

    [Fact]
    public void The_source_and_handoff_drops_must_differ()
    {
        Assert.Throws<ArgumentException>(() => new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress, 1, Release1WrongAddressAssignmentMode.Primary,
            Correlation(1, Release1TransitionKind.MissionAccepted),
            "cocaine", "Cocaine", "brick", "Brick", 1,
            "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
            "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
            1.25d));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void The_package_quantity_must_be_exactly_one(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress, 1, Release1WrongAddressAssignmentMode.Primary,
            Correlation(1, Release1TransitionKind.MissionAccepted),
            "cocaine", "Cocaine", "brick", "Brick", quantity,
            "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
            "drop-b", "Drop B", "Under the bench", 4d, 5d, 6d,
            1.25d));
    }

    [Fact]
    public void A_non_finite_handoff_position_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Release1WrongAddressAssignment(
            Release1MissionCatalog.WrongAddress, 1, Release1WrongAddressAssignmentMode.Primary,
            Correlation(1, Release1TransitionKind.MissionAccepted),
            "cocaine", "Cocaine", "brick", "Brick", 1,
            "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
            "drop-b", "Drop B", "Under the bench", double.NaN, 5d, 6d,
            1.25d));
    }

    [Fact]
    public void A_wrong_mission_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Release1WrongAddressAssignment(
            Release1MissionCatalog.SmallCourtesy, 1, Release1WrongAddressAssignmentMode.Primary,
            Correlation(1, Release1TransitionKind.MissionAccepted),
            "cocaine", "Cocaine", "brick", "Brick", 1,
            "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
            "drop-b", "Drop B", "Under the bench", 4d, 5d, 6d,
            1.25d));
    }

    [Fact]
    public void Progress_requires_the_wrong_address_mission_and_custody_implies_staged()
    {
        new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, true, true).Validate();
        new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, false, false).Validate();
        Assert.Throws<ArgumentException>(() =>
            new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 1, false, true).Validate());
        Assert.Throws<ArgumentException>(() =>
            new Release1WrongAddressProgress(Release1MissionCatalog.SmallCourtesy, 1, true, false).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1WrongAddressProgress(Release1MissionCatalog.WrongAddress, 0, true, false).Validate());
    }

    [Fact]
    public void The_selector_picks_two_different_drops_deterministically_from_the_correlation()
    {
        var drops = Drops("drop-a", "drop-b", "drop-c", "drop-d");

        Assert.True(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops, Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out var first, out var status));
        Assert.Equal(Release1WrongAddressSelectionStatus.Selected, status);
        Assert.NotEqual(first!.SourceDropGuid, first.HandoffDropGuid);

        Assert.True(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops.Reverse().ToArray(), Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out var again, out _));
        Assert.Equal(first.SourceDropGuid, again!.SourceDropGuid);
        Assert.Equal(first.HandoffDropGuid, again.HandoffDropGuid);
    }

    [Fact]
    public void The_selector_matches_the_specified_hash_rule_exactly()
    {
        var drops = Drops("drop-a", "drop-b", "drop-c", "drop-d");
        var correlation = Correlation(1, Release1TransitionKind.MissionAccepted);
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(correlation));
        var hash = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8));
        var sorted = new[] { "drop-a", "drop-b", "drop-c", "drop-d" };
        var expectedSource = sorted[(int)(hash % 4UL)];
        var remainder = sorted.Where(guid => guid != expectedSource).ToArray();
        var expectedHandoff = remainder[(int)((hash >> 8) % 3UL)];

        Assert.True(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops, Release1WrongAddressAssignmentMode.Primary, 1, correlation, "brick", "Brick",
            out var assignment, out _));
        Assert.Equal(expectedSource, assignment!.SourceDropGuid);
        Assert.Equal(expectedHandoff, assignment.HandoffDropGuid);
    }

    [Fact]
    public void A_different_correlation_can_change_the_selected_pair()
    {
        var drops = Drops("drop-a", "drop-b", "drop-c", "drop-d", "drop-e", "drop-f", "drop-g", "drop-h");
        Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops, Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick", out var first, out _);
        Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops, Release1WrongAddressAssignmentMode.MakeGood, 2,
            Correlation(2, Release1TransitionKind.MakeGoodAccepted), "jar", "Jar", out var second, out _);

        Assert.NotEqual(
            (first!.SourceDropGuid, first.HandoffDropGuid),
            (second!.SourceDropGuid, second.HandoffDropGuid));
    }

    [Fact]
    public void Fewer_than_two_empty_drops_refuses_without_producing_an_assignment()
    {
        Assert.False(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), Drops("drop-a"), Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out var assignment, out var status));
        Assert.Null(assignment);
        Assert.Equal(Release1WrongAddressSelectionStatus.InsufficientEmptyDeadDrops, status);
    }

    [Fact]
    public void Occupied_drops_are_excluded_before_the_count_check()
    {
        var drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d, true),
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "Under the bench", 4d, 5d, 6d, false)
        };

        Assert.False(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), drops, Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out _, out var status));
        Assert.Equal(Release1WrongAddressSelectionStatus.InsufficientEmptyDeadDrops, status);
    }

    [Fact]
    public void Undiscovered_products_are_ignored_and_the_highest_price_wins_with_ordinal_tie_break()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("undiscovered", "Undiscovered", 9_999d, false),
            new Release1SmallCourtesyProductCandidate("meth", "Meth", 500d, true),
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 500d, true)
        };

        Assert.True(Release1WrongAddressAssignmentSelector.TrySelect(
            products, Drops("drop-a", "drop-b"), Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out var assignment, out _));
        Assert.Equal("cocaine", assignment!.ProductId);
    }

    [Fact]
    public void No_discovered_product_refuses_before_drop_selection()
    {
        Assert.False(Release1WrongAddressAssignmentSelector.TrySelect(
            new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 500d, false) },
            Drops("drop-a", "drop-b"), Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick",
            out _, out var status));
        Assert.Equal(Release1WrongAddressSelectionStatus.NoDiscoveredProduct, status);
    }

    [Fact]
    public void Malformed_input_returns_a_typed_invalid_result_without_throwing()
    {
        Assert.False(Release1WrongAddressAssignmentSelector.TrySelect(
            Products(), Drops("drop-a", "drop-b"), Release1WrongAddressAssignmentMode.Primary, 1,
            "not-a-correlation", "brick", "Brick", out _, out var status));
        Assert.Equal(Release1WrongAddressSelectionStatus.InvalidInput, status);

        Assert.False(Release1WrongAddressAssignmentSelector.TrySelect(
            null, Drops("drop-a", "drop-b"), Release1WrongAddressAssignmentMode.Primary, 1,
            Correlation(1, Release1TransitionKind.MissionAccepted), "brick", "Brick", out _, out var nullStatus));
        Assert.Equal(Release1WrongAddressSelectionStatus.InvalidInput, nullStatus);
    }

    internal static string Correlation(int attempt, Release1TransitionKind kind) =>
        Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, attempt, kind,
            $"wrong-address-{kind}-accept-v1-a{attempt}").Value;

    internal static IReadOnlyList<Release1SmallCourtesyProductCandidate> Products() =>
        new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };

    internal static IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops(params string[] guids) =>
        guids.Select((guid, index) => new Release1SmallCourtesyDropCandidate(
            guid, $"Drop {guid}", $"Near marker {index}", index, index + 1, index + 2, true)).ToArray();

    private static Release1WrongAddressAssignment Assignment(
        Release1WrongAddressAssignmentMode mode, int attempt, string correlation) => new(
        Release1MissionCatalog.WrongAddress, attempt, mode, correlation,
        "cocaine", "Cocaine", "brick", "Brick", 1,
        "drop-a", "Drop A", "Behind the diner", 1d, 2d, 3d,
        "drop-b", "Drop B", "Under the bench", 4d, 5d, 6d,
        1.25d);
}
