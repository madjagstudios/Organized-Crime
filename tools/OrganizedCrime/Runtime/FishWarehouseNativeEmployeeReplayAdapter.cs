using System.Text.Json;
using Il2Cpp;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Runtime.Platform;
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

        if (!TryResolveEmployee(savedGuid, out var resolved, out var identity, out var malformedGuid))
        {
            if (malformedGuid)
            {
                failureReason = $"malformed-guid (guid={savedGuid})";
                return FishWarehouseEmployeeObservationResult.Failed;
            }

            return FishWarehouseEmployeeObservationResult.Pending;
        }

        try
        {
            var liveEmployee = (Employee)resolved!;
            var transform = liveEmployee.transform;
            var position = transform.position;
            var rotation = transform.rotation;
            var moveItemData = UnityLife.IsAlive(liveEmployee.MoveItemBehaviour)
                ? liveEmployee.MoveItemBehaviour.GetSaveData()
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
                Guid: liveEmployee.GUID.ToString(),
                Identity: identity!,
                Id: liveEmployee.ID,
                FirstName: liveEmployee.FirstName,
                LastName: liveEmployee.LastName,
                IsMale: liveEmployee.IsMale,
                AppearanceIndex: liveEmployee.AppearanceIndex,
                PositionX: position.x,
                PositionY: position.y,
                PositionZ: position.z,
                RotationX: rotation.x,
                RotationY: rotation.y,
                RotationZ: rotation.z,
                RotationW: rotation.w,
                PropertyCode: liveEmployee.AssignedProperty?.PropertyCode ?? string.Empty,
                PaidForToday: liveEmployee.PaidForToday,
                MoveItem: moveItem,
                HomeGuid: ReadHomeGuid(resolved!, identity!),
                StationGuids: ReadStationGuids(resolved!, identity!),
                SuppliesGuid: ReadSuppliesGuid(resolved!, identity!));
            return FishWarehouseEmployeeObservationResult.Ready;
        }
        catch (Exception ex)
        {
            failureReason = $"employee-observation-failure (guid={savedGuid}): {ex.Message}";
            return FishWarehouseEmployeeObservationResult.Failed;
        }
    }

    // Resolves a live employee of any of the four native types. Packager is
    // attempted first so the established path resolves exactly as before;
    // each attempt is exception-safe because a wrong-type registry lookup
    // may return null or throw depending on the registry state.
    private static bool TryResolveEmployee(
        string savedGuid,
        out UnityEngine.Object? employee,
        out string? identity,
        out bool malformedGuid)
    {
        employee = null;
        identity = null;
        malformedGuid = !System.Guid.TryParse(savedGuid, out _);
        if (malformedGuid)
            return false;

        var packager = TryGetAliveObject<Packager>(savedGuid);
        if (packager is not null)
        {
            employee = packager;
            identity = "Packager";
            return true;
        }

        var chemist = TryGetAliveObject<Chemist>(savedGuid);
        if (chemist is not null)
        {
            employee = chemist;
            identity = "Chemist";
            return true;
        }

        var botanist = TryGetAliveObject<Botanist>(savedGuid);
        if (botanist is not null)
        {
            employee = botanist;
            identity = "Botanist";
            return true;
        }

        var cleaner = TryGetAliveObject<Cleaner>(savedGuid);
        if (cleaner is not null)
        {
            employee = cleaner;
            identity = "Cleaner";
            return true;
        }

        return false;
    }

    private static T? TryGetAliveObject<T>(string savedGuid)
        where T : UnityEngine.Object
    {
        try
        {
            if (!TryInvokeGuidInterop(savedGuid, nativeGuid => GUIDManager.GetObject<T>(nativeGuid), out T? found))
                return null;

            return UnityLife.IsAlive(found) ? found : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadHomeGuid(UnityEngine.Object employee, string identity)
    {
        ObjectField? home = identity switch
        {
            "Chemist" => ((Chemist)employee).configuration?.Home,
            "Botanist" => ((Botanist)employee).configuration?.Home,
            "Cleaner" => ((Cleaner)employee).configuration?.Home,
            _ => ((Packager)employee).configuration?.Home,
        };

        return home?.SelectedObject?.GUID.ToString() ?? string.Empty;
    }

    private static IReadOnlyList<string> ReadStationGuids(UnityEngine.Object employee, string identity) => identity switch
    {
        "Chemist" => ReadSelectedObjectGuids(((Chemist)employee).configuration?.Stations),
        "Botanist" => ReadSelectedObjectGuids(((Botanist)employee).configuration?.Assigns),
        "Cleaner" => ReadSelectedObjectGuids(((Cleaner)employee).configuration?.Bins),
        _ => ReadStationGuids((Packager)employee),
    };

    private static IReadOnlyList<string> ReadSelectedObjectGuids(ObjectListField? stations)
    {
        var selectedObjects = stations?.SelectedObjects;
        if (selectedObjects is null)
            return Array.Empty<string>();

        var guids = new List<string>(selectedObjects.Count);
        for (var index = 0; index < selectedObjects.Count; index++)
        {
            var buildable = selectedObjects[index];
            if (UnityLife.IsAlive(buildable))
                guids.Add(buildable!.GUID.ToString());
        }

        return guids;
    }

    private static string ReadSuppliesGuid(UnityEngine.Object employee, string identity)
    {
        if (!string.Equals(identity, "Botanist", StringComparison.Ordinal))
            return string.Empty;

        var supplies = ((Botanist)employee).configuration?.Supplies;
        return supplies?.SelectedObject?.GUID.ToString() ?? string.Empty;
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
        if (!UnityLife.IsAlive(employeeManager))
        {
            failureReason = "fallback creation rejected because EmployeeManager was unavailable";
            return FishWarehouseEmployeeFallbackCreationResult.Failed;
        }

        try
        {
            employeeManager!.RpcLogic___CreateEmployee_311954683(
                _property,
                ResolveEmployeeType(descriptor.DataType),
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

    // Packager keeps today's Handler value: Packager is not an EEmployeeType
    // member, so the fallback creation call is unchanged for it. The other
    // three pass their matching member. Method bodies are unavailable in the
    // index, so what a Handler value creates — and the fallback creation
    // path for every type — is an owner-run check.
    private static EEmployeeType ResolveEmployeeType(string dataType) => dataType switch
    {
        "BotanistData" => EEmployeeType.Botanist,
        "ChemistData" => EEmployeeType.Chemist,
        "CleanerData" => EEmployeeType.Cleaner,
        _ => EEmployeeType.Handler,
    };

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

        if (!registered || !TryResolveEmployee(descriptor.Guid, out var resolved, out _, out _))
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
        }

        FishWarehouseEmployeeFallbackConfigurationResult configured;
        try
        {
            configured = ConfigureFallback(descriptor, expected, resolved!, out failureReason);
        }
        catch (Exception ex)
        {
            failureReason = $"fallback configuration failed: {ex.Message}";
            return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
        }

        if (configured == FishWarehouseEmployeeFallbackConfigurationResult.Configured)
            _fallbackConfigurationApplied.Add(guidKey);

        return configured;
    }

    private FishWarehouseEmployeeFallbackConfigurationResult ConfigureFallback(
        FishWarehouseEmployeeReplayDescriptor descriptor,
        FishWarehouseEmployeeReplayExpectedState expected,
        UnityEngine.Object resolved,
        out string? failureReason)
    {
        failureReason = null;

        // Preserved records have no typed payload; their RawJson carries the
        // same employee record, so the configuration is read from it instead.
        // A present-but-mismatched typed payload still fails, as before.
        string contentsJson;
        if (HasTypedPayload(descriptor))
        {
            if (!TryFindCapturedData(descriptor, out var data, out failureReason))
            {
                return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
            }

            if (!TryReadConfigurationContents(data, descriptor.DataType, out contentsJson, out failureReason))
            {
                return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
            }
        }
        else if (!TryReadConfigurationContents(descriptor.RawJson, descriptor.DataType, out contentsJson, out failureReason))
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
        }

        switch (descriptor.DataType)
        {
            case "ChemistData":
                if (!TryParseConfigurationData(contentsJson, "ChemistConfigurationData", out ChemistConfigurationData? chemistConfiguration, out failureReason))
                    return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
                return ApplyChemistConfiguration(expected, (Chemist)resolved, chemistConfiguration!);
            case "BotanistData":
                if (!TryParseConfigurationData(contentsJson, "BotanistConfigurationData", out BotanistConfigurationData? botanistConfiguration, out failureReason))
                    return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
                return ApplyBotanistConfiguration(expected, (Botanist)resolved, botanistConfiguration!);
            case "CleanerData":
                if (!TryParseConfigurationData(contentsJson, "CleanerConfigurationData", out CleanerConfigurationData? cleanerConfiguration, out failureReason))
                    return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
                return ApplyCleanerConfiguration(expected, (Cleaner)resolved, cleanerConfiguration!);
            default:
                if (!TryParseConfigurationData(contentsJson, "PackagerConfigurationData", out PackagerConfigurationData? packagerConfiguration, out failureReason))
                    return FishWarehouseEmployeeFallbackConfigurationResult.Failed;
                return ApplyPackagerConfiguration(expected, (Packager)resolved, packagerConfiguration!);
        }
    }

    private static FishWarehouseEmployeeFallbackConfigurationResult ApplyPackagerConfiguration(
        FishWarehouseEmployeeReplayExpectedState expected,
        Packager packager,
        PackagerConfigurationData configuration)
    {
        var packagerConfiguration = packager.configuration;
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

        packager.PaidForToday = expected.PaidForToday;
        home!.Load(configuration.Bed);
        stations!.Load(configuration.Stations);
        // Restore the configured dock->interior route(s). The native
        // PackagerConfigurationData already carries Routes (captured with Bed and
        // Stations); the fallback path previously loaded only Bed and Stations,
        // so a route-configured employee came back idle on restart.
        routes!.Load(configuration.Routes);

        // A route's SourceGUID/DestinationGUID may not be registered yet at this
        // replay frame; RouteListField.Load then binds the route to null endpoints
        // and it never becomes a live route (the employee stays idle, and a later
        // re-save captures him without the route -- the route decays away). So do
        // not accept the configuration as applied until every referenced GUID --
        // home, stations, and each route endpoint -- is registered. Until then,
        // stay Pending: this method is re-invoked each tick and re-applies the
        // load, so the route rebinds to real entities as soon as they resolve.
        if (!AllConfigurationGuidsResolved(expected, configuration))
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

        return FishWarehouseEmployeeFallbackConfigurationResult.Configured;
    }

    private static FishWarehouseEmployeeFallbackConfigurationResult ApplyChemistConfiguration(
        FishWarehouseEmployeeReplayExpectedState expected,
        Chemist chemist,
        ChemistConfigurationData configuration)
    {
        var chemistConfiguration = chemist.configuration;
        var home = chemistConfiguration?.Home;
        var stations = chemistConfiguration?.Stations;
        if (GetFallbackConfigurationSurfaceResult(
                chemistConfiguration is not null,
                home is not null,
                stations is not null,
                routesAvailable: true) == FishWarehouseEmployeeFallbackConfigurationResult.Pending)
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
        }

        chemist.PaidForToday = expected.PaidForToday;
        home!.Load(configuration.Bed);
        stations!.Load(configuration.Stations);

        if (!AllExpectedConfigurationGuidsResolved(expected))
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

        return FishWarehouseEmployeeFallbackConfigurationResult.Configured;
    }

    private static FishWarehouseEmployeeFallbackConfigurationResult ApplyBotanistConfiguration(
        FishWarehouseEmployeeReplayExpectedState expected,
        Botanist botanist,
        BotanistConfigurationData configuration)
    {
        var botanistConfiguration = botanist.configuration;
        var home = botanistConfiguration?.Home;
        var supplies = botanistConfiguration?.Supplies;
        var assigns = botanistConfiguration?.Assigns;
        if (GetFallbackConfigurationSurfaceResult(
                botanistConfiguration is not null,
                home is not null,
                stationsAvailable: assigns is not null,
                // The Botanist has no routes; the fourth slot carries supplies.
                routesAvailable: supplies is not null) == FishWarehouseEmployeeFallbackConfigurationResult.Pending)
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
        }

        botanist.PaidForToday = expected.PaidForToday;
        home!.Load(configuration.Bed);
        supplies!.Load(configuration.Supplies);
        assigns!.Load(configuration.Pots);

        if (!AllExpectedConfigurationGuidsResolved(expected))
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

        return FishWarehouseEmployeeFallbackConfigurationResult.Configured;
    }

    private static FishWarehouseEmployeeFallbackConfigurationResult ApplyCleanerConfiguration(
        FishWarehouseEmployeeReplayExpectedState expected,
        Cleaner cleaner,
        CleanerConfigurationData configuration)
    {
        var cleanerConfiguration = cleaner.configuration;
        var home = cleanerConfiguration?.Home;
        var bins = cleanerConfiguration?.Bins;
        if (GetFallbackConfigurationSurfaceResult(
                cleanerConfiguration is not null,
                home is not null,
                stationsAvailable: bins is not null,
                routesAvailable: true) == FishWarehouseEmployeeFallbackConfigurationResult.Pending)
        {
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;
        }

        cleaner.PaidForToday = expected.PaidForToday;
        home!.Load(configuration.Bed);
        bins!.Load(configuration.Bins);

        if (!AllExpectedConfigurationGuidsResolved(expected))
            return FishWarehouseEmployeeFallbackConfigurationResult.Pending;

        return FishWarehouseEmployeeFallbackConfigurationResult.Configured;
    }

    private static bool AllExpectedConfigurationGuidsResolved(FishWarehouseEmployeeReplayExpectedState expected)
    {
        if (!IsGuidRegistered(expected.HomeGuid))
            return false;

        if (!IsGuidRegistered(expected.SuppliesGuid))
            return false;

        foreach (var stationGuid in expected.StationGuids)
        {
            if (!IsGuidRegistered(stationGuid))
                return false;
        }

        return true;
    }

    private bool HasTypedPayload(FishWarehouseEmployeeReplayDescriptor descriptor)
    {
        if (!System.Guid.TryParse(descriptor.Guid, out var expectedGuid))
            return false;

        foreach (var candidate in _propertyData.Employees)
        {
            if (TryReadGuid(candidate.BaseData, out var candidateGuid) && candidateGuid == expectedGuid)
                return true;
        }

        return false;
    }

    private static bool TryReadConfigurationContents(
        DynamicSaveData data,
        string dataType,
        out string contentsJson,
        out string? failureReason)
    {
        contentsJson = string.Empty;
        failureReason = null;
        foreach (var additionalData in data.AdditionalDatas)
        {
            if (!string.Equals(additionalData.Name, "Configuration", StringComparison.Ordinal))
                continue;

            contentsJson = additionalData.Contents;
            return true;
        }

        failureReason = $"{ConfigurationDataName(dataType)} was unavailable";
        return false;
    }

    private static bool TryReadConfigurationContents(
        string rawJson,
        string dataType,
        out string contentsJson,
        out string? failureReason)
    {
        contentsJson = string.Empty;
        failureReason = null;
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.TryGetProperty("AdditionalDatas", out var additionalDatas) &&
                additionalDatas.ValueKind == JsonValueKind.Array)
            {
                foreach (var additionalData in additionalDatas.EnumerateArray())
                {
                    if (!additionalData.TryGetProperty("Name", out var name) ||
                        !string.Equals(name.GetString(), "Configuration", StringComparison.Ordinal) ||
                        !additionalData.TryGetProperty("Contents", out var contents))
                    {
                        continue;
                    }

                    if (contents.ValueKind == JsonValueKind.String)
                    {
                        contentsJson = contents.GetString() ?? string.Empty;
                        return true;
                    }

                    if (contents.ValueKind == JsonValueKind.Object)
                    {
                        contentsJson = contents.GetRawText();
                        return true;
                    }
                }
            }

            failureReason = $"{ConfigurationDataName(dataType)} was unavailable";
            return false;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            failureReason = $"{ConfigurationDataName(dataType)} parse failed: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseConfigurationData<TConfigurationData>(
        string contentsJson,
        string configurationDataName,
        out TConfigurationData? configuration,
        out string? failureReason)
        where TConfigurationData : class
    {
        configuration = null;
        failureReason = null;
        try
        {
            configuration = JsonUtility.FromJson<TConfigurationData>(contentsJson);
            if (configuration is null)
            {
                failureReason = $"{configurationDataName} was unavailable";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"{configurationDataName} parse failed: {ex.Message}";
            return false;
        }
    }

    private static string ConfigurationDataName(string dataType) => dataType switch
    {
        "BotanistData" => "BotanistConfigurationData",
        "ChemistData" => "ChemistConfigurationData",
        "CleanerData" => "CleanerConfigurationData",
        _ => "PackagerConfigurationData",
    };

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
            !TryResolveEmployee(descriptor.Guid, out var resolved, out _, out _))
        {
            return FishWarehouseEmployeeActiveMoveRestoreResult.Pending;
        }

        var liveEmployee = (Employee)resolved!;
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
            if (UnityLife.IsAlive(liveEmployee.MoveItemBehaviour))
            {
                var existingMove = liveEmployee.MoveItemBehaviour.GetSaveData();
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
                    liveEmployee.Inventory is null ||
                    liveEmployee.Inventory.ItemSlots is null)
                {
                    failureReason = $"active-move-inventory-unavailable (employee={descriptor.Guid})";
                    return FishWarehouseEmployeeActiveMoveRestoreResult.Failed;
                }

                inventory.LoadTo(liveEmployee.Inventory.ItemSlots);
                _inventoryRestored.Add(CanonicalGuid(descriptor.Guid));
            }

            if (!UnityLife.IsAlive(liveEmployee.MoveItemBehaviour))
                return FishWarehouseEmployeeActiveMoveRestoreResult.Pending;

            liveEmployee.MoveItemBehaviour.Load(new MoveItemData(
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
            if (UnityLife.IsAlive(buildable))
                guids.Add(buildable!.GUID.ToString());
        }

        return guids;
    }

}
