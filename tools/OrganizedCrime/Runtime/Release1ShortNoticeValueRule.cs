namespace OrganizedCrime.Runtime;

/// <summary>
/// Which of the two possible readings of <c>ProductItemInstance.GetMonetaryValue()</c> a slot
/// snapshot's MonetaryValue carries. OC has only ever read that number from a slot at quantity one,
/// so both readings are implemented and the live one is fixed by the owner proof rather than
/// assumed.
/// </summary>
public enum Release1ShortNoticeValueConvention
{
    PerUnit,
    PerStack
}

/// <summary>
/// The consumed value rule. The mission never multiplies or divides a value it did not measure: it
/// reads the matching slot before consumption and again after, and computes the summed value of the
/// consumed units from those two readings under the frozen convention. When the post slot is still
/// occupied the two readings disagree with each other in an observable way, and
/// <see cref="AgreesWithObservation"/> is that check; a contradiction blocks the effect rather than
/// paying an unverified amount.
/// </summary>
public static class Release1ShortNoticeValueRule
{
    /// <summary>
    /// The live reading, fixed by the OC-58 owner proof's F6 gate: three Big Monkey bricks read
    /// 4320 and one brick read 1440, so the slot's MonetaryValue is per stack, not per unit. It
    /// is frozen into each assignment at acceptance so a later change to this constant cannot
    /// change the reward basis of an attempt already under way.
    /// </summary>
    public const Release1ShortNoticeValueConvention Observed = Release1ShortNoticeValueConvention.PerStack;

    /// <summary>
    /// Reconciliation tolerance for comparing two live monetary readings. A live read taken before
    /// consumption and one taken after can differ by a rounding ulp even when both describe the same
    /// underlying value; exact equality would push that legitimate reading into the fail-closed hold.
    /// 0.5% of the larger magnitude, floored at 0.01 so near-zero values still get a usable tolerance.
    /// </summary>
    private const float ReconciliationTolerance = 0.005f;
    private const float ReconciliationToleranceFloor = 0.01f;

    private static bool NearlyEqual(float a, float b)
    {
        var tolerance = MathF.Max(ReconciliationTolerance * MathF.Max(MathF.Abs(a), MathF.Abs(b)), ReconciliationToleranceFloor);
        return MathF.Abs(a - b) <= tolerance;
    }

    public static float? SummedConsumedValue(
        Release1ShortNoticeValueConvention convention,
        int preQuantity,
        float preValue,
        int postQuantity,
        float postValue,
        int consumedCount)
    {
        if (!Enum.IsDefined(convention)) return null;
        if (consumedCount < 1 || preQuantity < consumedCount || postQuantity < 0) return null;
        if (postQuantity != preQuantity - consumedCount) return null;
        if (!float.IsFinite(preValue) || preValue < 0f || !float.IsFinite(postValue) || postValue < 0f) return null;
        if (postQuantity == 0 && !NearlyEqual(postValue, 0f)) return null;

        var summed = convention == Release1ShortNoticeValueConvention.PerUnit
            ? consumedCount * preValue
            : preValue - postValue;
        return float.IsFinite(summed) && summed >= 0f ? summed : null;
    }

    public static bool AgreesWithObservation(
        Release1ShortNoticeValueConvention convention,
        int preQuantity,
        float preValue,
        int postQuantity,
        float postValue)
    {
        if (!Enum.IsDefined(convention)) return false;
        if (preQuantity < 1 || postQuantity < 0 || postQuantity >= preQuantity) return false;
        if (!float.IsFinite(preValue) || preValue < 0f || !float.IsFinite(postValue) || postValue < 0f) return false;

        if (postQuantity == 0)
        {
            // A fully consumed slot gives one read, not two: there is no remaining quantity to
            // recover a per-unit price from, so PerUnit cannot be verified here and fails closed
            // rather than assuming an agreement it never checked. PerStack is checkable from this
            // single read alone: the consumed value (preValue - postValue) must be a real, positive
            // amount, and postValue must actually be at the empty-slot zero it claims to be.
            if (convention == Release1ShortNoticeValueConvention.PerUnit) return false;
            return NearlyEqual(postValue, 0f) && preValue - postValue > 0f;
        }

        return convention == Release1ShortNoticeValueConvention.PerUnit
            ? NearlyEqual(postValue, preValue)
            : postValue < preValue && postValue > 0f;
    }
}
