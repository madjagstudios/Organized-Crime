namespace OrganizedCrime.Model;

/// <summary>
/// Tunable Chapter 3 payment bound, as a named constant. No single Chapter 3
/// payment may exceed this: it is far above the largest real Chapter 3 amount
/// (the $30,000 weekly cut and five-figure make-goods and extensions), so it
/// only ever trips on a bug. Separate from the Chief's own ladder cap, which
/// still bounds the single-argument debit form he uses.
/// </summary>
public static class Chapter3PaymentTuning
{
    public const int MaximumPayment = 250_000;
    public const int WarehousePrice = 300_000;

    /// <summary>
    /// OC-117: the warehouse expansion prices. Both sit under
    /// <see cref="MaximumPayment"/>, so the capacity purchase debits
    /// through the bounded single-argument form, unlike the warehouse
    /// purchase itself.
    /// </summary>
    public const int WarehouseCapacityTier2Price = 50_000;
    public const int WarehouseCapacityTier3Price = 100_000;

    public static int CapacityPriceForTier(int tier) => tier switch
    {
        2 => WarehouseCapacityTier2Price,
        3 => WarehouseCapacityTier3Price,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), $"Chapter 3 warehouse capacity tier has no price: {tier}.")
    };
}
