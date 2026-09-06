using HarmonyLib;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI.Management;
using MelonLoader;

namespace OrganizedCrime.Runtime;

internal static class FishWarehouseManagementCanvasPatch
{
    private static Property? _targetProperty;
    private static bool _failureReported;

    public static void Apply(HarmonyLib.Harmony harmony)
    {
        var original = AccessTools.Method(
            typeof(ManagementWorldspaceCanvas),
            nameof(ManagementWorldspaceCanvas.GetConfigurablesToShow));
        var prefix = AccessTools.Method(
            typeof(FishWarehouseManagementCanvasPatch),
            nameof(AlignCurrentProperty));
        if (original is null || prefix is null)
            throw new MissingMethodException("Could not resolve the management canvas configurable filter patch surface.");

        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    public static void SetTargetProperty(Property? property)
    {
        _targetProperty = property;
        if (property is null)
            _failureReported = false;
    }

    private static void AlignCurrentProperty(ManagementWorldspaceCanvas __instance)
    {
        try
        {
            var target = _targetProperty;
            if (target is null || __instance is null)
                return;

            var canvasPropertyCode = __instance.CurrentProperty?.PropertyCode ?? string.Empty;
            var playerPropertyCode = Player.Local?.CurrentProperty?.PropertyCode ?? string.Empty;
            if (FishWarehouseManagementEligibilityDiagnostic.ShouldAlignCanvas(
                    __instance.IsOpen,
                    canvasPropertyCode,
                    playerPropertyCode,
                    target.IsOwned))
            {
                __instance.CurrentProperty = target;
            }
        }
        catch (Exception ex)
        {
            if (_failureReported)
                return;

            _failureReported = true;
            MelonLogger.Warning($"[Organized Crime] Fish Warehouse management canvas alignment failed safely: {ex}");
        }
    }
}
