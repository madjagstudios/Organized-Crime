using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public sealed class Release1StoryRuntimeService : IDisposable
{
    private const string SmallCourtesyTermsVersion = "small-courtesy-v1";
    private const string WrongAddressTermsVersion = "wrong-address-v1";
    private const string RoomWithNoNameTermsVersion = "room-with-no-name-v1";
    private const string ShortNoticeTermsVersion = "short-notice-v1";
    private const string KeepTheLightsOffTermsVersion = "keep-the-lights-off-v1";
    private const string TheEnvelopeTermsVersion = "the-envelope-v1";
    private readonly object _gate = new();
    private readonly IRelease1StoryHostContext _context;
    private IRelease1StoryRepository _repository;
    private readonly Func<string, IRelease1StoryRepository>? _repositoryFactory;
    private Release1StoryState? _state;
    private Release1StoryHostContextSnapshot _activeContext;
    private Release1StoryState? _saveSnapshot;
    private Release1StoryRuntimePhase _phase = Release1StoryRuntimePhase.Detached;
    private Release1StoryRuntimePhase _phaseBeforeSave;
    private bool _disposed;
    private Release1StoryRuntimeRejectReason _lastLifecycleRejectReason;
    /// <summary>
    /// OC-10: the exact bytes a failed load could not parse, captured when a SidecarLoadFailed
    /// quarantine occurs. OnSaveStart rewrites them verbatim while quarantined so the vanilla
    /// save routine does not remove a sidecar the mod never touched, and never serializes a
    /// fresh empty story over them.
    /// </summary>
    private string? _quarantinedRawContents;
    public Release1StoryRuntimeService(IRelease1StoryHostContext context, IRelease1StoryRepository repository) { _context = context ?? throw new ArgumentNullException(nameof(context)); _repository = repository ?? throw new ArgumentNullException(nameof(repository)); }
    public Release1StoryRuntimeService(IRelease1StoryHostContext context, Func<string, IRelease1StoryRepository> repositoryFactory) { _context = context ?? throw new ArgumentNullException(nameof(context)); _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory)); _repository = null!; }
    public Release1StoryRuntimeService(IRelease1StoryHostContext context, IRelease1StoryRepositoryFactory repositoryFactory) : this(context, folder => (repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory))).Create(folder)) { }
    public Release1StoryRuntimePhase Phase { get { lock (_gate) return _phase; } }
    public Release1StoryState? State { get { lock (_gate) return _state; } }
    public long LastPersistedRevision { get; private set; } = -1;
    // The story revision at which the most recent native effect was marked Applied. In-memory
    // only (resets to -1 on load, which is correct: a loaded Applied effect saved together with its
    // native mutation, so it is already covered). Used by PersistState to defer only while an applied
    // effect's world mutation is not yet covered by a native save, instead of blocking on any applied
    // effect — a covered effect must not stall unrelated durable commands (e.g. the next mission's
    // acceptance).
    private long _lastAppliedRevision = -1;
    public Release1StoryRuntimeRejectReason LastLifecycleRejectReason => _lastLifecycleRejectReason;

    /// <summary>
    /// The timing seam the sidecar read and write receipts report through. Assigned by the mod
    /// shell after construction; the default disabled seam keeps every existing caller unchanged.
    /// </summary>
    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    public Release1StoryRuntimeLifecycleResult OnPreLoad()
    {
        lock (_gate)
        {
            if (_disposed) return Lifecycle(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
            if (_phase == Release1StoryRuntimePhase.AwaitingLoad) return Lifecycle(Release1StoryRuntimeRejectReason.None, "PreLoad was already handled.");
            _state = null; _saveSnapshot = null; _quarantinedRawContents = null; _phase = Release1StoryRuntimePhase.AwaitingLoad; LastPersistedRevision = -1;
            return Lifecycle(Release1StoryRuntimeRejectReason.None, "Story state was cleared for load.");
        }
    }
    public Release1StoryRuntimeLifecycleResult OnLoadComplete()
    {
        lock (_gate)
        {
            if (_disposed) return Lifecycle(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
            if (_phase is Release1StoryRuntimePhase.Active or Release1StoryRuntimePhase.Saving) return Lifecycle(Release1StoryRuntimeRejectReason.None, "LoadComplete was already handled.");
            Release1StoryHostContextReadStatus status;
            Release1StoryHostContextSnapshot snapshot;
            try { status = _context.TryRead(out snapshot); }
            catch (Exception ex) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.ContextFaulted, ex.Message); }
            if (status == Release1StoryHostContextReadStatus.Pending) return Lifecycle(Release1StoryRuntimeRejectReason.ContextPending, "Host context is pending.");
            if (status == Release1StoryHostContextReadStatus.NotAuthoritative) return Lifecycle(Release1StoryRuntimeRejectReason.NotAuthoritative, "Host authority is still settling during load.");
            if (status != Release1StoryHostContextReadStatus.Ready) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Map(status), "Host context was not safe for story activation."); }
            if (_repositoryFactory is not null)
            {
                try { _repository = _repositoryFactory(snapshot.ActiveSaveFolder) ?? throw new InvalidOperationException("Repository factory returned null."); }
                catch (Exception ex) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
            }
            if (_repository is not IRelease1StorySaveFolderBoundRepository bound)
            {
                _phase = Release1StoryRuntimePhase.Quarantined;
                return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryUnbound, "Repository was not bound to a validated save folder.");
            }
            try
            {
                if (!SameSaveFolder(bound.BoundSaveFolder, snapshot.ActiveSaveFolder))
                {
                    _phase = Release1StoryRuntimePhase.Quarantined;
                    return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryPathMismatch, "Repository save folder did not match host context.");
                }
            }
            catch (Exception ex) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
            Release1StoryStoreLoadResult load;
            try { load = _repository.Load(); }
            catch (Exception ex) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
            if (!load.Succeeded) { _phase = Release1StoryRuntimePhase.Quarantined; _quarantinedRawContents = load.RawContents; return Lifecycle(Release1StoryRuntimeRejectReason.SidecarLoadFailed, load.Message); }
            if (load.Envelope?.Story is not null && load.Envelope.Story.PlayerId != snapshot.PlayerId) { _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.IdentityMismatch, "Story sidecar identity did not match host context."); }
            _activeContext = snapshot; _state = load.Envelope?.Story; LastPersistedRevision = _state?.Revision ?? -1; _phase = Release1StoryRuntimePhase.Active;
            RecoverAttemptingPhonePresentations();
            var reconciliation = ReconcileSmallCourtesyOnLoad();
            if (reconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(reconciliation.RejectReason, reconciliation.Message);
            var wrongAddressReconciliation = ReconcileWrongAddressOnLoad();
            if (wrongAddressReconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(wrongAddressReconciliation.RejectReason, wrongAddressReconciliation.Message);
            var roomWithNoNameReconciliation = ReconcileRoomWithNoNameOnLoad();
            if (roomWithNoNameReconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(roomWithNoNameReconciliation.RejectReason, roomWithNoNameReconciliation.Message);
            var shortNoticeReconciliation = ReconcileShortNoticeOnLoad();
            if (shortNoticeReconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(shortNoticeReconciliation.RejectReason, shortNoticeReconciliation.Message);
            var keepTheLightsOffReconciliation = ReconcileKeepTheLightsOffOnLoad();
            if (keepTheLightsOffReconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(keepTheLightsOffReconciliation.RejectReason, keepTheLightsOffReconciliation.Message);

            var theEnvelopeReconciliation = ReconcileTheEnvelopeOnLoad();
            if (theEnvelopeReconciliation is { Status: Release1StoryCommandStatus.Rejected })
                return Lifecycle(theEnvelopeReconciliation.RejectReason, theEnvelopeReconciliation.Message);
            return Lifecycle(Release1StoryRuntimeRejectReason.None, "Story runtime is active.");
        }
    }
    public Release1StoryRuntimeLifecycleResult OnSaveStart()
    {
        lock (_gate)
        {
            if (_disposed) return Lifecycle(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
            if (_phase == Release1StoryRuntimePhase.Saving) return Lifecycle(Release1StoryRuntimeRejectReason.None, "SaveStart was already handled.");
            if (_phase == Release1StoryRuntimePhase.Quarantined) return PreserveQuarantinedSidecar();
            if (_phase != Release1StoryRuntimePhase.Active) return Lifecycle(Release1StoryRuntimeRejectReason.Inactive, "Only an active story can save.");
            _phaseBeforeSave = _phase; _saveSnapshot = _state; _phase = Release1StoryRuntimePhase.Saving;
            return Lifecycle(Release1StoryRuntimeRejectReason.None, "Story save snapshot captured.");
        }
    }
    public Release1StoryRuntimeLifecycleResult OnSaveComplete()
    {
        lock (_gate)
        {
            if (_disposed) return Lifecycle(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
            if (_phase != Release1StoryRuntimePhase.Saving) return Lifecycle(Release1StoryRuntimeRejectReason.None, "SaveComplete was a lifecycle no-op.");
            Release1StoryStoreUpdateResult result;
            try { result = Timing.Measure("story-sidecar-write/save-complete", () => _repository.Update(_saveSnapshot)); }
            catch (Exception ex) { _saveSnapshot = null; _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
            if (!result.Succeeded) { _saveSnapshot = null; _phase = Release1StoryRuntimePhase.Quarantined; return Lifecycle(Release1StoryRuntimeRejectReason.SidecarSaveFailed, result.Message); }
            LastPersistedRevision = _saveSnapshot?.Revision ?? -1; _saveSnapshot = null; _phase = _phaseBeforeSave;
            return Lifecycle(Release1StoryRuntimeRejectReason.None, "Story save snapshot persisted.");
        }
    }
    public Release1StoryRuntimeCommandResult TryExecute(Release1StoryCommand command)
    {
        lock (_gate)
        {
            return TryExecuteCore(command, persistImmediately: false);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteDurably(Release1StoryCommand command)
    {
        lock (_gate)
        {
            return TryExecuteCore(command, persistImmediately: true);
        }
    }

    private Release1StoryRuntimeCommandResult TryExecuteCore(Release1StoryCommand command, bool persistImmediately)
    {
        var gate = CheckStoryCommandGate(command);
        if (gate is not null) return gate;
        var transition = Release1StoryTransitions.Apply(_state, command);
        if (!transition.Accepted) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
        if (transition.State is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted story transition did not return state.");
        if (transition.Idempotent)
        {
            if (!persistImmediately || LastPersistedRevision >= transition.State.Revision)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, transition.Message);
            return PersistState(transition.State, "Idempotent story transition was persisted.");
        }
        if (persistImmediately)
            return PersistState(transition.State, "Story transition was persisted.");
        _state = transition.State;
        return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, transition.Message);
    }

    public Release1StoryRuntimeCommandResult TryExecuteSmallCourtesyAcceptanceDurably(
        Release1StoryCommand command,
        Release1SmallCourtesyAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Small Courtesy assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1SmallCourtesyAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1SmallCourtesyAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1SmallCourtesyAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.SmallCourtesy ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Small Courtesy assignment did not match its acceptance command.");
            if (assignment.Mode == Release1SmallCourtesyAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, SmallCourtesyTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Small Courtesy primary terms version was not canonical.");

            var existing = _state.SmallCourtesyAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Small Courtesy attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted Small Courtesy correlation had no atomic assignment.");
                return ReconcileAcceptedSmallCourtesy(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    SmallCourtesyAssignments = transition.State.SmallCourtesyAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "Small Courtesy acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedSmallCourtesy(assignment);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteWrongAddressAcceptanceDurably(
        Release1StoryCommand command,
        Release1WrongAddressAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Wrong Address assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1WrongAddressAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.WrongAddress ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Wrong Address assignment did not match its acceptance command.");
            if (assignment.Mode == Release1WrongAddressAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, WrongAddressTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Wrong Address primary terms version was not canonical.");

            var existing = _state.WrongAddressAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Wrong Address attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted Wrong Address correlation had no atomic assignment.");
                return ReconcileAcceptedWrongAddress(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    WrongAddressAssignments = transition.State.WrongAddressAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "Wrong Address acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedWrongAddress(assignment);
        }
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedWrongAddress(Release1WrongAddressAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
        if (assignment.Mode != Release1WrongAddressAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Wrong Address stage was already active.");
        var receipt = $"wrong-address-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.WrongAddress,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileWrongAddressOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.WrongAddressAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1WrongAddressAssignmentMode.Primary);
            if (assignment is null)
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound,
                    $"Wrong Address mission was Accepted on load but attempt {mission.Attempt} had no assignment on file.");
            return ReconcileAcceptedWrongAddress(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"wrong-address-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.WrongAddress,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    public Release1StoryRuntimeCommandResult TryExecuteRoomWithNoNameAcceptanceDurably(
        Release1StoryCommand command,
        Release1RoomWithNoNameAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1RoomWithNoNameAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.RoomWithNoName ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name assignment did not match its acceptance command.");
            if (assignment.Mode == Release1RoomWithNoNameAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, RoomWithNoNameTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name primary terms version was not canonical.");

            var existing = _state.RoomWithNoNameAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted Room With No Name correlation had no atomic assignment.");
                return ReconcileAcceptedRoomWithNoName(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    RoomWithNoNameAssignments = transition.State.RoomWithNoNameAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "Room With No Name acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedRoomWithNoName(assignment);
        }
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedRoomWithNoName(Release1RoomWithNoNameAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
        if (assignment.Mode != Release1RoomWithNoNameAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Room With No Name stage was already active.");
        var receipt = $"room-with-no-name-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.RoomWithNoName,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.RoomWithNoName, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileRoomWithNoNameOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.RoomWithNoNameAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1RoomWithNoNameAssignmentMode.Primary);
            if (assignment is null)
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound,
                    $"Room With No Name mission was Accepted on load but attempt {mission.Attempt} had no assignment on file.");
            return ReconcileAcceptedRoomWithNoName(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"room-with-no-name-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.RoomWithNoName,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.RoomWithNoName, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    /// <summary>
    /// Writes the whole next Room With No Name progress row in memory only. It takes the whole record
    /// rather than one flag because the hold has to clear MissingSincePassGameMinutes as well as set
    /// flags, and a per-flag setter could not express a clear. Flags are monotonic within an attempt:
    /// a regression from true to false is refused rather than silently ignored.
    /// </summary>
    public Release1StoryRuntimeCommandResult TrySetRoomWithNoNameProgress(Release1RoomWithNoNameProgress next)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (next is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name progress was null.");
            try { next.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            if (!_state.RoomWithNoNameAssignments.Any(assignment => assignment.Attempt == next.Attempt))
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Room With No Name progress had no accepted assignment for its attempt.");

            var existing = _state.RoomWithNoNameProgress.SingleOrDefault(progress => progress.Attempt == next.Attempt);
            if (existing is not null &&
                ((existing.Staged && !next.Staged) || (existing.Custody && !next.Custody) ||
                 (existing.Stowed && !next.Stowed) || (existing.HoldSatisfied && !next.HoldSatisfied)))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Room With No Name progress flags cannot regress within an attempt.");
            if (existing is not null && existing == next)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Room With No Name progress was already recorded.");

            var updated = _state with
            {
                RoomWithNoNameProgress = existing is null
                    ? _state.RoomWithNoNameProgress.Append(next).ToArray()
                    : _state.RoomWithNoNameProgress.Select(progress => progress.Attempt == next.Attempt ? next : progress).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Room With No Name progress was recorded in memory.");
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteShortNoticeAcceptanceDurably(
        Release1StoryCommand command,
        Release1ShortNoticeAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1ShortNoticeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.ShortNotice ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice assignment did not match its acceptance command.");
            if (assignment.Mode == Release1ShortNoticeAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, ShortNoticeTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice primary terms version was not canonical.");

            var existing = _state.ShortNoticeAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted Short Notice correlation had no atomic assignment.");
                return ReconcileAcceptedShortNotice(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    ShortNoticeAssignments = transition.State.ShortNoticeAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "Short Notice acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedShortNotice(assignment);
        }
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedShortNotice(Release1ShortNoticeAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        if (assignment.Mode != Release1ShortNoticeAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Short Notice stage was already active.");
        var receipt = $"short-notice-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.ShortNotice,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileShortNoticeOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.ShortNoticeAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1ShortNoticeAssignmentMode.Primary);
            if (assignment is null)
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound,
                    $"Short Notice mission was Accepted on load but attempt {mission.Attempt} had no assignment on file.");
            return ReconcileAcceptedShortNotice(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"short-notice-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.ShortNotice,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    /// <summary>
    /// Writes the whole next Short Notice progress row in memory only. Unlike the Room With No Name
    /// setter, only SpreadNoticed is monotonic: ObservedQuantity and LastShortfallNoticed may fall
    /// again within an attempt, because the player can take units back out of the drop before the
    /// manifest is complete and the mission must observe that honestly.
    /// </summary>
    public Release1StoryRuntimeCommandResult TrySetShortNoticeProgress(Release1ShortNoticeProgress next)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (next is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice progress was null.");
            try { next.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            if (!_state.ShortNoticeAssignments.Any(assignment => assignment.Attempt == next.Attempt))
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Short Notice progress had no accepted assignment for its attempt.");

            var existing = _state.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == next.Attempt);
            if (existing is not null && existing.SpreadNoticed && !next.SpreadNoticed)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Short Notice spread notice cannot be withdrawn within an attempt.");
            if (existing is not null && existing == next)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Short Notice progress was already recorded.");

            var updated = _state with
            {
                ShortNoticeProgress = existing is null
                    ? _state.ShortNoticeProgress.Append(next).ToArray()
                    : _state.ShortNoticeProgress.Select(progress => progress.Attempt == next.Attempt ? next : progress).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Short Notice progress was recorded in memory.");
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteKeepTheLightsOffAcceptanceDurably(
        Release1StoryCommand command,
        Release1KeepTheLightsOffAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1KeepTheLightsOffAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.KeepTheLightsOff ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off assignment did not match its acceptance command.");
            if (assignment.Mode == Release1KeepTheLightsOffAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, KeepTheLightsOffTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off primary terms version was not canonical.");

            var existing = _state.KeepTheLightsOffAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted Keep the Lights Off correlation had no atomic assignment.");
                return ReconcileAcceptedKeepTheLightsOff(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    KeepTheLightsOffAssignments = transition.State.KeepTheLightsOffAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "Keep the Lights Off acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedKeepTheLightsOff(assignment);
        }
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedKeepTheLightsOff(Release1KeepTheLightsOffAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        if (assignment.Mode != Release1KeepTheLightsOffAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Keep the Lights Off stage was already active.");
        var receipt = $"keep-the-lights-off-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.KeepTheLightsOff,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.KeepTheLightsOff, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileKeepTheLightsOffOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.KeepTheLightsOffAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1KeepTheLightsOffAssignmentMode.Primary);
            if (assignment is null)
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound,
                    $"Keep the Lights Off mission was Accepted on load but attempt {mission.Attempt} had no assignment on file.");
            return ReconcileAcceptedKeepTheLightsOff(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"keep-the-lights-off-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.KeepTheLightsOff,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.KeepTheLightsOff, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    /// <summary>
    /// Writes the whole next Keep the Lights Off progress row in memory only. Unlike the Short Notice
    /// setter, only ClearConfirmedAtGameMinutes is monotonic: BreachSincePassGameMinutes may toggle
    /// freely within an attempt, because the player legitimately clears a breach before the grace
    /// elapses and the mission must observe that honestly.
    /// </summary>
    public Release1StoryRuntimeCommandResult TrySetKeepTheLightsOffProgress(Release1KeepTheLightsOffProgress next)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (next is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off progress was null.");
            try { next.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            if (!_state.KeepTheLightsOffAssignments.Any(assignment => assignment.Attempt == next.Attempt))
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Keep the Lights Off progress had no accepted assignment for its attempt.");

            var existing = _state.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == next.Attempt);
            if (existing is not null && existing.ClearConfirmedAtGameMinutes is not null && next.ClearConfirmedAtGameMinutes is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off clear confirmation cannot be withdrawn within an attempt.");
            if (existing is not null && existing == next)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Keep the Lights Off progress was already recorded.");

            var updated = _state with
            {
                KeepTheLightsOffProgress = existing is null
                    ? _state.KeepTheLightsOffProgress.Append(next).ToArray()
                    : _state.KeepTheLightsOffProgress.Select(progress => progress.Attempt == next.Attempt ? next : progress).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Keep the Lights Off progress was recorded in memory.");
        }
    }

    /// <summary>
    /// Re freezes a Keep the Lights Off assignment for an attempt that already has an acceptance on
    /// record but lost its assignment to the v9 to v10 migration. Deliberately transition free: the
    /// attempt, its deadline, its Standing and its mission state are all already correct, and the only
    /// thing missing is the assignment row the migration could not translate. Refused unless the mission
    /// is in an active stage, the attempt and mode match that stage, the acceptance correlation is
    /// already on the mission record, and no assignment exists for that attempt. Never writes progress.
    /// </summary>
    public Release1StoryRuntimeCommandResult TryRestoreKeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
            var expectedMode = mission.State switch
            {
                Release1MissionState.Accepted or Release1MissionState.Active => Release1KeepTheLightsOffAssignmentMode.Primary,
                Release1MissionState.MakeGoodActive => Release1KeepTheLightsOffAssignmentMode.MakeGood,
                Release1MissionState.RecoveryActive => Release1KeepTheLightsOffAssignmentMode.Recovery,
                _ => (Release1KeepTheLightsOffAssignmentMode?)null
            };
            if (expectedMode is null || assignment.Mode != expectedMode.Value || assignment.Attempt != mission.Attempt)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off restore did not match the accepted stage.");
            if (!mission.AcceptedLogicalCorrelations.Contains(assignment.AuthorizationCorrelationId, StringComparer.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Keep the Lights Off restore was not authorized by this attempt.");
            if (_state.KeepTheLightsOffAssignments.Any(candidate => candidate.Attempt == assignment.Attempt))
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Keep the Lights Off already has an assignment for this attempt.");

            var updated = _state with
            {
                KeepTheLightsOffAssignments = _state.KeepTheLightsOffAssignments.Append(assignment).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Keep the Lights Off assignment was restored.");
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteTheEnvelopeAcceptanceDurably(
        Release1StoryCommand command,
        Release1TheEnvelopeAssignment assignment)
    {
        lock (_gate)
        {
            var gate = CheckStoryCommandGate(command);
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (assignment is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope assignment was null.");
            try { assignment.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var expectedTransition = assignment.Mode switch
            {
                Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                Release1TheEnvelopeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
                _ => (Release1TransitionKind?)null
            };
            if (expectedTransition is null || command.TransitionKind != expectedTransition ||
                command.MissionKey != Release1MissionCatalog.TheEnvelope ||
                assignment.MissionKey != command.MissionKey || assignment.Attempt != command.Attempt ||
                assignment.AuthorizationCorrelationId != command.CorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope assignment did not match its acceptance command.");
            if (assignment.Mode == Release1TheEnvelopeAssignmentMode.Primary &&
                !string.Equals(command.TermsVersion, TheEnvelopeTermsVersion, StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope primary terms version was not canonical.");

            var existing = _state.TheEnvelopeAssignments.SingleOrDefault(candidate => candidate.Attempt == assignment.Attempt);
            if (existing is not null && existing != assignment)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope attempt already had a different assignment.");

            var transition = Release1StoryTransitions.Apply(_state, command);
            if (!transition.Accepted || transition.State is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
            if (transition.Idempotent)
            {
                if (existing is null)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted The Envelope correlation had no atomic assignment.");
                return ReconcileAcceptedTheEnvelope(existing);
            }

            Release1StoryState candidate;
            try
            {
                candidate = transition.State with
                {
                    TheEnvelopeAssignments = transition.State.TheEnvelopeAssignments.Append(assignment).ToArray()
                };
                candidate.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
            }

            var persisted = PersistState(candidate, "The Envelope acceptance and assignment were persisted atomically.");
            if (!persisted.Accepted) return persisted;
            return ReconcileAcceptedTheEnvelope(assignment);
        }
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedTheEnvelope(Release1TheEnvelopeAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];
        if (assignment.Mode != Release1TheEnvelopeAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "The Envelope stage was already active.");
        var receipt = $"the-envelope-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileTheEnvelopeOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.TheEnvelopeAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1TheEnvelopeAssignmentMode.Primary);
            if (assignment is null)
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound,
                    $"The Envelope mission was Accepted on load but attempt {mission.Attempt} had no assignment on file.");
            return ReconcileAcceptedTheEnvelope(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"the-envelope-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    public Release1StoryRuntimeCommandResult TrySetTheEnvelopeProgress(Release1TheEnvelopeProgress next)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (next is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope progress was null.");
            try { next.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            if (!_state.TheEnvelopeAssignments.Any(assignment => assignment.Attempt == next.Attempt))
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "The Envelope progress had no accepted assignment for its attempt.");

            var existing = _state.TheEnvelopeProgress.SingleOrDefault(progress => progress.Attempt == next.Attempt);
            if (existing is not null && existing.SpreadNoticed && !next.SpreadNoticed)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "The Envelope spread notice cannot be withdrawn within an attempt.");
            if (existing is not null && existing == next)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "The Envelope progress was already recorded.");

            var updated = _state with
            {
                TheEnvelopeProgress = existing is null
                    ? _state.TheEnvelopeProgress.Append(next).ToArray()
                    : _state.TheEnvelopeProgress.Select(progress => progress.Attempt == next.Attempt ? next : progress).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "The Envelope progress was recorded in memory.");
        }
    }

    public Release1StoryRuntimeCommandResult TrySetWrongAddressStaged(int attempt) =>
        SetWrongAddressProgress(attempt, staged: true, custody: false);

    public Release1StoryRuntimeCommandResult TrySetWrongAddressCustody(int attempt) =>
        SetWrongAddressProgress(attempt, staged: true, custody: true);

    private Release1StoryRuntimeCommandResult SetWrongAddressProgress(int attempt, bool staged, bool custody)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (attempt < 1) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Wrong Address progress attempt was not canonical.");
            if (!_state.WrongAddressAssignments.Any(assignment => assignment.Attempt == attempt))
                return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Wrong Address progress had no accepted assignment for its attempt.");

            var existing = _state.WrongAddressProgress.SingleOrDefault(progress => progress.Attempt == attempt);
            var next = new Release1WrongAddressProgress(
                Release1MissionCatalog.WrongAddress,
                attempt,
                staged || existing?.Staged == true,
                custody || existing?.Custody == true);
            if (existing is not null && existing == next)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Wrong Address progress was already recorded.");

            var updated = _state with
            {
                WrongAddressProgress = existing is null
                    ? _state.WrongAddressProgress.Append(next).ToArray()
                    : _state.WrongAddressProgress.Select(progress => progress.Attempt == attempt ? next : progress).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Wrong Address progress was recorded in memory.");
        }
    }

    private Release1StoryRuntimeCommandResult? CheckStoryCommandGate(Release1StoryCommand? command)
    {
        if (_disposed) return Reject(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
        if (_phase == Release1StoryRuntimePhase.Quarantined) return Reject(Release1StoryRuntimeRejectReason.Quarantined, "Story runtime is quarantined.");
        if (_phase == Release1StoryRuntimePhase.Saving) return new(Release1StoryCommandStatus.DeferredSaving, Release1StoryRuntimeRejectReason.None, _state, "Story command was deferred during saving.");
        if (_phase != Release1StoryRuntimePhase.Active) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story runtime is not active.");
        Release1StoryHostContextReadStatus status;
        Release1StoryHostContextSnapshot current;
        try { status = _context.TryRead(out current); }
        catch (Exception exception) { return Reject(Release1StoryRuntimeRejectReason.ContextFaulted, exception.Message); }
        if (status != Release1StoryHostContextReadStatus.Ready) return Reject(Map(status), "Host context was not ready.");
        if (command is null || current.SessionEpoch != _activeContext.SessionEpoch || current.LoadEpoch != _activeContext.LoadEpoch || command.SessionEpoch != _activeContext.SessionEpoch || command.LoadEpoch != _activeContext.LoadEpoch)
            return Reject(Release1StoryRuntimeRejectReason.WrongEpoch, "Story command epoch was stale.");
        if (current.PlayerId != _activeContext.PlayerId || command.PlayerId != _activeContext.PlayerId)
            return Reject(Release1StoryRuntimeRejectReason.IdentityMismatch, "Story command identity did not match host context.");
        return null;
    }

    private Release1StoryRuntimeCommandResult ReconcileAcceptedSmallCourtesy(Release1SmallCourtesyAssignment assignment)
    {
        if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        if (assignment.Mode != Release1SmallCourtesyAssignmentMode.Primary || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Small Courtesy stage was already active.");
        var receipt = $"small-courtesy-activate-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.SmallCourtesy, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    private Release1StoryRuntimeCommandResult? ReconcileSmallCourtesyOnLoad()
    {
        if (_state is null) return null;
        var mission = _state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = _state.SmallCourtesyAssignments.SingleOrDefault(candidate =>
                candidate.Attempt == mission.Attempt && candidate.Mode == Release1SmallCourtesyAssignmentMode.Primary);
            return assignment is null ? null : ReconcileAcceptedSmallCourtesy(assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"small-courtesy-reoffer-v1-a{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _activeContext.SessionEpoch,
            _activeContext.LoadEpoch,
            _activeContext.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_activeContext.PlayerId, Release1MissionCatalog.SmallCourtesy, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return TryExecuteCore(command, persistImmediately: true);
    }

    public Release1StoryRuntimeCommandResult TryAuthorizePhonePresentation(
        string missionKey,
        int attempt,
        string correlationId,
        string role,
        string? requiredPriorCorrelationId)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (!Release1MissionCatalog.IsMissionKey(missionKey) || attempt < 1)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation mission or attempt was not canonical.");
            if (!Release1LogicalCorrelation.TryParse(correlationId, out var correlation) ||
                correlation.PlayerId != _state.PlayerId || correlation.MissionKey != missionKey || correlation.Attempt != attempt)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation correlation did not match the story identity, mission, or attempt.");
            if (role is not ("Nell" or "Arthur"))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation role was invalid.");
            var current = _state.PhonePresentationAttempts.FirstOrDefault(candidate => candidate.CorrelationId == correlationId);
            if (current is not null)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Phone presentation authorization already exists.");
            var mission = _state.Missions.SingleOrDefault(candidate => candidate.MissionKey == missionKey && candidate.Attempt == attempt);
            if (mission is null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation attempt did not match the story state.");
            var priorIsDeliveredNellCall = !string.IsNullOrWhiteSpace(requiredPriorCorrelationId) &&
                _state.PhonePresentationAttempts.Any(candidate =>
                    candidate.CorrelationId == requiredPriorCorrelationId && candidate.Role == "Nell" &&
                    candidate.State is not Release1PhonePresentationAttemptState.Ambiguous);
            var priorIsReceiptedNellMessage = !string.IsNullOrWhiteSpace(requiredPriorCorrelationId) &&
                Release1LogicalCorrelation.TryParse(requiredPriorCorrelationId, out var prior) &&
                prior.PlayerId == _state.PlayerId && prior.MissionKey == missionKey && prior.Attempt == attempt &&
                prior.ReceiptId.StartsWith(Release1PhoneCallCorrelation.NellPresentationPrefix, StringComparison.Ordinal) &&
                _state.PresentationReceipts.Any(receipt => receipt.CorrelationId == requiredPriorCorrelationId);
            if (role == "Arthur" && !priorIsDeliveredNellCall && !priorIsReceiptedNellMessage)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Arthur requires a delivered Nell call or a receipted Nell message on this mission and attempt.");
            if (role == "Nell" && requiredPriorCorrelationId is not null)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Nell cannot have a prior presentation.");
            var presentation = new Release1PhonePresentationAttempt(correlationId, missionKey, attempt, role, requiredPriorCorrelationId, Release1PhonePresentationAttemptState.Pending, _state.Revision + 1);
            try { presentation.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            var updated = _state with
            {
                PhonePresentationAttempts = _state.PhonePresentationAttempts.Append(presentation).ToArray(),
                Revision = _state.Revision + 1
            };
            return PersistState(updated, "Phone presentation authorization was persisted.");
        }
    }

    public Release1StoryRuntimeCommandResult TryTransitionPhonePresentation(string correlationId, Release1PhonePresentationAttemptState nextState)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            var current = _state.PhonePresentationAttempts.SingleOrDefault(candidate => candidate.CorrelationId == correlationId);
            if (current is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Phone presentation correlation was not authorized.");
            if (!Enum.IsDefined(nextState)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation state was invalid.");
            if (current.State == nextState) return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Phone presentation state was already recorded.");
            if (!IsAllowedPhonePresentationTransition(current.State, nextState))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Phone presentation state transition was not allowed.");
            var updatedAttempt = current with { State = nextState, Revision = _state.Revision + 1 };
            var updated = _state with
            {
                PhonePresentationAttempts = _state.PhonePresentationAttempts.Select(candidate => candidate.CorrelationId == correlationId ? updatedAttempt : candidate).ToArray(),
                Revision = _state.Revision + 1
            };
            return PersistState(updated, "Phone presentation state was persisted.");
        }
    }

    public Release1StoryRuntimeCommandResult TryRecordPresentationReceipt(string correlationId)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (!Release1LogicalCorrelation.TryParse(correlationId, out var correlation) || correlation.PlayerId != _state.PlayerId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Presentation receipt correlation did not match the story identity.");
            var existing = _state.PresentationReceipts.FirstOrDefault(candidate => candidate.CorrelationId == correlationId);
            if (existing is not null)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Presentation receipt already exists.");
            var receipt = new Release1PresentationReceipt(correlationId, _state.Revision + 1);
            receipt.Validate();
            var updated = _state with
            {
                PresentationReceipts = _state.PresentationReceipts.Append(receipt).ToArray(),
                Revision = _state.Revision + 1
            };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Presentation receipt was recorded in memory.");
        }
    }

    public Release1PhonePresentationAttempt? GetPhonePresentationAttempt(string correlationId)
    {
        lock (_gate) return _state?.PhonePresentationAttempts.SingleOrDefault(candidate => candidate.CorrelationId == correlationId);
    }

    public IReadOnlyList<Release1PhonePresentationAttempt> GetPhonePresentationAttempts()
    {
        lock (_gate) return _state?.PhonePresentationAttempts.ToArray() ?? Array.Empty<Release1PhonePresentationAttempt>();
    }

    /// <summary>
    /// OC-73. Writes Chief Campbell's record in memory, validated, revision advanced, no story
    /// transition: he is not a mission, so there is no mission state, Standing or unlock to move, and
    /// the pure ledger that produced the record has already validated it. Refuses a record whose
    /// revision does not advance by exactly one, so a stale pass cannot overwrite a newer one.
    /// </summary>
    public Release1StoryRuntimeCommandResult TrySetChiefRecord(Release1ChiefRecord next)
    {
        lock (_gate)
        {
            var gate = CheckPresentationMutationGate();
            if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (next is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief record was null.");
            try { next.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

            var existing = _state.ChiefRecord;
            if (existing is not null && existing.ValueEquals(next))
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _state, "Chief record was already recorded.");
            var expectedRevision = existing is null ? 0L : existing.Revision + 1;
            if (next.Revision != expectedRevision)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief record revision did not advance by exactly one.");
            // OC-73 review fix. The ladder legitimately restarts at round 1 the moment a settled
            // payoff is followed by another arrest (Release1ChiefLedger.Observe's Adopted-or-Settled
            // branch), so this guard against a stale or regressive write must not also catch that one
            // intended transition: Settled reopening into DemandOpen at round 1.
            var isLadderRestart = existing is not null && existing.State == Release1ChiefState.Settled &&
                next.State == Release1ChiefState.DemandOpen && next.DemandRound == 1;
            if (existing is not null && next.DemandRound < existing.DemandRound && !isLadderRestart)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief demand round cannot fall.");

            var updated = _state with { ChiefRecord = next, Revision = _state.Revision + 1 };
            try { updated.Validate(); }
            catch (ArgumentException exception) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
            _state = updated;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, "Chief record was recorded in memory.");
        }
    }

    public bool TryGetActiveContext(out Release1StoryHostContextSnapshot snapshot, out Release1StoryRuntimeRejectReason rejectReason)
    {
        lock (_gate)
        {
            snapshot = default;
            rejectReason = Release1StoryRuntimeRejectReason.None;
            if (_disposed) { rejectReason = Release1StoryRuntimeRejectReason.Disposed; return false; }
            if (_phase == Release1StoryRuntimePhase.Quarantined) { rejectReason = Release1StoryRuntimeRejectReason.Quarantined; return false; }
            if (_phase != Release1StoryRuntimePhase.Active) { rejectReason = Release1StoryRuntimeRejectReason.Inactive; return false; }
            var authority = CheckCurrentAuthority();
            if (authority is not null) { rejectReason = authority.RejectReason; return false; }
            snapshot = _activeContext;
            return true;
        }
    }

    private Release1StoryRuntimeCommandResult? CheckPresentationMutationGate()
    {
        if (_disposed) return Reject(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
        if (_phase == Release1StoryRuntimePhase.Quarantined) return Reject(Release1StoryRuntimeRejectReason.Quarantined, "Story runtime is quarantined.");
        if (_phase == Release1StoryRuntimePhase.Saving) return new(Release1StoryCommandStatus.DeferredSaving, Release1StoryRuntimeRejectReason.None, _state, "Phone presentation mutation was deferred during saving.");
        if (_phase != Release1StoryRuntimePhase.Active) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story runtime is not active.");
        return CheckCurrentAuthority();
    }

    private Release1StoryRuntimeCommandResult PersistState(Release1StoryState updated, string successMessage)
    {
        // Defer only while an applied effect's world mutation is not yet covered by a native
        // save (_lastAppliedRevision ahead of LastPersistedRevision), not on the mere presence of any
        // applied effect. A covered applied effect (already persisted together with its native
        // mutation) must not stall an unrelated durable command such as the next mission's acceptance.
        if (_state is not null && _state.Revision > LastPersistedRevision &&
            _lastAppliedRevision > LastPersistedRevision)
            return new(
                Release1StoryCommandStatus.DeferredSaving,
                Release1StoryRuntimeRejectReason.None,
                _state,
                "Immediate persistence was deferred until the native save covers the applied effect.");
        try
        {
            var result = Timing.Measure("story-sidecar-write/transition", () => _repository.Update(updated));
            if (!result.Succeeded) return Reject(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.Message);
            _state = updated;
            LastPersistedRevision = updated.Revision;
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, successMessage);
        }
        catch (Exception exception)
        {
            return Reject(Release1StoryRuntimeRejectReason.RepositoryFaulted, exception.Message);
        }
    }

    private void RecoverAttemptingPhonePresentations()
    {
        if (_state is null) return;
        var recovered = _state.PhonePresentationAttempts
            .Select(attempt => attempt.State == Release1PhonePresentationAttemptState.Attempting
                ? attempt with { State = Release1PhonePresentationAttemptState.Ambiguous, Revision = _state.Revision + 1 }
                : attempt)
            .ToArray();
        if (recovered.SequenceEqual(_state.PhonePresentationAttempts)) return;
        var updated = _state with { PhonePresentationAttempts = recovered, Revision = _state.Revision + 1 };
        _state = updated;
        try
        {
            var result = _repository.Update(updated);
            if (result.Succeeded) LastPersistedRevision = updated.Revision;
        }
        catch
        {
            // The in-memory state is already fail-closed; the next lifecycle save can persist it.
        }
    }

    private static bool IsAllowedPhonePresentationTransition(Release1PhonePresentationAttemptState current, Release1PhonePresentationAttemptState next) =>
        (current, next) switch
        {
            (Release1PhonePresentationAttemptState.Pending, Release1PhonePresentationAttemptState.Attempting) => true,
            (Release1PhonePresentationAttemptState.Attempting, Release1PhonePresentationAttemptState.Delivered) => true,
            (Release1PhonePresentationAttemptState.Attempting, Release1PhonePresentationAttemptState.Ambiguous) => true,
            (Release1PhonePresentationAttemptState.Delivered, Release1PhonePresentationAttemptState.Completed) => true,
            _ => false
        };

    public Release1StoryRuntimeCommandResult TryPrepareNativeEffect(Release1NativeEffectJournalEntry effect)
    {
        lock (_gate)
        {
            var gate = CheckEffectMutationGate(); if (gate is not null) return gate;
            if (effect is null) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect request was null.");
            try { effect.Validate(); } catch (ArgumentException ex) { return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, ex.Message); }
            if (effect.Phase != Release1NativeEffectPhase.Prepared) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only Prepared effects may be issued for native execution.");
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal))
                return PrepareChiefEffect(effect);
            if (!Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) || authorization.PlayerId != _state.PlayerId || authorization.MissionKey != effect.MissionKey || authorization.Attempt != effect.Attempt || authorization.TransitionKind is not (Release1TransitionKind.MissionActivated or Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect did not reference an eligible accepted story transition.");
            var mission = _state.Missions.FirstOrDefault(m => m.MissionKey == effect.MissionKey && m.Attempt == effect.Attempt);
            if (mission is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect mission/attempt was not active in story state.");
            if (!mission.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal) ||
                (authorization.TransitionKind == Release1TransitionKind.MissionActivated && mission.State != Release1MissionState.Active) ||
                (authorization.TransitionKind == Release1TransitionKind.MakeGoodAccepted && mission.State != Release1MissionState.MakeGoodActive) ||
                (authorization.TransitionKind == Release1TransitionKind.RecoveryAccepted && mission.State != Release1MissionState.RecoveryActive) ||
                (effect.AuthorizedMissionRevision >= 0 && effect.AuthorizedMissionRevision != mission.Revision) ||
                string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization did not match the mission state or effect type.");
            var existing = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effect.EffectId);
            if (existing is null && effect.PreparedStoryRevision != _state.Revision + 1)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect revision did not match the next story revision.");
            if (existing is not null && !IsCurrentEffectAuthorization(existing)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
            var journal = Release1NativeEffectJournal.Apply(_state.NativeEffects, new(Release1NativeEffectCommandKind.Prepare, effect.EffectId, effect.MissionKey, effect.Attempt, effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity, StoryCorrelationId: effect.AuthorizedStoryCorrelationId, AuthorizedMissionRevision: mission.Revision + 1), _state.Revision + 1);
            if (!journal.Accepted) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
            if (!journal.Idempotent)
            {
                var missions = _state.Missions.Select(m => m.MissionKey == mission.MissionKey ? m with { NativeEffectIds = m.NativeEffectIds.Append(effect.EffectId).ToArray(), Revision = m.Revision + 1 } : m).ToArray();
                _state = _state with { NativeEffects = journal.Effects, Missions = missions, Revision = _state.Revision + 1 };
            }
            return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, journal.Message);
        }
    }

    // OC-73 spec decisions 4 and 6. The Chief's scope key is deliberately absent from
    // Release1MissionCatalog.All, so the mission lookup in TryPrepareNativeEffect would find nothing
    // and refuse every Chief effect. This branch is the whole authorization path for that scope key
    // and never touches Missions.
    //
    // The DemandOpen to Paying transition and the effect's prepare are folded into one atomic write
    // here (rather than a separate TrySetChiefRecord call ahead of this one): Release1ChiefRecord's
    // own Validate forbids a Paying record with an empty NativeEffectIds, and Release1StoryState's
    // reverse check forbids a ChiefRecord that names an effect id absent from NativeEffects, so no
    // intermediate state between DemandOpen and a Paying record with its effect already prepared can
    // ever be persisted. Refused here, the record is left exactly as it was (still DemandOpen or
    // still Paying on a retried prepare), so nothing is ever left half done.
    private Release1StoryRuntimeCommandResult PrepareChiefEffect(Release1NativeEffectJournalEntry effect)
    {
        var record = _state!.ChiefRecord;
        if (record is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Chief record was absent.");
        if (effect.Attempt != record.DemandRound || record.State is not (Release1ChiefState.DemandOpen or Release1ChiefState.Paying))
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect did not match a payable round.");
        if (!Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) ||
            authorization.PlayerId != _state.PlayerId || authorization.MissionKey != effect.MissionKey ||
            authorization.Attempt != effect.Attempt ||
            authorization.TransitionKind != Release1TransitionKind.ChiefPaymentAccepted ||
            !string.Equals(effect.EffectKind, Release1ChiefEffect.Kind, StringComparison.Ordinal) ||
            (effect.AuthorizedMissionRevision >= 0 && effect.AuthorizedMissionRevision != record.Revision))
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect authorization did not match his record.");
        if (record.State == Release1ChiefState.Paying &&
            !record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal))
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect authorization did not match his record.");
        var existing = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effect.EffectId);
        if (existing is null && effect.PreparedStoryRevision != _state.Revision + 1)
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect revision did not match the next story revision.");
        if (existing is not null && !IsCurrentEffectAuthorization(existing))
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded.");
        var journal = Release1NativeEffectJournal.Apply(
            _state.NativeEffects,
            new(Release1NativeEffectCommandKind.Prepare, effect.EffectId, effect.MissionKey, effect.Attempt,
                effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity,
                StoryCorrelationId: effect.AuthorizedStoryCorrelationId, AuthorizedMissionRevision: record.Revision + 1),
            _state.Revision + 1);
        if (!journal.Accepted) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent)
        {
            var nextRecord = record.State == Release1ChiefState.Paying
                ? record with { Revision = record.Revision + 1 }
                : record with
                {
                    State = Release1ChiefState.Paying,
                    LastShortfallNoticed = null,
                    AcceptedLogicalCorrelations = record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal)
                        ? record.AcceptedLogicalCorrelations
                        : record.AcceptedLogicalCorrelations.Append(effect.AuthorizedStoryCorrelationId!).ToArray(),
                    NativeEffectIds = record.NativeEffectIds.Contains(effect.EffectId, StringComparer.Ordinal)
                        ? record.NativeEffectIds
                        : record.NativeEffectIds.Append(effect.EffectId).ToArray(),
                    Revision = record.Revision + 1
                };
            _state = _state with
            {
                NativeEffects = journal.Effects,
                ChiefRecord = nextRecord,
                Revision = _state.Revision + 1
            };
        }
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted,
            Release1StoryRuntimeRejectReason.None, _state, journal.Message);
    }

    public Release1StoryRuntimeCommandResult TryMarkNativeEffectApplied(
        string effectId,
        string nativeReceiptId,
        Release1NativeEffectPersistenceMode mode = Release1NativeEffectPersistenceMode.SaveGated)
    {
        lock (_gate)
        {
            var gate = CheckEffectMutationGate(); if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            if (!Enum.IsDefined(mode)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Native effect persistence mode was not defined.");
            var existing = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
            if (existing is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
            if (!IsCurrentEffectAuthorization(existing)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
            if (mode == Release1NativeEffectPersistenceMode.RevertTolerant &&
                !Release1MissionCatalog.AllowsRevertTolerantEffects(existing.MissionKey))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only revert-tolerant missions may use the revert-tolerant mode.");
            if (mode == Release1NativeEffectPersistenceMode.SaveGated &&
                existing.Phase == Release1NativeEffectPhase.Prepared && existing.PreparedStoryRevision > LastPersistedRevision)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect was not durably persisted before native application.");
            var journal = Release1NativeEffectJournal.Apply(_state.NativeEffects, new(Release1NativeEffectCommandKind.MarkApplied, effectId, existing.MissionKey, existing.Attempt, existing.EffectKind, existing.SourceIdentity, existing.DestinationIdentity, existing.AmountOrCargoIdentity, nativeReceiptId), _state.Revision + 1);
            if (!journal.Accepted) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
            if (!journal.Idempotent)
            {
                _state = _state with { NativeEffects = journal.Effects, Revision = _state.Revision + 1 };
                _lastAppliedRevision = _state.Revision; // This applied mutation is not covered until the next native save
            }
            return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, journal.Message);
        }
    }

    // capturedPersistedRevision lets a caller that is committing more than one native effect in the
    // same post-save pass (Wrong Address's consumption and reward effects) gate the Applied check
    // below on the story revision it captured once, at the start of that pass, instead of the live
    // _state.Revision, which advances by one with each commit the pass itself performs. Left null,
    // behaviour is unchanged: the live revision is used, exactly as Small Courtesy relies on today.
    public Release1StoryRuntimeCommandResult TryCommitNativeEffect(string effectId, string storyCorrelationId, long? capturedPersistedRevision = null)
    {
        lock (_gate)
        {
            var gate = CheckEffectMutationGate(); if (gate is not null) return gate;
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            var effect = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
            if (effect is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
            if (!IsCurrentEffectAuthorization(effect)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
            if (capturedPersistedRevision is not null && !Release1MissionCatalog.AllowsRevertTolerantEffects(effect.MissionKey))
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only revert-tolerant missions may commit against a captured revision.");
            var revisionForAppliedGate = capturedPersistedRevision ?? _state.Revision;
            if (effect.Phase == Release1NativeEffectPhase.Applied && revisionForAppliedGate > LastPersistedRevision)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Applied effect was not durably persisted with its native mutation before commit.");
            if (effect.PreparedStoryRevision > LastPersistedRevision || effect.AuthorizedStoryCorrelationId is null || effect.AuthorizedStoryCorrelationId != storyCorrelationId)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect commit did not reference its durable authorization correlation.");
            if (!Release1LogicalCorrelation.TryParse(storyCorrelationId, out var correlation) || correlation.PlayerId != _state.PlayerId || correlation.MissionKey != effect.MissionKey || correlation.Attempt != effect.Attempt)
                return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect commit correlation did not match the effect.");
            var journal = Release1NativeEffectJournal.Apply(_state.NativeEffects, new(Release1NativeEffectCommandKind.Commit, effectId, effect.MissionKey, effect.Attempt, effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity, effect.NativeReceiptId, storyCorrelationId), _state.Revision + 1);
            if (!journal.Accepted) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
            if (!journal.Idempotent) _state = _state with { NativeEffects = journal.Effects, Revision = _state.Revision + 1 };
            return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _state, journal.Message);
        }
    }

    public Release1StoryRuntimeCommandResult TryRecordNativeEffectIssuance(string effectId, Release1NativeEffectIssuanceOutcome outcome, string? nativeReceiptId = null)
    {
        lock (_gate)
        {
            var gate = CheckEffectMutationGate(); if (gate is not null) return gate;
            if (outcome == Release1NativeEffectIssuanceOutcome.Applied) return TryMarkNativeEffectApplied(effectId, nativeReceiptId ?? string.Empty);
            if (_state is null) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
            var existing = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
            if (existing is null) return Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
            if (outcome == Release1NativeEffectIssuanceOutcome.Ambiguous)
            {
                if (!IsCurrentEffectAuthorization(existing)) return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
                if (existing.ExecutionBlocked)
                    return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.EffectAmbiguous, _state, "Ambiguous issuance was already recorded.");
                if (existing.Phase != Release1NativeEffectPhase.Prepared)
                    return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only a prepared effect may be marked ambiguous.");
                var effects = _state.NativeEffects.Select(e => e.EffectId == effectId ? e with { ExecutionBlocked = true } : e).ToArray();
                _state = _state with { NativeEffects = effects, Revision = _state.Revision + 1 };
                return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.EffectAmbiguous, _state, "Ambiguous issuance is blocked pending authoritative reconciliation.");
            }
            return Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect issuance failed; no native mutation was authorized.");
        }
    }
    public bool TryGetExecutablePreparedEffect(string effectId, out Release1NativeEffectJournalEntry effect)
    {
        lock (_gate)
        {
            effect = null!;
            if (_phase is not (Release1StoryRuntimePhase.Active or Release1StoryRuntimePhase.Saving) || _state is null || CheckCurrentAuthority() is not null) return false;
            var candidate = _state.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
            if (candidate is null || !IsCurrentEffectAuthorization(candidate) || candidate.Phase != Release1NativeEffectPhase.Prepared || candidate.ExecutionBlocked || candidate.PreparedStoryRevision > LastPersistedRevision) return false;
            effect = candidate; return true;
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _state = null; _saveSnapshot = null; _phase = Release1StoryRuntimePhase.Disposed; } }
    private Release1StoryRuntimeCommandResult? CheckEffectMutationGate()
    {
        if (_disposed) return Reject(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed.");
        if (_phase == Release1StoryRuntimePhase.Quarantined) return Reject(Release1StoryRuntimeRejectReason.Quarantined, "Story runtime is quarantined.");
        if (_phase is not (Release1StoryRuntimePhase.Active or Release1StoryRuntimePhase.Saving)) return Reject(Release1StoryRuntimeRejectReason.Inactive, "Story runtime is not active.");
        var authority = CheckCurrentAuthority(); if (authority is not null) return authority;
        if (_phase == Release1StoryRuntimePhase.Saving) return new(Release1StoryCommandStatus.DeferredSaving, Release1StoryRuntimeRejectReason.None, _state, "Story effect command was deferred during saving.");
        return null;
    }
    private Release1StoryRuntimeCommandResult? CheckCurrentAuthority()
    {
        Release1StoryHostContextReadStatus status;
        Release1StoryHostContextSnapshot current;
        try { status = _context.TryRead(out current); }
        catch (Exception ex) { return Reject(Release1StoryRuntimeRejectReason.ContextFaulted, ex.Message); }
        if (status != Release1StoryHostContextReadStatus.Ready) return Reject(Map(status), "Host context was not ready.");
        if (current.SessionEpoch != _activeContext.SessionEpoch || current.LoadEpoch != _activeContext.LoadEpoch) return Reject(Release1StoryRuntimeRejectReason.WrongEpoch, "Host context epoch was stale.");
        if (current.PlayerId != _activeContext.PlayerId || string.IsNullOrWhiteSpace(current.PlayerId)) return Reject(Release1StoryRuntimeRejectReason.IdentityMismatch, "Host context identity did not match.");
        try
        {
            if (!SameSaveFolder(current.ActiveSaveFolder, _activeContext.ActiveSaveFolder) || _repository is not IRelease1StorySaveFolderBoundRepository bound || !SameSaveFolder(bound.BoundSaveFolder, current.ActiveSaveFolder))
                return Reject(Release1StoryRuntimeRejectReason.RepositoryPathMismatch, "Host and repository save folders did not match.");
        }
        catch (Exception ex) { return Reject(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
        return null;
    }
    private bool IsCurrentEffectAuthorization(Release1NativeEffectJournalEntry effect)
    {
        if (_state is null || effect.AuthorizedStoryCorrelationId is null || !Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization)) return false;
        if (string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal))
        {
            var record = _state.ChiefRecord;
            if (record is null) return false;

            // OC-73 settled drain fix. An Applied Chief cash effect already passed this exact check
            // once, under the strict rule below, while it was still Prepared: TryMarkNativeEffectApplied
            // calls this method before the journal entry's own phase flips to Applied, so nothing here
            // is granting a first authorization, only recognizing one already given. That is what makes
            // it safe to stop re-deriving it from the live record: Release1ChiefLedger.Observe's
            // Adopted-or-Settled ArrestObserved branch resets DemandRound to 1 and clears both
            // AcceptedLogicalCorrelations and NativeEffectIds the moment a new arrest lands, which the
            // settled drain fix now allows to happen before this same effect's post save commit runs,
            // so none of round, the correlation list, or Paying-or-Settled still describe it by then.
            // Applied is the narrow gate: a still-Prepared effect (RunPay's own debit and confirm path,
            // untouched by this fix) falls through to the unwidened rule that follows.
            if (effect.Phase == Release1NativeEffectPhase.Applied && !effect.ExecutionBlocked)
            {
                return authorization.PlayerId == _state.PlayerId &&
                    authorization.MissionKey == Release1MissionCatalog.ChiefCampbell &&
                    authorization.TransitionKind == Release1TransitionKind.ChiefPaymentAccepted;
            }

            if (effect.Attempt != record.DemandRound) return false;
            // The same completedConsumptionSuccessor allowance the mission branch below relies on:
            // Release1ChiefLedger.Settle bumps the record's Revision by exactly one on top of the
            // Revision the effect was authorized against, the moment the wipe settles the record on
            // the pass after Applied. The effect still has to commit after the next save, so its
            // authorization must survive that one-step revision advance.
            var completedPaymentSuccessor =
                record.State == Release1ChiefState.Settled &&
                record.Revision > 0 &&
                effect.AuthorizedMissionRevision == record.Revision - 1;
            if (effect.AuthorizedMissionRevision != record.Revision && !completedPaymentSuccessor) return false;
            return record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal) &&
                authorization.PlayerId == _state.PlayerId &&
                authorization.MissionKey == Release1MissionCatalog.ChiefCampbell &&
                authorization.Attempt == record.DemandRound &&
                authorization.TransitionKind == Release1TransitionKind.ChiefPaymentAccepted &&
                record.State is Release1ChiefState.Paying or Release1ChiefState.Settled;
        }
        var mission = _state.Missions.SingleOrDefault(m => m.MissionKey == effect.MissionKey && m.Attempt == effect.Attempt);
        if (mission is null || !mission.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal)) return false;
        // A consumption effect (cargo or cash) whose own mission has already completed on the
        // strength of that same effect: The Envelope's CashTransfer completes and commits after
        // Satisfied exactly the way Wrong Address, Room With No Name, and Short Notice's
        // CargoTransfer do, since none of them prepare a reward effect that could otherwise carry
        // the post-completion authorization forward.
        var completedConsumptionSuccessor =
            effect.EffectKind is "CargoTransfer" or "CashTransfer" &&
            effect.Phase is Release1NativeEffectPhase.Applied or Release1NativeEffectPhase.Committed &&
            mission.State == Release1MissionState.Satisfied &&
            mission.Revision > 0 &&
            effect.AuthorizedMissionRevision == mission.Revision - 1 &&
            authorization.TransitionKind is Release1TransitionKind.MissionActivated or Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted;
        if (mission.Revision != effect.AuthorizedMissionRevision && !completedConsumptionSuccessor) return false;
        return authorization.PlayerId == _state.PlayerId && authorization.MissionKey == mission.MissionKey && authorization.Attempt == mission.Attempt && authorization.TransitionKind switch
        {
            Release1TransitionKind.MissionAccepted => mission.State == Release1MissionState.Accepted,
            Release1TransitionKind.MissionActivated => mission.State == Release1MissionState.Active || completedConsumptionSuccessor,
            Release1TransitionKind.MissionCompleted => mission.State == Release1MissionState.Satisfied,
            Release1TransitionKind.MakeGoodAccepted => mission.State == Release1MissionState.MakeGoodActive || completedConsumptionSuccessor,
            Release1TransitionKind.RecoveryAccepted => mission.State == Release1MissionState.RecoveryActive || completedConsumptionSuccessor,
            _ => false
        };
    }
    private static bool SameSaveFolder(string left, string right)
    {
        static string Normalize(string value)
        {
            var full = Path.GetFullPath(value.Trim());
            var root = Path.GetPathRoot(full);
            return root is not null && full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>
    /// OC-10: called from OnSaveStart while quarantined. Never persists _state (it may be null,
    /// and writing it would replace a quarantined sidecar with a fresh empty story); rewrites
    /// only the exact bytes the failed load captured, so the game's native save routine sees a
    /// file the mod just touched and does not remove it.
    /// </summary>
    private Release1StoryRuntimeLifecycleResult PreserveQuarantinedSidecar()
    {
        if (_quarantinedRawContents is null || _repository is not IRelease1StoryRawContentPreservingRepository preserver)
            return Lifecycle(Release1StoryRuntimeRejectReason.Quarantined, "Story runtime is quarantined; no sidecar bytes were available to preserve.");
        Release1StoryStoreUpdateResult result;
        try { result = preserver.PreserveRawContents(_quarantinedRawContents); }
        catch (Exception ex) { return Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, ex.Message); }
        if (!result.Succeeded) return Lifecycle(Release1StoryRuntimeRejectReason.SidecarSaveFailed, result.Message);
        return Lifecycle(Release1StoryRuntimeRejectReason.Quarantined, "Story runtime is quarantined; quarantined sidecar bytes were preserved.");
    }
    private Release1StoryRuntimeLifecycleResult Lifecycle(Release1StoryRuntimeRejectReason reason, string message) { _lastLifecycleRejectReason = reason; return new(_phase, reason, message); }
    private Release1StoryRuntimeCommandResult Reject(Release1StoryRuntimeRejectReason reason, string message) => new(Release1StoryCommandStatus.Rejected, reason, _state, message);
    private static Release1StoryRuntimeRejectReason Map(Release1StoryHostContextReadStatus status) => status switch { Release1StoryHostContextReadStatus.Pending => Release1StoryRuntimeRejectReason.ContextPending, Release1StoryHostContextReadStatus.NotAuthoritative => Release1StoryRuntimeRejectReason.NotAuthoritative, Release1StoryHostContextReadStatus.UnsupportedMultiplayer => Release1StoryRuntimeRejectReason.UnsupportedMultiplayer, Release1StoryHostContextReadStatus.AmbiguousIdentity => Release1StoryRuntimeRejectReason.AmbiguousIdentity, _ => Release1StoryRuntimeRejectReason.ContextFaulted };
}
