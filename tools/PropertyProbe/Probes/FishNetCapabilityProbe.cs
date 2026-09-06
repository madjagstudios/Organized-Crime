using System.Reflection;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetCapabilityProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private bool _hasRun;

    public FishNetCapabilitySnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F19 FishNet capability preflight already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running read-only FishNet prefab-registration capability preflight. Registration, component creation, spawn, ownership, persistence, and scene mutation are disabled.");

        try
        {
            var docks = FindProperty(DocksWarehouseCode);
            var docksNetworkObject = docks is null ? null : ResolveNetworkObject(docks);
            if (docks is null || docksNetworkObject is null)
                return WriteFailure("preconditions", "Docks Warehouse or its NetworkObject was not found.");

            var networkManager = docksNetworkObject.NetworkManager;
            var serverManager = InstanceFinder.ServerManager;
            var relevantMembers = CaptureRelevantMembers(networkManager)
                .Concat(CaptureRelevantMembers(serverManager))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(member => member, StringComparer.Ordinal)
                .ToArray();
            var spawnablePrefabsType = FindSpawnablePrefabsType(networkManager, serverManager);
            var spawnablePrefabMemberFound = relevantMembers.Any(IsSpawnablePrefabMember) ||
                spawnablePrefabsType is not null;
            var apiCapabilities = Runtime.S1ApiCapabilityDetector.Detect();
            var s1Api = apiCapabilities.First(capability => string.Equals(capability.AssemblyName, "S1API", StringComparison.OrdinalIgnoreCase));
            var s1MApi = apiCapabilities.First(capability => string.Equals(capability.AssemblyName, "S1MAPI", StringComparison.OrdinalIgnoreCase));
            var serverAvailable = serverManager is not null && serverManager.OneServerStarted();

            var snapshot = new FishNetCapabilitySnapshot(
                NetworkManagerPresent: networkManager is not null,
                ServerManagerPresent: serverManager is not null,
                ServerAvailable: serverAvailable,
                NetworkManagerType: TypeName(networkManager),
                ServerManagerType: TypeName(serverManager),
                SpawnablePrefabsType: spawnablePrefabsType ?? "<not exposed by reflected members>",
                SpawnablePrefabMemberFound: spawnablePrefabMemberFound,
                RelevantMembers: relevantMembers,
                S1ApiLoaded: s1Api.IsLoaded,
                S1MApiLoaded: s1MApi.IsLoaded,
                SceneIdentityReused: false,
                DocksMetadata: CaptureMetadata(docksNetworkObject),
                MutationAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                Gate: networkManager is not null && serverManager is not null && spawnablePrefabMemberFound
                    ? "capability-found"
                    : "capability-partial",
                FailureReason: networkManager is null
                    ? "Docks NetworkObject did not expose a NetworkManager."
                    : serverManager is null
                        ? "FishNet InstanceFinder.ServerManager was unavailable."
                        : spawnablePrefabMemberFound
                            ? null
                            : "No spawnable-prefab member was exposed by the reflected FishNet manager types.");

            Write(snapshot);
            ProbeLog.Info(snapshot.Gate == "capability-found"
                ? "FishNet prefab-registration capability preflight found a candidate spawnable-prefab surface. No mutation was attempted."
                : "FishNet prefab-registration capability preflight was partial. No mutation was attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString());
        }
    }

    private static IEnumerable<string> CaptureRelevantMembers(object? instance)
    {
        if (instance is null)
            return Array.Empty<string>();

        var type = instance.GetType();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => IsRelevantName(method.Name))
            .Select(method => $"{type.FullName}::{method}");
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(property => IsRelevantName(property.Name))
            .Select(property => $"{type.FullName}::{property.Name} : {property.PropertyType.FullName}");
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(field => IsRelevantName(field.Name))
            .Select(field => $"{type.FullName}::{field.Name} : {field.FieldType.FullName}");

        return methods.Concat(properties).Concat(fields);
    }

    private static string? FindSpawnablePrefabsType(params object?[] instances)
    {
        foreach (var instance in instances.Where(instance => instance is not null))
        {
            var type = instance!.GetType();
            var property = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name.Contains("SpawnablePrefab", StringComparison.OrdinalIgnoreCase));
            if (property is not null)
                return property.PropertyType.FullName;

            var field = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name.Contains("SpawnablePrefab", StringComparison.OrdinalIgnoreCase));
            if (field is not null)
                return field.FieldType.FullName;
        }

        return null;
    }

    private static bool IsRelevantName(string name) =>
        name.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Prefab", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Register", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Add", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpawnablePrefabMember(string member) =>
        member.Contains("SpawnablePrefab", StringComparison.OrdinalIgnoreCase) ||
        member.Contains("AddPrefab", StringComparison.OrdinalIgnoreCase) ||
        member.Contains("RegisterPrefab", StringComparison.OrdinalIgnoreCase);

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

    private static NetworkObjectMetadata CaptureMetadata(NetworkObject networkObject) =>
        new(
            Present: true,
            Networked: Read(() => networkObject.IsNetworked),
            SceneObject: Read(() => networkObject.IsSceneObject),
            Nested: Read(() => networkObject.IsNested),
            Deinitializing: Read(() => networkObject.IsDeinitializing),
            State: ReadString(() => networkObject.State.ToString()),
            ObjectId: ReadInt(() => networkObject.ObjectId),
            PrefabId: ReadUShort(() => networkObject.PrefabId),
            SceneId: ReadULong(() => networkObject.SceneId),
            SpawnableCollectionId: ReadUShort(() => networkObject.SpawnableCollectionId),
            NetworkManagerPresent: Read(() => networkObject.NetworkManager is not null),
            ServerManagerPresent: Read(() => networkObject.ServerManager is not null),
            NetworkBehaviourCount: ReadInt(() => networkObject.NetworkBehaviours?.Length ?? 0),
            SerializedJson: ReadString(() => UnityEngine.JsonUtility.ToJson(networkObject)));

    private static FishNetCapabilitySnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = new FishNetCapabilitySnapshot(
            NetworkManagerPresent: false,
            ServerManagerPresent: false,
            ServerAvailable: false,
            NetworkManagerType: "<unavailable>",
            ServerManagerType: "<unavailable>",
            SpawnablePrefabsType: "<unavailable>",
            SpawnablePrefabMemberFound: false,
            RelevantMembers: Array.Empty<string>(),
            S1ApiLoaded: false,
            S1MApiLoaded: false,
            SceneIdentityReused: false,
            DocksMetadata: NetworkObjectMetadata.Empty,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet capability preflight stopped at {gate}: {reason}");
        return snapshot;
    }

    private static string TypeName(object? instance) => instance?.GetType().FullName ?? "<unavailable>";

    private static void Write(FishNetCapabilitySnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-capability-preflight.txt", FishNetCapabilityFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-capability-preflight.json", FishNetCapabilityFormatter.FormatJson(snapshot));
    }

    private static bool Read(Func<bool> read) { try { return read(); } catch { return false; } }
    private static int ReadInt(Func<int> read) { try { return read(); } catch { return 0; } }
    private static ushort ReadUShort(Func<ushort> read) { try { return read(); } catch { return 0; } }
    private static ulong ReadULong(Func<ulong> read) { try { return read(); } catch { return 0; } }
    private static string ReadString(Func<string> read) { try { return read(); } catch (Exception ex) { return $"<unavailable: {ex.GetType().Name}>"; } }
}
