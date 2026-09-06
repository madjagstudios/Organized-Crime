using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrganizedCrime.Persistence;

public static class FishWarehouseSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize(FishWarehouseSaveState state)
    {
        Validate(state, "Cannot serialize invalid Fish Warehouse state.");
        return JsonSerializer.Serialize(state, Options);
    }

    public static bool TryDeserialize(
        string json,
        out FishWarehouseSaveState state,
        out string? failureReason)
    {
        state = null!;
        failureReason = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            failureReason = "Save state JSON was empty.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Save state JSON root was not an object.";
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                !schemaVersion.TryGetInt32(out _))
            {
                failureReason = "Save state schemaVersion was missing or invalid.";
                return false;
            }

            if (!root.TryGetProperty("propertyCode", out var propertyCode) ||
                propertyCode.ValueKind != JsonValueKind.String)
            {
                failureReason = "Save state propertyCode was missing or invalid.";
                return false;
            }

            if (!root.TryGetProperty("unlocked", out var unlocked) ||
                (unlocked.ValueKind != JsonValueKind.True && unlocked.ValueKind != JsonValueKind.False))
            {
                failureReason = "Save state unlocked flag was missing or invalid.";
                return false;
            }

            if (!root.TryGetProperty("owned", out var owned) ||
                (owned.ValueKind != JsonValueKind.True && owned.ValueKind != JsonValueKind.False))
            {
                failureReason = "Save state owned flag was missing or invalid.";
                return false;
            }

            var parsedSchemaVersion = schemaVersion.GetInt32();
            if (parsedSchemaVersion == FishWarehouseSaveState.CurrentSchemaVersion)
                ValidateV2JsonShape(root);

            state = parsedSchemaVersion switch
            {
                1 => MigrateV1(JsonSerializer.Deserialize<V1SaveState>(json, Options)),
                FishWarehouseSaveState.CurrentSchemaVersion => JsonSerializer.Deserialize<FishWarehouseSaveState>(json, Options)!,
                _ => throw new InvalidOperationException("Save state schema was not supported.")
            };
            Validate(state, "Save state identity or schema was not supported.");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            state = null!;
            failureReason = ex.Message;
            return false;
        }
    }

    private static void Validate(FishWarehouseSaveState state, string message)
    {
        if (state is null ||
            state.SchemaVersion != FishWarehouseSaveState.CurrentSchemaVersion ||
            !string.Equals(state.PropertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal) ||
            state.Owned && !state.Unlocked ||
            state.UnsupportedEmployees is null)
        {
            throw new InvalidOperationException(message);
        }

        var seenEmployeeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in state.UnsupportedEmployees)
        {
            if (employee is null ||
                string.IsNullOrWhiteSpace(employee.Guid) ||
                string.IsNullOrWhiteSpace(employee.DataType) ||
                string.IsNullOrWhiteSpace(employee.Identity) ||
                employee.RawJson is null ||
                !seenEmployeeGuids.Add(employee.Guid))
            {
                throw new InvalidOperationException("Save state contained an invalid or duplicate unsupported employee.");
            }
        }

        if (state.Replay is null)
            return;

        if (!Enum.IsDefined(state.Replay.Phase) ||
            string.IsNullOrWhiteSpace(state.Replay.GenerationId) ||
            !IsSha256(state.Replay.SaveFolderKey) ||
            string.IsNullOrWhiteSpace(state.Replay.SnapshotRelativePath) ||
            Path.IsPathRooted(state.Replay.SnapshotRelativePath) ||
            !IsSha256(state.Replay.SnapshotSha256) ||
            state.Replay.CapturedObjectCount < 0 ||
            state.Replay.CapturedEmployeeCount < 0 ||
            state.Replay.ReplayedObjectCount < 0 ||
            state.Replay.ReplayedEmployeeCount < 0 ||
            state.Replay.InventoryRestoredEmployeeGuids?.Any(guid =>
                !System.Guid.TryParse(guid, out _) ||
                string.IsNullOrWhiteSpace(guid)) == true ||
            state.Replay.InventoryRestoredEmployeeGuids is not null &&
            state.Replay.InventoryRestoredEmployeeGuids.Count != state.Replay.InventoryRestoredEmployeeGuids
                .Select(guid => guid.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count())
        {
            throw new InvalidOperationException("Save state replay checkpoint folder or hash fields were invalid.");
        }
    }

    private static void ValidateV2JsonShape(JsonElement root)
    {
        if (!root.TryGetProperty("employeeHomePlaced", out var employeeHomePlaced) ||
            (employeeHomePlaced.ValueKind != JsonValueKind.True && employeeHomePlaced.ValueKind != JsonValueKind.False))
        {
            throw new InvalidOperationException("Save state employeeHomePlaced was missing or invalid.");
        }

        if (!root.TryGetProperty("unsupportedEmployees", out var unsupportedEmployees) ||
            unsupportedEmployees.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Save state unsupportedEmployees was missing or invalid.");
        }

        if (root.TryGetProperty("employeeAgentTypeId", out var employeeAgentTypeId) &&
            employeeAgentTypeId.ValueKind != JsonValueKind.Null &&
            (employeeAgentTypeId.ValueKind != JsonValueKind.Number || !employeeAgentTypeId.TryGetInt32(out _)))
        {
            throw new InvalidOperationException("Save state employeeAgentTypeId was invalid.");
        }

        if (root.TryGetProperty("replay", out var replay) &&
            replay.ValueKind != JsonValueKind.Null &&
            replay.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Save state replay was invalid.");
        }
    }

    private static FishWarehouseSaveState MigrateV1(V1SaveState? v1)
    {
        if (v1 is null)
            throw new InvalidOperationException("Save state schema v1 was invalid.");

        return new FishWarehouseSaveState(
            FishWarehouseSaveState.CurrentSchemaVersion,
            v1.PropertyCode,
            v1.Unlocked,
            v1.Owned,
            v1.EmployeeHomePlaced,
            EmployeeAgentTypeId: null,
            Replay: null,
            UnsupportedEmployees: Array.Empty<FishWarehouseUnsupportedEmployeeRecord>());
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
            return false;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }

    private sealed record V1SaveState(
        int SchemaVersion,
        string PropertyCode,
        bool Unlocked,
        bool Owned,
        bool EmployeeHomePlaced = false,
        string? EmployeeHomeAssignedEmployeeName = null);
}
