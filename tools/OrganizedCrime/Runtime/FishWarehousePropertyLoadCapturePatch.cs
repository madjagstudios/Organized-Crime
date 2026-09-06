using HarmonyLib;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

internal static class FishWarehousePropertyLoadCapturePatch
{
    private static Func<bool> _isAuthoritativeHost = () => false;
    private static Func<string?> _activeSaveFolder = () => null;
    private static FishWarehousePropertyCaptureService? _captureService;
    private static Action<string> _log = _ => { };

    public static void Apply(
        HarmonyLib.Harmony harmony,
        Func<bool> isAuthoritativeHost,
        Func<string?> activeSaveFolder,
        FishWarehousePropertyCaptureService captureService,
        Action<string>? log = null)
    {
        _isAuthoritativeHost = isAuthoritativeHost;
        _activeSaveFolder = activeSaveFolder;
        _captureService = captureService;
        _log = log ?? (_ => { });
        var original = AccessTools.Method(
            typeof(PropertyLoader),
            nameof(PropertyLoader.Load),
            new[] { typeof(PropertyData), typeof(string) })
            ?? throw new MissingMethodException(typeof(PropertyLoader).FullName, nameof(PropertyLoader.Load));
        var prefix = AccessTools.Method(typeof(FishWarehousePropertyLoadCapturePatch), nameof(Prefix))
            ?? throw new MissingMethodException(typeof(FishWarehousePropertyLoadCapturePatch).FullName, nameof(Prefix));
        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    private static bool Prefix(PropertyData __0, string __1)
    {
        _ = __1;
        if (!_isAuthoritativeHost())
            return true;

        var activeSaveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return true;

        string? propertyCode;
        try
        {
            propertyCode = __0.PropertyCode;
        }
        catch (Exception ex)
        {
            return HandleAuthoritativePropertyCodeReadFailure(activeSaveFolder, _captureService, ex);
        }

        if (FishWarehousePropertyCapturePolicy.ShouldPassThrough(true, propertyCode, activeSaveFolder))
            return true;

        try
        {
            var captureService = _captureService;
            if (captureService is null)
            {
                _log("WARNING: Fish Warehouse native property load was suppressed because the capture service was unavailable.");
                return false;
            }

            var generationId = captureService.GenerationId ?? Guid.NewGuid().ToString("N");
            var result = captureService.TryCapture(__0, activeSaveFolder!, generationId);
            if (result.State != FishWarehousePropertyCaptureState.Captured)
            {
                _log($"WARNING: Fish Warehouse native property load remains suppressed ({result.State}): {result.FailureReason}");
            }

            return false;
        }
        catch (Exception ex)
        {
            _log($"WARNING: Fish Warehouse native property capture threw after the target was identified; native load remains suppressed: {ex}");
            return false;
        }
    }

    internal static bool HandleAuthoritativePropertyCodeReadFailure(
        string? activeSaveFolder,
        FishWarehousePropertyCaptureService? captureService,
        Exception exception)
    {
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return true;

        if (captureService is null)
        {
            _log("WARNING: Fish Warehouse native property load was suppressed because PropertyCode could not be read and the capture service was unavailable.");
            return false;
        }

        try
        {
            var generationId = captureService.GenerationId ?? Guid.NewGuid().ToString("N");
            var result = captureService.RecordCaptureFailure(
                activeSaveFolder,
                generationId,
                $"Fish Warehouse native property discriminator PropertyCode could not be read: {exception.Message}");
            _log($"WARNING: Fish Warehouse native property load remains suppressed ({result.State}) because PropertyCode could not be read: {result.FailureReason}");
        }
        catch (Exception recordFailure)
        {
            _log($"WARNING: Fish Warehouse native property load remains suppressed because PropertyCode could not be read and capture failure recording threw: {recordFailure}");
        }

        return false;
    }

    private static void VerifyInstalledInterop(
        PropertyLoader loader,
        PropertyData propertyData,
        Property property,
        DynamicSaveData dynamicSaveData,
        string saveFolder)
    {
        loader.Load(propertyData, saveFolder);
        Il2CppSystem.Collections.Generic.List<string> writtenFiles = property.WriteData(saveFolder);
        _ = writtenFiles;
        _ = propertyData.PropertyCode;
        _ = propertyData.IsOwned;
        _ = propertyData.Objects;
        _ = propertyData.Employees;
        _ = dynamicSaveData.DataType;
        _ = dynamicSaveData.BaseData;
        _ = dynamicSaveData.AdditionalDatas;
    }

    public static void Reset()
    {
        _isAuthoritativeHost = () => false;
        _activeSaveFolder = () => null;
        _captureService = null;
        _log = _ => { };
    }
}
