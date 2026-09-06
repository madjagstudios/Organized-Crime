using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RewardMathTests
{
    // 999.6f cast to double is 999.5999755859375 (a hair below the true midpoint), so
    // double-multiplying by 1.25 rounds down to 1249, while decimal on the same float's decimal
    // representation lands exactly on the midpoint and rounds away from zero to 1250. This is the
    // value where float-first and double-first multiplication used to disagree between the two
    // mission services before they shared this helper.
    [Theory]
    [InlineData(999.6f, 1250f)]
    [InlineData(1000f, 1250f)]
    [InlineData(1440f, 1800f)]
    [InlineData(899.6f, 1125f)]
    public void Quotes_one_hundred_and_twenty_five_percent_rounded_away_from_zero(float value, float expected)
    {
        Assert.Equal(expected, Release1RewardMath.QuoteWholeDollars(value));
    }

    [Fact]
    public void Zero_quotes_zero()
    {
        Assert.Equal(0f, Release1RewardMath.QuoteWholeDollars(0f));
    }

    [Fact]
    public void A_negative_value_is_refused()
    {
        Assert.Null(Release1RewardMath.QuoteWholeDollars(-1f));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_non_finite_value_is_refused(float value)
    {
        Assert.Null(Release1RewardMath.QuoteWholeDollars(value));
    }

    // Both mission services delegate to the shared helper, so their quotes for the disagreement
    // value and the brief's pinned cases must match it exactly rather than drift independently.
    [Theory]
    [InlineData(999.6f, 1250f)]
    [InlineData(1000f, 1250f)]
    [InlineData(1440f, 1800f)]
    [InlineData(899.6f, 1125f)]
    public void Both_mission_reward_paths_agree_with_the_shared_helper(float value, float expected)
    {
        using var wrongAddress = Release1WrongAddressHarness.InCustody();
        wrongAddress.World.PlaceExactPackage(wrongAddress.Assignment, wrongAddress.Assignment.HandoffDropGuid, slotIndex: 2, value: value);
        var wrongAddressCashBefore = wrongAddress.World.CashBalance;

        wrongAddress.Service.ReconcileDelivery();

        Assert.Equal(wrongAddressCashBefore + expected, wrongAddress.World.CashBalance);
        Assert.Equal(expected, Release1RewardMath.QuoteWholeDollars(value));

        using var smallCourtesy = Release1SmallCourtesyDepositTests.ActiveMission();
        smallCourtesy.World.Slots[0] = smallCourtesy.World.Slots[0] with
        {
            ProductId = smallCourtesy.Assignment.ProductId,
            PackagingId = smallCourtesy.Assignment.PackagingId,
            Quantity = 2,
            IsPackaged = true,
            MonetaryValue = value
        };
        var smallCourtesyCashBefore = smallCourtesy.World.CashBalance;
        smallCourtesy.Service.TryHandleDropClosed(smallCourtesy.Assignment.DeadDropGuid);
        Release1SmallCourtesyDepositTests.Save(smallCourtesy);
        Release1SmallCourtesyDepositTests.Save(smallCourtesy);

        Assert.Equal(smallCourtesyCashBefore + expected, smallCourtesy.World.CashBalance);
    }

    [Theory]
    [InlineData(1_000f, 1_500f)]
    [InlineData(999f, 1_499f)]
    [InlineData(1f, 2f)]
    [InlineData(0f, 0f)]
    public void One_hundred_and_fifty_percent_rounds_to_whole_dollars_away_from_zero(float delivered, float expected)
    {
        Assert.Equal(expected, Release1RewardMath.QuoteWholeDollars(delivered, 1.5m));
    }

    [Fact]
    public void The_default_overload_still_quotes_one_hundred_and_twenty_five_percent()
    {
        Assert.Equal(Release1RewardMath.QuoteWholeDollars(1_000f, 1.25m), Release1RewardMath.QuoteWholeDollars(1_000f));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void A_non_usable_value_quotes_nothing_at_any_multiplier(float delivered)
    {
        Assert.Null(Release1RewardMath.QuoteWholeDollars(delivered, 1.5m));
    }
}
