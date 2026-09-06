using System.Reflection;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetPrefabObjectsProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private bool _hasRun;

    public FishNetPrefabObjectsSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F20 PrefabObjects inspection already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running read-only FishNet PrefabObjects inspection. No PrefabObjects member will be invoked; registration, component creation, spawn, ownership, persistence, and scene mutation are disabled.");

        try
        {
            var docks = FindProperty(DocksWarehouseCode);
            var docksNetworkObject = docks is null ? null : ResolveNetworkObject(docks);
            var networkManager = docksNetworkObject?.NetworkManager;
            if (networkManager is null)
                return WriteFailure("preconditions", "Docks Warehouse or its NetworkManager was not found.");

            object? prefabObjects;
            try
            {
                prefabObjects = networkManager.SpawnablePrefabs;
            }
            catch (Exception ex)
            {
                return WriteFailure("capture", $"NetworkManager.SpawnablePrefabs could not be read: {ex.GetType().Name}: {ex.Message}");
            }

            if (prefabObjects is null)
                return WriteFailure("capability-partial", "NetworkManager.SpawnablePrefabs returned null.");

            var relevantMembers = CaptureRelevantMembers(prefabObjects)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(member => member, StringComparer.Ordinal)
                .ToArray();
            var countMemberFound = CaptureCountMember(prefabObjects);
            var snapshot = new FishNetPrefabObjectsSnapshot(
                PrefabObjectsPresent: true,
                PrefabObjectsType: prefabObjects.GetType().FullName ?? prefabObjects.GetType().Name,
                RelevantMembers: relevantMembers,
                CountMemberFound: countMemberFound,
                InvocationAttempted: false,
                MutationAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                Gate: relevantMembers.Length > 0 ? "capability-found" : "capability-partial",
                FailureReason: relevantMembers.Length > 0
                    ? null
                    : "PrefabObjects was readable but exposed no relevant public registration or collection members.");

            Write(snapshot);
            ProbeLog.Info(snapshot.Gate == "capability-found"
                ? "FishNet PrefabObjects inspection found candidate registration-related members. No member was invoked and no mutation was attempted."
                : "FishNet PrefabObjects inspection found the collection but no relevant public members. No member was invoked and no mutation was attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString());
        }
    }

    private static IEnumerable<string> CaptureRelevantMembers(object instance)
    {
        var type = instance.GetType();
        var members = new List<string>();

        try
        {
            members.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => IsRelevantName(method.Name))
                .Select(method => $"{type.FullName}::{method}"));
        }
        catch
        {
            // Keep the inspection failure-closed; properties and fields may still be readable.
        }

        try
        {
            members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(property => IsRelevantName(property.Name))
                .Select(property => $"{type.FullName}::{property.Name} : {property.PropertyType.FullName}"));
        }
        catch
        {
            // Keep the inspection failure-closed; methods and fields may still be readable.
        }

        try
        {
            members.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(field => IsRelevantName(field.Name))
                .Select(field => $"{type.FullName}::{field.Name} : {field.FieldType.FullName}"));
        }
        catch
        {
            // Reflection metadata is optional; no runtime member is invoked here.
        }

        return members;
    }

    private static bool CaptureCountMember(object instance)
    {
        try
        {
            var type = instance.GetType();
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(member => member.Name.Contains("Count", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRelevantName(string name) =>
        name.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Register", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Prefab", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Collection", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Get", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Set", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Remove", StringComparison.OrdinalIgnoreCase);

    private static Property? FindProperty(string code)
    {
        if (Property.Properties is null)
            return null;

        foreach (var property in Property.Properties)
        {
            if (property is not null && string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
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

    private static FishNetPrefabObjectsSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = new FishNetPrefabObjectsSnapshot(
            PrefabObjectsPresent: false,
            PrefabObjectsType: "<unavailable>",
            RelevantMembers: Array.Empty<string>(),
            CountMemberFound: false,
            InvocationAttempted: false,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet PrefabObjects inspection stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(FishNetPrefabObjectsSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-prefab-objects-inspection.txt", FishNetPrefabObjectsFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-prefab-objects-inspection.json", FishNetPrefabObjectsFormatter.FormatJson(snapshot));
    }
}
