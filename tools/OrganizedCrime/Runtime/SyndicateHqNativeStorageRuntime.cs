using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public interface ISyndicateHqStorageRuntime : IDisposable
{
    SyndicateHqStorageResult CurrentResult { get; }
    SyndicateHqStorageResult BeginLoad(SyndicateHqStorageContext context);
    SyndicateHqStorageResult PrepareAtVanillaLoadBoundary();
    SyndicateHqStorageResult CompleteVanillaLoadBoundary();
    SyndicateHqStorageResult PlaceAtPocket(SyndicateHqVector3 pocketRoot);
    Release1HoldRoomSnapshot ReadHoldRoom();
    Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(string closetGuid, int slotIndex, out float balance);
    Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(string closetGuid, int slotIndex, bool locked);
    Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(string closetGuid, int slotIndex, float amount);
    void DisableInteractions();
    void MarkTeardown();
}

public sealed class SyndicateHqNativeStorageRuntime : ISyndicateHqStorageRuntime
{
    private sealed record RetainedStorage(
        SyndicateHqStorageDefinition Definition,
        object Entity,
        bool CreatedThisAttempt);

    private readonly ISyndicateHqNativeStorageBoundary _boundary;
    private readonly Action<string> _log;
    private readonly Action<string> _receiptLog;
    private readonly List<RetainedStorage> _retained = new();
    private SyndicateHqStorageContext? _boundContext;
    private object? _preparedGrid;
    private bool _admitted;
    private bool _creationAttempted;
    private bool _gridBoundaryHandled;
    private bool _completionBoundaryHandled;
    private bool _disposed;
    private bool _interactionsEnabled = true;
    private bool _tearingDown;

    public SyndicateHqNativeStorageRuntime(
        ISyndicateHqNativeStorageBoundary boundary,
        Action<string>? log = null,
        Action<string>? receiptLog = null)
    {
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        _log = log ?? (_ => { });
        _receiptLog = receiptLog ?? _log;
        CurrentResult = new(SyndicateHqStorageStatus.NotPrepared, "Native HQ storage has not been prepared.");
    }

    public SyndicateHqStorageResult CurrentResult { get; private set; }

    public SyndicateHqStorageResult BeginLoad(SyndicateHqStorageContext context)
    {
        if (_disposed) return Set(SyndicateHqStorageStatus.Disposed, "Native HQ storage was disposed.");

        var admission = SyndicateHqStorageAdmission.Evaluate(context);
        if (!admission.IsReady)
        {
            _admitted = false;
            DisableInteractions();
            return Set(admission.Status, admission.Reason);
        }

        if (_boundContext is not null &&
            (!PathsEqual(_boundContext.BoundSaveFolder, context.BoundSaveFolder) ||
             !string.Equals(_boundContext.CanonicalPlayerId, context.CanonicalPlayerId, StringComparison.Ordinal)))
        {
            _admitted = false;
            DisableInteractions();
            return Set(SyndicateHqStorageStatus.SaveBindingChanged, "The process was already bound to a different save folder.");
        }

        _boundContext ??= context;
        DisableInteractions();
        _retained.Clear();
        _admitted = true;
        _creationAttempted = false;
        _gridBoundaryHandled = false;
        _completionBoundaryHandled = false;
        _preparedGrid = null;
        return Set(SyndicateHqStorageStatus.NotPrepared, "Native HQ storage is admitted and awaiting the vanilla storage-load boundary.");
    }

    public SyndicateHqStorageResult PrepareAtVanillaLoadBoundary()
    {
        if (_disposed) return Set(SyndicateHqStorageStatus.Disposed, "Native HQ storage was disposed.");
        if (!_admitted || _boundContext is null) return CurrentResult;
        if (_gridBoundaryHandled) return CurrentResult;
        _gridBoundaryHandled = true;
        try
        {
            if (!_boundary.TryPrepareGrid(out _preparedGrid, out var gridReason))
                return Set(SyndicateHqStorageStatus.GridUnavailable, gridReason);
            return Set(SyndicateHqStorageStatus.NotPrepared, "The native HQ Grid is ready and awaiting vanilla closet restoration.");
        }
        catch (Exception ex)
        {
            return Set(SyndicateHqStorageStatus.GridUnavailable, $"Native Grid preparation threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    public SyndicateHqStorageResult CompleteVanillaLoadBoundary()
    {
        if (_disposed) return Set(SyndicateHqStorageStatus.Disposed, "Native HQ storage was disposed.");
        if (!_admitted || _boundContext is null || !_gridBoundaryHandled || _preparedGrid is null) return CurrentResult;
        if (_completionBoundaryHandled) return CurrentResult;
        _completionBoundaryHandled = true;
        if (_creationAttempted)
            return CurrentResult;

        _creationAttempted = true;
        try
        {
            // OC-70 final review fix (finding 5): evict the retained closet cache once for this
            // vanilla load boundary before resolving any definition, so a vanilla restore that
            // produced a second in-scene object is caught by FindRestoredCloset's own
            // matches.Length != 1 identity-conflict check instead of being hidden behind a stale
            // retained reference from a prior boundary.
            _boundary.EvictRetainedClosetsForLoad();
            foreach (var definition in SyndicateHqStorageContract.Definitions)
            {
                var createdThisAttempt = false;
                var lookup = _boundary.FindRestoredCloset(definition, out var entity, out var lookupReason);
                if (lookup == SyndicateHqNativeStorageLookupStatus.IdentityConflict)
                    return FailCreated(SyndicateHqStorageStatus.IdentityConflict, lookupReason);
                if (lookup == SyndicateHqNativeStorageLookupStatus.Missing)
                {
                    if (!_boundary.TryCreate(_preparedGrid, definition, out entity, out var createReason))
                        return FailCreated(SyndicateHqStorageStatus.SpawnUnavailable, createReason);
                    createdThisAttempt = true;
                }

                _retained.Add(new(definition, entity, createdThisAttempt));
                if (!_boundary.TryValidate(entity, definition, out var validationReason))
                    return FailCreated(SyndicateHqStorageStatus.ValidationFailed, validationReason);
            }

            _interactionsEnabled = true;
            return Set(SyndicateHqStorageStatus.Ready, "Nine complete vanilla 20-slot HQ closets were restored or created.");
        }
        catch (Exception ex)
        {
            return FailCreated(SyndicateHqStorageStatus.SpawnUnavailable, $"Native storage preparation threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    public SyndicateHqStorageResult PlaceAtPocket(SyndicateHqVector3 pocketRoot)
    {
        if (_disposed) return Set(SyndicateHqStorageStatus.Disposed, "Native HQ storage was disposed.");
        if (!pocketRoot.IsFinite || CurrentResult.Status != SyndicateHqStorageStatus.Ready || _retained.Count != SyndicateHqStorageContract.Definitions.Count)
        {
            DisableInteractions();
            return CurrentResult.Status == SyndicateHqStorageStatus.Ready
                ? Set(SyndicateHqStorageStatus.ValidationFailed, "Native HQ storage placement inputs were invalid.")
                : CurrentResult;
        }

        try
        {
            foreach (var retained in _retained)
            {
                var position = pocketRoot + retained.Definition.LocalPosition;
                if (!_boundary.TryPlace(
                        retained.Entity,
                        position,
                        retained.Definition.YawDegrees,
                        out var placementReason))
                {
                    DisableInteractions();
                    return Set(SyndicateHqStorageStatus.ValidationFailed, placementReason);
                }
            }

            foreach (var retained in _retained)
                _boundary.SetInteractionEnabled(retained.Entity, true);
            _interactionsEnabled = true;
            return Set(SyndicateHqStorageStatus.Ready, "Nine native HQ storage interactions were placed and enabled.");
        }
        catch (Exception ex)
        {
            DisableInteractions();
            return Set(SyndicateHqStorageStatus.ValidationFailed, $"Native storage placement threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads every retained contract closet's slots in ordinal GUID order. Ready only when the
    /// runtime itself is Ready and all nine definitions are retained; anything else returns a
    /// not-ready room, which the mission treats as "hold and change nothing". Never mutates.
    /// </summary>
    public Release1HoldRoomSnapshot ReadHoldRoom()
    {
        if (_disposed) return Release1HoldRoomSnapshot.Unavailable();
        if (CurrentResult.Status != SyndicateHqStorageStatus.Ready ||
            _retained.Count != SyndicateHqStorageContract.Definitions.Count)
            return Release1HoldRoomSnapshot.NotReady();

        var closets = new List<Release1HoldRoomClosetSnapshot>(_retained.Count);
        foreach (var retained in _retained.OrderBy(item => item.Definition.Guid.ToString("D"), StringComparer.Ordinal))
        {
            IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots;
            string reason;
            try
            {
                if (!_boundary.TryReadClosetSlots(retained.Definition, out slots, out reason))
                {
                    _log($"Native HQ storage hold-room read held: {reason}");
                    return Release1HoldRoomSnapshot.NotReady();
                }
            }
            catch (Exception ex)
            {
                _log($"Native HQ storage hold-room read threw {ex.GetType().Name}: {ex.Message}");
                return Release1HoldRoomSnapshot.Unavailable();
            }

            var closet = new Release1HoldRoomClosetSnapshot(
                retained.Definition.Guid.ToString("D"),
                slots.Count,
                slots);
            try { closet.Validate(); }
            catch (ArgumentException ex)
            {
                _log($"Native HQ storage hold-room read held: {ex.Message}");
                return Release1HoldRoomSnapshot.NotReady();
            }
            closets.Add(closet);
        }

        var room = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready, closets);
        try { room.Validate(); }
        catch (ArgumentException) { return Release1HoldRoomSnapshot.NotReady(); }
        return room;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(string closetGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        if (!TryResolveRetainedDefinition(closetGuid, out var definition)) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        try
        {
            var status = _boundary.TryReadClosetSlotCashBalance(definition, slotIndex, out var read, out var reason);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready)
            {
                _log($"Native HQ storage closet cash read held: {reason}");
                return status;
            }
            balance = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch (Exception ex)
        {
            _log($"Native HQ storage closet cash read threw {ex.GetType().Name}: {ex.Message}");
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(string closetGuid, int slotIndex, bool locked)
    {
        if (!TryResolveRetainedDefinition(closetGuid, out var definition)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try
        {
            var status = _boundary.TrySetClosetSlotLocked(definition, slotIndex, locked, out var reason);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded) _log($"Native HQ storage closet lock held: {reason}");
            return status;
        }
        catch (Exception ex)
        {
            _log($"Native HQ storage closet lock threw {ex.GetType().Name}: {ex.Message}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(string closetGuid, int slotIndex, float amount)
    {
        if (!TryResolveRetainedDefinition(closetGuid, out var definition)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try
        {
            var status = _boundary.TryChangeClosetSlotCashBalance(definition, slotIndex, amount, out var reason);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded) _log($"Native HQ storage closet cash change held: {reason}");
            return status;
        }
        catch (Exception ex)
        {
            _log($"Native HQ storage closet cash change threw {ex.GetType().Name}: {ex.Message}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    /// <summary>The one place a closet GUID string becomes a contract definition. Requires the runtime to be Ready with every definition retained, exactly as ReadHoldRoom does, so no cash call can run against a half prepared room.</summary>
    private bool TryResolveRetainedDefinition(string closetGuid, out SyndicateHqStorageDefinition definition)
    {
        definition = null!;
        if (_disposed || CurrentResult.Status != SyndicateHqStorageStatus.Ready ||
            _retained.Count != SyndicateHqStorageContract.Definitions.Count)
            return false;
        var match = _retained.FirstOrDefault(item =>
            string.Equals(item.Definition.Guid.ToString("D"), closetGuid, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;
        definition = match.Definition;
        return true;
    }

    public void DisableInteractions()
    {
        if (_tearingDown)
        {
            if (_interactionsEnabled)
            {
                _receiptLog($"Native HQ storage interactions left to the game during teardown; count={_retained.Count}");
                _interactionsEnabled = false;
            }
            return;
        }

        if (!_interactionsEnabled) return;

        foreach (var retained in _retained)
        {
            try { _boundary.SetInteractionEnabled(retained.Entity, false); }
            catch (Exception ex) { _log($"Native HQ storage interaction disable failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        _interactionsEnabled = false;
    }

    public void MarkTeardown() => _tearingDown = true;

    public void Dispose()
    {
        if (_disposed) return;
        try { _boundary.ReleaseOwnedClosetSlotLocks(); } catch { }
        DisableInteractions();
        _disposed = true;
        _admitted = false;
        _retained.Clear();
        _preparedGrid = null;
        Set(SyndicateHqStorageStatus.Disposed, "Native HQ storage was disposed.");
    }

    private SyndicateHqStorageResult FailCreated(SyndicateHqStorageStatus status, string reason)
    {
        var cleanupFailures = new List<string>();
        foreach (var retained in _retained.Where(item => item.CreatedThisAttempt))
        {
            try
            {
                if (!_boundary.TryCleanup(retained.Entity, out var cleanupReason))
                    cleanupFailures.Add(cleanupReason);
            }
            catch (Exception ex)
            {
                cleanupFailures.Add($"{ex.GetType().Name}: {ex.Message}");
            }
        }
        _retained.Clear();
        if (cleanupFailures.Count > 0)
            reason += " Cleanup remained incomplete: " + string.Join(" | ", cleanupFailures);
        return Set(status, reason);
    }

    private SyndicateHqStorageResult Set(SyndicateHqStorageStatus status, string reason)
    {
        CurrentResult = new(status, reason);
        if (status != SyndicateHqStorageStatus.Ready && status != SyndicateHqStorageStatus.NotPrepared)
        {
            var target = status == SyndicateHqStorageStatus.Disposed ? _receiptLog : _log;
            target($"Native HQ storage: {status}: {reason}");
        }
        return CurrentResult;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
