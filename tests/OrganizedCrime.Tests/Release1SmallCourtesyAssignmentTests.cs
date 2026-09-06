using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyAssignmentTests
{
    private const string PlayerId = "76561190000000001";

    [Theory]
    [InlineData(Release1SmallCourtesyAssignmentMode.Primary, Release1TransitionKind.MissionAccepted, "brick")]
    [InlineData(Release1SmallCourtesyAssignmentMode.MakeGood, Release1TransitionKind.MakeGoodAccepted, "jar")]
    [InlineData(Release1SmallCourtesyAssignmentMode.Recovery, Release1TransitionKind.RecoveryAccepted, "jar")]
    public void Valid_assignments_are_immutable_and_value_equal(
        Release1SmallCourtesyAssignmentMode mode,
        Release1TransitionKind transition,
        string packagingId)
    {
        var first = Assignment(mode, attempt: (int)mode + 1, transition, packagingId);
        var second = Assignment(mode, attempt: (int)mode + 1, transition, packagingId);

        first.Validate();
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Assignment_rejects_wrong_mission_attempt_or_transition()
    {
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { MissionKey = Release1MissionCatalog.WrongAddress });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { Attempt = 0 });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with
        {
            AuthorizationCorrelationId = Correlation(1, Release1TransitionKind.MakeGoodAccepted, "wrong-transition")
        });
    }

    [Fact]
    public void Assignment_rejects_malformed_identity_and_presentation_fields()
    {
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { ProductId = " " });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { PackagingId = "bad/package" });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { DeadDropGuid = new string('g', 257) });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { ProductName = "\n" });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { PackagingName = new string('p', 257) });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { DeadDropDescription = new string('d', 1025) });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.01)]
    public void Assignment_rejects_invalid_selection_price(double price) =>
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { SelectionAskingPrice = price });

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(0, 0, double.NegativeInfinity)]
    public void Assignment_rejects_non_finite_drop_positions(double x, double y, double z) =>
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { DeadDropX = x, DeadDropY = y, DeadDropZ = z });

    [Fact]
    public void Assignment_rejects_invalid_mode_and_reward_multiplier()
    {
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { Mode = (Release1SmallCourtesyAssignmentMode)99 });
        Assert.ThrowsAny<ArgumentException>(() => Assignment() with { RewardMultiplier = 1.5 });
    }

    [Fact]
    public void Story_rejects_duplicate_mission_attempt_assignments()
    {
        var assignment = Assignment();
        var state = Story() with { SmallCourtesyAssignments = new[] { assignment, assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_assignment_from_a_future_mission_attempt()
    {
        var assignment = Assignment(
            Release1SmallCourtesyAssignmentMode.MakeGood,
            2,
            Release1TransitionKind.MakeGoodAccepted,
            "jar");
        var state = Story() with { SmallCourtesyAssignments = new[] { assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_assignment_whose_authorization_was_not_accepted_by_small_courtesy()
    {
        var assignment = Assignment();
        var state = Story() with { SmallCourtesyAssignments = new[] { assignment } };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Fact]
    public void Story_rejects_product_or_later_stage_package_drift()
    {
        var primary = Assignment();
        var makeGood = Assignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, Release1TransitionKind.MakeGoodAccepted, "jar");
        var recovery = Assignment(Release1SmallCourtesyAssignmentMode.Recovery, 3, Release1TransitionKind.RecoveryAccepted, "jar");

        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood) with
        {
            SmallCourtesyAssignments = new[] { primary, makeGood with { ProductId = "other-product" } }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (StoryWithAssignments(primary, makeGood, recovery) with
        {
            SmallCourtesyAssignments = new[] { primary, makeGood, recovery with { PackagingId = "other-jar" } }
        }).Validate());
    }

    [Fact]
    public void Story_value_equality_and_hash_include_assignments()
    {
        var assignment = Assignment();
        var left = StoryWithAssignments(assignment);
        var same = StoryWithAssignments(assignment);
        var different = StoryWithAssignments(assignment with { DeadDropName = "Other drop" });

        left.Validate();
        same.Validate();
        different.Validate();
        Assert.True(left.ValueEquals(same));
        Assert.Equal(left.GetHashCode(), same.GetHashCode());
        Assert.False(left.ValueEquals(different));
        Assert.NotEqual(left.GetHashCode(), different.GetHashCode());
    }

    [Fact]
    public void Selector_chooses_highest_discovered_price_then_ordinal_product_id()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("z-product", "Z", 400, true),
            new Release1SmallCourtesyProductCandidate("a-product", "A", 400, true),
            new Release1SmallCourtesyProductCandidate("hidden", "Hidden", 900, false)
        };

        Assert.True(TrySelect(products, Drops(), Correlation(1, Release1TransitionKind.MissionAccepted, "select-product"), out var assignment, out var status));
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.Selected, status);
        Assert.Equal("a-product", assignment!.ProductId);
        Assert.Equal(400, assignment.SelectionAskingPrice);
    }

    [Fact]
    public void Selector_excludes_occupied_drops_and_is_input_order_independent()
    {
        var drops = Drops().Append(new Release1SmallCourtesyDropCandidate("000-drop", "Occupied", "No", 4, 5, 6, false)).ToArray();
        var correlation = Correlation(1, Release1TransitionKind.MissionAccepted, "stable-drop");

        Assert.True(TrySelect(Products(), drops, correlation, out var first, out _));
        Assert.True(TrySelect(Products().Reverse().ToArray(), drops.Reverse().ToArray(), correlation, out var second, out _));
        Assert.Equal(first, second);
        Assert.NotEqual("000-drop", first!.DeadDropGuid);
    }

    [Fact]
    public void Selector_uses_canonical_correlation_to_choose_across_sorted_empty_drops()
    {
        var selections = Enumerable.Range(1, 32)
            .Select(index =>
            {
                Assert.True(TrySelect(Products(), Drops(), Correlation(1, Release1TransitionKind.MissionAccepted, $"receipt-{index}"), out var assignment, out _));
                return assignment!.DeadDropGuid;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(selections.Length > 1);
    }

    [Fact]
    public void Selector_fails_typed_and_without_assignment_for_missing_candidates()
    {
        Assert.False(TrySelect(Array.Empty<Release1SmallCourtesyProductCandidate>(), Drops(), Correlation(1, Release1TransitionKind.MissionAccepted, "no-product"), out var noProduct, out var productStatus));
        Assert.Null(noProduct);
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.NoDiscoveredProduct, productStatus);

        Assert.False(TrySelect(Products(), Drops().Select(drop => drop with { IsEmpty = false }).ToArray(), Correlation(1, Release1TransitionKind.MissionAccepted, "no-drop"), out var noDrop, out var dropStatus));
        Assert.Null(noDrop);
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.NoEmptyDeadDrop, dropStatus);
    }

    [Fact]
    public void Selector_rejects_malformed_candidates_and_correlation()
    {
        Assert.False(TrySelect(new[] { Products()[0] with { AskingPrice = double.NaN } }, Drops(), Correlation(1, Release1TransitionKind.MissionAccepted, "bad-product"), out _, out var productStatus));
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.InvalidInput, productStatus);

        Assert.False(TrySelect(Products(), new[] { Drops()[0] with { X = double.PositiveInfinity } }, Correlation(1, Release1TransitionKind.MissionAccepted, "bad-drop"), out _, out var dropStatus));
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.InvalidInput, dropStatus);

        Assert.False(TrySelect(Products(), Drops(), "not-a-correlation", out _, out var correlationStatus));
        Assert.Equal(Release1SmallCourtesyAssignmentSelectionStatus.InvalidInput, correlationStatus);
    }

    private static bool TrySelect(
        IReadOnlyList<Release1SmallCourtesyProductCandidate> products,
        IReadOnlyList<Release1SmallCourtesyDropCandidate> drops,
        string correlation,
        out Release1SmallCourtesyAssignment? assignment,
        out Release1SmallCourtesyAssignmentSelectionStatus status) =>
        Release1SmallCourtesyAssignmentSelector.TrySelect(
            products, drops, Release1SmallCourtesyAssignmentMode.Primary, 1, correlation,
            "brick", "Brick", out assignment, out status);

    private static Release1SmallCourtesyProductCandidate[] Products() =>
        new[] { new Release1SmallCourtesyProductCandidate("product", "Product", 500, true) };

    private static Release1SmallCourtesyDropCandidate[] Drops() =>
        new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-c", "C", "Drop C", 3, 0, 0, true),
            new Release1SmallCourtesyDropCandidate("drop-a", "A", "Drop A", 1, 0, 0, true),
            new Release1SmallCourtesyDropCandidate("drop-b", "B", "Drop B", 2, 0, 0, true)
        };

    private static Release1SmallCourtesyAssignment Assignment(
        Release1SmallCourtesyAssignmentMode mode = Release1SmallCourtesyAssignmentMode.Primary,
        int attempt = 1,
        Release1TransitionKind transition = Release1TransitionKind.MissionAccepted,
        string packagingId = "brick") =>
        new(
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            mode,
            Correlation(attempt, transition, $"authorize-{attempt}"),
            "product",
            "Product",
            500,
            packagingId,
            packagingId == "brick" ? "Brick" : "Jar",
            $"drop-{attempt}",
            $"Drop {attempt}",
            "A dead drop",
            attempt,
            2,
            3,
            1.25);

    private static string Correlation(int attempt, Release1TransitionKind transition, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.SmallCourtesy, attempt, transition, receipt).Value;

    private static Release1StoryState Story() =>
        Release1StoryState.CreateAccepted(PlayerId,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Release1StoryState StoryWithAssignments(params Release1SmallCourtesyAssignment[] assignments)
    {
        var story = Story();
        var missions = story.Missions.ToArray();
        missions[0] = missions[0] with
        {
            Attempt = assignments.Max(assignment => assignment.Attempt),
            AcceptedLogicalCorrelations = assignments.Select(assignment => assignment.AuthorizationCorrelationId).ToArray()
        };
        return story with { Missions = missions, SmallCourtesyAssignments = assignments };
    }
}
