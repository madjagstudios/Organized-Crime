using System.Collections;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppFishNet;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.PlayerScripts;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace OrganizedCrime.PropertyProbe.Probes;

/// <summary>
/// Captures the loaded Nightclub shell and listens only to exact validated door interaction starts for one owner run.
/// This class deliberately has no interaction, Harmony, scene, save, or ownership writes.
/// </summary>
internal sealed class NightclubTimingDiagnosticProbe
{
    private readonly List<NightclubProbeObservation> _observations = new();
    private IReadOnlyList<StaticDoor> _candidateDoors = Array.Empty<StaticDoor>();
    private IReadOnlyDictionary<string, NightclubDoorFingerprint> _candidateFingerprints =
        new Dictionary<string, NightclubDoorFingerprint>(StringComparer.Ordinal);
    private NightclubDoorFingerprint? _selectedDoor;
    private NightclubVanillaFingerprint _vanillaBefore = UnavailableVanilla();
    private string _invariantBefore = string.Empty;
    private string _runId = string.Empty;
    private string _sceneName = string.Empty;
    private NightclubEventObserverLedger? _eventObserver;
    private NightclubListenerRegistry? _listenerRegistry;
    private float _startedAt;
    private bool _active;
    private bool _consumed;
    private bool _menuWasOpen;
    private bool _menuEverOpened;
    private bool _menuSurfaceObserved;
    private bool _sceneChanged;
    private bool _exceptionObserved;
    private bool _exactShellValidated;
    private string? _failureReason;
    private NightclubProbeAuthority _authorityAtFinish = NightclubProbeAuthority.Host;

    public void Start()
    {
        if (_consumed)
        {
            ProbeLog.Warn("Nightclub timing diagnostic ignored because this process already consumed its one-run guard.");
            return;
        }

        _consumed = true;
        var runtime = ReadAuthority();
        if (!NightclubProbeContract.TryAdmitTrigger(
                f5Pressed: true,
                hostCount: runtime.HostCount,
                serverStarted: runtime.ServerStarted,
                clientStarted: runtime.ClientStarted,
                alreadyRan: false,
                out var admission))
        {
            ProbeLog.Warn($"Nightclub timing diagnostic stopped: {admission.Reason}");
            WriteEvidence(new NightclubProbeEvidence(
                RunId: "not-admitted",
                SceneName: SceneManager.GetActiveScene().name,
                Authority: runtime.HostCount == 1 ? NightclubProbeAuthority.Host : NightclubProbeAuthority.Unknown,
                DoorCandidates: Array.Empty<NightclubDoorFingerprint>(),
                SelectedDoor: null,
                VanillaBefore: UnavailableVanilla(),
                VanillaAfter: UnavailableVanilla(),
                Observations: Array.Empty<NightclubProbeObservation>(),
                MenuOutcome: NightclubMenuOutcome.Unknown,
                MenuSurfaceObserved: false,
                DuplicateInteractionTested: false,
                TeardownObserved: true,
                MutationAttempted: false,
                HarmonyUsed: false,
                SceneChanged: false,
                ExceptionObserved: false,
                ExactShellValidated: false,
                VanillaInvariantsUnchanged: true,
                DurationSeconds: 0,
                FailureReason: admission.Reason));
            return;
        }

        _active = true;
        _runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        _sceneName = SceneManager.GetActiveScene().name;
        _startedAt = Time.unscaledTime;

        try
        {
            _candidateDoors = FindNightclubDoors();
            _exactShellValidated = ValidateExactShell(_candidateDoors);
            _vanillaBefore = CaptureVanillaSafe(_candidateDoors, selectedDoor: null);
            _invariantBefore = CaptureInvariantSafe(_candidateDoors);
            var initialMenus = FindNpcSummonMenus();
            _menuSurfaceObserved = initialMenus.Count > 0;
            _menuWasOpen = initialMenus.Any(IsMenuOpen);
            AddObservation(NightclubProbeEventKind.OwnerTriggered, "<owner>", "F5 admitted on one authoritative single-player host.");

            if (!_exactShellValidated)
            {
                _failureReason = "The approved Nightclub shell was absent or duplicated; the run stopped fail-closed.";
                Finish(NightclubListenerTermination.Stop);
                return;
            }

            var fingerprints = _candidateDoors.Select(CaptureDoorSafe).ToArray();
            if (fingerprints.Any(fingerprint => fingerprint.StableIdentity == "<unavailable>") ||
                fingerprints.GroupBy(fingerprint => fingerprint.StableIdentity, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                _failureReason = "Exact Nightclub door identities could not be captured uniquely; listener setup stopped fail-closed.";
                Finish(NightclubListenerTermination.Stop);
                return;
            }

            _candidateFingerprints = fingerprints.ToDictionary(fingerprint => fingerprint.StableIdentity, StringComparer.Ordinal);
            _eventObserver = new NightclubEventObserverLedger(fingerprints);
            _listenerRegistry = new NightclubListenerRegistry(fingerprints);
            SubscribeExactDoorListeners();

            ProbeLog.Info(
                $"Nightclub timing diagnostic admitted. {_candidateDoors.Count} exact-shell StaticDoor candidate(s) found; " +
                $"listening to exact onInteractStart events with a {NightclubEventObserverLedger.DebounceSeconds:0.##}-second per-door debounce; " +
                "manually interact with the door twice, then wait for the bounded window to finish.");
        }
        catch (Exception ex)
        {
            RecordException(ex);
            Finish(NightclubListenerTermination.Exception);
        }
    }

    public void Tick()
    {
        if (!_active)
            return;

        try
        {
            var activeAuthority = ReadAuthority();
            var authorityAssessment = NightclubProbeContract.EvaluateActiveAuthority(
                activeAuthority.HostCount,
                activeAuthority.ServerStarted,
                activeAuthority.ClientStarted);
            if (authorityAssessment.Decision == NightclubActiveWindowDecision.Stop)
            {
                _authorityAtFinish = NightclubProbeAuthority.Unknown;
                _failureReason = authorityAssessment.Reason;
                Finish(NightclubListenerTermination.AuthorityDrift);
                return;
            }

            if (!string.Equals(SceneManager.GetActiveScene().name, _sceneName, StringComparison.Ordinal))
            {
                _sceneChanged = true;
                _failureReason = "The active scene changed during the bounded observation window.";
                Finish(NightclubListenerTermination.SceneChanged);
                return;
            }

            PollMenu();

            if (ElapsedSeconds() >= NightclubProbeContract.MaximumRunSeconds)
            {
                AddObservation(NightclubProbeEventKind.WindowElapsed, "<window>", "The bounded twelve-second observation window elapsed.");
                Finish(NightclubListenerTermination.NormalCompletion);
            }
        }
        catch (Exception ex)
        {
            RecordException(ex);
            Finish(NightclubListenerTermination.Exception);
        }
    }

    public void Dispose(NightclubListenerTermination termination = NightclubListenerTermination.Disposal)
    {
        if (_active)
        {
            _failureReason = "The process ended before the bounded observation window completed.";
            Finish(termination);
            return;
        }

        RetryPendingDoorListeners(termination);
    }

    private void SubscribeExactDoorListeners()
    {
        foreach (var door in _candidateDoors)
        {
            if (IsUnityNull(door))
                throw new InvalidOperationException("An exact candidate door was destroyed before listener registration.");

            var interactable = door.IntObj;
            if (IsUnityNull(interactable))
                throw new InvalidOperationException("An exact candidate door lost its InteractableObject before listener registration.");

            var fingerprint = CaptureDoorSafe(door);
            if (!_candidateFingerprints.ContainsKey(fingerprint.StableIdentity))
                throw new InvalidOperationException("An exact candidate door identity changed before listener registration.");

            var stableIdentity = fingerprint.StableIdentity;
            var listener = DelegateSupport.ConvertDelegate<UnityAction>(
                new Action(() => RecordDoorInteraction(stableIdentity)));
            if (listener is null)
                throw new InvalidOperationException("The exact-door UnityAction delegate could not be created.");
            interactable!.onInteractStart.AddListener(listener);
            var registration = new RegisteredDoorListener(stableIdentity, interactable, listener);
            if (!_listenerRegistry!.MarkRegistered(registration))
            {
                registration.TryRemove();
                throw new InvalidOperationException("A duplicate or untracked exact-door listener registration was rejected.");
            }
        }
    }

    private void RecordDoorInteraction(string stableIdentity)
    {
        if (!_active || _eventObserver is null || !_candidateFingerprints.TryGetValue(stableIdentity, out var fingerprint))
            return;

        var result = _eventObserver.Record(stableIdentity, ElapsedSeconds());
        if (result.Receipt is null)
            return;

        if (result.Disposition == NightclubEventReceiptDisposition.Accepted)
            _selectedDoor ??= fingerprint;

        var receipt = result.Receipt;
        AddObservation(
            result.Disposition == NightclubEventReceiptDisposition.Accepted
                ? NightclubProbeEventKind.DoorInteractionObserved
                : NightclubProbeEventKind.DuplicateInteractionObserved,
            receipt.DoorPath,
            $"Exact candidate onInteractStart observed; ordinal={receipt.Ordinal}; duplicateCount={receipt.DuplicateCount}; stableIdentity={receipt.DoorStableIdentity}",
            receipt.TimestampSeconds);
    }

    private void PollMenu()
    {
        var menus = FindNpcSummonMenus();
        _menuSurfaceObserved |= menus.Count > 0;
        var menuOpen = menus.Any(IsMenuOpen);
        if (menuOpen && !_menuWasOpen)
        {
            _menuEverOpened = true;
            AddObservation(NightclubProbeEventKind.MenuOpenedObserved, FindMenuPath(), "NPCSummonMenu open state became observable.");
        }
        else if (!menuOpen && _menuWasOpen)
        {
            AddObservation(NightclubProbeEventKind.MenuClosedObserved, FindMenuPath(), "NPCSummonMenu closed state became observable.");
        }

        _menuWasOpen = menuOpen;
    }

    private void Finish(NightclubListenerTermination requestedTermination)
    {
        if (!_active)
            return;

        var duration = ElapsedSeconds();
        var menuOutcome = _menuEverOpened
            ? (_menuWasOpen ? NightclubMenuOutcome.Opened : NightclubMenuOutcome.OpenedAndClosed)
            : NightclubMenuOutcome.NotObserved;
        var after = CaptureVanillaSafe(_candidateDoors, _selectedDoor);
        var evidence = new NightclubProbeEvidence(
            RunId: _runId,
            SceneName: _sceneName,
            Authority: _authorityAtFinish,
            DoorCandidates: _candidateDoors.Select(CaptureDoorSafe).ToArray(),
            SelectedDoor: _selectedDoor,
            VanillaBefore: _vanillaBefore,
            VanillaAfter: after,
            Observations: _observations.ToArray(),
            MenuOutcome: menuOutcome,
            MenuSurfaceObserved: _menuSurfaceObserved,
            DuplicateInteractionTested: _eventObserver?.AcceptedInteractionCount >= 2,
            TeardownObserved: false,
            MutationAttempted: false,
            HarmonyUsed: false,
            SceneChanged: _sceneChanged,
            ExceptionObserved: _exceptionObserved,
            ExactShellValidated: _exactShellValidated,
            VanillaInvariantsUnchanged: _exactShellValidated &&
                string.Equals(_invariantBefore, CaptureInvariantSafe(_candidateDoors), StringComparison.Ordinal),
            DurationSeconds: duration,
            FailureReason: _failureReason);

        _active = false;
        var evaluatedTermination = requestedTermination == NightclubListenerTermination.NormalCompletion
            ? NightclubProbeEvaluator.Evaluate(evidence).Decision switch
            {
                NightclubProbeDecision.Stop => NightclubListenerTermination.Stop,
                NightclubProbeDecision.Inconclusive => NightclubListenerTermination.Inconclusive,
                _ => NightclubListenerTermination.NormalCompletion
            }
            : requestedTermination;
        var cleanup = RemovePendingDoorListeners(evaluatedTermination);
        var teardownSucceeded = cleanup?.TeardownSucceeded ?? true;
        if (teardownSucceeded)
        {
            AddObservation(NightclubProbeEventKind.TeardownObserved, "<diagnostic>",
                $"Exact-door listener teardown completed for {evaluatedTermination}; no door, menu, world, or save method was invoked.");
        }
        else
        {
            _exceptionObserved = true;
            _failureReason ??= "STOP: exact-door listener teardown was incomplete; failed registrations remain pending for disposal retry.";
            AddObservation(NightclubProbeEventKind.TeardownObserved, "<diagnostic>",
                $"Exact-door listener teardown is incomplete for {evaluatedTermination}; no successful teardown is claimed and failed registrations remain pending.");
        }
        evidence = evidence with
        {
            Observations = _observations.ToArray(),
            ExceptionObserved = _exceptionObserved,
            TeardownObserved = teardownSucceeded,
            FailureReason = _failureReason
        };
        WriteEvidence(evidence);
    }

    private NightclubListenerCleanupResult? RemovePendingDoorListeners(NightclubListenerTermination termination)
    {
        var cleanup = _listenerRegistry?.RemovePending(termination);
        if (cleanup is null)
            return null;

        foreach (var failedRemoval in cleanup.FailedRemovals)
        {
            ProbeLog.Error(
                $"Nightclub timing diagnostic listener removal incomplete for {failedRemoval.DoorStableIdentity}: " +
                $"{failedRemoval.Disposition}; {failedRemoval.Detail ?? "no detail"}");
        }

        return cleanup;
    }

    private void RetryPendingDoorListeners(NightclubListenerTermination termination)
    {
        var cleanup = RemovePendingDoorListeners(termination);
        if (cleanup is { TeardownSucceeded: false })
            ProbeLog.Error("Nightclub timing diagnostic disposal retry left exact-door listener registrations pending.");
    }

    private void WriteEvidence(NightclubProbeEvidence evidence)
    {
        var assessment = NightclubProbeEvaluator.Evaluate(evidence);
        ProbeLog.WriteFile("nightclub-door-timing.txt", NightclubTimingFormatter.FormatText(evidence, assessment));
        ProbeLog.WriteFile("nightclub-door-timing.json", NightclubTimingFormatter.FormatJson(evidence, assessment));
        ProbeLog.Info($"Nightclub timing diagnostic completed with decision {assessment.Decision}.");
    }

    private void RecordException(Exception exception)
    {
        _exceptionObserved = true;
        _failureReason = exception.GetType().Name + ": " + exception.Message;
        ProbeLog.Error($"Nightclub timing diagnostic observation failed: {exception}");
    }

    private void AddObservation(NightclubProbeEventKind kind, string subjectPath, string detail, float? timestampSeconds = null)
    {
        _observations.Add(new NightclubProbeObservation(
            Sequence: _observations.Count + 1,
            EventKind: kind,
            TimestampSeconds: timestampSeconds ?? ElapsedSeconds(),
            SubjectPath: subjectPath,
            Detail: detail));
    }

    private float ElapsedSeconds() => Math.Max(0f, Time.unscaledTime - _startedAt);

    private static IReadOnlyList<StaticDoor> FindNightclubDoors()
    {
        var doors = Resources.FindObjectsOfTypeAll<StaticDoor>() ?? Array.Empty<StaticDoor>();
        return doors
            .Where(door => !IsUnityNull(door) && door.gameObject.scene.IsValid())
            .Where(door => GetTransformPath(door.transform).StartsWith(
                NightclubProbeContract.ExpectedShellPath + "/", StringComparison.Ordinal))
            .OrderBy(door => GetTransformPath(door.transform), StringComparer.Ordinal)
            .ThenBy(door => door.GetInstanceID())
            .ToArray();
    }

    private static bool ValidateExactShell(IReadOnlyList<StaticDoor> doors)
    {
        var shellRoots = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(gameObject => !IsUnityNull(gameObject) && gameObject.scene.IsValid())
            .Where(gameObject => NightclubProbeContract.IsExpectedShellPath(GetTransformPath(gameObject.transform)))
            .ToArray();
        return shellRoots.Length == 1 && doors.Count > 0;
    }

    private static NightclubDoorFingerprint CaptureDoor(StaticDoor door)
    {
        var interactable = GetMemberValue(door, "IntObj") as Component;
        var building = GetMemberValue(door, "Building") as Component;
        var accessPoint = GetMemberValue(door, "AccessPoint") as Component;
        var path = GetTransformPath(door.transform);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "IntObj", "Building", "AccessPoint", "CanKnock", "Usable", "doorIndex" })
            fields[name] = GetMemberStatus(door, name);

        return new NightclubDoorFingerprint(
            HierarchyPath: path,
            RuntimeType: TypeName(door),
            InstanceId: door.GetInstanceID(),
            ActiveInHierarchy: door.gameObject.activeInHierarchy,
            Position: ToDto(door.transform.position),
            Forward: ToDto(door.transform.forward),
            InteractablePath: GetTransformPath(GetTransform(interactable)),
            InteractableType: TypeName(interactable),
            BuildingPath: GetTransformPath(GetTransform(building)),
            BuildingType: TypeName(building),
            BuildingName: ReadMemberText(building, "BuildingName"),
            BuildingGuid: ReadMemberText(building, "GUID"),
            AccessPointPath: GetTransformPath(GetTransform(accessPoint)),
            AccessPointPosition: IsUnityNull(accessPoint) ? null : ToDto(accessPoint!.transform.position),
            ComponentTypes: CaptureComponentTypes(door.gameObject),
            FieldAvailability: fields,
            StableIdentity: path + "|instance:" + door.GetInstanceID());
    }

    private static NightclubDoorFingerprint CaptureDoorSafe(StaticDoor door)
    {
        try
        {
            return CaptureDoor(door);
        }
        catch
        {
            return NightclubDoorFingerprint.Test("<unavailable>") with
            {
                RuntimeType = TypeName(door),
                InstanceId = -1,
                ActiveInHierarchy = false,
                Position = new Vector3Dto(0, 0, 0),
                Forward = new Vector3Dto(0, 0, 0),
                InteractablePath = "<unavailable>",
                InteractableType = "<unavailable>",
                BuildingPath = "<unavailable>",
                BuildingType = "<unavailable>",
                BuildingName = "<unavailable>",
                BuildingGuid = "<unavailable>",
                AccessPointPath = "<unavailable>",
                AccessPointPosition = null,
                ComponentTypes = Array.Empty<string>(),
                FieldAvailability = new Dictionary<string, string>(StringComparer.Ordinal),
                StableIdentity = "<unavailable>"
            };
        }
    }

    private static NightclubVanillaFingerprint CaptureVanilla(
        IReadOnlyList<StaticDoor> doors,
        NightclubDoorFingerprint? selectedDoor)
    {
        var doorState = string.Join(" || ", doors.Select(door =>
        {
            var fingerprint = CaptureDoorSafe(door);
            return fingerprint.StableIdentity + ";active=" + fingerprint.ActiveInHierarchy +
                ";position=" + fingerprint.Position +
                ";usable=" + ReadMemberText(door, "Usable") +
                ";canKnock=" + ReadMemberText(door, "CanKnock") +
                ";doorIndex=" + ReadMemberText(door, "doorIndex");
        }));
        var interactableState = string.Join(" || ", doors.Select(door =>
        {
            var interactable = GetMemberValue(door, "IntObj");
            return GetTransformPath(GetTransform(interactable as Component)) +
                ";type=" + TypeName(interactable) +
                ";interactionState=" + ReadMemberText(interactable, "__interactionState") +
                ";interactionType=" + ReadMemberText(interactable, "__interactionType");
        }));
        var buildingState = string.Join(" || ", doors
            .Select(door => GetMemberValue(door, "Building") as Component)
            .Where(building => !IsUnityNull(building))
            .Distinct()
            .Select(building => GetTransformPath(GetTransform(building)) +
                ";name=" + ReadMemberText(building, "BuildingName") +
                ";guid=" + ReadMemberText(building, "GUID") +
                ";occupantCount=" + ReadMemberText(building, "OccupantCount")));
        var accessPointState = string.Join(" || ", doors.Select(door =>
        {
            var accessPoint = GetMemberValue(door, "AccessPoint") as Component;
            return GetTransformPath(GetTransform(accessPoint)) +
                ";position=" + (IsUnityNull(accessPoint) ? "<null>" : ToDto(accessPoint!.transform.position).ToString());
        }));

        var menuState = string.Join(" || ", FindNpcSummonMenus().Select(menu =>
            GetTransformPath(menu.transform) + ";open=" + IsMenuOpen(menu) + ";state=" + ReadMemberText(menu, "State")));
        var npcState = string.Join(" || ", doors
            .Select(door => GetMemberValue(GetMemberValue(door, "Building"), "Occupants"))
            .Select(DescribeEnumerable));

        return new NightclubVanillaFingerprint(
            DoorState: doorState,
            InteractableState: interactableState,
            BuildingState: buildingState,
            AccessPointState: accessPointState,
            MenuState: string.IsNullOrEmpty(menuState) ? "<menu-instance-not-found>" : menuState,
            NpcState: npcState);
    }

    private static NightclubVanillaFingerprint CaptureVanillaSafe(
        IReadOnlyList<StaticDoor> doors,
        NightclubDoorFingerprint? selectedDoor)
    {
        try
        {
            return CaptureVanilla(doors, selectedDoor);
        }
        catch
        {
            return UnavailableVanilla();
        }
    }

    private static string CaptureInvariant(IReadOnlyList<StaticDoor> doors) =>
        string.Join(" || ", doors.Select(door =>
        {
            var fingerprint = CaptureDoorSafe(door);
            var building = GetMemberValue(door, "Building") as Component;
            var accessPoint = GetMemberValue(door, "AccessPoint") as Component;
            return fingerprint.StableIdentity +
                ";interactable=" + fingerprint.InteractablePath +
                ";building=" + GetTransformPath(GetTransform(building)) + "/" + ReadMemberText(building, "GUID") +
                ";access=" + GetTransformPath(GetTransform(accessPoint)) +
                ";npc=" + DescribeEnumerable(GetMemberValue(building, "Occupants"));
        }));

    private static string CaptureInvariantSafe(IReadOnlyList<StaticDoor> doors)
    {
        try
        {
            return CaptureInvariant(doors);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static IReadOnlyList<MonoBehaviour> FindNpcSummonMenus() =>
        (Resources.FindObjectsOfTypeAll<MonoBehaviour>() ?? Array.Empty<MonoBehaviour>())
            .Where(menu => !IsUnityNull(menu) && IsType(menu, ".NPCSummonMenu"))
            .ToArray();

    private static bool IsNpcSummonMenuOpen() => FindNpcSummonMenus().Any(IsMenuOpen);

    private static bool IsMenuOpen(MonoBehaviour menu)
    {
        var state = GetMemberValue(menu, "State");
        var stateActive = TryReadBool(state, "Active", "IsActive", "IsOpen", "Open");
        if (stateActive.HasValue)
            return stateActive.Value;

        var canvas = GetMemberValue(menu, "Canvas") as Behaviour;
        return !IsUnityNull(canvas) ? canvas!.enabled : menu.gameObject.activeInHierarchy;
    }

    private static string FindMenuPath() =>
        FindNpcSummonMenus().Select(menu => GetTransformPath(menu.transform)).FirstOrDefault() ?? "<menu-instance-not-found>";

    private static RuntimeAuthority ReadAuthority()
    {
        try
        {
            var serverManager = InstanceFinder.ServerManager;
            var clientManager = InstanceFinder.ClientManager;
            if (serverManager is null || clientManager is null)
                return new(0, false, false);

            var serverStarted = serverManager.OneServerStarted();
            var clientStarted = clientManager.Started;
            if (!serverStarted || !clientStarted || Player.PlayerList is null)
                return new(0, serverStarted, clientStarted);

            var hostCount = Player.PlayerList
                .ToArray()
                .Count(player => player is not null && player.IsServerInitialized && player.Connection is not null);
            return new(hostCount, serverStarted, clientStarted);
        }
        catch
        {
            return new(0, false, false);
        }
    }

    private static string GetMemberStatus(object? target, string name)
    {
        if (target is null)
            return "target-null";

        return FindMember(target.GetType(), name) is null ? "missing" : "present";
    }

    private static object? GetMemberValue(object? target, string name)
    {
        if (target is null)
            return null;

        try
        {
            var member = FindMember(target.GetType(), name);
            return member switch
            {
                PropertyInfo property => property.GetValue(target),
                FieldInfo field => field.GetValue(target),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static MemberInfo? FindMember(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) as MemberInfo ??
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static string ReadMemberText(object? target, string name)
    {
        var value = GetMemberValue(target, name);
        return value is null ? "<null>" : value.ToString() ?? "<unprintable>";
    }

    private static bool? TryReadBool(object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetMemberValue(target, name);
            if (value is bool boolean)
                return boolean;
        }

        return null;
    }

    private static string DescribeEnumerable(object? value)
    {
        if (value is not IEnumerable enumerable)
            return value is null ? "<null>" : TypeName(value);

        var values = new List<string>();
        foreach (var item in enumerable)
        {
            if (values.Count == 100)
            {
                values.Add("<truncated>");
                break;
            }

            values.Add(TypeName(item) + ":" + ReadMemberText(item, "NpcID"));
        }

        return "[" + string.Join(",", values) + "]";
    }

    private static IReadOnlyList<string> CaptureComponentTypes(GameObject gameObject) =>
        (gameObject.GetComponents<Component>() ?? Array.Empty<Component>())
            .Where(component => !IsUnityNull(component))
            .Select(TypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

    private static bool IsUnityNull(UnityEngine.Object? value) => value == null;

    private static Transform? GetTransform(Component? component) =>
        IsUnityNull(component) ? null : component!.transform;

    private static bool IsType(object value, string suffix) =>
        (value.GetType().FullName ?? value.GetType().Name).EndsWith(suffix, StringComparison.Ordinal);

    private static string TypeName(object? value) =>
        value is null ? "<null>" : value.GetType().FullName ?? value.GetType().Name;

    private static string GetTransformPath(Transform? transform)
    {
        if (IsUnityNull(transform))
            return "<null>";

        var names = new Stack<string>();
        Transform? current = transform;
        while (!IsUnityNull(current))
        {
            names.Push(current!.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static Vector3Dto ToDto(Vector3 value) => new(value.x, value.y, value.z);

    private static NightclubVanillaFingerprint UnavailableVanilla() =>
        new("<unavailable>", "<unavailable>", "<unavailable>", "<unavailable>", "<unavailable>", "<unavailable>");

    private sealed class RegisteredDoorListener : INightclubListenerRegistration
    {
        private readonly InteractableObject _subscribedInteractable;
        private readonly UnityAction _listener;

        public RegisteredDoorListener(string doorStableIdentity, InteractableObject subscribedInteractable, UnityAction listener)
        {
            DoorStableIdentity = doorStableIdentity;
            _subscribedInteractable = subscribedInteractable;
            _listener = listener;
        }

        public string DoorStableIdentity { get; }

        public NightclubListenerRemovalAttempt TryRemove()
        {
            if (IsUnityNull(_subscribedInteractable))
            {
                return new(
                    DoorStableIdentity,
                    NightclubListenerRemovalDisposition.TargetUnavailable,
                    "The captured subscribed InteractableObject is no longer Unity-live.");
            }

            try
            {
                _subscribedInteractable.onInteractStart.RemoveListener(_listener);
                return new(DoorStableIdentity, NightclubListenerRemovalDisposition.Removed, null);
            }
            catch (Exception ex)
            {
                return new(
                    DoorStableIdentity,
                    NightclubListenerRemovalDisposition.Failed,
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private readonly record struct RuntimeAuthority(int HostCount, bool ServerStarted, bool ClientStarted);
}
