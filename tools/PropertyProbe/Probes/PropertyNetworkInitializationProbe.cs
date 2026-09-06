using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyNetworkInitializationProbe
{
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";

    private readonly PropertyPromotionExperimentProbe _promotionExperimentProbe;
    private bool _hasRun;

    public PropertyNetworkInitializationProbe(PropertyPromotionExperimentProbe promotionExperimentProbe)
    {
        _promotionExperimentProbe = promotionExperimentProbe;
    }

    public PropertyNetworkInitializationSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F13 network initialization experiment already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse FishNet initialization experiment. Ownership, spawning, and persistence are disabled.");

        if (!_promotionExperimentProbe.TryGetRegisteredExperiment(out var property, out var host))
            return WriteFailure("preconditions", "F11 registration did not pass in this game process.");

        var docksProperty = FindProperty(DocksWarehouseCode);
        if (docksProperty is null)
            return WriteFailure("preconditions", "Docks Warehouse was not found by property code.");

        var docksBefore = CaptureState(docksProperty);
        var targetNetworkObjectPresentBefore = host.GetComponent<NetworkObject>() is not null;

        try
        {
            var networkObject = host.GetComponent<NetworkObject>() ?? host.AddComponent<NetworkObject>();
            byte componentIndex = 0;
            networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
            property.NetworkInitializeIfDisabled();

            var targetAfter = CaptureState(property);
            var docksAfter = CaptureState(FindProperty(DocksWarehouseCode));
            var docksUnchanged = docksAfter.Matches(docksBefore);
            var initializationPassed = targetAfter.NetworkObjectPresent &&
                targetAfter.NetworkObjectResolved &&
                targetAfter.PropertyNetworked &&
                docksUnchanged;

            var snapshot = new PropertyNetworkInitializationSnapshot(
                TargetName: property.PropertyName,
                TargetPath: TargetPath,
                RegistrationPassed: true,
                TargetNetworkObjectPresentBefore: targetNetworkObjectPresentBefore,
                TargetNetworkObjectPresentAfter: targetAfter.NetworkObjectPresent,
                TargetPropertyNetworkObjectResolvedAfter: targetAfter.NetworkObjectResolved,
                TargetPropertyNetworkInitializedAfter: targetAfter.PropertyNetworked,
                TargetPropertyClientInitializedAfter: targetAfter.ClientInitialized,
                TargetPropertyServerInitializedAfter: targetAfter.ServerInitialized,
                TargetNetworkObjectSpawnedAfter: targetAfter.NetworkObjectSpawned,
                DocksNetworkObjectPresent: docksBefore.NetworkObjectPresent,
                DocksPropertyNetworkInitialized: docksBefore.PropertyNetworked,
                DocksPropertyClientInitialized: docksBefore.ClientInitialized,
                DocksPropertyServerInitialized: docksBefore.ServerInitialized,
                DocksNetworkObjectSpawned: docksBefore.NetworkObjectSpawned,
                Gate: initializationPassed ? "network-initialization" : "network-initialization-failed",
                FailureReason: initializationPassed
                    ? null
                    : "FishNet initialization or Docks Warehouse invariant check failed.");

            Write(snapshot);
            ProbeLog.Info(initializationPassed
                ? "FishNet initialization gate passed. Spawning, ownership, and persistence were not attempted."
                : "FishNet initialization gate failed. Spawning, ownership, and persistence were not attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            var targetAfter = CaptureState(property);
            var docksAfter = CaptureState(FindProperty(DocksWarehouseCode));
            var snapshot = new PropertyNetworkInitializationSnapshot(
                TargetName: property.PropertyName,
                TargetPath: TargetPath,
                RegistrationPassed: true,
                TargetNetworkObjectPresentBefore: targetNetworkObjectPresentBefore,
                TargetNetworkObjectPresentAfter: targetAfter.NetworkObjectPresent,
                TargetPropertyNetworkObjectResolvedAfter: targetAfter.NetworkObjectResolved,
                TargetPropertyNetworkInitializedAfter: targetAfter.PropertyNetworked,
                TargetPropertyClientInitializedAfter: targetAfter.ClientInitialized,
                TargetPropertyServerInitializedAfter: targetAfter.ServerInitialized,
                TargetNetworkObjectSpawnedAfter: targetAfter.NetworkObjectSpawned,
                DocksNetworkObjectPresent: docksBefore.NetworkObjectPresent,
                DocksPropertyNetworkInitialized: docksBefore.PropertyNetworked,
                DocksPropertyClientInitialized: docksBefore.ClientInitialized,
                DocksPropertyServerInitialized: docksBefore.ServerInitialized,
                DocksNetworkObjectSpawned: docksBefore.NetworkObjectSpawned,
                Gate: "network-initialization-failed",
                FailureReason: $"{ex}");

            Write(snapshot);
            ProbeLog.Warn($"FishNet initialization experiment stopped: {ex}");
            return snapshot;
        }
        finally
        {
            _promotionExperimentProbe.CleanupRegisteredExperiment();
        }
    }

    private static PropertyNetworkInitializationSnapshot WriteFailure(string gate, string reason)
    {
        var docks = FindProperty(DocksWarehouseCode);
        var docksState = CaptureState(docks);
        var snapshot = new PropertyNetworkInitializationSnapshot(
            TargetName: TargetName,
            TargetPath: TargetPath,
            RegistrationPassed: false,
            TargetNetworkObjectPresentBefore: false,
            TargetNetworkObjectPresentAfter: false,
            TargetPropertyNetworkObjectResolvedAfter: false,
            TargetPropertyNetworkInitializedAfter: false,
            TargetPropertyClientInitializedAfter: false,
            TargetPropertyServerInitializedAfter: false,
            TargetNetworkObjectSpawnedAfter: false,
            DocksNetworkObjectPresent: docksState.NetworkObjectPresent,
            DocksPropertyNetworkInitialized: docksState.PropertyNetworked,
            DocksPropertyClientInitialized: docksState.ClientInitialized,
            DocksPropertyServerInitialized: docksState.ServerInitialized,
            DocksNetworkObjectSpawned: docksState.NetworkObjectSpawned,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet initialization experiment stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(PropertyNetworkInitializationSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-network-initialization.txt",
            PropertyNetworkInitializationFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-network-initialization.json",
            PropertyNetworkInitializationFormatter.FormatJson(snapshot));
    }

    private static NetworkState CaptureState(Property? property)
    {
        if (property is null)
            return NetworkState.Empty;

        NetworkObject? networkObject = null;
        try
        {
            networkObject = property.NetworkObject;
        }
        catch
        {
            // A missing FishNet cache is the condition this probe is measuring.
        }

        var component = property.GetComponent<NetworkObject>();
        return new NetworkState(
            NetworkObjectPresent: component is not null,
            NetworkObjectResolved: networkObject is not null,
            PropertyNetworked: Read(() => property.IsNetworked),
            ClientInitialized: Read(() => property.IsClientInitialized),
            ServerInitialized: Read(() => property.IsServerInitialized),
            NetworkObjectSpawned: Read(() => (networkObject ?? component)?.IsSpawned ?? false));
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

    private readonly record struct NetworkState(
        bool NetworkObjectPresent,
        bool NetworkObjectResolved,
        bool PropertyNetworked,
        bool ClientInitialized,
        bool ServerInitialized,
        bool NetworkObjectSpawned)
    {
        public static NetworkState Empty => new(false, false, false, false, false, false);

        public bool Matches(NetworkState other) =>
            NetworkObjectPresent == other.NetworkObjectPresent &&
            NetworkObjectResolved == other.NetworkObjectResolved &&
            PropertyNetworked == other.PropertyNetworked &&
            ClientInitialized == other.ClientInitialized &&
            ServerInitialized == other.ServerInitialized &&
            NetworkObjectSpawned == other.NetworkObjectSpawned;
    }
}
