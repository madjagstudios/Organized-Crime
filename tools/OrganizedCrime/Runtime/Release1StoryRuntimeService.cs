using OrganizedCrime.Core.Lifecycle;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime.Release1.Missions;

namespace OrganizedCrime.Runtime;

public sealed class Release1StoryRuntimeService : IDisposable
{
    private readonly object _gate = new();
    private readonly IRelease1StoryHostContext _context;
    private IRelease1StoryRepository _repository;
    private readonly Func<string, IRelease1StoryRepository>? _repositoryFactory;
    private Release1StoryState? _state;
    private Release1StoryHostContextSnapshot _activeContext;
    private Release1StoryState? _saveSnapshot;
    private Release1StoryRuntimePhase _phase = Release1StoryRuntimePhase.Detached;
    private bool _disposed;
    private readonly SaveLifecycle<Release1StoryRuntimePhase, Release1StoryRuntimeLifecycleResult, Release1StorySaveEnvelope> _saveLifecycle;
    private Release1MissionCommandContext _missionContext = null!;
    private Release1MissionCommandHandler _missions = null!;
    private Release1NativeEffectJournalHost _effects = null!;
    private Release1StoryRuntimeRejectReason _lastLifecycleRejectReason;
    /// <summary>
    /// OC-10: the exact bytes a failed load could not parse, captured when a SidecarLoadFailed
    /// quarantine occurs. OnSaveStart rewrites them verbatim while quarantined so the vanilla
    /// save routine does not remove a sidecar the mod never touched, and never serializes a
    /// fresh empty story over them.
    /// </summary>
    private string? _quarantinedRawContents;
    public Release1StoryRuntimeService(IRelease1StoryHostContext context, IRelease1StoryRepository repository)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _saveLifecycle = CreateSaveLifecycle();
        CreateMissionCommands();
    }

    public Release1StoryRuntimeService(IRelease1StoryHostContext context, Func<string, IRelease1StoryRepository> repositoryFactory)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _repository = null!;
        _saveLifecycle = CreateSaveLifecycle();
        CreateMissionCommands();
    }
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
            return _saveLifecycle.OnPreLoad();
        }
    }
    public Release1StoryRuntimeLifecycleResult OnLoadComplete()
    {
        lock (_gate)
        {
            return _saveLifecycle.OnLoadComplete();
        }
    }
    public Release1StoryRuntimeLifecycleResult OnSaveStart()
    {
        lock (_gate)
        {
            return _saveLifecycle.OnSaveStart();
        }
    }
    public Release1StoryRuntimeLifecycleResult OnSaveComplete()
    {
        lock (_gate)
        {
            return _saveLifecycle.OnSaveComplete();
        }
    }

    private SaveLifecycle<Release1StoryRuntimePhase, Release1StoryRuntimeLifecycleResult, Release1StorySaveEnvelope> CreateSaveLifecycle() =>
        new(new SaveLifecycleHooks<Release1StoryRuntimePhase, Release1StoryRuntimeLifecycleResult, Release1StorySaveEnvelope>
        {
            AwaitingLoadPhase = Release1StoryRuntimePhase.AwaitingLoad,
            ActivePhase = Release1StoryRuntimePhase.Active,
            SavingPhase = Release1StoryRuntimePhase.Saving,
            QuarantinedPhase = Release1StoryRuntimePhase.Quarantined,
            GetPhase = () => _phase,
            SetPhase = phase => _phase = phase,
            IsDisposed = () => _disposed,
            BuildResult = BuildLifecycleResult,
            MapContextStatus = MapContextStatusForLifecycle,
            ReadContext = ReadContextForLifecycle,
            CreateRepository = CreateRepositoryForLifecycle,
            GetBoundSaveFolder = GetBoundSaveFolderForLifecycle,
            SameSaveFolder = SameSaveFolder,
            Load = LoadForLifecycle,
            Activate = ActivateForLifecycle,
            AfterLoad = AfterLoadForLifecycle,
            ClearForLoad = ClearForLifecycle,
            PreserveQuarantined = PreserveQuarantinedSidecar,
            CaptureSaveSnapshot = () => _saveSnapshot = _state,
            ClearSaveSnapshot = () => _saveSnapshot = null,
            SaveUpdate = SaveUpdateForLifecycle,
            AfterSave = () => LastPersistedRevision = _saveSnapshot?.Revision ?? -1,
        });

    private void CreateMissionCommands()
    {
        _missionContext = new Release1MissionCommandContext(
            () => _state,
            state => _state = state,
            () => _activeContext,
            () => LastPersistedRevision,
            revision => _lastAppliedRevision = revision,
            () => _phase,
            CheckStoryCommandGate,
            CheckPresentationMutationGate,
            CheckEffectMutationGate,
            CheckCurrentAuthority,
            Reject,
            TryExecuteCore,
            PersistState);
        _missions = new Release1MissionCommandHandler(_missionContext);
        _effects = new Release1NativeEffectJournalHost(_missionContext);
    }

    private Release1StoryRuntimeLifecycleResult BuildLifecycleResult(SaveLifecycleOutcome outcome, string? message) => outcome switch
    {
        SaveLifecycleOutcome.Disposed => Lifecycle(Release1StoryRuntimeRejectReason.Disposed, "Service was disposed."),
        SaveLifecycleOutcome.PreLoadRepeat => Lifecycle(Release1StoryRuntimeRejectReason.None, "PreLoad was already handled."),
        SaveLifecycleOutcome.LoadRepeat => Lifecycle(Release1StoryRuntimeRejectReason.None, "LoadComplete was already handled."),
        SaveLifecycleOutcome.SaveStartRepeat => Lifecycle(Release1StoryRuntimeRejectReason.None, "SaveStart was already handled."),
        SaveLifecycleOutcome.SaveCompleteNoop => Lifecycle(Release1StoryRuntimeRejectReason.None, "SaveComplete was a lifecycle no-op."),
        SaveLifecycleOutcome.ContextPending => Lifecycle(Release1StoryRuntimeRejectReason.ContextPending, "Host context is pending."),
        SaveLifecycleOutcome.NotAuthoritative => Lifecycle(Release1StoryRuntimeRejectReason.NotAuthoritative, "Host authority is still settling during load."),
        SaveLifecycleOutcome.RepositoryUnbound => Lifecycle(Release1StoryRuntimeRejectReason.RepositoryUnbound, "Repository was not bound to a validated save folder."),
        SaveLifecycleOutcome.PathMismatch => Lifecycle(Release1StoryRuntimeRejectReason.RepositoryPathMismatch, "Repository save folder did not match host context."),
        SaveLifecycleOutcome.IdentityMismatch => Lifecycle(Release1StoryRuntimeRejectReason.IdentityMismatch, "Story sidecar identity did not match host context."),
        SaveLifecycleOutcome.SaveInactive => Lifecycle(Release1StoryRuntimeRejectReason.Inactive, "Only an active story can save."),
        SaveLifecycleOutcome.PreLoadOk => Lifecycle(Release1StoryRuntimeRejectReason.None, "Story state was cleared for load."),
        SaveLifecycleOutcome.LoadOk => Lifecycle(Release1StoryRuntimeRejectReason.None, "Story runtime is active."),
        SaveLifecycleOutcome.SaveStartOk => Lifecycle(Release1StoryRuntimeRejectReason.None, "Story save snapshot captured."),
        SaveLifecycleOutcome.SaveCompleteOk => Lifecycle(Release1StoryRuntimeRejectReason.None, "Story save snapshot persisted."),
        SaveLifecycleOutcome.ContextFaulted => Lifecycle(Release1StoryRuntimeRejectReason.ContextFaulted, message!),
        SaveLifecycleOutcome.FactoryFaulted => Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.LoadFaulted => Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.LoadFailed => Lifecycle(Release1StoryRuntimeRejectReason.SidecarLoadFailed, message!),
        SaveLifecycleOutcome.SaveFaulted => Lifecycle(Release1StoryRuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.SaveFailed => Lifecycle(Release1StoryRuntimeRejectReason.SidecarSaveFailed, message!),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown save lifecycle outcome."),
    };

    private Release1StoryRuntimeLifecycleResult MapContextStatusForLifecycle(Release1StoryHostContextReadStatus status) =>
        Lifecycle(Map(status), "Host context was not safe for story activation.");

    private SaveLifecycleContextRead ReadContextForLifecycle()
    {
        var status = _context.TryRead(out var snapshot);
        return new SaveLifecycleContextRead(status, snapshot);
    }

    private void CreateRepositoryForLifecycle(string activeSaveFolder)
    {
        if (_repositoryFactory is not null)
            _repository = _repositoryFactory(activeSaveFolder) ?? throw new InvalidOperationException("Repository factory returned null.");
    }

    private SaveLifecycleBoundFolder GetBoundSaveFolderForLifecycle() =>
        _repository is not IRelease1StorySaveFolderBoundRepository bound
            ? new SaveLifecycleBoundFolder(false, null)
            : new SaveLifecycleBoundFolder(true, bound.BoundSaveFolder);

    private SaveLifecycleLoadAttempt<Release1StorySaveEnvelope> LoadForLifecycle()
    {
        var load = _repository.Load();
        if (!load.Succeeded)
        {
            _quarantinedRawContents = load.RawContents;
            return SaveLifecycleLoadAttempt<Release1StorySaveEnvelope>.Failed(load.Message);
        }
        return SaveLifecycleLoadAttempt<Release1StorySaveEnvelope>.Loaded(load.Envelope?.Story is not null, load.Envelope?.Story?.PlayerId, load.Envelope);
    }

    private void ActivateForLifecycle(Release1StorySaveEnvelope? envelope, Release1StoryHostContextSnapshot snapshot)
    {
        _activeContext = snapshot; _state = envelope?.Story; LastPersistedRevision = _state?.Revision ?? -1;
    }

    private Release1StoryRuntimeLifecycleResult? AfterLoadForLifecycle()
    {
        RecoverAttemptingPhonePresentations();
        var smallCourtesy = _missions.ReconcileOnLoad(Release1MissionDescriptors.SmallCourtesy);
        if (smallCourtesy is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(smallCourtesy.RejectReason, smallCourtesy.Message);
        var wrongAddress = _missions.ReconcileOnLoad(Release1MissionDescriptors.WrongAddress);
        if (wrongAddress is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(wrongAddress.RejectReason, wrongAddress.Message);
        var roomWithNoName = _missions.ReconcileOnLoad(Release1MissionDescriptors.RoomWithNoName);
        if (roomWithNoName is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(roomWithNoName.RejectReason, roomWithNoName.Message);
        var shortNotice = _missions.ReconcileOnLoad(Release1MissionDescriptors.ShortNotice);
        if (shortNotice is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(shortNotice.RejectReason, shortNotice.Message);
        var keepTheLightsOff = _missions.ReconcileOnLoad(Release1MissionDescriptors.KeepTheLightsOff);
        if (keepTheLightsOff is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(keepTheLightsOff.RejectReason, keepTheLightsOff.Message);
        var theEnvelope = _missions.ReconcileOnLoad(Release1MissionDescriptors.TheEnvelope);
        if (theEnvelope is { Status: Release1StoryCommandStatus.Rejected }) return Lifecycle(theEnvelope.RejectReason, theEnvelope.Message);
        return null;
    }

    private void ClearForLifecycle()
    {
        _state = null; _saveSnapshot = null; _quarantinedRawContents = null; _phase = Release1StoryRuntimePhase.AwaitingLoad; LastPersistedRevision = -1;
    }

    private SaveLifecycleSaveAttempt SaveUpdateForLifecycle()
    {
        var result = Timing.Measure("story-sidecar-write/save-complete", () => _repository.Update(_saveSnapshot));
        return result.Succeeded
            ? SaveLifecycleSaveAttempt.Ok()
            : SaveLifecycleSaveAttempt.Failed(result.Message);
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
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.SmallCourtesy, command, assignment);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteWrongAddressAcceptanceDurably(
        Release1StoryCommand command,
        Release1WrongAddressAssignment assignment)
    {
        lock (_gate)
        {
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.WrongAddress, command, assignment);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteRoomWithNoNameAcceptanceDurably(
        Release1StoryCommand command,
        Release1RoomWithNoNameAssignment assignment)
    {
        lock (_gate)
        {
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.RoomWithNoName, command, assignment);
        }
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
            return _missions.TrySetProgress(Release1MissionDescriptors.RoomWithNoNameProgress, next);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteShortNoticeAcceptanceDurably(
        Release1StoryCommand command,
        Release1ShortNoticeAssignment assignment)
    {
        lock (_gate)
        {
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.ShortNotice, command, assignment);
        }
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
            return _missions.TrySetProgress(Release1MissionDescriptors.ShortNoticeProgress, next);
        }
    }

    public Release1StoryRuntimeCommandResult TryExecuteKeepTheLightsOffAcceptanceDurably(
        Release1StoryCommand command,
        Release1KeepTheLightsOffAssignment assignment)
    {
        lock (_gate)
        {
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.KeepTheLightsOff, command, assignment);
        }
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
            return _missions.TrySetProgress(Release1MissionDescriptors.KeepTheLightsOffProgress, next);
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
            return _missions.TryExecuteAcceptanceDurably(Release1MissionDescriptors.TheEnvelope, command, assignment);
        }
    }

    public Release1StoryRuntimeCommandResult TrySetTheEnvelopeProgress(Release1TheEnvelopeProgress next)
    {
        lock (_gate)
        {
            return _missions.TrySetProgress(Release1MissionDescriptors.TheEnvelopeProgress, next);
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
            return _effects.TryPrepareNativeEffect(effect);
        }
    }

    public Release1StoryRuntimeCommandResult TryMarkNativeEffectApplied(
        string effectId,
        string nativeReceiptId,
        Release1NativeEffectPersistenceMode mode = Release1NativeEffectPersistenceMode.SaveGated)
    {
        lock (_gate)
        {
            return _effects.TryMarkNativeEffectApplied(effectId, nativeReceiptId, mode);
        }
    }

    public Release1StoryRuntimeCommandResult TryCommitNativeEffect(string effectId, string storyCorrelationId, long? capturedPersistedRevision = null)
    {
        lock (_gate)
        {
            return _effects.TryCommitNativeEffect(effectId, storyCorrelationId, capturedPersistedRevision);
        }
    }

    public Release1StoryRuntimeCommandResult TryRecordNativeEffectIssuance(string effectId, Release1NativeEffectIssuanceOutcome outcome, string? nativeReceiptId = null)
    {
        lock (_gate)
        {
            return _effects.TryRecordNativeEffectIssuance(effectId, outcome, nativeReceiptId);
        }
    }
    public bool TryGetExecutablePreparedEffect(string effectId, out Release1NativeEffectJournalEntry effect)
    {
        lock (_gate)
        {
            return _effects.TryGetExecutablePreparedEffect(effectId, out effect);
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
