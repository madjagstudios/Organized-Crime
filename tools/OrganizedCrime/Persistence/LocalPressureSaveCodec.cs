using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public enum LocalPressureCodecFailureReason
{
    None,
    EmptyJson,
    MalformedJson,
    InvalidRoot,
    MissingRequiredField,
    InvalidJsonType,
    InvalidSchemaVersion,
    MissingPlayers,
    UnsupportedSchema,
    InvalidPlayerId,
    DuplicatePlayerId,
    InvalidHeat,
    InvalidGameTime,
    InvalidRevision,
    InvalidContext,
    UnknownField,
    InvalidEnvelope,
    SerializationFailed
}

public sealed record LocalPressureCodecResult(
    bool Succeeded,
    LocalPressureCodecFailureReason Reason,
    string Message)
{
    public static LocalPressureCodecResult Success() =>
        new(true, LocalPressureCodecFailureReason.None, string.Empty);

    public static LocalPressureCodecResult Failure(LocalPressureCodecFailureReason reason, string message) =>
        new(false, reason, message);
}

public static class LocalPressureSaveCodec
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumPlayerIdLength = 256;
    public const int MaximumContextLength = 64;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly HashSet<string> EnvelopeFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "players"
    };

    private static readonly HashSet<string> PlayerFields = new(StringComparer.Ordinal)
    {
        "playerId", "localHeat", "knownOffender", "lastEvidenceGameTime",
        "quietGraceUntil", "lastDecayEvaluation", "region", "property", "revision"
    };

    public static LocalPressureSaveEnvelope CreateEmpty() => LocalPressureSaveEnvelope.CreateEmpty();

    public static bool TrySerialize(
        LocalPressureSaveEnvelope? envelope,
        out string json,
        out LocalPressureCodecResult result)
    {
        json = string.Empty;
        if (!TryValidateEnvelope(envelope, out result))
            return false;

        try
        {
            var ordered = new LocalPressureSaveEnvelope(
                CurrentSchemaVersion,
                envelope!.Players.OrderBy(player => player.PlayerId, StringComparer.Ordinal).ToArray());
            json = JsonSerializer.Serialize(ordered, Options);
            result = LocalPressureCodecResult.Success();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.SerializationFailed,
                $"Local Pressure sidecar could not be serialized: {ex.Message}");
            return false;
        }
    }

    public static bool TrySerialize(
        IEnumerable<LocalPressureState>? states,
        out string json,
        out LocalPressureCodecResult result)
    {
        json = string.Empty;
        if (states is null)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.InvalidEnvelope,
                "Local Pressure state collection was null.");
            return false;
        }

        try
        {
            return TrySerialize(
                new LocalPressureSaveEnvelope(
                    CurrentSchemaVersion,
                    states.Select(LocalPressurePlayerRecord.FromState).ToArray()),
                out json,
                out result);
        }
        catch (ArgumentException ex)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.InvalidPlayerId,
                ex.Message);
            return false;
        }
    }

    public static bool TryDeserialize(
        string? json,
        out LocalPressureSaveEnvelope? envelope,
        out LocalPressureCodecResult result)
    {
        envelope = null;
        result = LocalPressureCodecResult.Failure(
            LocalPressureCodecFailureReason.MalformedJson,
            "Local Pressure sidecar JSON was invalid.");

        if (string.IsNullOrWhiteSpace(json))
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.EmptyJson,
                "Local Pressure sidecar JSON was empty.");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result = LocalPressureCodecResult.Failure(
                    LocalPressureCodecFailureReason.InvalidRoot,
                    "Local Pressure sidecar JSON root was not an object.");
                return false;
            }

            if (!TryValidateUniqueProperties(root, EnvelopeFields, out result))
                return false;
            if (!root.TryGetProperty("schemaVersion", out var schemaVersion))
                return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, "Local Pressure schemaVersion was missing.");
            if (schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out var parsedSchemaVersion))
                return Fail(out result, LocalPressureCodecFailureReason.InvalidSchemaVersion, "Local Pressure schemaVersion was invalid.");

            return parsedSchemaVersion switch
            {
                CurrentSchemaVersion => TryDeserializeV1(root, out envelope, out result),
                _ => Fail(out result, LocalPressureCodecFailureReason.UnsupportedSchema,
                    $"Local Pressure schemaVersion {parsedSchemaVersion} is not supported.")
            };
        }
        catch (JsonException ex)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.MalformedJson,
                $"Local Pressure sidecar JSON was malformed: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.MalformedJson,
                $"Local Pressure sidecar JSON could not be read: {ex.Message}");
            return false;
        }
    }

    public static bool TryDeserializeStates(
        string? json,
        out IReadOnlyList<LocalPressureState>? states,
        out LocalPressureCodecResult result)
    {
        states = null;
        if (!TryDeserialize(json, out var envelope, out result))
            return false;

        try
        {
            states = envelope!.Players.Select(player => player.ToState()).ToArray();
            return true;
        }
        catch (ArgumentException ex)
        {
            result = LocalPressureCodecResult.Failure(
                LocalPressureCodecFailureReason.InvalidEnvelope,
                ex.Message);
            return false;
        }
    }

    private static bool TryDeserializeV1(
        JsonElement root,
        out LocalPressureSaveEnvelope? envelope,
        out LocalPressureCodecResult result)
    {
        envelope = null;
        if (!root.TryGetProperty("players", out var players))
            return Fail(out result, LocalPressureCodecFailureReason.MissingPlayers, "Local Pressure players collection was missing.");
        if (players.ValueKind != JsonValueKind.Array)
            return Fail(out result, LocalPressureCodecFailureReason.MissingPlayers, "Local Pressure players collection was not an array.");

        var records = new List<LocalPressurePlayerRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in players.EnumerateArray())
        {
            if (player.ValueKind != JsonValueKind.Object)
                return Fail(out result, LocalPressureCodecFailureReason.InvalidJsonType, "Local Pressure player record was not an object.");
            if (!TryValidateUniqueProperties(player, PlayerFields, out result))
                return false;
            if (!TryReadPlayer(player, out var record, out result))
                return false;
            if (!seen.Add(record!.PlayerId))
                return Fail(out result, LocalPressureCodecFailureReason.DuplicatePlayerId,
                    $"Local Pressure player ID was duplicated: {record.PlayerId}.");
            records.Add(record);
        }

        envelope = new LocalPressureSaveEnvelope(CurrentSchemaVersion, records);
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadPlayer(
        JsonElement player,
        out LocalPressurePlayerRecord? record,
        out LocalPressureCodecResult result)
    {
        record = null;
        if (!TryReadRequiredString(player, "playerId", out var playerId, out result))
            return false;
        if (!IsValidIdentity(playerId!))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidPlayerId, "Local Pressure playerId was blank, unbounded, or contained control characters.");
        if (!TryReadRequiredInt32(player, "localHeat", out var localHeat, out result))
            return false;
        if (localHeat < LocalPressureProfile.MinimumHeat || localHeat > LocalPressureProfile.MaximumHeat)
            return Fail(out result, LocalPressureCodecFailureReason.InvalidHeat, "Local Pressure localHeat was outside 0 through 100.");
        if (!TryReadRequiredBoolean(player, "knownOffender", out var knownOffender, out result))
            return false;
        if (!TryReadGameTime(player, "lastEvidenceGameTime", out var evidenceTime, out result) ||
            !TryReadGameTime(player, "quietGraceUntil", out var graceTime, out result) ||
            !TryReadGameTime(player, "lastDecayEvaluation", out var decayTime, out result))
        {
            return false;
        }
        if (!TryReadOptionalContext(player, "region", out var region, out result) ||
            !TryReadOptionalContext(player, "property", out var property, out result))
        {
            return false;
        }
        if (!TryReadRequiredInt64(player, "revision", out var revision, out result))
            return false;
        if (revision < 0)
            return Fail(out result, LocalPressureCodecFailureReason.InvalidRevision, "Local Pressure revision cannot be negative.");

        record = new LocalPressurePlayerRecord(
            playerId!, localHeat, knownOffender, evidenceTime, graceTime, decayTime, region, property, revision);
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryValidateEnvelope(
        LocalPressureSaveEnvelope? envelope,
        out LocalPressureCodecResult result)
    {
        if (envelope is null)
            return Fail(out result, LocalPressureCodecFailureReason.InvalidEnvelope, "Local Pressure envelope was null.");
        if (envelope.SchemaVersion != CurrentSchemaVersion)
            return Fail(out result, LocalPressureCodecFailureReason.UnsupportedSchema, "Local Pressure envelope schema was not supported.");
        if (envelope.Players is null)
            return Fail(out result, LocalPressureCodecFailureReason.MissingPlayers, "Local Pressure players collection was null.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in envelope.Players)
        {
            if (player is null)
                return Fail(out result, LocalPressureCodecFailureReason.InvalidEnvelope, "Local Pressure player record was null.");
            if (!IsValidIdentity(player.PlayerId))
                return Fail(out result, LocalPressureCodecFailureReason.InvalidPlayerId, "Local Pressure playerId was blank, unbounded, or contained control characters.");
            if (!seen.Add(player.PlayerId))
                return Fail(out result, LocalPressureCodecFailureReason.DuplicatePlayerId, $"Local Pressure player ID was duplicated: {player.PlayerId}.");
            if (player.LocalHeat < LocalPressureProfile.MinimumHeat || player.LocalHeat > LocalPressureProfile.MaximumHeat)
                return Fail(out result, LocalPressureCodecFailureReason.InvalidHeat, "Local Pressure localHeat was outside 0 through 100.");
            if (!IsValidGameTime(player.LastEvidenceGameTime) || !IsValidGameTime(player.QuietGraceUntil) || !IsValidGameTime(player.LastDecayEvaluation))
                return Fail(out result, LocalPressureCodecFailureReason.InvalidGameTime, "Local Pressure game time was invalid.");
            if (player.Revision < 0)
                return Fail(out result, LocalPressureCodecFailureReason.InvalidRevision, "Local Pressure revision cannot be negative.");
            if (!IsValidContext(player.Region) || !IsValidContext(player.Property))
                return Fail(out result, LocalPressureCodecFailureReason.InvalidContext, "Local Pressure presentation context was invalid or unbounded.");
        }

        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryValidateUniqueProperties(
        JsonElement element,
        HashSet<string> allowedFields,
        out LocalPressureCodecResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                return Fail(out result, LocalPressureCodecFailureReason.InvalidEnvelope, $"Local Pressure JSON field was duplicated: {property.Name}.");
            if (!allowedFields.Contains(property.Name))
                return Fail(out result, LocalPressureCodecFailureReason.UnknownField, $"Local Pressure JSON field was unsupported: {property.Name}.");
        }

        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadRequiredString(JsonElement element, string name, out string? value, out LocalPressureCodecResult result)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
            return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, $"Local Pressure field {name} was missing.");
        if (property.ValueKind != JsonValueKind.String)
            return Fail(out result, LocalPressureCodecFailureReason.InvalidJsonType, $"Local Pressure field {name} was not a string.");
        value = property.GetString();
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadRequiredInt32(JsonElement element, string name, out int value, out LocalPressureCodecResult result)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property))
            return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, $"Local Pressure field {name} was missing.");
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out value))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidJsonType, $"Local Pressure field {name} was not an integer.");
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadRequiredInt64(JsonElement element, string name, out long value, out LocalPressureCodecResult result)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property))
            return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, $"Local Pressure field {name} was missing.");
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out value))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidJsonType, $"Local Pressure field {name} was not an integer.");
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadRequiredBoolean(JsonElement element, string name, out bool value, out LocalPressureCodecResult result)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property))
            return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, $"Local Pressure field {name} was missing.");
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidJsonType, $"Local Pressure field {name} was not a boolean.");
        value = property.GetBoolean();
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadGameTime(JsonElement element, string name, out double? value, out LocalPressureCodecResult result)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
            return Fail(out result, LocalPressureCodecFailureReason.MissingRequiredField, $"Local Pressure field {name} was missing.");
        if (property.ValueKind == JsonValueKind.Null)
        {
            result = LocalPressureCodecResult.Success();
            return true;
        }
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var parsed) || !IsValidGameTime(parsed))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidGameTime, $"Local Pressure field {name} was not a finite, non-negative game time.");
        value = parsed;
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool TryReadOptionalContext(JsonElement element, string name, out string? value, out LocalPressureCodecResult result)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            result = LocalPressureCodecResult.Success();
            return true;
        }
        if (property.ValueKind == JsonValueKind.Null)
        {
            result = LocalPressureCodecResult.Success();
            return true;
        }
        if (property.ValueKind != JsonValueKind.String)
            return Fail(out result, LocalPressureCodecFailureReason.InvalidContext, $"Local Pressure field {name} was not a string or null.");
        value = property.GetString();
        if (!IsValidContext(value))
            return Fail(out result, LocalPressureCodecFailureReason.InvalidContext, $"Local Pressure field {name} was blank, unbounded, or contained control characters.");
        result = LocalPressureCodecResult.Success();
        return true;
    }

    private static bool IsValidIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumPlayerIdLength && value.All(character => !char.IsControl(character));

    private static bool IsValidContext(string? value) =>
        value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= MaximumContextLength && value.All(character => !char.IsControl(character)));

    private static bool IsValidGameTime(double? value) =>
        value is null || (!double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value >= 0);

    private static bool Fail(out LocalPressureCodecResult result, LocalPressureCodecFailureReason reason, string message)
    {
        result = LocalPressureCodecResult.Failure(reason, message);
        return false;
    }
}
