using Il2CppScheduleOne.PlayerScripts;
using S1API.Entities;
using ApiPlayer = S1API.Entities.Player;
using S1API.GameTime;
using Money = S1API.Money.Money;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Owner QA only (OwnerQaKeys): the native reads behind
/// <see cref="IChapter3BeatProbeWorld"/> provide the snapshot through S1API money,
/// time, and crime data plus native player inventory slots, and invincibility
/// through <c>S1API.Entities.Player.IsInvincible</c>. The hurt and knockout probes
/// are gone now the beating owns health; snapshot and invincibility stay for tuning.
/// Every entry point is total: any fault reads as a refusal with a reason, never throws.
/// </summary>
public sealed class S1ApiChapter3BeatProbeWorld : IChapter3BeatProbeWorld, IDisposable
{
    private readonly Action<string> _warn;
    private readonly Func<bool> _isHost;
    private bool _disposed;

    public S1ApiChapter3BeatProbeWorld(Action<string> warn, Func<bool> isHost)
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

    public bool TryReadSnapshot(out Chapter3BeatSnapshot snapshot, out string reason) =>
        TryReadSnapshotCore(out snapshot, out reason);

    public bool TryReadInvincible(out bool on, out string reason)
    {
        on = false;
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beat probe world is disposed.";
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
            on = player.IsInvincible;
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("read invincible", exception);
            reason = "reading invincibility threw.";
            return false;
        }
    }

    public bool TrySetInvincible(bool on, out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beat probe world is disposed.";
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
            player.IsInvincible = on;
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("set invincible", exception);
            reason = "setting invincibility threw.";
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private bool TryReadSnapshotCore(out Chapter3BeatSnapshot snapshot, out string reason)
    {
        snapshot = EmptySnapshot();
        reason = string.Empty;
        if (_disposed)
        {
            reason = "the beat probe world is disposed.";
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
            var position = player.Position;
            string pursuit;
            try
            {
                var crime = player.CrimeData;
                pursuit = crime is null ? "unreadable" : crime.CurrentPursuitLevel.ToString();
            }
            catch
            {
                pursuit = "unreadable";
            }
            string timeText;
            int elapsedDays;
            try
            {
                elapsedDays = TimeManager.ElapsedDays;
                timeText = TimeManager.GetFormatted12HourTime();
            }
            catch
            {
                elapsedDays = -1;
                timeText = "unreadable";
            }
            snapshot = new Chapter3BeatSnapshot(
                player.CurrentHealth,
                player.MaxHealth,
                !player.IsDead,
                player.IsInvincible,
                Money.GetCashBalance(),
                Money.GetOnlineBalance(),
                ReadSlots(),
                position.x,
                position.y,
                position.z,
                elapsedDays,
                timeText,
                pursuit);
            return true;
        }
        catch (Exception exception)
        {
            FailClosed("read snapshot", exception);
            reason = "reading the snapshot threw.";
            return false;
        }
    }

    private IReadOnlyList<Chapter3BeatSlot> ReadSlots()
    {
        var slots = new List<Chapter3BeatSlot>();
        try
        {
            if (!PlayerInventory.InstanceExists)
                return slots;
            var inventory = PlayerInventory.Instance;
            if (inventory is null)
                return slots;
            var hotbar = inventory.hotbarSlots;
            if (hotbar is null)
                return slots;
            foreach (var slot in hotbar)
            {
                if (slot is null)
                    continue;
                string id;
                try
                {
                    id = slot.ItemInstance?.GetItemData()?.ID ?? string.Empty;
                }
                catch
                {
                    id = string.Empty;
                }
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var quantity = 0;
                try { quantity = slot.Quantity; }
                catch { }
                slots.Add(new Chapter3BeatSlot(id, quantity));
            }
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 beat probe slot read failed: {exception.GetType().Name}.");
        }
        return slots;
    }

    private void FailClosed(string what, Exception exception) =>
        _warn($"Chapter 3 beat probe {what} failed closed: {exception.GetType().Name}.");

    private static Chapter3BeatSnapshot EmptySnapshot() => new(
        0f, 0f, false, false, 0f, 0f,
        Array.Empty<Chapter3BeatSlot>(), 0f, 0f, 0f, -1, "unreadable", "unreadable");

}
