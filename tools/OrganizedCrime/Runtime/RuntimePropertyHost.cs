using Il2CppFishNet;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI.Management;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Composition;
using OrganizedCrime.Runtime.Platform;
using OrganizedCrime.Runtime.Qa;
using UnityEngine;
using UnityEngine.AI;

namespace OrganizedCrime.Runtime;

public sealed class RuntimePropertyHost : IRuntimePropertyHost, IFishWarehouseLightingOwnerQa, IFishWarehousePropertyHost
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const string IdentityJson = "{\"propertyCode\":\"oc_fishwarehouse\",\"propertyName\":\"Syndicate Warehouse\"}";

    private GameObject? _root;
    private NetworkObject? _networkObject;
    internal Property? _property;
    private PrefabObjects? _bucket;
    private NetworkManager? _networkManager;
    private ServerManager? _serverManager;
    private object? _authoredCollection;
    private Transform? _anchor;
    private FishWarehouseBuildSurfaceHost? _buildSurface;
    private FishWarehouseInteriorRoomHost? _interiorRoom;
    internal FishWarehouseLightingHost? _lighting;
    private FishWarehouseGarageJambHost? _garageJambs;
    private FishWarehouseGarageOpeningHost? _garageOpening;
    private FishWarehouseNativeNavigationHost? _navigation;
    private FishWarehouseGarageCrossingHost? _garageCrossing;
    private FishWarehouseDockDeliveryBridgeHost? _dockDeliveryBridge;
    private FishWarehouseContinuousNavigationCoordinator? _continuousNavigationCoordinator;
    private FishWarehouseContinuousNavigationState _employeeNavigationState =
        FishWarehouseContinuousNavigationState.Inactive;
    private FishWarehousePropertyBoundsHost? _propertyBounds;
    private FishWarehouseEmployeeInfrastructureHost<Transform>? _employeeInfrastructure;
    private FishWarehousePlayerSpawnHost<Transform>? _playerSpawns;
    private FishWarehouseFrontageClearanceHost? _frontageClearance;
    private bool _frontageClearanceCleanupPending;
    private FishWarehouseLoadingDockHost? _loadingDocks;
    private bool _managementDiagnosticFailureReported;
    private RectTransform? _managementWorldspaceUiContainer;
    private DocksIdentity _docksBefore = DocksIdentity.Empty;
    private RuntimePropertyDefinition? _definition;
    private bool _persistenceObjectReplayRequired;
    private bool _persistenceObjectsReplayed;
    private int? _persistenceEmployeeAgentTypeId;

    private readonly FishWarehouseLampTuningQa _lampTuning;

    public RuntimePropertyHost()
    {
        _lampTuning = new FishWarehouseLampTuningQa(this);
    }

    public Property? CurrentProperty => _property;
    public Transform? CurrentAnchor => _anchor;
    public NetworkObject? CurrentNetworkObject => _networkObject;
    public GameObject? CurrentRoot => _root;
    public bool CanRestoreDeliveries =>
        _property is not null &&
        Read(() => _property.IsOwned) &&
        _loadingDocks?.IsReady == true;
    public FishWarehouseContinuousNavigationState EmployeeNavigationState =>
        _employeeNavigationState == FishWarehouseContinuousNavigationState.Failed
            ? FishWarehouseContinuousNavigationState.Failed
            : _continuousNavigationCoordinator?.State ?? _employeeNavigationState;
    public bool EmployeeNavigationReady =>
        EmployeeNavigationState == FishWarehouseContinuousNavigationState.Ready;
    public int? EmployeeNavigationAgentTypeId =>
        EmployeeNavigationReady ? _navigation?.ResolvedEmployeeAgentTypeId : null;
    public bool PersistenceRuntimeReady =>
        UnityLife.IsAlive(_property) &&
        Read(() => _property!.IsOwned) &&
        _buildSurface?.IsReady == true &&
        RuntimePropertyDefinition.FishWarehouse.HasBuildGridGuid(_buildSurface.GridGuid);

    /// <summary>
    /// OC-117: the tier capacity (plus pending replay count) the employee
    /// infrastructure attach reads. Wired by Mod.cs over the Chapter 3
    /// service and the capture service; null means tier 1 with no
    /// pending replay.
    /// </summary>
    public IFishWarehouseEmployeeCapacitySource? EmployeeCapacitySource { get; set; }

    public bool IsEmployeeInfrastructureReady =>
        _employeeInfrastructure?.IsReady == true && UnityLife.IsAlive(_property);

    /// <summary>
    /// OC-117: the live tier raise. False when the infrastructure is not
    /// attached; a pending build applies the tier at attach instead.
    /// </summary>
    public bool TrySetEmployeeCapacity(int capacity)
    {
        var infrastructure = _employeeInfrastructure;
        if (infrastructure is null || !infrastructure.IsReady || !UnityLife.IsAlive(_property))
            return false;
        return infrastructure.TryRaiseCapacity(capacity, Log);
    }

    /// <summary>
    /// OC-117: the Property's live capacity and idle-point count, for
    /// owner QA. False when the Property is gone.
    /// </summary>
    public bool TryReadEmployeeCapacity(out int capacity, out int idlePointCount)
    {
        capacity = 0;
        idlePointCount = 0;
        var property = _property;
        if (!UnityLife.IsAlive(property))
            return false;
        try
        {
            capacity = property!.EmployeeCapacity;
            idlePointCount = property.EmployeeIdlePoints?.Length ?? 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys): moves all created idle points into a 5 x 4 block, 0.8 m
    /// apart, centred on the local player's ground point with its rows square to the player's
    /// facing, then logs every point's warehouse-local position for baking into
    /// <see cref="FishWarehouseEmployeeInfrastructureDefinition"/>. The points' staging root
    /// sits at the Property's identity, so a staging local position is a Property-local one.
    /// Employees keep the same Transform objects and walk to the new spots on their next
    /// idle.
    /// </summary>
    public bool TryMoveIdlePointsToPlayer(out string report)
    {
        const int columns = 5;
        const float spacing = 0.8f;
        var infrastructure = _employeeInfrastructure;
        var property = _property;
        if (infrastructure is null || !infrastructure.IsReady || !UnityLife.IsAlive(property))
        {
            report = "Chapter 3 QA capacity here refused: the warehouse is not attached.";
            return false;
        }
        if (!PlayerSingleton<PlayerMovement>.InstanceExists || !UnityLife.IsAlive(PlayerSingleton<PlayerMovement>.Instance))
        {
            report = "Chapter 3 QA capacity here refused: the local player movement was unavailable.";
            return false;
        }

        var points = infrastructure.CreatedIdlePoints;
        var rows = (points.Count + columns - 1) / columns;
        var player = PlayerSingleton<PlayerMovement>.Instance.transform;
        var origin = player.position;
        if (Physics.Raycast(origin + Vector3.up * 0.5f, Vector3.down, out var hit, 5f))
            origin = hit.point;
        var frame = property!.transform;
        var centre = frame.InverseTransformPoint(origin);
        var forward = frame.InverseTransformDirection(player.forward);
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        var right = new Vector3(forward.z, 0f, -forward.x);

        var text = new System.Text.StringBuilder(
            $"OC-117 idle group placed at the player (warehouse-local, {points.Count} points):");
        for (var index = 0; index < points.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var local = centre +
                right * ((column - (columns - 1) / 2f) * spacing) +
                forward * ((row - (rows - 1) / 2f) * spacing);
            points[index].localPosition = local;
            text.Append($" [{index}] ({local.x:F2}, {local.y:F2}, {local.z:F2})");
        }

        report = text.ToString();
        return true;
    }

    // Owner QA only (OwnerQaKeys): reports the runtime player spawn world positions and
    // whether the Property fields still point at them.
    public bool TryReadPlayerSpawns(out FishWarehousePlayerSpawnQaSnapshot snapshot)
    {
        snapshot = UnassignedPlayerSpawns();
        try
        {
            var property = _property;
            var spawns = _playerSpawns;
            if (!UnityLife.IsAlive(property) || spawns is null || !spawns.IsReady)
                return false;
            var exterior = property!.SpawnPoint;
            var interior = property.InteriorSpawnPoint;
            var exteriorPosition = UnityLife.IsAlive(exterior) ? exterior!.position : Vector3.zero;
            var interiorPosition = UnityLife.IsAlive(interior) ? interior!.position : Vector3.zero;
            var exteriorRetained = false;
            var interiorRetained = false;
            try
            {
                exteriorRetained = exterior == spawns.CreatedExterior;
                interiorRetained = interior == spawns.CreatedInterior;
            }
            catch
            {
            }
            snapshot = new(
                exteriorPosition.x, exteriorPosition.y, exteriorPosition.z, UnityLife.IsAlive(exterior), exteriorRetained,
                interiorPosition.x, interiorPosition.y, interiorPosition.z, UnityLife.IsAlive(interior), interiorRetained);
            return true;
        }
        catch
        {
            snapshot = UnassignedPlayerSpawns();
            return false;
        }
    }

    // Owner QA only (OwnerQaKeys): moves one player spawn point to the local player for the
    // session and reports its room-frame coordinates with yaw, so a chosen spot can be baked
    // into the definition.
    public bool TryMovePlayerSpawnToPlayer(bool exterior, out string report)
    {
        report = "Chapter 3 QA spawn here refused: the warehouse is not attached.";
        var property = _property;
        if (!UnityLife.IsAlive(property) || _playerSpawns is null || !_playerSpawns.IsReady)
            return false;
        if (!PlayerSingleton<PlayerMovement>.InstanceExists || !UnityLife.IsAlive(PlayerSingleton<PlayerMovement>.Instance))
        {
            report = "Chapter 3 QA spawn here refused: the local player movement was unavailable.";
            return false;
        }

        var spawn = exterior ? property!.SpawnPoint : property!.InteriorSpawnPoint;
        if (!UnityLife.IsAlive(spawn))
        {
            report = $"Chapter 3 QA spawn here refused: the {(exterior ? "exterior" : "interior")} spawn point is missing.";
            return false;
        }

        var player = PlayerSingleton<PlayerMovement>.Instance.transform;
        spawn!.position = player.position;
        spawn.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
        var frame = property!.transform;
        var local = frame.InverseTransformPoint(player.position);
        var forward = frame.InverseTransformDirection(player.forward);
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        var yaw = MathF.Atan2(forward.x, forward.z) * (180f / MathF.PI);
        if (yaw < 0f)
            yaw += 360f;
        var name = exterior
            ? FishWarehousePlayerSpawnDefinition.PlayerExteriorSpawn.Name
            : FishWarehousePlayerSpawnDefinition.PlayerInteriorSpawn.Name;
        report = $"OC-139 {name} moved to the player (room-frame): ({local.x:F2}, {local.y:F2}, {local.z:F2}) yaw {yaw:F1}";
        return true;
    }

    private static FishWarehousePlayerSpawnQaSnapshot UnassignedPlayerSpawns() =>
        new(0f, 0f, 0f, false, false, 0f, 0f, 0f, false, false);

    public RuntimePropertyHostResult Start(RuntimePropertyDefinition definition)
    {
        if (_root is not null)
            return RuntimePropertyHostResult.Failure("duplicate-start", "Runtime Property host already owns a root.");

        if (_frontageClearanceCleanupPending)
        {
            if (_frontageClearance is null || !_frontageClearance.Dispose(Log))
                return RuntimePropertyHostResult.Failure("pending-frontage-cleanup", "Previous Fish Warehouse frontage cleanup is still partial.");

            _frontageClearance = null;
            _frontageClearanceCleanupPending = false;
        }

        _definition = definition;
        var propertyRegistered = false;
        var networkRegistrationPassed = false;
        var spawnPassed = false;
        var stage = "preconditions";

        try
        {
            var result = StartPreconditions(definition, stage);
            if (result is not null)
                return result;

            result = StartIdentity(definition, ref stage, propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            result = StartPropertyBounds(ref stage, propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            result = StartEmployeeInfrastructure(ref stage, propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            result = StartPlayerSpawns(ref stage, propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            result = StartLoadingDocks(ref stage, propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            result = StartPropertyRegistration(definition, ref stage, ref propertyRegistered, networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            StartNetworkInitialization(ref stage);

            result = StartNetworkRegistration(definition, ref stage, propertyRegistered, ref networkRegistrationPassed, spawnPassed);
            if (result is not null)
                return result;

            return StartSpawn(ref stage, propertyRegistered, networkRegistrationPassed, ref spawnPassed);
        }
        catch (Exception ex)
        {
            return FailAndCleanup(stage, ex.ToString(), propertyRegistered, networkRegistrationPassed, spawnPassed);
        }
    }

    // StartPreconditions establishes these fields before the later steps run.
    // Nullable flow analysis cannot carry those assignments across private calls.
#pragma warning disable CS8602, CS8604
    private RuntimePropertyHostResult? StartPreconditions(RuntimePropertyDefinition definition, string stage)
    {
        var anchor = FindAnchor(definition.AnchorPath);
        if (anchor is null)
            return RuntimePropertyHostResult.Failure(stage, $"Physical anchor was not found: {definition.AnchorPath}");
        _anchor = anchor.transform;

        var docksProperty = FindProperty(DocksWarehouseCode);
        var docksNetworkObject = docksProperty is null ? null : ResolveNetworkObject(docksProperty);
        _networkManager = docksNetworkObject?.NetworkManager;
        _serverManager = InstanceFinder.ServerManager;
        _authoredCollection = _networkManager?.SpawnablePrefabs;
        if (docksProperty is null || docksNetworkObject is null || _networkManager is null)
            return RuntimePropertyHostResult.Failure(stage, "Docks Warehouse or its NetworkManager was not found.");

        if (_authoredCollection is null)
            return RuntimePropertyHostResult.Failure(stage, "Authored FishNet prefab collection was unavailable.");

        if (_serverManager is null || !Read(() => FishNetHostGate.IsStartedOrFalse(_serverManager)))
            return RuntimePropertyHostResult.Failure(stage, "A started FishNet server is required; client-only lifecycle is not enabled in this slice.");

        if (FindProperty(definition.PropertyCode) is not null)
            return RuntimePropertyHostResult.Failure(stage, $"Property code {definition.PropertyCode} is already registered.");

        if (_networkManager.GetPrefabObjects<SinglePrefabObjects>(definition.RuntimeCollectionId, false) is not null)
            return RuntimePropertyHostResult.Failure(stage, $"Runtime prefab collection {definition.RuntimeCollectionId} already exists.");

        _docksBefore = CaptureDocksIdentity(docksNetworkObject);
        _root = new GameObject(definition.RootName);
        _root.transform.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);
        _root.SetActive(false);
        _networkObject = _root.AddComponent<NetworkObject>();
        _property = _root.AddComponent<Property>();

        return null;
    }

    private RuntimePropertyHostResult? StartIdentity(RuntimePropertyDefinition definition, ref string stage, bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "identity";
        JsonUtility.FromJsonOverwrite(IdentityJson, _property);
        if (!string.Equals(_property.PropertyCode, definition.PropertyCode, StringComparison.Ordinal) ||
            !string.Equals(_property.PropertyName, definition.DisplayName, StringComparison.Ordinal))
        {
            return FailAndCleanup(stage, "Serialized Property identity did not apply.", propertyRegistered, networkRegistrationPassed, spawnPassed);
        }

        return null;
    }

    private RuntimePropertyHostResult? StartPropertyBounds(ref string stage, bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "property-bounds";
        _propertyBounds = new FishWarehousePropertyBoundsHost();
        if (!_propertyBounds.TryAttach(_property, Log))
        {
            return FailAndCleanup(
                stage,
                "Authored Property bounds could not be attached.",
                propertyRegistered,
                networkRegistrationPassed,
                spawnPassed);
        }

        return null;
    }

    private RuntimePropertyHostResult? StartEmployeeInfrastructure(ref string stage, bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "employee-infrastructure";
        _employeeInfrastructure = new FishWarehouseEmployeeInfrastructureHost<Transform>();
        var infrastructureTarget = new FishWarehousePropertyInfrastructureTarget(_property);
        if (!_employeeInfrastructure.TryAttach(infrastructureTarget, EmployeeCapacitySource, Log))
        {
            return FailAndCleanup(
                stage,
                "Employee infrastructure could not be attached.",
                propertyRegistered,
                networkRegistrationPassed,
                spawnPassed);
        }

        return null;
    }

    private RuntimePropertyHostResult? StartPlayerSpawns(ref string stage, bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "player-spawns";
        _playerSpawns = new FishWarehousePlayerSpawnHost<Transform>();
        if (!_playerSpawns.TryAttach(new FishWarehousePropertySpawnTarget(_property), LogWarning))
        {
            return FailAndCleanup(
                stage,
                "Player spawn points could not be attached.",
                propertyRegistered,
                networkRegistrationPassed,
                spawnPassed);
        }

        return null;
    }

    private RuntimePropertyHostResult? StartLoadingDocks(ref string stage, bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "loading-docks";
        _frontageClearance = new FishWarehouseFrontageClearanceHost();
        if (!_frontageClearance.TryAttach(_property, Log))
        {
            _frontageClearanceCleanupPending = !_frontageClearance.Dispose(Log);
            if (!_frontageClearanceCleanupPending)
                _frontageClearance = null;
            else
                return FailAndCleanup(stage, "Frontage clearance cleanup remained partial.", propertyRegistered, networkRegistrationPassed, spawnPassed);
            LogWarning("Fish Warehouse will continue without loading docks; frontage clearance could not be established safely.");
        }

        _loadingDocks = new FishWarehouseLoadingDockHost();
        if (_frontageClearance is null || !_loadingDocks.TryAttach(_property, Log))
        {
            _loadingDocks.Dispose();
            _loadingDocks = null;
            if (_frontageClearance is not null)
            {
                _frontageClearanceCleanupPending = !_frontageClearance.Dispose(Log);
                if (!_frontageClearanceCleanupPending)
                    _frontageClearance = null;
                else
                    return FailAndCleanup(stage, "Frontage clearance cleanup remained partial after loading dock setup failed.", propertyRegistered, networkRegistrationPassed, spawnPassed);
            }
            LogWarning("Fish Warehouse will continue without loading docks; existing Property features remain available.");
        }

        return null;
    }

    private RuntimePropertyHostResult? StartPropertyRegistration(RuntimePropertyDefinition definition, ref string stage, ref bool propertyRegistered, bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "property-registration";
        RegisterPropertyCollections(_property);
        propertyRegistered = FindProperty(definition.PropertyCode) == _property;
        if (!propertyRegistered)
            return FailAndCleanup(stage, "Temporary Property was not observable in the vanilla Property collection.", propertyRegistered, networkRegistrationPassed, spawnPassed);

        return null;
    }

    private void StartNetworkInitialization(ref string stage)
    {
        stage = "network-initialization";
        _property.InitializeSaveable();
        _root.SetActive(true);
        byte componentIndex = 0;
        _networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
        _property.NetworkInitializeIfDisabled();
    }

    private RuntimePropertyHostResult? StartNetworkRegistration(RuntimePropertyDefinition definition, ref string stage, bool propertyRegistered, ref bool networkRegistrationPassed, bool spawnPassed)
    {
        stage = "network-registration";
        _bucket = _networkManager.GetPrefabObjects<SinglePrefabObjects>(definition.RuntimeCollectionId, true);
        if (_bucket is null)
            return FailAndCleanup(stage, "Runtime prefab collection creation returned null.", propertyRegistered, networkRegistrationPassed, spawnPassed);

        var beforeCount = ReadInt(() => _bucket.GetObjectCount());
        _bucket.AddObject(_networkObject, true);
        var afterCount = ReadInt(() => _bucket.GetObjectCount());
        networkRegistrationPassed = afterCount > beforeCount;
        if (!networkRegistrationPassed)
            return FailAndCleanup(stage, "Runtime prefab registration did not increase the collection count.", propertyRegistered, networkRegistrationPassed, spawnPassed);

        return null;
    }

    private RuntimePropertyHostResult StartSpawn(ref string stage, bool propertyRegistered, bool networkRegistrationPassed, ref bool spawnPassed)
    {
        stage = "spawn";
        _serverManager.Spawn(_networkObject, null, _root.scene);
        var postSpawnNetworkObjectSpawned = Read(() => _networkObject.IsSpawned);
        var postSpawnNetworkObjectServerInitialized = Read(() => _networkObject.IsServerInitialized);
        var postSpawnPropertyNetworked = Read(() => _property.IsNetworked);
        var postSpawnPropertyClientInitialized = Read(() => _property.IsClientInitialized);
        var postSpawnPropertyServerInitialized = Read(() => _property.IsServerInitialized);
        var postSpawnPropertySpawned = Read(() => _property.IsSpawned);
        spawnPassed = postSpawnNetworkObjectSpawned &&
            postSpawnNetworkObjectServerInitialized &&
            postSpawnPropertySpawned;
        if (!spawnPassed)
        {
            var state = $"networkObjectSpawned={postSpawnNetworkObjectSpawned}, networkObjectServerInitialized={postSpawnNetworkObjectServerInitialized}, propertyNetworked={postSpawnPropertyNetworked}, propertyClientInitialized={postSpawnPropertyClientInitialized}, propertyServerInitialized={postSpawnPropertyServerInitialized}, propertySpawned={postSpawnPropertySpawned}";
            return FailAndCleanup(stage, $"Spawn returned without a spawned/server-initialized Property root ({state}).", propertyRegistered, networkRegistrationPassed, spawnPassed);
        }

        return BuildResult(true, "spawn", propertyRegistered, networkRegistrationPassed, spawnPassed, true, null);
    }
#pragma warning restore CS8602, CS8604

    public RuntimePropertyHostResult Stop()
    {
        if (_loadingDocks is not null && !_loadingDocks.CanUnload())
        {
            return new RuntimePropertyHostResult(
                Succeeded: false,
                Stage: "occupied-loading-docks",
                PropertyRegistered: true,
                NetworkRegistrationPassed: true,
                SpawnPassed: true,
                DespawnPassed: false,
                CleanupPassed: true,
                AuthoredCollectionUnchanged: true,
                DocksNetworkIdentityUnchanged: true,
                FailureReason: "A Fish Warehouse loading dock is occupied or reserved by an active delivery.");
        }

        return StopAfterNavigationCleanup();
    }

    private RuntimePropertyHostResult StopAfterNavigationCleanup()
    {
        if (_root is null && _property is null && _bucket is null && _frontageClearance is null)
        {
            return new RuntimePropertyHostResult(true, "already-stopped", false, false, false, true, true, true, true, null);
        }

        var despawnPassed = true;
        var cleanupPassed = true;
        var propertyRegistered = CleanupPropertyIsRegistered();
        if (_continuousNavigationCoordinator is not null && !_continuousNavigationCoordinator.Stop())
        {
            _employeeNavigationState = FishWarehouseContinuousNavigationState.Failed;
            return new RuntimePropertyHostResult(
                Succeeded: false,
                Stage: "pending-native-navigation-cleanup",
                PropertyRegistered: propertyRegistered,
                NetworkRegistrationPassed: _bucket is not null,
                SpawnPassed: _networkObject is not null && Read(() => _networkObject.IsSpawned),
                DespawnPassed: false,
                CleanupPassed: false,
                AuthoredCollectionUnchanged: true,
                DocksNetworkIdentityUnchanged: true,
                FailureReason: "Fish Warehouse native navigation cleanup is pending; invoke Stop again to retry before property teardown.");
        }

        try
        {
            _continuousNavigationCoordinator = null;
            _employeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;
            _garageOpening = null;
            _navigation = null;
            DisposeManagementWorldspaceUiContainer();
            _loadingDocks?.Dispose();
            _loadingDocks = null;
            _employeeInfrastructure?.Dispose();
            _employeeInfrastructure = null;
            _playerSpawns?.Dispose();
            _playerSpawns = null;
            _propertyBounds?.Dispose();
            _propertyBounds = null;
            _buildSurface?.Dispose();
            _buildSurface = null;
            _lighting?.Dispose();
            _lighting = null;
            _garageJambs?.Dispose();
            _garageJambs = null;
            _interiorRoom?.Dispose();
            _interiorRoom = null;
            if (_networkObject is not null && Read(() => _networkObject.IsSpawned))
            {
                if (_serverManager is null || _root is null)
                    throw new InvalidOperationException("ServerManager or runtime root was unavailable for despawn.");

                _serverManager.Despawn(
                    _root,
                    new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
                despawnPassed = !Read(() => _networkObject.IsSpawned);
            }
        }
        catch (Exception ex)
        {
            despawnPassed = false;
            cleanupPassed = false;
            LogWarning($"Runtime Property despawn failed: {ex}");
        }
        finally
        {
            if (_frontageClearance is not null)
            {
                try
                {
                    var frontageRestored = _frontageClearance.Dispose(Log);
                    cleanupPassed &= frontageRestored;
                    _frontageClearanceCleanupPending = !frontageRestored;
                }
                catch (Exception ex)
                {
                    cleanupPassed = false;
                    _frontageClearanceCleanupPending = true;
                    LogWarning($"Runtime Property frontage clearance restoration failed: {ex}");
                }

                if (!_frontageClearanceCleanupPending)
                    _frontageClearance = null;
            }
        }

        if (_property is not null)
        {
            try
            {
                RemovePropertyCollections(_property);
                propertyRegistered = CleanupPropertyIsRegistered();
                cleanupPassed &= !propertyRegistered;
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                LogWarning($"Runtime Property collection cleanup failed: {ex}");
            }
        }

        if (_bucket is not null && _networkManager is not null && _definition is not null)
        {
            try
            {
                cleanupPassed &= RuntimePropertyCleanup.Clean(_bucket, UnityLife.IsAlive,
                    _ => RuntimePropertyCleanup.Clean(_networkManager, UnityLife.IsAlive,
                        manager => manager.RemoveSpawnableCollection(_definition.RuntimeCollectionId)));
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                LogWarning($"Runtime Property bucket cleanup failed: {ex}");
            }
        }

        if (_root is not null)
        {
            try
            {
                cleanupPassed &= RuntimePropertyCleanup.Clean(_root, UnityLife.IsAlive, root =>
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    return true;
                });
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                LogWarning($"Runtime Property root cleanup failed: {ex}");
            }
        }

        var authoredUnchanged = _networkManager is null || _authoredCollection is null ||
            ReferenceEquals(_authoredCollection, _networkManager.SpawnablePrefabs);
        var docksUnchanged = true;
        var docksProperty = FindProperty(DocksWarehouseCode);
        if (docksProperty is not null)
        {
            var docksNetworkObject = ResolveNetworkObject(docksProperty);
            docksUnchanged = docksNetworkObject is not null && _docksBefore == CaptureDocksIdentity(docksNetworkObject);
        }

        var result = new RuntimePropertyHostResult(
            Succeeded: false,
            Stage: "cleanup",
            PropertyRegistered: !propertyRegistered,
            NetworkRegistrationPassed: _bucket is not null,
            SpawnPassed: false,
            DespawnPassed: despawnPassed,
            CleanupPassed: cleanupPassed,
            AuthoredCollectionUnchanged: authoredUnchanged,
            DocksNetworkIdentityUnchanged: docksUnchanged,
            FailureReason: cleanupPassed ? null : "Runtime Property cleanup was partial.");
        Clear();
        return result;
    }

    private bool CleanupPropertyIsRegistered() => RuntimePropertyCleanup.IsRegistered(
        _property, () => FindProperty(_definition?.PropertyCode ?? string.Empty),
        UnityLife.IsAlive, (found, property) => found == property);

    public bool TrySetOwned()
    {
        if (_property is null || _definition is null || _networkObject is null)
        {
            LogWarning("Fish Warehouse ownership gate rejected: runtime Property is not active.");
            return false;
        }

        if (!Read(() => _networkObject.IsSpawned) || !Read(() => _networkObject.IsServerInitialized))
        {
            LogWarning("Fish Warehouse ownership gate rejected: runtime Property NetworkObject is not spawned/server-initialized.");
            return false;
        }

        var managerProperty = PropertyManager.Instance.GetProperty(_definition.PropertyCode);
        var ownedBefore = SnapshotProperties(Property.OwnedProperties);
        var unownedBefore = SnapshotProperties(Property.UnownedProperties);
        var docksBefore = FindProperty(DocksWarehouseCode);
        if (managerProperty != _property || Read(() => _property.IsOwned) ||
            !unownedBefore.Contains(_property) || ownedBefore.Contains(_property) || docksBefore is null)
        {
            LogWarning("Fish Warehouse ownership gate rejected: ownership preconditions were not satisfied.");
            return false;
        }

        try
        {
            _property.SetOwned();
        }
        catch (Exception ex)
        {
            LogWarning($"Fish Warehouse Property.SetOwned() failed: {ex}");
            return false;
        }

        var ownedAfter = SnapshotProperties(Property.OwnedProperties);
        var unownedAfter = SnapshotProperties(Property.UnownedProperties);
        var managerAfter = PropertyManager.Instance.GetProperty(_definition.PropertyCode);
        var docksAfter = FindProperty(DocksWarehouseCode);
        var docksUnchanged = docksAfter is not null &&
            docksAfter == docksBefore &&
            string.Equals(docksAfter.PropertyCode, docksBefore.PropertyCode, StringComparison.OrdinalIgnoreCase) &&
            docksAfter.IsOwned == docksBefore.IsOwned;
        var ownershipPassed = Read(() => _property.IsOwned) &&
            SnapshotProperties(Property.Properties).Contains(_property) &&
            ownedAfter.Contains(_property) &&
            !unownedAfter.Contains(_property) &&
            managerAfter == _property &&
            ownedAfter.Length == ownedBefore.Length + 1 &&
            unownedAfter.Length == unownedBefore.Length - 1 &&
            docksUnchanged;

        if (ownershipPassed)
        {
            FishWarehouseManagementCanvasPatch.SetTargetProperty(_property);
            if (!EnsureManagementWorldspaceUiContainer())
                LogWarning("Fish Warehouse ownership succeeded, but its management worldspace UI container is unavailable.");

            if (_propertyBounds is null || !_propertyBounds.EnsureActive())
                LogWarning("Fish Warehouse ownership succeeded, but its authored Property bounds are unavailable.");

            _buildSurface ??= new FishWarehouseBuildSurfaceHost();
            var buildSurfaceReady = _buildSurface.TryAttach(
                _property,
                _anchor!,
                _definition.BuildGridRootName,
                _definition.BuildGridGuid,
                Log);
            if (!buildSurfaceReady)
                LogWarning("Fish Warehouse ownership succeeded, but the native build Grid was not attached.");

            if (buildSurfaceReady && _employeeInfrastructure?.IsReady == true)
            {
                _continuousNavigationCoordinator ??= new FishWarehouseContinuousNavigationCoordinator(
                    new RuntimePropertyNavigationActions(this));
                _employeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;
            }
            else
            {
                _employeeNavigationState = FishWarehouseContinuousNavigationState.Failed;
                LogWarning("Fish Warehouse ownership succeeded, but employee navigation is unavailable because the native build surface or employee infrastructure did not restore.");
            }

            OrganizedCrimeLog.Diagnostic("Fish Warehouse runtime Property is owned in memory. No direct save write was performed; a normal game save persists the Organized Crime sidecar.", Log);
        }
        else
        {
            LogWarning("Fish Warehouse ownership gate did not pass its collection or Docks Warehouse invariants. No direct save write was performed.");
        }

        return ownershipPassed;
    }

    public void UpdateOwnedFeatures()
    {
        if (!UnityLife.IsAlive(_property) || _property?.IsOwned != true || _continuousNavigationCoordinator is null ||
            (_persistenceObjectReplayRequired && !_persistenceObjectsReplayed))
            return;

        try
        {
            float now = Time.realtimeSinceStartup;
            if (_continuousNavigationCoordinator.State == FishWarehouseContinuousNavigationState.Inactive)
                _continuousNavigationCoordinator.Start(now);
            else
                _continuousNavigationCoordinator.Tick(now);

            UpdateDockDeliveryBridge();
            UpdateGarageCrossingAssist();
        }
        catch (Exception ex)
        {
            _employeeNavigationState = FishWarehouseContinuousNavigationState.Failed;
            LogWarning($"Fish Warehouse employee navigation failed safely before completion: {ex.Message}");
        }
    }

    /// <summary>
    /// Deterministically populates Organized Crime's loading dock from an arrived
    /// delivery van (OC-7), so a dock->interior route always has its source even when
    /// the native VehicleDetector -> SetOccupant chain does not fire on the cloned
    /// dock. Independent of navigation readiness (a delivery can arrive first); runs
    /// only on the authoritative host (UpdateOwnedFeatures is host-gated upstream).
    /// </summary>
    private void UpdateDockDeliveryBridge()
    {
        if (!UnityLife.IsAlive(_property))
            return;

        _dockDeliveryBridge ??= new FishWarehouseDockDeliveryBridgeHost();
        _dockDeliveryBridge.Tick(_property!, hostAuthority: true, Log);
    }

    /// <summary>
    /// Assists Organized Crime's employees across the garage off-mesh link during
    /// dock->interior routes (OC-7). Runs only once native navigation is Ready and
    /// only on the authoritative host (UpdateOwnedFeatures is host-gated upstream).
    /// </summary>
    private void UpdateGarageCrossingAssist()
    {
        if (_continuousNavigationCoordinator?.State != FishWarehouseContinuousNavigationState.Ready ||
            !UnityLife.IsAlive(_property))
        {
            return;
        }

        _garageCrossing ??= new FishWarehouseGarageCrossingHost();
        if (!_garageCrossing.IsActive)
            _garageCrossing.Activate(ComputeGarageWorldCenter());

        _garageCrossing.Tick(_property!, Time.deltaTime, hostAuthority: true);
    }

    private Vector3 ComputeGarageWorldCenter()
    {
        FishWarehouseGarageOpeningProjection? projection = _garageOpening?.Preflight?.Projection;
        if (projection is null || !UnityLife.IsAlive(_anchor))
            return UnityLife.IsAlive(_property) ? _property!.transform.position : Vector3.zero;

        FishWarehouseTransitionPrism prism = projection.TransitionPrism;
        var center = new Vector3(
            (prism.MinimumX + prism.MaximumX) / 2f,
            (prism.MinimumY + prism.MaximumY) / 2f,
            (prism.MinimumZ + prism.MaximumZ) / 2f);
        return _anchor!.TransformPoint(center);
    }

    public bool LightingActive => _interiorRoom?.IsReady == true && _lighting?.IsActive == true;

    public bool TryAddLampAbovePlayer() =>
        _lampTuning.TryAddLampAbovePlayer();

    public bool TryRemoveMostRecentLamp() =>
        _lampTuning.TryRemoveMostRecentLamp();

    public bool TryAdjustMostRecentLampHeight(float delta) =>
        _lampTuning.TryAdjustMostRecentLampHeight(delta);

    public bool TryAdjustAllLampIntensity(float delta) =>
        _lampTuning.TryAdjustAllLampIntensity(delta);

    public bool TryToggleMostRecentLampYaw() =>
        _lampTuning.TryToggleMostRecentLampYaw();

    public bool TryLogLampLayout() =>
        _lampTuning.TryLogLampLayout();

    private void TryActivateLighting()
    {
        try
        {
            if (!UnityLife.IsAlive(_property))
                return;

            _lighting ??= new FishWarehouseLightingHost();
            _lighting.TryActivate(_property!.transform, FishWarehouseLightingDefinition.Default, Log);
        }
        catch
        {
            // Lighting failure never blocks the room, ownership, or the grid.
        }
    }

    private void TryActivateGarageJambs(FishWarehouseGarageOpeningProjection projection)
    {
        try
        {
            if (!UnityLife.IsAlive(_property))
                return;

            _garageJambs ??= new FishWarehouseGarageJambHost();
            _garageJambs.TryActivate(_property!.transform, projection, _interiorRoom?.SharedMaterial, Log);
        }
        catch
        {
            // Jamb failure never blocks the room, garage, grid, or lamps.
        }
    }

    public bool TryPlaceEmployeeHome()
    {
        if (_property is null || _buildSurface is null || !_property.IsOwned)
        {
            LogWarning("Fish Warehouse employee-home placement rejected: the runtime Property is not owned or its native Grid is unavailable.");
            return false;
        }

        return _buildSurface.TryPlaceEmployeeHome(
            _property,
            FishWarehouseEmployeeHomeDefinition.Default,
            Log);
    }

    public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId)
    {
        _persistenceObjectReplayRequired = objectReplayRequired;
        _persistenceObjectsReplayed = !objectReplayRequired;
        _persistenceEmployeeAgentTypeId = employeeAgentTypeId;
    }

    public void MarkPersistenceObjectsReplayed()
    {
        _persistenceObjectsReplayed = true;
    }

    public bool TryCreateEmployeeReplayAdapter(
        PropertyData propertyData,
        out IFishWarehouseNativeEmployeeReplayAdapter adapter,
        out string? failureReason,
        IReadOnlyCollection<string>? inventoryRestoredEmployeeGuids = null)
    {
        adapter = null!;
        failureReason = null;
        if (!PersistenceRuntimeReady || (_persistenceObjectReplayRequired && !_persistenceObjectsReplayed))
        {
            failureReason = "Fish Warehouse employee replay adapter requires a ready runtime Property after object replay.";
            return false;
        }

        var employeeManager = EmployeeManager.Instance;
        if (!UnityLife.IsAlive(employeeManager))
        {
            failureReason = "Fish Warehouse employee replay adapter requires a live EmployeeManager.";
            return false;
        }

        adapter = new FishWarehouseNativeEmployeeReplayAdapter(
            propertyData,
            _property!,
            employeeManager,
            inventoryRestoredEmployeeGuids);
        return true;
    }

    public bool TryAdoptRestoredEmployeeHome()
        => AssessRestoredEmployeeHome() == FishWarehouseEmployeeHomeAdoptionResult.Adopted;

    public FishWarehouseEmployeeHomeAdoptionResult AssessRestoredEmployeeHome(
        IReadOnlyCollection<string>? savedHomeGuids = null)
    {
        if (!PersistenceRuntimeReady || (_persistenceObjectReplayRequired && !_persistenceObjectsReplayed))
        {
            LogWarning("Fish Warehouse restored employee-home adoption rejected because object replay or persistence runtime readiness was incomplete.");
            return FishWarehouseEmployeeHomeAdoptionResult.Failed;
        }

        var property = _property;
        var buildSurface = _buildSurface;
        if (!UnityLife.IsAlive(property) || buildSurface is null)
            return FishWarehouseEmployeeHomeAdoptionResult.Failed;

        var matches = new List<(BuildableItem Buildable, EmployeeHome Home)>();
        try
        {
            foreach (var employeeHome in UnityEngine.Object.FindObjectsOfType<EmployeeHome>())
            {
                if (!UnityLife.IsAlive(employeeHome))
                    continue;

                var buildable = employeeHome.GetComponent<BuildableItem>();
                if (!UnityLife.IsAlive(buildable) || buildable!.ParentProperty != property)
                    continue;

                matches.Add((buildable, employeeHome));
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Fish Warehouse restored employee-home scan failed safely: {ex.Message}");
            return FishWarehouseEmployeeHomeAdoptionResult.Failed;
        }

        if (matches.Count == 0)
            return FishWarehouseEmployeeHomeAdoptionResult.NoneFound;

        // Each employee's own home is restored independently from that employee's
        // captured bed GUID; this property-level step only adopts ONE representative
        // locker -- for the optional navigation probe and to suppress the authored
        // no-locker fallback. Multiple lockers are legitimate (a player who hires
        // several handlers owns several homes), so pick one deterministically --
        // preferring a locker whose GUID matches a saved employee-home GUID -- rather
        // than failing the whole replay. Adopting exactly one is required: adopting
        // none would let the authored no-locker fallback place a spurious duplicate.
        var candidateGuids = new List<string?>(matches.Count);
        foreach (var match in matches)
            candidateGuids.Add(ReadHomeGuid(match.Buildable));

        int? index = FishWarehouseEmployeeHomeAdoptionPlanner.SelectHomeIndex(candidateGuids, savedHomeGuids);
        if (index is null)
            return FishWarehouseEmployeeHomeAdoptionResult.NoneFound;

        var chosen = matches[index.Value];

        return buildSurface.TryAdoptRestoredEmployeeHome(
            property!,
            chosen.Buildable,
            chosen.Home,
            Log)
            ? FishWarehouseEmployeeHomeAdoptionResult.Adopted
            : FishWarehouseEmployeeHomeAdoptionResult.Failed;
    }

    private static string? ReadHomeGuid(BuildableItem buildable)
    {
        try
        {
            return buildable.GUID.ToString();
        }
        catch
        {
            return null;
        }
    }



    public void EnsureManagementWorldspaceUiContainerForOpenCanvas()
    {
        if (_property is null || !_property.IsOwned)
            return;

        try
        {
            var canvas = Singleton<ManagementWorldspaceCanvas>.InstanceExists
                ? Singleton<ManagementWorldspaceCanvas>.Instance
                : null;
            if (canvas?.IsOpen != true)
            {
                _managementDiagnosticFailureReported = false;
                return;
            }

            if (_property.WorldspaceUIContainer is null)
                EnsureManagementWorldspaceUiContainer();
        }
        catch (Exception ex)
        {
            if (_managementDiagnosticFailureReported)
                return;

            _managementDiagnosticFailureReported = true;
            LogWarning($"Fish Warehouse management worldspace UI repair failed safely: {ex.Message}");
        }
    }

    private bool EnsureManagementWorldspaceUiContainer()
    {
        if (_property is null)
            return false;

        if (!FishWarehouseManagementEligibilityDiagnostic.ShouldCreatePropertyWorldspaceUiContainer(
                _property.IsOwned,
                _property.WorldspaceUIContainer is not null))
        {
            return _property.WorldspaceUIContainer is not null;
        }

        GameObject? staging = null;
        try
        {
            if (!Singleton<ManagementWorldspaceCanvas>.InstanceExists)
                return false;

            var canvas = Singleton<ManagementWorldspaceCanvas>.Instance;
            if (canvas?.Canvas is null)
                return false;

            staging = new GameObject("Fish Warehouse Worldspace UI Container");
            var container = staging.AddComponent<RectTransform>();
            container.SetParent(canvas.Canvas.transform, false);
            staging.SetActive(false);
            _property.WorldspaceUIContainer = container;
            _managementWorldspaceUiContainer = container;
            staging = null;

            var attached = _property.WorldspaceUIContainer == container &&
                container.parent == canvas.Canvas.transform;
            if (attached)
                OrganizedCrimeLog.Diagnostic("Fish Warehouse management worldspace UI container enabled under the vanilla management canvas.", Log);
            return attached;
        }
        catch (Exception ex)
        {
            if (staging is not null)
                UnityEngine.Object.DestroyImmediate(staging);
            LogWarning($"Fish Warehouse management worldspace UI container failed safely: {ex}");
            return false;
        }
    }

    private void DisposeManagementWorldspaceUiContainer()
    {
        if (UnityLife.IsAlive(_property) &&
            _property!.WorldspaceUIContainer == _managementWorldspaceUiContainer)
        {
            _property.WorldspaceUIContainer = null;
        }

        if (UnityLife.IsAlive(_managementWorldspaceUiContainer))
            UnityEngine.Object.DestroyImmediate(_managementWorldspaceUiContainer!.gameObject);
        _managementWorldspaceUiContainer = null;
    }

    private RuntimePropertyHostResult FailAndCleanup(
        string stage,
        string reason,
        bool propertyRegistered,
        bool networkRegistrationPassed,
        bool spawnPassed)
    {
        LogWarning($"Fish Warehouse runtime Property stopped at {stage}: {reason}");
        var cleanup = Stop();
        return new RuntimePropertyHostResult(
            Succeeded: false,
            Stage: stage,
            PropertyRegistered: propertyRegistered,
            NetworkRegistrationPassed: networkRegistrationPassed,
            SpawnPassed: spawnPassed,
            DespawnPassed: cleanup.DespawnPassed,
            CleanupPassed: cleanup.CleanupPassed,
            AuthoredCollectionUnchanged: cleanup.AuthoredCollectionUnchanged,
            DocksNetworkIdentityUnchanged: cleanup.DocksNetworkIdentityUnchanged,
            FailureReason: reason);
    }

    private RuntimePropertyHostResult BuildResult(
        bool succeeded,
        string stage,
        bool propertyRegistered,
        bool networkRegistrationPassed,
        bool spawnPassed,
        bool cleanupPassed,
        string? failureReason)
    {
        return new RuntimePropertyHostResult(
            Succeeded: succeeded,
            Stage: stage,
            PropertyRegistered: propertyRegistered,
            NetworkRegistrationPassed: networkRegistrationPassed,
            SpawnPassed: spawnPassed,
            DespawnPassed: false,
            CleanupPassed: cleanupPassed,
            AuthoredCollectionUnchanged: true,
            DocksNetworkIdentityUnchanged: true,
            FailureReason: failureReason);
    }

    public void AbandonAfterSceneUnload()
    {
        // OC-108: after quit-to-menu the previous scene's objects are already destroyed,
        // so a partial restore (e.g. frontage clearance) has nothing left to retry.
        LogWarning("Fish Warehouse runtime from the unloaded scene was abandoned; the next load starts clean.");
        _frontageClearance = null;
        _frontageClearanceCleanupPending = false;
        _continuousNavigationCoordinator = null;
        Clear();
    }

    private void Clear()
    {
        if (_continuousNavigationCoordinator is not null && !_continuousNavigationCoordinator.Stop())
        {
            LogWarning("Fish Warehouse native navigation cleanup remains pending; retaining navigation ownership for a later explicit Stop retry.");
            return;
        }

        _garageCrossing?.Deactivate();
        _garageCrossing = null;

        // The bridge holds no native state to restore (it only re-runs native dock
        // population); dropping the reference is a complete teardown.
        _dockDeliveryBridge = null;

        _root = null;
        _networkObject = null;
        _property = null;
        _bucket = null;
        _networkManager = null;
        _serverManager = null;
        _authoredCollection = null;
        _anchor = null;
        _buildSurface = null;
        _interiorRoom = null;
        _lighting = null;
        _garageJambs = null;
        _garageOpening = null;
        _navigation = null;
        _continuousNavigationCoordinator = null;
        _employeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;
        _propertyBounds = null;
        _employeeInfrastructure = null;
        if (!_frontageClearanceCleanupPending)
            _frontageClearance = null;
        _loadingDocks = null;
        _managementDiagnosticFailureReported = false;
        _managementWorldspaceUiContainer = null;
        FishWarehouseManagementCanvasPatch.SetTargetProperty(null);
        _definition = null;
        _persistenceObjectReplayRequired = false;
        _persistenceObjectsReplayed = false;
        _persistenceEmployeeAgentTypeId = null;
        _docksBefore = DocksIdentity.Empty;
    }

    private sealed class RuntimePropertyNavigationActions : IFishWarehouseContinuousNavigationActions
    {
        private readonly RuntimePropertyHost _host;

        public RuntimePropertyNavigationActions(RuntimePropertyHost host)
        {
            _host = host;
        }

        public bool TryPreflightGeometry()
        {
            if (!UnityLife.IsAlive(_host._anchor) || !UnityLife.IsAlive(_host._property))
                return false;

            _host._garageOpening ??= new FishWarehouseGarageOpeningHost();
            if (!_host._garageOpening.TryPreflight(
                    _host._anchor!,
                    _host._property!.transform,
                    out FishWarehouseGarageOpeningPreflightResult? preflight,
                    Log) ||
                preflight is null)
            {
                return false;
            }

            return true;
        }

        public bool TryActivateRoom()
        {
            FishWarehouseGarageOpeningProjection? projection = _host._garageOpening?.Preflight?.Projection;
            if (!UnityLife.IsAlive(_host._anchor) || !UnityLife.IsAlive(_host._property) || projection is null)
                return false;

            _host._interiorRoom ??= new FishWarehouseInteriorRoomHost();
            var roomReady = _host._interiorRoom.TryPrepare(
                       _host._anchor!,
                       _host._property!.transform,
                       projection,
                       Log) &&
                   _host._interiorRoom.Activate();
            if (roomReady)
            {
                _host.TryActivateLighting();
                _host.TryActivateGarageJambs(projection);
            }
            return roomReady;
        }

        public bool TryOpenGarage() => _host._garageOpening?.TryOpen(Log) == true;

        public string GetBuildableAndConfigurableSignature()
        {
            try
            {
                int configurableCount = _host._property?.Configurables?.Count ?? 0;
                int employeeCount = _host._property?.Employees?.Count ?? 0;
                return $"configurables={configurableCount};employees={employeeCount}";
            }
            catch
            {
                return "unavailable";
            }
        }

        public FishWarehouseNativeNavigationBuildResult TryBuildNavigation()
        {
            FishWarehouseGarageOpeningProjection? projection = _host._garageOpening?.Preflight?.Projection;
            Transform? locker = _host._buildSurface?.CurrentEmployeeHome?.transform;
            PackagingStation? packagingStation = _host.FindRepresentativePackagingStation();
            // OC-117: navigation probes all 20 created points from tier 1
            // on, not the tier-length slice the Property was handed, so a
            // bad point fails early instead of at the tier that reveals it.
            Transform[]? idlePoints = _host._employeeInfrastructure?.IsReady == true
                ? _host._employeeInfrastructure.CreatedIdlePoints.ToArray()
                : null;
            if (!UnityLife.IsAlive(_host._anchor))
            {
                LogWarning("Fish Warehouse employee navigation build rejected: anchor was unavailable.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            if (!UnityLife.IsAlive(_host._property))
            {
                LogWarning("Fish Warehouse employee navigation build rejected: property root was unavailable.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            if (projection is null)
            {
                LogWarning("Fish Warehouse employee navigation build rejected: garage projection was unavailable.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            int expectedCount = FishWarehouseEmployeeInfrastructureDefinition.DefinedIdlePointCount;
            if (idlePoints is null)
            {
                LogWarning($"Fish Warehouse employee navigation build rejected missing-idle-points: expected={expectedCount} actual=0.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            if (idlePoints.Length != expectedCount)
            {
                string reason = idlePoints.Length < expectedCount
                    ? "too-few-idle-points"
                    : "too-many-idle-points";
                LogWarning($"Fish Warehouse employee navigation build rejected {reason}: expected={expectedCount} actual={idlePoints.Length}.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            if (idlePoints.Any(point => !UnityLife.IsAlive(point)))
            {
                LogWarning($"Fish Warehouse employee navigation build rejected malformed-idle-points: expected={expectedCount} valid live points.");
                return FishWarehouseNativeNavigationBuildResult.Failed;
            }

            _host._navigation ??= new FishWarehouseNativeNavigationHost();
            return _host._navigation.TryBuild(
                _host._anchor!,
                _host._property!.transform,
                projection,
                idlePoints,
                locker,
                packagingStation,
                _host._persistenceEmployeeAgentTypeId,
                Log);
        }

        public bool ValidateNavigation() => _host._navigation?.IsBuilt == true;

        public bool RemoveNavigation()
        {
            try
            {
                _host._garageCrossing?.Deactivate();
                _host._garageCrossing = null;
                _host._dockDeliveryBridge = null;
                return _host._navigation?.Remove() ?? true;
            }
            catch (Exception ex)
            {
                LogWarning($"Fish Warehouse employee navigation removal was partial: {ex.Message}");
                return false;
            }
        }

        public void RestoreGarage()
        {
            try
            {
                _host._garageOpening?.Restore(Log);
            }
            catch (Exception ex)
            {
                LogWarning($"Fish Warehouse garage restoration was partial: {ex.Message}");
            }
        }

        public void DisposeRoom()
        {
            try
            {
                _host._lighting?.Dispose();
                _host._lighting = null;
                _host._garageJambs?.Dispose();
                _host._garageJambs = null;
                _host._interiorRoom?.Dispose();
            }
            catch (Exception ex)
            {
                LogWarning($"Fish Warehouse authored interior room cleanup was partial: {ex.Message}");
            }
            finally
            {
                _host._lighting = null;
                _host._garageJambs = null;
                _host._interiorRoom = null;
            }
        }
    }

    private PackagingStation? FindRepresentativePackagingStation()
    {
        var property = _property;
        if (!UnityLife.IsAlive(property))
            return null;

        try
        {
            foreach (PackagingStation station in Resources.FindObjectsOfTypeAll<PackagingStation>())
            {
                if (!UnityLife.IsAlive(station))
                    continue;

                BuildableItem? buildable = station.GetComponent<BuildableItem>();
                if (UnityLife.IsAlive(buildable) && buildable!.ParentProperty == property)
                    return station;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Fish Warehouse representative packaging-station lookup failed safely: {ex.Message}");
        }

        return null;
    }

    private static Property? FindProperty(string code)
    {
        foreach (var property in SnapshotProperties(Property.Properties))
        {
            if (string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
    }

    private static GameObject? FindAnchor(string path)
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is not null && gameObject.scene.IsValid() &&
                string.Equals(GetTransformPath(gameObject.transform), path, StringComparison.Ordinal))
                return gameObject;
        }

        return null;
    }

    private static NetworkObject? ResolveNetworkObject(Property property)
    {
        try
        {
            if (property.NetworkObject is not null)
                return property.NetworkObject;
        }
        catch
        {
            // Fall through to local component lookup.
        }

        return property.gameObject.GetComponent<NetworkObject>();
    }

    private static void RegisterPropertyCollections(Property property)
    {
        if (!Property.Properties.Contains(property))
            Property.Properties.Add(property);

        if (!Property.UnownedProperties.Contains(property))
            Property.UnownedProperties.Add(property);
    }

    private static void RemovePropertyCollections(Property property)
    {
        Property.Properties.Remove(property);
        Property.OwnedProperties.Remove(property);
        Property.UnownedProperties.Remove(property);
    }

    private static Property[] SnapshotProperties(Il2CppSystem.Collections.Generic.List<Property>? properties) =>
        properties is null ? Array.Empty<Property>() : properties.ToArray();

    private static DocksIdentity CaptureDocksIdentity(NetworkObject networkObject) =>
        new(
            ReadInt(() => networkObject.ObjectId),
            ReadULong(() => networkObject.SceneId),
            ReadString(() => networkObject.State.ToString()));

    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        Transform? current = transform;
        while (current is not null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    internal static void Log(string message) => OrganizedCrimeLog.Receipt(message);
    private static void LogWarning(string message) => OrganizedCrimeLog.Warning(message);
    private static int GetNativeInstanceId(UnityEngine.Object? value)
    {
        if (!UnityLife.IsAlive(value))
            return 0;

        try
        {
            return value!.GetInstanceID();
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadNativeId(Func<UnityEngine.Object?> read)
    {
        try
        {
            var value = read();
            if (!UnityLife.IsAlive(value))
                return "<none>";

            return value!.GetInstanceID().ToString();
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool Read(Func<bool> read) { try { return read(); } catch { return false; } }
    private static int ReadInt(Func<int> read) { try { return read(); } catch { return 0; } }
    private static ulong ReadULong(Func<ulong> read) { try { return read(); } catch { return 0; } }
    private static string ReadString(Func<string> read) { try { return read(); } catch { return "<unavailable>"; } }

    private sealed record DocksIdentity(int ObjectId, ulong SceneId, string State)
    {
        public static DocksIdentity Empty => new(0, 0, "<unavailable>");
    }
}
