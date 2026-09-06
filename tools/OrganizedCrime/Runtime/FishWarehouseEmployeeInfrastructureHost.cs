using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseEmployeeInfrastructureHost
{
    private GameObject? _root;

    public bool IsReady => _root is not null;

    public bool TryAttach(Property property, Action<string>? log = null)
    {
        if (IsReady)
            return true;
        if (property is null)
            return false;

        GameObject? staging = null;
        try
        {
            if (!FishWarehouseEmployeeInfrastructureDefinition.HasValidLayout())
            {
                throw new InvalidOperationException(
                    "Fish Warehouse employee point definition failed its bounds/capacity contract.");
            }

            staging = new GameObject("OC_FishWarehouse_EmployeeInfrastructure");
            staging.transform.SetParent(property.transform, false);

            var spawn = CreatePoint(
                staging.transform,
                FishWarehouseEmployeeInfrastructureDefinition.NpcSpawnPoint);
            var idle = new Il2CppReferenceArray<Transform>(
                FishWarehouseEmployeeInfrastructureDefinition.Capacity);
            for (var index = 0;
                 index < FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count;
                 index++)
            {
                idle[index] = CreatePoint(
                    staging.transform,
                    FishWarehouseEmployeeInfrastructureDefinition.IdlePoints[index]);
            }

            property.EmployeeCapacity = FishWarehouseEmployeeInfrastructureDefinition.Capacity;
            property.NPCSpawnPoint = spawn;
            property.EmployeeIdlePoints = idle;

            if (property.EmployeeCapacity != FishWarehouseEmployeeInfrastructureDefinition.Capacity ||
                property.NPCSpawnPoint != spawn ||
                property.EmployeeIdlePoints is null ||
                property.EmployeeIdlePoints.Length != FishWarehouseEmployeeInfrastructureDefinition.Capacity)
            {
                throw new InvalidOperationException(
                    "Fish Warehouse Property did not retain employee infrastructure references.");
            }

            _root = staging;
            staging = null;
            log?.Invoke(
                $"Fish Warehouse employee infrastructure enabled (capacity={property.EmployeeCapacity}, idlePoints={property.EmployeeIdlePoints.Length}, npcSpawn={property.NPCSpawnPoint.name}).");
            return true;
        }
        catch (Exception ex)
        {
            if (staging is not null)
                UnityEngine.Object.DestroyImmediate(staging);
            log?.Invoke($"Fish Warehouse employee infrastructure failed safely: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_root is not null)
            UnityEngine.Object.DestroyImmediate(_root);
        _root = null;
    }

    private static Transform CreatePoint(
        Transform parent,
        FishWarehouseEmployeePointDefinition definition)
    {
        var point = new GameObject(definition.Name).transform;
        point.SetParent(parent, false);
        point.localPosition = new Vector3(definition.X, definition.Y, definition.Z);
        point.localRotation = Quaternion.identity;
        return point;
    }
}
