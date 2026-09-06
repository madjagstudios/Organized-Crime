using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;
using UnityEngine.AI;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The only production type that invokes Unity's native NavMesh API.
/// </summary>
public sealed class UnityNativeNavigationAdapter : IFishWarehouseNativeNavigationAdapter
{
    private readonly Transform _propertyRoot;
    private readonly Dictionary<int, NavMeshDataInstance> _data = new();
    private readonly Dictionary<int, NavMeshLinkInstance> _links = new();

    public UnityNativeNavigationAdapter(Transform propertyRoot)
    {
        _propertyRoot = propertyRoot ?? throw new ArgumentNullException(nameof(propertyRoot));
    }

    public IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> GetAgentSettings()
    {
        var settings = new List<FishWarehouseNativeNavigationAgentSetting>();
        for (int index = 0; index < NavMesh.GetSettingsCount(); index++)
        {
            NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByIndex(index);
            int agentTypeId = buildSettings.agentTypeID;
            settings.Add(new FishWarehouseNativeNavigationAgentSetting(
                agentTypeId,
                NavMesh.GetSettingsNameFromID(agentTypeId) ?? string.Empty));
        }

        return settings;
    }

    public IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> FindLiveEmployeeAgents(
        FishWarehouseNativeNavigationEmployeeScope scope)
    {
        var agents = new List<FishWarehouseNativeNavigationLiveEmployeeAgent>();
        if (scope == FishWarehouseNativeNavigationEmployeeScope.PropertyAssigned)
        {
            Property? property = _propertyRoot.GetComponent<Property>();
            if (IsAlive(property) && property!.Employees is not null)
            {
                foreach (Employee employee in property.Employees)
                    AddLiveEmployeeAgent(employee, agents);
            }

            return agents;
        }

        if (scope != FishWarehouseNativeNavigationEmployeeScope.Global)
            throw new ArgumentOutOfRangeException(nameof(scope), scope, null);

        foreach (Employee employee in Resources.FindObjectsOfTypeAll<Employee>())
            AddLiveEmployeeAgent(employee, agents);

        return agents;
    }

    public FishWarehouseNativeNavigationRuntimeGraphProbe ProbeGraph(
        int agentTypeId,
        FishWarehouseNativeNavigationWorldDescriptor world)
    {
        var filter = CreateFilter(agentTypeId);
        bool exteriorSampled = NavMesh.SamplePosition(
            ToUnity(world.Link.NominalExteriorQueryPoint),
            out NavMeshHit exteriorHit,
            world.SampleRadius,
            filter);
        bool interiorSampled = NavMesh.SamplePosition(
            ToUnity(world.DoorwayInterior),
            out NavMeshHit interiorHit,
            world.SampleRadius,
            filter);
        bool pathComplete = exteriorSampled && interiorSampled && CalculatePath(
            exteriorHit.position,
            interiorHit.position,
            filter) == FishWarehouseNativeNavigationPathStatus.PathComplete;
        FishWarehouseNativeNavigationVector exterior = exteriorSampled
            ? ToModelVector(exteriorHit.position)
            : world.Link.NominalExteriorQueryPoint;
        return new(agentTypeId, exteriorSampled, exterior, interiorSampled, pathComplete);
    }

    public int AddSyntheticSurface(int agentTypeId, FishWarehouseNativeNavigationWorldDescriptor world)
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeId);
        Il2CppSystem.Collections.Generic.List<NavMeshBuildSource> sources = CreateSources(world);
        NavMeshData? data = NavMeshBuilder.BuildNavMeshData(
            settings,
            sources,
            ToUnityBounds(world.BakeBounds),
            Vector3.zero,
            Quaternion.identity);
        if (!IsAlive(data))
            throw new InvalidOperationException($"Fish Warehouse native navigation produced no NavMeshData for agent ID {agentTypeId}.");

        NavMeshDataInstance instance = NavMesh.AddNavMeshData(data!, Vector3.zero, Quaternion.identity);
        if (!instance.valid)
            throw new InvalidOperationException($"Fish Warehouse native navigation added an invalid NavMeshData instance for agent ID {agentTypeId}.");

        _data.Add(instance.id, instance);
        return instance.id;
    }

    public int AddLink(
        int agentTypeId,
        FishWarehouseNativeNavigationWorldDescriptor world,
        FishWarehouseNativeNavigationVector exteriorEndpoint)
    {
        var link = new NavMeshLinkData
        {
            startPosition = ToUnity(world.Link.Start),
            endPosition = ToUnity(exteriorEndpoint),
            agentTypeID = agentTypeId,
            area = 0,
            costModifier = -1f,
            width = world.Link.Width,
            bidirectional = true
        };
        NavMeshLinkInstance instance = NavMesh.AddLink(link, Vector3.zero, Quaternion.identity);
        if (!instance.valid)
            throw new InvalidOperationException($"Fish Warehouse native navigation added an invalid NavMeshLink instance for agent ID {agentTypeId}.");

        _links.Add(instance.id, instance);
        return instance.id;
    }

    public void ClearNativeSampleCache()
    {
        NavMeshUtility.ClearCache();
    }

    public FishWarehouseNativeNavigationPathObservation ValidatePath(
        FishWarehouseNativeNavigationGraphRole graphRole,
        int agentTypeId,
        string targetName,
        FishWarehouseNativeNavigationVector start,
        FishWarehouseNativeNavigationVector end)
    {
        var filter = CreateFilter(agentTypeId);
        bool startSampled = NavMesh.SamplePosition(ToUnity(start), out NavMeshHit startHit, 1f, filter);
        bool endSampled = NavMesh.SamplePosition(ToUnity(end), out NavMeshHit endHit, 1f, filter);
        FishWarehouseNativeNavigationPathStatus status = startSampled && endSampled
            ? CalculatePath(startHit.position, endHit.position, filter)
            : FishWarehouseNativeNavigationPathStatus.PathInvalid;
        return new(startSampled, endSampled, status);
    }

    public void Remove(FishWarehouseNativeNavigationTeardownOperation operation)
    {
        switch (operation.Kind)
        {
            case FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink:
                RemoveLink(operation.ResourceKey);
                return;
            case FishWarehouseNativeNavigationTeardownOperationKind.RemoveData:
                RemoveData(operation.ResourceKey);
                return;
            case FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache:
                ClearNativeSampleCache();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, null);
        }
    }

    private Il2CppSystem.Collections.Generic.List<NavMeshBuildSource> CreateSources(
        FishWarehouseNativeNavigationWorldDescriptor worldDescriptor)
    {
        var source = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = ToUnity(worldDescriptor.Surface.Size),
            transform = Matrix4x4.TRS(
                ToUnity(worldDescriptor.Surface.Center),
                _propertyRoot.rotation,
                Vector3.one),
            area = 0,
            generateLinks = false
        };
        var sources = new Il2CppSystem.Collections.Generic.List<NavMeshBuildSource>();
        sources.Add(source);
        return sources;
    }

    private static NavMeshQueryFilter CreateFilter(int agentTypeId) => new()
    {
        agentTypeID = agentTypeId,
        areaMask = NavMesh.AllAreas
    };

    private static FishWarehouseNativeNavigationPathStatus CalculatePath(
        Vector3 start,
        Vector3 end,
        NavMeshQueryFilter filter)
    {
        var path = new NavMeshPath();
        bool calculated = NavMesh.CalculatePath(start, end, filter, path);
        if (!calculated)
            return FishWarehouseNativeNavigationPathStatus.PathInvalid;

        return path.status switch
        {
            NavMeshPathStatus.PathComplete => FishWarehouseNativeNavigationPathStatus.PathComplete,
            NavMeshPathStatus.PathPartial => FishWarehouseNativeNavigationPathStatus.PathPartial,
            _ => FishWarehouseNativeNavigationPathStatus.PathInvalid
        };
    }

    private void AddLiveEmployeeAgent(
        Employee employee,
        ICollection<FishWarehouseNativeNavigationLiveEmployeeAgent> agents)
    {
        if (!IsAlive(employee) || !IsAlive(employee.Movement) || !IsAlive(employee.Movement.Agent))
            return;

        int agentTypeId = employee.Movement.Agent.agentTypeID;
        string identity = $"{employee.name}#{employee.GetInstanceID()}";
        agents.Add(new FishWarehouseNativeNavigationLiveEmployeeAgent(identity, agentTypeId));
    }

    private static Bounds ToUnityBounds(FishWarehouseNativeNavigationBox value) =>
        new(ToUnity(value.Center), ToUnity(value.Size));

    private static FishWarehouseNativeNavigationVector ToModelVector(Vector3 value) => new(value.x, value.y, value.z);

    private static Vector3 ToUnity(FishWarehouseNativeNavigationVector value) => new(value.X, value.Y, value.Z);

    private static bool IsAlive(UnityEngine.Object? value)
    {
        try
        {
            return value is not null && value != null;
        }
        catch
        {
            return false;
        }
    }

    private void RemoveLink(int? resourceKey)
    {
        if (resourceKey is not int linkKey || !_links.TryGetValue(linkKey, out NavMeshLinkInstance link))
            return;

        if (link.valid)
            link.Remove();
        _links.Remove(linkKey);
    }

    private void RemoveData(int? resourceKey)
    {
        if (resourceKey is not int dataKey || !_data.TryGetValue(dataKey, out NavMeshDataInstance data))
            return;

        if (data.valid)
            data.Remove();
        _data.Remove(dataKey);
    }
}
