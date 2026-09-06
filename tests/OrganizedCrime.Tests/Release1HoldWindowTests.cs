using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1HoldWindowTests
{
    private static readonly Release1ConsignmentFingerprint Brick = new("cocaine", "brick", 1);
    private const string ClosetA = "8ec9d63b-f0f7-4af9-86fb-f1c73c7af481";
    private const string ClosetB = "dcf4ca2a-4d27-47c4-a10a-2819ea298fe3";

    [Fact]
    public void A_room_that_is_not_ready_never_classifies_as_missing()
    {
        Assert.Equal(Release1HoldObservation.RoomNotReady, Release1HoldRoomClassifier.Classify(null, Brick, 9, out var guid));
        Assert.Null(guid);
        Assert.Equal(Release1HoldObservation.RoomNotReady, Release1HoldRoomClassifier.Classify(Release1HoldRoomSnapshot.NotReady(), Brick, 9, out _));
        Assert.Equal(Release1HoldObservation.RoomNotReady, Release1HoldRoomClassifier.Classify(Release1HoldRoomSnapshot.Unavailable(), Brick, 9, out _));
    }

    [Fact]
    public void A_closet_count_that_is_not_the_expected_nine_holds_instead_of_failing()
    {
        var room = Room(Closet(ClosetA, hasBrick: true));

        Assert.Equal(Release1HoldObservation.RoomNotReady, Release1HoldRoomClassifier.Classify(room, Brick, 9, out _));
    }

    [Fact]
    public void Exactly_one_matching_slot_anywhere_in_the_room_is_held()
    {
        var room = Room(Closet(ClosetB, hasBrick: false), Closet(ClosetA, hasBrick: true));

        Assert.Equal(Release1HoldObservation.Held, Release1HoldRoomClassifier.Classify(room, Brick, 2, out var guid));
        Assert.Equal(ClosetA, guid);
    }

    [Fact]
    public void Two_matching_slots_are_ambiguous_whether_in_one_closet_or_two()
    {
        var acrossTwo = Room(Closet(ClosetA, hasBrick: true), Closet(ClosetB, hasBrick: true));
        Assert.Equal(Release1HoldObservation.Ambiguous, Release1HoldRoomClassifier.Classify(acrossTwo, Brick, 2, out var acrossGuid));
        Assert.Null(acrossGuid);

        var inOne = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[]
        {
            new Release1HoldRoomClosetSnapshot(ClosetA, 2, new[]
            {
                new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 1_000f),
                new Release1SmallCourtesySlotSnapshot(1, "cocaine", "brick", 1, true, 1_000f)
            }),
            Closet(ClosetB, hasBrick: false)
        });
        Assert.Equal(Release1HoldObservation.Ambiguous, Release1HoldRoomClassifier.Classify(inOne, Brick, 2, out _));
    }

    [Fact]
    public void No_matching_slot_is_missing_even_when_other_items_are_present()
    {
        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, new[]
        {
            new Release1HoldRoomClosetSnapshot(ClosetA, 1, new[] { new Release1SmallCourtesySlotSnapshot(0, "meth", "jar", 1, true, 500f) }),
            Closet(ClosetB, hasBrick: false)
        });

        Assert.Equal(Release1HoldObservation.Missing, Release1HoldRoomClassifier.Classify(room, Brick, 2, out var guid));
        Assert.Null(guid);
    }

    [Fact]
    public void A_slot_with_the_same_product_and_packaging_but_a_different_quantity_is_ambiguous_not_missing()
    {
        var room = Room(StackedCloset(ClosetA));

        Assert.Equal(Release1HoldObservation.Ambiguous, Release1HoldRoomClassifier.Classify(room, Brick, 1, out var guid));
        Assert.Null(guid);
    }

    [Fact]
    public void A_stacked_slot_alongside_an_exact_match_elsewhere_is_still_ambiguous()
    {
        var room = Room(Closet(ClosetA, hasBrick: true), StackedCloset(ClosetB));

        Assert.Equal(Release1HoldObservation.Ambiguous, Release1HoldRoomClassifier.Classify(room, Brick, 2, out var guid));
        Assert.Null(guid);
    }

    [Fact]
    public void An_unready_or_ambiguous_observation_always_holds()
    {
        var stowed = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: null);

        Assert.Equal(Release1HoldDecision.Hold, Release1HoldWindow.Decide(Release1HoldObservation.RoomNotReady, stowed, 10_000d, 1_440d));
        Assert.Equal(Release1HoldDecision.Hold, Release1HoldWindow.Decide(Release1HoldObservation.Ambiguous, stowed, 10_000d, 1_440d));
    }

    [Fact]
    public void The_first_observed_stow_is_recorded()
    {
        var custodyOnly = Progress(stowed: false, holdSatisfied: false, stowedAt: null, missingSince: null);

        Assert.Equal(Release1HoldDecision.RecordStow, Release1HoldWindow.Decide(Release1HoldObservation.Held, custodyOnly, 500d, 1_440d));
    }

    [Theory]
    [InlineData(1_539.99d, Release1HoldDecision.None)]
    [InlineData(1_540d, Release1HoldDecision.SatisfyHold)]
    [InlineData(9_000d, Release1HoldDecision.SatisfyHold)]
    public void The_hold_expiry_boundary_is_exact(double now, Release1HoldDecision expected)
    {
        var stowed = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: null);

        Assert.Equal(expected, Release1HoldWindow.Decide(Release1HoldObservation.Held, stowed, now, 1_440d));
    }

    [Fact]
    public void An_already_satisfied_hold_does_nothing_further_while_it_is_still_in_the_room()
    {
        var released = Progress(stowed: true, holdSatisfied: true, stowedAt: 100d, missingSince: null);

        Assert.Equal(Release1HoldDecision.None, Release1HoldWindow.Decide(Release1HoldObservation.Held, released, 9_000d, 1_440d));
    }

    [Fact]
    public void Moving_the_consignment_back_into_the_room_inside_the_grace_clears_the_miss()
    {
        var missing = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: 500d);

        Assert.Equal(Release1HoldDecision.ClearMissing, Release1HoldWindow.Decide(Release1HoldObservation.Held, missing, 520d, 1_440d));
    }

    [Fact]
    public void A_first_missing_pass_records_the_time_and_never_fails()
    {
        var stowed = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: null);

        Assert.Equal(Release1HoldDecision.RecordMissing, Release1HoldWindow.Decide(Release1HoldObservation.Missing, stowed, 500d, 1_440d));
    }

    [Theory]
    [InlineData(559.99d, Release1HoldDecision.Hold)]
    [InlineData(560d, Release1HoldDecision.FailHold)]
    [InlineData(5_000d, Release1HoldDecision.FailHold)]
    public void The_sixty_minute_grace_boundary_is_exact(double now, Release1HoldDecision expected)
    {
        var missing = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: 500d);

        Assert.Equal(expected, Release1HoldWindow.Decide(Release1HoldObservation.Missing, missing, now, 1_440d));
    }

    [Fact]
    public void A_consignment_missing_before_the_stow_or_after_the_release_is_not_a_miss()
    {
        var beforeStow = Progress(stowed: false, holdSatisfied: false, stowedAt: null, missingSince: null);
        var afterRelease = Progress(stowed: true, holdSatisfied: true, stowedAt: 100d, missingSince: null);

        Assert.Equal(Release1HoldDecision.None, Release1HoldWindow.Decide(Release1HoldObservation.Missing, beforeStow, 5_000d, 1_440d));
        Assert.Equal(Release1HoldDecision.None, Release1HoldWindow.Decide(Release1HoldObservation.Missing, afterRelease, 5_000d, 1_440d));
    }

    [Fact]
    public void A_hold_that_expired_while_the_game_was_closed_is_satisfied_on_the_first_pass()
    {
        var stowed = Progress(stowed: true, holdSatisfied: false, stowedAt: 100d, missingSince: null);

        Assert.Equal(Release1HoldDecision.SatisfyHold, Release1HoldWindow.Decide(Release1HoldObservation.Held, stowed, 100_000d, 1_440d));
    }

    private static Release1RoomWithNoNameProgress Progress(bool stowed, bool holdSatisfied, double? stowedAt, double? missingSince) =>
        new(Release1MissionCatalog.RoomWithNoName, 1, true, true, stowed, holdSatisfied, stowedAt, stowed ? ClosetA : null, missingSince);

    private static Release1HoldRoomSnapshot Room(params Release1HoldRoomClosetSnapshot[] closets) =>
        new(Release1HoldRoomReadiness.Ready, closets);

    private static Release1HoldRoomClosetSnapshot Closet(string guid, bool hasBrick) =>
        new(guid, 1, new[]
        {
            hasBrick
                ? new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 1, true, 1_000f)
                : new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f)
        });

    // A single slot holding two bricks of the fingerprint's exact product and packaging, stacked into
    // one slot instead of the one Brick expects.
    private static Release1HoldRoomClosetSnapshot StackedCloset(string guid) =>
        new(guid, 1, new[] { new Release1SmallCourtesySlotSnapshot(0, "cocaine", "brick", 2, true, 2_000f) });
}
