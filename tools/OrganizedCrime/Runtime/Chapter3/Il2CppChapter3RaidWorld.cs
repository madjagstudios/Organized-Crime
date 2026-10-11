using System.Globalization;
using Il2CppScheduleOne.Building.Doors;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Runtime.Platform;
using S1API.Products;
using S1API.Storages;
using UnityEngine;

namespace OrganizedCrime.Runtime.Chapter3;

internal sealed class Il2CppChapter3RaidWorld : IChapter3RaidWorld, IChapter3RaidSeizureWorld, IChapter3RaidPollingWorld, IChapter3RaidLoadWorld, IChapter3RaidStorageReader
{
    private readonly Chapter3RaidScanCache _stock = new();
    private readonly Chapter3RaidWrapperCache<StorageInstance> _wrappers = new();
    private readonly Dictionary<string, HashSet<Guid>> _storageGuids = new(StringComparer.Ordinal);
    private readonly Dictionary<Chapter3RaidSlotIdentity, Il2CppScheduleOne.ItemFramework.ItemSlot> _locks = new();
    internal Func<bool>? RestorationReady { get; set; }
    private readonly ISyndicateHqStorageRuntime _hqStorage;
    private readonly Func<bool> _host;
    private readonly Func<Release1WorldPoint?> _hqDoor;
    private readonly SyndicateHqRuntimeService _hq;
    private readonly Dictionary<string, (Property Property, Vector3 Local, Vector3 Fallback, Chapter3RaidEntranceSource Source)> _entrances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Property Property, long NextAttempt)> _entranceRetries = new(StringComparer.Ordinal);
    private const long EntranceRetryMilliseconds = 30_000;
    private readonly OnceLog _entranceWarnings;

    internal Il2CppChapter3RaidWorld(ISyndicateHqStorageRuntime storage, Func<bool> host,
        Func<Release1WorldPoint?> hqDoor, SyndicateHqRuntimeService hq, Action<string>? warning = null)
    {
        _hqStorage = storage; _host = host; _hqDoor = hqDoor; _hq = hq;
        _entranceWarnings = new(warning ?? OrganizedCrimeLog.Warning);
    }
    public void OnPreLoad() => _entranceWarnings.ClearAll();
    internal void ReportEntranceUnavailable(string code) => _entranceWarnings.Report(code,
        $"Chapter 3 raid entrance unavailable for {code}; occupancy blocked, retrying every 30 seconds.");
    public bool IsHost { get { try { return _host(); } catch { return false; } } }
    public void Reset()
    {
        foreach (var pair in _locks.ToArray()) Unlock("", pair.Key);
        _entrances.Clear(); _entranceRetries.Clear();
        _stock.Reset(); _wrappers.Reset(); _storageGuids.Clear();
    }

    public bool IsRestored(string siteCode)
    {
        try
        {
            return IsHost && RestorationReady?.Invoke() == true &&
                _hqStorage.ReadHoldRoom().Readiness == Release1HoldRoomReadiness.Ready;
        }
        catch { return false; }
    }

    private static bool IsHq(string code) => code == "oc_syndicatehq";
    private static bool Personal(Chapter3RaidSlotIdentity id) => Chapter3StorageRun.PlayerClosetIds.Contains(id.StorageGuid.ToString("D"));

    private bool ResolvePropertySlot(string code, Chapter3RaidSlotIdentity id,
        out BuildableItem owner, out Il2CppScheduleOne.Storage.StorageEntity storage, out Il2CppScheduleOne.ItemFramework.ItemSlot native)
    {
        owner = null!; storage = null!; native = null!;
        if (Property.OwnedProperties is null || Business.OwnedBusinesses is null) return false;
        var candidates = new List<Property>();
        foreach (var p in Property.OwnedProperties) if (UnityLife.IsAlive(p) && p.PropertyCode == code) candidates.Add(p);
        foreach (var p in Business.OwnedBusinesses) if (UnityLife.IsAlive(p) && p.PropertyCode == code && !candidates.Any(c => c == p)) candidates.Add(p);
        if (candidates.Count != 1 || !candidates[0].IsOwned) return false;
        var property = candidates[0];
        if (property.BuildableItems is null) return false;
        var matches = new List<(BuildableItem Owner, Il2CppScheduleOne.Storage.StorageEntity Storage)>();
        foreach (var item in property.BuildableItems)
        {
            if (!UnityLife.IsAlive(item) || !Guid.TryParse(item.GUID.ToString(), out var guid) || guid != id.StorageGuid || UnityLife.IsAlive(item.TryCast<BedItem>())) continue;
            var placeable = item.TryCast<PlaceableStorageEntity>(); var surface = item.TryCast<SurfaceStorageEntity>();
            var entity = UnityLife.IsAlive(placeable) ? placeable!.StorageEntity : UnityLife.IsAlive(surface) ? surface!.StorageEntity : null;
            if (!UnityLife.IsAlive(entity)) return false;
            matches.Add((item, entity!));
        }
        if (matches.Count != 1 || id.SlotIndex < 0) return false;
        var raw = matches[0].Storage;
        if (!UnityLife.IsAlive(raw.gameObject)) return false;
        if (raw.ItemSlots is null || id.SlotIndex >= raw.ItemSlots.Count) return false;
        owner = matches[0].Owner;
        storage = raw;
        native = raw.ItemSlots[id.SlotIndex];
        return native is not null;
    }

    public bool TryReadSlot(string code, Chapter3RaidSlotIdentity id, out Chapter3RaidSlotRead read)
    {
        read = null!;
        if (!IsRestored(code)) return false;
        if (IsHq(code))
        {
            if (!Personal(id)) return false;
            return _hqStorage is IChapter3RaidHqSlotReader reader &&
                reader.TryReadRaidSlot(id.StorageGuid.ToString("D"), id.SlotIndex, out read);
        }
        if (!ResolvePropertySlot(code, id, out _, out _, out var slot)) return false;
        return TryReadNativeSlot(slot, out read);
    }

    public bool TryReadStorageQuantity(string code, Guid guid, string fingerprint, out long quantity)
    {
        quantity = 0;
        try
        {
            if (!IsRestored(code)) return false;
            if (IsHq(code))
            {
                if (!Personal(new(guid, 0)) || _hqStorage is not IChapter3RaidHqSlotReader reader) return false;
                var room = _hqStorage.ReadHoldRoom();
                if (room.Readiness != Release1HoldRoomReadiness.Ready) return false;
                room.Validate();
                var closet = room.Closets.SingleOrDefault(c => c.ClosetGuid == guid.ToString("D"));
                if (closet is null) return false;
                return Chapter3RaidStorageQuantity.TrySum(closet.Slots.Count,
                    i => reader.TryReadRaidSlot(closet.ClosetGuid, i, out var read) ? read : null,
                    fingerprint, out quantity);
            }
            // Resolve the entity through slot zero, not the historical slot
            // index. Count every native slot, including job-excluded stacks.
            if (!ResolvePropertySlot(code, new(guid, 0), out _, out var storage, out _)) return false;
            var slots = storage.ItemSlots;
            if (slots is null) return false;
            return Chapter3RaidStorageQuantity.TrySum(slots.Count,
                i => slots[i] is { } slot && TryReadNativeSlot(slot, out var read) ? read : null,
                fingerprint, out quantity);
        }
        catch { return false; }
    }

    private static bool TryReadNativeSlot(Il2CppScheduleOne.ItemFramework.ItemSlot slot, out Chapter3RaidSlotRead read)
    {
        read = null!;
        var quantity = slot.Quantity;
        if (quantity == 0) { read = new(0, null, null); return true; }
        var item = slot.ItemInstance;
        if (quantity < 0 || item is null) return false;
        var product = item.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
        if (product is null) { read = new(quantity, "non-product", "non-product"); return true; }
        if (!UnityLife.IsAlive(product.Definition)) return false;
        var id = product.Definition.ID;
        var applied = product.AppliedPackaging;
        var packaging = UnityLife.IsAlive(applied) && !string.IsNullOrWhiteSpace(applied.ID) ? applied.ID : product.PackagingID;
        if (string.IsNullOrWhiteSpace(packaging)) packaging = "none";
        var units = 1;
        if (packaging != "none")
        {
            var raw = Il2CppScheduleOne.Registry.GetItem(packaging);
            var package = UnityLife.IsAlive(raw) ? raw.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>() : null;
            if (!UnityLife.IsAlive(package) || package!.Quantity < 1) return false;
            units = package.Quantity;
        }
        if (string.IsNullOrWhiteSpace(id)) return false;
        var fingerprint = $"{id}|{packaging}|{units}|quality={product.Quality}";
        read = new(quantity, fingerprint, fingerprint);
        return true;
    }

    public bool TryLock(string code, Chapter3RaidSlotIdentity id)
    {
        if (!IsRestored(code)) return false;
        if (IsHq(code))
        {
            if (!Personal(id)) return false;
            try
            {
                var result = _hqStorage.TrySetClosetSlotLocked(id.StorageGuid.ToString("D"), id.SlotIndex, true);
                if (result == Release1SmallCourtesyWorldMutationStatus.Succeeded) return true;
            }
            catch { }
            _hqStorage.TrySetClosetSlotLocked(id.StorageGuid.ToString("D"), id.SlotIndex, false);
            return false;
        }
        if (_locks.ContainsKey(id) || !ResolvePropertySlot(code, id, out _, out _, out var slot) || slot.IsRemovalLocked) return false;
        _locks.Add(id, slot);
        try { slot.SetIsRemovalLocked(true); if (slot.IsRemovalLocked) return true; }
        catch { }
        Unlock(code, id); return false;
    }

    public void Unlock(string code, Chapter3RaidSlotIdentity id)
    {
        if (IsHq(code)) { if (Personal(id)) _hqStorage.TrySetClosetSlotLocked(id.StorageGuid.ToString("D"), id.SlotIndex, false); return; }
        if (!_locks.TryGetValue(id, out var slot)) return;
        try { slot.SetIsRemovalLocked(false); if (!slot.IsRemovalLocked) _locks.Remove(id); } catch { }
    }

    public void Take(string code, Chapter3RaidSlotIdentity id, int packages)
    {
        if (packages < 1 || !IsRestored(code)) return;
        if (IsHq(code))
        {
            if (Personal(id)) _hqStorage.TryChangeClosetSlotQuantity(id.StorageGuid.ToString("D"), id.SlotIndex, -packages);
            return;
        }
        if (!_locks.TryGetValue(id, out var locked) || !locked.IsRemovalLocked ||
            !ResolvePropertySlot(code, id, out var owner, out var storage, out var slot) || slot.Pointer != locked.Pointer || slot.Quantity < packages) return;
        try
        {
            slot.SetIsRemovalLocked(false);
            if (slot.IsRemovalLocked) return;
            Chapter3RaidNativeQuantity.Take(packages, () => slot.Quantity, slot.ChangeQuantity,
                quantity => storage.SetItemSlotQuantity(id.SlotIndex, quantity), () => owner.HasChanged = true);
        }
        finally { slot.SetIsRemovalLocked(true); }
    }

    public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions)
        => ReadCore(exclusions, ReadMinutes(), force: true);

    public IReadOnlyList<Chapter3RaidSite> ReadForPoll(Chapter3RaidExclusions exclusions, double? minutes)
        => ReadCore(exclusions, minutes, force: false);

    private static double? ReadMinutes()
    {
        try
        {
            var days = S1API.GameTime.TimeManager.ElapsedDays;
            var minutes = S1API.GameTime.TimeManager.GetMinutesFrom24HourTime(S1API.GameTime.TimeManager.CurrentTime);
            return days >= 0 && minutes is >= 0 and < 1440 ? days * 1440d + minutes : null;
        }
        catch { return null; }
    }

    private IReadOnlyList<Chapter3RaidSite> ReadCore(Chapter3RaidExclusions exclusions, double? minutes, bool force)
    {
        if (!IsHost) return Array.Empty<Chapter3RaidSite>();
        var sites = new List<Chapter3RaidSite>();
        var properties = Property.OwnedProperties;
        if (properties is null) throw new InvalidOperationException("Owned property registry unavailable.");
        var owned = new List<Property>();
        foreach (var raw in properties) owned.Add(raw);
        var businesses = Business.OwnedBusinesses;
        if (businesses is null) throw new InvalidOperationException("Owned business registry unavailable.");
        foreach (var raw in businesses) owned.Add(raw);
        var seen = new Dictionary<string, Property>(StringComparer.Ordinal);
        foreach (var raw in owned)
        {
            var property = UnityLife.IsAlive(raw) ? raw.TryCast<Property>() : null;
            if (!UnityLife.IsAlive(property)) throw new InvalidOperationException("Owned property unreadable.");
            // Skip unowned entries before inspecting their entrance or stock.
            if (!property!.IsOwned) continue;
            var code = property.PropertyCode;
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Property identity unavailable.");
            if (seen.TryGetValue(code, out var existing))
            {
                if (!UnityLife.IsAlive(existing) || existing != property) throw new InvalidOperationException("Duplicate property identity.");
                continue;
            }
            seen.Add(code, property);
            var presence = PropertyPresence(property);
            sites.Add(_stock.Read(code, property.Pointer.ToInt64(), minutes, force,
                // Live preview/raid reads retain the existing early job-storage
                // exclusion (including unreadable reserved stock). Idle polling
                // captures unfiltered stock for later exclusion refreshes.
                () => new(code, property.PropertyName, true, ReadProperty(property, force ? exclusions : new()), presence), presence, exclusions));
        }
        sites.Add(_stock.Read("oc_syndicatehq", 0, minutes, force, () => ReadHq(force ? exclusions : new()), HqPresence(), exclusions));
        var codes = sites.Select(site => site.Code).ToHashSet(StringComparer.Ordinal);
        _stock.Retain(codes);
        foreach (var code in _storageGuids.Keys.Where(code => !codes.Contains(code)).ToArray()) _storageGuids.Remove(code);
        _wrappers.Retain(_storageGuids.Values.SelectMany(guids => guids).ToHashSet());
        return sites.ToArray();
    }

    private IReadOnlyList<Chapter3RaidSlot> ReadProperty(Property property, Chapter3RaidExclusions exclusions) =>
        Chapter3RaidScanCache.ScanGuids(_storageGuids, property.PropertyCode, seen => ReadProperty(property, exclusions, seen));

    private IReadOnlyList<Chapter3RaidSlot> ReadProperty(Property property, Chapter3RaidExclusions exclusions, HashSet<Guid> seen)
    {
        var items = property.BuildableItems ?? throw new InvalidOperationException("Buildables unavailable.");
        var slots = new List<Chapter3RaidSlot>();
        foreach (var item in items)
        {
            if (!UnityLife.IsAlive(item)) throw new InvalidOperationException("Buildable unreadable.");
            // Il2Cpp list projections require TryCast; a machinery child storage
            // component does not turn its owning BuildableItem into storage.
            if (UnityLife.IsAlive(item.TryCast<BedItem>())) continue;
            var placeable = item.TryCast<PlaceableStorageEntity>();
            var surface = item.TryCast<SurfaceStorageEntity>();
            var placeableAlive = UnityLife.IsAlive(placeable);
            if (!placeableAlive && !UnityLife.IsAlive(surface)) continue;
            if (!Guid.TryParse(item.GUID.ToString(), out var guid) || guid == Guid.Empty || !seen.Add(guid))
                throw new InvalidOperationException("Storage GUID is absent or duplicated.");
            if (exclusions.Storage.Contains(guid)) continue;
            var nativeStorage = placeableAlive ? placeable!.StorageEntity : surface!.StorageEntity;
            if (!UnityLife.IsAlive(nativeStorage)) throw new InvalidOperationException("Storage unavailable.");
            var storageObject = nativeStorage!.gameObject;
            if (!UnityLife.IsAlive(storageObject)) throw new InvalidOperationException("Storage object unavailable.");
            var storage = _wrappers.Get(guid, nativeStorage.Pointer.ToInt64(),
                () => StorageInstance.FromGameObject(storageObject)!);
            var read = storage.Slots;
            for (var index = 0; index < read.Length; index++)
            {
                var slot = read[index];
                if (slot is null) throw new InvalidOperationException("Slot unavailable.");
                if (slot.Quantity == 0) continue;
                var instance = slot.ItemInstance ?? throw new InvalidOperationException("Occupied slot unreadable.");
                if (instance is not ProductInstance product) continue;
                var package = product.IsPackaged ? product.AppliedPackaging : null;
                var packaging = package?.ID ?? "none";
                var units = package?.Quantity ?? 1;
                if (package is null)
                {
                    // Match the HQ reader's recovery fallback: a stored brick
                    // with an unresolved AppliedPackaging must never become one
                    // base unit merely because the wrapper reports unpackaged.
                    var nativeProduct = nativeStorage.ItemSlots[index].ItemInstance
                        ?.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
                    // ProductItemInstance is an Il2Cpp item wrapper, not a Unity object.
                    if (nativeProduct is null) throw new InvalidOperationException("Product projection unreadable.");
                    var nativePackaging = nativeProduct.PackagingID;
                    if (!string.IsNullOrWhiteSpace(nativePackaging) && nativePackaging != "none")
                    {
                        var rawPackage = Il2CppScheduleOne.Registry.GetItem(nativePackaging);
                        var recovered = UnityLife.IsAlive(rawPackage) ? rawPackage.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>() : null;
                        if (!UnityLife.IsAlive(recovered) || recovered!.Quantity < 1)
                            throw new InvalidOperationException("Product packaging unavailable.");
                        packaging = nativePackaging;
                        units = recovered.Quantity;
                    }
                }
                var id = product.Definition.ID;
                var fingerprint = $"{id}|{packaging}|{units}|quality={product.Quality}";
                var candidate = new Chapter3RaidSlot(new(guid, index), fingerprint, id, packaging, slot.Quantity, units);
                if (units < 1 || string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Product packaging unreadable.");
                var eligible = Chapter3RaidEligibility.Capture(candidate, exclusions);
                if (eligible is not null) slots.Add(eligible);
            }
        }
        return slots.ToArray();
    }

    private Chapter3RaidSite ReadHq(Chapter3RaidExclusions exclusions)
    {
        var slots = new List<Chapter3RaidSlot>();
        try
        {
            var room = _hqStorage.ReadHoldRoom();
            if (room.Readiness != Release1HoldRoomReadiness.Ready) throw new InvalidOperationException("HQ storage unavailable.");
            room.Validate();
            foreach (var closet in room.Closets)
            {
                var guid = Guid.Parse(closet.ClosetGuid);
                if (!Chapter3StorageRun.PlayerClosetIds.Contains(guid.ToString("D"))) continue;
                foreach (var slot in closet.Slots)
                {
                    if (slot.Quantity == 0 || exclusions.Slots.Contains(new(guid, slot.SlotIndex))) continue;
                    var raw = Il2CppScheduleOne.Registry.GetItem(slot.ProductId);
                    if (!UnityLife.IsAlive(raw)) throw new InvalidOperationException("HQ item definition unreadable.");
                    // The room snapshot also contains cash/equipment. A registry
                    // definition cast verifies it is a finished drug product.
                    if (!UnityLife.IsAlive(raw.TryCast<Il2CppScheduleOne.Product.ProductDefinition>())) continue;
                    var packaging = slot.IsPackaged ? slot.PackagingId : "none";
                    var units = 1;
                    if (slot.IsPackaged)
                    {
                        var rawPackage = Il2CppScheduleOne.Registry.GetItem(packaging);
                        var package = UnityLife.IsAlive(rawPackage) ? rawPackage.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>() : null;
                        if (!UnityLife.IsAlive(package) || package!.Quantity < 1) throw new InvalidOperationException("HQ packaging unreadable.");
                        units = package.Quantity;
                    }
                    var fingerprint = $"{slot.ProductId}|{packaging}|{units}|value={slot.MonetaryValue.ToString("R", CultureInfo.InvariantCulture)}";
                    var candidate = new Chapter3RaidSlot(new(guid, slot.SlotIndex), fingerprint, slot.ProductId!, packaging!, slot.Quantity, units);
                    var eligible = Chapter3RaidEligibility.Capture(candidate, exclusions);
                    if (eligible is not null) slots.Add(eligible);
                }
            }
            return new("oc_syndicatehq", "Syndicate HQ", true, slots.ToArray(), HqPresence());
        }
        catch { return new("oc_syndicatehq", "Syndicate HQ", false, Array.Empty<Chapter3RaidSlot>(), HqPresence()); }
    }

    // D7a supports only a corroborated one-player census. PlayerList alone
    // cannot establish that a joining/teleported guest is absent. Multiplayer
    // always blocks until a per-player HQ transition census is supported.
    private static Player? SinglePlayer()
    {
        var players = Player.PlayerList;
        var server = Il2CppFishNet.InstanceFinder.ServerManager;
        var lobby = Lobby.Instance;
        if (players is null || !UnityLife.IsAlive(server) || server.Clients is null || !UnityLife.IsAlive(lobby) ||
            !Chapter3RaidOccupancy.SupportsCensus(players.Count, server.Clients.Count, lobby.PlayerCount)) return null;
        var rawPlayer = players[0];
        var player = UnityLife.IsAlive(rawPlayer) ? rawPlayer.TryCast<Player>() : null;
        return UnityLife.IsAlive(player) && player!.IsServerInitialized && player.IsLocalPlayer && player.Connection is not null ? player : null;
    }

    private Chapter3RaidPresence PropertyPresence(Property property)
    {
        try
        {
            var player = SinglePlayer();
            if (!UnityLife.IsAlive(player) || !UnityLife.IsAlive(player!.transform)) return Chapter3RaidPresence.Unknown;
            var position = player.transform.position;
            var current = player.CurrentProperty;
            // A genuinely unset CurrentProperty is normal outside. A retained
            // destroyed Unity wrapper is unreadable and must block occupancy.
            var currentAlive = UnityLife.IsAlive(current);
            if (!Finite(position) || (!currentAlive && !ReferenceEquals(current, null))) return Chapter3RaidPresence.Unknown;
            if (currentAlive && current == property) return Chapter3RaidPresence.Inside;
            var bounds = ReadBounds(property);
            var hasEntrance = TryEntrance(property, bounds, out var entrance, out var source);
            var inside = false;
            try { inside = property.DoBoundsContainPoint(position); } catch { }
            return Chapter3RaidEntranceGeometry.PropertyPresence(Point(position), false,
                inside, hasEntrance ? Point(entrance) : null, bounds, hasEntrance ? source : null);
        }
        catch { return Chapter3RaidPresence.Unknown; }
    }

    private Chapter3RaidPresence HqPresence()
    {
        try
        {
            var player = SinglePlayer();
            var door = _hqDoor();
            if (!UnityLife.IsAlive(player) || !UnityLife.IsAlive(player!.transform) || door is null || _hq.IsDisposed || _hq.IsTeardownBlocked) return Chapter3RaidPresence.Unknown;
            var position = player.transform.position;
            if (!Finite(position)) return Chapter3RaidPresence.Unknown;
            var transition = _hq.State is not (SyndicateHqEntryState.Inside or SyndicateHqEntryState.EnterPrompt or SyndicateHqEntryState.LockedPrompt);
            return Chapter3RaidOccupancy.Classify(true, new[] { new Chapter3RaidPlayer(true, false, false,
                Vector3.Distance(position, new((float)door.X, (float)door.Y, (float)door.Z))) }, _hq.IsInside, transition);
        }
        catch { return Chapter3RaidPresence.Unknown; }
    }

    private bool TryEntrance(Property property, IReadOnlyList<Chapter3RaidBounds> bounds,
        out Vector3 entrance, out Chapter3RaidEntranceSource source)
    {
        entrance = default;
        source = Chapter3RaidEntranceSource.Bounds;
        if (!UnityLife.IsAlive(property) || !property.IsOwned) return false;
        var code = property.PropertyCode;
        if (_entrances.TryGetValue(code, out var bound) && UnityLife.IsAlive(bound.Property) && bound.Property == property)
        {
            // Keep a bound offset rather than retaining a live doorway transform.
            Chapter3RaidPoint? live = null;
            try { if (UnityLife.IsAlive(property.transform)) live = Point(property.transform.TransformPoint(bound.Local)); } catch { }
            var resolved = Chapter3RaidEntranceGeometry.Rebind(live, Point(bound.Fallback), bounds);
            if (resolved is { } point) { entrance = Vector(point); source = bound.Source; return true; }
            _entrances.Remove(code);
        }
        var now = Environment.TickCount64;
        var previouslyAttempted = _entranceRetries.TryGetValue(code, out var retry) &&
            UnityLife.IsAlive(retry.Property) && retry.Property == property;
        if (previouslyAttempted && now < retry.NextAttempt) return false;
        _entrances.Remove(code);
        _entranceRetries[code] = (property, now + EntranceRetryMilliseconds);
        try
        {
            var boundNow = TryBindEntrance(property, bounds, out entrance);
            if (boundNow) source = _entrances[code].Source;
            return boundNow;
        }
        catch { return false; }
        finally
        {
            if (!_entrances.ContainsKey(code))
                ReportEntranceUnavailable(code);
        }
    }

    private bool TryBindEntrance(Property property, IReadOnlyList<Chapter3RaidBounds> bounds, out Vector3 entrance)
    {
        entrance = default;
        var code = property.PropertyCode;
        Chapter3RaidPoint? spawn = null;
        try { if (UnityLife.IsAlive(property.SpawnPoint)) spawn = Point(property.SpawnPoint.position); } catch { }
        var doors = new List<Chapter3RaidPoint>();
        if (code == RuntimePropertyDefinition.FishWarehouse.PropertyCode && UnityLife.IsAlive(property.transform))
        {
            // Preserve the authored warehouse exterior threshold, validated too.
            doors.Add(Point(property.transform.TransformPoint(new(-10.96f, FishWarehouseInteriorRoomDefinition.FloorY,
                FishWarehouseInteriorRoomDefinition.HalfDepth))));
        }
        else
        {
            try
            {
                foreach (var door in Resources.FindObjectsOfTypeAll<PropertyDoorController>())
                {
                    try
                    {
                        if (!UnityLife.IsAlive(door) || !UnityLife.IsAlive(door.Property) || door.Property != property ||
                            !UnityLife.IsAlive(door.PlayerBlocker)) continue;
                        var box = door.PlayerBlocker.bounds;
                        // Empty/culled blockers and unstable door transforms are
                        // not anchors. A failed candidate never discards spawn.
                        if (!Box(box).Valid) continue;
                        doors.Add(Point(box.center));
                    }
                    catch { }
                }
            }
            catch { }
        }
        var selected = Chapter3RaidEntranceGeometry.Bind(doors, spawn, bounds);
        if (selected is null) return false;
        entrance = Vector(selected.Point);
        var local = new Vector3(float.NaN, float.NaN, float.NaN);
        try { if (UnityLife.IsAlive(property.transform)) local = property.transform.InverseTransformPoint(entrance); } catch { }
        _entrances[code] = (property, local, entrance, selected.Source);
        return true;
    }

    private static Chapter3RaidPoint Point(Vector3 p) => new(p.x, p.y, p.z);
    private static Vector3 Vector(Chapter3RaidPoint p) => new((float)p.X, (float)p.Y, (float)p.Z);
    private static Chapter3RaidBounds Box(Bounds b) => new(Point(b.min), Point(b.max));
    private static IReadOnlyList<Chapter3RaidBounds> ReadBounds(Property property)
    {
        var result = new List<Chapter3RaidBounds>();
        var colliders = new List<BoxCollider>();
        var native = property.propertyBoundsColliders;
        if (native is not null) foreach (var collider in native) colliders.Add(collider);
        if (colliders.Count == 0 && UnityLife.IsAlive(property.BoundingBox))
            foreach (var collider in property.BoundingBox.GetComponentsInChildren<BoxCollider>(true)) colliders.Add(collider);
        foreach (var collider in colliders)
        {
            if (!UnityLife.IsAlive(collider)) return Array.Empty<Chapter3RaidBounds>();
            var box = Box(collider.bounds);
            if (!box.Valid && UnityLife.IsAlive(collider.transform))
            {
                // Disabled property bounding volumes have empty physics bounds.
                // Reconstruct only their authored boxes, never doorway roots.
                var center = collider.center; var half = collider.size * 0.5f;
                var corners = new List<Chapter3RaidPoint>();
                for (var x = -1; x <= 1; x += 2)
                    for (var y = -1; y <= 1; y += 2)
                        for (var z = -1; z <= 1; z += 2)
                            corners.Add(Point(collider.transform.TransformPoint(center + new Vector3(x * half.x, y * half.y, z * half.z))));
                box = Chapter3RaidEntranceGeometry.FromCorners(corners) ?? default;
            }
            if (!box.Valid) return Array.Empty<Chapter3RaidBounds>();
            result.Add(box);
        }
        return result;
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
}
