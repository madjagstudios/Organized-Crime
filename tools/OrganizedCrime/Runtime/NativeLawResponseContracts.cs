using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed record LocalPressureTierTransitionNotification(
    Guid SessionEpoch, long LoadEpoch, string PlayerId, object? SourcePlayer,
    string CorrelationId, LocalPressureTier PreviousTier, LocalPressureTier CurrentTier,
    string? Region, string? PropertyCode)
{
    public Guid SessionEpoch { get; } = SessionEpoch;
    public long LoadEpoch { get; } = LoadEpoch;
    public string PlayerId { get; } = Required(PlayerId, nameof(PlayerId));
    public object? SourcePlayer { get; } = SourcePlayer;
    public string CorrelationId { get; } = Required(CorrelationId, nameof(CorrelationId));
    public LocalPressureTier PreviousTier { get; } = PreviousTier;
    public LocalPressureTier CurrentTier { get; } = CurrentTier;
    public string? Region { get; } = Region;
    public string? PropertyCode { get; } = PropertyCode;
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value;
}

public sealed record NativeLawResponseProfile(int RequestedOfficerCount, bool UseVehicle, bool BeginAsSighted)
{
    public int RequestedOfficerCount { get; } = RequestedOfficerCount > 0 ? RequestedOfficerCount : throw new ArgumentOutOfRangeException(nameof(RequestedOfficerCount));
    public bool UseVehicle { get; } = UseVehicle;
    public bool BeginAsSighted { get; } = BeginAsSighted;
    public static NativeLawResponseProfile VehicleTwoOfficerV1 { get; } = new(2, true, false);
}

public sealed record NativeLawResponseRequest(
    Guid SessionEpoch, long LoadEpoch, string PlayerId, object SourcePlayer,
    string CorrelationId, LocalPressureTier TriggerTier, string? Region,
    string? PropertyCode, NativeLawResponseProfile Profile)
{
    public Guid SessionEpoch { get; } = SessionEpoch;
    public long LoadEpoch { get; } = LoadEpoch;
    public string PlayerId { get; } = Required(PlayerId, nameof(PlayerId));
    public object SourcePlayer { get; } = SourcePlayer ?? throw new ArgumentNullException(nameof(SourcePlayer));
    public string CorrelationId { get; } = Required(CorrelationId, nameof(CorrelationId));
    public LocalPressureTier TriggerTier { get; } = TriggerTier;
    public string? Region { get; } = Region;
    public string? PropertyCode { get; } = PropertyCode;
    public NativeLawResponseProfile Profile { get; } = Profile ?? throw new ArgumentNullException(nameof(Profile));
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value;
}

public enum NativeLawResponseResultState { Accepted, Suppressed, Rejected, UnavailableCapacity, InconclusiveOutcome, AdapterUnavailable, AuthorityRejected, TargetRejected }
public sealed record NativeLawResponseDiagnostics(string? StationIdentity, int? OfficersBefore, int? OfficersConsumed, int? VehiclesBefore, int? VehiclesConsumed)
{
    public string? StationIdentity { get; } = StationIdentity;
    public int? OfficersBefore { get; } = OfficersBefore;
    public int? OfficersConsumed { get; } = OfficersConsumed;
    public int? VehiclesBefore { get; } = VehiclesBefore;
    public int? VehiclesConsumed { get; } = VehiclesConsumed;
}

public sealed record NativeLawResponseResult(
    NativeLawResponseResultState State, string CorrelationId, string Reason,
    NativeLawResponseDiagnostics? Diagnostics = null)
{
    public NativeLawResponseResultState State { get; } = State;
    public string CorrelationId { get; } = Required(CorrelationId, nameof(CorrelationId));
    public string Reason { get; } = Required(Reason, nameof(Reason));
    public NativeLawResponseDiagnostics? Diagnostics { get; } = Diagnostics;
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value;
}

public interface ILocalPressureTierTransitionSink { void Publish(LocalPressureTierTransitionNotification notification); void ResetForEpoch(Guid sessionEpoch, long loadEpoch); }
public interface INativeLawResponseAdapter { NativeLawResponseResult TryRequest(NativeLawResponseRequest request); }
