namespace OrganizedCrime.Runtime;

/// <summary>
/// Lifecycle of one roster slot. Requested waits for spawn work, Spawning has
/// a ready template and is mid-attempt, Active holds a live clone, KnockedOut
/// holds a knocked-out clone awaiting its respawn window, and Failed stays
/// empty until the caller releases and re-requests the slot.
/// </summary>
public enum OcOfficerSlotState
{
    Requested,
    Spawning,
    Active,
    KnockedOut,
    Failed
}

public enum OcOfficerRequestStatus
{
    Accepted,
    RefusedCap,
    RefusedInvalid,
    RefusedNotHost,
    RefusedDisposed
}

public sealed record OcOfficerSlotSnapshot(
    int Handle,
    string OwnerKey,
    OcOfficerSlotState State,
    string CloneId,
    int Attempts,
    OcOfficerPosition Position,
    double? KnockedOutGameMinutes,
    bool HasLiveClone);

/// <summary>
/// The pure roster machine: slot ownership, the cap, unique IDs, retry
/// counting, and save/load transitions. It never touches the world; the
/// service pairs each transition with the matching world call.
/// </summary>
public sealed class OcOfficerRoster
{
    public const int MaxOfficers = 8;
    internal const int MaximumCapacity = 10;
    public const int MaxAttempts = 3;
    public const double KnockoutRespawnMinutes = 120d;
    public const double FailedRetryMinutes = 30d;
    public const long AttemptBackoffUpdates = 30;

    private readonly string _sessionId;
    private readonly OcOfficerSlot?[] _slots = new OcOfficerSlot?[MaximumCapacity];
    private int _capacity = MaxOfficers;
    internal int Capacity => _capacity;
    internal void SetPatrolCapacity(int capacity) => _capacity = Math.Clamp(capacity, MaxOfficers, MaximumCapacity);
    private int _nextId = 1;

    public OcOfficerRoster(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("A session id is required.", nameof(sessionId));
        _sessionId = sessionId;
    }

    public int Count
    {
        get
        {
            var count = 0;
            foreach (var slot in _slots)
                if (slot is not null)
                    count++;
            return count;
        }
    }

    public string MintCloneId() => $"oc_officer_{_sessionId}_{_nextId++}";

    public OcOfficerRequestStatus TryRequest(string? ownerKey, OcOfficerPosition position, long updateIndex, out int handle)
    {
        handle = -1;
        if (string.IsNullOrWhiteSpace(ownerKey) || !position.IsFinite)
            return OcOfficerRequestStatus.RefusedInvalid;
        if (Count >= _capacity) return OcOfficerRequestStatus.RefusedCap;
        var free = Array.IndexOf(_slots, null);
        if (free < 0)
            return OcOfficerRequestStatus.RefusedCap;
        _slots[free] = new OcOfficerSlot(ownerKey!, position, MintCloneId(), updateIndex - AttemptBackoffUpdates);
        handle = free;
        return OcOfficerRequestStatus.Accepted;
    }

    public bool Release(int handle, out OcOfficerCloneRef? liveClone)
    {
        liveClone = null;
        var slot = GetSlot(handle);
        if (slot is null)
            return false;
        liveClone = slot.LiveClone;
        _slots[handle] = null;
        return true;
    }

    public bool TryGetLiveClone(int handle, out OcOfficerCloneRef liveClone)
    {
        liveClone = default;
        var slot = GetSlot(handle);
        if (slot?.LiveClone is not { } live)
            return false;
        liveClone = live;
        return true;
    }

    public bool IsAttemptDue(int handle, long updateIndex)
    {
        var slot = GetSlot(handle);
        return slot is not null &&
            slot.State == OcOfficerSlotState.Requested &&
            updateIndex - slot.LastAttemptUpdateIndex >= AttemptBackoffUpdates;
    }

    public bool MarkSpawning(int handle, long updateIndex)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Requested)
            return false;
        slot.State = OcOfficerSlotState.Spawning;
        slot.LastAttemptUpdateIndex = updateIndex;
        return true;
    }

    public bool MarkActive(int handle, OcOfficerCloneRef liveClone)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Spawning)
            return false;
        slot.State = OcOfficerSlotState.Active;
        slot.LiveClone = liveClone;
        return true;
    }

    public bool MarkAttemptFailed(int handle, long updateIndex, double? gameMinutes = null)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Spawning)
            return false;
        slot.Attempts++;
        slot.LastAttemptUpdateIndex = updateIndex;
        slot.State = slot.Attempts >= MaxAttempts ? OcOfficerSlotState.Failed : OcOfficerSlotState.Requested;
        if (slot.State == OcOfficerSlotState.Failed)
            slot.FailedGameMinutes = gameMinutes;
        return true;
    }

    public bool IsRetryDue(int handle, double gameMinutes)
    {
        var slot = GetSlot(handle);
        return slot is not null &&
            slot.State == OcOfficerSlotState.Failed &&
            slot.FailedGameMinutes is { } failed &&
            gameMinutes >= failed + FailedRetryMinutes;
    }

    public bool MarkRetryFromFailed(int handle, string newCloneId, long updateIndex)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Failed || string.IsNullOrWhiteSpace(newCloneId))
            return false;
        slot.LiveClone = null;
        slot.CloneId = newCloneId;
        slot.Attempts = 0;
        slot.FailedGameMinutes = null;
        slot.State = OcOfficerSlotState.Requested;
        slot.LastAttemptUpdateIndex = updateIndex - AttemptBackoffUpdates;
        return true;
    }

    public bool MarkSpawnAborted(int handle, long updateIndex)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Spawning)
            return false;
        slot.State = OcOfficerSlotState.Requested;
        slot.LastAttemptUpdateIndex = updateIndex - AttemptBackoffUpdates;
        return true;
    }

    public bool MarkKnockedOut(int handle, double gameMinutes)
    {
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.Active)
            return false;
        slot.State = OcOfficerSlotState.KnockedOut;
        slot.KnockedOutGameMinutes = gameMinutes;
        return true;
    }

    public bool MarkRespawnRequested(int handle, string newCloneId, long updateIndex, out OcOfficerCloneRef? removedClone)
    {
        removedClone = null;
        var slot = GetSlot(handle);
        if (slot is null || slot.State != OcOfficerSlotState.KnockedOut || string.IsNullOrWhiteSpace(newCloneId))
            return false;
        removedClone = slot.LiveClone;
        slot.LiveClone = null;
        slot.CloneId = newCloneId;
        slot.Attempts = 0;
        slot.KnockedOutGameMinutes = null;
        slot.State = OcOfficerSlotState.Requested;
        slot.LastAttemptUpdateIndex = updateIndex - AttemptBackoffUpdates;
        return true;
    }

    /// <summary>
    /// Save start: every live clone is listed for despawning while its slot
    /// stays held and requeues. IDs survive the boundary so a stale native
    /// registration reads as the same ID twice instead of a silent orphan.
    /// </summary>
    public IReadOnlyList<OcOfficerCloneRef> SuspendForSave()
    {
        var live = new List<OcOfficerCloneRef>();
        foreach (var slot in _slots)
        {
            if (slot is null || slot.State == OcOfficerSlotState.Failed)
                continue;
            if (slot.LiveClone is { } clone)
                live.Add(clone);
            slot.LiveClone = null;
            slot.Attempts = 0;
            slot.KnockedOutGameMinutes = null;
            slot.State = OcOfficerSlotState.Requested;
            slot.LastAttemptUpdateIndex = -AttemptBackoffUpdates;
        }
        return live;
    }

    public IReadOnlyList<OcOfficerCloneRef> Clear()
    {
        var live = new List<OcOfficerCloneRef>();
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i]?.LiveClone is { } clone)
                live.Add(clone);
            _slots[i] = null;
        }
        return live;
    }

    public IReadOnlyList<OcOfficerSlotSnapshot> Snapshot()
    {
        var snapshots = new List<OcOfficerSlotSnapshot>();
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (slot is null)
                continue;
            snapshots.Add(new OcOfficerSlotSnapshot(
                i,
                slot.OwnerKey,
                slot.State,
                slot.CloneId,
                slot.Attempts,
                slot.Position,
                slot.KnockedOutGameMinutes,
                slot.LiveClone is not null));
        }
        return snapshots;
    }

    private OcOfficerSlot? GetSlot(int handle) =>
        handle >= 0 && handle < _slots.Length ? _slots[handle] : null;

    private sealed class OcOfficerSlot
    {
        public OcOfficerSlot(string ownerKey, OcOfficerPosition position, string cloneId, long lastAttemptUpdateIndex)
        {
            OwnerKey = ownerKey;
            Position = position;
            CloneId = cloneId;
            LastAttemptUpdateIndex = lastAttemptUpdateIndex;
        }

        public string OwnerKey { get; }
        public OcOfficerPosition Position { get; }
        public string CloneId { get; set; }
        public OcOfficerSlotState State { get; set; } = OcOfficerSlotState.Requested;
        public int Attempts { get; set; }
        public double? KnockedOutGameMinutes { get; set; }
        public double? FailedGameMinutes { get; set; }
        public long LastAttemptUpdateIndex { get; set; }
        public OcOfficerCloneRef? LiveClone { get; set; }
    }
}
