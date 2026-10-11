using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public interface IChapter3DayClock
{
    event Action? DayPassed;
    event Action? WeekPassed;
    bool TryReadElapsedDays(out int elapsedDays);
}

public interface IChapter3StoryGate
{
    bool IsEnvelopeSatisfied();
}

/// <summary>
/// OC-117: the Chapter 3 side of warehouse staff capacity. The adapter
/// below is the production implementation over the runtime property host;
/// tests fake this seam.
/// </summary>
public interface IChapter3WarehouseCapacity
{
    bool IsAttached();
    bool TryApplyCapacity(int capacity);
    bool TryReadLive(out int capacity, out int idlePointCount);

    /// <summary>
    /// Moves every idle point into one block centred on the local player and reports the
    /// warehouse-local positions, so a chosen layout can be baked into the definition. Fakes
    /// that never place points keep the refusal.
    /// </summary>
    bool TryMoveIdleGroupToPlayer(out string report)
    {
        report = "Chapter 3 QA capacity here refused: the warehouse cannot place idle points.";
        return false;
    }

    /// <summary>
    /// Reads the runtime player spawn points for `oc_ch3 warehouse spawns`. Fakes that never
    /// attach a warehouse keep the refusal.
    /// </summary>
    bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot)
    {
        snapshot = new(0f, 0f, 0f, false, false, 0f, 0f, 0f, false, false);
        return false;
    }

    /// <summary>
    /// Moves one player spawn point to the local player for the session and reports its
    /// room-frame coordinates, so a chosen spot can be baked into the definition. True moves
    /// the exterior point, false the interior one.
    /// </summary>
    bool TryMovePlayerSpawnToPlayer(bool exterior, out string report)
    {
        report = "Chapter 3 QA spawn here refused: the warehouse cannot place spawn points.";
        return false;
    }
}

/// <summary>
/// World positions of the runtime player spawn points, plus whether each Property field is
/// assigned and still points at the transform the spawn host created.
/// </summary>
public sealed record FishWarehousePlayerSpawnQaSnapshot(
    float ExteriorX,
    float ExteriorY,
    float ExteriorZ,
    bool ExteriorAssigned,
    bool ExteriorRetained,
    float InteriorX,
    float InteriorY,
    float InteriorZ,
    bool InteriorAssigned,
    bool InteriorRetained);

/// <summary>
/// Reads whether the player owns the Fish Warehouse right now. The weekly cut
/// falls due only while owned; the warehouse itself is never repossessed.
/// </summary>
public interface IChapter3WarehouseOwnership
{
    bool IsOwned();
}

/// <summary>
/// Reads warehouse ownership from the Fish Warehouse runtime's current state.
/// The delegate keeps the Chapter 3 service free of a dependency on the
/// warehouse service. A faulting read counts as not owned, so a broken
/// warehouse never opens cuts.
/// </summary>
public sealed class Chapter3WarehouseOwnership : IChapter3WarehouseOwnership
{
    private readonly Func<bool> _readOwned;

    public Chapter3WarehouseOwnership(Func<bool> readOwned) =>
        _readOwned = readOwned ?? throw new ArgumentNullException(nameof(readOwned));

    public bool IsOwned()
    {
        try
        {
            return _readOwned();
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// The Chapter 3 side of the warehouse purchase: ownership, its
/// settled-ness, acquisition readiness, and the acquire step. The adapter
/// below is the production implementation; tests fake this seam.
/// </summary>
public interface IChapter3WarehouseAcquisition
{
    bool IsOwned();
    bool CanAcquire();
    bool TryAcquire();

    /// <summary>
    /// Whether the ownership answer above is settled. A blocked or
    /// degraded load capture, or an in-flight replay under snapshot
    /// protection, leaves ownership unknown, and the story build must
    /// wait for it. True by default so fakes that only model ownership
    /// keep their meaning; the production adapter overrides it.
    /// </summary>
    bool IsOwnershipSettled() => true;
}

/// <summary>
/// Adapts the fish warehouse runtime service to the Chapter 3 purchase seam.
/// A faulting service reads as not owned / not acquirable and fails the
/// mutation, so the purchase path refuses rather than wedging. A missing
/// or faulting settled-ness probe reads as unsettled, so the story build
/// waits rather than building over an unknown warehouse.
/// </summary>
public sealed class Chapter3WarehouseAcquisition : IChapter3WarehouseAcquisition
{
    private readonly FishWarehouseRuntimeService _service;
    private readonly Func<bool> _isOwnershipSettled;

    public Chapter3WarehouseAcquisition(
        FishWarehouseRuntimeService service,
        Func<bool>? isOwnershipSettled = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _isOwnershipSettled = isOwnershipSettled ?? (() => true);
    }

    public bool IsOwned()
    {
        try
        {
            return _service.OwnershipUnlocked;
        }
        catch
        {
            return false;
        }
    }

    public bool IsOwnershipSettled()
    {
        try
        {
            return _isOwnershipSettled();
        }
        catch
        {
            return false;
        }
    }

    public bool CanAcquire()
    {
        try
        {
            return _service.CanAcquireForStory;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAcquire()
    {
        try
        {
            return _service.TryAcquireForStory();
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Reads the Chapter 3 start trigger from the Release 1 story runtime's current
/// state: The Envelope mission is Satisfied. The delegate keeps the model and the
/// Chapter 3 service free of a dependency on the story service.
/// </summary>
public sealed class Chapter3StoryGate : IChapter3StoryGate
{
    private readonly Func<Release1StoryState?> _readStory;

    public Chapter3StoryGate(Func<Release1StoryState?> readStory) =>
        _readStory = readStory ?? throw new ArgumentNullException(nameof(readStory));

    public bool IsEnvelopeSatisfied()
    {
        try
        {
            var story = _readStory();
            if (story is null)
                return false;

            var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
            if (index < 0 || index >= story.Missions.Count)
                return false;

            return story.Missions[index].State == Release1MissionState.Satisfied;
        }
        catch
        {
            return false;
        }
    }
}

public interface IChapter3Repository
{
    Chapter3StoreLoadResult Load();
    Chapter3StoreUpdateResult Update(Chapter3State? state);
}
public interface IChapter3SaveFolderBoundRepository
{
    string BoundSaveFolder { get; }
}
public interface IChapter3RawContentPreservingRepository
{
    Chapter3StoreUpdateResult PreserveRawContents(string rawContents);
}
public interface IChapter3RepositoryFactory
{
    IChapter3Repository Create(string activeSaveFolder);
}
public sealed class Chapter3StateStoreRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
{
    private readonly Chapter3StateStore _store;
    public Chapter3StateStoreRepository(Chapter3StateStore store) => _store = store;
    public string BoundSaveFolder => _store.SavePath.ActiveSaveFolder;
    public Chapter3StoreLoadResult Load() { _store.TryLoad(out var result); return result; }
    public Chapter3StoreUpdateResult Update(Chapter3State? state) { _store.TryUpdate(state, out var result); return result; }
    public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) { _store.TryPreserveRawContents(rawContents, out var result); return result; }
}
public enum Chapter3RuntimePhase { Detached, AwaitingLoad, Active, Saving, Quarantined, Disposed }
public enum Chapter3RuntimeRejectReason
{
    None, Inactive, ContextPending, NotAuthoritative, UnsupportedMultiplayer, AmbiguousIdentity,
    ContextFaulted, IdentityMismatch, SidecarLoadFailed, SidecarSaveFailed, Quarantined, Disposed,
    RepositoryUnbound, RepositoryPathMismatch, RepositoryFaulted
}
public sealed record Chapter3RuntimeLifecycleResult(Chapter3RuntimePhase Phase, Chapter3RuntimeRejectReason RejectReason, string Message);

public static class Chapter3LifecycleLogging
{
    public static void LogIfRejected(string phase, Chapter3RuntimeLifecycleResult? result)
    {
        if (result is null || result.RejectReason == Chapter3RuntimeRejectReason.None) return;
        OrganizedCrimeLog.Warning($"Chapter 3 runtime {phase} rejected: {result.RejectReason}: {result.Message}");
    }
}
