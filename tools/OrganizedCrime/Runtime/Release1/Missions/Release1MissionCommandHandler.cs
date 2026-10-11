using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.Missions;

// OC-153: the service-owned state and helpers the mission command handler and
// the native-effect journal host reach through. Every member is a delegate the
// service wires once in its constructor (method groups where possible); the
// handler and host take no lock and run only while the service holds its gate.
internal delegate Release1StoryRuntimeCommandResult TryExecuteCoreDelegate(Release1StoryCommand command, bool persistImmediately);

internal sealed class Release1MissionCommandContext
{
    public Release1MissionCommandContext(
        Func<Release1StoryState?> getState,
        Action<Release1StoryState?> setState,
        Func<Release1StoryHostContextSnapshot> getActiveContext,
        Func<long> getLastPersistedRevision,
        Action<long> setLastAppliedRevision,
        Func<Release1StoryRuntimePhase> getPhase,
        Func<Release1StoryCommand?, Release1StoryRuntimeCommandResult?> checkStoryCommandGate,
        Func<Release1StoryRuntimeCommandResult?> checkPresentationMutationGate,
        Func<Release1StoryRuntimeCommandResult?> checkEffectMutationGate,
        Func<Release1StoryRuntimeCommandResult?> checkCurrentAuthority,
        Func<Release1StoryRuntimeRejectReason, string, Release1StoryRuntimeCommandResult> reject,
        TryExecuteCoreDelegate tryExecuteCore,
        Func<Release1StoryState, string, Release1StoryRuntimeCommandResult> persistState)
    {
        GetState = getState ?? throw new ArgumentNullException(nameof(getState));
        SetState = setState ?? throw new ArgumentNullException(nameof(setState));
        GetActiveContext = getActiveContext ?? throw new ArgumentNullException(nameof(getActiveContext));
        GetLastPersistedRevision = getLastPersistedRevision ?? throw new ArgumentNullException(nameof(getLastPersistedRevision));
        SetLastAppliedRevision = setLastAppliedRevision ?? throw new ArgumentNullException(nameof(setLastAppliedRevision));
        GetPhase = getPhase ?? throw new ArgumentNullException(nameof(getPhase));
        CheckStoryCommandGate = checkStoryCommandGate ?? throw new ArgumentNullException(nameof(checkStoryCommandGate));
        CheckPresentationMutationGate = checkPresentationMutationGate ?? throw new ArgumentNullException(nameof(checkPresentationMutationGate));
        CheckEffectMutationGate = checkEffectMutationGate ?? throw new ArgumentNullException(nameof(checkEffectMutationGate));
        CheckCurrentAuthority = checkCurrentAuthority ?? throw new ArgumentNullException(nameof(checkCurrentAuthority));
        Reject = reject ?? throw new ArgumentNullException(nameof(reject));
        TryExecuteCore = tryExecuteCore ?? throw new ArgumentNullException(nameof(tryExecuteCore));
        PersistState = persistState ?? throw new ArgumentNullException(nameof(persistState));
    }

    public Func<Release1StoryState?> GetState { get; }
    public Action<Release1StoryState?> SetState { get; }
    public Func<Release1StoryHostContextSnapshot> GetActiveContext { get; }
    public Func<long> GetLastPersistedRevision { get; }
    public Action<long> SetLastAppliedRevision { get; }
    public Func<Release1StoryRuntimePhase> GetPhase { get; }
    public Func<Release1StoryCommand?, Release1StoryRuntimeCommandResult?> CheckStoryCommandGate { get; }
    public Func<Release1StoryRuntimeCommandResult?> CheckPresentationMutationGate { get; }
    public Func<Release1StoryRuntimeCommandResult?> CheckEffectMutationGate { get; }
    public Func<Release1StoryRuntimeCommandResult?> CheckCurrentAuthority { get; }
    public Func<Release1StoryRuntimeRejectReason, string, Release1StoryRuntimeCommandResult> Reject { get; }
    public TryExecuteCoreDelegate TryExecuteCore { get; }
    public Func<Release1StoryState, string, Release1StoryRuntimeCommandResult> PersistState { get; }
}

// OC-153: the shared skeleton behind the six TryExecute*AcceptanceDurably,
// six ReconcileAccepted*, six Reconcile*OnLoad and four TrySet*Progress
// clones. Each method follows its originals statement for statement; every
// per-mission difference comes from the descriptor, so this class never
// branches on the mission identity. Call only while holding the service gate.
internal sealed class Release1MissionCommandHandler
{
    private readonly Release1MissionCommandContext _context;

    public Release1MissionCommandHandler(Release1MissionCommandContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Release1StoryRuntimeCommandResult TryExecuteAcceptanceDurably<TAssignment, TMode>(
        Release1MissionDescriptor<TAssignment, TMode> descriptor,
        Release1StoryCommand command,
        TAssignment assignment)
    {
        var gate = _context.CheckStoryCommandGate(command);
        if (gate is not null) return gate;
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        if (assignment is null) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, descriptor.DisplayName + " assignment was null.");
        try { descriptor.ValidateAssignment(assignment); }
        catch (ArgumentException exception) { return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }

        var expectedTransition = descriptor.MapModeToTransition(descriptor.GetMode(assignment));
        if (expectedTransition is null || command.TransitionKind != expectedTransition ||
            command.MissionKey != descriptor.MissionKey ||
            descriptor.GetMissionKey(assignment) != command.MissionKey || descriptor.GetAttempt(assignment) != command.Attempt ||
            descriptor.GetAuthorizationCorrelationId(assignment) != command.CorrelationId)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, descriptor.DisplayName + " assignment did not match its acceptance command.");
        if (descriptor.IsPrimaryMode(descriptor.GetMode(assignment)) &&
            !string.Equals(command.TermsVersion, descriptor.TermsVersion, StringComparison.Ordinal))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, descriptor.DisplayName + " primary terms version was not canonical.");

        var existing = descriptor.GetAssignments(_context.GetState()!).SingleOrDefault(candidate => descriptor.GetAttempt(candidate) == descriptor.GetAttempt(assignment));
        if (existing is not null && !descriptor.AssignmentsEqual(existing, assignment))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, descriptor.DisplayName + " attempt already had a different assignment.");

        var transition = Release1StoryTransitions.Apply(_context.GetState()!, command);
        if (!transition.Accepted || transition.State is null)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, transition.Message);
        if (transition.Idempotent)
        {
            if (existing is null)
                return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Accepted " + descriptor.DisplayName + " correlation had no atomic assignment.");
            return ReconcileAccepted(descriptor, existing);
        }

        Release1StoryState candidate;
        try
        {
            candidate = descriptor.WithAssignmentAppended(transition.State, assignment);
            candidate.Validate();
        }
        catch (ArgumentException exception)
        {
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message);
        }

        var persisted = _context.PersistState(candidate, descriptor.DisplayName + " acceptance and assignment were persisted atomically.");
        if (!persisted.Accepted) return persisted;
        return ReconcileAccepted(descriptor, assignment);
    }

    public Release1StoryRuntimeCommandResult ReconcileAccepted<TAssignment, TMode>(
        Release1MissionDescriptor<TAssignment, TMode> descriptor,
        TAssignment assignment)
    {
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var mission = _context.GetState()!.Missions[Release1MissionCatalog.IndexOf(descriptor.MissionKey)];
        if (!descriptor.IsPrimaryMode(descriptor.GetMode(assignment)) || mission.State != Release1MissionState.Accepted)
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _context.GetState(), descriptor.DisplayName + " stage was already active.");
        var receipt = $"{descriptor.ActivateReceiptPrefix}{mission.Attempt}";
        var command = new Release1StoryCommand(
            _context.GetActiveContext().SessionEpoch,
            _context.GetActiveContext().LoadEpoch,
            _context.GetActiveContext().PlayerId,
            descriptor.MissionKey,
            mission.Attempt,
            Release1TransitionKind.MissionActivated,
            receipt,
            Release1LogicalCorrelation.Create(_context.GetActiveContext().PlayerId, descriptor.MissionKey, mission.Attempt, Release1TransitionKind.MissionActivated, receipt).Value);
        return _context.TryExecuteCore(command, persistImmediately: true);
    }

    public Release1StoryRuntimeCommandResult? ReconcileOnLoad<TAssignment, TMode>(
        Release1MissionDescriptor<TAssignment, TMode> descriptor)
    {
        if (_context.GetState() is null) return null;
        var mission = _context.GetState()!.Missions[Release1MissionCatalog.IndexOf(descriptor.MissionKey)];
        if (mission.State == Release1MissionState.Accepted)
        {
            var assignment = descriptor.GetAssignments(_context.GetState()!).SingleOrDefault(candidate =>
                descriptor.GetAttempt(candidate) == mission.Attempt && descriptor.IsPrimaryMode(descriptor.GetMode(candidate)));
            if (assignment is null)
            {
                var message = descriptor.MissingAssignmentMessage(mission.Attempt);
                return message is null ? null : _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, message);
            }
            return ReconcileAccepted(descriptor, assignment);
        }
        if (mission.State != Release1MissionState.Deferred) return null;
        var receipt = $"{descriptor.ReofferReceiptPrefix}{mission.Attempt + 1}";
        var command = new Release1StoryCommand(
            _context.GetActiveContext().SessionEpoch,
            _context.GetActiveContext().LoadEpoch,
            _context.GetActiveContext().PlayerId,
            descriptor.MissionKey,
            mission.Attempt,
            Release1TransitionKind.MissionReoffered,
            receipt,
            Release1LogicalCorrelation.Create(_context.GetActiveContext().PlayerId, descriptor.MissionKey, mission.Attempt, Release1TransitionKind.MissionReoffered, receipt).Value);
        return _context.TryExecuteCore(command, persistImmediately: true);
    }

    public Release1StoryRuntimeCommandResult TrySetProgress<TProgress>(
        Release1MissionProgressDescriptor<TProgress> descriptor,
        TProgress next)
    {
        var gate = _context.CheckPresentationMutationGate();
        if (gate is not null) return gate;
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        if (next is null) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, descriptor.DisplayName + " progress was null.");
        try { descriptor.ValidateProgress(next); }
        catch (ArgumentException exception) { return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
        if (!descriptor.HasAssignmentForAttempt(_context.GetState()!, next))
            return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, descriptor.DisplayName + " progress had no accepted assignment for its attempt.");

        var existing = descriptor.GetProgress(_context.GetState()!).SingleOrDefault(progress => descriptor.GetAttempt(progress) == descriptor.GetAttempt(next));
        if (existing is not null)
        {
            var regression = descriptor.CheckRegression(existing, next);
            if (regression is not null)
                return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, regression);
        }
        if (existing is not null && descriptor.ProgressEqual(existing, next))
            return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.None, _context.GetState(), descriptor.DisplayName + " progress was already recorded.");

        var current = _context.GetState()!;
        var updated = descriptor.WithProgressList(current, existing is null
            ? descriptor.GetProgress(current).Append(next).ToArray()
            : descriptor.GetProgress(current).Select(progress => descriptor.GetAttempt(progress) == descriptor.GetAttempt(next) ? next : progress).ToArray());
        try { updated.Validate(); }
        catch (ArgumentException exception) { return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, exception.Message); }
        _context.SetState(updated);
        return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _context.GetState(), descriptor.DisplayName + " progress was recorded in memory.");
    }
}
