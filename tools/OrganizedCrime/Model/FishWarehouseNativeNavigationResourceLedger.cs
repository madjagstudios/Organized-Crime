namespace OrganizedCrime.Model;

public enum FishWarehouseNativeNavigationTeardownOperationKind
{
    RemoveLink,
    RemoveData,
    ClearNativeSampleCache
}

public readonly record struct FishWarehouseNativeNavigationTeardownOperation(
    FishWarehouseNativeNavigationTeardownOperationKind Kind,
    FishWarehouseNativeNavigationGraphRole GraphRole,
    int? ResourceKey)
{
    public override string ToString() => Kind switch
    {
        FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink =>
            $"remove-link:{GraphRole.ToString().ToLowerInvariant()}",
        FishWarehouseNativeNavigationTeardownOperationKind.RemoveData =>
            $"remove-data:{GraphRole.ToString().ToLowerInvariant()}",
        FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache =>
            "clear-native-sample-cache",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };
}

public sealed class FishWarehouseNativeNavigationResourceLedger
{
    private readonly HashSet<ResourceEntry> _data = new();
    private readonly HashSet<ResourceEntry> _links = new();
    private bool _cacheClearPending;

    public void RecordData(
        FishWarehouseNativeNavigationGraphRole graphRole,
        int resourceKey)
    {
        _data.Add(new ResourceEntry(graphRole, resourceKey));
        _cacheClearPending = true;
    }

    public void RecordLink(
        FishWarehouseNativeNavigationGraphRole graphRole,
        int resourceKey)
    {
        _links.Add(new ResourceEntry(graphRole, resourceKey));
        _cacheClearPending = true;
    }

    public bool HasPendingTeardown => _cacheClearPending;

    public IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> PendingTeardown()
    {
        if (!_cacheClearPending)
            return Array.Empty<FishWarehouseNativeNavigationTeardownOperation>();

        var operations = new List<FishWarehouseNativeNavigationTeardownOperation>(
            _links.Count + _data.Count + 1);

        AddRemoveOperations(
            _links,
            FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink,
            operations);
        AddRemoveOperations(
            _data,
            FishWarehouseNativeNavigationTeardownOperationKind.RemoveData,
            operations);
        operations.Add(new(
            FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache,
            FishWarehouseNativeNavigationGraphRole.Default,
            ResourceKey: null));

        return operations;
    }

    public void Acknowledge(FishWarehouseNativeNavigationTeardownOperation operation)
    {
        switch (operation.Kind)
        {
            case FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink:
                AcknowledgeResource(_links, operation);
                return;
            case FishWarehouseNativeNavigationTeardownOperationKind.RemoveData:
                AcknowledgeResource(_data, operation);
                return;
            case FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache:
                if (!_cacheClearPending || _links.Count != 0 || _data.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Fish Warehouse native navigation cache cannot be acknowledged before every native resource is removed.");
                }

                _cacheClearPending = false;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, null);
        }
    }

    private static void AcknowledgeResource(
        ISet<ResourceEntry> resources,
        FishWarehouseNativeNavigationTeardownOperation operation)
    {
        if (operation.ResourceKey is not int resourceKey ||
            !resources.Remove(new ResourceEntry(operation.GraphRole, resourceKey)))
        {
            throw new InvalidOperationException(
                $"Fish Warehouse native navigation teardown acknowledgement did not match {operation}.");
        }
    }

    private static void AddRemoveOperations(
        IEnumerable<ResourceEntry> resources,
        FishWarehouseNativeNavigationTeardownOperationKind kind,
        ICollection<FishWarehouseNativeNavigationTeardownOperation> operations)
    {
        foreach (ResourceEntry resource in resources
                     .OrderBy(resource => resource.GraphRole)
                     .ThenBy(resource => resource.ResourceKey))
        {
            operations.Add(new(kind, resource.GraphRole, resource.ResourceKey));
        }
    }

    private readonly record struct ResourceEntry(
        FishWarehouseNativeNavigationGraphRole GraphRole,
        int ResourceKey);
}
