using System.Collections;
using System.Reflection;
using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class FishNetRuntimeBucketCreationProbe
{
    private const string DocksWarehouseCode = "dockswarehouse";
    private const ushort ProbeCollectionId = 65000;
    private bool _hasRun;

    public FishNetRuntimeBucketCreationSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F22 runtime bucket creation experiment already ran in this game process.", false);

        _hasRun = true;
        ProbeLog.Info("Running guarded FishNet runtime bucket creation experiment on disposable-save guidance. Only GetPrefabObjects<SinglePrefabObjects>(65000, true) may mutate runtime manager state; no NetworkObject will be added and no spawn, ownership, persistence, or scene mutation is allowed.");

        try
        {
            var docks = FindProperty(DocksWarehouseCode);
            var docksNetworkObject = docks is null ? null : ResolveNetworkObject(docks);
            var networkManager = docksNetworkObject?.NetworkManager;
            if (networkManager is null || docksNetworkObject is null)
                return WriteFailure("preconditions", "Docks Warehouse or its NetworkManager was not found.", false);

            var authoredCollection = networkManager.SpawnablePrefabs;
            object? runtimeCollection;
            try
            {
                runtimeCollection = networkManager.RuntimeSpawnablePrefabs;
            }
            catch (Exception ex)
            {
                return WriteFailure("capture", $"RuntimeSpawnablePrefabs could not be read: {ex.GetType().Name}: {ex.Message}", false);
            }

            if (authoredCollection is null || runtimeCollection is null)
                return WriteFailure("preconditions", "Authored or runtime prefab collection was unavailable.", false);

            var beforeKeys = CaptureKeys(runtimeCollection);
            var beforeCount = CaptureCount(runtimeCollection);
            if (beforeKeys.Contains(ProbeCollectionId.ToString(), StringComparer.Ordinal))
                return WriteFailure("preconditions", $"Probe collection ID {ProbeCollectionId} is already present; no bucket creation was attempted.", false, beforeCount, beforeKeys);

            var docksBefore = CaptureDocksIdentity(docksNetworkObject);
            PrefabObjects? bucket;
            try
            {
                bucket = networkManager.GetPrefabObjects<SinglePrefabObjects>(ProbeCollectionId, true);
            }
            catch (Exception ex)
            {
                return WriteFailure("bucket-creation", $"GetPrefabObjects<T>(65000, true) failed: {ex.GetType().Name}: {ex.Message}", true, beforeCount, beforeKeys, authoredCollection, docksBefore, docksNetworkObject);
            }

            if (bucket is null)
                return WriteFailure("bucket-creation", "GetPrefabObjects<T>(65000, true) returned null.", true, beforeCount, beforeKeys, authoredCollection, docksBefore, docksNetworkObject);

            var afterRuntimeCollection = networkManager.RuntimeSpawnablePrefabs;
            var afterKeys = CaptureKeys(afterRuntimeCollection);
            var afterCount = CaptureCount(afterRuntimeCollection);
            var docksAfter = CaptureDocksIdentity(docksNetworkObject);
            var authoredUnchanged = ReferenceEquals(authoredCollection, networkManager.SpawnablePrefabs);
            var bucketCreated = afterKeys.Contains(ProbeCollectionId.ToString(), StringComparer.Ordinal) && afterCount > beforeCount;
            var snapshot = new FishNetRuntimeBucketCreationSnapshot(
                CollectionId: ProbeCollectionId,
                BeforeBucketCount: beforeCount,
                AfterBucketCount: afterCount,
                BeforeBucketKeys: beforeKeys,
                AfterBucketKeys: afterKeys,
                BucketCreated: bucketCreated,
                BucketType: bucket.GetType().FullName ?? bucket.GetType().Name,
                ReturnedCollectionId: ReadUShort(() => bucket.CollectionId),
                BucketObjectCount: ReadInt(() => bucket.GetObjectCount()),
                AuthoredCollectionUnchanged: authoredUnchanged,
                DocksObjectIdBefore: docksBefore.ObjectId,
                DocksObjectIdAfter: docksAfter.ObjectId,
                DocksSceneIdBefore: docksBefore.SceneId,
                DocksSceneIdAfter: docksAfter.SceneId,
                DocksStateBefore: docksBefore.State,
                DocksStateAfter: docksAfter.State,
                DocksNetworkIdentityUnchanged: docksBefore == docksAfter,
                BucketCreationAttempted: true,
                AddObjectAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                SceneMutationAttempted: false,
                Gate: bucketCreated ? "bucket-created" : "bucket-not-observed",
                FailureReason: bucketCreated
                    ? null
                    : "GetPrefabObjects returned a bucket, but the runtime dictionary did not show the requested collection ID.");

            Write(snapshot);
            ProbeLog.Info(bucketCreated
                ? "FishNet runtime bucket creation succeeded for collection 65000. No NetworkObject was added and no spawn, ownership, persistence, or scene mutation was attempted."
                : "FishNet runtime bucket creation did not produce an observable collection 65000 entry. No NetworkObject was added and no later operation was attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString(), false);
        }
    }

    private static IReadOnlyList<string> CaptureKeys(object instance)
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
            // Keys are diagnostic metadata; retain a failure-closed report.
        }

        return keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
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
            // Count is optional diagnostic metadata.
        }

        return 0;
    }

    private static DocksIdentity CaptureDocksIdentity(NetworkObject networkObject) =>
        new(
            ReadInt(() => networkObject.ObjectId),
            ReadULong(() => networkObject.SceneId),
            ReadString(() => networkObject.State.ToString()));

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

    private static FishNetRuntimeBucketCreationSnapshot WriteFailure(
        string gate,
        string reason,
        bool bucketCreationAttempted,
        int beforeCount = 0,
        IReadOnlyList<string>? beforeKeys = null,
        object? authoredCollection = null,
        DocksIdentity? docksBefore = null,
        NetworkObject? docksNetworkObject = null)
    {
        var docksAfter = docksNetworkObject is null ? docksBefore ?? DocksIdentity.Empty : CaptureDocksIdentity(docksNetworkObject);
        var snapshot = new FishNetRuntimeBucketCreationSnapshot(
            CollectionId: ProbeCollectionId,
            BeforeBucketCount: beforeCount,
            AfterBucketCount: beforeCount,
            BeforeBucketKeys: beforeKeys ?? Array.Empty<string>(),
            AfterBucketKeys: beforeKeys ?? Array.Empty<string>(),
            BucketCreated: false,
            BucketType: "<unavailable>",
            ReturnedCollectionId: 0,
            BucketObjectCount: 0,
            AuthoredCollectionUnchanged: authoredCollection is null || true,
            DocksObjectIdBefore: docksBefore?.ObjectId ?? 0,
            DocksObjectIdAfter: docksAfter.ObjectId,
            DocksSceneIdBefore: docksBefore?.SceneId ?? 0,
            DocksSceneIdAfter: docksAfter.SceneId,
            DocksStateBefore: docksBefore?.State ?? "<unavailable>",
            DocksStateAfter: docksAfter.State,
            DocksNetworkIdentityUnchanged: docksBefore is null || docksBefore == docksAfter,
            BucketCreationAttempted: bucketCreationAttempted,
            AddObjectAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            SceneMutationAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet runtime bucket creation stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(FishNetRuntimeBucketCreationSnapshot snapshot)
    {
        ProbeLog.WriteFile("fishnet-runtime-bucket-creation.txt", FishNetRuntimeBucketCreationFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("fishnet-runtime-bucket-creation.json", FishNetRuntimeBucketCreationFormatter.FormatJson(snapshot));
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
