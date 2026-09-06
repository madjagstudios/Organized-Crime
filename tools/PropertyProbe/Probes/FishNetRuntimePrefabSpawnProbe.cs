using Il2CppFishNet;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetRuntimePrefabSpawnProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const ushort ProbeCollectionId = 65000;
    private const string TemporaryObjectName = "OC_FishWarehouse_RuntimePrefabProbe";
    private bool _hasRun;

    public FishNetRuntimePrefabSpawnSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F25 runtime prefab spawn already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running guarded FishNet runtime prefab spawn/despawn experiment on a disposable save. Only the temporary registered NetworkObject may spawn; ownership, persistence, Property mutation, and save operations are disabled.");

        GameObject? temporaryObject = null;
        NetworkObject? networkObject = null;
        PrefabObjects? bucket = null;
        NetworkManager? networkManager = null;
        ServerManager? serverManager = null;
        NetworkObject? docksNetworkObject = null;
        object? authoredCollection = null;
        var docksBefore = DocksIdentity.Empty;
        var beforeCount = 0;
        var afterCount = 0;
        var registrationPassed = false;
        var spawnAttempted = false;
        var spawnPassed = false;
        var despawnAttempted = false;
        var despawnPassed = false;
        var bucketCleanupAttempted = false;
        var bucketCleanupPassed = true;
        var temporaryObjectDestroyed = false;
        var serverAvailable = false;
        var authoredCollectionUnchanged = true;
        var objectId = 0;
        var prefabId = 0;
        ushort spawnableCollectionId = 0;
        var networked = false;
        var spawned = false;
        var serverInitialized = false;
        var networkState = "<unavailable>";
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
                var existing = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false);
                if (authoredCollection is null)
                {
                    failureReason = "Authored SpawnablePrefabs was unavailable.";
                }
                else if (existing is not null)
                {
                    failureReason = $"Probe collection ID {ProbeCollectionId} already existed; no spawn was attempted.";
                }
                else
                {
                    temporaryObject = new GameObject(TemporaryObjectName);
                    networkObject = temporaryObject.AddComponent<NetworkObject>();
                    byte componentIndex = 0;
                    networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
                    bucket = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, true);
                    if (bucket is null)
                    {
                        gate = "bucket-creation";
                        failureReason = "Runtime bucket creation returned null; registration and spawn were not attempted.";
                    }
                    else
                    {
                        beforeCount = ReadInt(() => bucket.GetObjectCount());
                        bucket.AddObject(networkObject, true);
                        afterCount = ReadInt(() => bucket.GetObjectCount());
                        registrationPassed = afterCount > beforeCount;
                        serverManager = InstanceFinder.ServerManager;
                        serverAvailable = serverManager is not null && Read(() => serverManager.OneServerStarted());
                        if (!registrationPassed)
                        {
                            gate = "registration-failed";
                            failureReason = "Runtime AddObject did not increase the bucket count; spawn was not attempted.";
                        }
                        else if (!serverAvailable)
                        {
                            gate = "server-unavailable";
                            failureReason = serverManager is null
                                ? "FishNet ServerManager was unavailable."
                                : "FishNet ServerManager was present but no server was started.";
                        }
                        else
                        {
                            spawnAttempted = true;
                            try
                            {
                                serverManager!.Spawn(networkObject, null, temporaryObject.scene);
                            }
                            catch (Exception ex)
                            {
                                gate = "spawn-failed";
                                failureReason = ex.ToString();
                            }

                            networked = Read(() => networkObject.IsNetworked);
                            spawned = Read(() => networkObject.IsSpawned);
                            serverInitialized = Read(() => networkObject.IsServerInitialized);
                            networkState = ReadString(() => networkObject.State.ToString());
                            objectId = ReadInt(() => networkObject.ObjectId);
                            prefabId = ReadInt(() => networkObject.PrefabId);
                            spawnableCollectionId = ReadUShort(() => networkObject.SpawnableCollectionId);
                            spawnPassed = spawned && serverInitialized;
                            if (failureReason is null)
                            {
                                gate = spawnPassed ? "spawn-passed" : "spawn-failed";
                                failureReason = spawnPassed ? null : "Spawn returned without a spawned/server-initialized NetworkObject.";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            gate = "spawn-failed";
            failureReason = ex.ToString();
        }
        finally
        {
            if (networkObject is not null && Read(() => networkObject.IsSpawned))
            {
                despawnAttempted = true;
                try
                {
                    if (serverManager is null)
                        throw new InvalidOperationException("ServerManager was unavailable for temporary despawn.");

                    // The spawned NetworkObject wrapper can become invalid at the
                    // IL2CPP boundary even while its state properties remain readable.
                    // FishNet exposes an equivalent GameObject overload; use the
                    // original live Unity object for the cleanup call.
                    if (temporaryObject is null)
                        throw new InvalidOperationException("Temporary GameObject was unavailable for FishNet despawn.");

                    serverManager.Despawn(
                        temporaryObject,
                        new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
                    despawnPassed = !Read(() => networkObject.IsSpawned);
                }
                catch (Exception ex)
                {
                    despawnPassed = false;
                    ProbeLog.Warn($"Temporary FishNet prefab despawn failed: {ex}");
                }
            }

            bucketCleanupAttempted = bucket is not null;
            if (bucketCleanupAttempted && networkManager is not null)
            {
                try
                {
                    bucketCleanupPassed = networkManager.RemoveSpawnableCollection(ProbeCollectionId);
                }
                catch (Exception ex)
                {
                    bucketCleanupPassed = false;
                    ProbeLog.Warn($"Runtime prefab spawn bucket cleanup failed: {ex}");
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
                    bucketCleanupPassed = false;
                    ProbeLog.Warn($"Temporary runtime prefab spawn GameObject cleanup failed: {ex}");
                }
            }
        }

        if (networkManager is not null && authoredCollection is not null)
            authoredCollectionUnchanged = ReferenceEquals(authoredCollection, networkManager.SpawnablePrefabs);

        var docksAfter = docksNetworkObject is null ? docksBefore : CaptureDocksIdentity(docksNetworkObject);
        var cleanupPassed = (!despawnAttempted || despawnPassed) && (!bucketCleanupAttempted || bucketCleanupPassed) && (!bucketCleanupAttempted || temporaryObjectDestroyed);
        var snapshot = new FishNetRuntimePrefabSpawnSnapshot(
            CollectionId: ProbeCollectionId,
            ServerManagerPresent: serverManager is not null,
            ServerAvailable: serverAvailable,
            RegistrationPassed: registrationPassed,
            SpawnAttempted: spawnAttempted,
            SpawnPassed: spawnPassed,
            DespawnAttempted: despawnAttempted,
            DespawnPassed: despawnPassed,
            NetworkObjectPresent: networkObject is not null,
            Networked: networked,
            Spawned: spawned,
            ServerInitialized: serverInitialized,
            NetworkState: networkState,
            ObjectId: objectId,
            PrefabId: prefabId,
            SpawnableCollectionId: spawnableCollectionId,
            BucketCleanupAttempted: bucketCleanupAttempted,
            BucketCleanupPassed: bucketCleanupPassed,
            TemporaryObjectDestroyed: temporaryObjectDestroyed,
            AuthoredCollectionUnchanged: authoredCollectionUnchanged,
            DocksNetworkIdentityUnchanged: docksBefore == docksAfter,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            PropertyMutationAttempted: false,
            Gate: spawnPassed && cleanupPassed ? "spawn-passed" : gate,
            FailureReason: spawnPassed && cleanupPassed ? null : failureReason ?? (cleanupPassed ? "Spawn gate did not pass." : "Cleanup gate did not pass."));

        Write(snapshot);
        ProbeLog.Info(snapshot.SpawnPassed && cleanupPassed
            ? "FishNet runtime prefab spawn/despawn gate passed and cleanup completed. Ownership, persistence, and Property mutation were not attempted."
            : "FishNet runtime prefab spawn/despawn gate did not pass or cleanup was partial. Ownership, persistence, and Property mutation were not attempted.");
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

    private static FishNetRuntimePrefabSpawnSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = FishNetRuntimePrefabSpawnSnapshot.Test();
        snapshot = snapshot with { Gate = gate, FailureReason = reason };
        Write(snapshot);
        ProbeLog.Warn($"FishNet runtime prefab spawn stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(FishNetRuntimePrefabSpawnSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-runtime-prefab-spawn.txt", FishNetRuntimePrefabSpawnFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-runtime-prefab-spawn.json", FishNetRuntimePrefabSpawnFormatter.FormatJson(snapshot));
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
