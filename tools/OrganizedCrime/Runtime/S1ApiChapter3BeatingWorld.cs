using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using NativePlayerHealth = Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth;
using S1API.Entities;
using S1API.Property;
using ApiPlayer = S1API.Entities.Player;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-135: the native reads and writes behind
/// <see cref="IChapter3BeatingWorld"/>:
/// <list type="bullet">
/// <item>the lock is the death path's own input seam: S1API
/// invincibility (a TakeDamage prefix), <c>PlayerMovement.CanMove</c>,
/// and <c>PlayerInventory.SetInventoryEnabled</c>, which also drops
/// equipping and hides the hotbar. Look stays on so the player
/// watches Arthur; the HUD stays so the health bar drops. Every
/// prior is recorded and restored exactly;</item>
/// <item>health steps through native <c>PlayerHealth.SetHealth</c>,
/// which fires the HUD event and bypasses invincibility;</item>
/// <item>the fades use <c>BlackOverlay</c>, the same overlay the
/// native respawn opens and closes, with its 0.5 s default;</item>
/// <item>the wake reuses the native respawn pick -
/// <c>LastVisitedProperty</c>'s exterior spawn plus the up vector,
/// else the first owned property - through
/// <c>PlayerMovement.Teleport</c>, the same call the revive and
/// free paths use;</item>
/// <item>the bill is the native <c>HospitalBillScreen</c>, opened
/// with <c>arrested</c> forced false.</item>
/// </list>
/// Every entry point is total: any fault reads as a refusal with a
/// reason, never throws. Nothing is touched before FishNet reports
/// the local player spawned, and the unlock is idempotent.
/// </summary>
public sealed class S1ApiChapter3BeatingWorld : IChapter3BeatingWorld, IDisposable
{
    private readonly Action<string> _warn;
    private readonly Func<bool> _isHost;
    private bool _disposed;
    private bool _locked;
    private bool _priorInvincible;
    private bool _priorCanMove = true;
    private bool _priorEquipping = true;

    public S1ApiChapter3BeatingWorld(Action<string> warn, Func<bool> isHost)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _isHost = isHost ?? throw new ArgumentNullException(nameof(isHost));
    }

    public bool IsHost
    {
        get
        {
            try { return _isHost(); }
            catch { return false; }
        }
    }

    public bool IsPlayerSpawned
    {
        get
        {
            try
            {
                var native = Il2CppScheduleOne.PlayerScripts.Player.Local;
                return native is not null && native.IsSpawned;
            }
            catch { return false; }
        }
    }

    public bool TryReadHealth(out float health, out float maxHealth, out string reason)
    {
        health = 0f;
        maxHealth = 0f;
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            var player = ApiPlayer.Local;
            if (player is null)
            {
                reason = "the local player was missing.";
                return false;
            }
            health = player.CurrentHealth;
            maxHealth = player.MaxHealth;
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("read health", exception);
            reason = "reading health threw.";
            return false;
        }
    }

    public bool TrySetHealth(float health, out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            var nativeHealth = TryGetSpawnedHealth(out reason);
            if (nativeHealth is null)
                return false;
            if (!nativeHealth.IsAlive)
            {
                reason = "the player is not up.";
                return false;
            }
            // The coordinator never aims below the 25% floor; clamp
            // defensively so a beating can never kill.
            nativeHealth.SetHealth(Math.Max(1f, health));
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("set health", exception);
            reason = "setting health threw.";
            return false;
        }
    }

    public bool TryLock(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        if (_locked)
            return true;
        try
        {
            var player = ApiPlayer.Local;
            if (player is null)
            {
                reason = "the local player was missing.";
                return false;
            }
            var native = Il2CppScheduleOne.PlayerScripts.Player.Local;
            if (native is null || !native.IsSpawned)
            {
                reason = "the local player is not spawned yet.";
                return false;
            }
            var movement = TryResolveMovement();
            if (movement is null)
            {
                reason = "the player movement was missing.";
                return false;
            }
            if (!PlayerInventory.InstanceExists || PlayerInventory.Instance is null)
            {
                reason = "the player inventory was missing.";
                return false;
            }

            _priorInvincible = player.IsInvincible;
            _priorCanMove = movement.CanMove;
            _priorEquipping = PlayerInventory.Instance.EquippingEnabled;

            // Protection first, then the input seams.
            player.IsInvincible = true;
            movement.CanMove = false;
            PlayerInventory.Instance.SetInventoryEnabled(false);
            _locked = true;
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("lock", exception);
            reason = "locking the player threw.";
            return false;
        }
    }

    public bool TryUnlock(out string reason)
    {
        reason = string.Empty;
        if (!_locked)
            return true;
        var failures = new List<string>();
        try
        {
            var player = ApiPlayer.Local;
            if (player is null)
            {
                failures.Add("the local player was missing.");
            }
            else
            {
                // Movement and items back first; protection drops last.
                try
                {
                    // f7 holds movement as a local-player singleton, not on the Player object.
                    var movement = TryResolveMovement();
                    if (movement is null)
                        failures.Add("the player movement was missing.");
                    else
                        movement.CanMove = _priorCanMove;
                }
                catch (Exception exception)
                {
                    failures.Add($"movement threw {exception.GetType().Name}.");
                }
                try
                {
                    if (!PlayerInventory.InstanceExists || PlayerInventory.Instance is null)
                    {
                        failures.Add("the player inventory was missing.");
                    }
                    else
                    {
                        // SetInventoryEnabled(true) shows the hotbar but
                        // does not re-enable equipping (f7 body), so the
                        // recorded equipping state goes back separately.
                        PlayerInventory.Instance.SetInventoryEnabled(true);
                        PlayerInventory.Instance.SetEquippingEnabled(_priorEquipping);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"inventory threw {exception.GetType().Name}.");
                }
                try
                {
                    player.IsInvincible = _priorInvincible;
                }
                catch (Exception exception)
                {
                    failures.Add($"invincibility threw {exception.GetType().Name}.");
                }
            }
            try
            {
                if (BlackOverlay.InstanceExists && BlackOverlay.Instance is not null && BlackOverlay.Instance.isShown)
                    BlackOverlay.Instance.Close();
            }
            catch (Exception exception)
            {
                failures.Add($"overlay threw {exception.GetType().Name}.");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"unlock threw {exception.GetType().Name}.");
        }
        if (failures.Count == 0)
        {
            _locked = false;
            return true;
        }
        // Stay locked so a retry still restores; the coordinator and
        // OnPreLoad both retry, and a reload rebuilds the natives.
        reason = string.Join(" ", failures);
        return false;
    }

    public bool TryFadeOut(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            if (!BlackOverlay.InstanceExists || BlackOverlay.Instance is null)
            {
                reason = "the black overlay was missing.";
                return false;
            }
            BlackOverlay.Instance.Open();
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("fade out", exception);
            reason = "fading out threw.";
            return false;
        }
    }

    public bool TryFadeIn(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            if (!BlackOverlay.InstanceExists || BlackOverlay.Instance is null)
            {
                reason = "the black overlay was missing.";
                return false;
            }
            BlackOverlay.Instance.Close();
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("fade in", exception);
            reason = "fading in threw.";
            return false;
        }
    }

    public bool TryJoltCamera(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            if (!Il2CppScheduleOne.PlayerScripts.PlayerCamera.InstanceExists
                || Il2CppScheduleOne.PlayerScripts.PlayerCamera.Instance is null)
            {
                reason = "the player camera was missing.";
                return false;
            }
            Il2CppScheduleOne.PlayerScripts.PlayerCamera.Instance.JoltCamera();
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("jolt camera", exception);
            reason = "jolting the camera threw.";
            return false;
        }
    }

    public bool TryMoveToWake(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason)
    {
        wokeAt = (0f, 0f, 0f);
        propertyCode = string.Empty;
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            var player = ApiPlayer.Local;
            if (player is null)
            {
                reason = "the local player was missing.";
                return false;
            }
            var property = player.LastVisitedProperty ?? FirstProperty();
            if (property is null)
            {
                reason = "no property to wake at.";
                return false;
            }
            // The native respawn pick: the property's exterior spawn
            // plus the up vector (f7 respawn routine), facing its
            // default rotation.
            var position = property.ExteriorSpawnPosition + UnityEngine.Vector3.up;
            var native = Il2CppScheduleOne.PlayerScripts.Player.Local;
            var movement = TryResolveMovement();
            if (movement is null)
            {
                reason = "the player movement was missing.";
                return false;
            }
            movement.Teleport(position, false);
            try
            {
                var body = native?.gameObject?.transform;
                if (body is not null)
                    body.rotation = UnityEngine.Quaternion.Euler(0f, property.DefaultRotation, 0f);
            }
            catch (Exception exception)
            {
                _warn($"Chapter 3 beating wake rotation failed: {exception.GetType().Name}.");
            }
            try
            {
                if (Il2CppScheduleOne.PlayerScripts.PlayerCamera.InstanceExists
                    && Il2CppScheduleOne.PlayerScripts.PlayerCamera.Instance is not null)
                    Il2CppScheduleOne.PlayerScripts.PlayerCamera.Instance.ResetRotation();
            }
            catch (Exception exception)
            {
                _warn($"Chapter 3 beating camera reset failed: {exception.GetType().Name}.");
            }
            wokeAt = (position.x, position.y, position.z);
            propertyCode = property.PropertyCode ?? "unknown";
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("move to wake", exception);
            reason = "moving to the wake spot threw.";
            return false;
        }
    }

    public bool TryOpenBill(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beating world is disposed.";
            return false;
        }
        try
        {
            if (!HospitalBillScreen.InstanceExists || HospitalBillScreen.Instance is null)
            {
                reason = "the hospital bill screen was missing.";
                return false;
            }
            HospitalBillScreen.Instance.Open();
            // Open copies Player.IsArrested; a beating is never an
            // arrest, so force the plain variant.
            try { HospitalBillScreen.Instance.arrested = false; }
            catch (Exception exception)
            {
                _warn($"Chapter 3 beating bill arrested flag failed: {exception.GetType().Name}.");
            }
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("open bill", exception);
            reason = "opening the bill threw.";
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        TryUnlock(out _);
    }

    private static PropertyWrapper? FirstProperty()
    {
        try
        {
            var owned = PropertyManager.GetOwnedProperties();
            if (owned is null)
                return null;
            foreach (var property in owned)
                return property;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static NativePlayerHealth? TryGetSpawnedHealth(out string reason)
    {
        reason = string.Empty;
        var native = Il2CppScheduleOne.PlayerScripts.Player.Local;
        if (native is null)
        {
            reason = "the local player was missing.";
            return null;
        }
        if (!native.IsSpawned)
        {
            reason = "the local player is not spawned yet.";
            return null;
        }
        var health = native.Health;
        if (health is null)
        {
            reason = "the player health was missing.";
            return null;
        }
        return health;
    }

    // f7 holds the local player movement as a PlayerSingleton, not as a
    // component on the Player GameObject (PlayerSingleton<T> exposes
    // InstanceExists/Instance; the HQ transport resolves it the same way),
    // so a GetComponent lookup always misses. Null reads as missing.
    private static PlayerMovement? TryResolveMovement()
    {
        try
        {
            if (!PlayerSingleton<PlayerMovement>.InstanceExists)
                return null;
            return PlayerSingleton<PlayerMovement>.Instance;
        }
        catch
        {
            return null;
        }
    }

    private void FailClosed(string what, Exception exception) =>
        _warn($"Chapter 3 beating {what} failed closed: {exception.GetType().Name}.");
}
