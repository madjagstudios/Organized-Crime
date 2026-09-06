using Il2CppFishNet;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetRuntimePrefabRegistrationProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const ushort ProbeCollectionId = 65000;
    private const string TemporaryObjectName = "OC_FishWarehouse_RuntimePrefabProbe";
    private bool _hasRun;

    public FishNetRuntimePrefabRegistrationSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F24 runtime prefab registration already ran in this game process.", false, false);

        _hasRun = true;
        ProbeLog.Info("Running guarded FishNet runtime prefab registration experiment on a disposable save. One temporary NetworkObject may be added to runtime bucket 65000; spawn, ownership, persistence, Property mutation, and save operations are disabled.");

        GameObject? temporaryObject = null;
        NetworkObject? networkObject = null;
        PrefabObjects? bucket = null;
        NetworkManager? networkManager = null;
        NetworkObject? docksNetworkObject = null;
        var bucketCreationAttempted = false;
        var addObjectAttempted = false;
        var registrationPassed = false;
        var cleanupAttempted = false;
        var cleanupPassed = true;
        var temporaryObjectDestroyed = false;
        var addObjectResult = "not-attempted";
        var beforeCount = 0;
        var afterCount = 0;
        var authoredCollectionUnchanged = true;
        object? authoredCollection = null;
        var docksBefore = DocksIdentity.Empty;
        var gate = "preconditions";
        string? failureReason = null;

        try
        {
            var docksProperty = FindProperty(DocksWarehouseCode);
            docksNetworkObject = docksProperty is null ? null : ResolveNetworkObject(docksProperty);
            networkManager = docksNetworkObject?.NetworkManager;
            if (networkManager is null || docksNetworkObject is null)
            {
                failureReason = "Docks Warehouse or its NetworkManager was not found.";
            }
            else
            {
                authoredCollection = networkManager.SpawnablePrefabs;
                docksBefore = CaptureDocksIdentity(docksNetworkObject);
                if (authoredCollection is null)
                {
                    failureReason = "Authored SpawnablePrefabs was unavailable.";
                }
                else
                {
                    var existing = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false);
                    if (existing is not null)
                    {
                        failureReason = $"Probe collection ID {ProbeCollectionId} already existed; no registration was attempted.";
                    }
                    else
                    {
                        temporaryObject = new GameObject(TemporaryObjectName);
                        networkObject = temporaryObject.AddComponent<NetworkObject>();
                        byte componentIndex = 0;
                        networkObject.UpdateNetworkBehaviours(null, ref componentIndex);

                        bucket = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, true);
                        bucketCreationAttempted = true;
                        if (bucket is null)
                        {
                            gate = "bucket-creation";
                            failureReason = "Runtime bucket creation returned null; AddObject was not attempted.";
                        }
                        else
                        {
                            beforeCount = ReadInt(() => bucket.GetObjectCount());
                            addObjectAttempted = true;
                            try
                            {
                                bucket.AddObject(networkObject, true);
                                addObjectResult = "returned";
                            }
                            catch (Exception ex)
                            {
                                addObjectResult = $"failed: {ex.GetType().Name}: {ex.Message}";
                            }

                            afterCount = ReadInt(() => bucket.GetObjectCount());
                            registrationPassed = addObjectResult == "returned" && afterCount > beforeCount;
                            gate = registrationPassed ? "registration-passed" : "registration-failed";
                            failureReason = registrationPassed
                                ? null
                                : "AddObject returned without increasing the runtime bucket object count.";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            gate = "registration-failed";
            failureReason = ex.ToString();
        }
        finally
        {
            cleanupAttempted = bucketCreationAttempted || temporaryObject is not null;
            if (bucketCreationAttempted && networkManager is not null)
            {
                try
                {
                    cleanupPassed = networkManager.RemoveSpawnableCollection(ProbeCollectionId);
                }
                catch (Exception ex)
                {
                    cleanupPassed = false;
                    ProbeLog.Warn($"Runtime prefab bucket cleanup failed: {ex}");
                }
            }

            if (temporaryObject is not null)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(temporaryObject);
                    temporaryObjectDestroyed = true;
                }
                catch (Exception ex)
                {
                    cleanupPassed = false;
                    ProbeLog.Warn($"Temporary runtime prefab GameObject cleanup failed: {ex}");
                }
            }
        }

        if (networkManager is not null && authoredCollection is not null)
            authoredCollectionUnchanged = ReferenceEquals(authoredCollection, networkManager.SpawnablePrefabs);

        var docksAfter = docksNetworkObject is null ? docksBefore : CaptureDocksIdentity(docksNetworkObject);
        var snapshot = new FishNetRuntimePrefabRegistrationSnapshot(
            CollectionId: ProbeCollectionId,
            TemporaryObjectPath: TemporaryObjectName,
            BeforeBucketObjectCount: beforeCount,
            AfterBucketObjectCount: afterCount,
            AddObjectAttempted: addObjectAttempted,
            RegistrationPassed: registrationPassed,
            AddObjectResult: addObjectResult,
            NetworkObjectPresent: networkObject is not null,
            Networked: networkObject is not null && Read(() => networkObject.IsNetworked),
            SceneObject: networkObject is not null && Read(() => networkObject.IsSceneObject),
            Spawned: networkObject is not null && Read(() => networkObject.IsSpawned),
            NetworkState: networkObject is null ? "<unavailable>" : ReadString(() => networkObject.State.ToString()),
            PrefabId: networkObject is null ? 0 : ReadInt(() => networkObject.PrefabId),
            SpawnableCollectionId: networkObject is null ? (ushort)0 : ReadUShort(() => networkObject.SpawnableCollectionId),
            CleanupAttempted: cleanupAttempted,
            CleanupPassed: cleanupPassed,
            TemporaryObjectDestroyed: temporaryObjectDestroyed,
            AuthoredCollectionUnchanged: authoredCollectionUnchanged,
            DocksNetworkIdentityUnchanged: docksBefore == docksAfter,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            PropertyMutationAttempted: false,
            Gate: registrationPassed && cleanupPassed ? gate : gate == "preconditions" ? gate : "registration-partial",
            FailureReason: registrationPassed && cleanupPassed ? failureReason : failureReason ?? "Registration or cleanup did not pass.");

        Write(snapshot);
        ProbeLog.Info(snapshot.RegistrationPassed && snapshot.CleanupPassed
            ? "FishNet runtime prefab registration gate passed and cleanup completed. Spawn, ownership, persistence, and Property mutation were not attempted."
            : "FishNet runtime prefab registration gate did not pass or cleanup was partial. Spawn, ownership, persistence, and Property mutation were not attempted.");
        return snapshot;
    }

    private static FishNetRuntimePrefabRegistrationSnapshot WriteFailure(string gate, string reason, bool bucketCreationAttempted, bool cleanupAttempted)
    {
        var snapshot = new FishNetRuntimePrefabRegistrationSnapshot(
            CollectionId: ProbeCollectionId,
            TemporaryObjectPath: TemporaryObjectName,
            BeforeBucketObjectCount: 0,
            AfterBucketObjectCount: 0,
            AddObjectAttempted: false,
            RegistrationPassed: false,
            AddObjectResult: "not-attempted",
            NetworkObjectPresent: false,
            Networked: false,
            SceneObject: false,
            Spawned: false,
            NetworkState: "<unavailable>",
            PrefabId: 0,
            SpawnableCollectionId: 0,
            CleanupAttempted: cleanupAttempted,
            CleanupPassed: !cleanupAttempted,
            TemporaryObjectDestroyed: false,
            AuthoredCollectionUnchanged: true,
            DocksNetworkIdentityUnchanged: true,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            PropertyMutationAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet runtime prefab registration stopped at {gate}: {reason}");
        return snapshot;
    }

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

    private static DocksIdentity CaptureDocksIdentity(NetworkObject networkObject) =>
        new(
            ReadInt(() => networkObject.ObjectId),
            ReadULong(() => networkObject.SceneId),
            ReadString(() => networkObject.State.ToString()));

    private static void Write(FishNetRuntimePrefabRegistrationSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-runtime-prefab-registration.txt", FishNetRuntimePrefabRegistrationFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-runtime-prefab-registration.json", FishNetRuntimePrefabRegistrationFormatter.FormatJson(snapshot));
    }

    private static bool Read(Func<bool> read) { try { return read(); } catch { return false; } }
    private static int ReadInt(Func<int> read) { try { return read(); } catch { return 0; } }
    private static ushort ReadUShort(Func<ushort> read) { try { return read(); } catch { return 0; } }
    private static ulong ReadULong(Func<ulong> read) { try { return read(); } catch { return 0; } }
    private static string ReadString(Func<string> read) { try { return read(); } catch { return "<unavailable>"; } }

    private sealed record DocksIdentity(int ObjectId, ulong SceneId, string State)
    {
        public static DocksIdentity Empty => new(0, 0, "<unavailable>");
    }
}
