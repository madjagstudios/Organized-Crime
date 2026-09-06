using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum LocalPressureRuntimePhase
{
    Detached,
    AwaitingLoad,
    AwaitingHostBaseline,
    Active,
    Quarantined,
    Saving,
    Disposed
}

public enum LocalPressureClockBoundary
{
    HostReady,
    Hour,
    Day,
    SleepEnd
}

public enum LocalPressureRuntimeRejectReason
{
    None,
    HostNotReady,
    NotAuthoritativeHost,
    MissingClock,
    InvalidClock,
    StaleSequence,
    DuplicateSample,
    ClockRewound,
    WrongEpoch,
    LifecycleTransition,
    IdentityUnavailable,
    IdentityAmbiguous,
    UnsupportedMultiplayer,
    SavePathUnavailable,
    SidecarLoadFailed,
    SidecarSaveFailed,
    AdapterFailure,
    Disposed
}

public readonly record struct LocalPressureClockSample(
    long TotalGameMinutes,
    int ElapsedDays,
    int Time24h,
    uint? SourceSequence,
    LocalPressureClockBoundary Boundary,
    DateTime ReceivedAtUtc);

public readonly record struct LocalPressurePlayerSample(
    string? PlayerId,
    bool IsHostOwned,
    bool IsLocalPlayer,
    string? PlayerCode,
    string? Region,
    string? PropertyCode,
    object? SourcePlayer = null,
    bool HasConnection = true);

public enum LocalPressureHostAuthorityReadStatus
{
    Ready,
    Pending,
    NotAuthoritative,
    Faulted
}

public enum LocalPressurePlayerReadStatus
{
    Ready,
    Pending,
    UnsupportedMultiplayer,
    Faulted
}

public enum LocalPressureClockReadStatus
{
    Ready,
    Pending,
    Faulted
}

public enum LocalPressureClockBoundaryBindingStatus
{
    Ready,
    Pending,
    Faulted
}

public delegate LocalPressureDecayResult LocalPressureDecayEvaluator(
    LocalPressureDecayInput input,
    LocalPressureProfile profile);

public interface ILocalPressureRuntimeHostAdapter : IDisposable
{
    LocalPressureHostAuthorityReadStatus ReadHostAuthority();
    LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions();
    string? ActiveSaveFolder { get; }
    string? CanonicalHostIdentity { get; }
    event Action? PreLoad;
    event Action? LoadComplete;
    event Action? SaveStart;
    event Action? SaveComplete;
    event Action<LocalPressureClockBoundary>? ClockBoundary;
    LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample);
    LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players);
}

public interface ILocalPressureStateRepository
{
    LocalPressureStoreLoadResult Load();
    LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record);
}

public readonly record struct LocalPressureActiveEvidenceSnapshot(
    Guid SessionEpoch,
    long LoadEpoch,
    string PlayerId,
    object? SupportedPlayer);

public sealed record CustodyEntryEvidence(
    string PlayerId,
    string CorrelationId,
    string? Region,
    string? PropertyCode);

public enum LocalPressureEvidenceWriteRejectReason
{
    None,
    RuntimeNotActive,
    NotAuthoritativeHost,
    MissingIdentity,
    PlayerMismatch,
    StaleEpoch,
    MissingCorrelation,
    InvalidCorrelation,
    DuplicateCorrelation,
    ClockUnavailable,
    InvalidClock,
    UnsupportedMultiplayer,
    AdapterFailure,
    Disposed
}

public sealed record LocalPressureEvidenceWriteResult(
    bool Accepted,
    LocalPressureEvidenceWriteRejectReason RejectReason,
    LocalPressureState? State,
    string Message);

public delegate LocalPressureTransitionResult LocalPressureEvidenceEvaluator(
    LocalPressureState state,
    LocalPressureEvidenceEvent evidence,
    LocalPressureProfile profile);

public interface ILocalPressureCustodyEvidenceWriter
{
    bool TryGetActiveEvidenceSnapshot(out LocalPressureActiveEvidenceSnapshot snapshot);

    LocalPressureEvidenceWriteResult TryApplyCustodyEvidence(
        CustodyEntryEvidence evidence,
        Guid sessionEpoch,
        long loadEpoch);
}
