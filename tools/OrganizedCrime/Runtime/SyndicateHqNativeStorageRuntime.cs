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
    /// <summary>
    /// OC-136: the family locker's product decrement. A default
    /// implementation so the existing fakes, which never take this path,
    /// need no change.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotQuantity(string closetGuid, int slotIndex, int amount) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;
    /// <summary>
    /// OC-136: the family locker's UI name. A default implementation so
    /// the existing fakes, which never take this path, need no change.
    /// </summary>
    Release1SmallCourtesyWorldMutationStatus TrySetClosetStorageName(string closetGuid, string name) =>
        Release1SmallCourtesyWorldMutationStatus.Unavailable;
    void DisableInteractions();
    bool TrySetSaveInteractions(bool enabled) => false;
    void MarkTeardown();
}

public sealed class SyndicateHqNativeStorageRuntime : ISyndicateHqStorageRuntime, Chapter3.IChapter3RaidHqSlotReader
{
    bool Chapter3.IChapter3RaidHqSlotReader.TryReadRaidSlot(string guid, int index, out Chapter3.Chapter3RaidSlotRead read)
    {
        read = null!;
        if (!TryResolveRetainedDefinition(guid, out _) || _boundary is not Chapter3.IChapter3RaidHqSlotReader reader) return false;
        try { return reader.TryReadRaidSlot(guid, index, out read); } catch { return false; }
    }
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
    private bool _saveInteractionFailureLogged;
    private bool _saveInteractionAttempted;
    internal Action<string, Exception>? SaveRetentionFailureReporter { get; set; }

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
        _saveInteractionFailureLogged = false;
        _saveInteractionAttempted = false;

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
            _saveInteractionAttempted = false;
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

    public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotQuantity(string closetGuid, int slotIndex, int amount)
    {
        if (!TryResolveRetainedDefinition(closetGuid, out var definition)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try
        {
            var status = _boundary.TryChangeClosetSlotQuantity(definition, slotIndex, amount, out var reason);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded) _log($"Native HQ storage closet change held: {reason}");
            return status;
        }
        catch (Exception ex)
        {
            _log($"Native HQ storage closet change threw {ex.GetType().Name}: {ex.Message}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetClosetStorageName(string closetGuid, string name)
    {
        if (!TryResolveRetainedDefinition(closetGuid, out var definition)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        try
        {
            var status = _boundary.TrySetClosetStorageName(definition, name, out var reason);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded) _log($"Native HQ storage closet rename held: {reason}");
            return status;
        }
        catch (Exception ex)
        {
            _log($"Native HQ storage closet rename threw {ex.GetType().Name}: {ex.Message}");
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public bool TrySetSaveInteractions(bool enabled)
    {
        if (_disposed || _tearingDown || CurrentResult.Status != SyndicateHqStorageStatus.Ready ||
            _retained.Count != SyndicateHqStorageContract.Definitions.Count) return false;
        _saveInteractionAttempted = true;
        var step = "closet validation";
        try
        {
            foreach (var retained in _retained)
            {
                if (_boundary.TryValidate(retained.Entity, retained.Definition, out _)) continue;
                if (_boundary is SyndicateHqNativeStorageBoundary { LastValidationException: { } failure })
                    LogSaveInteractionFailure($"closet validation {retained.Definition.Guid}", failure);
                // Resume must still visit every healthy closet after a dead one.
                if (enabled) { RestoreSaveInteractions(); return false; }
                return false;
            }
            // Same visibility/culling path as teardown. Keep the native GUID, Property/Grid
            // registration, slots, and item instances intact for the vanilla save writer.
            foreach (var retained in _retained)
            {
                step = $"closet {(enabled ? "resume" : "suspension")} {retained.Definition.Guid}";
                _boundary.SetInteractionEnabled(retained.Entity, enabled);
            }
            // A visibility callback can invalidate a wrapper after the admission
            // pass. Never claim retention when that native graph stopped being ready.
            foreach (var retained in _retained)
            {
                step = $"closet visibility verification {retained.Definition.Guid}";
                if (_boundary.TryValidate(retained.Entity, retained.Definition, out _)) continue;
                if (_boundary is SyndicateHqNativeStorageBoundary { LastValidationException: { } failure })
                    LogSaveInteractionFailure(step, failure);
                RestoreSaveInteractions();
                return false;
            }
            _interactionsEnabled = enabled;
            _saveInteractionAttempted = !enabled;
            return true;
        }
        catch (Exception ex)
        {
            LogSaveInteractionFailure(step, ex);
            // Undo a partial suspension, or finish a partial resume. Visit the rest
            // even when one native object throws; save fallback still tears down.
            RestoreSaveInteractions();
            return false;
        }
    }

    private void RestoreSaveInteractions()
    {
        foreach (var retained in _retained)
        {
            try
            {
                if (_boundary.TryValidate(retained.Entity, retained.Definition, out _))
                    _boundary.SetInteractionEnabled(retained.Entity, true);
                else if (_boundary is SyndicateHqNativeStorageBoundary { LastValidationException: { } failure })
                    LogSaveInteractionFailure($"closet restore validation {retained.Definition.Guid}", failure);
            }
            catch (Exception ex) { LogSaveInteractionFailure($"closet restore {retained.Definition.Guid}", ex); }
        }
        _interactionsEnabled = true;
    }

    private void LogSaveInteractionFailure(string step, Exception ex)
    {
        // Native composition shares the service's one-per-load gate. Standalone
        // storage consumers retain the same bounded ordinary warning behavior.
        if (SaveRetentionFailureReporter is not null) { SaveRetentionFailureReporter(step, ex); return; }
        if (_saveInteractionFailureLogged) return;
        _saveInteractionFailureLogged = true;
        _log($"Syndicate HQ save retention failed closed: step={step}; {ex.GetType().FullName}: {ex.Message}; managed stack: {ex.StackTrace ?? Environment.StackTrace}");
    }

    public void DisableInteractions()
    {
        if (_tearingDown)
        {
            if (_interactionsEnabled)
            {
                OrganizedCrimeLog.Diagnostic($"Native HQ storage interactions left to the game during teardown; count={_retained.Count}", _receiptLog);
                _interactionsEnabled = false;
            }
            return;
        }

        if (!_interactionsEnabled) return;

        foreach (var retained in _retained)
        {
            try { _boundary.SetInteractionEnabled(retained.Entity, false); }
            catch (Exception ex)
            {
                if (_saveInteractionAttempted) LogSaveInteractionFailure($"closet disable {retained.Definition.Guid}", ex);
                else _log($"Native HQ storage interaction disable failed: {ex.GetType().Name}: {ex.Message}");
            }
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
