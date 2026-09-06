using System.Collections;
using System.Reflection;
using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetRuntimePrefabCollectionProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private bool _hasRun;

    public FishNetRuntimePrefabCollectionSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F21 runtime prefab collection inspection already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running read-only FishNet RuntimeSpawnablePrefabs inspection. No runtime prefab member will be invoked; registration, object creation, spawn, ownership, persistence, and scene mutation are disabled.");

        try
        {
            var docks = FindProperty(DocksWarehouseCode);
            var docksNetworkObject = docks is null ? null : ResolveNetworkObject(docks);
            var networkManager = docksNetworkObject?.NetworkManager;
            if (networkManager is null)
                return WriteFailure("preconditions", "Docks Warehouse or its NetworkManager was not found.");

            object? runtimeCollection;
            try
            {
                runtimeCollection = networkManager.RuntimeSpawnablePrefabs;
            }
            catch (Exception ex)
            {
                return WriteFailure("capture", $"NetworkManager.RuntimeSpawnablePrefabs could not be read: {ex.GetType().Name}: {ex.Message}");
            }

            if (runtimeCollection is null)
                return WriteFailure("capability-partial", "NetworkManager.RuntimeSpawnablePrefabs returned null.");

            var bucketKeys = CaptureBucketKeys(runtimeCollection);
            var relevantMembers = CaptureRelevantMembers(typeof(PrefabObjects))
                .Concat(CaptureRelevantMembers(runtimeCollection.GetType()))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(member => member, StringComparer.Ordinal)
                .ToArray();
            var bucketCount = CaptureCount(runtimeCollection);
            var snapshot = new FishNetRuntimePrefabCollectionSnapshot(
                RuntimeCollectionPresent: true,
                RuntimeCollectionType: runtimeCollection.GetType().FullName ?? runtimeCollection.GetType().Name,
                BucketCount: bucketCount,
                BucketKeys: bucketKeys,
                RelevantMembers: relevantMembers,
                InvocationAttempted: false,
                MutationAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                Gate: relevantMembers.Length > 0 ? "capability-found" : "capability-partial",
                FailureReason: relevantMembers.Length > 0
                    ? null
                    : "RuntimeSpawnablePrefabs was readable but exposed no relevant prefab collection members.");

            Write(snapshot);
            ProbeLog.Info(snapshot.Gate == "capability-found"
                ? "FishNet RuntimeSpawnablePrefabs inspection found a candidate runtime registration surface. No member was invoked and no mutation was attempted."
                : "FishNet RuntimeSpawnablePrefabs inspection found the collection but no relevant public members. No member was invoked and no mutation was attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString());
        }
    }

    private static int CaptureCount(object instance)
    {
        try
        {
            var countProperty = instance.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (countProperty?.PropertyType == typeof(int))
                return (int)(countProperty.GetValue(instance) ?? 0);
        }
        catch
        {
            // Count is optional metadata; keep the report failure-closed.
        }

        return 0;
    }

    private static IReadOnlyList<string> CaptureBucketKeys(object instance)
    {
        var keys = new List<string>();
        try
        {
            if (instance is not IEnumerable enumerable)
                return keys;

            foreach (var entry in enumerable)
            {
                if (entry is null)
                    continue;

                var keyProperty = entry.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                var key = keyProperty?.GetValue(entry)?.ToString();
                if (!string.IsNullOrWhiteSpace(key))
                    keys.Add(key);
            }
        }
        catch
        {
            // Bucket keys are optional metadata; do not fail the capability report.
        }

        return keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> CaptureRelevantMembers(Type type)
    {
        var members = new List<string>();
        try
        {
            members.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => IsRelevantName(method.Name))
                .Select(method => $"{type.FullName}::{method}"));
        }
        catch
        {
            // Reflection metadata is optional and no member is invoked.
        }

        try
        {
            members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(property => IsRelevantName(property.Name))
                .Select(property => $"{type.FullName}::{property.Name} : {property.PropertyType.FullName}"));
        }
        catch
        {
            // Reflection metadata is optional and no member is invoked.
        }

        return members;
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

    private static FishNetRuntimePrefabCollectionSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = new FishNetRuntimePrefabCollectionSnapshot(
            RuntimeCollectionPresent: false,
            RuntimeCollectionType: "<unavailable>",
            BucketCount: 0,
            BucketKeys: Array.Empty<string>(),
            RelevantMembers: Array.Empty<string>(),
            InvocationAttempted: false,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet RuntimeSpawnablePrefabs inspection stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(FishNetRuntimePrefabCollectionSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-runtime-prefab-collection.txt", FishNetRuntimePrefabCollectionFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-runtime-prefab-collection.json", FishNetRuntimePrefabCollectionFormatter.FormatJson(snapshot));
    }
}
