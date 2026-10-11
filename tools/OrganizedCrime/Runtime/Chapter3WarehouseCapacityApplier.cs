using OrganizedCrime.Runtime.Composition;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-117: adapts the runtime property host to the Chapter 3 capacity
/// seam. A faulting host reads as detached, fails the mutation, and
/// withholds the live read, so the payment path refuses rather than
/// wedging and QA reports unknown instead of crashing. Production-only
/// (Mod.cs wires it); tests fake <see cref="IChapter3WarehouseCapacity"/>.
/// </summary>
public sealed class Chapter3WarehouseCapacityApplier : IChapter3WarehouseCapacity
{
    private readonly IFishWarehousePropertyHost _host;

    public Chapter3WarehouseCapacityApplier(IFishWarehousePropertyHost host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    public bool IsAttached()
    {
        try
        {
            return _host.IsEmployeeInfrastructureReady;
        }
        catch
        {
            return false;
        }
    }

    public bool TryApplyCapacity(int capacity)
    {
        try
        {
            return _host.TrySetEmployeeCapacity(capacity);
        }
        catch
        {
            return false;
        }
    }

    public bool TryMoveIdleGroupToPlayer(out string report)
    {
        try
        {
            return _host.TryMoveIdlePointsToPlayer(out report);
        }
        catch (Exception ex)
        {
            report = $"Chapter 3 QA capacity here failed: {ex.Message}";
            return false;
        }
    }

    public bool TryReadLive(out int capacity, out int idlePointCount)
    {
        capacity = 0;
        idlePointCount = 0;
        try
        {
            return _host.TryReadEmployeeCapacity(out capacity, out idlePointCount);
        }
        catch
        {
            return false;
        }
    }

    public bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot)
    {
        snapshot = new(0f, 0f, 0f, false, false, 0f, 0f, 0f, false, false);
        try
        {
            return _host.TryReadPlayerSpawns(out snapshot);
        }
        catch
        {
            snapshot = new(0f, 0f, 0f, false, false, 0f, 0f, 0f, false, false);
            return false;
        }
    }

    public bool TryMovePlayerSpawnToPlayer(bool exterior, out string report)
    {
        try
        {
            return _host.TryMovePlayerSpawnToPlayer(exterior, out report);
        }
        catch (Exception ex)
        {
            report = $"Chapter 3 QA spawn here refused: the warehouse threw ({ex.Message}).";
            return false;
        }
    }
}
