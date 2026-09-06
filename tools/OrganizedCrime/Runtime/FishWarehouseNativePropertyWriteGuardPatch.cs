using HarmonyLib;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

internal static class FishWarehouseNativePropertyWriteGuardPatch
{
    private static Func<bool> _isAuthoritativeHost = () => false;
    private static Func<bool> _isDegraded = () => false;

    public static void Apply(HarmonyLib.Harmony harmony, Func<bool> isAuthoritativeHost, Func<bool> isDegraded)
    {
        _isAuthoritativeHost = isAuthoritativeHost;
        _isDegraded = isDegraded;
        var original = AccessTools.Method(
            typeof(Property),
            nameof(Property.WriteData),
            new[] { typeof(string) })
            ?? throw new MissingMethodException(typeof(Property).FullName, nameof(Property.WriteData));
        var prefix = AccessTools.Method(typeof(FishWarehouseNativePropertyWriteGuardPatch), nameof(Prefix))
            ?? throw new MissingMethodException(typeof(FishWarehouseNativePropertyWriteGuardPatch).FullName, nameof(Prefix));
        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    private static bool Prefix(
        Property __instance,
        ref Il2CppSystem.Collections.Generic.List<string> __result)
    {
        return ApplyWriteGuardDecision(EvaluateWriteGuard(__instance), ref __result);
    }

    internal static FishWarehousePropertyWriteGuardDecision EvaluateWriteGuard(Property property)
    {
        if (property is null)
            return FishWarehousePropertyWriteGuardDecision.RunOriginal;

        try
        {
            return EvaluateWriteGuard(_isAuthoritativeHost(), property.PropertyCode, _isDegraded());
        }
        catch
        {
            return FishWarehousePropertyWriteGuardDecision.RunOriginal;
        }
    }

    internal static FishWarehousePropertyWriteGuardDecision EvaluateWriteGuard(
        bool isAuthoritativeHost,
        string? propertyCode,
        bool isDegraded) =>
        isAuthoritativeHost &&
        isDegraded &&
        string.Equals(propertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal)
            ? FishWarehousePropertyWriteGuardDecision.SuppressWithEmptyResult
            : FishWarehousePropertyWriteGuardDecision.RunOriginal;

    internal static bool ApplyWriteGuardDecision(
        FishWarehousePropertyWriteGuardDecision decision,
        ref Il2CppSystem.Collections.Generic.List<string> __result)
    {
        if (decision == FishWarehousePropertyWriteGuardDecision.RunOriginal)
            return true;

        __result = new Il2CppSystem.Collections.Generic.List<string>();
        return false;
    }

    public static void Reset()
    {
        _isAuthoritativeHost = () => false;
        _isDegraded = () => false;
    }
}
