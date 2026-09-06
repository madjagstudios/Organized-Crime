using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetRuntimeBucketLifecycleProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const ushort ProbeCollectionId = 65000;
    private bool _hasRun;

    public FishNetRuntimeBucketLifecycleSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F23 runtime bucket lifecycle already ran in this game process.", false, false);

        _hasRun = true;
        ProbeLog.Info("Running guarded FishNet runtime bucket lifecycle experiment on a disposable save. Only runtime bucket create/retrieve/remove is allowed; no NetworkObject registration, spawn, ownership, persistence, or scene mutation is permitted.");

        try
        {
            var docks = FindProperty(DocksWarehouseCode);
            var docksNetworkObject = docks is null ? null : ResolveNetworkObject(docks);
            var networkManager = docksNetworkObject?.NetworkManager;
            if (networkManager is null || docksNetworkObject is null)
                return WriteFailure("preconditions", "Docks Warehouse or its NetworkManager was not found.", false, false);

            var authoredCollection = networkManager.SpawnablePrefabs;
            if (authoredCollection is null)
                return WriteFailure("preconditions", "Authored SpawnablePrefabs was unavailable.", false, false);

            var docksBefore = CaptureDocksIdentity(docksNetworkObject);
            PrefabObjects? existing;
            try
            {
                existing = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false);
            }
            catch (Exception ex)
            {
                return WriteFailure("preconditions", $"Initial no-create bucket lookup failed: {ex.GetType().Name}: {ex.Message}", false, false);
            }

            if (existing is not null)
                return WriteFailure("preconditions", $"Probe collection ID {ProbeCollectionId} already existed; no creation or removal was attempted.", false, false);

            PrefabObjects? created;
            try
            {
                created = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, true);
            }
            catch (Exception ex)
            {
                return WriteFailure("create", $"Runtime bucket creation failed: {ex.GetType().Name}: {ex.Message}", true, false);
            }

            if (created is null)
                return WriteFailure("create", "Runtime bucket creation returned null; removal was not attempted.", true, false);

            var createdInstanceId = ReadInt(() => created.GetInstanceID());
            var createdCollectionId = ReadUShort(() => created.CollectionId);
            PrefabObjects? retrieved;
            try
            {
                retrieved = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false);
            }
            catch (Exception ex)
            {
                return WriteFailure("retrieve", $"Runtime bucket retrieval failed: {ex.GetType().Name}: {ex.Message}", true, false, created, createdInstanceId, createdCollectionId, authoredCollection, docksBefore, docksNetworkObject);
            }

            var retrievedInstanceId = retrieved is null ? 0 : ReadInt(() => retrieved.GetInstanceID());
            var retrieveSame = retrieved is not null && retrievedInstanceId == createdInstanceId && ReadUShort(() => retrieved.CollectionId) == createdCollectionId;
            bool removed;
            try
            {
                removed = networkManager.RemoveSpawnableCollection(ProbeCollectionId);
            }
            catch (Exception ex)
            {
                return WriteFailure("remove", $"RemoveSpawnableCollection(65000) failed: {ex.GetType().Name}: {ex.Message}", true, true, created, createdInstanceId, createdCollectionId, authoredCollection, docksBefore, docksNetworkObject, retrievedInstanceId, retrieveSame);
            }

            PrefabObjects? afterRemoval;
            try
            {
                afterRemoval = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, false);
            }
            catch (Exception ex)
            {
                return WriteFailure("verify-removal", $"Post-removal no-create lookup failed: {ex.GetType().Name}: {ex.Message}", true, true, created, createdInstanceId, createdCollectionId, authoredCollection, docksBefore, docksNetworkObject, retrievedInstanceId, retrieveSame, removed);
            }

            var docksAfter = CaptureDocksIdentity(docksNetworkObject);
            var snapshot = new FishNetRuntimeBucketLifecycleSnapshot(
                CollectionId: ProbeCollectionId,
                CreateReturnedBucket: true,
                RetrieveReturnedSameBucket: retrieveSame,
                RemovalReturnedTrue: removed,
                PostRemovalReturnedBucket: afterRemoval is not null,
                BucketType: created.GetType().FullName ?? created.GetType().Name,
                CreatedBucketInstanceId: createdInstanceId,
                RetrievedBucketInstanceId: retrievedInstanceId,
                AuthoredCollectionUnchanged: ReferenceEquals(authoredCollection, networkManager.SpawnablePrefabs),
                DocksNetworkIdentityUnchanged: docksBefore == docksAfter,
                BucketCreationAttempted: true,
                RemovalAttempted: true,
                AddObjectAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                SceneMutationAttempted: false,
                Gate: retrieveSame && removed && afterRemoval is null ? "lifecycle-passed" : "lifecycle-partial",
                FailureReason: retrieveSame && removed && afterRemoval is null
                    ? null
                    : "Runtime bucket lifecycle did not satisfy create/retrieve/remove/post-removal expectations.");

            Write(snapshot);
            ProbeLog.Info(snapshot.Gate == "lifecycle-passed"
                ? "FishNet runtime bucket create/retrieve/remove lifecycle passed. No NetworkObject was registered or spawned."
                : "FishNet runtime bucket lifecycle was partial. No NetworkObject was registered or spawned.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString(), false, false);
        }
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

    private static FishNetRuntimeBucketLifecycleSnapshot WriteFailure(
        string gate,
        string reason,
        bool bucketCreationAttempted,
        bool removalAttempted,
        PrefabObjects? created = null,
        int createdInstanceId = 0,
        ushort createdCollectionId = 0,
        object? authoredCollection = null,
        DocksIdentity? docksBefore = null,
        NetworkObject? docksNetworkObject = null,
        int retrievedInstanceId = 0,
        bool retrieveSame = false,
        bool removalReturnedTrue = false)
    {
        var docksAfter = docksNetworkObject is null ? docksBefore ?? DocksIdentity.Empty : CaptureDocksIdentity(docksNetworkObject);
        var snapshot = new FishNetRuntimeBucketLifecycleSnapshot(
            CollectionId: ProbeCollectionId,
            CreateReturnedBucket: created is not null,
            RetrieveReturnedSameBucket: retrieveSame,
            RemovalReturnedTrue: removalReturnedTrue,
            PostRemovalReturnedBucket: false,
            BucketType: created?.GetType().FullName ?? "<unavailable>",
            CreatedBucketInstanceId: createdInstanceId,
            RetrievedBucketInstanceId: retrievedInstanceId,
            AuthoredCollectionUnchanged: authoredCollection is null || false,
            DocksNetworkIdentityUnchanged: docksBefore is null || docksBefore == docksAfter,
            BucketCreationAttempted: bucketCreationAttempted,
            RemovalAttempted: removalAttempted,
            AddObjectAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            SceneMutationAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet runtime bucket lifecycle stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(FishNetRuntimeBucketLifecycleSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-runtime-bucket-lifecycle.txt", FishNetRuntimeBucketLifecycleFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-runtime-bucket-lifecycle.json", FishNetRuntimeBucketLifecycleFormatter.FormatJson(snapshot));
    }

    private static int ReadInt(Func<int> read) { try { return read(); } catch { return 0; } }
    private static ushort ReadUShort(Func<ushort> read) { try { return read(); } catch { return 0; } }
    private static ulong ReadULong(Func<ulong> read) { try { return read(); } catch { return 0; } }
    private static string ReadString(Func<string> read) { try { return read(); } catch { return "<unavailable>"; } }

    private sealed record DocksIdentity(int ObjectId, ulong SceneId, string State)
    {
        public static DocksIdentity Empty => new(0, 0, "<unavailable>");
    }
}
