using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Probes;
using OrganizedCrime.PropertyProbe.Runtime;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe;

internal static class ProbeCoordinator
{
    private static PropertyInventoryProbe? _inventoryProbe;
    private static BuildingCensusProbe? _buildingCensusProbe;
    private static PropertyPromotionPreflightProbe? _propertyPromotionPreflightProbe;
    private static PropertyPromotionExperimentProbe? _propertyPromotionExperimentProbe;
    private static PropertyNetworkInitializationProbe? _propertyNetworkInitializationProbe;
    private static PropertySpawnExperimentProbe? _propertySpawnExperimentProbe;
    private static PropertySpawnEligibilityProbe? _propertySpawnEligibilityProbe;
    private static PropertyNetworkRootInspectionProbe? _propertyNetworkRootInspectionProbe;
    private static PropertyDirectAttachmentProbe? _propertyDirectAttachmentProbe;
    private static PropertyRootTopologyProbe? _propertyRootTopologyProbe;
    private static SafehouseCensusProbe? _safehouseCensusProbe;
    private static FishNetCapabilityProbe? _fishNetCapabilityProbe;
    private static FishNetPrefabObjectsProbe? _fishNetPrefabObjectsProbe;
    private static FishNetRuntimePrefabCollectionProbe? _fishNetRuntimePrefabCollectionProbe;
    private static FishNetRuntimeBucketCreationProbe? _fishNetRuntimeBucketCreationProbe;
    private static FishNetRuntimeBucketLifecycleProbe? _fishNetRuntimeBucketLifecycleProbe;
    private static FishNetRuntimePrefabRegistrationProbe? _fishNetRuntimePrefabRegistrationProbe;
    private static FishNetRuntimePrefabSpawnProbe? _fishNetRuntimePrefabSpawnProbe;
    private static RuntimePropertyVerticalSliceProbe? _runtimePropertyVerticalSliceProbe;
    private static NightclubTimingDiagnosticProbe? _nightclubTimingDiagnosticProbe;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        _inventoryProbe = new PropertyInventoryProbe(new DirectGamePropertyAdapter());
        _buildingCensusProbe = new BuildingCensusProbe();
        _propertyPromotionPreflightProbe = new PropertyPromotionPreflightProbe();
        _propertyPromotionExperimentProbe = new PropertyPromotionExperimentProbe();
        _propertyNetworkInitializationProbe = new PropertyNetworkInitializationProbe(_propertyPromotionExperimentProbe);
        _propertySpawnExperimentProbe = new PropertySpawnExperimentProbe(_propertyPromotionExperimentProbe);
        _propertySpawnEligibilityProbe = new PropertySpawnEligibilityProbe(_propertyPromotionExperimentProbe);
        _propertyNetworkRootInspectionProbe = new PropertyNetworkRootInspectionProbe(_propertyPromotionExperimentProbe);
        _propertyDirectAttachmentProbe = new PropertyDirectAttachmentProbe(_propertyPromotionExperimentProbe);
        _propertyRootTopologyProbe = new PropertyRootTopologyProbe();
        _safehouseCensusProbe = new SafehouseCensusProbe();
        _fishNetCapabilityProbe = new FishNetCapabilityProbe();
        _fishNetPrefabObjectsProbe = new FishNetPrefabObjectsProbe();
        _fishNetRuntimePrefabCollectionProbe = new FishNetRuntimePrefabCollectionProbe();
        _fishNetRuntimeBucketCreationProbe = new FishNetRuntimeBucketCreationProbe();
        _fishNetRuntimeBucketLifecycleProbe = new FishNetRuntimeBucketLifecycleProbe();
        _fishNetRuntimePrefabRegistrationProbe = new FishNetRuntimePrefabRegistrationProbe();
        _fishNetRuntimePrefabSpawnProbe = new FishNetRuntimePrefabSpawnProbe();
        _runtimePropertyVerticalSliceProbe = new RuntimePropertyVerticalSliceProbe();
        _nightclubTimingDiagnosticProbe = new NightclubTimingDiagnosticProbe();

        ProbeLog.Info("Property probe coordinator initialized.");
        ProbeLog.Info("Press F7 after loading a playable save to inventory properties.");
        ProbeLog.Info("Press F8 after loading a playable save to census candidate building objects.");
        ProbeLog.Info("Press F9 while facing a candidate safehouse entrance to capture its anchor, nearby structural bounds, and registered Property/Business conflicts. Press Shift+F9 from inside to attach one optional interior point to the latest candidate. Up to three candidates are retained; no candidate is approved automatically.");
        ProbeLog.Info("Press F10 to run the read-only Fish Warehouse property-promotion preflight.");
        ProbeLog.Info("Press F11 only on a disposable save to run the gated Fish Warehouse registration experiment.");
        ProbeLog.Info("Press F12 after a successful F11 registration run on a disposable save to test Fish Warehouse ownership.");
        ProbeLog.Info("Press Insert after a successful F11 registration run on a disposable save to inspect FishNet initialization.");
        ProbeLog.Info("Press Delete after a successful F11 registration run on a disposable save to test FishNet spawning only.");
        ProbeLog.Info("Press End after a successful F11 registration run on a disposable save to inspect FishNet spawn eligibility metadata.");
        ProbeLog.Info("Press PageUp after a successful F11 registration run on a disposable save to inspect the Fish Warehouse NetworkObject root chain.");
        ProbeLog.Info("Press Home after a successful F11 registration run on a disposable save to test direct temporary Property attachment.");
        ProbeLog.Info("Press PageDown after loading a playable save to run the read-only property root topology census.");
        ProbeLog.Info("Press F6 (or F17) after loading a playable save to enumerate registered vanilla Properties and descendant StorageEntity objects; no ownership, item, save, registration, geometry, or Fish Warehouse mutation is attempted.");
        ProbeLog.Info("Press Pause/Break after loading a playable save to inspect FishNet prefab-registration capability without mutation.");
        ProbeLog.Info("Press KeypadPlus (or F20) after loading a playable save to inspect the concrete FishNet PrefabObjects API without invoking it.");
        ProbeLog.Info("Press KeypadMultiply (or F21) after loading a playable save to inspect FishNet RuntimeSpawnablePrefabs without invoking it.");
        ProbeLog.Info("Press KeypadDivide (or F22) on a disposable save in a fresh process to create only runtime prefab bucket 65000; no prefab registration or spawning is attempted.");
        ProbeLog.Info("Press KeypadEnter (or F23) on a disposable save in a fresh process to verify runtime bucket 65000 create/retrieve/remove cleanup; no prefab registration or spawning is attempted.");
        ProbeLog.Info("Press Left Bracket (or F24) on a disposable save in a fresh process to test one temporary NetworkObject AddObject registration; spawn, ownership, and persistence are disabled.");
        ProbeLog.Info("Press Right Bracket on a disposable save in a fresh process to test temporary FishNet prefab spawn/despawn; ownership, persistence, and Property mutation are disabled. This is the F25 probe; Unity exposes no F25 enum in this build.");
        ProbeLog.Info("Press Backslash on a disposable save in a fresh process to run the combined runtime Property vertical slice: registration, network initialization, spawn, and cleanup only; ownership, persistence, and save writes are disabled.");
        ProbeLog.Info("Press F5 while standing at the approved Nightclub shell door to run one bounded, read-only door/menu timing diagnostic on the authoritative single-player host; interact manually twice and wait for completion.");

        foreach (var capability in S1ApiCapabilityDetector.Detect())
        {
            ProbeLog.Info(
                $"Optional API {capability.AssemblyName}: " +
                (capability.IsLoaded
                    ? $"loaded (version {capability.Version ?? "unknown"})"
                    : "not loaded"));
        }
    }

    public static void Update()
    {
        if (!_initialized)
            return;

        _nightclubTimingDiagnosticProbe?.Tick();

        if (Input.GetKeyDown(KeyCode.F5))
        {
            try
            {
                _nightclubTimingDiagnosticProbe!.Start();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Nightclub door/menu timing diagnostic failed to start: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            if (Property.Properties is null || Property.Properties.Count == 0)
            {
                ProbeLog.Warn("No runtime properties are registered yet. Load a playable save and press F7 again.");
            }
            else
            {
                try
                {
                    _inventoryProbe!.Run();
                }
                catch (Exception ex)
                {
                    ProbeLog.Error($"Property inventory failed: {ex}");
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            try
            {
                _buildingCensusProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Building census failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            try
            {
                var shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                var marker = SafehouseLocationCaptureTrigger.GetMarker(f9Pressed: true, shiftPressed);
                _buildingCensusProbe!.RunAtPlayerLocation(marker);
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Safehouse location capture failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.F10))
        {
            try
            {
                _propertyPromotionPreflightProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property-promotion preflight failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.F11))
        {
            try
            {
                _propertyPromotionExperimentProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property-promotion experiment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.F12))
        {
            try
            {
                _propertyPromotionExperimentProbe!.RunOwnership();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property-ownership experiment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.F13))
        {
            try
            {
                _propertyNetworkInitializationProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property network-initialization experiment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.F14))
        {
            try
            {
                _propertySpawnExperimentProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property spawn experiment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.End) || Input.GetKeyDown(KeyCode.F15))
        {
            try
            {
                _propertySpawnEligibilityProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property spawn-eligibility preflight failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.F16))
        {
            try
            {
                _propertyNetworkRootInspectionProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property NetworkObject root inspection failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Home))
        {
            try
            {
                _propertyDirectAttachmentProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Direct Fish Warehouse Property attachment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.F18))
        {
            try
            {
                _propertyRootTopologyProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Property root topology census failed: {ex}");
            }
        }

        if (SafehouseCensusTrigger.IsPressed(
                Input.GetKeyDown(KeyCode.F6),
                Input.GetKeyDown(KeyCode.F17)))
        {
            try
            {
                _safehouseCensusProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Safehouse Property/StorageEntity census failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Pause) || Input.GetKeyDown(KeyCode.F19))
        {
            try
            {
                _fishNetCapabilityProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet capability preflight failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.F20))
        {
            try
            {
                _fishNetPrefabObjectsProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet PrefabObjects inspection failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.KeypadMultiply) || Input.GetKeyDown(KeyCode.F21))
        {
            try
            {
                _fishNetRuntimePrefabCollectionProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet runtime prefab collection inspection failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.KeypadDivide) || Input.GetKeyDown(KeyCode.F22))
        {
            try
            {
                _fishNetRuntimeBucketCreationProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet runtime bucket creation experiment failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.F23))
        {
            try
            {
                _fishNetRuntimeBucketLifecycleProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet runtime bucket lifecycle failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.F24))
        {
            try
            {
                _fishNetRuntimePrefabRegistrationProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet runtime prefab registration failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            try
            {
                _fishNetRuntimePrefabSpawnProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"FishNet runtime prefab spawn failed: {ex}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            try
            {
                _runtimePropertyVerticalSliceProbe!.Run();
            }
            catch (Exception ex)
            {
                ProbeLog.Error($"Runtime Property vertical slice failed: {ex}");
            }
        }
    }

    public static void Dispose()
    {
        _nightclubTimingDiagnosticProbe?.Dispose(NightclubListenerTermination.ApplicationQuit);
    }
}
