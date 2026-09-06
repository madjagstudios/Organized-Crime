using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum FishWarehousePropertyReplayState
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehousePropertyReplayResult(
    FishWarehousePropertyReplayState State,
    int ExpectedObjectCount,
    int ResolvedObjectCount,
    int LoadedObjectCount,
    string? FailureReason);

public sealed class FishWarehousePropertyReplayHost
{
    private readonly IFishWarehouseNativePropertyReplayAdapter _adapter;
    private readonly IReadOnlyList<FishWarehouseSavedObjectDescriptor> _descriptors;
    private readonly Action? _placeFallbackEmployeeHome;
    private readonly Action<string> _log;
    private readonly HashSet<int> _loadedPayloadIndexes = new();
    private bool _fallbackPlaced;

    public FishWarehousePropertyReplayHost(
        IFishWarehouseNativePropertyReplayAdapter adapter,
        IReadOnlyList<FishWarehouseSavedObjectDescriptor> descriptors,
        Action? placeFallbackEmployeeHome = null,
        Action<string>? log = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
        _placeFallbackEmployeeHome = placeFallbackEmployeeHome;
        _log = log ?? (_ => { });
        Result = new FishWarehousePropertyReplayResult(
            FishWarehousePropertyReplayState.Pending,
            UniqueGuidCount(),
            0,
            0,
            null);
    }

    public FishWarehousePropertyReplayResult Result { get; private set; }

    public FishWarehouseEmployeeReplayResult ReplayEmployees(
        FishWarehouseEmployeeReplayHost employeeReplay,
        bool navigationReady)
    {
        if (employeeReplay is null)
            throw new ArgumentNullException(nameof(employeeReplay));

        return Result.State == FishWarehousePropertyReplayState.Succeeded
            ? employeeReplay.Replay(navigationReady)
            : employeeReplay.Result;
    }

    public FishWarehousePropertyReplayResult Replay()
    {
        if (Result.State == FishWarehousePropertyReplayState.Succeeded)
            return Result;

        var planned = BuildPlan(out var planningFailure);
        if (planningFailure is not null)
            return Fail(planningFailure);

        foreach (var entry in planned)
        {
            if (_adapter.IsGuidRegistered(entry.Descriptor.Guid))
                continue;

            if (_loadedPayloadIndexes.Add(entry.Descriptor.PayloadIndex))
            {
                try
                {
                    _adapter.LoadObject(entry.Descriptor.PayloadIndex);
                }
                catch (Exception ex)
                {
                    return Fail($"loader-failure (index={entry.Descriptor.PayloadIndex}, type={entry.Descriptor.DataType}, guid={entry.Descriptor.Guid}): {ex.Message}");
                }
            }
        }

        var restored = new List<FishWarehouseRestoredObjectObservation>(planned.Count);
        foreach (var entry in planned)
        {
            FishWarehouseRestoredObjectObservation observation;
            try
            {
                observation = _adapter.ObserveObject(entry.Descriptor.Guid);
            }
            catch (Exception ex)
            {
                return Fail($"observation-failure (index={entry.Descriptor.PayloadIndex}, guid={entry.Descriptor.Guid}): {ex.Message}");
            }

            if (!observation.IsResolved)
                return Fail($"unresolved-guid (index={entry.Descriptor.PayloadIndex}, guid={entry.Descriptor.Guid})");

            if (!string.Equals(
                    observation.ParentPropertyCode,
                    RuntimePropertyDefinition.FishWarehouse.PropertyCode,
                    StringComparison.Ordinal))
            {
                return Fail($"wrong-parent-property (index={entry.Descriptor.PayloadIndex}, guid={entry.Descriptor.Guid}, ParentPropertyCode={observation.ParentPropertyCode ?? "null"})");
            }

            restored.Add(observation);
        }

        if (!_fallbackPlaced &&
            !planned.Any(entry => entry.Descriptor.IsSavedEmployeeHome) &&
            !restored.Any(observation => observation.IsEmployeeHome))
        {
            _fallbackPlaced = true;
            _placeFallbackEmployeeHome?.Invoke();
        }

        Result = new FishWarehousePropertyReplayResult(
            FishWarehousePropertyReplayState.Succeeded,
            planned.Count,
            restored.Count,
            _loadedPayloadIndexes.Count,
            null);
        _log($"Fish Warehouse native replay summary: {Result.ResolvedObjectCount}/{Result.ExpectedObjectCount} resolved, loaded={Result.LoadedObjectCount}.");
        return Result;
    }

    private IReadOnlyList<PlannedObject> BuildPlan(out string? failureReason)
    {
        failureReason = null;
        var planned = new List<PlannedObject>();
        foreach (var descriptor in _descriptors)
        {
            if (!System.Guid.TryParse(descriptor.Guid, out var parsedGuid))
            {
                failureReason = $"malformed-guid (index={descriptor.PayloadIndex}, type={descriptor.DataType}, guid={descriptor.Guid})";
                return Array.Empty<PlannedObject>();
            }

            if (!_adapter.TryGetObjectLoaderLoadOrder(descriptor.DataType, out var loaderLoadOrder))
            {
                failureReason = $"missing-loader (index={descriptor.PayloadIndex}, type={descriptor.DataType}, guid={descriptor.Guid})";
                return Array.Empty<PlannedObject>();
            }

            planned.Add(new PlannedObject(descriptor, loaderLoadOrder, parsedGuid.ToString("D")));
        }

        var ordered = planned
            .OrderBy(entry => entry.LoaderLoadOrder)
            .ThenBy(entry => entry.Descriptor.SavedLoadOrder)
            .ThenBy(entry => entry.Descriptor.IsDependent ? 1 : 0)
            .ThenBy(entry => entry.Descriptor.PayloadIndex)
            .ToArray();
        var seenGuids = new HashSet<string>(StringComparer.Ordinal);
        return ordered.Where(entry => seenGuids.Add(entry.GuidKey)).ToArray();
    }

    private FishWarehousePropertyReplayResult Fail(string reason)
    {
        Result = new FishWarehousePropertyReplayResult(
            FishWarehousePropertyReplayState.Failed,
            UniqueGuidCount(),
            0,
            _loadedPayloadIndexes.Count,
            reason);
        _log($"Fish Warehouse native replay failed: {reason}");
        return Result;
    }

    private int UniqueGuidCount() =>
        _descriptors.Select(descriptor => CanonicalGuidKey(descriptor.Guid)).Distinct(StringComparer.Ordinal).Count();

    private static string CanonicalGuidKey(string savedGuid) =>
        System.Guid.TryParse(savedGuid, out var parsedGuid) ? parsedGuid.ToString("D") : savedGuid;

    private sealed record PlannedObject(
        FishWarehouseSavedObjectDescriptor Descriptor,
        int LoaderLoadOrder,
        string GuidKey);
}
