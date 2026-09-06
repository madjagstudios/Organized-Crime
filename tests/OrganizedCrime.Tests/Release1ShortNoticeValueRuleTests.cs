using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeValueRuleTests
{
    [Fact]
    public void Whole_stack_consumed_sums_correctly_under_both_conventions()
    {
        Assert.Equal(120f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 0, 0f, 3));
        Assert.Equal(40f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerStack, 3, 40f, 0, 0f, 3));
    }

    [Fact]
    public void Surplus_left_per_unit_world_agrees_with_per_unit_and_disagrees_with_per_stack()
    {
        Assert.Equal(120f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 5, 40f, 2, 40f, 3));
        Assert.True(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 5, 40f, 2, 40f));
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 5, 40f, 2, 40f));
    }

    [Fact]
    public void Surplus_left_per_stack_world_agrees_with_per_stack_and_disagrees_with_per_unit()
    {
        Assert.Equal(120f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerStack, 5, 200f, 2, 80f, 3));
        Assert.True(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 5, 200f, 2, 80f));
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 5, 200f, 2, 80f));
    }

    [Fact]
    public void One_unit_consumed_sums_the_same_under_both_conventions()
    {
        Assert.Equal(40f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 1, 40f, 0, 0f, 1));
        Assert.Equal(40f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerStack, 1, 40f, 0, 0f, 1));
    }

    [Fact]
    public void Agrees_with_observation_on_a_fully_emptied_slot_passes_under_per_stack_and_fails_closed_under_per_unit()
    {
        // A full consumption leaves only one usable read (pre and post), which is enough to verify
        // PerStack (the consumed value is preValue - postValue, and it must be positive) but not
        // PerUnit (recovering a per-unit price needs a surviving quantity to divide by, and there is
        // none), so PerUnit must fail closed here rather than assume an agreement it never checked.
        Assert.True(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 3, 40f, 0, 0f));
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 0, 0f));
    }

    [Fact]
    public void Three_bricks_reading_4320_per_stack_pre_and_zero_post_pass_the_guard_and_pay_off_4320()
    {
        // The OC-58 owner proof's F6 gate: three Big Monkey bricks read a pre-consumption slot value
        // of 4320 and left the slot at zero after the whole stack was consumed. PerStack recovers the
        // consumed value directly from that one read and agrees with it; PerUnit cannot verify itself
        // from a fully emptied slot and fails closed instead of assuming agreement.
        Assert.Equal(4320f, Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerStack, 3, 4320f, 0, 0f, 3));
        Assert.True(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 3, 4320f, 0, 0f));
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 3, 4320f, 0, 0f));
    }

    [Fact]
    public void Per_unit_disagrees_when_the_post_value_fell()
    {
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 5, 200f, 2, 80f));
    }

    [Fact]
    public void Per_stack_disagrees_when_the_post_value_did_not_move()
    {
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 5, 40f, 2, 40f));
    }

    [Fact]
    public void Per_stack_disagrees_when_the_post_value_is_zero_on_a_non_empty_slot()
    {
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerStack, 5, 200f, 2, 0f));
    }

    [Fact]
    public void Per_unit_agrees_when_the_post_value_differs_by_a_tiny_fraction_within_tolerance()
    {
        Assert.True(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 5, 40f, 2, 40f + 0.001f));
    }

    [Fact]
    public void Per_unit_still_holds_when_the_post_value_differs_by_more_than_tolerance()
    {
        Assert.False(Release1ShortNoticeValueRule.AgreesWithObservation(
            Release1ShortNoticeValueConvention.PerUnit, 5, 40f, 2, 40f + 1f));
    }

    [Fact]
    public void Summed_value_is_null_for_a_non_finite_or_negative_input_a_consumed_count_below_one_and_a_post_quantity_that_is_not_pre_minus_consumed()
    {
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, float.NaN, 0, 0f, 3));
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, -1f, 0, 0f, 3));
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 0, float.PositiveInfinity, 3));
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 0, -1f, 3));
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 0, 0f, 0));
        Assert.Null(Release1ShortNoticeValueRule.SummedConsumedValue(
            Release1ShortNoticeValueConvention.PerUnit, 3, 40f, 1, 0f, 3));
    }
}
