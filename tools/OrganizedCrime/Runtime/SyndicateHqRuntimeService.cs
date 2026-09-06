using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.DevUtilities;
using OrganizedCrime.Model;
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
    bool TryEnsure(SyndicateHqVector3 exteriorAnchor);
    bool TryGetMarker(string name, out SyndicateHqVector3 position);
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
    private bool _disposed;

    public SyndicateHqRuntimeService(IRelease1StoryHostContext context, ISyndicateHqUnlockAuthority authority, ISyndicateHqDoorSource doorObserver, ISyndicateHqInterior interior, ISyndicateHqPlayerTransport transport, Action<string>? log = null, Func<float>? now = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _doorObserver = doorObserver ?? throw new ArgumentNullException(nameof(doorObserver));
        _interior = interior ?? throw new ArgumentNullException(nameof(interior));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _log = log ?? (_ => { });
        _now = now ?? (() => Time.unscaledTime);
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
    /// runtime is not yet prepared (every save-start boundary tears the runtime down first), and
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
            _machine.MarkReady();
            _runtimePrepared = true;
            _teardownBlocked = false;
            return true;
        }
        catch (Exception ex) { _log($"Syndicate HQ load activation failed: {ex.Message}"); FailActivationCleanup(); return false; }
    }

    public bool OnPreLoad() => TeardownAtBoundary();
    public bool OnPreSceneChange() => TeardownAtBoundary();
    public bool OnSaveStart() => TeardownAtBoundary();
    public bool OnSaveComplete()
    {
        if (_disposed) return false;
        if (_teardownBlocked && !TryTeardown()) return false;
        return _runtimePrepared || OnLoadComplete();
    }

    public void Update()
    {
        if (_disposed) return;
        try
        {
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
        if (_disposed || _teardownBlocked || !_runtimePrepared || !_doorObserver.IsCurrent(fingerprint, generation) || _machine.State is SyndicateHqEntryState.Inside or SyndicateHqEntryState.Entering or SyndicateHqEntryState.Exiting or SyndicateHqEntryState.Faulted) return;
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
            if (_disposed || _teardownBlocked || !_runtimePrepared || _machine.State != SyndicateHqEntryState.EnterPrompt || !IsCurrentPrompt() || !_authority.Evaluate().IsUnlocked) return false;
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

    private bool TeardownAtBoundary()
    {
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
        if (_player is null)
        {
            _log("Syndicate HQ transport could not resolve the canonical player.");
            return false;
        }
        state = new(ToValue(_player.transform.position), door.AccessPointPosition, context.SessionEpoch, context.LoadEpoch, context.PlayerId);
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
    public bool TryReadPosition(out SyndicateHqVector3 position) { if (_player is null) { position = default; return false; } position = ToValue(_player.transform.position); return true; }
    private static Player? FindCanonicalPlayer(string playerId)
    {
        try
        {
            var players = Player.PlayerList;
            if (players is null) return null;
            Player? found = null;
            foreach (var player in players)
            {
                if (player is not null && player.IsServerInitialized && player.Connection is not null && string.Equals(player.PlayerCode?.Trim(), playerId, StringComparison.Ordinal))
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
