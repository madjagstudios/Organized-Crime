using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public static class RuntimePropertyReadiness
{
    public static bool IsFishWarehouseReady()
    {
        var definition = RuntimePropertyDefinition.FishWarehouse;
        if (!FindAnchor(definition.AnchorPath))
            return false;

        if (Property.Properties is null || Property.Properties.Count == 0)
            return false;

        var docks = FindProperty("dockswarehouse");
        if (docks is null)
            return false;

        NetworkObject? networkObject;
        try
        {
            networkObject = docks.NetworkObject;
        }
        catch
        {
            networkObject = docks.gameObject.GetComponent<NetworkObject>();
        }

        var serverManager = InstanceFinder.ServerManager;
        return networkObject?.NetworkManager is not null &&
            serverManager is not null &&
            Read(() => serverManager.OneServerStarted());
    }

    private static bool FindAnchor(string path)
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is not null && gameObject.scene.IsValid() &&
                string.Equals(GetTransformPath(gameObject.transform), path, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static Property? FindProperty(string code)
    {
        foreach (var property in Property.Properties)
        {
            if (property is not null && string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
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

    private static bool Read(Func<bool> read) { try { return read(); } catch { return false; } }
}
