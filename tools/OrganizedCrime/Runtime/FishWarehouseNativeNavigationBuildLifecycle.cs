using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Owns one native-navigation build attempt and its resource ledger. Unity
/// work stays in the adapter; this boundary owns graph decisions, validation,
/// and deterministic cleanup.
/// </summary>
public sealed class FishWarehouseNativeNavigationBuildLifecycle
{
    private const int DefaultAgentTypeId = 0;
    private const string LockerTargetName = "employee-locker";
    private const string PackagingStationTargetName = "packaging-station";

    private readonly FishWarehouseNativeNavigationResourceLedger _resources = new();
    private IFishWarehouseNativeNavigationAdapter? _adapter;
    private bool _mutated;

    public bool IsBuilt { get; private set; }

    public Exception? LastFailure { get; private set; }

    public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();

    public int? ResolvedEmployeeAgentTypeId { get; private set; }

    public FishWarehouseNativeNavigationBuildResult TryBuild(
        IFishWarehouseNativeNavigationAdapter adapter,
        FishWarehouseNativeNavigationBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(request);

        if (IsBuilt)
            return FishWarehouseNativeNavigationBuildResult.Succeeded;

        if (_resources.HasPendingTeardown)
        {
            LastFailure = new InvalidOperationException(
                "Fish Warehouse native navigation cannot build while prior native cleanup remains pending.");
            return FishWarehouseNativeNavigationBuildResult.Failed;
        }

        FishWarehouseEmployeeAgentTypeResolutionResult resolutionResult;
        try
        {
            IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> settings = adapter.GetAgentSettings() ??
                Array.Empty<FishWarehouseNativeNavigationAgentSetting>();
            resolutionResult = FishWarehouseEmployeeAgentTypeResolver.Resolve(
                request.PreferredEmployeeAgentTypeId,
                settings,
                Array.Empty<int>(),
                Array.Empty<int>());
            if (resolutionResult.Status == FishWarehouseEmployeeAgentTypeResolutionStatus.Pending)
            {
                IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> propertyEmployees = adapter
                    .FindLiveEmployeeAgents(FishWarehouseNativeNavigationEmployeeScope.PropertyAssigned) ??
                    Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>();
                resolutionResult = FishWarehouseEmployeeAgentTypeResolver.Resolve(
                    request.PreferredEmployeeAgentTypeId,
                    settings,
                    propertyEmployees.Select(employee => employee.AgentTypeId).ToArray(),
                    Array.Empty<int>());
            }

            if (resolutionResult.Status == FishWarehouseEmployeeAgentTypeResolutionStatus.Pending)
            {
                IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> globalEmployees = adapter
                    .FindLiveEmployeeAgents(FishWarehouseNativeNavigationEmployeeScope.Global) ??
                    Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>();
                resolutionResult = FishWarehouseEmployeeAgentTypeResolver.Resolve(
                    request.PreferredEmployeeAgentTypeId,
                    settings,
                    Array.Empty<int>(),
                    globalEmployees.Select(employee => employee.AgentTypeId).ToArray());
            }
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }

        if (resolutionResult.Status == FishWarehouseEmployeeAgentTypeResolutionStatus.Pending)
            return FishWarehouseNativeNavigationBuildResult.Pending;

        if (resolutionResult.Status == FishWarehouseEmployeeAgentTypeResolutionStatus.Failed)
            return Fail(new InvalidOperationException(resolutionResult.FailureReason));

        _adapter = adapter;
        FishWarehouseEmployeeAgentTypeResolution resolution = resolutionResult.Resolution!;
        int employeeAgentTypeId = resolution.AgentTypeId;
        try
        {
            ValidateIdlePointCount(request.IdlePoints);

            int[] graphAgentTypeIds = employeeAgentTypeId == DefaultAgentTypeId
                ? new[] { DefaultAgentTypeId }
                : new[] { employeeAgentTypeId, DefaultAgentTypeId };
            var probes = new Dictionary<int, FishWarehouseNativeNavigationRuntimeGraphProbe>();
            foreach (int agentTypeId in graphAgentTypeIds)
            {
                FishWarehouseNativeNavigationRuntimeGraphProbe probe = adapter.ProbeGraph(agentTypeId, request.World);
                if (probe.AgentTypeId != agentTypeId)
                    throw new InvalidOperationException(
                        $"Fish Warehouse native navigation graph probe returned agent ID {probe.AgentTypeId} for requested ID {agentTypeId}.");
                if (!probe.ExteriorSampled)
                    throw new InvalidOperationException(
                        $"Fish Warehouse native navigation could not sample the exterior for agent ID {agentTypeId}.");

                probes.Add(agentTypeId, probe);
            }

            foreach (int agentTypeId in graphAgentTypeIds)
                ApplyGraphDecision(agentTypeId, probes[agentTypeId], request.World);

            if (_mutated)
                adapter.ClearNativeSampleCache();

            FishWarehouseNativeNavigationAcceptanceResult acceptance = Validate(
                request,
                probes[employeeAgentTypeId],
                probes[DefaultAgentTypeId],
                employeeAgentTypeId);
            LastWarnings = acceptance.Warnings;
            if (!acceptance.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Fish Warehouse native navigation validation failed: {string.Join(" | ", acceptance.Failures)}");
            }

            LastFailure = null;
            IsBuilt = true;
            ResolvedEmployeeAgentTypeId = resolution.AgentTypeId;
            return FishWarehouseNativeNavigationBuildResult.Succeeded;
        }
        catch (Exception exception)
        {
            return Fail(exception);
        }
    }

    public bool Remove()
    {
        IsBuilt = false;
        LastWarnings = Array.Empty<string>();
        ResolvedEmployeeAgentTypeId = null;
        try
        {
            RemoveOwnedResources();
            _mutated = false;
            _adapter = null;
            LastFailure = null;
            return true;
        }
        catch (Exception exception)
        {
            LastFailure = exception;
            return false;
        }
    }

    private void ApplyGraphDecision(
        int agentTypeId,
        FishWarehouseNativeNavigationRuntimeGraphProbe probe,
        FishWarehouseNativeNavigationWorldDescriptor world)
    {
        FishWarehouseNativeGraphDecision decision = FishWarehouseNativeGraphDecisionDefinition.Decide(
            new FishWarehouseNativeGraphProbe(
                probe.ExteriorSampled,
                probe.InteriorSampled,
                probe.PathComplete));
        if (decision == FishWarehouseNativeGraphDecision.Fail)
            throw new InvalidOperationException($"Fish Warehouse native navigation graph decision failed for agent ID {agentTypeId}.");

        FishWarehouseNativeNavigationGraphRole role = agentTypeId == DefaultAgentTypeId
            ? FishWarehouseNativeNavigationGraphRole.Default
            : FishWarehouseNativeNavigationGraphRole.Employee;
        if (decision == FishWarehouseNativeGraphDecision.AddSurfaceAndLink)
        {
            int dataKey = _adapter!.AddSyntheticSurface(agentTypeId, world);
            _resources.RecordData(role, dataKey);
            _mutated = true;
        }

        if (decision is FishWarehouseNativeGraphDecision.AddSurfaceAndLink or FishWarehouseNativeGraphDecision.AddLinkOnly)
        {
            int linkKey = _adapter!.AddLink(agentTypeId, world, probe.ExteriorPosition);
            _resources.RecordLink(role, linkKey);
            _mutated = true;
        }
    }

    private FishWarehouseNativeNavigationAcceptanceResult Validate(
        FishWarehouseNativeNavigationBuildRequest request,
        FishWarehouseNativeNavigationRuntimeGraphProbe employeeProbe,
        FishWarehouseNativeNavigationRuntimeGraphProbe defaultProbe,
        int employeeAgentTypeId)
    {
        var probes = new List<FishWarehouseNativeNavigationProbe>
        {
            ObserveRequired(
                FishWarehouseNativeNavigationGraphRole.Employee,
                employeeAgentTypeId,
                FishWarehouseNativeNavigationAcceptanceDefinition.EmployeeExteriorToDoorwayTargetName,
                employeeProbe.ExteriorPosition,
                request.World.DoorwayInterior)
        };

        for (int index = 0; index < request.IdlePoints.Count; index++)
        {
            probes.Add(ObserveRequired(
                FishWarehouseNativeNavigationGraphRole.Employee,
                employeeAgentTypeId,
                FishWarehouseNativeNavigationAcceptanceDefinition.DoorwayToIdleTargetName(index),
                request.World.DoorwayInterior,
                request.IdlePoints[index]));
        }

        probes.Add(ObserveRequired(
            FishWarehouseNativeNavigationGraphRole.Default,
            DefaultAgentTypeId,
            FishWarehouseNativeNavigationAcceptanceDefinition.DefaultExteriorToDoorwayTargetName,
            defaultProbe.ExteriorPosition,
            request.World.DoorwayInterior));
        probes.Add(ObserveOptional(
            employeeAgentTypeId,
            LockerTargetName,
            request.LockerPoint,
            request.World.DoorwayInterior));
        probes.Add(ObserveOptional(
            employeeAgentTypeId,
            PackagingStationTargetName,
            request.PackagingStationPoint,
            request.World.DoorwayInterior));

        return FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);
    }

    private FishWarehouseNativeNavigationProbe ObserveRequired(
        FishWarehouseNativeNavigationGraphRole graphRole,
        int agentTypeId,
        string targetName,
        FishWarehouseNativeNavigationVector start,
        FishWarehouseNativeNavigationVector end)
    {
        FishWarehouseNativeNavigationPathObservation observation = _adapter!.ValidatePath(
            graphRole,
            agentTypeId,
            targetName,
            start,
            end);
        return new(
            graphRole,
            agentTypeId,
            targetName,
            Required: true,
            TargetPresent: true,
            observation.StartSampled,
            observation.EndSampled,
            observation.PathStatus);
    }

    private FishWarehouseNativeNavigationProbe ObserveOptional(
        int agentTypeId,
        string targetName,
        FishWarehouseNativeNavigationVector? target,
        FishWarehouseNativeNavigationVector start)
    {
        if (!target.HasValue)
        {
            return new(
                FishWarehouseNativeNavigationGraphRole.Employee,
                agentTypeId,
                targetName,
                Required: false,
                TargetPresent: false,
                StartSampled: false,
                EndSampled: false,
                FishWarehouseNativeNavigationPathStatus.PathInvalid);
        }

        FishWarehouseNativeNavigationPathObservation observation = _adapter!.ValidatePath(
            FishWarehouseNativeNavigationGraphRole.Employee,
            agentTypeId,
            targetName,
            start,
            target.Value);
        return new(
            FishWarehouseNativeNavigationGraphRole.Employee,
            agentTypeId,
            targetName,
            Required: false,
            TargetPresent: true,
            observation.StartSampled,
            observation.EndSampled,
            observation.PathStatus);
    }

    private FishWarehouseNativeNavigationBuildResult Fail(Exception failure)
    {
        IsBuilt = false;
        _mutated = false;
        LastWarnings = Array.Empty<string>();
        ResolvedEmployeeAgentTypeId = null;
        LastFailure = failure;
        try
        {
            RemoveOwnedResources();
        }
        catch (Exception cleanupFailure)
        {
            LastFailure = new AggregateException(failure, cleanupFailure);
        }

        return FishWarehouseNativeNavigationBuildResult.Failed;
    }

    private static void ValidateIdlePointCount(IReadOnlyList<FishWarehouseNativeNavigationVector>? idlePoints)
    {
        int expectedCount = FishWarehouseEmployeeInfrastructureDefinition.Capacity;
        if (expectedCount != FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count)
        {
            throw new InvalidOperationException(
                $"Fish Warehouse native navigation rejected capacity-idle-point-count-mismatch: " +
                $"capacity={expectedCount} definitionIdlePoints={FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count}.");
        }

        if (idlePoints is null)
        {
            throw new InvalidOperationException(
                $"Fish Warehouse native navigation rejected missing-idle-points: expected={expectedCount} actual=0.");
        }

        if (idlePoints.Count == expectedCount)
            return;

        string reason = idlePoints.Count < expectedCount
            ? "too-few-idle-points"
            : "too-many-idle-points";
        throw new InvalidOperationException(
            $"Fish Warehouse native navigation rejected {reason}: expected={expectedCount} actual={idlePoints.Count}.");
    }

    private void RemoveOwnedResources()
    {
        if (_adapter is null)
            return;

        foreach (FishWarehouseNativeNavigationTeardownOperation operation in _resources.PendingTeardown())
        {
            _adapter.Remove(operation);
            _resources.Acknowledge(operation);
        }
    }
}
