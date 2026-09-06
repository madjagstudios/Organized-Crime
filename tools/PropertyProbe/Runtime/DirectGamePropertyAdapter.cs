using OrganizedCrime.PropertyProbe.Model;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Runtime;

internal sealed class DirectGamePropertyAdapter : IPropertyRuntimeAdapter
{
    public string AdapterName => "Direct Schedule I Property Adapter";

    public IReadOnlyList<PropertySnapshot> CaptureProperties()
    {
        var snapshots = new List<PropertySnapshot>();

        foreach (var property in Property.Properties.ToArray())
        {
            if (property is null)
                continue;

            try
            {
                snapshots.Add(CaptureProperty(property));
            }
            catch (Exception ex)
            {
                snapshots.Add(CreateErrorSnapshot(property, ex));
            }
        }

        return snapshots;
    }

    private static PropertySnapshot CaptureProperty(Property property)
    {
        var docks = (property.LoadingDocks ?? Array.Empty<LoadingDock>())
            .Where(d => d is not null)
            .Select(CaptureDock)
            .ToArray();

        var position = property.transform.position;

        return new PropertySnapshot(
            RuntimeType: property.GetType().FullName ?? property.GetType().Name,
            GameObjectName: property.gameObject.name,
            TransformPath: GetTransformPath(property.transform),
            PropertyName: property.PropertyName,
            PropertyCode: property.PropertyCode,
            IsOwned: property.IsOwned,
            OwnedByDefault: property.OwnedByDefault,
            Price: property.Price,
            EmployeeCapacity: property.EmployeeCapacity,
            EmployeeCount: property.Employees?.Count ?? 0,
            EmployeeIdlePointCount: property.EmployeeIdlePoints?.Length ?? 0,
            HasNpcSpawnPoint: property.NPCSpawnPoint is not null,
            HasEmployeeContainer: property.EmployeeContainer is not null,
            HasContentsContainer: property.Container is not null,
            HasBoundingBox: property.BoundingBox is not null,
            GridCount: property.Grids?.Count ?? 0,
            BuildableItemCount: property.BuildableItems?.Count ?? 0,
            ConfigurableCount: property.Configurables?.Count ?? 0,
            LoadingDockCount: docks.Length,
            Position: new Vector3Dto(position.x, position.y, position.z),
            LoadingDocks: docks);
    }

    private static LoadingDockSnapshot CaptureDock(LoadingDock dock)
    {
        var position = dock.transform.position;

        return new LoadingDockSnapshot(
            Guid: dock.GUID.ToString(),
            GameObjectName: dock.gameObject.name,
            TransformPath: GetTransformPath(dock.transform),
            ParentPropertyCode: dock.ParentProperty?.PropertyCode ?? string.Empty,
            Position: new Vector3Dto(position.x, position.y, position.z),
            AccessPointCount: dock.AccessPoints?.Length ?? 0,
            InputSlotCount: dock.InputSlots?.Count ?? 0,
            OutputSlotCount: dock.OutputSlots?.Count ?? 0);
    }

    private static PropertySnapshot CreateErrorSnapshot(Property property, Exception ex)
    {
        var position = property.transform.position;

        return new PropertySnapshot(
            RuntimeType: property.GetType().FullName ?? property.GetType().Name,
            GameObjectName: property.gameObject.name,
            TransformPath: GetTransformPath(property.transform),
            PropertyName: Safe(() => property.PropertyName, "<capture-error>"),
            PropertyCode: Safe(() => property.PropertyCode, "<capture-error>"),
            IsOwned: Safe(() => property.IsOwned, false),
            OwnedByDefault: Safe(() => property.OwnedByDefault, false),
            Price: Safe(() => property.Price, 0f),
            EmployeeCapacity: 0,
            EmployeeCount: 0,
            EmployeeIdlePointCount: 0,
            HasNpcSpawnPoint: false,
            HasEmployeeContainer: false,
            HasContentsContainer: false,
            HasBoundingBox: false,
            GridCount: 0,
            BuildableItemCount: 0,
            ConfigurableCount: 0,
            LoadingDockCount: 0,
            Position: new Vector3Dto(position.x, position.y, position.z),
            LoadingDocks: Array.Empty<LoadingDockSnapshot>(),
            CaptureError: ex.ToString());
    }

    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

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
}
