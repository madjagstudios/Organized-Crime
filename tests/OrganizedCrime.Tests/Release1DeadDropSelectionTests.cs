using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1DeadDropSelectionTests
{
    [Fact]
    public void Two_different_drops_are_chosen_deterministically_from_the_correlation()
    {
        var drops = Drops("d", "a", "c", "b");

        Assert.True(Release1DeadDropSelection.TryChoosePair(drops, "oc10/v1/p/release1.room-with-no-name/1/MissionAccepted/r", out var source, out var handoff));
        Assert.True(Release1DeadDropSelection.TryChoosePair(drops, "oc10/v1/p/release1.room-with-no-name/1/MissionAccepted/r", out var source2, out var handoff2));

        Assert.NotNull(source);
        Assert.NotNull(handoff);
        Assert.NotEqual(source!.Guid, handoff!.Guid);
        Assert.Equal(source.Guid, source2!.Guid);
        Assert.Equal(handoff.Guid, handoff2!.Guid);
    }

    [Fact]
    public void Input_order_does_not_change_the_choice()
    {
        Assert.True(Release1DeadDropSelection.TryChoosePair(Drops("a", "b", "c", "d"), "corr", out var first, out var firstHandoff));
        Assert.True(Release1DeadDropSelection.TryChoosePair(Drops("d", "c", "b", "a"), "corr", out var second, out var secondHandoff));

        Assert.Equal(first!.Guid, second!.Guid);
        Assert.Equal(firstHandoff!.Guid, secondHandoff!.Guid);
    }

    [Fact]
    public void Occupied_drops_are_never_chosen()
    {
        var drops = new[] { Drop("a", isEmpty: false), Drop("b", isEmpty: true), Drop("c", isEmpty: true) };

        Assert.True(Release1DeadDropSelection.TryChoosePair(drops, "corr", out var source, out var handoff));

        Assert.NotEqual("a", source!.Guid);
        Assert.NotEqual("a", handoff!.Guid);
    }

    [Fact]
    public void Fewer_than_two_empty_drops_refuses_and_returns_nothing()
    {
        Assert.False(Release1DeadDropSelection.TryChoosePair(Drops("a"), "corr", out var source, out var handoff));
        Assert.Null(source);
        Assert.Null(handoff);

        Assert.False(Release1DeadDropSelection.TryChoosePair(Array.Empty<Release1SmallCourtesyDropCandidate>(), "corr", out _, out _));
        Assert.False(Release1DeadDropSelection.TryChoosePair(null, "corr", out _, out _));
    }

    [Fact]
    public void The_shared_rule_reproduces_the_shipped_wrong_address_choice()
    {
        var drops = Drops("drop-a", "drop-b", "drop-c", "drop-d");
        var correlation = Release1LogicalCorrelation.Create(
            "player-one", Release1MissionCatalog.WrongAddress, 1,
            Release1TransitionKind.MissionAccepted, "wrong-address-primary-accept-v1-a1").Value;

        Assert.True(Release1WrongAddressAssignmentSelector.TrySelect(
            new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) },
            drops,
            Release1WrongAddressAssignmentMode.Primary,
            1,
            correlation,
            "brick",
            "Brick",
            out var assignment,
            out var status));
        Assert.Equal(Release1WrongAddressSelectionStatus.Selected, status);

        Assert.True(Release1DeadDropSelection.TryChoosePair(drops, correlation, out var source, out var handoff));
        Assert.Equal(assignment!.SourceDropGuid, source!.Guid);
        Assert.Equal(assignment.HandoffDropGuid, handoff!.Guid);
    }

    [Fact]
    public void Single_choice_is_deterministic_for_one_correlation()
    {
        var drops = Drops("d", "a", "c", "b", "f", "e");

        Assert.True(Release1DeadDropSelection.TryChooseSingle(drops, "corr-single", out var first));
        Assert.True(Release1DeadDropSelection.TryChooseSingle(drops, "corr-single", out var second));

        Assert.NotNull(first);
        Assert.Equal(first!.Guid, second!.Guid);
    }

    [Fact]
    public void Single_choice_ignores_occupied_drops()
    {
        var drops = new[] { Drop("a", isEmpty: false), Drop("b", isEmpty: true), Drop("c", isEmpty: true) };

        Assert.True(Release1DeadDropSelection.TryChooseSingle(drops, "corr", out var chosen));

        Assert.NotEqual("a", chosen!.Guid);
    }

    [Fact]
    public void Single_choice_refuses_when_no_drop_is_empty()
    {
        var drops = new[] { Drop("a", isEmpty: false), Drop("b", isEmpty: false) };

        Assert.False(Release1DeadDropSelection.TryChooseSingle(drops, "corr", out var chosen));
        Assert.Null(chosen);

        Assert.False(Release1DeadDropSelection.TryChooseSingle(Array.Empty<Release1SmallCourtesyDropCandidate>(), "corr", out _));
        Assert.False(Release1DeadDropSelection.TryChooseSingle(null, "corr", out _));
    }

    [Fact]
    public void Single_choice_succeeds_on_exactly_one_empty_drop()
    {
        var drops = new[] { Drop("a", isEmpty: false), Drop("b", isEmpty: true) };

        Assert.True(Release1DeadDropSelection.TryChooseSingle(drops, "corr", out var chosen));
        Assert.Equal("b", chosen!.Guid);

        Assert.False(Release1DeadDropSelection.TryChoosePair(drops, "corr", out var source, out var handoff));
        Assert.Null(source);
        Assert.Null(handoff);
    }

    [Fact]
    public void Pair_source_still_equals_the_single_choice_on_the_same_input()
    {
        var drops = Drops("d", "a", "c", "b", "f", "e");
        var correlations = Enumerable.Range(0, 10).Select(i => $"corr-anti-drift-{i}");

        foreach (var correlation in correlations)
        {
            Assert.True(Release1DeadDropSelection.TryChooseSingle(drops, correlation, out var single));
            Assert.True(Release1DeadDropSelection.TryChoosePair(drops, correlation, out var source, out _));
            Assert.Equal(single!.Guid, source!.Guid);
        }
    }

    [Fact]
    public void Pair_still_refuses_fewer_than_two_empty_drops()
    {
        Assert.False(Release1DeadDropSelection.TryChoosePair(Drops("a"), "corr", out var source, out var handoff));
        Assert.Null(source);
        Assert.Null(handoff);
    }

    private static Release1SmallCourtesyDropCandidate[] Drops(params string[] guids) =>
        guids.Select(guid => Drop(guid, isEmpty: true)).ToArray();

    private static Release1SmallCourtesyDropCandidate Drop(string guid, bool isEmpty) =>
        new(guid, $"Drop {guid}", "A vanilla dead drop.", 1, 2, 3, isEmpty);
}
