using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using OrganizedCrime.Model;
using S1API.DeadDrops;
using S1API.Items;
using S1API.Products;
using S1API.Storages;
using UnityEngine;
using Money = S1API.Money.Money;
using ApiProductDefinition = S1API.Products.ProductDefinition;
using ProductManager = S1API.Products.ProductManager;
using TimeManager = S1API.GameTime.TimeManager;
using NativeBusiness = Il2CppScheduleOne.Property.Business;
using NativeEmployee = Il2CppScheduleOne.Employees.Employee;
using NativeProperty = Il2CppScheduleOne.Property.Property;

namespace OrganizedCrime.Runtime;

internal interface IRelease1SmallCourtesyCloseRegistration
{
    bool TryRemove();
}

internal interface IRelease1SmallCourtesyWorldAccess : IDisposable
{
    Release1SmallCourtesyWorldReadStatus TryReadClock(out int elapsedDays, out int minuteOfDay);
    Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products);
    Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops);
    Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging);
    Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots);
    Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room);
    Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked);
    Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount);
    Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason);
    Release1SmallCourtesyWorldReadStatus TryAttachDropClosed(string deadDropGuid, Action callback, out IRelease1SmallCourtesyCloseRegistration? registration);
    Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance);
    Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount);
    Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance);
    Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount);
    Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance);
    Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked);
    Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount);
    Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity);
    Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot);
    Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason);
    Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason);
    Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason);
    Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason);
}

public sealed class S1ApiRelease1SmallCourtesyWorld : IRelease1SmallCourtesyWorld, IDisposable
{
    private readonly IRelease1StoryHostContext _hostContext;
    private readonly IRelease1SmallCourtesyWorldAccess _access;
    private readonly HarmonyLib.Harmony? _lockdownHarmony;
    private CloseEntry? _closeEntry;
    private bool _disposed;

    public S1ApiRelease1SmallCourtesyWorld(
        IRelease1StoryHostContext hostContext,
        ISyndicateHqStorageRuntime? holdRoomStorage = null,
        IRelease1FieldContactRuntime? fieldContacts = null,
        HarmonyLib.Harmony? lockdownHarmony = null)
        : this(hostContext, new S1ApiRelease1SmallCourtesyWorldAccess(holdRoomStorage, fieldContacts), lockdownHarmony) { }

    internal S1ApiRelease1SmallCourtesyWorld(
        IRelease1StoryHostContext hostContext,
        IRelease1SmallCourtesyWorldAccess access,
        HarmonyLib.Harmony? lockdownHarmony = null)
    {
        _hostContext = hostContext ?? throw new ArgumentNullException(nameof(hostContext));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _lockdownHarmony = lockdownHarmony;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (_disposed) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        try
        {
            var status = _hostContext.TryRead(out context);
            if (status == Release1StoryHostContextReadStatus.Ready) return Release1SmallCourtesyWorldReadStatus.Ready;
            context = default;
            return status switch
            {
                Release1StoryHostContextReadStatus.Pending => Release1SmallCourtesyWorldReadStatus.Pending,
                Release1StoryHostContextReadStatus.NotAuthoritative => Release1SmallCourtesyWorldReadStatus.NotAuthoritative,
                Release1StoryHostContextReadStatus.UnsupportedMultiplayer => Release1SmallCourtesyWorldReadStatus.NotAuthoritative,
                Release1StoryHostContextReadStatus.AmbiguousIdentity => Release1SmallCourtesyWorldReadStatus.Faulted,
                _ => Release1SmallCourtesyWorldReadStatus.Faulted
            };
        }
        catch
        {
            context = default;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
    {
        totalMinutes = 0d;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadClock(out var elapsedDays, out var minuteOfDay);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return status;
            if (elapsedDays < 0 || minuteOfDay is < 0 or >= 1_440) return Release1SmallCourtesyWorldReadStatus.Unavailable;
            totalMinutes = checked((double)elapsedDays * 1_440d + minuteOfDay);
            return double.IsFinite(totalMinutes)
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        catch
        {
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) =>
        ReadValidated(_access.TryReadProducts, candidate => candidate.Validate(), out products);

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) =>
        ReadValidated(_access.TryReadDeadDrops, candidate => candidate.Validate(), out drops);

    public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
        Release1SmallCourtesyPackageKind kind,
        out Release1SmallCourtesyPackagingCandidate packaging)
    {
        packaging = null!;
        if (!Enum.IsDefined(kind) || !HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadPackaging(kind, out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || read is null) return status;
            read.Validate();
            packaging = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            packaging = null!;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
        string deadDropGuid,
        out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        if (!IsStableId(deadDropGuid)) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        return ReadValidated(
            (out IReadOnlyList<Release1SmallCourtesySlotSnapshot> read) => _access.TryReadDeadDropSlots(deadDropGuid, out read),
            candidate => candidate.Validate(),
            out slots,
            requireUniqueIndex: true);
    }

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
    {
        room = Release1HoldRoomSnapshot.Unavailable();
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadHoldRoom(out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || read is null)
                return status == Release1SmallCourtesyWorldReadStatus.Ready
                    ? Release1SmallCourtesyWorldReadStatus.Unavailable
                    : status;
            read.Validate();
            room = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
    {
        activity = Release1ProductionActivitySnapshot.Empty;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadProductionActivity(out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || read is null)
                return status == Release1SmallCourtesyWorldReadStatus.Ready
                    ? Release1SmallCourtesyWorldReadStatus.Unavailable
                    : status;
            read.Validate();
            activity = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            activity = Release1ProductionActivitySnapshot.Empty;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
    {
        if (!IsStableId(deadDropGuid) || slotIndex < 0 || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TrySetSlotLocked(deadDropGuid, slotIndex, locked); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    /// <summary>
    /// The largest Release 1 manifest is three units, so the boundary accepts a decrement of one,
    /// two, or three and nothing else. Zero, any positive amount, and anything past the manifest
    /// bound stay rejected, so no OC code path can add to a player's slot or empty an arbitrary
    /// stack.
    /// </summary>
    public const int MaximumSlotDecrement = 3;

    public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
    {
        if (!IsStableId(deadDropGuid) || slotIndex < 0 || amount >= 0 || amount < -MaximumSlotDecrement || !HasAuthority())
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryChangeSlotQuantity(deadDropGuid, slotIndex, amount); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
        string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
    {
        reason = string.Empty;
        if (!IsStableId(deadDropGuid) || slotIndex < 0 || !IsStableId(productId) || !IsStableId(packagingId) ||
            quantity <= 0 || !HasAuthority())
        {
            reason = "the staging request was invalid or the world was not authoritative.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        try
        {
            return _access.TryInsertPackagedProduct(deadDropGuid, slotIndex, productId, packagingId, quantity, out reason);
        }
        catch (Exception ex)
        {
            reason = "inserting the packaged product threw an exception: " + ex.Message;
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
        string deadDropGuid,
        Action<string> callback,
        out IRelease1SmallCourtesyDropSubscription? subscription)
    {
        subscription = null;
        if (!IsStableId(deadDropGuid) || callback is null) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (TryReadContext(out var context) != Release1SmallCourtesyWorldReadStatus.Ready)
            return AuthorityReadStatus();

        if (_closeEntry is not null)
        {
            if (_closeEntry.Active &&
                string.Equals(_closeEntry.DeadDropGuid, deadDropGuid, StringComparison.Ordinal) &&
                _closeEntry.LoadEpoch == context.LoadEpoch &&
                _closeEntry.Callback == callback)
            {
                subscription = _closeEntry.Token;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }

            _closeEntry.Active = false;
            if (!_closeEntry.Registration.TryRemove()) return Release1SmallCourtesyWorldReadStatus.Unavailable;
            _closeEntry = null;
        }

        CloseEntry? entry = null;
        Action guardedCallback = () =>
        {
            if (entry is null || !ReferenceEquals(_closeEntry, entry) || !entry.Active) return;
            if (TryReadContext(out var current) != Release1SmallCourtesyWorldReadStatus.Ready) return;
            if (current.SessionEpoch != entry.SessionEpoch || current.LoadEpoch != entry.LoadEpoch ||
                !string.Equals(current.PlayerId, entry.PlayerId, StringComparison.Ordinal) ||
                !string.Equals(current.ActiveSaveFolder, entry.ActiveSaveFolder, StringComparison.OrdinalIgnoreCase)) return;
            entry.Callback(entry.DeadDropGuid);
        };

        try
        {
            var status = _access.TryAttachDropClosed(deadDropGuid, guardedCallback, out var registration);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || registration is null)
            {
                if (registration is not null && !registration.TryRemove())
                {
                    entry = CloseEntry.Pending(deadDropGuid, callback, context, registration, this);
                    _closeEntry = entry;
                }
                return status == Release1SmallCourtesyWorldReadStatus.Ready
                    ? Release1SmallCourtesyWorldReadStatus.Faulted
                    : status;
            }

            entry = CloseEntry.Attached(deadDropGuid, callback, context, registration, this);
            _closeEntry = entry;
            subscription = entry.Token;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
    {
        balance = 0f;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadCashBalance(out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || !float.IsFinite(read) || read < 0f) return status == Release1SmallCourtesyWorldReadStatus.Ready ? Release1SmallCourtesyWorldReadStatus.Unavailable : status;
            balance = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryChangeCashBalance(amount); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    /// <summary>
    /// The largest wallet debit OC will ever ask for is the capped demand, so the boundary accepts a
    /// magnitude of at most that and nothing past it. Deliberately its own bound rather than
    /// <see cref="MaximumCashDecrement"/> (20000), which 25000 would exceed.
    /// </summary>
    public const float MaximumWalletDebit = 25_000f;

    public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
    {
        if (!float.IsFinite(amount) || amount >= 0f || amount < -MaximumWalletDebit || !HasAuthority())
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try
        {
            var readStatus = _access.TryReadCashBalance(out var before);
            if (readStatus != Release1SmallCourtesyWorldReadStatus.Ready || !float.IsFinite(before) || before < 0f)
                return Release1SmallCourtesyWorldMutationStatus.Unavailable;
            if (before + amount < 0f) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            // The native passthrough is unconditional on sign; only the sibling wrapper's guard was.
            return _access.TryChangeCashBalance(amount);
        }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
    {
        reason = string.Empty;
        if (_lockdownHarmony is null)
        {
            reason = "no Harmony instance was supplied to the lockdown seam.";
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }
        try { return Map(Release1LockdownGatePatch.TryEngage(_lockdownHarmony, _ => { }, HasAuthority), out reason); }
        catch (Exception exception)
        {
            reason = $"the lockdown engage threw: {exception.GetType().Name}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
    {
        reason = string.Empty;
        try { return Map(Release1LockdownGatePatch.TryRelease(HasAuthority), out reason); }
        catch (Exception exception)
        {
            reason = $"the lockdown release threw: {exception.GetType().Name}";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Spec decision 21: NeedsOptionB holds, it does not fail. It maps to Unavailable, the same
    /// recoverable status every other read member on this boundary uses for "not right now", so the
    /// pass retries and the record stays announced and not engaged rather than being rolled back.
    /// </summary>
    private static Release1SmallCourtesyWorldMutationStatus Map(Release1StagingHarnessResult result, out string reason)
    {
        reason = string.Join(" ", result.Lines);
        return result.Status switch
        {
            Release1StagingHarnessStatus.Succeeded => Release1SmallCourtesyWorldMutationStatus.Succeeded,
            Release1StagingHarnessStatus.NeedsOptionB or Release1StagingHarnessStatus.Unavailable => Release1SmallCourtesyWorldMutationStatus.Unavailable,
            Release1StagingHarnessStatus.Ambiguous => Release1SmallCourtesyWorldMutationStatus.Ambiguous,
            _ => Release1SmallCourtesyWorldMutationStatus.Rejected
        };
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        if (!IsStableId(deadDropGuid) || slotIndex < 0) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadDeadDropSlotCashBalance(deadDropGuid, slotIndex, out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return status;
            if (!float.IsFinite(read) || read <= 0f) return Release1SmallCourtesyWorldReadStatus.Unavailable;
            balance = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    /// <summary>
    /// The largest Release 1 cash amount is the Envelope primary tier, so the boundary accepts a
    /// decrement whose magnitude is at most that and nothing past it. Zero, any positive amount, and
    /// anything past the amount bound stay rejected, so no OC code path can add to a slot's cash or
    /// empty an arbitrary balance.
    /// </summary>
    public const float MaximumCashDecrement = (float)Release1TheEnvelopeAssignment.PrimaryAmount;

    public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount)
    {
        if (!IsStableId(deadDropGuid) || slotIndex < 0 || !float.IsFinite(amount) || amount >= 0f || amount < -MaximumCashDecrement || !HasAuthority())
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryChangeDeadDropSlotCashBalance(deadDropGuid, slotIndex, amount); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        if (!IsStableId(closetGuid) || slotIndex < 0) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadHoldRoomSlotCashBalance(closetGuid, slotIndex, out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return status;
            if (!float.IsFinite(read) || read <= 0f) return Release1SmallCourtesyWorldReadStatus.Unavailable;
            balance = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked)
    {
        if (!IsStableId(closetGuid) || slotIndex < 0 || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TrySetHoldRoomSlotLocked(closetGuid, slotIndex, locked); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount)
    {
        if (!IsStableId(closetGuid) || slotIndex < 0 || !float.IsFinite(amount) || amount >= 0f ||
            amount < -MaximumCashDecrement || !HasAuthority())
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryChangeHoldRoomSlotCashBalance(closetGuid, slotIndex, amount); }
        catch { return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    /// <summary>
    /// The spike places the contact one short walk in front of the player and nothing further. Ten
    /// metres is the outer bound the boundary will accept at all, so no caller can put a contact
    /// across the map, and the harness's own frozen constant is well inside it.
    /// </summary>
    public const float MaximumFieldContactAheadMetres = 10f;

    public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
    {
        snapshot = Release1FieldContactSnapshot.Unavailable();
        if (!IsStableId(contactId)) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = _access.TryReadFieldContact(contactId, out var read);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || read is null) return status;
            read.Validate();
            snapshot = read;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Faulted; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
    {
        reason = "the field contact park was refused.";
        if (!IsStableId(contactId) || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryParkFieldContact(contactId, out reason); }
        catch { reason = "the field contact park threw."; return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
    {
        reason = "the field contact unpark was refused.";
        if (!IsStableId(contactId) || !float.IsFinite(aheadMetres) || aheadMetres <= 0f ||
            aheadMetres > MaximumFieldContactAheadMetres || !HasAuthority())
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryUnparkFieldContact(contactId, aheadMetres, out reason); }
        catch { reason = "the field contact unpark threw."; return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
    {
        reason = "the field contact despawn was refused.";
        if (!IsStableId(contactId) || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryDespawnFieldContact(contactId, out reason); }
        catch { reason = "the field contact despawn threw."; return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
    {
        reason = "the field contact provoke was refused.";
        if (!IsStableId(contactId) || !HasAuthority()) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try { return _access.TryProvokeFieldContact(contactId, out reason); }
        catch { reason = "the field contact provoke threw."; return Release1SmallCourtesyWorldMutationStatus.Ambiguous; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_closeEntry is not null)
        {
            _closeEntry.Active = false;
            if (_closeEntry.Registration.TryRemove()) _closeEntry = null;
        }
        _access.Dispose();
    }

    private void Dispose(CloseEntry entry)
    {
        if (!ReferenceEquals(_closeEntry, entry)) return;
        entry.Active = false;
        if (entry.Registration.TryRemove()) _closeEntry = null;
    }

    private bool HasAuthority() => TryReadContext(out _) == Release1SmallCourtesyWorldReadStatus.Ready;

    private Release1SmallCourtesyWorldReadStatus AuthorityReadStatus()
    {
        var status = TryReadContext(out _);
        return status == Release1SmallCourtesyWorldReadStatus.Ready
            ? Release1SmallCourtesyWorldReadStatus.Unavailable
            : status;
    }

    private delegate Release1SmallCourtesyWorldReadStatus ReadMany<T>(out IReadOnlyList<T> values);

    private Release1SmallCourtesyWorldReadStatus ReadValidated<T>(
        ReadMany<T> read,
        Action<T> validate,
        out IReadOnlyList<T> values,
        bool requireUniqueIndex = false)
    {
        values = Array.Empty<T>();
        if (!HasAuthority()) return AuthorityReadStatus();
        try
        {
            var status = read(out var raw);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || raw is null) return status;
            var copy = raw.ToArray();
            foreach (var value in copy)
            {
                if (value is null) return Release1SmallCourtesyWorldReadStatus.Unavailable;
                validate(value);
            }
            if (requireUniqueIndex && copy.Cast<Release1SmallCourtesySlotSnapshot>().Select(value => value.SlotIndex).Distinct().Count() != copy.Length)
                return Release1SmallCourtesyWorldReadStatus.Unavailable;
            values = copy;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            values = Array.Empty<T>();
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    private static bool IsStableId(string? value)
    {
        try { Release1SmallCourtesyAssignment.ValidateStableId(value, nameof(value)); return true; }
        catch { return false; }
    }

    private sealed class CloseEntry
    {
        private CloseEntry(
            string deadDropGuid,
            Action<string> callback,
            Release1StoryHostContextSnapshot context,
            IRelease1SmallCourtesyCloseRegistration registration,
            S1ApiRelease1SmallCourtesyWorld owner,
            bool active)
        {
            DeadDropGuid = deadDropGuid;
            Callback = callback;
            SessionEpoch = context.SessionEpoch;
            LoadEpoch = context.LoadEpoch;
            PlayerId = context.PlayerId;
            ActiveSaveFolder = context.ActiveSaveFolder;
            Registration = registration;
            Active = active;
            Token = new DropSubscription(this, owner);
        }

        public string DeadDropGuid { get; }
        public Action<string> Callback { get; }
        public Guid SessionEpoch { get; }
        public long LoadEpoch { get; }
        public string PlayerId { get; }
        public string ActiveSaveFolder { get; }
        public IRelease1SmallCourtesyCloseRegistration Registration { get; }
        public DropSubscription Token { get; }
        public bool Active { get; set; }

        public static CloseEntry Attached(string guid, Action<string> callback, Release1StoryHostContextSnapshot context, IRelease1SmallCourtesyCloseRegistration registration, S1ApiRelease1SmallCourtesyWorld owner) =>
            new(guid, callback, context, registration, owner, true);

        public static CloseEntry Pending(string guid, Action<string> callback, Release1StoryHostContextSnapshot context, IRelease1SmallCourtesyCloseRegistration registration, S1ApiRelease1SmallCourtesyWorld owner) =>
            new(guid, callback, context, registration, owner, false);
    }

    private sealed class DropSubscription : IRelease1SmallCourtesyDropSubscription
    {
        private readonly CloseEntry _entry;
        private readonly S1ApiRelease1SmallCourtesyWorld _owner;
        public DropSubscription(CloseEntry entry, S1ApiRelease1SmallCourtesyWorld owner) { _entry = entry; _owner = owner; }
        public string DeadDropGuid => _entry.DeadDropGuid;
        public void Dispose() => _owner.Dispose(_entry);
    }
}

internal sealed class S1ApiRelease1SmallCourtesyWorldAccess : IRelease1SmallCourtesyWorldAccess
{
    private const string UnpackagedId = "unpackaged";

    /// <summary>
    /// Matches the tolerance <c>Release1TheEnvelopeCashIdentity</c> uses for its pre-minus-consumed-
    /// equals-post invariant, so a cash balance carrying cents is never rejected by exact float
    /// equality on either side of the boundary.
    /// </summary>
    private const float CashBalanceTolerance = 0.01f;
    private readonly HashSet<(string Guid, int SlotIndex)> _ownedSlotLocks = new();
    private readonly ISyndicateHqStorageRuntime? _holdRoomStorage;
    private readonly IRelease1FieldContactRuntime? _fieldContacts;

    public S1ApiRelease1SmallCourtesyWorldAccess() : this(null, null) { }

    public S1ApiRelease1SmallCourtesyWorldAccess(ISyndicateHqStorageRuntime? holdRoomStorage)
        : this(holdRoomStorage, null) { }

    public S1ApiRelease1SmallCourtesyWorldAccess(
        ISyndicateHqStorageRuntime? holdRoomStorage,
        IRelease1FieldContactRuntime? fieldContacts)
    {
        _holdRoomStorage = holdRoomStorage;
        _fieldContacts = fieldContacts;
    }

    private const string NoFieldContactRuntime = "no field contact runtime is composed.";

    public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
    {
        if (_fieldContacts is not null) return _fieldContacts.TryReadFieldContact(contactId, out snapshot);
        snapshot = Release1FieldContactSnapshot.Unavailable();
        return Release1SmallCourtesyWorldReadStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
    {
        if (_fieldContacts is not null) return _fieldContacts.TryParkFieldContact(contactId, out reason);
        reason = NoFieldContactRuntime;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
    {
        if (_fieldContacts is not null) return _fieldContacts.TryUnparkFieldContact(contactId, aheadMetres, out reason);
        reason = NoFieldContactRuntime;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
    {
        if (_fieldContacts is not null) return _fieldContacts.TryDespawnFieldContact(contactId, out reason);
        reason = NoFieldContactRuntime;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
    {
        if (_fieldContacts is not null) return _fieldContacts.TryProvokeFieldContact(contactId, out reason);
        reason = NoFieldContactRuntime;
        return Release1SmallCourtesyWorldMutationStatus.Unavailable;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
    {
        if (_holdRoomStorage is null)
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        room = _holdRoomStorage.ReadHoldRoom();
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
    {
        if (_holdRoomStorage is null)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        return _holdRoomStorage.TryReadClosetSlotCashBalance(closetGuid, slotIndex, out balance);
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked)
    {
        if (_holdRoomStorage is null) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        return _holdRoomStorage.TrySetClosetSlotLocked(closetGuid, slotIndex, locked);
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount)
    {
        if (_holdRoomStorage is null) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        return _holdRoomStorage.TryChangeClosetSlotCashBalance(closetGuid, slotIndex, amount);
    }

    public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
    {
        activity = Release1ProductionActivitySnapshot.Empty;
        var properties = new List<Release1ProductionPropertySnapshot>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var seenStations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in OwnedProperties())
        {
            if (property == null) continue;
            var code = property.PropertyCode;
            if (string.IsNullOrWhiteSpace(code) || !seenCodes.Add(code)) continue;
            var name = property.PropertyName;
            properties.Add(new Release1ProductionPropertySnapshot(
                code,
                string.IsNullOrWhiteSpace(name) ? code : name,
                ReadEmployees(property),
                ReadStations(property, seenStations)));
        }
        activity = new Release1ProductionActivitySnapshot(properties);
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    /// <summary>The union of the two owned lists. Whether an owned business also appears in OwnedProperties is
    /// unproven; deduplicating by PropertyCode above makes the union correct either way.</summary>
    private static IEnumerable<NativeProperty> OwnedProperties()
    {
        var owned = NativeProperty.OwnedProperties;
        if (owned != null)
            foreach (var property in owned)
                yield return property;
        var businesses = NativeBusiness.OwnedBusinesses;
        if (businesses != null)
            foreach (var business in businesses)
                yield return business;
    }

    private static IReadOnlyList<Release1ProductionEmployeeSnapshot> ReadEmployees(NativeProperty property)
    {
        var employees = new List<Release1ProductionEmployeeSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = property.Employees;
        if (list == null) return employees;
        foreach (var employee in list)
        {
            if (employee == null) continue;
            var id = employee.ID;
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            employees.Add(ReadEmployee(employee, id));
        }
        return employees;
    }

    private static Release1ProductionEmployeeSnapshot ReadEmployee(NativeEmployee employee, string id)
    {
        var holder = employee.Behaviour;
        var behaviour = holder == null ? null : holder.activeBehaviour;
        if (behaviour == null) behaviour = null;
        var displayName = $"{employee.FirstName} {employee.LastName}".Trim();
        return new Release1ProductionEmployeeSnapshot(
            id,
            string.IsNullOrWhiteSpace(displayName) ? id : displayName,
            employee.EmployeeType.ToString(),
            employee.Fired,
            employee.PaidForToday,
            employee.IsWaitingOutside,
            Math.Max(0, employee.TicksSinceLastWork),
            employee.IsAnyWorkInProgress(),
            behaviour is null ? null : behaviour.Name,
            behaviour is null ? null : behaviour.GetIl2CppType().Name,
            behaviour is not null && behaviour.Active);
    }

    private static IReadOnlyList<Release1ProductionStationSnapshot> ReadStations(
        NativeProperty property,
        HashSet<string> seenStations)
    {
        var stations = new List<Release1ProductionStationSnapshot>();
        var items = property.BuildableItems;
        if (items == null) return stations;
        foreach (var item in items)
        {
            if (item == null || item.IsDestroyed) continue;
            var guid = item.GUID.ToString();
            if (string.IsNullOrWhiteSpace(guid) || !seenStations.Add(guid)) continue;
            var station = ReadStation(item, guid);
            if (station is null) { seenStations.Remove(guid); continue; }
            stations.Add(station);
        }
        return stations;
    }

    /// <summary>
    /// Per kind, from the station's own public members; MixingStationMk2 is caught by that cast. Everything
    /// under Growing is deliberately absent: plants are not stations and growth is never a breach. Brick
    /// press, packaging station and laundering station expose no operation object and no running flag at
    /// all, so they are covered only through the employee read, a stated limitation.
    /// </summary>
    private static Release1ProductionStationSnapshot? ReadStation(BuildableItem item, string guid)
    {
        var chemistry = item.TryCast<ChemistryStation>();
        if (chemistry != null)
        {
            var operation = chemistry.CurrentCookOperation;
            return operation == null
                ? new(guid, Release1ProductionStationKind.ChemistryStation, false, null, 0)
                : new(guid, Release1ProductionStationKind.ChemistryStation, true,
                    Identity(operation.RecipeID), Math.Max(0, operation.CurrentTime));
        }

        var oven = item.TryCast<LabOven>();
        if (oven != null)
        {
            var operation = oven.CurrentOperation;
            return operation == null
                ? new(guid, Release1ProductionStationKind.LabOven, false, null, 0)
                : new(guid, Release1ProductionStationKind.LabOven, true,
                    Identity(operation.ProductID), Math.Max(0, operation.CookProgress));
        }

        var mixing = item.TryCast<MixingStation>();
        if (mixing != null)
        {
            var operation = mixing.CurrentMixOperation;
            return operation == null
                ? new(guid, Release1ProductionStationKind.MixingStation, false, null, 0)
                : new(guid, Release1ProductionStationKind.MixingStation, true,
                    Identity(operation.ProductID), Math.Max(0, mixing.CurrentMixTime));
        }

        var cauldron = item.TryCast<Cauldron>();
        if (cauldron != null)
        {
            if (!cauldron.isCooking) return new(guid, Release1ProductionStationKind.Cauldron, false, null, 0);
            var progress = Release1ProductionStationProgress.CauldronProgress(
                cauldron.CookTime, cauldron.RemainingCookTime,
                Release1ProductionStationProgress.CauldronRemainingCountsDown);
            return new(guid, Release1ProductionStationKind.Cauldron, true, "cauldron-cook", progress,
                cauldron.CookTime, cauldron.RemainingCookTime);
        }

        var rack = item.TryCast<DryingRack>();
        if (rack != null)
        {
            var operations = rack.DryingOperations;
            if (operations == null || operations.Count == 0)
                return new(guid, Release1ProductionStationKind.DryingRack, false, null, 0);
            var itemIds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var operation in operations)
                if (operation != null && !string.IsNullOrWhiteSpace(operation.ItemID)) itemIds.Add(operation.ItemID);
            if (itemIds.Count == 0) return new(guid, Release1ProductionStationKind.DryingRack, false, null, 0);
            // Progress is deliberately zero here. DryingOperation.Time has no paired duration and its
            // direction is as unproven as the cauldron's, and a rack's operation count legitimately falls
            // as racks finish, which a progress comparison would misread as a restart. The identity set
            // alone is enough: adding an item id reads as a new operation, removing one never does.
            return new(guid, Release1ProductionStationKind.DryingRack, true, string.Join("|", itemIds), 0);
        }

        return null;
    }

    private static string Identity(string? value) => string.IsNullOrWhiteSpace(value) ? "unnamed" : value;

    public Release1SmallCourtesyWorldReadStatus TryReadClock(out int elapsedDays, out int minuteOfDay)
    {
        elapsedDays = TimeManager.ElapsedDays;
        minuteOfDay = TimeManager.GetMinutesFrom24HourTime(TimeManager.CurrentTime);
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
    {
        var result = new List<Release1SmallCourtesyProductCandidate>();
        foreach (var product in ProductManager.DiscoveredProducts ?? Array.Empty<ApiProductDefinition>())
        {
            if (product is null) continue;
            try
            {
                var candidate = new Release1SmallCourtesyProductCandidate(product.ID, product.Name, product.Price, true);
                candidate.Validate();
                result.Add(candidate);
            }
            catch
            {
                // A stale or malformed wrapper is omitted; no native state is changed.
            }
        }
        products = result;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
    {
        var result = new List<Release1SmallCourtesyDropCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wrapper in DeadDropManager.All ?? Array.Empty<DeadDropInstance>())
        {
            if (wrapper is null) continue;
            try
            {
                var guid = wrapper.GUID;
                if (!TryResolveNativeDrop(guid, out var native, out var ambiguous))
                {
                    if (ambiguous) { drops = Array.Empty<Release1SmallCourtesyDropCandidate>(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }
                    continue;
                }
                if (native == null || !native.gameObject.scene.IsValid()) continue;
                if (string.IsNullOrWhiteSpace(native.DeadDropName) || string.IsNullOrWhiteSpace(native.DeadDropDescription))
                {
                    drops = Array.Empty<Release1SmallCourtesyDropCandidate>();
                    return Release1SmallCourtesyWorldReadStatus.Unavailable;
                }
                var position = wrapper.Position;
                if (!IsFinite(position)) continue;
                var storage = wrapper.Storage;
                if (storage is null) continue;
                var slots = storage.Slots;
                var isEmpty = slots is not null && slots.All(slot => slot is not null && slot.Quantity == 0);
                var candidate = new Release1SmallCourtesyDropCandidate(
                    guid,
                    native.DeadDropName.Trim(),
                    native.DeadDropDescription.Trim(),
                    position.x,
                    position.y,
                    position.z,
                    isEmpty);
                candidate.Validate();
                if (!seen.Add(guid)) { drops = Array.Empty<Release1SmallCourtesyDropCandidate>(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }
                result.Add(candidate);
            }
            catch
            {
                // Unity-aware native checks plus per-wrapper isolation skip stale objects.
            }
        }
        drops = result;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
        Release1SmallCourtesyPackageKind kind,
        out Release1SmallCourtesyPackagingCandidate packaging)
    {
        packaging = null!;
        var id = kind == Release1SmallCourtesyPackageKind.Brick ? "brick" : "jar";
#pragma warning disable CS0618 // Current S1API Release source exposes GetItemDefinition; the installed projection marks it obsolete.
        var definition = ItemManager.GetItemDefinition(id);
#pragma warning restore CS0618
        if (definition is null || !string.Equals(definition.ID, id, StringComparison.Ordinal))
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        packaging = new(definition.ID, definition.Name);
        packaging.Validate();
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
        string deadDropGuid,
        out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        var hasApiDrop = TryResolveApiDrop(deadDropGuid, out var wrapper, out var apiAmbiguous);
        var hasNativeDrop = TryResolveNativeDrop(deadDropGuid, out var native, out var nativeAmbiguous);
        if (!hasApiDrop || !hasNativeDrop)
            return apiAmbiguous || nativeAmbiguous ? Release1SmallCourtesyWorldReadStatus.Unavailable : Release1SmallCourtesyWorldReadStatus.Pending;
        if (native == null || !native.gameObject.scene.IsValid() || native.Storage == null) return Release1SmallCourtesyWorldReadStatus.Pending;
        var storage = wrapper.Storage;
        if (storage is null) return Release1SmallCourtesyWorldReadStatus.Pending;
        var apiSlots = storage.Slots;
        var nativeSlots = native.Storage.ItemSlots;
        if (apiSlots is null || nativeSlots is null || apiSlots.Length != nativeSlots.Count)
            return Release1SmallCourtesyWorldReadStatus.Unavailable;

        var result = new List<Release1SmallCourtesySlotSnapshot>(apiSlots.Length);
        for (var index = 0; index < apiSlots.Length; index++)
        {
            var slot = apiSlots[index];
            if (slot is null) return Release1SmallCourtesyWorldReadStatus.Unavailable;
            var item = slot.ItemInstance;
            if (slot.Quantity == 0 || item is null)
            {
                result.Add(new(index, null, null, 0, false, 0f));
                continue;
            }

            var productId = item.Definition.ID;
            var packagingId = UnpackagedId;
            var packaged = false;
            var monetaryValue = 0f;
            if (item is ProductInstance product)
            {
                packaged = product.IsPackaged;
                if (packaged) packagingId = product.AppliedPackaging.ID;
                var nativeItem = nativeSlots[index].ItemInstance;
                var nativeProduct = nativeItem == null ? null : nativeItem.TryCast<ProductItemInstance>();
                if (nativeProduct == null) return Release1SmallCourtesyWorldReadStatus.Unavailable;
                monetaryValue = nativeProduct.GetMonetaryValue();
            }

            var snapshot = new Release1SmallCourtesySlotSnapshot(index, productId, packagingId, slot.Quantity, packaged, monetaryValue);
            snapshot.Validate();
            result.Add(snapshot);
        }
        slots = result;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
    {
        if (!TryResolveNativeSlot(deadDropGuid, slotIndex, out var slot)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var key = (deadDropGuid, slotIndex);
        if (locked && !_ownedSlotLocks.Contains(key) && (slot.IsRemovalLocked || slot.IsAddLocked))
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        if (!locked && !_ownedSlotLocks.Contains(key)) return Release1SmallCourtesyWorldMutationStatus.Rejected;

        if (locked) _ownedSlotLocks.Add(key);
        try
        {
            slot.SetIsRemovalLocked(locked);
            slot.SetIsAddLocked(locked);
            if (slot.IsRemovalLocked != locked || slot.IsAddLocked != locked)
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            if (!locked) _ownedSlotLocks.Remove(key);
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch
        {
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
    {
        var key = (deadDropGuid, slotIndex);
        if (!_ownedSlotLocks.Contains(key)) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        if (!TryResolveNativeSlot(deadDropGuid, slotIndex, out var slot))
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var preQuantity = slot.Quantity;
        if (amount >= 0 || preQuantity + amount < 0) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try
        {
            slot.SetIsRemovalLocked(false);
            if (slot.IsRemovalLocked) return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            slot.ChangeQuantity(amount, true);
        }
        finally
        {
            slot.SetIsRemovalLocked(true);
        }
        if (!slot.IsRemovalLocked) return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        return slot.Quantity == preQuantity + amount
            ? Release1SmallCourtesyWorldMutationStatus.Succeeded
            : Release1SmallCourtesyWorldMutationStatus.Ambiguous;
    }

    public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
        string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
    {
        reason = string.Empty;
        if (!TryResolveNativeSlot(deadDropGuid, slotIndex, out var slot))
        {
            reason = "the dead drop slot could not be resolved.";
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }
        if (slot.Quantity != 0)
        {
            reason = "the slot was not empty before staging.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        if (!TryResolveNativeDrop(deadDropGuid, out var drop, out _) || drop.Storage == null)
        {
            reason = "the dead drop storage could not be resolved.";
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }
        var storage = drop.Storage;

        var rawProductDefinition = Il2CppScheduleOne.Registry.GetItem(productId);
        var productDefinition = rawProductDefinition == null
            ? null
            : rawProductDefinition.TryCast<Il2CppScheduleOne.Product.ProductDefinition>();
        if (productDefinition == null)
        {
            reason = "the product definition could not be resolved from the registry.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }
        var rawPackagingDefinition = Il2CppScheduleOne.Registry.GetItem(packagingId);
        var packagingDefinition = rawPackagingDefinition == null
            ? null
            : rawPackagingDefinition.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>();
        if (packagingDefinition == null)
        {
            reason = "the packaging definition could not be resolved from the registry.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        var rawInstance = productDefinition.GetDefaultInstance(quantity);
        var instance = rawInstance == null ? null : rawInstance.TryCast<ProductItemInstance>();
        if (instance == null)
        {
            reason = "the packaged product instance could not be created.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        try
        {
            instance.SetPackaging(packagingDefinition);
            if (!storage.CanItemFit(instance, 1))
            {
                reason = "the storage refused the item.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            storage.InsertItem(instance, true);
        }
        catch (Exception ex)
        {
            reason = "inserting the packaged product threw an exception: " + ex.Message;
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }

        var readStatus = TryReadDeadDropSlots(deadDropGuid, out var slots);
        if (readStatus != Release1SmallCourtesyWorldReadStatus.Ready)
        {
            reason = $"the slot could not be read back after staging, status was {readStatus}.";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
        var matches = slots
            .Where(candidate =>
                string.Equals(candidate.ProductId, productId, StringComparison.Ordinal) &&
                string.Equals(candidate.PackagingId, packagingId, StringComparison.Ordinal) &&
                candidate.Quantity == quantity)
            .ToArray();
        if (matches.Length != 1)
        {
            reason = $"expected exactly one slot to hold the staged item after insertion, found {matches.Length}.";
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }

        reason = $"the packaged product was staged and read back with the exact expected values in slot {matches[0].SlotIndex}.";
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }

    public Release1SmallCourtesyWorldReadStatus TryAttachDropClosed(
        string deadDropGuid,
        Action callback,
        out IRelease1SmallCourtesyCloseRegistration? registration)
    {
        registration = null;
        if (!TryResolveApiDrop(deadDropGuid, out var wrapper, out var ambiguous))
            return ambiguous ? Release1SmallCourtesyWorldReadStatus.Unavailable : Release1SmallCourtesyWorldReadStatus.Pending;
        var storage = wrapper.Storage;
        if (storage is null) return Release1SmallCourtesyWorldReadStatus.Pending;
        var created = new S1ApiCloseRegistration(wrapper, storage, callback);
        registration = created;
        try
        {
            storage.OnClosed += callback;
            created.MarkAttached();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        catch
        {
            return Release1SmallCourtesyWorldReadStatus.Faulted;
        }
    }

    public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
    {
        balance = Money.GetCashBalance();
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
    {
        Money.ChangeCashBalance(amount, true, false);
        return Release1SmallCourtesyWorldMutationStatus.Succeeded;
    }

    public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
    {
        balance = 0f;
        if (!TryResolveNativeSlot(deadDropGuid, slotIndex, out var slot)) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (slot.Quantity == 0) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        var item = slot.ItemInstance;
        var cash = item == null ? null : item.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
        if (cash == null) return Release1SmallCourtesyWorldReadStatus.Unavailable;
        balance = cash.Balance;
        return Release1SmallCourtesyWorldReadStatus.Ready;
    }

    public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount)
    {
        var key = (deadDropGuid, slotIndex);
        if (!_ownedSlotLocks.Contains(key)) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        if (!TryResolveNativeSlot(deadDropGuid, slotIndex, out var slot)) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var item = slot.ItemInstance;
        var cash = item == null ? null : item.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
        if (cash == null) return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var preBalance = cash.Balance;
        if (amount >= 0f || preBalance + amount < 0f) return Release1SmallCourtesyWorldMutationStatus.Rejected;
        try
        {
            slot.SetIsRemovalLocked(false);
            if (slot.IsRemovalLocked) return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            cash.ChangeBalance(amount);
        }
        finally
        {
            slot.SetIsRemovalLocked(true);
        }
        if (!slot.IsRemovalLocked) return Release1SmallCourtesyWorldMutationStatus.Ambiguous;

        var expected = preBalance + amount;
        var postItem = slot.ItemInstance;
        if (expected <= 0f)
        {
            // A decrement to exactly zero has two valid native outcomes: the game may empty the slot
            // entirely, or it may leave a CashInstance behind at a zero balance. Both are accepted as
            // Succeeded with a read-back balance of 0; only a CashInstance surviving at a non-zero
            // balance is a genuine mismatch.
            if (postItem == null || slot.Quantity == 0) return Release1SmallCourtesyWorldMutationStatus.Succeeded;
            var zeroCash = postItem.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
            return zeroCash != null && MathF.Abs(zeroCash.Balance) <= CashBalanceTolerance
                ? Release1SmallCourtesyWorldMutationStatus.Succeeded
                : Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
        var postCash = postItem == null ? null : postItem.TryCast<Il2CppScheduleOne.ItemFramework.CashInstance>();
        return postCash != null && MathF.Abs(postCash.Balance - expected) <= CashBalanceTolerance
            ? Release1SmallCourtesyWorldMutationStatus.Succeeded
            : Release1SmallCourtesyWorldMutationStatus.Ambiguous;
    }

    public void Dispose()
    {
        foreach (var (guid, slotIndex) in _ownedSlotLocks.ToArray())
        {
            if (TryResolveNativeSlot(guid, slotIndex, out var slot))
            {
                try { slot.SetIsRemovalLocked(false); slot.SetIsAddLocked(false); }
                catch { continue; }
            }
            _ownedSlotLocks.Remove((guid, slotIndex));
        }
        // _fieldContacts, like the sibling _holdRoomStorage above, is injected and owned by Mod.cs,
        // which creates and disposes it directly; disposing it here too would be a second owner for
        // the same instance, matching neither _holdRoomStorage's convention nor this class's own.
    }

    private static bool TryResolveApiDrop(string guid, out DeadDropInstance wrapper, out bool ambiguous)
    {
        wrapper = null!;
        ambiguous = false;
        var matches = (DeadDropManager.All ?? Array.Empty<DeadDropInstance>())
            .Where(candidate => candidate is not null && string.Equals(candidate.GUID, guid, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length == 1) { wrapper = matches[0]; return true; }
        ambiguous = matches.Length > 1;
        return false;
    }

    private static bool TryResolveNativeDrop(string guid, out DeadDrop drop, out bool ambiguous)
    {
        drop = null!;
        ambiguous = false;
        var found = 0;
        var registry = DeadDrop.DeadDrops;
        if (registry is null) return false;
        foreach (var candidate in registry)
        {
            if (candidate == null) continue;
            try
            {
                if (!candidate.gameObject.scene.IsValid() || !string.Equals(candidate.GUID.ToString(), guid, StringComparison.Ordinal)) continue;
                found++;
                if (found == 1) drop = candidate;
                if (found > 1) { ambiguous = true; drop = null!; return false; }
            }
            catch
            {
                // Destroyed Unity objects compare equal to null; race-time failures are skipped.
            }
        }
        return found == 1;
    }

    private static bool TryResolveNativeSlot(string guid, int slotIndex, out Il2CppScheduleOne.ItemFramework.ItemSlot slot)
    {
        slot = null!;
        if (!TryResolveNativeDrop(guid, out var drop, out _) || drop.Storage == null || drop.Storage.ItemSlots is null ||
            slotIndex < 0 || slotIndex >= drop.Storage.ItemSlots.Count) return false;
        slot = drop.Storage.ItemSlots[slotIndex];
        return slot is not null;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private sealed class S1ApiCloseRegistration : IRelease1SmallCourtesyCloseRegistration
    {
        private readonly DeadDropInstance _drop;
        private readonly StorageInstance _storage;
        private readonly Action _callback;
        private bool _maybeAttached = true;

        public S1ApiCloseRegistration(DeadDropInstance drop, StorageInstance storage, Action callback) { _drop = drop; _storage = storage; _callback = callback; }
        public void MarkAttached() => _maybeAttached = true;
        public bool TryRemove()
        {
            if (!_maybeAttached) return true;
            try { _storage.OnClosed -= _callback; _maybeAttached = false; return true; }
            catch { return false; }
        }
    }
}
