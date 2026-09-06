using System.Text.Json;
using Il2Cpp;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseNativeEmployeeReplayAdapter : IFishWarehouseNativeEmployeeReplayAdapter
{
    private readonly PropertyData _propertyData;
    private readonly Property _property;
    private readonly EmployeeManager? _employeeManager;
    private readonly HashSet<string> _fallbackConfigurationApplied = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inventoryRestored = new(StringComparer.Ordinal);

    public FishWarehouseNativeEmployeeReplayAdapter(
        PropertyData propertyData,
        Property property,
        EmployeeManager? employeeManager = null,
        IReadOnlyCollection<string>? inventoryRestoredEmployeeGuids = null)
    {
        _propertyData = propertyData ?? throw new ArgumentNullException(nameof(propertyData));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _employeeManager = employeeManager;
        foreach (var guid in inventoryRestoredEmployeeGuids ?? Array.Empty<string>())
            _inventoryRestored.Add(CanonicalGuid(guid));
    }

    public IReadOnlyCollection<string> InventoryRestoredEmployeeGuids => _inventoryRestored;

    public bool TryIsGuidRegistered(string savedGuid, out bool isRegistered, out string? failureReason)
    {
        isRegistered = false;
        failureReason = null;
        if (!TryInvokeGuidInterop(savedGuid, GUIDManager.IsGUIDAlreadyRegistered, out isRegistered))
        {
            failureReason = $"malformed-guid (guid={savedGuid})";
            return false;
        }

        return true;
    }

    public FishWarehouseEmployeePrimaryLoadResult TryLoadPrimary(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        out string? failureReason)
    {
        failureReason = null;
        if (!TryFindCapturedData(descriptor, out var data, out failureReason))
            return FishWarehouseEmployeePrimaryLoadResult.LoaderUnavailable;

        try
        {
            NPCLoader? loader = LoadManager.Instance?.GetNPCLoader(data.DataType);
            if (loader is null)
            {
                failureReason = $"missing-npc-loader (type={data.DataType})";
                return FishWarehouseEmployeePrimaryLoadResult.LoaderUnavailable;
            }

            loader?.Load(data);
            return FishWarehouseEmployeePrimaryLoadResult.Started;
        }
        catch (Exception ex)
        {
            failureReason = $"npc-loader-failure (type={descriptor.DataType}): {ex.Message}";
            return FishWarehouseEmployeePrimaryLoadResult.Failed;
        }
    }

    public FishWarehouseEmployeeObservationResult TryObserveEmployee(
        string savedGuid,
        out FishWarehouseEmployeeReplayObservedState? observation,
        out string? failureReason)
    {
        observation = null;
        failureReason = null;
        if (!TryIsGuidRegistered(savedGuid, out var registered, out failureReason))
            return FishWarehouseEmployeeObservationResult.Failed;

        if (!registered)
            return FishWarehouseEmployeeObservationResult.Pending;

        if (!TryInvokeGuidInterop(savedGuid, nativeGuid => GUIDManager.GetObject<Packager>(nativeGuid), out Packager? packager))
        {
            failureReason = $"malformed-guid (guid={savedGuid})";
            return FishWarehouseEmployeeObservationResult.Failed;
        }

        if (!IsAlive(packager))
            return FishWarehouseEmployeeObservationResult.Pending;

        try
        {
            var transform = packager!.transform;
            var position = transform.position;
            var rotation = transform.rotation;
            var configuration = packager.configuration;
            var moveItemData = IsAlive(packager.MoveItemBehaviour)
                ? packager.MoveItemBehaviour.GetSaveData()
                : null;
            var moveItem = moveItemData is null
                ? null
                : new FishWarehouseEmployeeMoveItemState(
                    moveItemData.SourceGUID,
                    moveItemData.DestinationGUID,
                    moveItemData.TemplateItemJSON,
                    moveItemData.GrabbedItemQuantity);
            observation = new FishWarehouseEmployeeReplayObservedState(
                IsAlive: true,
                Guid: packager.GUID.ToString(),
                Identity: "Packager",
                Id: packager.ID,
                FirstName: packager.FirstName,
                LastName: packager.LastName,
                IsMale: packager.IsMale,
                AppearanceIndex: packager.AppearanceIndex,
                PositionX: position.x,
                PositionY: position.y,
                PositionZ: position.z,
                RotationX: rotation.x,
                RotationY: rotation.y,
                RotationZ: rotation.z,
                RotationW: rotation.w,
                PropertyCode: packager.AssignedProperty?.PropertyCode ?? string.Empty,
                PaidForToday: packager.PaidForToday,
                MoveItem: moveItem,
                HomeGuid: configuration?.Home?.SelectedObject?.GUID.ToString() ?? string.Empty,
                StationGuids: ReadStationGuids(packager));
            return FishWarehouseEmployeeObservationResult.Ready;
        }
        catch (Exception ex)
        {
            failureReason = $"employee-observation-failure (guid={savedGuid}): {ex.Message}";
            return FishWarehouseEmployeeObservationResult.Failed;
        }
    }

    public FishWarehouseEmployeeFallbackCreationResult TryCreateFallback(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        FishWarehouseEmployeeReplayExpectedState expected,
        out string? failureReason)
    {
        failureReason = null;
        if (!TryIsGuidRegistered(descriptor.Guid, out var registered, out failureReason))
            return FishWarehouseEmployeeFallbackCreationResult.Failed;

        if (registered)
        {
            failureReason = $"fallback creation rejected existing GUID (guid={descriptor.Guid})";
            return FishWarehouseEmployeeFallbackCreationResult.Failed;
        }

        var employeeManager = _employeeManager ?? EmployeeManager.Instance;
        if (!IsAlive(employeeManager))
        {
            failureReason = "fallback creation rejected because EmployeeManager was unavailable";
            return FishWarehouseEmployeeFallbackCreationResult.Failed;
        }

        try
        {
            employeeManager!.RpcLogic___CreateEmployee_311954683(
                _property,
                EEmployeeType.Handler,
                expected.FirstName,
                expected.LastName,
                expected.Id,
                expected.IsMale,
                expected.AppearanceIndex,
                new Vector3(expected.PositionX, expected.PositionY, expected.PositionZ),
                new Quaternion(expected.RotationX, expected.RotationY, expected.RotationZ, expected.RotationW),
                expected.Guid);
            return FishWarehouseEmployeeFallbackCreationResult.Started;
        }
        catch (Exception ex)
        {
            failureReason = $"fallback creation failed: {ex.Message}";
            return FishWarehouseEmployeeFallbackCreationResult.Failed;
        }
    }

    public FishWarehouseEmployeeFallbackConfigurationResult TryConfigureFallback(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        FishWarehouseEmployeeReplayExpectedState expected,
        out string? failureReason)
    {
        failureReason = null;
        var guidKey = CanonicalGuid(descriptor.Guid);
        if (_fallbackConfigurationApplied.Contains(guidKey))
            return FishWarehouseEmployeeFallbackConfigurationResult.Configured;

        if (!TryIsGuidRegistered(descriptor.Guid, out var registered, out failureReason))
            return FishWarehouseEmployeeFallbackConfigurationResult.Failed;

        if (!registered ||
            !TryInvokeGuidInterop(descriptor.Guid, nativeGuid => GUIDManager.GetObject<Packager>(nativeGuid), out Packager? packager))
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
        }

        if (!IsAlive(packager))
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

        if (!TryFindCapturedData(descriptor, out var data, out failureReason) ||
            !TryReadPackagerConfiguration(data, out var configuration, out failureReason))
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
        }

        try
        {
            var packagerConfiguration = packager!.configuration;
            var home = packagerConfiguration?.Home;
            var stations = packagerConfiguration?.Stations;
            var routes = packagerConfiguration?.Routes;
            if (GetFallbackConfigurationSurfaceResult(
                    packagerConfiguration is not null,
                    home is not null,
                    stations is not null,
                    routes is not null) == FishWarehouseEmployeeFallbackConfigurationResult.Pending)
            {
                return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
            }

            packager!.PaidForToday = expected.PaidForToday;
            home!.Load(configuration!.Bed);
            stations!.Load(configuration.Stations);
            // Restore the configured dock→interior route(s). The native
            // PackagerConfigurationData already carries Routes (captured with Bed and
            // Stations); the fallback path previously loaded only Bed and Stations,
            // so a route-configured employee came back idle on restart.
            routes!.Load(configuration.Routes);

            // A route's SourceGUID/DestinationGUID may not be registered yet at this
            // replay frame; RouteListField.Load then binds the route to null endpoints
            // and it never becomes a live route (the employee stays idle, and a later
            // re-save captures him without the route — the route decays away). So do
            // not accept the configuration as applied until every referenced GUID —
            // home, stations, and each route endpoint — is registered. Until then,
            // stay Pending: this method is re-invoked each tick and re-applies the
            // load, so the route rebinds to real entities as soon as they resolve.
            if (!AllConfigurationGuidsResolved(expected, configuration))
                return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

            _fallbackConfigurationApplied.Add(guidKey);
            return FishWarehouseEmployeeFallbackConfigurationResult.Configured;
        }
        catch (Exception ex)
        {
            failureReason = $"fallback configuration failed: {ex.Message}";
            return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
        }
    }

    public FishWarehouseEmployeeActiveMoveRestoreResult TryRestoreActiveMove(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        FishWarehouseEmployeeReplayExpectedState expected,
        out string? failureReason)
    {
        failureReason = null;
        if (expected.MoveItem is null)
            return FishWarehouseEmployeeActiveMoveRestoreResult.NotRequired;

        if (!TryIsGuidRegistered(descriptor.Guid, out var registered, out failureReason))
            return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;

        if (!registered ||
            !TryInvokeGuidInterop(descriptor.Guid, nativeGuid => GUIDManager.GetObject<Packager>(nativeGuid), out Packager? packager) ||
            !IsAlive(packager))
        {
            return FishWarehouseEmployeeActiveMoveRestoreResult.Pending;
        }

        var livePackager = packager!;
        if (!System.Guid.TryParse(expected.MoveItem.SourceGuid, out var sourceGuid) ||
            !System.Guid.TryParse(expected.MoveItem.DestinationGuid, out var destinationGuid))
        {
            failureReason = $"active-move-malformed-guid (employee={descriptor.Guid})";
            return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;
        }

        if (!IsGuidRegistered(expected.MoveItem.SourceGuid) ||
            !IsGuidRegistered(expected.MoveItem.DestinationGuid))
        {
            failureReason = $"active-move-endpoints-pending (employee={descriptor.Guid})";
            return FishWarehouseEmployeeActiveMoveRestoreResult.Pending;
        }

        try
        {
            if (IsAlive(livePackager.MoveItemBehaviour))
            {
                var existingMove = livePackager.MoveItemBehaviour.GetSaveData();
                if (existingMove is not null)
                {
                    if (string.Equals(existingMove.SourceGUID, expected.MoveItem.SourceGuid, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existingMove.DestinationGUID, expected.MoveItem.DestinationGuid, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existingMove.TemplateItemJSON, expected.MoveItem.TemplateItemJson, StringComparison.Ordinal) &&
                        existingMove.GrabbedItemQuantity == expected.MoveItem.GrabbedItemQuantity)
                    {
                        return FishWarehouseEmployeeActiveMoveRestoreResult.Restored;
                    }

                    failureReason = $"active-move-existing-mismatch (employee={descriptor.Guid})";
                    return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;
                }
            }

            if (!_inventoryRestored.Contains(CanonicalGuid(descriptor.Guid)))
            {
                if (string.IsNullOrWhiteSpace(expected.InventoryJson) ||
                    !ItemSet.TryDeserialize(expected.InventoryJson, out var inventory) ||
                    inventory is null ||
                    livePackager.Inventory is null ||
                    livePackager.Inventory.ItemSlots is null)
                {
                    failureReason = $"active-move-inventory-unavailable (employee={descriptor.Guid})";
                    return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;
                }

                inventory.LoadTo(livePackager.Inventory.ItemSlots);
                _inventoryRestored.Add(CanonicalGuid(descriptor.Guid));
            }

            if (!IsAlive(livePackager.MoveItemBehaviour))
                return FishWarehouseEmployeeActiveMoveRestoreResult.Pending;

            livePackager.MoveItemBehaviour.Load(new MoveItemData(
                expected.MoveItem.TemplateItemJson,
                expected.MoveItem.GrabbedItemQuantity,
                new Il2CppSystem.Guid(sourceGuid.ToString("D")),
                new Il2CppSystem.Guid(destinationGuid.ToString("D"))));
            return FishWarehouseEmployeeActiveMoveRestoreResult.Restored;
        }
        catch (Exception ex)
        {
            failureReason = $"active-move-restore-failed (employee={descriptor.Guid}): {ex.Message}";
            return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;
        }
    }

    internal static FishWarehouseEmployeeFallbackConfigurationResult GetFallbackConfigurationSurfaceResult(
        bool configurationAvailable,
        bool homeAvailable,
        bool stationsAvailable,
        bool routesAvailable) =>
        configurationAvailable && homeAvailable && stationsAvailable && routesAvailable
            ? FishWarehouseEmployeeFallbackConfigurationResult.Configured
            : FishWarehouseEmployeeFallbackConfigurationResult.Pending;

    // True only when every GUID the configuration references — home, each station,
    // and each route's source and destination — is already registered. A route
    // whose endpoints are not yet registered binds to null and never activates, so
    // the caller stays Pending and re-applies until this returns true.
    private static bool AllConfigurationGuidsResolved(
        FishWarehouseEmployeeReplayExpectedState expected,
        PackagerConfigurationData configuration)
    {
        if (!IsGuidRegistered(expected.HomeGuid))
            return false;

        foreach (var stationGuid in expected.StationGuids)
        {
            if (!IsGuidRegistered(stationGuid))
                return false;
        }

        var routes = configuration.Routes?.Routes;
        if (routes is not null)
        {
            foreach (var route in routes)
            {
                if (route is null)
                    continue;
                if (!IsGuidRegistered(route.SourceGUID) || !IsGuidRegistered(route.DestinationGUID))
                    return false;
            }
        }

        return true;
    }

    private static bool IsGuidRegistered(string? guid)
    {
        // An empty GUID references nothing, so there is nothing to wait on.
        if (string.IsNullOrWhiteSpace(guid))
            return true;

        return TryInvokeGuidInterop(guid, GUIDManager.IsGUIDAlreadyRegistered, out var registered) && registered;
    }

    internal static bool TryInvokeGuidInterop<T>(
        string savedGuid,
        Func<Il2CppSystem.Guid, T> interopCall,
        out T result)
    {
        if (!System.Guid.TryParse(savedGuid, out var parsed))
        {
            result = default!;
            return false;
        }

        result = interopCall(new Il2CppSystem.Guid(parsed.ToString()));
        return true;
    }

    private bool TryFindCapturedData(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        out DynamicSaveData data,
        out string? failureReason)
    {
        data = null!;
        failureReason = null;
        if (!System.Guid.TryParse(descriptor.Guid, out var expectedGuid))
        {
            failureReason = $"malformed-guid (guid={descriptor.Guid})";
            return false;
        }

        foreach (var candidate in _propertyData.Employees)
        {
            if (!TryReadGuid(candidate.BaseData, out var candidateGuid) || candidateGuid != expectedGuid)
                continue;

            if (!string.Equals(candidate.DataType, descriptor.DataType, StringComparison.Ordinal))
            {
                failureReason = $"captured-data-type-mismatch (guid={descriptor.Guid})";
                return false;
            }

            data = candidate;
            return true;
        }

        failureReason = $"captured-employee-payload-missing (guid={descriptor.Guid})";
        return false;
    }

    private static bool TryReadGuid(string baseData, out System.Guid guid)
    {
        guid = default;
        try
        {
            using var document = JsonDocument.Parse(baseData);
            return document.RootElement.TryGetProperty("GUID", out var savedGuid) &&
                savedGuid.ValueKind == JsonValueKind.String &&
                System.Guid.TryParse(savedGuid.GetString(), out guid);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadPackagerConfiguration(
        DynamicSaveData data,
        out PackagerConfigurationData? configuration,
        out string? failureReason)
    {
        configuration = null;
        failureReason = null;
        foreach (var additionalData in data.AdditionalDatas)
        {
            if (!string.Equals(additionalData.Name, "Configuration", StringComparison.Ordinal))
                continue;

            try
            {
                configuration = JsonUtility.FromJson<PackagerConfigurationData>(additionalData.Contents);
                if (configuration is null)
                {
                    failureReason = "PackagerConfigurationData was unavailable";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"PackagerConfigurationData parse failed: {ex.Message}";
                return false;
            }
        }

        failureReason = "PackagerConfigurationData was unavailable";
        return false;
    }

    private static string CanonicalGuid(string savedGuid) =>
        System.Guid.TryParse(savedGuid, out var parsed) ? parsed.ToString("D") : savedGuid;

    private static IReadOnlyList<string> ReadStationGuids(Packager packager)
    {
        var selectedObjects = packager.configuration?.Stations?.SelectedObjects;
        if (selectedObjects is null)
            return Array.Empty<string>();

        var guids = new List<string>(selectedObjects.Count);
        for (var index = 0; index < selectedObjects.Count; index++)
        {
            var buildable = selectedObjects[index];
            if (IsAlive(buildable))
                guids.Add(buildable!.GUID.ToString());
        }

        return guids;
    }

    private static bool IsAlive(UnityEngine.Object? value)
    {
        try
        {
            return value is not null && value != null;
        }
        catch
        {
            return false;
        }
    }
}
