using System.Text.Json;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum FishWarehouseEmployeePrimaryLoadResult
{
    Started,
    LoaderUnavailable,
    Failed
}

public enum FishWarehouseEmployeeObservationResult
{
    Ready,
    Pending,
    Failed
}

public enum FishWarehouseEmployeeFallbackCreationResult
{
    Started,
    Failed
}

public enum FishWarehouseEmployeeFallbackConfigurationResult
{
    Configured,
    Pending,
    Failed
}

public enum FishWarehouseEmployeeActiveMoveRestoreResult
{
    NotRequired,
    Pending,
    Restored,
    Failed
}

// RequiresFallbackCreation marks a record restored from the sidecar's
// preserved list: it has no typed payload, so the replay skips the
// registry loader and creates it through fallback instead.
public sealed record FishWarehouseEmployeeReplayDescriptor(
    string Guid,
    string DataType,
    string Identity,
    string RawJson,
    bool RequiresFallbackCreation = false)
{
    // Historical name: this is now the supported-employee gate for all four
    // native types. Packager behavior is unchanged; Botanist, Chemist and
    // Cleaner match their DataType identity too, while Handler and unknown
    // types stay unsupported and preserved.
    public bool IsSupportedPackager =>
        FishWarehouseEmployeeIdentity.IsSupportedEmployee(DataType, Identity);

    public bool TryParseExpectedState(
        out FishWarehouseEmployeeReplayExpectedState expected,
        out string? failureReason)
    {
        expected = null!;
        failureReason = null;
        try
        {
            using var document = JsonDocument.Parse(RawJson);
            var root = document.RootElement;
            var payloadDataType = ReadRequiredString(root, "DataType");
            if (!string.Equals(payloadDataType, DataType, StringComparison.Ordinal))
                throw new JsonException("data-type-mismatch");

            var baseData = ReadWrappedObject(root, "BaseData");
            var payloadGuid = ReadRequiredString(baseData, "GUID");
            if (!System.Guid.TryParse(payloadGuid, out var parsedPayloadGuid) ||
                !System.Guid.TryParse(Guid, out var parsedDescriptorGuid) ||
                parsedPayloadGuid != parsedDescriptorGuid)
            {
                throw new JsonException("guid-mismatch");
            }

            var payloadIdentity = baseData.TryGetProperty("Identity", out _)
                ? ReadRequiredString(baseData, "Identity")
                : FishWarehouseEmployeeIdentity.FromDataType(payloadDataType);
            var descriptorIdentity = string.IsNullOrWhiteSpace(Identity)
                ? FishWarehouseEmployeeIdentity.FromDataType(DataType)
                : Identity;
            if (!string.Equals(payloadIdentity, descriptorIdentity, StringComparison.Ordinal))
                throw new JsonException("identity-mismatch");

            var position = ReadObject(baseData, "Position");
            var rotation = ReadObject(baseData, "Rotation");
            var configuration = ReadConfigurationOrNull(root);
            var inventoryJson = ReadOptionalAdditionalData(root, "Inventory");
            var homeGuid = baseData.TryGetProperty("BedGUID", out _)
                ? ReadOptionalString(baseData, "BedGUID")
                : ReadConfigurationBedGuid(configuration);
            var stationGuids = ReadConfigurationStationGuids(configuration);
            var suppliesGuid = ReadConfigurationSuppliesGuid(configuration);
            // The warehouse has three packaging stations, so an employee may be
            // assigned zero to three. OC-7 logistics can leave an employee with
            // fewer than three stations (or none) plus a dock→interior route, so
            // the replay contract accepts any count within that bound rather than
            // requiring exactly three — otherwise a route-configured employee is
            // rejected and lost on restart.
            if (stationGuids.Count > 3)
                throw new JsonException("station-count-exceeds-max");

            FishWarehouseEmployeeMoveItemState? moveItem = null;
            if (baseData.TryGetProperty("MoveItemData", out var moveItemData) && moveItemData.ValueKind != JsonValueKind.Null)
            {
                if (!IsIdleMoveItemData(moveItemData))
                {
                    moveItem = new FishWarehouseEmployeeMoveItemState(
                        ReadRequiredString(moveItemData, "SourceGUID"),
                        ReadRequiredString(moveItemData, "DestinationGUID"),
                        ReadRequiredString(moveItemData, "TemplateItemJSON"),
                        ReadRequiredInt(moveItemData, "GrabbedItemQuantity"));
                }
            }

            expected = new FishWarehouseEmployeeReplayExpectedState(
                parsedDescriptorGuid.ToString("D"),
                payloadIdentity,
                ReadRequiredString(baseData, "ID"),
                ReadRequiredString(baseData, "FirstName"),
                ReadRequiredString(baseData, "LastName"),
                ReadRequiredBoolean(baseData, "IsMale"),
                ReadRequiredInt(baseData, "AppearanceIndex"),
                ReadRequiredFloat(position, "x"),
                ReadRequiredFloat(position, "y"),
                ReadRequiredFloat(position, "z"),
                ReadRequiredFloat(rotation, "x"),
                ReadRequiredFloat(rotation, "y"),
                ReadRequiredFloat(rotation, "z"),
                ReadRequiredFloat(rotation, "w"),
                ReadPropertyCode(baseData),
                ReadRequiredBoolean(baseData, "PaidForToday"),
                moveItem,
                homeGuid,
                stationGuids,
                inventoryJson,
                suppliesGuid);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static JsonElement? ReadConfigurationOrNull(JsonElement root)
    {
        if (!root.TryGetProperty("AdditionalDatas", out var additionalDatas) || additionalDatas.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var additionalData in additionalDatas.EnumerateArray())
        {
            if (!additionalData.TryGetProperty("Name", out var name) ||
                !string.Equals(name.GetString(), "Configuration", StringComparison.Ordinal) ||
                !additionalData.TryGetProperty("Contents", out var contents))
            {
                continue;
            }

            return ReadObjectValue(contents, "Configuration");
        }

        return null;
    }

    // A missing Bed, Stations section, or Configuration entry is an
    // unassigned-as-yet employee (just hired), not a corrupt record: it
    // reads as no home / no stations, mirroring the inspector. Present-but-
    // malformed values still fail the parse.
    private static string ReadConfigurationBedGuid(JsonElement? configuration)
    {
        if (configuration is not { } configurationObject ||
            !configurationObject.TryGetProperty("Bed", out var bed))
            return string.Empty;

        if (bed.ValueKind != JsonValueKind.Object)
            throw new JsonException("Bed-missing");

        if (!bed.TryGetProperty("ObjectGUID", out _))
            return string.Empty;

        return ReadOptionalString(bed, "ObjectGUID");
    }

    private static IReadOnlyList<string> ReadConfigurationStationGuids(JsonElement? configuration)
    {
        if (configuration is not { } configurationObject)
            return Array.AsReadOnly(Array.Empty<string>());

        if (configurationObject.TryGetProperty("StationGUIDs", out _))
            return ReadStringArray(configurationObject, "StationGUIDs");

        // Packager/Chemist list Stations; the Botanist lists Pots and the
        // Cleaner lists Bins. A configuration carries exactly one of them.
        foreach (var listName in new[] { "Stations", "Pots", "Bins" })
        {
            if (configurationObject.TryGetProperty(listName, out _))
                return ReadStringArray(ReadObject(configurationObject, listName), "ObjectGUIDs");
        }

        return Array.AsReadOnly(Array.Empty<string>());
    }

    private static string ReadConfigurationSuppliesGuid(JsonElement? configuration)
    {
        if (configuration is not { } configurationObject ||
            !configurationObject.TryGetProperty("Supplies", out var supplies))
            return string.Empty;

        if (supplies.ValueKind != JsonValueKind.Object)
            throw new JsonException("Supplies-missing");

        if (!supplies.TryGetProperty("ObjectGUID", out _))
            return string.Empty;

        return ReadOptionalString(supplies, "ObjectGUID");
    }

    private static string? ReadOptionalAdditionalData(JsonElement root, string name)
    {
        if (!root.TryGetProperty("AdditionalDatas", out var additionalDatas) || additionalDatas.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var additionalData in additionalDatas.EnumerateArray())
        {
            if (!additionalData.TryGetProperty("Name", out var dataName) ||
                !string.Equals(dataName.GetString(), name, StringComparison.Ordinal) ||
                !additionalData.TryGetProperty("Contents", out var contents))
            {
                continue;
            }

            return contents.ValueKind switch
            {
                JsonValueKind.String => contents.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => contents.GetRawText(),
                _ => throw new JsonException($"{name}-invalid")
            };
        }

        return null;
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{name}-missing");

        return value;
    }

    private static JsonElement ReadWrappedObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new JsonException($"{name}-missing");

        return ReadObjectValue(value, name);
    }

    private static JsonElement ReadObjectValue(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value;

        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var document = JsonDocument.Parse(value.GetString() ?? string.Empty);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new JsonException($"{name}-invalid");
            }
        }

        throw new JsonException($"{name}-missing");
    }

    private static string ReadPropertyCode(JsonElement baseData)
    {
        return baseData.TryGetProperty("PropertyCode", out _)
            ? ReadRequiredString(baseData, "PropertyCode")
            : ReadRequiredString(baseData, "AssignedProperty");
    }

    private static bool IsIdleMoveItemData(JsonElement moveItemData)
    {
        return moveItemData.ValueKind == JsonValueKind.Object &&
            IsEmptyString(moveItemData, "SourceGUID") &&
            IsEmptyString(moveItemData, "DestinationGUID") &&
            IsEmptyString(moveItemData, "TemplateItemJSON") &&
            moveItemData.TryGetProperty("GrabbedItemQuantity", out var quantity) &&
            quantity.TryGetInt32(out var parsedQuantity) &&
            parsedQuantity == 0;
    }

    private static bool IsEmptyString(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), string.Empty, StringComparison.Ordinal);
    }

    private static string ReadRequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"{name}-missing");
        }

        return value.GetString()!;
    }

    private static string ReadOptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name}-missing");

        var text = value.GetString();
        if (text is null || (text.Length > 0 && string.IsNullOrWhiteSpace(text)))
            throw new JsonException($"{name}-missing");

        return text;
    }

    private static bool ReadRequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new JsonException($"{name}-missing");
        }

        return value.GetBoolean();
    }

    private static int ReadRequiredInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new JsonException($"{name}-missing");

        return result;
    }

    private static float ReadRequiredFloat(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result))
            throw new JsonException($"{name}-missing");

        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
            throw new JsonException($"{name}-missing");

        var result = values.EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        if (result.Length != values.GetArrayLength())
            throw new JsonException($"{name}-invalid");

        return result;
    }
}

public sealed record FishWarehouseEmployeeReplayExpectedState(
    string Guid,
    string Identity,
    string Id,
    string FirstName,
    string LastName,
    bool IsMale,
    int AppearanceIndex,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationX,
    float RotationY,
    float RotationZ,
    float RotationW,
    string PropertyCode,
    bool PaidForToday,
    FishWarehouseEmployeeMoveItemState? MoveItem,
    string HomeGuid,
    IReadOnlyList<string> StationGuids,
    string? InventoryJson = null,
    string SuppliesGuid = "");

public sealed record FishWarehouseEmployeeMoveItemState(
    string SourceGuid,
    string DestinationGuid,
    string TemplateItemJson,
    int GrabbedItemQuantity);

public sealed record FishWarehouseEmployeeReplayObservedState(
    bool IsAlive,
    string Guid,
    string Identity,
    string Id,
    string FirstName,
    string LastName,
    bool IsMale,
    int AppearanceIndex,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationX,
    float RotationY,
    float RotationZ,
    float RotationW,
    string PropertyCode,
    bool PaidForToday,
    FishWarehouseEmployeeMoveItemState? MoveItem,
    string HomeGuid,
    IReadOnlyList<string> StationGuids,
    string SuppliesGuid = "");

public interface IFishWarehouseNativeEmployeeReplayAdapter
{
    IReadOnlyCollection<string> InventoryRestoredEmployeeGuids { get; }
    bool TryIsGuidRegistered(string savedGuid, out bool isRegistered, out string? failureReason);
    FishWarehouseEmployeePrimaryLoadResult TryLoadPrimary(FishWarehouseEmployeeReplayDescriptor descriptor, out string? failureReason);
    FishWarehouseEmployeeObservationResult TryObserveEmployee(string savedGuid, out FishWarehouseEmployeeReplayObservedState? observation, out string? failureReason);
    FishWarehouseEmployeeFallbackCreationResult TryCreateFallback(FishWarehouseEmployeeReplayDescriptor descriptor, FishWarehouseEmployeeReplayExpectedState expected, out string? failureReason);
    FishWarehouseEmployeeFallbackConfigurationResult TryConfigureFallback(FishWarehouseEmployeeReplayDescriptor descriptor, FishWarehouseEmployeeReplayExpectedState expected, out string? failureReason);
    FishWarehouseEmployeeActiveMoveRestoreResult TryRestoreActiveMove(FishWarehouseEmployeeReplayDescriptor descriptor, FishWarehouseEmployeeReplayExpectedState expected, out string? failureReason);
}
