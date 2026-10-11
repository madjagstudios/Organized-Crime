using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.DevUtilities;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public interface ISyndicateHqPlayerTransport
{
    bool TryCapture(SyndicateHqDoorFingerprint door, Release1StoryHostContextSnapshot context, out SyndicateHqExteriorReturnState state);
    bool TryTeleport(SyndicateHqVector3 position);
    bool TryReadPosition(out SyndicateHqVector3 position);
}

public interface ISyndicateHqUnlockAuthority
{
    SyndicateHqUnlockDecision Evaluate();
}

public interface ISyndicateHqInterior
{
    bool IsCreated { get; }
    bool IsDestroyPending { get; }
    bool IsReadyForSave => IsCreated && !IsDestroyPending;
    bool TryEnsure(SyndicateHqVector3 exteriorAnchor);
    bool TryGetMarker(string name, out SyndicateHqVector3 position);
    // An interior without a verified storage suspension path must use boundary teardown.
    bool TrySetSaveInteractions(bool enabled) => false;
    void Destroy();
    void Dispose();
}

public sealed class Release1StorySyndicateHqUnlockAuthority : ISyndicateHqUnlockAuthority
{
    private readonly IRelease1StoryHostContext _context;
    private readonly Release1StoryRuntimeService _story;
    public Release1StorySyndicateHqUnlockAuthority(IRelease1StoryHostContext context, Release1StoryRuntimeService story) { _context = context; _story = story; }
    public SyndicateHqUnlockDecision Evaluate()
    {
        try
        {
            var status = _context.TryRead(out var snapshot);
            return SyndicateHqUnlockAdmission.Evaluate(status, snapshot, _story.State);
        }
        catch (Exception ex) { return SyndicateHqUnlockDecision.Locked($"Unlock authority failed closed: {ex.Message}"); }
    }
}

public sealed class SyndicateHqRuntimeService : IDisposable
{
    private readonly IRelease1StoryHostContext _context;
    private readonly ISyndicateHqUnlockAuthority _authority;
    private readonly ISyndicateHqDoorSource _doorObserver;
    private readonly ISyndicateHqInterior _interior;
    private readonly ISyndicateHqPlayerTransport _transport;
    private readonly Action<string> _log;
    private readonly Func<float> _now;
    private readonly SyndicateHqEntryStateMachine _machine = new();
    private SyndicateHqExteriorReturnState? _returnState;
    private SyndicateHqPrompt _prompt = new(SyndicateHqPromptKind.None, string.Empty, string.Empty);
    private SyndicateHqDoorFingerprint? _promptFingerprint;
    private Release1StoryHostContextSnapshot? _promptContext;
    private long _promptGeneration;
    private float _promptExpiresAt;
    private bool _runtimePrepared;
    private bool _teardownBlocked;
    private bool _recoveryRequiresFreshAccessPoint;
    private bool _returnedForTeardown;
    private bool _exitPromptArmed;
    private float _reconcileAwaySince = -1f;
    private float _reconcileNextCheck;
    private bool _disposed;
    private bool _saveInProgress;
    private bool _retainedForSave;
    private bool _saveRecoveryPending;
    private Release1StoryHostContextSnapshot? _preparedContext;
    private Release1StoryHostContextSnapshot? _retentionLogContext;
    private bool _retentionFailureLogged;
    private string _retentionStep = "admission";

    public SyndicateHqRuntimeService(IRelease1StoryHostContext context, ISyndicateHqUnlockAuthority authority, ISyndicateHqDoorSource doorObserver, ISyndicateHqInterior interior, ISyndicateHqPlayerTransport transport, Action<string>? log = null, Func<float>? now = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _doorObserver = doorObserver ?? throw new ArgumentNullException(nameof(doorObserver));
        _interior = interior ?? throw new ArgumentNullException(nameof(interior));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _log = log ?? (_ => { });
        _now = now ?? (() => Time.unscaledTime);
        if (_interior is SyndicateHqInteriorRuntime nativeInterior)
            nativeInterior.SetSaveRetentionFailureReporter((step, ex) => { _retentionStep = step; LogRetentionFailure(ex); });
    }

    public SyndicateHqEntryState State => _machine.State;
    public SyndicateHqPrompt Prompt => _prompt;
    public bool IsInside => _machine.State == SyndicateHqEntryState.Inside;
    public bool IsTeardownBlocked => _teardownBlocked;
    public bool IsDisposed => _disposed;

    /// <summary>
    /// OC-70 final review fix (finding 5). Evicts the door observer's retained identity so the very
    /// next resolve (triggered by the <see cref="OnLoadComplete"/> call that
    /// <see cref="SyndicateHqRuntimeComposition.OnLoadComplete"/> makes right after this) is forced
    /// through the door observer's fresh scene scan, doors.Length != 1 and
    /// <see cref="SyndicateHqIdentity"/>.Validate included, instead of trusting a possibly-stale
    /// retained reference. Deliberately not called from inside <see cref="OnLoadComplete"/> itself:
    /// <see cref="OnSaveComplete"/> also falls through to <see cref="OnLoadComplete"/> whenever the
    /// runtime is not yet prepared (unsafe save-start boundaries tear the runtime down), and
    /// evicting there too would force a full scene scan at every save, undoing Task 3's retained-identity
    /// performance fix. Composition calls this only from the mod's real load-complete handler.
    /// </summary>
    public void EvictRetainedDoorIdentityForLoad() => _doorObserver.EvictRetainedForLoad();

    public bool OnLoadComplete()
    {
        if (_disposed) return false;
        if (_teardownBlocked && !TryTeardown()) return false;
        if (_runtimePrepared) return true;
        try
        {
            if (!_doorObserver.TryAttach()) { _machine.Fault(); return false; }
            var fingerprint = _doorObserver.CurrentFingerprint;
            if (fingerprint is null || !_interior.TryEnsure(fingerprint.Position))
            {
                FailActivationCleanup();
                return false;
            }
            _preparedContext = _context.TryRead(out var context) == Release1StoryHostContextReadStatus.Ready ? context : null;
            if (_preparedContext is not null) ObserveRetentionLogContext(_preparedContext.Value);
            _machine.MarkReady();
            _runtimePrepared = true;
            _teardownBlocked = false;
            return true;
        }
        catch (Exception ex) { _log($"Syndicate HQ load activation failed: {ex.Message}"); FailActivationCleanup(); return false; }
    }

    public bool OnPreLoad() => TeardownAtBoundary();
    public bool OnPreSceneChange() => TeardownAtBoundary();
    public bool OnSaveStart()
    {
        if (_disposed) return false;
        if (_saveInProgress) return !_teardownBlocked;
        _saveInProgress = true;
        ClearPrompt();
        _retentionStep = "admission";
        try
        {
            if (CanRetainForSave() && _interior.TrySetSaveInteractions(false))
            {
                _retainedForSave = true;
                return true;
            }
        }
        catch (Exception ex) { LogRetentionFailure(ex); }
        var succeeded = TeardownAtBoundary(loadBoundary: false);
        // Keep entry suspended through save-complete, including fallback. Unity may
        // defer root destruction; all fallback saves must retry recovery next frame.
        _saveInProgress = true;
        _saveRecoveryPending = true;
        return succeeded;
    }

    private bool CanRetainForSave()
    {
        _retentionStep = "load context";
        if (_context.TryRead(out var context) != Release1StoryHostContextReadStatus.Ready) return false;
        ObserveRetentionLogContext(context);
        if (_preparedContext is null || !SameContext(_preparedContext.Value, context)) return false;
        if (!_runtimePrepared || _teardownBlocked || IsInside || _returnState is not null ||
            _machine.State is not (SyndicateHqEntryState.AwaitingDoor or SyndicateHqEntryState.LockedPrompt or SyndicateHqEntryState.EnterPrompt)) return false;
        _retentionStep = "door currency";
        if (_doorObserver.CurrentFingerprint is not { } door || !_doorObserver.IsCurrent(door, _doorObserver.Generation)) return false;
        _retentionStep = "interior readiness";
        if (_interior.IsDestroyPending || !_interior.IsReadyForSave) return false;
        _retentionStep = "exit marker";
        if (!_interior.TryGetMarker(SyndicateHqContract.ExitMarker, out var exit) || !exit.IsFinite) return false;
        _retentionStep = "player capture";
        // Refresh from this load's canonical identity; a previous native capture can
        // remain a non-null managed wrapper after the old scene has been destroyed.
        if (!_transport.TryCapture(door, context, out _)) return false;
        _retentionStep = "player position";
        if (!_transport.TryReadPosition(out var player) || !player.IsFinite) return false;

        // The entire 14 by 10 m pocket is within these bounds. A state flag alone cannot
        // prove outside after a transport, position spike, or interrupted transition.
        var dx = player.X - exit.X;
        var dz = player.Z - exit.Z;
        _retentionStep = "closet suspension";
        return MathF.Sqrt(dx * dx + dz * dz) > ReconcileAwayHorizontalMeters ||
            player.Y < exit.Y - ReconcileBelowRoomMeters;
    }

    private void ObserveRetentionLogContext(Release1StoryHostContextSnapshot context)
    {
        if (_retentionLogContext is not null && SameContext(_retentionLogContext.Value, context)) return;
        _retentionLogContext = context;
        _retentionFailureLogged = false;
    }

    private void LogRetentionFailure(Exception ex)
    {
        if (_retentionFailureLogged) return;
        _retentionFailureLogged = true;
        // OC-84: operational failures stay visible through the ordinary sink.
        _log($"Syndicate HQ save retention failed closed: step={_retentionStep}; {ex.GetType().FullName}: {ex.Message}; managed stack: {ex.StackTrace ?? Environment.StackTrace}");
    }

    public bool OnSaveComplete()
    {
        if (_disposed) return false;
        _saveInProgress = false;
        var recoveringRetainedSave = _retainedForSave || _saveRecoveryPending;
        if (_retainedForSave)
        {
            _retainedForSave = false;
            try
            {
                _retentionStep = "completion context";
                var current = _context.TryRead(out var context) == Release1StoryHostContextReadStatus.Ready &&
                    _preparedContext is not null && SameContext(_preparedContext.Value, context);
                _retentionStep = "closet resume";
                if (current && _runtimePrepared && !_teardownBlocked && !_interior.IsDestroyPending &&
                    _interior.IsReadyForSave && _interior.TrySetSaveInteractions(true)) return true;
            }
            catch (Exception ex) { LogRetentionFailure(ex); }
            if (!TeardownAtBoundary(loadBoundary: false)) { _saveRecoveryPending = true; return false; }
        }
        if (_teardownBlocked && !TryTeardown()) return false;
        var ready = _runtimePrepared || OnLoadComplete();
        if (recoveringRetainedSave) _saveRecoveryPending = !ready;
        return ready;
    }

    public void Update()
    {
        if (_disposed || _saveInProgress) return;
        try
        {
            if (_saveRecoveryPending)
            {
                // Unity defers Destroy until the end of the frame. Retained-save recovery
                // may therefore need the next update before TryEnsure can recreate the root.
                if (OnLoadComplete()) _saveRecoveryPending = false;
                return;
            }
            if (_prompt.Kind != SyndicateHqPromptKind.None && _now() >= _promptExpiresAt) ClearPrompt();
            if (IsInside && _interior.TryGetMarker(SyndicateHqContract.ExitMarker, out var exitMarker) && _transport.TryReadPosition(out var playerPosition))
            {
                var distanceToExit = playerPosition.DistanceTo(exitMarker);
                if (distanceToExit > 1.5f) _exitPromptArmed = true;
                var besideExit = _exitPromptArmed && distanceToExit <= 1.5f;
                if (besideExit && _context.TryRead(out var context) == Release1StoryHostContextReadStatus.Ready)
                    ShowPrompt(new(SyndicateHqPromptKind.Exit, "Leave Syndicate HQ.", SyndicateHqContract.ExitAction), 2f, _doorObserver.Generation, _doorObserver.CurrentFingerprint, context);
                else if (!besideExit && _prompt.Kind == SyndicateHqPromptKind.Exit) ClearPrompt();
            }
            CheckReconcileLeftWithoutExit();
            if (_prompt.Kind is SyndicateHqPromptKind.Enter or SyndicateHqPromptKind.Exit && Input.GetKeyDown(KeyCode.E))
            {
                if (_prompt.Kind == SyndicateHqPromptKind.Enter) TryEnter();
                else TryExit();
            }
        }
        catch (Exception ex) { _log($"Syndicate HQ update was isolated: {ex.Message}"); }
    }

    public void OnGUI()
    {
        if (_disposed || _prompt.Kind == SyndicateHqPromptKind.None) return;
        try
        {
            var width = Mathf.Min(SyndicateHqPromptLayout.Width, Mathf.Max(320f, Screen.width - 32f));
            var box = new Rect(
                Screen.width / 2f - width / 2f,
                Screen.height - SyndicateHqPromptLayout.Height - 28f,
                width,
                SyndicateHqPromptLayout.Height);
            var boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = SyndicateHqPromptLayout.BodyFontSize,
                wordWrap = true,
                padding = new RectOffset(22, 22, 18, 18)
            };
            boxStyle.normal.textColor = Color.white;
            GUI.Box(box, _prompt.Text, boxStyle);
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = SyndicateHqPromptLayout.ActionFontSize
            };
            var button = new Rect(box.x + 42f, box.yMax - SyndicateHqPromptLayout.ActionHeight - 18f, box.width - 84f, SyndicateHqPromptLayout.ActionHeight);
            if (_prompt.Kind is SyndicateHqPromptKind.Enter or SyndicateHqPromptKind.Exit && GUI.Button(button, _prompt.ActionText + "  [E]", buttonStyle))
            {
                if (_prompt.Kind == SyndicateHqPromptKind.Enter) TryEnter();
                else TryExit();
            }
        }
        catch (Exception ex) { _log($"Syndicate HQ presentation was isolated: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!TryTeardown()) { _log("Syndicate HQ disposal is blocked until a safe teardown succeeds."); return; }
        try
        {
            if (!_doorObserver.TryDispose())
            {
                _teardownBlocked = true;
                _machine.Fault();
                _log("Syndicate HQ listener disposal remained retryable.");
                return;
            }
            _interior.Dispose();
            _disposed = true;
            _machine.Dispose();
        }
        catch (Exception ex) { _log($"Syndicate HQ disposal failed and remains retryable: {ex.Message}"); }
    }

    public void HandleDoorInteraction(SyndicateHqDoorFingerprint fingerprint, long generation)
    {
        if (_disposed || _saveInProgress || _teardownBlocked || !_runtimePrepared || !_doorObserver.IsCurrent(fingerprint, generation) || _machine.State is SyndicateHqEntryState.Inside or SyndicateHqEntryState.Entering or SyndicateHqEntryState.Exiting or SyndicateHqEntryState.Faulted) return;
        try
        {
            if (_context.TryRead(out var context) != Release1StoryHostContextReadStatus.Ready) return;
            var decision = _authority.Evaluate();
            _machine.ObserveDoor(decision.IsUnlocked);
            ShowPrompt(decision.IsUnlocked ? new(SyndicateHqPromptKind.Enter, "Syndicate HQ is ready.", SyndicateHqContract.EnterAction) : new(SyndicateHqPromptKind.Locked, "Syndicate HQ is locked.", string.Empty), decision.IsUnlocked ? 5f : 3f, generation, fingerprint, context);
        }
        catch (Exception ex) { _log($"Syndicate HQ door admission was isolated: {ex.Message}"); }
    }

    public bool TryEnter()
    {
        try
        {
            if (_disposed || _saveInProgress || _teardownBlocked || !_runtimePrepared || _machine.State != SyndicateHqEntryState.EnterPrompt || !IsCurrentPrompt() || !_authority.Evaluate().IsUnlocked) return false;
            if (_doorObserver.CurrentFingerprint is not { } door || _context.TryRead(out var context) != Release1StoryHostContextReadStatus.Ready || !SameContext(context, _promptContext!.Value) || !_doorObserver.IsCurrent(_promptFingerprint!, _promptGeneration)) return false;
            if (!_transport.TryCapture(door, context, out var returnState) || !_interior.TryGetMarker(SyndicateHqContract.EntryMarker, out var entryMarker)) return false;
            _returnState = returnState;
            _returnedForTeardown = false;
            if (!_machine.BeginEntry(true)) return false;
            if (!_transport.TryTeleport(entryMarker)) { _machine.CompleteEntry(false); return false; }
            _machine.CompleteEntry(true);
            _exitPromptArmed = false;
            ClearPrompt();
            return true;
        }
        catch (Exception ex) { _log($"Syndicate HQ entry failed safely: {ex.Message}"); _machine.Fault(); return false; }
    }

    public bool TryExit()
    {
        if (_disposed || _teardownBlocked || !_runtimePrepared || _machine.State != SyndicateHqEntryState.Inside) return false;
        try
        {
            if (_context.TryRead(out var context) != Release1StoryHostContextReadStatus.Ready) return false;
            var plan = SyndicateHqReturnFallbackPlanner.Plan(_returnState, context.SessionEpoch, context.LoadEpoch, context.PlayerId);
            if (plan.Path == SyndicateHqReturnPath.NoSafeReturn || !_machine.BeginExit()) return false;
            if (!_transport.TryTeleport(plan.Position)) { _machine.CompleteExit(false); return false; }
            _machine.CompleteExit(true);
            _returnState = null;
            _returnedForTeardown = false;
            ClearPrompt();
            return true;
        }
        catch (Exception ex) { _log($"Syndicate HQ exit failed safely: {ex.Message}"); _machine.Fault(); return false; }
    }

    /// <summary>
    /// OC-136: the teleport-out safety net. While Inside, a player more
    /// than 25 m (horizontal) from the exit marker, or more than 3 m
    /// below it, for 2 s straight has left without the exit (a teleport
    /// or transport). Neither threshold is reachable inside the 14 by
    /// 10 m room, and the 2 s hold ignores position spikes. Never runs
    /// while entering, exiting, rebuilding, tearing down, or unreadable.
    /// It runs at most every 0.25 s, and never reads IsCreated (a full
    /// interior validation): the marker read already fails while the
    /// interior is gone or rebuilding.
    /// </summary>
    private const float ReconcileAwayHorizontalMeters = 25f;
    private const float ReconcileBelowRoomMeters = 3f;
    private const float ReconcileAwaySeconds = 2f;
    private const float ReconcileCheckSeconds = 0.25f;

    private void CheckReconcileLeftWithoutExit()
    {
        if (!IsInside || _teardownBlocked || !_runtimePrepared || _interior.IsDestroyPending)
        {
            _reconcileAwaySince = -1f;
            return;
        }
        var now = _now();
        if (now < _reconcileNextCheck)
            return;
        _reconcileNextCheck = now + ReconcileCheckSeconds;
        if (!_interior.TryGetMarker(SyndicateHqContract.ExitMarker, out var exitMarker) ||
            !_transport.TryReadPosition(out var playerPosition) ||
            !exitMarker.IsFinite || !playerPosition.IsFinite)
        {
            _reconcileAwaySince = -1f;
            return;
        }
        var awayX = playerPosition.X - exitMarker.X;
        var awayZ = playerPosition.Z - exitMarker.Z;
        var horizontalMeters = MathF.Sqrt(awayX * awayX + awayZ * awayZ);
        var belowRoom = playerPosition.Y < exitMarker.Y - ReconcileBelowRoomMeters;
        if (horizontalMeters <= ReconcileAwayHorizontalMeters && !belowRoom)
        {
            _reconcileAwaySince = -1f;
            return;
        }
        if (_reconcileAwaySince < 0f)
        {
            _reconcileAwaySince = now;
            return;
        }
        if (now - _reconcileAwaySince < ReconcileAwaySeconds)
            return;
        _reconcileAwaySince = -1f;
        ReconcileLeftWithoutExit();
    }

    private void ReconcileLeftWithoutExit()
    {
        if (!_machine.BeginExit())
            return;
        _machine.CompleteExit(true);
        _returnState = null;
        _returnedForTeardown = false;
        _exitPromptArmed = false;
        ClearPrompt();
        _log("Syndicate HQ: the player left without the exit (a teleport or transport); reconciled to outside.");
    }

    public bool TryTeardown()
    {
        if (_disposed) return true;
        try
        {
            if (_returnState is not null && !_returnedForTeardown && (_machine.State is SyndicateHqEntryState.Inside or SyndicateHqEntryState.Entering or SyndicateHqEntryState.Exiting or SyndicateHqEntryState.Faulted || _teardownBlocked) && !TryReturnForTeardown())
            {
                _teardownBlocked = true;
                _recoveryRequiresFreshAccessPoint = true;
                _machine.Fault();
                return false;
            }
            var cleanupSucceeded = true;
            try { cleanupSucceeded &= _doorObserver.Detach(); } catch (Exception ex) { cleanupSucceeded = false; _log($"Syndicate HQ listener teardown remained retryable: {ex.Message}"); }
            try { _interior.Destroy(); } catch (Exception ex) { cleanupSucceeded = false; _log($"Syndicate HQ interior teardown remained retryable: {ex.Message}"); }
            cleanupSucceeded &= _doorObserver.SubscriptionCount == 0 && !_interior.IsCreated;
            if (!cleanupSucceeded) { _teardownBlocked = true; _machine.Fault(); return false; }
            _returnState = null;
            _returnedForTeardown = false;
            _recoveryRequiresFreshAccessPoint = false;
            _runtimePrepared = false;
            _preparedContext = null;
            _retainedForSave = false;
            _saveInProgress = false;
            _teardownBlocked = false;
            ClearPrompt();
            _machine.Reset();
            return true;
        }
        catch (Exception ex) { _teardownBlocked = true; _machine.Fault(); _log($"Syndicate HQ teardown remained retryable: {ex.Message}"); return false; }
    }

    private bool TryReturnForTeardown()
    {
        if (_context.TryRead(out var context) != Release1StoryHostContextReadStatus.Ready) return false;
        SyndicateHqReturnPlan plan;
        if (_recoveryRequiresFreshAccessPoint)
        {
            if (!_doorObserver.TryResolveFresh(out var freshDoor)) return false;
            plan = SyndicateHqReturnFallbackPlanner.PlanFreshAccessPoint(_returnState, context.SessionEpoch, context.PlayerId, freshDoor.AccessPointPosition);
        }
        else
        {
            plan = SyndicateHqReturnFallbackPlanner.Plan(_returnState, context.SessionEpoch, context.LoadEpoch, context.PlayerId);
        }
        if (plan.Path == SyndicateHqReturnPath.NoSafeReturn) return false;
        if (_machine.State == SyndicateHqEntryState.Inside && !_machine.BeginExit()) return false;
        if (_machine.State == SyndicateHqEntryState.Faulted && !_machine.BeginRecoveryExit()) return false;
        try
        {
            if (!_transport.TryTeleport(plan.Position)) return false;
            var completed = _machine.CompleteExit(true);
            if (completed) _returnedForTeardown = true;
            return completed;
        }
        catch (Exception ex) { _log($"Syndicate HQ safe return failed: {ex.Message}"); return false; }
    }

    private bool TeardownAtBoundary(bool loadBoundary = true)
    {
        // Invalidate before cleanup, including a blocked cleanup attempt.
        _preparedContext = null;
        _retainedForSave = false;
        if (loadBoundary) { _retentionLogContext = null; _retentionFailureLogged = false; _saveInProgress = false; }
        _saveRecoveryPending = false;
        var succeeded = TryTeardown();
        if (!succeeded) _recoveryRequiresFreshAccessPoint = true;
        return succeeded;
    }

    private void FailActivationCleanup()
    {
        _runtimePrepared = false;
        var detached = false;
        try { detached = _doorObserver.Detach(); }
        catch (Exception ex) { _log($"Syndicate HQ activation cleanup remained retryable: {ex.Message}"); }
        _teardownBlocked = !detached || _doorObserver.SubscriptionCount != 0;
        _machine.Fault();
    }

    private bool IsCurrentPrompt() => _prompt.Kind == SyndicateHqPromptKind.Enter && _promptFingerprint is not null && _promptContext is not null && _doorObserver.IsCurrent(_promptFingerprint, _promptGeneration);
    private void ShowPrompt(SyndicateHqPrompt prompt, float seconds, long generation, SyndicateHqDoorFingerprint? fingerprint, Release1StoryHostContextSnapshot context) { _prompt = prompt; _promptExpiresAt = _now() + seconds; _promptGeneration = generation; _promptFingerprint = fingerprint; _promptContext = context; }
    private void ClearPrompt() { _prompt = new(SyndicateHqPromptKind.None, string.Empty, string.Empty); _promptFingerprint = null; _promptContext = null; _promptGeneration = 0; }
    private static bool SameContext(Release1StoryHostContextSnapshot left, Release1StoryHostContextSnapshot right) => left.SessionEpoch == right.SessionEpoch && left.LoadEpoch == right.LoadEpoch && string.Equals(left.PlayerId, right.PlayerId, StringComparison.Ordinal) && string.Equals(left.ActiveSaveFolder, right.ActiveSaveFolder, StringComparison.OrdinalIgnoreCase);
}

public sealed class SyndicateHqNativePlayerTransport : ISyndicateHqPlayerTransport
{
    private readonly Action<string> _log;
    private Player? _player;

    public SyndicateHqNativePlayerTransport(Action<string>? log = null) =>
        _log = log ?? (_ => { });

    public bool TryCapture(SyndicateHqDoorFingerprint door, Release1StoryHostContextSnapshot context, out SyndicateHqExteriorReturnState state)
    {
        state = default!;
        _player = FindCanonicalPlayer(context.PlayerId);
        if (!UnityLife.IsAlive(_player))
        {
            _log("Syndicate HQ transport could not resolve the canonical player.");
            return false;
        }
        var transform = _player!.transform;
        if (!UnityLife.IsAlive(transform)) { _player = null; return false; }
        state = new(ToValue(transform.position), door.AccessPointPosition, context.SessionEpoch, context.LoadEpoch, context.PlayerId);
        return true;
    }
    public bool TryTeleport(SyndicateHqVector3 position)
    {
        try
        {
            if (_player is null)
            {
                _log("Syndicate HQ transport had no captured canonical player.");
                return false;
            }
            if (!PlayerSingleton<PlayerMovement>.InstanceExists)
            {
                _log("Syndicate HQ transport could not resolve PlayerSingleton<PlayerMovement>.");
                return false;
            }
            var movement = PlayerSingleton<PlayerMovement>.Instance;
            movement.Teleport(ToUnity(position), true);
            Physics.SyncTransforms();
            return true;
        }
        catch (Exception ex)
        {
            _log($"Syndicate HQ native teleport failed: {ex.Message}");
            return false;
        }
    }
    public bool TryReadPosition(out SyndicateHqVector3 position)
    {
        position = default;
        if (!UnityLife.IsAlive(_player)) { _player = null; return false; }
        var transform = _player!.transform;
        if (!UnityLife.IsAlive(transform)) { _player = null; return false; }
        position = ToValue(transform.position);
        return position.IsFinite;
    }
    private static Player? FindCanonicalPlayer(string playerId)
    {
        try
        {
            var players = Player.PlayerList;
            if (players is null || players.Pointer == IntPtr.Zero) return null;
            Player? found = null;
            foreach (var player in players)
            {
                if (UnityLife.IsAlive(player) && player!.IsServerInitialized && player.Connection is not null && string.Equals(player.PlayerCode?.Trim(), playerId, StringComparison.Ordinal))
                {
                    if (found is not null) return null;
                    found = player;
                }
            }
            return found;
        }
        catch { return null; }
    }
    private static SyndicateHqVector3 ToValue(Vector3 value) => new(value.x, value.y, value.z);
    private static Vector3 ToUnity(SyndicateHqVector3 value) => new(value.X, value.Y, value.Z);
}
