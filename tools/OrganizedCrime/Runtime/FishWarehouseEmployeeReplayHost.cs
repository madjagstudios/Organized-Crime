using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum FishWarehouseEmployeeReplayState
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehouseEmployeeReplayResult(
    FishWarehouseEmployeeReplayState State,
    int CapturedEmployeeCount,
    int SupportedCapturedEmployeeCount,
    int ReplayedEmployeeCount,
    IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> UnsupportedEmployees,
    bool CanAdvanceEmployeesReplayed,
    string? PrimaryFailureReason,
    string? FailureReason,
    IReadOnlyList<string>? InventoryRestoredEmployeeGuids = null);

public sealed class FishWarehouseEmployeeReplayHost
{
    public const float ConfigurationVerificationTimeoutSeconds = 3f;

    private readonly IFishWarehouseNativeEmployeeReplayAdapter _adapter;
    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, FishWarehouseUnsupportedEmployeeRecord> _unsupportedByGuid;
    private readonly Func<float> _realtimeSinceStartup;
    private readonly Action<string> _log;

    public FishWarehouseEmployeeReplayHost(
        IFishWarehouseNativeEmployeeReplayAdapter adapter,
        IReadOnlyList<FishWarehouseEmployeeReplayDescriptor> capturedEmployees,
        IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> existingUnsupportedEmployees,
        Func<float>? realtimeSinceStartup = null,
        Action<string>? log = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _entries = (capturedEmployees ?? throw new ArgumentNullException(nameof(capturedEmployees)))
            .Select(descriptor => new Entry(descriptor))
            .ToArray();
        _unsupportedByGuid = new Dictionary<string, FishWarehouseUnsupportedEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingUnsupportedEmployees ?? throw new ArgumentNullException(nameof(existingUnsupportedEmployees)))
            _unsupportedByGuid.TryAdd(existing.Guid, existing);

        _realtimeSinceStartup = realtimeSinceStartup ?? (() => UnityEngine.Time.realtimeSinceStartup);
        _log = log ?? (_ => { });
        Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
    }

    public FishWarehouseEmployeeReplayResult Result { get; private set; }

    public FishWarehouseEmployeeReplayResult Replay(bool navigationReady)
    {
        if (Result.State is FishWarehouseEmployeeReplayState.Succeeded or FishWarehouseEmployeeReplayState.Failed)
            return Result;

        PreserveUnsupportedRecords();
        if (!navigationReady)
            return Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);

        foreach (var entry in _entries)
        {
            if (entry.IsUnsupported || entry.IsReplayed)
                continue;

            if (!entry.Descriptor.IsSupportedPackager)
            {
                Preserve(entry, "unsupported employee type");
                continue;
            }

            if (entry.Expected is null)
            {
                if (!entry.Descriptor.TryParseExpectedState(out var expected, out var parseFailure))
                {
                    Preserve(entry, $"malformed supported employee payload: {parseFailure}");
                    return Fail($"malformed-employee-payload (guid={entry.Descriptor.Guid}): {parseFailure}");
                }

                entry.Expected = expected;
            }

            if (!Process(entry, entry.Expected!))
                return Result;
        }

        return Result = BuildResult(FishWarehouseEmployeeReplayState.Succeeded, null);
    }

    private bool Process(Entry entry, FishWarehouseEmployeeReplayExpectedState expected)
    {
        var now = _realtimeSinceStartup();
        if (entry.Stage == ReplayStage.NotStarted)
        {
            if (!_adapter.TryIsGuidRegistered(entry.Descriptor.Guid, out var registered, out var registrationFailure))
            {
                Preserve(entry, "GUID interop rejected the saved GUID");
                Fail($"guid-interop-failure (guid={entry.Descriptor.Guid}): {registrationFailure}");
                return false;
            }

            entry.GuidWasRegistered = registered;
            entry.PrimaryStartedAt = now;
            if (registered)
            {
                entry.Stage = ReplayStage.PrimaryVerification;
            }
            else
            {
                var load = _adapter.TryLoadPrimary(entry.Descriptor, out var loadFailure);
                switch (load)
                {
                    case FishWarehouseEmployeePrimaryLoadResult.Started:
                        entry.Stage = ReplayStage.PrimaryVerification;
                        break;
                    case FishWarehouseEmployeePrimaryLoadResult.LoaderUnavailable:
                        Preserve(entry, "registry loader was unavailable");
                        return true;
                    case FishWarehouseEmployeePrimaryLoadResult.Failed:
                        entry.PrimaryFailureReason = $"primary-failure (guid={entry.Descriptor.Guid}): {loadFailure}";
                        return StartFallback(entry, expected, now);
                    default:
                        throw new InvalidOperationException("Unknown primary replay result.");
                }
            }
        }

        if (entry.Stage == ReplayStage.PrimaryVerification)
        {
            if (!TryRestoreActiveMove(entry, expected, out var moveRestoreFailure))
            {
                if (moveRestoreFailure is not null)
                    entry.LastVerificationReason = moveRestoreFailure;
                if (_realtimeSinceStartup() < entry.PrimaryStartedAt + ConfigurationVerificationTimeoutSeconds)
                    return false;
            }

            var verification = TryVerify(entry, expected, out var verificationReason);
            if (verification == FishWarehouseEmployeeObservationResult.Ready)
            {
                entry.IsReplayed = true;
                return true;
            }

            if (verification == FishWarehouseEmployeeObservationResult.Failed)
            {
                entry.PrimaryFailureReason = $"primary-observation-failure (guid={entry.Descriptor.Guid}): {verificationReason}";
                return entry.GuidWasRegistered
                    ? StartRegisteredConfiguration(entry, expected, now)
                    : StartFallback(entry, expected, now);
            }

            if (now < entry.PrimaryStartedAt + ConfigurationVerificationTimeoutSeconds)
            {
                entry.LastVerificationReason = verificationReason;
                Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
                return false;
            }

            entry.PrimaryFailureReason = $"primary-timeout (guid={entry.Descriptor.Guid}): {verificationReason}";
            return entry.GuidWasRegistered
                ? StartRegisteredConfiguration(entry, expected, now)
                : StartFallback(entry, expected, now);
        }

        if (entry.Stage == ReplayStage.FallbackVerification)
        {
            var configured = _adapter.TryConfigureFallback(entry.Descriptor, expected, out var configurationFailure);
            if (configured == FishWarehouseEmployeeFallbackConfigurationResult.Failed)
            {
                Preserve(entry, "fallback configuration failed");
                Fail($"fallback-configuration-failure (guid={entry.Descriptor.Guid}): {configurationFailure}");
                return false;
            }

            if (!TryRestoreActiveMove(entry, expected, out var moveRestoreFailure))
            {
                if (moveRestoreFailure is not null)
                    entry.LastVerificationReason = moveRestoreFailure;
                Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
                return false;
            }

            string? verificationReason = null;
            var verification = configured == FishWarehouseEmployeeFallbackConfigurationResult.Configured
                ? TryVerify(entry, expected, out verificationReason)
                : FishWarehouseEmployeeObservationResult.Pending;
            if (verification == FishWarehouseEmployeeObservationResult.Ready)
            {
                entry.IsReplayed = true;
                return true;
            }

            if (now < entry.FallbackStartedAt + ConfigurationVerificationTimeoutSeconds)
            {
                entry.LastVerificationReason = configured == FishWarehouseEmployeeFallbackConfigurationResult.Pending
                    ? "fallback configuration pending"
                    : verificationReason;
                Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
                return false;
            }

            Preserve(entry, "fallback configuration verification timed out");
            Fail($"fallback-configuration-timeout (guid={entry.Descriptor.Guid}): {entry.LastVerificationReason ?? "unavailable"}");
            return false;
        }

        throw new InvalidOperationException("Unknown employee replay stage.");
    }

    private bool TryRestoreActiveMove(
        Entry entry,
        FishWarehouseEmployeeReplayExpectedState expected,
        out string? failureReason)
    {
        var result = _adapter.TryRestoreActiveMove(entry.Descriptor, expected, out failureReason);
        if (result == FishWarehouseEmployeeActiveMoveRestoreResult.Failed)
        {
            Fail($"active-move-restore-failure (guid={entry.Descriptor.Guid}): {failureReason}");
            return false;
        }

        if (result == FishWarehouseEmployeeActiveMoveRestoreResult.Pending)
        {
            Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
            return false;
        }

        return true;
    }

    private bool StartFallback(Entry entry, FishWarehouseEmployeeReplayExpectedState expected, float now)
    {
        if (!_adapter.TryIsGuidRegistered(entry.Descriptor.Guid, out var registered, out var registrationFailure))
        {
            Fail($"fallback-guid-adoption-check-failed (guid={entry.Descriptor.Guid}): {registrationFailure}");
            return false;
        }

        if (registered)
            return StartRegisteredConfiguration(entry, expected, now);

        if (entry.FallbackAttempted)
        {
            Fail($"fallback-retry-rejected (guid={entry.Descriptor.Guid})");
            return false;
        }

        entry.FallbackAttempted = true;
        var fallback = _adapter.TryCreateFallback(entry.Descriptor, expected, out var fallbackFailure);
        if (fallback == FishWarehouseEmployeeFallbackCreationResult.Failed)
        {
            Preserve(entry, "fallback creation failed");
            Fail($"fallback-creation-failure (guid={entry.Descriptor.Guid}): {fallbackFailure}");
            return false;
        }

        entry.Stage = ReplayStage.FallbackVerification;
        entry.FallbackStartedAt = now;
        Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
        return false;
    }

    private bool StartRegisteredConfiguration(
        Entry entry,
        FishWarehouseEmployeeReplayExpectedState expected,
        float now)
    {
        entry.Stage = ReplayStage.FallbackVerification;
        entry.FallbackStartedAt = now;
        entry.LastVerificationReason = entry.PrimaryFailureReason;
        Result = BuildResult(FishWarehouseEmployeeReplayState.Pending, null);
        return false;
    }

    private FishWarehouseEmployeeObservationResult TryVerify(
        Entry entry,
        FishWarehouseEmployeeReplayExpectedState expected,
        out string? reason)
    {
        var observed = _adapter.TryObserveEmployee(entry.Descriptor.Guid, out var employee, out reason);
        if (observed != FishWarehouseEmployeeObservationResult.Ready || employee is null)
            return observed;

        if (!employee.IsAlive)
        {
            reason = "employee-not-alive";
            return FishWarehouseEmployeeObservationResult.Pending;
        }

        reason = Compare(expected, employee);
        return reason is null
            ? FishWarehouseEmployeeObservationResult.Ready
            : FishWarehouseEmployeeObservationResult.Pending;
    }

    private static string? Compare(
        FishWarehouseEmployeeReplayExpectedState expected,
        FishWarehouseEmployeeReplayObservedState actual)
    {
        if (!SameGuid(expected.Guid, actual.Guid)) return "guid-mismatch";
        if (!string.Equals(expected.Identity, actual.Identity, StringComparison.Ordinal)) return "identity-mismatch";
        if (!string.Equals(expected.Id, actual.Id, StringComparison.Ordinal)) return "id-mismatch";
        if (!string.Equals(expected.FirstName, actual.FirstName, StringComparison.Ordinal)) return "first-name-mismatch";
        if (!string.Equals(expected.LastName, actual.LastName, StringComparison.Ordinal)) return "last-name-mismatch";
        if (expected.IsMale != actual.IsMale) return "sex-mismatch";
        if (expected.AppearanceIndex != actual.AppearanceIndex) return "appearance-mismatch";
        if (!string.Equals(expected.PropertyCode, actual.PropertyCode, StringComparison.Ordinal)) return "property-mismatch";
        if (expected.PaidForToday != actual.PaidForToday) return "paid-state-mismatch";
        if (expected.MoveItem != actual.MoveItem) return "move-item-mismatch";
        if (!SameGuid(expected.HomeGuid, actual.HomeGuid)) return "home-mismatch";
        if (!SameGuidSet(expected.StationGuids, actual.StationGuids)) return "stations-mismatch";
        return null;
    }

    private static bool SameGuid(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return string.IsNullOrWhiteSpace(expected) && string.IsNullOrWhiteSpace(actual);

        return System.Guid.TryParse(expected, out var expectedGuid) &&
            System.Guid.TryParse(actual, out var actualGuid) &&
            expectedGuid == actualGuid;
    }

    private static bool SameGuidSet(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var expectedSet = expected.Select(CanonicalGuid).ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.Select(CanonicalGuid).ToHashSet(StringComparer.Ordinal);
        return expectedSet.Count == expected.Count && actualSet.Count == actual.Count && expectedSet.SetEquals(actualSet);
    }

    private static string CanonicalGuid(string value) =>
        System.Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : value;

    private void PreserveUnsupportedRecords()
    {
        foreach (var entry in _entries.Where(entry => !entry.Descriptor.IsSupportedPackager))
            Preserve(entry, "unsupported employee type");
    }

    private void Preserve(Entry entry, string reason)
    {
        if (entry.Descriptor.IsSupportedPackager &&
            !reason.Contains("malformed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (entry.IsUnsupported)
            return;

        entry.IsUnsupported = true;
        _unsupportedByGuid.TryAdd(
            entry.Descriptor.Guid,
            new FishWarehouseUnsupportedEmployeeRecord(
                entry.Descriptor.Guid,
                entry.Descriptor.DataType,
                entry.Descriptor.Identity,
                entry.Descriptor.RawJson));
        _log($"WARNING: Fish Warehouse employee replay preserved unsupported record (guid={entry.Descriptor.Guid}, type={entry.Descriptor.DataType}): {reason}.");
    }

    private FishWarehouseEmployeeReplayResult Fail(string reason)
    {
        foreach (var entry in _entries.Where(entry => !entry.IsReplayed && !entry.Descriptor.IsSupportedPackager))
            Preserve(entry, "replay did not complete");

        return Result = BuildResult(FishWarehouseEmployeeReplayState.Failed, reason);
    }

    private FishWarehouseEmployeeReplayResult BuildResult(FishWarehouseEmployeeReplayState state, string? failureReason)
    {
        var supportedCount = _entries.Count(entry => entry.Descriptor.IsSupportedPackager);
        var replayedCount = _entries.Count(entry => entry.IsReplayed);
        var primaryFailure = _entries.Select(entry => entry.PrimaryFailureReason).FirstOrDefault(reason => reason is not null);
        return new FishWarehouseEmployeeReplayResult(
            state,
            _entries.Count,
            supportedCount,
            replayedCount,
            _unsupportedByGuid.Values.ToArray(),
            state == FishWarehouseEmployeeReplayState.Succeeded && replayedCount == supportedCount,
            primaryFailure,
            failureReason,
            _adapter.InventoryRestoredEmployeeGuids.ToArray());
    }

    private enum ReplayStage
    {
        NotStarted,
        PrimaryVerification,
        FallbackVerification
    }

    private sealed class Entry
    {
        public Entry(FishWarehouseEmployeeReplayDescriptor descriptor) => Descriptor = descriptor;

        public FishWarehouseEmployeeReplayDescriptor Descriptor { get; }
        public FishWarehouseEmployeeReplayExpectedState? Expected { get; set; }
        public ReplayStage Stage { get; set; }
        public bool GuidWasRegistered { get; set; }
        public bool FallbackAttempted { get; set; }
        public bool IsReplayed { get; set; }
        public bool IsUnsupported { get; set; }
        public float PrimaryStartedAt { get; set; }
        public float FallbackStartedAt { get; set; }
        public string? PrimaryFailureReason { get; set; }
        public string? LastVerificationReason { get; set; }
    }
}
