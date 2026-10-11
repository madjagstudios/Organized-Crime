using System.Diagnostics.CodeAnalysis;

namespace OrganizedCrime.Model;

/// <summary>
/// The journal side of Chapter 3: one quest, reused per job, with its marker
/// on the frozen drop. The key is namespaced so it can never collide with a
/// <c>release1.*</c> catalog key in the shared native presentation.
/// </summary>
public static class Chapter3QuestCopy
{
    public const string QuestKey = "chapter3.job";

    internal static string CashDepositScope(int jobId) => $"chapter3.cash:{jobId}:deposit";
    internal static string CashPaymentScope(int jobId) => $"chapter3.cash:{jobId}:payment";

    public const string QuestTitle = "Family Work";

    public const string QuestDescription = "Jobs for the family, through Nell.";

    /// <summary>
    /// Whether a delivery carries a mappable drop position. The planner and
    /// the no-quest warning share this so they can never disagree.
    /// </summary>
    public static bool HasMappablePosition([NotNullWhen(true)] Chapter3DeadDropDelivery? delivery) =>
        delivery is not null &&
        double.IsFinite(delivery.DropX) && double.IsFinite(delivery.DropY) && double.IsFinite(delivery.DropZ);

    /// <summary>
    /// Whether a recovery carries two mappable drop positions. Either end
    /// missing or non-finite means no quest at all: a half-marked journal is
    /// worse than none.
    /// </summary>
    public static bool HasMappablePosition([NotNullWhen(true)] Chapter3PackageRecovery? recovery) =>
        recovery is not null &&
        double.IsFinite(recovery.Pickup.X) && double.IsFinite(recovery.Pickup.Y) && double.IsFinite(recovery.Pickup.Z) &&
        double.IsFinite(recovery.Handoff.X) && double.IsFinite(recovery.Handoff.Y) && double.IsFinite(recovery.Handoff.Z);

    /// <summary>
    /// The dead-drop entry: bricks, product and drop from the frozen delivery.
    /// Blank names fall back to generics so the journal never reads half-empty.
    /// </summary>
    public static string DeadDropEntryText(int brickCount, string? productName, string? dropName)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "your best product" : productName;
        var drop = string.IsNullOrWhiteSpace(dropName) ? "the drop" : dropName;
        return $"Drop {brickCount} bricks of {product} at {drop}";
    }

    public static string RecoveryPickupEntryText(string? pickupName)
    {
        var pickup = string.IsNullOrWhiteSpace(pickupName) ? "the drop" : pickupName;
        return $"Pick up the package at {pickup}";
    }

    /// <summary>
    /// OC-136: the demand's journal entries share the one Chapter 3
    /// quest with the job entries; the progress lines re-project as the
    /// counts move.
    /// </summary>
    public static string DemandEntryTitle(int quantity, string? productName)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        return $"Family demand: {quantity} {product}";
    }

    public static string DemandMadeLine(int freshCount, int quantity) =>
        $"Made in the warehouse: {freshCount}/{quantity}";

    public static string DemandHandedLine(int delivered, int quantity) =>
        $"Hand over at the HQ family locker (back of the row nearest Arthur's desk): {delivered}/{quantity}";

    /// <summary>
    /// OC-117: the open expansion offer's journal line. Markerless: the
    /// player answers through Nell, not by walking anywhere.
    /// </summary>
    public static string WarehouseCapacityOfferLine(int capacity, int price) =>
        $"Warehouse expansion: {capacity} staff for ${price:N0} (reply to Nell)";

    /// <summary>OC-118. The markerless quest line while Campbell's protection covers the player.</summary>
    public static string ProtectionLine(int chapterDay) =>
        $"Campbell's protection: until day {chapterDay}";

    public static string RecoveryHandoffEntryText(string? handoffName)
    {
        var handoff = string.IsNullOrWhiteSpace(handoffName) ? "the drop" : handoffName;
        return $"Bring it to {handoff}";
    }
}
