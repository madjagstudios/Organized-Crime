namespace OrganizedCrime.Model;

/// <summary>
/// OC-117: the owner-tunable Fish Warehouse staff capacity tiers. Tier 1
/// applies as soon as the warehouse is owned (including existing saves and
/// the dev-key warehouse); tiers 2 and 3 unlock through delivered production
/// demands and are then bought through Nell. One table: capacities,
/// unlock thresholds (lifetime completed demands), and prices each have a
/// single row per tier so tuning never edits call sites.
/// </summary>
public static class Chapter3WarehouseCapacityTiers
{
    public const int MinTier = 1;
    public const int MaxTier = 3;

    /// <summary>
    /// Tier 1 capacity. Also the fallback whenever Chapter 3 is not Active:
    /// any owned warehouse holds 12.
    /// </summary>
    public const int Tier1Capacity = 12;

    public static int CapacityForTier(int tier) => tier switch
    {
        1 => Tier1Capacity,
        2 => 16,
        3 => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), $"Chapter 3 warehouse capacity tier was unknown: {tier}.")
    };

    /// <summary>
    /// Lifetime completed demands needed before a tier can be offered. Tier
    /// 1 needs none; the counts never reset, so an already-earned tier 3
    /// opens right after tier 2 is paid for.
    /// </summary>
    public static int UnlockThresholdForTier(int tier) => tier switch
    {
        1 => 0,
        2 => 1,
        3 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), $"Chapter 3 warehouse capacity tier was unknown: {tier}.")
    };

    public static bool IsValidTier(int tier) => tier is >= MinTier and <= MaxTier;

    /// <summary>
    /// The tier Nell can sell right now, derived from state (never saved):
    /// the warehouse must be story-purchased, the current tier below the
    /// top, and the lifetime completed-demand count at the next tier's
    /// threshold. Null when no offer is open.
    /// </summary>
    public static int? OpenOfferTier(bool storyPurchased, int tier, int demandsCompleted)
    {
        if (!storyPurchased || !IsValidTier(tier) || tier >= MaxTier || demandsCompleted < 0)
            return null;
        var next = tier + 1;
        return demandsCompleted >= UnlockThresholdForTier(next) ? next : null;
    }
}
