namespace OrganizedCrime.Runtime;

// Shared reward rounding for the missions that pay a fixed percentage of a delivered dollar value,
// rounded to the nearest whole dollar with ties rounding away from zero. The math runs in decimal,
// not float or double: multiplying a float value by a float or double multiplier can land a hair
// below an exact midpoint depending on which precision the multiplication happens in, so two
// missions at different multipliers would otherwise disagree with each other right at those
// midpoints. Decimal arithmetic on the value's own decimal representation has no such disagreement.
public static class Release1RewardMath
{
    private const decimal DefaultRewardMultiplier = 1.25m;

    public static float? QuoteWholeDollars(float deliveredValue) =>
        QuoteWholeDollars(deliveredValue, DefaultRewardMultiplier);

    // Returns the whole dollar reward for deliveredValue at the given multiplier, or null if
    // deliveredValue is not a usable non-negative finite amount, the multiplier is not positive, or
    // converting to decimal overflows (decimal's range is far smaller than float's, so a rounded
    // result can never overflow back the other way).
    public static float? QuoteWholeDollars(float deliveredValue, decimal multiplier)
    {
        if (!float.IsFinite(deliveredValue) || deliveredValue < 0f || multiplier <= 0m) return null;
        try
        {
            var rounded = Math.Round((decimal)deliveredValue * multiplier, 0, MidpointRounding.AwayFromZero);
            return (float)rounded;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
