using OrganizedCrime.Runtime.Police;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The production OC officer world: every native lookup the roster needs,
/// ported from the OC-107 clone proof and re-verified against the 0.4.7f9
/// interop by reflection. Every call returns a status and never throws.
/// f9 differences from the f6 proof: <c>NPCSpeedController</c> lost its
/// parameterless constructor (only <c>(float)</c> remains), and FishNet's
/// <c>PrefabObjects</c> still offers no single-entry removal, so a destroyed
/// template's numeric registration stays behind, never referenced again.
/// </summary>
public sealed class Il2CppOcOfficerWorld : IOcOfficerWorld, IOcPatrolWorld, IOcOfficerAttachmentDiagnostics, IOcOfficerPoolRecoveryWorld, IOcOfficerDependencyWorld, IOcOfficerGuidWorld
{
    private readonly OcOfficerWorldContext _context;
    private readonly OcOfficerTemplateBuilder _templates;
    private readonly OcOfficerSpawner _spawner;
    private readonly OcOfficerDutyBinder _duty;
    private readonly OcOfficerDiagnostics _diagnostics;
    private readonly OcPatrolBindings _patrols;
    private readonly Il2CppOcPatrolNative _patrolNative;

    public Il2CppOcOfficerWorld(Action<string>? warn = null)
    {
        _context = new OcOfficerWorldContext(warn);
        _templates = new OcOfficerTemplateBuilder(_context);
        _spawner = new OcOfficerSpawner(_context, _templates);
        _duty = new OcOfficerDutyBinder(_context);
        _diagnostics = new OcOfficerDiagnostics(_context);
        _patrolNative = new Il2CppOcPatrolNative(_context);
        _patrols = new OcPatrolBindings(_patrolNative);
    }

    OcOfficerWorldStatus IOcPatrolWorld.TryReadPatrolState(OcOfficerCloneRef clone, out OcPatrolCloneState state) =>
        _patrolNative.TryReadState(clone, out state);

    OcOfficerWorldStatus IOcOfficerAttachmentDiagnostics.TryReadStaleAttachmentCount(OcOfficerCloneRef clone, out int count) =>
        _diagnostics.TryReadStaleAttachmentCount(clone, out count);

    void IOcOfficerPoolWorld.PollOfficerPools() => _patrolNative.PollOfficerPools();

    void IOcOfficerPoolWorld.ResetOfficerPoolWarnings() => _patrolNative.ResetOfficerPoolWarnings();

    bool IOcOfficerPoolRecoveryWorld.IsOwnedOfficer(long pointer) => _context._ownPointers.Contains(pointer);

    void IOcOfficerPoolRecoveryWorld.SetPoolRelease(Action<long> release) => _patrolNative.PoolRelease = release;

    public bool IsHost => _context.IsHost;

    void IOcOfficerGuidWorld.ClearOwnedGuids() => _spawner.ClearOwnedGuids();
    void IOcOfficerDependencyWorld.PollDependencies() => _diagnostics.PollDependencies();
    void IOcOfficerDependencyWorld.ResetDependencyWarnings() => _diagnostics.ResetDependencyWarnings();
    string IOcOfficerDependencyWorld.ReadDependencies(OcOfficerCloneRef clone) => _diagnostics.ReadDependencies(clone);

    public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) =>
        _diagnostics.TryReadGameMinutes(out minutes);

    public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail) =>
        _templates.TryEnsureTemplate(templateId, out status, out detail);

    public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail) =>
        _spawner.TrySpawnClone(position, cloneId, out clone, out detail);

    public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) =>
        _diagnostics.TryReadKnockedOut(clone, out knockedOut);

    public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) =>
        _diagnostics.TryReadClonePosition(clone, out position);

    public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
    {
        if (_patrols.Snapshot().Any(p => p.Officer == clone))
        {
            var stopped = _patrols.TryUnbind(clone, out detail);
            if (stopped != OcOfficerWorldStatus.Ok) return stopped;
        }
        return _duty.TryBindToCheckpoint(clone, nativeCheckpoint, standPoint, out detail);
    }

    public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive) =>
        _diagnostics.TryReadBehaviourState(clone, out activeBehaviour, out checkpointActive);

    public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state) =>
        _diagnostics.TryReadBoundState(clone, ocCheckpoint, out state);

    public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics) =>
        _diagnostics.TryReadBehaviourDiagnostics(clone, ocCheckpoint, out diagnostics);

    public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
    {
        if (_patrols.Snapshot().Any(p => p.Officer == clone))
        {
            var stopped = _patrols.TryUnbind(clone, out detail);
            if (stopped != OcOfficerWorldStatus.Ok) return stopped;
        }
        return _spawner.TryRemoveClone(clone, out detail);
    }

    OcOfficerWorldStatus IOcPatrolWorld.TryBindPatrol(OcOfficerCloneRef clone, OcPatrolDuty duty, OcOfficerPosition target, out string detail) =>
        _patrols.TryBind(clone, duty, target, out detail);

    OcOfficerWorldStatus IOcPatrolWorld.TryUnbindPatrol(OcOfficerCloneRef clone, out string detail) =>
        _patrols.TryUnbind(clone, out detail);

    void IOcPatrolWorld.ClearPatrols() => _patrols.ClearPatrols();

    IReadOnlyList<OcPatrolSnapshot> IOcPatrolWorld.PatrolSnapshot() => _patrols.Snapshot();

    IReadOnlyList<string> IOcPatrolWorld.PatrolPoolStatus() => _patrolNative.ReadPoolStatus();

    int IOcPatrolWorld.PatrolGroupCount() => _patrols.PatrolGroupCount();

    public OcOfficerWorldStatus TryDestroyTemplate(out string detail) =>
        _templates.TryDestroyTemplate(out detail);

    public OcOfficerWorldStatus TryClearSwapMap(out string detail) =>
        _duty.TryClearSwapMap(out detail);

    public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) =>
        _duty.TryUnmapClone(clone, out detail);

    public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail) =>
        _diagnostics.TryReadNativeLeak(nativeLabel, out leaked, out detail);

    public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) =>
        _templates.TryReadTemplateStatus(out status);

    public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) =>
        _diagnostics.TryReadNativeCounts(out counts);

    public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees) =>
        _diagnostics.TryReadPlayerPose(out position, out yawDegrees);
}
