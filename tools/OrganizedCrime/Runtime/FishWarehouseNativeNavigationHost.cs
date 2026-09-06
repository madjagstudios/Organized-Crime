using Il2CppScheduleOne.ObjectScripts;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Converts the approved anchor-local plan to property-world coordinates and
/// owns the native lifecycle. Task 4 wires this host into the property flow.
/// </summary>
public sealed class FishWarehouseNativeNavigationHost
{
    private readonly Func<Transform, Action<string>?, IFishWarehouseNativeNavigationAdapter> _adapterFactory;
    private readonly FishWarehouseNativeNavigationBuildLifecycle _lifecycle = new();

    public FishWarehouseNativeNavigationHost(
        Func<Transform, Action<string>?, IFishWarehouseNativeNavigationAdapter>? adapterFactory = null)
    {
        _adapterFactory = adapterFactory ?? ((propertyRoot, log) =>
            new UnityNativeNavigationAdapter(propertyRoot));
    }

    public bool IsBuilt => _lifecycle.IsBuilt;

    public Exception? LastFailure => _lifecycle.LastFailure;

    public int? ResolvedEmployeeAgentTypeId => _lifecycle.ResolvedEmployeeAgentTypeId;

    public FishWarehouseNativeNavigationBuildResult TryBuild(
        IFishWarehouseNativeNavigationAdapter adapter,
        FishWarehouseNativeNavigationBuildRequest request,
        Action<string>? log = null)
    {
        FishWarehouseNativeNavigationBuildResult result = _lifecycle.TryBuild(adapter, request);
        return result;
    }

    public FishWarehouseNativeNavigationBuildResult TryBuild(
        Transform anchor,
        Transform propertyRoot,
        FishWarehouseGarageOpeningProjection projection,
        IEnumerable<Transform> idlePoints,
        Transform? restoredLocker,
        PackagingStation? representativePackagingStation,
        int? preferredEmployeeAgentTypeId = null,
        Action<string>? log = null)
    {
        if (!IsAlive(anchor) || !IsAlive(propertyRoot) || projection is null || idlePoints is null ||
            !FishWarehouseTransformSignatureDefinition.IsCoincident(
                ToTransformSignature(anchor),
                ToTransformSignature(propertyRoot)))
        {
            return FailClosed("Fish Warehouse native navigation build rejected: required anchor, Property, or projection was unavailable.", log);
        }

        Transform[] idle = idlePoints.ToArray();
        int expectedCount = FishWarehouseEmployeeInfrastructureDefinition.Capacity;
        if (idle.Length != expectedCount)
        {
            string reason = idle.Length < expectedCount
                ? "too-few-idle-points"
                : "too-many-idle-points";
            return FailClosed(
                $"Fish Warehouse native navigation build rejected {reason}: expected={expectedCount} actual={idle.Length}.",
                log);
        }

        if (idle.Any(point => !IsAlive(point)))
        {
            return FailClosed(
                $"Fish Warehouse native navigation build rejected malformed-idle-points: expected={expectedCount} valid live points.",
                log);
        }

        try
        {
            FishWarehouseNativeNavigationPlan plan = FishWarehouseNativeNavigationDefinition.Create(projection);
            var request = new FishWarehouseNativeNavigationBuildRequest(
                ToWorldDescriptor(plan, propertyRoot),
                idle.Select(point => ToModelVector(point.position)).ToArray(),
                IsAlive(restoredLocker) ? ToModelVector(restoredLocker!.position) : null,
                IsAlive(representativePackagingStation)
                    ? ToModelVector(representativePackagingStation!.transform.position)
                    : null,
                preferredEmployeeAgentTypeId);
            FishWarehouseNativeNavigationBuildResult result = TryBuild(
                _adapterFactory(propertyRoot, log),
                request,
                log);
            foreach (string warning in _lifecycle.LastWarnings)
                Log(log, $"Fish Warehouse native navigation acceptance warning: {warning}.");
            if (result == FishWarehouseNativeNavigationBuildResult.Failed)
                Log(log, $"Fish Warehouse native navigation build failed safely: {_lifecycle.LastFailure?.Message}");
            return result;
        }
        catch (Exception exception)
        {
            Log(log, $"Fish Warehouse native navigation build failed safely: {exception.Message}");
            _lifecycle.Remove();
            return FishWarehouseNativeNavigationBuildResult.Failed;
        }
    }

    public bool Remove() => _lifecycle.Remove();

    private FishWarehouseNativeNavigationBuildResult FailClosed(string message, Action<string>? log)
    {
        Log(log, message);
        if (!_lifecycle.Remove())
            Log(log, $"Fish Warehouse native navigation cleanup remains pending: {_lifecycle.LastFailure?.Message}");
        return FishWarehouseNativeNavigationBuildResult.Failed;
    }

    private static void Log(Action<string>? log, string message)
    {
        try
        {
            log?.Invoke(message);
        }
        catch
        {
            // Diagnostics cannot change native ownership.
        }
    }

    private static FishWarehouseNativeNavigationWorldDescriptor ToWorldDescriptor(
        FishWarehouseNativeNavigationPlan plan,
        Transform propertyRoot) =>
        new(
            plan.Surface with { Center = ToModelVector(propertyRoot.TransformPoint(ToUnity(plan.Surface.Center))) },
            ToWorldBounds(plan.BakeBounds, propertyRoot),
            plan.Link with
            {
                Start = ToModelVector(propertyRoot.TransformPoint(ToUnity(plan.Link.Start))),
                NominalExteriorQueryPoint = ToModelVector(propertyRoot.TransformPoint(ToUnity(plan.Link.NominalExteriorQueryPoint)))
            },
            ToModelVector(propertyRoot.TransformPoint(ToUnity(plan.DoorwayInterior))),
            plan.SampleRadius);

    private static FishWarehouseNativeNavigationBox ToWorldBounds(
        FishWarehouseNativeNavigationBox localBounds,
        Transform propertyRoot)
    {
        Vector3 half = ToUnity(localBounds.Size) * 0.5f;
        Vector3 center = ToUnity(localBounds.Center);
        var worldBounds = new Bounds(propertyRoot.TransformPoint(center - half), Vector3.zero);
        foreach (int x in new[] { -1, 1 })
        foreach (int y in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            worldBounds.Encapsulate(propertyRoot.TransformPoint(center + Vector3.Scale(half, new Vector3(x, y, z))));

        return new FishWarehouseNativeNavigationBox(
            ToModelVector(worldBounds.center),
            ToModelVector(worldBounds.size));
    }

    private static FishWarehouseTransformSignature ToTransformSignature(Transform transform) =>
        new(
            new FishWarehouseTransformVector(transform.position.x, transform.position.y, transform.position.z),
            new FishWarehouseTransformVector(transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z),
            new FishWarehouseTransformVector(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z));

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
}
