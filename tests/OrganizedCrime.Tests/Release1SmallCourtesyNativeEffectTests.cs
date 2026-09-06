using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyNativeEffectTests
{
    [Fact]
    public void Cargo_identity_round_trips_as_a_bounded_single_segment()
    {
        var identity = new Release1SmallCourtesyCargoIdentity(
            "cocaine-heavenly",
            "brick",
            7,
            3,
            2,
            12_345.625f);

        var encoded = identity.Serialize();

        Assert.True(encoded.Length <= 256);
        Assert.DoesNotContain('/', encoded);
        Assert.True(Release1SmallCourtesyCargoIdentity.TryParse(encoded, out var restored));
        Assert.Equal(identity, restored);
        Assert.Equal(encoded, restored!.Serialize());
    }

    [Fact]
    public void Reward_identity_round_trips_with_the_fixed_verification_tolerance()
    {
        var identity = new Release1SmallCourtesyRewardIdentity(1_234.25f, 7_500f, 8_734.25f);

        var encoded = identity.Serialize();

        Assert.True(encoded.Length <= 256);
        Assert.True(Release1SmallCourtesyRewardIdentity.TryParse(encoded, out var restored));
        Assert.Equal(identity, restored);
        Assert.Equal(0.50f, restored!.VerificationTolerance);
        Assert.Equal(encoded, restored.Serialize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("sc-cargo-v0|product|brick|0|1|0|10")]
    [InlineData("sc-reward-v1|0|1|1|0.5")]
    [InlineData("sc-cargo-v1|product|brick|0|1|0|10|trailing")]
    [InlineData("sc-cargo-v1|product|brick|0|1|0|NaN")]
    [InlineData("sc-cargo-v1|product|brick|0|1|0|Infinity")]
    public void Cargo_parser_rejects_wrong_type_version_trailing_and_nonfinite_data(string encoded)
    {
        Assert.False(Release1SmallCourtesyCargoIdentity.TryParse(encoded, out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sc-reward-v0|100|25|125|0.5")]
    [InlineData("sc-cargo-v1|product|brick|0|1|0|10")]
    [InlineData("sc-reward-v1|100|25|125|0.5|trailing")]
    [InlineData("sc-reward-v1|NaN|25|125|0.5")]
    [InlineData("sc-reward-v1|100|25|Infinity|0.5")]
    public void Reward_parser_rejects_wrong_type_version_trailing_and_nonfinite_data(string encoded)
    {
        Assert.False(Release1SmallCourtesyRewardIdentity.TryParse(encoded, out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("bad|product", "brick", 0, 1, 0, 10f)]
    [InlineData("product", "bad|brick", 0, 1, 0, 10f)]
    [InlineData("product", "brick", -1, 1, 0, 10f)]
    [InlineData("product", "brick", 0, 0, -1, 10f)]
    [InlineData("product", "brick", 0, 1, 1, 10f)]
    [InlineData("product", "brick", 0, 1, 0, -1f)]
    public void Cargo_identity_rejects_delimiters_invalid_slot_quantity_delta_and_value(
        string productId,
        string packageId,
        int slotIndex,
        int preQuantity,
        int postQuantity,
        float depositedValue)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyCargoIdentity(
            productId,
            packageId,
            slotIndex,
            preQuantity,
            postQuantity,
            depositedValue));
    }

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(5, 2, 3)]
    [InlineData(2, 1, 1)]
    public void Cargo_identity_accepts_a_multi_unit_consumption_and_reports_its_count(
        int preQuantity,
        int postQuantity,
        int expectedConsumed)
    {
        var identity = new Release1SmallCourtesyCargoIdentity("product", "brick", 0, preQuantity, postQuantity, 10f);

        Assert.Equal(expectedConsumed, identity.ConsumedCount);
        Assert.True(Release1SmallCourtesyCargoIdentity.TryParse(identity.Serialize(), out var parsed));
        Assert.Equal(identity, parsed);
    }

    [Fact]
    public void Cargo_identity_still_rejects_a_consumption_of_zero_or_less()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyCargoIdentity("product", "brick", 0, 3, 3, 10f));
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyCargoIdentity("product", "brick", 0, 3, 4, 10f));
    }

    [Fact]
    public void Cargo_identity_rejects_nonfinite_and_oversize_output()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyCargoIdentity(
            "product", "brick", 0, 1, 0, float.NaN));
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyCargoIdentity(
            new string('p', 200), new string('b', 100), 0, 1, 0, 10f));
    }

    [Theory]
    [InlineData(-1f, 25f, 24f)]
    [InlineData(100f, -1f, 99f)]
    [InlineData(100f, 25.5f, 125.5f)]
    [InlineData(100f, 25f, 124.49f)]
    [InlineData(100f, 25f, 125.51f)]
    public void Reward_identity_rejects_negative_non_whole_and_impossible_equations(
        float baseline,
        float amount,
        float expected)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyRewardIdentity(
            baseline,
            amount,
            expected));
    }

    [Fact]
    public void Reward_identity_rejects_nonfinite_values()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Release1SmallCourtesyRewardIdentity(
            float.PositiveInfinity,
            25f,
            25f));
    }
}
