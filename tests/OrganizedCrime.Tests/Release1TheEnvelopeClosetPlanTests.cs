using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeClosetPlanTests
{
    private const string ClosetGuid = "closet-a";

    [Fact]
    public void Twenty_full_stacks_plan_every_slot_to_zero_for_the_primary_amount()
    {
        var cashSlots = Enumerable.Range(0, 20)
            .Select(index => new Release1TheEnvelopeCashSlot(index, 1000d))
            .ToArray();

        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 20000d, out var plan));

        Assert.Equal(20, plan!.Slots.Count);
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(index, plan.Slots[index].SlotIndex);
            Assert.Equal(1000d, plan.Slots[index].PreBalance);
            Assert.Equal(0d, plan.Slots[index].PostBalance);
        }
        Assert.Equal(20000d, plan.ConsumedAmount);
    }

    [Fact]
    public void Whole_stacks_come_first_in_ascending_index_order_and_the_surplus_is_never_planned()
    {
        var indexes = new[] { 2, 5, 7, 9, 11, 13, 15, 17, 19, 20, 21 };
        var cashSlots = indexes.Select(index => new Release1TheEnvelopeCashSlot(index, 1000d)).ToArray();

        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 10000d, out var plan));

        var expected = new[] { 2, 5, 7, 9, 11, 13, 15, 17, 19, 20 };
        Assert.Equal(expected, plan!.Slots.Select(slot => slot.SlotIndex));
        Assert.All(plan.Slots, slot => Assert.Equal(1000d, slot.PreBalance));
        Assert.All(plan.Slots, slot => Assert.Equal(0d, slot.PostBalance));
        Assert.DoesNotContain(plan.Slots, slot => slot.SlotIndex == 21);
    }

    [Fact]
    public void A_partial_last_stack_carries_the_residual_remainder()
    {
        var cashSlots = new[]
        {
            new Release1TheEnvelopeCashSlot(0, 1000d),
            new Release1TheEnvelopeCashSlot(1, 1000d),
            new Release1TheEnvelopeCashSlot(2, 1000d),
            new Release1TheEnvelopeCashSlot(3, 1000d),
            new Release1TheEnvelopeCashSlot(4, 1200d)
        };

        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 5000d, out var plan));

        Assert.Equal(5, plan!.Slots.Count);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(1000d, plan.Slots[index].PreBalance);
            Assert.Equal(0d, plan.Slots[index].PostBalance);
        }
        Assert.Equal(4, plan.Slots[4].SlotIndex);
        Assert.Equal(1200d, plan.Slots[4].PreBalance);
        Assert.Equal(200d, plan.Slots[4].PostBalance);
        Assert.Equal(5000d, plan.ConsumedAmount);
    }

    [Fact]
    public void Surplus_slots_are_never_in_the_plan()
    {
        var cashSlots = Enumerable.Range(0, 21)
            .Select(index => new Release1TheEnvelopeCashSlot(index, 1000d))
            .ToArray();

        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 20000d, out var plan));

        Assert.Equal(20, plan!.Slots.Count);
        Assert.DoesNotContain(plan.Slots, slot => slot.SlotIndex == 20);
    }

    [Fact]
    public void A_sum_below_the_amount_does_not_build()
    {
        var cashSlots = new[]
        {
            new Release1TheEnvelopeCashSlot(0, 1000d),
            new Release1TheEnvelopeCashSlot(1, 1000d)
        };

        Assert.False(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 5000d, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void A_non_whole_dollar_pre_balance_does_not_build()
    {
        var cashSlots = new[] { new Release1TheEnvelopeCashSlot(0, 5000.5d) };

        Assert.False(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 5000d, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void A_pre_balance_within_a_hundredth_of_a_whole_dollar_builds_at_the_whole_dollar()
    {
        var cashSlots = new[] { new Release1TheEnvelopeCashSlot(0, 5000.004d) };

        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 5000d, out var plan));

        var slot = Assert.Single(plan!.Slots);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(5000d, slot.PreBalance);
        Assert.Equal(0d, slot.PostBalance);
    }

    [Fact]
    public void A_plan_needing_twenty_one_groups_does_not_build()
    {
        // Twenty one slots, each a full ten thousand dollar stack, indexed high enough (100 to 120)
        // that every group carries a three digit slot index and a five digit pre balance: exactly the
        // amount needed to push the 256 character bound past its limit at 21 groups, matching the
        // brief's own worked example for a twenty group plan of shorter groups fitting inside it.
        var cashSlots = Enumerable.Range(100, 21)
            .Select(index => new Release1TheEnvelopeCashSlot(index, 10000d))
            .ToArray();

        Assert.False(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, ClosetGuid, 210000d, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Serialize_round_trips_through_TryParse_and_rejects_a_mutated_encoding()
    {
        var plan = new Release1TheEnvelopeClosetPlan(ClosetGuid, new[] { new Release1TheEnvelopePlannedSlot(0, 1000d, 0d) });
        var encoded = plan.Serialize();

        Assert.True(Release1TheEnvelopeClosetPlan.TryParse(encoded, ClosetGuid, out var restored));
        Assert.Equal(encoded, restored!.Serialize());
        Assert.Equal(0, restored.Slots[0].SlotIndex);
        Assert.Equal(1000d, restored.Slots[0].PreBalance);
        Assert.Equal(0d, restored.Slots[0].PostBalance);

        var mutated = encoded.Replace("1000", "1000.5", StringComparison.Ordinal);
        Assert.False(Release1TheEnvelopeClosetPlan.TryParse(mutated, ClosetGuid, out var mutatedResult));
        Assert.Null(mutatedResult);
    }

    [Fact]
    public void TryParse_rejects_an_encoding_whose_groups_are_not_strictly_ascending_by_slot_index()
    {
        const string encoded = "te-closet-v1|1,1000,0|0,1000,0";

        Assert.False(Release1TheEnvelopeClosetPlan.TryParse(encoded, ClosetGuid, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void TryParse_rejects_an_encoding_longer_than_two_hundred_and_fifty_six_characters()
    {
        var encoded = new string('9', 257);

        Assert.False(Release1TheEnvelopeClosetPlan.TryParse(encoded, ClosetGuid, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Only_the_last_planned_slot_may_keep_a_remainder()
    {
        Assert.Throws<ArgumentException>(() => new Release1TheEnvelopeClosetPlan(ClosetGuid, new[]
        {
            new Release1TheEnvelopePlannedSlot(0, 1000d, 400d),
            new Release1TheEnvelopePlannedSlot(1, 1000d, 0d)
        }));
    }

    [Fact]
    public void A_planned_post_balance_is_never_negative_and_never_at_or_above_its_pre_balance()
    {
        Assert.Throws<ArgumentException>(() => new Release1TheEnvelopeClosetPlan(ClosetGuid,
            new[] { new Release1TheEnvelopePlannedSlot(0, 1000d, -1d) }));
        Assert.Throws<ArgumentException>(() => new Release1TheEnvelopeClosetPlan(ClosetGuid,
            new[] { new Release1TheEnvelopePlannedSlot(0, 1000d, 1000d) }));
        Assert.Throws<ArgumentException>(() => new Release1TheEnvelopeClosetPlan(ClosetGuid,
            new[] { new Release1TheEnvelopePlannedSlot(0, 1000d, 1200d) }));
    }
}
