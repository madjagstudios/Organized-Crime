using System.Text.Json;
using Il2Cpp;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseNativePropertyReplayAdapter : IFishWarehouseNativePropertyReplayAdapter
{
    private readonly PropertyData _propertyData;
    private readonly Property _property;
    private readonly Action<string> _log;
    private readonly Dictionary<string, BuildableItemLoader> _loaders = new(StringComparer.Ordinal);

    public FishWarehouseNativePropertyReplayAdapter(
        PropertyData propertyData,
        Property property,
        Action<string>? log = null)
    {
        _propertyData = propertyData ?? throw new ArgumentNullException(nameof(propertyData));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _log = log ?? (_ => { });
    }

    public bool IsGuidRegistered(string guid)
    {
        if (!TryInvokeGuidInterop(guid, GUIDManager.IsGUIDAlreadyRegistered, out var registered))
        {
            _log($"Fish Warehouse native replay rejected malformed GUID before interop: {guid}");
            return false;
        }

        return registered;
    }

    public bool TryGetObjectLoaderLoadOrder(string dataType, out int loadOrder)
    {
        loadOrder = default;
        if (string.IsNullOrWhiteSpace(dataType))
            return false;

        if (!_loaders.TryGetValue(dataType, out var loader))
        {
            loader = LoadManager.Instance?.GetObjectLoader(dataType);
            if (loader is null)
                return false;

            _loaders.Add(dataType, loader);
        }

        loadOrder = loader.LoadOrder;
        return true;
    }

    public void LoadObject(int payloadIndex)
    {
        if (payloadIndex < 0 || payloadIndex >= _propertyData.Objects.Count)
            throw new ArgumentOutOfRangeException(nameof(payloadIndex));

        DynamicSaveData data = _propertyData.Objects[payloadIndex];
        if (!_loaders.TryGetValue(data.DataType, out var loader))
            throw new InvalidOperationException($"No registry-owned loader was resolved for {data.DataType}.");

        loader.Load(data);
    }

    public FishWarehouseRestoredObjectObservation ObserveObject(string guid)
    {
        if (!TryInvokeGuidInterop(guid, nativeGuid => GUIDManager.GetObject<BuildableItem>(nativeGuid), out BuildableItem? buildable) ||
            buildable is null)
        {
            return new FishWarehouseRestoredObjectObservation(false, null, false);
        }

        var parentPropertyCode = buildable.ParentProperty?.PropertyCode;
        var isEmployeeHome = buildable.GetComponent<EmployeeHome>() is not null;
        return new FishWarehouseRestoredObjectObservation(
            true,
            parentPropertyCode,
            isEmployeeHome && buildable.ParentProperty == _property);
    }

    public static bool TryCreateDescriptors(
        PropertyData propertyData,
        out IReadOnlyList<FishWarehouseSavedObjectDescriptor> descriptors,
        out string? failureReason)
    {
        descriptors = Array.Empty<FishWarehouseSavedObjectDescriptor>();
        failureReason = null;
        if (propertyData is null)
        {
            failureReason = "Fish Warehouse replay descriptors could not be created because captured PropertyData was unavailable.";
            return false;
        }

        var captured = new List<FishWarehouseSavedObjectDescriptor>();
        for (var index = 0; index < propertyData.Objects.Count; index++)
        {
            var data = propertyData.Objects[index];
            try
            {
                using var document = JsonDocument.Parse(data.BaseData);
                var root = document.RootElement;
                var guid = root.GetProperty("GUID").GetString();
                if (string.IsNullOrWhiteSpace(guid))
                    throw new JsonException("GUID was unavailable.");

                var itemId = ReadItemId(root);
                captured.Add(new FishWarehouseSavedObjectDescriptor(
                    index,
                    data.DataType,
                    guid,
                    root.GetProperty("LoadOrder").GetInt32(),
                    string.Equals(data.DataType, "ProceduralGridItemData", StringComparison.Ordinal),
                    string.Equals(itemId, "locker", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                failureReason = $"Fish Warehouse replay descriptor failed at object index {index}: {ex.Message}";
                return false;
            }
        }

        descriptors = captured.AsReadOnly();
        return true;
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

    private static string ReadItemId(JsonElement baseData)
    {
        if (!baseData.TryGetProperty("ItemString", out var itemString))
            return string.Empty;

        if (itemString.ValueKind == JsonValueKind.Object)
            return itemString.TryGetProperty("ID", out var id) ? id.GetString() ?? string.Empty : string.Empty;

        if (itemString.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(itemString.GetString()))
            return string.Empty;

        using var itemDocument = JsonDocument.Parse(itemString.GetString()!);
        return itemDocument.RootElement.TryGetProperty("ID", out var idValue)
            ? idValue.GetString() ?? string.Empty
            : string.Empty;
    }
}
