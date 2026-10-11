// OC-151: the owner QA hotkey map, moved verbatim from Mod.OnUpdate. Poll runs last in
// the update fan-out: it returns while a text field has focus, then while the owner QA
// keys preference is off, then polls every bound key in code order. Each handler reads
// the composition root at fire time, so post-teardown presses are skipped exactly as
// the Mod.cs null-conditional handlers were. KeyCode reads go through the engine seam
// so tests can press keys without Unity input.
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tiles;
using Il2CppScheduleOne.Vehicles;
using OrganizedCrime.Runtime.Composition;
using UnityEngine;

namespace OrganizedCrime.Runtime;

internal sealed record QaKeyBinding(KeyCode Key, string Name, Action Handler);

internal sealed class OwnerQaKeys
{
    private readonly OcCompositionRoot _root;
    private readonly IOcEngineAdapters _engine;
    private readonly Action<string> _log;

    public OwnerQaKeys(OcCompositionRoot root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _engine = root.Engine;
        _log = root.Log;
        Bindings = new[]
        {
            new QaKeyBinding(KeyCode.Backslash, "warehouse-lifecycle", () => _root.WarehouseRuntime?.ToggleDeveloperLifecycle()),
            new QaKeyBinding(KeyCode.O, "warehouse-unlock", () => _root.WarehouseRuntime?.TryUnlockDeveloperLifecycle()),
            new QaKeyBinding(KeyCode.P, "warehouse-place-home", () => _root.WarehouseRuntime?.TryPlaceEmployeeHome()),
            new QaKeyBinding(KeyCode.Semicolon, "warehouse-lamp-add", () => _root.WarehouseRuntime?.TryLightingAddLamp()),
            new QaKeyBinding(KeyCode.Quote, "warehouse-lamp-remove", () => _root.WarehouseRuntime?.TryLightingRemoveLamp()),
            new QaKeyBinding(KeyCode.PageUp, "warehouse-lamp-raise", () => _root.WarehouseRuntime?.TryLightingRaiseLamp()),
            new QaKeyBinding(KeyCode.PageDown, "warehouse-lamp-lower", () => _root.WarehouseRuntime?.TryLightingLowerLamp()),
            new QaKeyBinding(KeyCode.LeftBracket, "warehouse-lamp-dim", () => _root.WarehouseRuntime?.TryLightingDimLamps()),
            new QaKeyBinding(KeyCode.RightBracket, "warehouse-lamp-brighten", () => _root.WarehouseRuntime?.TryLightingBrightenLamps()),
            new QaKeyBinding(KeyCode.Comma, "warehouse-lamp-yaw", () => _root.WarehouseRuntime?.TryLightingToggleLampYaw()),
            new QaKeyBinding(KeyCode.Equals, "warehouse-lamp-layout", () => _root.WarehouseRuntime?.TryLightingLogLayout()),
            new QaKeyBinding(KeyCode.F8, "recognition", HandleRecognition),
            new QaKeyBinding(KeyCode.Minus, "chapter3-start", HandleChapter3Start),
            new QaKeyBinding(KeyCode.F9, "staging-stage", HandleStagingStage),
            new QaKeyBinding(KeyCode.F10, "staging-dump", HandleStagingDump),
            new QaKeyBinding(KeyCode.F11, "hold-room-dump", HandleHoldRoomDump),
            new QaKeyBinding(KeyCode.F6, "quantity-decrement", HandleQuantityDecrement),
            new QaKeyBinding(KeyCode.F1, "cash-gate", HandleCashGate),
            new QaKeyBinding(KeyCode.F2, "closet-cash-gate", HandleClosetCashGate),
            new QaKeyBinding(KeyCode.Home, "production-gate", HandleProductionGate),
            new QaKeyBinding(KeyCode.End, "lockdown-toggle", HandleLockdownToggle),
            new QaKeyBinding(KeyCode.Insert, "npc-registry", HandleNpcRegistry),
        };
    }

    public IReadOnlyList<QaKeyBinding> Bindings { get; }

    public void Poll()
    {
        if (_engine.IsPlayerTyping())
            return;
        if (!_root.OwnerQaKeysEnabled)
            return;
        foreach (var binding in Bindings)
        {
            if (_engine.IsKeyDown(binding.Key))
                binding.Handler();
        }
    }

    private void HandleRecognition()
    {
        var hqContext = _root.HqContext;
        var story = _root.Story;
        if (hqContext is not null && story is not null)
        {
            var result = Release1StoryOwnerQaRecognitionHarness.TryRecognize(hqContext, story);
            _log($"[Organized Crime] OC-48 owner QA recognition: {result.Status}: {result.Message}");
        }
    }

    // A single unbound key: a bare C can collide with the game's Crouch binding, and a
    // Ctrl+Shift+C chord never registered in an owner run. The press is logged so a missed
    // key is distinguishable from a refused start.
    private void HandleChapter3Start()
    {
        _log("[Organized Crime] Chapter 3 QA start key pressed.");
        var chapter3 = _root.Chapter3;
        if (chapter3 is null)
            _log("[Organized Crime] Chapter 3 QA start refused: the Chapter 3 service is not constructed.");
        else
            chapter3.TryQaStartNow();
    }

    private void HandleStagingStage()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = Release1WrongAddressStagingHarness.TryStage(world);
            OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: stage status {result.Status}", _log);
            foreach (var line in result.Lines)
                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: {line}", _log);
        }
    }

    private void HandleStagingDump()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = Release1WrongAddressStagingHarness.TryDump(world);
            OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: dump status {result.Status}", _log);
            foreach (var line in result.Lines)
                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: {line}", _log);

            try
            {
                var storageEntities = WorldStorageEntity.All;
                var storageCount = storageEntities == null ? 0 : storageEntities.Count;
                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: world storage entities: {storageCount}", _log);
                if (storageEntities != null)
                {
                    foreach (var entity in storageEntities)
                    {
                        if (entity == null) continue;
                        var slots = entity.ItemSlots;
                        var slotCount = slots == null ? 0 : slots.Count;
                        var occupied = 0;
                        if (slots != null)
                        {
                            foreach (var slot in slots)
                            {
                                if (slot != null && slot.Quantity > 0) occupied++;
                            }
                        }
                        OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: entity {entity.GUID} name {entity.gameObject.name} changed {entity.HasChanged} slots {slotCount} occupied {occupied}", _log);
                        if (slots != null)
                        {
                            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                            {
                                var slot = slots[slotIndex];
                                if (slot == null || slot.Quantity <= 0) continue;
                                var item = slot.ItemInstance;
                                var itemId = item == null ? "null" : item.ID;
                                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: entity {entity.GUID} slot {slotIndex} id {itemId} quantity {slot.Quantity}", _log);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[Organized Crime] OC-52 staging proof: world storage dump failed: {ex.Message}");
            }

            try
            {
                var deadDrops = DeadDrop.DeadDrops;
                var dropCount = deadDrops == null ? 0 : deadDrops.Count;
                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: dead drops: {dropCount}", _log);
                if (deadDrops != null)
                {
                    foreach (var drop in deadDrops)
                    {
                        if (drop == null) continue;
                        var storage = drop.Storage;
                        if (storage == null)
                        {
                            OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: drop {drop.DeadDropName} guid {drop.GUID} storage null", _log);
                            continue;
                        }
                        OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof: drop {drop.DeadDropName} guid {drop.GUID} storage {storage.GUID} storageName {storage.gameObject.name}", _log);
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[Organized Crime] OC-52 staging proof: dead drop dump failed: {ex.Message}");
            }
        }
    }

    private void HandleHoldRoomDump()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = Release1RoomWithNoNameHoldRoomHarness.TryDump(world);
            _log($"[Organized Crime] OC-57 hold room proof: dump status {result.Status}");
            foreach (var line in result.Lines)
                _log($"[Organized Crime] OC-57 hold room proof: {line}");
        }
    }

    private void HandleQuantityDecrement()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);
            _log($"[Organized Crime] OC-58 quantity proof: status {result.Status}");
            foreach (var line in result.Lines)
                _log($"[Organized Crime] OC-58 quantity proof: {line}");
        }
    }

    private void HandleCashGate()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = _root.Lifetime.TheEnvelopeCashGateDumpNext
                ? Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(world)
                : Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);
            var mode = _root.Lifetime.TheEnvelopeCashGateDumpNext ? "dump" : "decrement";
            _log($"[Organized Crime] OC-60 cash gate proof ({mode}): status {result.Status}");
            foreach (var line in result.Lines)
                _log($"[Organized Crime] OC-60 cash gate proof: {line}");
            _root.Lifetime.TheEnvelopeCashGateDumpNext = !_root.Lifetime.TheEnvelopeCashGateDumpNext;
        }
    }

    private void HandleClosetCashGate()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = _root.Lifetime.TheEnvelopeClosetCashGateDumpNext
                ? Release1TheEnvelopeClosetCashGateHarness.TryDumpClosetCashSlots(world)
                : Release1TheEnvelopeClosetCashGateHarness.TryDecrementFirstClosetCashSlot(world);
            var mode = _root.Lifetime.TheEnvelopeClosetCashGateDumpNext ? "dump" : "decrement";
            _log($"[Organized Crime] OC-61 closet cash gate proof ({mode}): status {result.Status}");
            foreach (var line in result.Lines)
                _log($"[Organized Crime] OC-61 closet cash gate proof: {line}");
            _root.Lifetime.TheEnvelopeClosetCashGateDumpNext = !_root.Lifetime.TheEnvelopeClosetCashGateDumpNext;
        }
    }

    private void HandleProductionGate()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            var result = _root.Lifetime.KeepTheLightsOffProductionGateDumpNext
                ? Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(world)
                : Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(world);
            var mode = _root.Lifetime.KeepTheLightsOffProductionGateDumpNext ? "dump" : "cross check";
            _log($"[Organized Crime] OC-65 production gate proof ({mode}): status {result.Status}");
            foreach (var line in result.Lines)
                _log($"[Organized Crime] OC-65 production gate proof: {line}");
            _root.Lifetime.KeepTheLightsOffProductionGateDumpNext = !_root.Lifetime.KeepTheLightsOffProductionGateDumpNext;
        }
    }

    private void HandleLockdownToggle()
    {
        var world = _root.CourtesyWorld;
        if (world is not null)
        {
            string reason;
            var status = _engine.IsLockdownActive()
                ? world.TryReleaseLockdown(out reason)
                : world.TryEngageLockdown(out reason);
            _log($"[Organized Crime] OC-73 lockdown gate: status {status}");
            if (!string.IsNullOrEmpty(reason))
                _log($"[Organized Crime] OC-73 lockdown gate: {reason}");
        }
    }

    private void HandleNpcRegistry()
    {
        var result = Release1NpcRegistryDumpHarness.TryDump();
        _log($"[Organized Crime] OC-56 npc registry: status {result.Status}");
        foreach (var line in result.Lines)
            _log($"[Organized Crime] OC-56 npc registry: {line}");
    }
}
