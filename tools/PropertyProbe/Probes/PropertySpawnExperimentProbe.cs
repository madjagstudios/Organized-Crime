using Il2CppFishNet;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertySpawnExperimentProbe
{
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";

    private readonly PropertyPromotionExperimentProbe _promotionExperimentProbe;
    private bool _hasRun;

    public PropertySpawnExperimentProbe(PropertyPromotionExperimentProbe promotionExperimentProbe)
    {
        _promotionExperimentProbe = promotionExperimentProbe;
    }

    public PropertySpawnExperimentSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F14 spawn experiment already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse FishNet spawn experiment. Ownership and persistence are disabled.");

        if (!_promotionExperimentProbe.TryGetRegisteredExperiment(out var property, out var host))
            return WriteFailure("preconditions", "F11 registration did not pass in this game process.");

        var docksProperty = FindProperty(DocksWarehouseCode);
        if (docksProperty is null)
            return WriteFailure("preconditions", "Docks Warehouse was not found by property code.");

        var docksBefore = CaptureState(docksProperty);
        var targetNetworkObjectPresentBefore = host.GetComponent<NetworkObject>() is not null;
        ServerManager? serverManager = null;
        NetworkObject? networkObject = null;
        var spawnAttempted = false;
        var spawnPassed = false;
        var cleanupAttempted = false;
        var cleanupPassed = true;
        PropertySpawnExperimentSnapshot? snapshot = null;

        try
        {
            networkObject = host.GetComponent<NetworkObject>() ?? host.AddComponent<NetworkObject>();
            byte componentIndex = 0;
            networkObject.UpdateNetworkBehaviours(null, ref componentIndex);
            property.NetworkInitializeIfDisabled();

            serverManager = InstanceFinder.ServerManager;
            var serverManagerPresent = serverManager is not null;
            var serverAvailable = serverManagerPresent && serverManager!.OneServerStarted();
            if (!serverManagerPresent || !serverAvailable)
            {
                snapshot = BuildSnapshot(
                    property,
                    docksBefore,
                    targetNetworkObjectPresentBefore,
                    serverManagerPresent,
                    serverAvailable,
                    spawnAttempted,
                    spawnPassed,
                    cleanupAttempted,
                    cleanupPassed,
                    "server-unavailable",
                    serverManagerPresent
                        ? "FishNet ServerManager is present but no server is started."
                        : "FishNet InstanceFinder.ServerManager is unavailable.");
            }
            else
            {
                spawnAttempted = true;
                ProbeLog.Info("Calling FishNet ServerManager.Spawn() for the temporary Fish Warehouse NetworkObject only.");
                try
                {
                    serverManager!.Spawn(networkObject, null, host.scene);
                    spawnPassed = Read(() => networkObject.IsSpawned) &&
                        Read(() => property.IsServerInitialized);
                }
                catch (Exception ex)
                {
                    snapshot = BuildSnapshot(
                        property,
                        docksBefore,
                        targetNetworkObjectPresentBefore,
                        serverManagerPresent,
                        serverAvailable,
                        spawnAttempted,
                        false,
                        cleanupAttempted,
                        cleanupPassed,
                        "spawn-failed",
                        $"FishNet ServerManager.Spawn() failed: {ex}");
                }

                if (snapshot is null)
                {
                    snapshot = BuildSnapshot(
                        property,
                        docksBefore,
                        targetNetworkObjectPresentBefore,
                        serverManagerPresent,
                        serverAvailable,
                        spawnAttempted,
                        spawnPassed,
                        cleanupAttempted,
                        cleanupPassed,
                        spawnPassed ? "spawn" : "spawn-failed",
                        spawnPassed ? null : "FishNet Spawn returned without a spawned/server-initialized target.");
                }
            }
        }
        catch (Exception ex)
        {
            snapshot ??= BuildSnapshot(
                property,
                docksBefore,
                targetNetworkObjectPresentBefore,
                serverManager is not null,
                serverManager is not null && Read(() => serverManager.OneServerStarted()),
                spawnAttempted,
                spawnPassed,
                cleanupAttempted,
                cleanupPassed,
                "spawn-failed",
                ex.ToString());
        }
        finally
        {
            cleanupAttempted = true;
            try
            {
                if (networkObject is not null && Read(() => networkObject.IsSpawned))
                {
                    if (serverManager is null)
                        throw new InvalidOperationException("Cannot despawn the temporary object because ServerManager is unavailable.");

                    serverManager.Despawn(networkObject, null);
                }
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                ProbeLog.Warn($"Temporary Fish Warehouse network despawn failed: {ex}");
            }

            try
            {
                _promotionExperimentProbe.CleanupRegisteredExperiment();
            }
            catch (Exception ex)
            {
                cleanupPassed = false;
                ProbeLog.Warn($"Temporary Fish Warehouse registration cleanup failed: {ex}");
            }

            if (networkObject is not null && Read(() => networkObject.IsSpawned))
                cleanupPassed = false;
        }

        var docksAfter = CaptureState(FindProperty(DocksWarehouseCode));
        var docksUnchanged = docksAfter.Matches(docksBefore);
        if (snapshot is not null)
        {
            snapshot = snapshot with
            {
                CleanupAttempted = cleanupAttempted,
                CleanupPassed = cleanupPassed,
                FailureReason = snapshot.FailureReason ?? (docksUnchanged
                    ? null
                    : "Docks Warehouse network-state invariant check failed.")
            };
        }
        else
        {
            snapshot = WriteFailure("spawn-failed", "Spawn experiment produced no report.");
        }

        Write(snapshot);
        ProbeLog.Info(snapshot.SpawnPassed
            ? "FishNet spawn gate passed. Ownership and persistence were not attempted."
            : "FishNet spawn gate did not pass. Ownership and persistence were not attempted.");
        return snapshot;
    }

    private static PropertySpawnExperimentSnapshot BuildSnapshot(
        Property property,
        NetworkState docksBefore,
        bool targetNetworkObjectPresentBefore,
        bool serverManagerPresent,
        bool serverAvailable,
        bool spawnAttempted,
        bool spawnPassed,
        bool cleanupAttempted,
        bool cleanupPassed,
        string gate,
        string? failureReason)
    {
        var target = CaptureState(property);
        return new PropertySpawnExperimentSnapshot(
            TargetName: property.PropertyName,
            TargetPath: TargetPath,
            RegistrationPassed: true,
            ServerManagerPresent: serverManagerPresent,
            ServerAvailable: serverAvailable,
            SpawnAttempted: spawnAttempted,
            SpawnPassed: spawnPassed,
            TargetNetworkObjectPresentBefore: targetNetworkObjectPresentBefore,
            TargetNetworkObjectPresentAfter: target.NetworkObjectPresent,
            TargetPropertyNetworkObjectResolvedAfter: target.NetworkObjectResolved,
            TargetPropertyNetworkInitializedAfter: target.PropertyNetworked,
            TargetPropertyClientInitializedAfter: target.ClientInitialized,
            TargetPropertyServerInitializedAfter: target.ServerInitialized,
            TargetNetworkObjectSpawnedAfter: target.NetworkObjectSpawned,
            CleanupAttempted: cleanupAttempted,
            CleanupPassed: cleanupPassed,
            DocksNetworkObjectPresent: docksBefore.NetworkObjectPresent,
            DocksPropertyNetworkInitialized: docksBefore.PropertyNetworked,
            DocksPropertyClientInitialized: docksBefore.ClientInitialized,
            DocksPropertyServerInitialized: docksBefore.ServerInitialized,
            DocksNetworkObjectSpawned: docksBefore.NetworkObjectSpawned,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: failureReason);
    }

    private static PropertySpawnExperimentSnapshot WriteFailure(string gate, string reason)
    {
        var docks = CaptureState(FindProperty(DocksWarehouseCode));
        var snapshot = new PropertySpawnExperimentSnapshot(
            TargetName: TargetName,
            TargetPath: TargetPath,
            RegistrationPassed: false,
            ServerManagerPresent: false,
            ServerAvailable: false,
            SpawnAttempted: false,
            SpawnPassed: false,
            TargetNetworkObjectPresentBefore: false,
            TargetNetworkObjectPresentAfter: false,
            TargetPropertyNetworkObjectResolvedAfter: false,
            TargetPropertyNetworkInitializedAfter: false,
            TargetPropertyClientInitializedAfter: false,
            TargetPropertyServerInitializedAfter: false,
            TargetNetworkObjectSpawnedAfter: false,
            CleanupAttempted: false,
            CleanupPassed: true,
            DocksNetworkObjectPresent: docks.NetworkObjectPresent,
            DocksPropertyNetworkInitialized: docks.PropertyNetworked,
            DocksPropertyClientInitialized: docks.ClientInitialized,
            DocksPropertyServerInitialized: docks.ServerInitialized,
            DocksNetworkObjectSpawned: docks.NetworkObjectSpawned,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"FishNet spawn experiment stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(PropertySpawnExperimentSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-spawn-experiment.txt",
            PropertySpawnExperimentFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-spawn-experiment.json",
            PropertySpawnExperimentFormatter.FormatJson(snapshot));
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
            // A missing FishNet cache is a measured state for this probe.
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
