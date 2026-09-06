using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyNetworkRootInspectionProbe
{
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";

    private readonly PropertyPromotionExperimentProbe _promotionExperimentProbe;
    private bool _hasRun;

    public PropertyNetworkRootInspectionProbe(PropertyPromotionExperimentProbe promotionExperimentProbe)
    {
        _promotionExperimentProbe = promotionExperimentProbe;
    }

    public PropertyNetworkRootInspectionSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F16 root NetworkObject inspection already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse NetworkObject root inspection. Spawn, ownership, and persistence are disabled.");

        if (!_promotionExperimentProbe.TryGetRegisteredExperiment(out _, out _))
            return WriteFailure("preconditions", "F11 registration did not pass in this game process.");

        var target = FindTarget();
        if (target is null)
            return WriteFailure("preconditions", $"Could not find target GameObject at {TargetPath}.");

        PropertyNetworkRootInspectionSnapshot? snapshot = null;
        var cleanupAttempted = false;
        var cleanupPassed = true;

        try
        {
            var chain = new List<NetworkRootChainEntry>();
            Transform? current = target.transform;
            while (current is not null)
            {
                var localNetworkObject = current.GetComponent<NetworkObject>();
                var parentNetworkObject = FindParentNetworkObject(current.parent);
                var identity = localNetworkObject ?? parentNetworkObject;
                chain.Add(new NetworkRootChainEntry(
                    Name: current.name,
                    Path: GetTransformPath(current),
                    LocalNetworkObjectPresent: localNetworkObject is not null,
                    ParentNetworkObjectResolved: parentNetworkObject is not null,
                    Metadata: CaptureMetadata(identity)));
                current = current.parent;
            }

            var capturePassed = chain.Count > 0;
            snapshot = new PropertyNetworkRootInspectionSnapshot(
                TargetName: target.name,
                TargetPath: GetTransformPath(target.transform),
                RegistrationPassed: true,
                MetadataCapturePassed: capturePassed,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                Chain: chain,
                Gate: capturePassed ? "metadata" : "metadata-failed",
                FailureReason: capturePassed ? null : "No transform chain entries were captured.");
        }
        catch (Exception ex)
        {
            snapshot = new PropertyNetworkRootInspectionSnapshot(
                TargetName: target.name,
                TargetPath: GetTransformPath(target.transform),
                RegistrationPassed: true,
                MetadataCapturePassed: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                Chain: Array.Empty<NetworkRootChainEntry>(),
                Gate: "metadata-failed",
                FailureReason: ex.ToString());
        }
        finally
        {
            cleanupAttempted = true;
            try
            {
                _promotionExperimentProbe.CleanupRegisteredExperiment();
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                ProbeLog.Warn($"Temporary Fish Warehouse root-inspection cleanup failed: {ex}");
            }
        }

        if (snapshot is not null)
        {
            snapshot = snapshot with
            {
                CleanupAttempted = cleanupAttempted,
                CleanupPassed = cleanupPassed
            };
        }
        else
        {
            snapshot = WriteFailure("metadata-failed", "Root inspection produced no report.");
        }

        Write(snapshot);
        ProbeLog.Info(snapshot.MetadataCapturePassed
            ? "Fish Warehouse root NetworkObject inspection passed. Spawn, ownership, and persistence were not attempted."
            : "Fish Warehouse root NetworkObject inspection did not pass. Spawn, ownership, and persistence were not attempted.");
        return snapshot;
    }

    private static NetworkObjectMetadata CaptureMetadata(NetworkObject? networkObject)
    {
        if (networkObject is null)
            return NetworkObjectMetadata.Empty;

        return new NetworkObjectMetadata(
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
            SerializedJson: ReadString(() => JsonUtility.ToJson(networkObject)));
    }

    private static NetworkObject? FindParentNetworkObject(Transform? parent)
    {
        var current = parent;
        while (current is not null)
        {
            var networkObject = current.GetComponent<NetworkObject>();
            if (networkObject is not null)
                return networkObject;

            current = current.parent;
        }

        return null;
    }

    private static GameObject? FindTarget()
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is null || !gameObject.scene.IsValid())
                continue;

            if (string.Equals(GetTransformPath(gameObject.transform), TargetPath, StringComparison.Ordinal))
                return gameObject;
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

    private static PropertyNetworkRootInspectionSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = new PropertyNetworkRootInspectionSnapshot(
            TargetName: TargetName,
            TargetPath: TargetPath,
            RegistrationPassed: false,
            MetadataCapturePassed: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            CleanupAttempted: false,
            CleanupPassed: true,
            Chain: Array.Empty<NetworkRootChainEntry>(),
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"Fish Warehouse root inspection stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(PropertyNetworkRootInspectionSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-network-root-inspection.txt",
            PropertyNetworkRootInspectionFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-network-root-inspection.json",
            PropertyNetworkRootInspectionFormatter.FormatJson(snapshot));
    }

    private static bool Read(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return false;
        }
    }

    private static int ReadInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return 0;
        }
    }

    private static ushort ReadUShort(Func<ushort> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return 0;
        }
    }

    private static ulong ReadULong(Func<ulong> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.GetType().Name}>";
        }
    }
}
