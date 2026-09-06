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

internal sealed class RuntimePropertyVerticalSliceProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const string ProposedPropertyCode = "oc_fishwarehouse";
    private const string TargetName = "Fish Warehouse";
    private const ushort ProbeCollectionId = 65000;
    private const string TemporaryObjectName = "OC_FishWarehouse_RuntimePropertyProbe";
    private bool _hasRun;

    public RuntimePropertyVerticalSliceSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "Runtime Property vertical slice already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running guarded runtime Property vertical slice. Registration, network initialization, spawn, and cleanup are enabled; ownership, persistence, and save writes are disabled.");

        GameObject? temporaryObject = null;
        NetworkObject? networkObject = null;
        Property? property = null;
        PrefabObjects? bucket = null;
        NetworkManager? networkManager = null;
        ServerManager? serverManager = null;
        NetworkObject? docksNetworkObject = null;
        object? authoredCollection = null;
        var docksBefore = DocksIdentity.Empty;
        var propertyCountBefore = 0;
        var propertyCountAfter = 0;
        var targetInProperties = false;
        var targetInUnownedProperties = false;
        var targetInOwnedProperties = false;
        var identityConfigured = false;
        var propertyRegistered = false;
        var runtimeNetworkRegistrationPassed = false;
        var networkInitializeAttempted = false;
        var networkInitializePassed = false;
        var propertyNetworkObjectResolved = false;
        var propertyNetworked = false;
        var propertyClientInitialized = false;
        var propertyServerInitialized = false;
        var propertySpawned = false;
        var spawnAttempted = false;
        var spawnPassed = false;
        var despawnAttempted = false;
        var despawnPassed = false;
        var bucketCleanupAttempted = false;
        var bucketCleanupPassed = true;
        var temporaryObjectDestroyed = false;
        var authoredCollectionUnchanged = true;
        var serverAvailable = false;
        var objectId = 0;
        var prefabId = 0;
        ushort spawnableCollectionId = 0;
        var networkState = "<unavailable>";
        var gate = "preconditions";
        string? failureReason = null;

        try
        {
            propertyCountBefore = SnapshotProperties(Property.Properties).Length;
            var docksProperty = FindProperty(DocksWarehouseCode);
            docksNetworkObject = docksProperty is null ? null : ResolveNetworkObject(docksProperty);
            networkManager = docksNetworkObject?.NetworkManager;
            authoredCollection = networkManager?.SpawnablePrefabs;
            serverManager = InstanceFinder.ServerManager;
            serverAvailable = serverManager is not null && Read(() => serverManager.OneServerStarted());
            if (docksNetworkObject is null || networkManager is null)
            {
                failureReason = "Docks Warehouse or its NetworkManager was not found.";
            }
            else if (authoredCollection is null)
            {
                failureReason = "Authored SpawnablePrefabs was unavailable.";
            }
            else if (FindProperty(ProposedPropertyCode) is not null)
            {
                failureReason = $"Property code {ProposedPropertyCode} is already registered; no temporary root was created.";
            }
            else if (networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false) is not null)
            {
                failureReason = $"Probe collection ID {ProbeCollectionId} already existed; no registration or spawn was attempted.";
            }
            else if (!serverAvailable)
            {
                failureReason = serverManager is null
                    ? "FishNet ServerManager was unavailable."
                    : "FishNet ServerManager was present but no server was started.";
            }
            else
            {
                docksBefore = CaptureDocksIdentity(docksNetworkObject);
                temporaryObject = new GameObject(TemporaryObjectName);
                temporaryObject.SetActive(false);
                networkObject = temporaryObject.AddComponent<NetworkObject>();
                property = temporaryObject.AddComponent<Property>();
                identityConfigured = TrySetIdentity(property, out var identityFailure);
                if (!identityConfigured)
                {
                    gate = "identity";
                    failureReason = identityFailure;
                }
                else
                {
                    RegisterPropertyCollections(property);
                    propertyRegistered = FindProperty(ProposedPropertyCode) == property;
                    propertyCountAfter = SnapshotProperties(Property.Properties).Length;
                    targetInProperties = SnapshotProperties(Property.Properties).Contains(property);
                    targetInUnownedProperties = SnapshotProperties(Property.UnownedProperties).Contains(property);
                    targetInOwnedProperties = SnapshotProperties(Property.OwnedProperties).Contains(property);
                    if (!propertyRegistered)
                    {
                        gate = "property-registration";
                        failureReason = "Temporary Property was not observable in the Schedule I Property collection after registration.";
                    }
                    else
                    {
                        property.InitializeSaveable();
                        temporaryObject.SetActive(true);
                        byte componentIndex = 0;
                        networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
                        networkInitializeAttempted = true;
                        property.NetworkInitializeIfDisabled();
                        networkInitializePassed = true;
                        propertyNetworkObjectResolved = ResolveNetworkObject(property) is not null;
                        propertyNetworked = Read(() => property.IsNetworked);
                        propertyClientInitialized = Read(() => property.IsClientInitialized);
                        propertyServerInitialized = Read(() => property.IsServerInitialized);
                        propertySpawned = Read(() => property.IsSpawned);

                        bucket = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, true);
                        if (bucket is null)
                        {
                            gate = "bucket-creation";
                            failureReason = "Runtime bucket creation returned null; registration and spawn were not attempted.";
                        }
                        else
                        {
                            var beforeCount = ReadInt(() => bucket.GetObjectCount());
                            bucket.AddObject(networkObject, true);
                            var afterCount = ReadInt(() => bucket.GetObjectCount());
                            runtimeNetworkRegistrationPassed = afterCount > beforeCount;
                            if (!runtimeNetworkRegistrationPassed)
                            {
                                gate = "network-registration";
                                failureReason = "Temporary NetworkObject was not added to the runtime prefab bucket.";
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

                                propertyNetworked = Read(() => property.IsNetworked);
                                propertyClientInitialized = Read(() => property.IsClientInitialized);
                                propertyServerInitialized = Read(() => property.IsServerInitialized);
                                propertySpawned = Read(() => property.IsSpawned);
                                propertyNetworkObjectResolved = ResolveNetworkObject(property) is not null;
                                networkState = ReadString(() => networkObject.State.ToString());
                                objectId = ReadInt(() => networkObject.ObjectId);
                                prefabId = ReadInt(() => networkObject.PrefabId);
                                spawnableCollectionId = ReadUShort(() => networkObject.SpawnableCollectionId);
                                spawnPassed = propertySpawned && Read(() => networkObject.IsSpawned) && Read(() => networkObject.IsServerInitialized);
                                if (failureReason is null)
                                {
                                    gate = spawnPassed ? "spawn-passed" : "spawn-failed";
                                    failureReason = spawnPassed ? null : "Spawn returned without a spawned/server-initialized Property root.";
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            gate = networkInitializeAttempted ? "vertical-slice-failed" : "property-runtime-failed";
            failureReason = ex.ToString();
        }
        finally
        {
            if (networkObject is not null && Read(() => networkObject.IsSpawned))
            {
                despawnAttempted = true;
                try
                {
                    if (serverManager is null || temporaryObject is null)
                        throw new InvalidOperationException("FishNet ServerManager or temporary GameObject was unavailable for despawn.");

                    serverManager.Despawn(
                        temporaryObject,
                        new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
                    despawnPassed = !Read(() => networkObject.IsSpawned);
                }
                catch (Exception ex)
                {
                    despawnPassed = false;
                    bucketCleanupPassed = false;
                    ProbeLog.Warn($"Runtime Property vertical-slice despawn failed: {ex}");
                }
            }

            if (property is not null)
            {
                try
                {
                    RemovePropertyCollections(property);
                }
                catch (Exception ex)
                {
                    bucketCleanupPassed = false;
                    ProbeLog.Warn($"Temporary Property collection cleanup failed: {ex}");
                }
            }

            bucketCleanupAttempted = bucket is not null;
            if (bucketCleanupAttempted && networkManager is not null)
            {
                try
                {
                    bucketCleanupPassed &= networkManager.RemoveSpawnableCollection(ProbeCollectionId);
                }
                catch (Exception ex)
                {
                    bucketCleanupPassed = false;
                    ProbeLog.Warn($"Runtime Property vertical-slice bucket cleanup failed: {ex}");
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
                    ProbeLog.Warn($"Temporary Property root cleanup failed: {ex}");
                }
            }
        }

        if (networkManager is not null && authoredCollection is not null)
            authoredCollectionUnchanged = ReferenceEquals(authoredCollection, networkManager.SpawnablePrefabs);

        var docksAfter = docksNetworkObject is null ? docksBefore : CaptureDocksIdentity(docksNetworkObject);
        var cleanupPassed = (!despawnAttempted || despawnPassed) &&
            (!bucketCleanupAttempted || bucketCleanupPassed) &&
            (!bucketCleanupAttempted || temporaryObjectDestroyed);
        var verticalSlicePassed = propertyRegistered && runtimeNetworkRegistrationPassed && networkInitializePassed && spawnPassed && cleanupPassed &&
            authoredCollectionUnchanged && docksBefore == docksAfter;
        var snapshot = new RuntimePropertyVerticalSliceSnapshot(
            ProposedPropertyCode: ProposedPropertyCode,
            TargetName: TargetName,
            CollectionId: ProbeCollectionId,
            ServerManagerPresent: serverManager is not null,
            ServerAvailable: serverAvailable,
            TemporaryRootCreated: temporaryObject is not null,
            NetworkObjectPresent: networkObject is not null,
            PropertyPresent: property is not null,
            IdentityConfigured: identityConfigured,
            PropertyRegistered: propertyRegistered,
            RuntimeNetworkRegistrationPassed: runtimeNetworkRegistrationPassed,
            PropertyCountBefore: propertyCountBefore,
            PropertyCountAfter: propertyCountAfter,
            TargetInProperties: targetInProperties,
            TargetInUnownedProperties: targetInUnownedProperties,
            TargetInOwnedProperties: targetInOwnedProperties,
            PropertyNetworkObjectResolved: propertyNetworkObjectResolved,
            NetworkInitializeAttempted: networkInitializeAttempted,
            NetworkInitializePassed: networkInitializePassed,
            PropertyNetworked: propertyNetworked,
            PropertyClientInitialized: propertyClientInitialized,
            PropertyServerInitialized: propertyServerInitialized,
            PropertySpawned: propertySpawned,
            SpawnAttempted: spawnAttempted,
            SpawnPassed: spawnPassed,
            DespawnAttempted: despawnAttempted,
            DespawnPassed: despawnPassed,
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
            SaveAttempted: false,
            Gate: verticalSlicePassed ? "vertical-slice-passed" : gate,
            FailureReason: verticalSlicePassed ? null : failureReason ?? (cleanupPassed ? "Runtime Property vertical slice gate did not pass." : "Cleanup gate did not pass."));

        Write(snapshot);
        ProbeLog.Info(verticalSlicePassed
            ? "Runtime Property vertical slice passed and cleanup completed. Ownership, persistence, and save writes were not attempted."
            : "Runtime Property vertical slice did not pass or cleanup was partial. Ownership, persistence, and save writes were not attempted.");
        return snapshot;
    }

    private static Property? FindProperty(string code)
    {
        foreach (var property in SnapshotProperties(Property.Properties))
        {
            if (string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
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

    private static bool TrySetIdentity(Property property, out string failure)
    {
        try
        {
            JsonUtility.FromJsonOverwrite(PropertyPromotionIdentityPayload.Json, property);
            var codeSet = string.Equals(property.PropertyCode, ProposedPropertyCode, StringComparison.Ordinal);
            var nameSet = string.Equals(property.PropertyName, TargetName, StringComparison.Ordinal);
            if (codeSet && nameSet)
            {
                failure = string.Empty;
                return true;
            }

            failure = $"Serialized Property identity did not apply (code applied: {codeSet}; name applied: {nameSet}).";
            return false;
        }
        catch (Exception ex)
        {
            failure = $"Serialized Property identity failed: {ex}";
            return false;
        }
    }

    private static void RegisterPropertyCollections(Property property)
    {
        if (!Property.Properties.Contains(property))
            Property.Properties.Add(property);

        if (!Property.UnownedProperties.Contains(property))
            Property.UnownedProperties.Add(property);
    }

    private static void RemovePropertyCollections(Property property)
    {
        Property.Properties.Remove(property);
        Property.OwnedProperties.Remove(property);
        Property.UnownedProperties.Remove(property);
    }

    private static Property[] SnapshotProperties(Il2CppSystem.Collections.Generic.List<Property>? properties) =>
        properties is null ? Array.Empty<Property>() : properties.ToArray();

    private static DocksIdentity CaptureDocksIdentity(NetworkObject networkObject) =>
        new(
            ReadInt(() => networkObject.ObjectId),
            ReadULong(() => networkObject.SceneId),
            ReadString(() => networkObject.State.ToString()));

    private static RuntimePropertyVerticalSliceSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = RuntimePropertyVerticalSliceSnapshot.Test();
        snapshot = snapshot with { Gate = gate, FailureReason = reason };
        Write(snapshot);
        ProbeLog.Warn($"Runtime Property vertical slice stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(RuntimePropertyVerticalSliceSnapshot snapshot)
    {
        ProbeLog.WriteFile("runtime-property-vertical-slice.txt", RuntimePropertyVerticalSliceFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("runtime-property-vertical-slice.json", RuntimePropertyVerticalSliceFormatter.FormatJson(snapshot));
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
