using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

/// <summary>
/// The versioned Local Pressure sidecar envelope. The wire contract is one schema
/// version containing a complete, per-player collection; it has no profile/state
/// tuning version and no regional Heat ledgers.
/// </summary>
public sealed record LocalPressureSaveEnvelope(
    int SchemaVersion,
    IReadOnlyList<LocalPressurePlayerRecord> Players)
{
    public static LocalPressureSaveEnvelope CreateEmpty() =>
        new(LocalPressureSaveCodec.CurrentSchemaVersion, Array.Empty<LocalPressurePlayerRecord>());
}

/// <summary>
/// Persistence DTO for one stable network/player identity. Region and Property
/// are presentation context only; they do not partition the player's Heat.
/// </summary>
public sealed record LocalPressurePlayerRecord(
    string PlayerId,
    int LocalHeat,
    bool KnownOffender,
    double? LastEvidenceGameTime,
    double? QuietGraceUntil,
    double? LastDecayEvaluation,
    string? Region,
    string? Property,
    long Revision)
{
    public static LocalPressurePlayerRecord CreateDefault(string playerId) => new(
        playerId,
        LocalHeat: 0,
        KnownOffender: false,
        LastEvidenceGameTime: null,
        QuietGraceUntil: null,
        LastDecayEvaluation: null,
        Region: null,
        Property: null,
        Revision: 0);

    public static LocalPressurePlayerRecord FromState(LocalPressureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.PlayerId))
            throw new ArgumentException("Persisted Local Pressure state requires a player identity.", nameof(state));

        return new LocalPressurePlayerRecord(
            state.PlayerId,
            state.LocalHeat,
            state.KnownOffender,
            state.LastEvidenceGameTime,
            state.QuietGraceUntil,
            state.LastDecayEvaluation,
            state.Region,
            state.PropertyCode,
            state.Revision);
    }

    public LocalPressureState ToState() => new(
        LocalHeat,
        KnownOffender,
        LastEvidenceGameTime,
        QuietGraceUntil,
        LastDecayEvaluation,
        PlayerId,
        Region,
        Property,
        Revision);
}
