namespace OrganizedCrime.Census;

public sealed class CensusTimeEventBindingState
{
    private object? _boundManager;

    public bool IsBoundTo(object? manager) =>
        manager is not null && ReferenceEquals(_boundManager, manager);

    public void MarkBound(object manager) =>
        _boundManager = manager ?? throw new ArgumentNullException(nameof(manager));

    public void Clear() => _boundManager = null;
}

public sealed class CensusCapacityNotice
{
    private bool _reported;

    public bool ShouldReport(bool capacityReached)
    {
        if (!capacityReached || _reported)
            return false;

        _reported = true;
        return true;
    }
}

public static class CensusAuthorityProjection
{
    public static bool IsHost(bool isServer, bool isClient) => isServer && isClient;
}
