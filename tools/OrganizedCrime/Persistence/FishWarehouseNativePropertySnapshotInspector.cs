using System.Text.Json;

namespace OrganizedCrime.Persistence;

public sealed record FishWarehouseNativePropertyObjectSnapshot(
    string Guid,
    string GridGuid,
    int LoadOrder,
    string ItemId,
    bool IsSavedHome,
    string RawJson);

public sealed record FishWarehouseNativePropertyEmployeeSnapshot(
    string Guid,
    string DataType,
    string Identity,
    bool PaidForToday,
    string BedGuid,
    IReadOnlyList<string> StationGuids,
    string RawJson);

public sealed record FishWarehouseNativePropertySnapshot(
    string PropertyCode,
    bool IsOwned,
    IReadOnlyList<FishWarehouseNativePropertyObjectSnapshot> Objects,
    IReadOnlyList<FishWarehouseNativePropertyEmployeeSnapshot> Employees);

public sealed class FishWarehouseNativePropertySnapshotInspector
{
    public bool TryInspect(
        byte[] nativePropertyBytes,
        out FishWarehouseNativePropertySnapshot snapshot,
        out string? failureReason)
    {
        snapshot = null!;
        failureReason = null;
        if (nativePropertyBytes is null || nativePropertyBytes.Length == 0)
        {
            failureReason = "Fish Warehouse native property bytes were unavailable for inspection.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(nativePropertyBytes);
            var root = document.RootElement;
            var objects = ReadObjects(root.GetProperty("Objects"));
            var employeeRecords = root.GetProperty("Employees");
            var employees = ReadEmployees(employeeRecords);
            snapshot = new FishWarehouseNativePropertySnapshot(
                ReadPropertyCode(root, employeeRecords),
                root.GetProperty("IsOwned").GetBoolean(),
                Array.AsReadOnly(objects),
                Array.AsReadOnly(employees));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            failureReason = $"Fish Warehouse native property inspection failed: {ex.Message}";
            return false;
        }
    }

    private static FishWarehouseNativePropertyObjectSnapshot[] ReadObjects(JsonElement objects)
    {
        if (objects.ValueKind != JsonValueKind.Array)
            throw new JsonException("Native Fish Warehouse Objects must be an array.");

        return objects.EnumerateArray()
            .Select(record =>
            {
                var baseData = record.TryGetProperty("BaseData", out _)
                    ? ReadNestedObject(record, "BaseData")
                    : record;
                var itemString = ReadNestedObject(baseData, "ItemString");
                var itemId = ReadRequiredString(itemString, "ID");
                return new FishWarehouseNativePropertyObjectSnapshot(
                    ReadRequiredString(baseData, "GUID"),
                    ReadRequiredString(baseData, "GridGUID"),
                    baseData.GetProperty("LoadOrder").GetInt32(),
                    itemId,
                    string.Equals(itemId, "locker", StringComparison.Ordinal),
                    record.GetRawText());
            })
            .ToArray();
    }

    private static FishWarehouseNativePropertyEmployeeSnapshot[] ReadEmployees(JsonElement employees)
    {
        if (employees.ValueKind != JsonValueKind.Array)
            throw new JsonException("Native Fish Warehouse Employees must be an array.");

        return employees.EnumerateArray()
            .Select(record =>
            {
                var baseData = ReadNestedObject(record, "BaseData");
                var dataType = ReadRequiredString(record, "DataType");
                return new FishWarehouseNativePropertyEmployeeSnapshot(
                    ReadRequiredString(baseData, "GUID"),
                    dataType,
                    ReadEmployeeIdentity(baseData, dataType),
                    baseData.GetProperty("PaidForToday").GetBoolean(),
                    ReadEmployeeBedGuid(baseData, record),
                    ReadConfigurationStationGuids(record),
                    record.GetRawText());
            })
            .ToArray();
    }

    private static string ReadPropertyCode(JsonElement root, JsonElement employeeRecords)
    {
        if (root.TryGetProperty("PropertyCode", out _))
            return ReadRequiredString(root, "PropertyCode");

        if (employeeRecords.ValueKind == JsonValueKind.Array)
        {
            foreach (var employeeRecord in employeeRecords.EnumerateArray())
            {
                var baseData = ReadNestedObject(employeeRecord, "BaseData");
                if (baseData.TryGetProperty("AssignedProperty", out var assignedProperty))
                {
                    var propertyCode = assignedProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(propertyCode))
                        return propertyCode;
                }
            }
        }

        return ReadRequiredString(root, "PropertyCode");
    }

    private static string ReadEmployeeIdentity(JsonElement baseData, string dataType)
    {
        if (baseData.TryGetProperty("Identity", out _))
            return ReadRequiredString(baseData, "Identity");

        if (string.Equals(dataType, "PackagerData", StringComparison.Ordinal))
            return "Packager";

        return ReadRequiredString(baseData, "Identity");
    }

    private static string ReadEmployeeBedGuid(JsonElement baseData, JsonElement employeeRecord)
    {
        if (baseData.TryGetProperty("BedGUID", out _))
            return ReadRequiredString(baseData, "BedGUID");

        var configuration = ReadConfigurationContents(employeeRecord);
        if (configuration is not { } configurationObject ||
            configurationObject.ValueKind != JsonValueKind.Object ||
            !configurationObject.TryGetProperty("Bed", out var bed))
            throw new JsonException("Native Fish Warehouse Bed/ObjectGUID was unavailable.");

        return ReadOptionalString(bed, "ObjectGUID");
    }

    private static IReadOnlyList<string> ReadConfigurationStationGuids(JsonElement employeeRecord)
    {
        var configuration = ReadConfigurationContents(employeeRecord);
        if (configuration is not { } configurationObject)
            return Array.AsReadOnly(Array.Empty<string>());

        return ReadStationGuids(configurationObject);
    }

    private static IReadOnlyList<string> ReadStationGuids(JsonElement contents)
    {
        if (contents.ValueKind == JsonValueKind.String)
        {
            using var document = JsonDocument.Parse(contents.GetString() ?? string.Empty);
            return ReadStationGuids(document.RootElement);
        }

        if (contents.ValueKind != JsonValueKind.Object)
            return Array.AsReadOnly(Array.Empty<string>());

        if (contents.TryGetProperty("Stations", out var stations) &&
            stations.ValueKind == JsonValueKind.Object &&
            stations.TryGetProperty("ObjectGUIDs", out var objectGuids) &&
            objectGuids.ValueKind == JsonValueKind.Array)
            return ReadStringArray(objectGuids);

        if (!contents.TryGetProperty("StationGUIDs", out var stationGuids) ||
            stationGuids.ValueKind != JsonValueKind.Array)
            return Array.AsReadOnly(Array.Empty<string>());

        return ReadStringArray(stationGuids);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement values)
    {
        return Array.AsReadOnly(values.EnumerateArray()
            .Select(station => station.GetString())
            .Where(station => !string.IsNullOrWhiteSpace(station))
            .Select(station => station!)
            .ToArray());
    }

    private static JsonElement? ReadConfigurationContents(JsonElement employeeRecord)
    {
        if (!employeeRecord.TryGetProperty("AdditionalDatas", out var additionalDatas) ||
            additionalDatas.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var additionalData in additionalDatas.EnumerateArray())
        {
            if (!additionalData.TryGetProperty("Name", out var name) ||
                !string.Equals(name.GetString(), "Configuration", StringComparison.Ordinal) ||
                !additionalData.TryGetProperty("Contents", out var contents))
                continue;

            if (contents.ValueKind == JsonValueKind.String)
            {
                using var document = JsonDocument.Parse(contents.GetString() ?? string.Empty);
                return document.RootElement.Clone();
            }

            return contents;
        }

        return null;
    }

    private static JsonElement ReadNestedObject(JsonElement record, string propertyName)
    {
        var value = record.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.String)
        {
            using var document = JsonDocument.Parse(value.GetString() ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Native Fish Warehouse {propertyName} must be a JSON object.");

            return document.RootElement.Clone();
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Native Fish Warehouse {propertyName} must be a JSON object.");

        return value;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"Native Fish Warehouse {propertyName} was unavailable.");

        return value;
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException($"Native Fish Warehouse {propertyName} was unavailable.");

        var text = value.GetString();
        if (text is null || (text.Length > 0 && string.IsNullOrWhiteSpace(text)))
            throw new JsonException($"Native Fish Warehouse {propertyName} was unavailable.");

        return text;
    }
}
