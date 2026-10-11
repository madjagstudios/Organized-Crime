namespace OrganizedCrime.Runtime;

/// <summary>
/// The game-touching surface behind the beat probes. Total like every other world boundary:
/// bools and out values, never an exception.
/// </summary>
public interface IChapter3BeatProbeWorld
{
    bool IsHost { get; }
    bool TryReadSnapshot(out Chapter3BeatSnapshot snapshot, out string reason);
    bool TryReadInvincible(out bool on, out string reason);
    bool TrySetInvincible(bool on, out string reason);
}

/// <summary>
/// One observed player state: health, money, inventory, position, clock, and pursuit.
/// </summary>
public sealed record Chapter3BeatSlot(string Id, int Quantity);

public sealed record Chapter3BeatSnapshot(
    float Health,
    float MaxHealth,
    bool IsAlive,
    bool IsInvincible,
    float Cash,
    float Bank,
    IReadOnlyList<Chapter3BeatSlot> Slots,
    float X,
    float Y,
    float Z,
    int ElapsedDays,
    string TimeText,
    string Pursuit)
{
    public int SlotCount => Slots.Count;
}

/// <summary>
/// Pure probe order, parsing, and formatting over an injected game surface, so the fakes pin
/// every refusal and the diff text. Changes no Chapter 3 state and no persistence: snapshots
/// only read, hurt and knockout only touch the living player, and invincible off restores the
/// exact pre-on state the probe recorded.
/// </summary>
public static class Chapter3BeatProbe
{
    public const int KnockoutTimeoutSeconds = 60;
    public const int MinHurtPercent = 1;
    public const int MaxHurtPercent = 100;

    // Trigger names the f7 build passes to AvatarAnimation.SetTrigger
    // (RpcLogic___Punch_2166136261 plays "Punch" on the avatar rig the
    // NPCs share). The probe accepts any string so the owner can try
    // more; these are the known-good starting list.
    public static readonly IReadOnlyList<string> KnownTriggers = new[]
    {
        "Punch",
        "Shoot",
        "Nod",
        "GrabItem",
    };

    public static bool TryParsePercent(string? text, out int percent)
    {
        percent = 0;
        if (!int.TryParse(text?.Trim(), out var parsed))
            return false;
        if (parsed < MinHurtPercent || parsed > MaxHurtPercent)
            return false;
        percent = parsed;
        return true;
    }

    public static IReadOnlyList<string> DescribeSnapshot(Chapter3BeatSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"health: {snapshot.Health:0} / {snapshot.MaxHealth:0} ({(snapshot.IsAlive ? "alive" : "dead")}, invincible {(snapshot.IsInvincible ? "on" : "off")})",
            $"cash: ${snapshot.Cash:0}, bank: ${snapshot.Bank:0}",
            DescribeSlots(snapshot.Slots),
            $"position: ({snapshot.X:0.0}, {snapshot.Y:0.0}, {snapshot.Z:0.0})",
            $"time: {snapshot.TimeText}, day {snapshot.ElapsedDays}",
            $"pursuit: {snapshot.Pursuit}",
        };
        return lines;
    }

    public static string DescribeSlots(IReadOnlyList<Chapter3BeatSlot> slots)
    {
        if (slots.Count == 0)
            return "inventory: 0 slots";
        var items = string.Join(", ", slots.Select(slot => $"{slot.Id} x{slot.Quantity}"));
        return $"inventory: {slots.Count} slots ({items})";
    }

    /// <summary>
    /// The knockout closing diff: where they woke, the time and day
    /// elapsed, the change in cash and bank, the inventory change,
    /// the health after, and whether the bill screen showed.
    /// </summary>
    public static IReadOnlyList<string> DescribeDiff(
        Chapter3BeatSnapshot before,
        Chapter3BeatSnapshot after,
        bool billShown,
        bool timedOut)
    {
        var dayDelta = after.ElapsedDays - before.ElapsedDays;
        var cashDelta = after.Cash - before.Cash;
        var bankDelta = after.Bank - before.Bank;
        return new[]
        {
            timedOut
                ? $"knockout timed out after {KnockoutTimeoutSeconds} s with no revive."
                : "knockout complete: the player revived through the native flow.",
            $"woke at ({after.X:0.0}, {after.Y:0.0}, {after.Z:0.0}) from ({before.X:0.0}, {before.Y:0.0}, {before.Z:0.0}).",
            $"time {before.TimeText} day {before.ElapsedDays} -> {after.TimeText} day {after.ElapsedDays} ({dayDelta:+0;-0;+0} days).",
            $"cash ${before.Cash:0} -> ${after.Cash:0} ({cashDelta:+0;-0;+0}), bank ${before.Bank:0} -> ${after.Bank:0} ({bankDelta:+0;-0;+0}).",
            $"inventory: {DescribeInventoryChange(before.Slots, after.Slots)}",
            $"health after: {after.Health:0} / {after.MaxHealth:0} ({(after.IsAlive ? "alive" : "dead")}).",
            $"bill screen showed: {(billShown ? "yes" : "no")}.",
        };
    }

    public static string DescribeInventoryChange(
        IReadOnlyList<Chapter3BeatSlot> before,
        IReadOnlyList<Chapter3BeatSlot> after)
    {
        var beforeById = before.GroupBy(slot => slot.Id).ToDictionary(group => group.Key, group => group.Sum(slot => slot.Quantity));
        var afterById = after.GroupBy(slot => slot.Id).ToDictionary(group => group.Key, group => group.Sum(slot => slot.Quantity));
        var changes = new List<string>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).OrderBy(id => id, StringComparer.Ordinal))
        {
            beforeById.TryGetValue(id, out var was);
            afterById.TryGetValue(id, out var now);
            if (was != now)
                changes.Add($"{id} {was} -> {now}");
        }
        if (changes.Count == 0)
            return $"{before.Count} slots, unchanged";
        return $"{before.Count} -> {after.Count} slots ({string.Join(", ", changes)})";
    }
}

/// <summary>
/// The invincible on/off memory: on records whether the probe itself flipped the seam, and
/// off only touches it when the probe did, so off restores exactly what was there before on.
/// </summary>
public sealed class Chapter3BeatInvincibleProbe
{
    private readonly IChapter3BeatProbeWorld _world;
    private bool _setByProbe;

    public Chapter3BeatInvincibleProbe(IChapter3BeatProbeWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public IReadOnlyList<string> TurnOn()
    {
        if (!_world.TryReadInvincible(out var on, out var reason))
            return new[] { $"Chapter 3 QA beat probe invincible refused: {reason}" };
        if (on)
            return new[] { "Chapter 3 QA beat probe invincible: already on, left as found; this probe will not turn it off." };
        if (!_world.TrySetInvincible(true, out reason))
            return new[] { $"Chapter 3 QA beat probe invincible refused: {reason}" };
        _setByProbe = true;
        return new[] { "Chapter 3 QA beat probe invincible: on." };
    }

    public IReadOnlyList<string> TurnOff()
    {
        if (!_setByProbe)
        {
            if (!_world.TryReadInvincible(out var readOn, out var readReason))
                return new[] { $"Chapter 3 QA beat probe invincible refused: {readReason}" };
            return readOn
                ? new[] { "Chapter 3 QA beat probe invincible refused: on, but not by this probe; left on." }
                : new[] { "Chapter 3 QA beat probe invincible: already off." };
        }
        if (!_world.TrySetInvincible(false, out var reason))
            return new[] { $"Chapter 3 QA beat probe invincible refused: {reason}" };
        _setByProbe = false;
        return new[] { "Chapter 3 QA beat probe invincible: off, restored." };
    }
}
