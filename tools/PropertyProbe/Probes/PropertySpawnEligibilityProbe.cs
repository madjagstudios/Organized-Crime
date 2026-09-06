using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertySpawnEligibilityProbe
{
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";

    private readonly PropertyPromotionExperimentProbe _promotionExperimentProbe;
    private bool _hasRun;

    public PropertySpawnEligibilityProbe(PropertyPromotionExperimentProbe promotionExperimentProbe)
    {
        _promotionExperimentProbe = promotionExperimentProbe;
    }

    public PropertySpawnEligibilitySnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F15 spawn-eligibility preflight already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse FishNet spawn-eligibility metadata preflight. Spawn, ownership, and persistence are disabled.");

        if (!_promotionExperimentProbe.TryGetRegisteredExperiment(out var property, out var host))
            return WriteFailure("preconditions", "F11 registration did not pass in this game process.");

        var docksProperty = FindProperty(DocksWarehouseCode);
        if (docksProperty is null)
            return WriteFailure("preconditions", "Docks Warehouse was not found by property code.");

        var docksBefore = CaptureMetadata(ResolveNetworkObject(docksProperty));
        PropertySpawnEligibilitySnapshot? snapshot = null;
        var cleanupAttempted = false;
        var cleanupPassed = true;

        try
        {
            var networkObject = host.GetComponent<NetworkObject>() ?? host.AddComponent<NetworkObject>();
            byte componentIndex = 0;
            networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
            property.NetworkInitializeIfDisabled();

            var targetMetadata = CaptureMetadata(ResolveNetworkObject(property));
            var capturePassed = targetMetadata.Present &&
                targetMetadata.Networked &&
                targetMetadata.NetworkBehaviourCount > 0 &&
                docksBefore.Present;

            snapshot = new PropertySpawnEligibilitySnapshot(
                TargetName: property.PropertyName,
                TargetPath: TargetPath,
                RegistrationPassed: true,
                MetadataCapturePassed: capturePassed,
                SpawnAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                TargetMetadata: targetMetadata,
                DocksMetadata: docksBefore,
                Gate: capturePassed ? "metadata" : "metadata-failed",
                FailureReason: capturePassed
                    ? null
                    : "Required target or Docks NetworkObject metadata was unavailable.");
        }
        catch (Exception ex)
        {
            snapshot = new PropertySpawnEligibilitySnapshot(
                TargetName: property.PropertyName,
                TargetPath: TargetPath,
                RegistrationPassed: true,
                MetadataCapturePassed: false,
                SpawnAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                TargetMetadata: NetworkObjectMetadata.Empty,
                DocksMetadata: docksBefore,
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
                ProbeLog.Warn($"Temporary Fish Warehouse metadata cleanup failed: {ex}");
            }
        }

        var docksAfter = CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode)));
        var docksUnchanged = docksAfter == docksBefore;
        if (snapshot is not null)
        {
            snapshot = snapshot with
            {
                CleanupAttempted = cleanupAttempted,
                CleanupPassed = cleanupPassed,
                DocksMetadata = docksAfter,
                FailureReason = snapshot.FailureReason ?? (docksUnchanged
                    ? null
                    : "Docks Warehouse NetworkObject metadata changed during preflight.")
            };
        }
        else
        {
            snapshot = WriteFailure("metadata-failed", "Metadata preflight produced no report.");
        }

        Write(snapshot);
        ProbeLog.Info(snapshot.MetadataCapturePassed
            ? "FishNet metadata preflight passed. Spawn, ownership, and persistence were not attempted."
            : "FishNet metadata preflight did not pass. Spawn, ownership, and persistence were not attempted.");
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

    private static NetworkObject? ResolveNetworkObject(Property? property)
    {
        if (property is null)
            return null;

        try
        {
            if (property.NetworkObject is not null)
                return property.NetworkObject;
        }
        catch
        {
            // Fall through to the component lookup for diagnostic capture.
        }

        return property.GetComponent<NetworkObject>();
    }

    private static PropertySpawnEligibilitySnapshot WriteFailure(string gate, string reason)
    {
        var docks = CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode)));
        var snapshot = new PropertySpawnEligibilitySnapshot(
            TargetName: TargetName,
            TargetPath: TargetPath,
            RegistrationPassed: false,
            MetadataCapturePassed: false,
            SpawnAttempted: false,
            CleanupAttempted: false,
            CleanupPassed: true,
            TargetMetadata: NetworkObjectMetadata.Empty,
            DocksMetadata: docks,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet eligibility preflight stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(PropertySpawnEligibilitySnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-spawn-eligibility.txt",
            PropertySpawnEligibilityFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-spawn-eligibility.json",
            PropertySpawnEligibilityFormatter.FormatJson(snapshot));
    }

    private static Property? FindProperty(string code)
    {
        if (Property.Properties is null)
            return null;

        foreach (var property in Property.Properties.ToArray())
        {
            if (string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
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
