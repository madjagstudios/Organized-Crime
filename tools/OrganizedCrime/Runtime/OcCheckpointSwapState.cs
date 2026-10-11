using NativeLocation = Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The prefix's change record: whether the manager field was swapped and the
/// original it must be restored to. The postfix and the finalizer share it,
/// so a restore is idempotent and never skipped.
/// </summary>
internal sealed record OcCheckpointSwap(bool Swapped, object? Original)
{
    public static OcCheckpointSwap None { get; } = new(false, null);
}

/// <summary>
/// OC-141. The decision core behind the checkpoint swap patch: which OC clone
/// NPCs resolve to which OC RoadCheckpoint while the native Activate/Resume
/// body reads the CheckpointManager. The roster's world writes the map at
/// bind and clears it on release, despawn, pre-load and dispose; the patch
/// only swaps the manager field for a mapped clone whose AssignedCheckpoint
/// is the fixed OC key, and always restores it. Native officers are never in
/// the map, so they pass through untouched. Every method fails open.
/// </summary>
internal static class OcCheckpointSwapState
{
    internal const NativeLocation CloneLocation = NativeLocation.Western;

    private static readonly object _gate = new();
    private static readonly Dictionary<long, object> _checkpoints = new();

    public static void MapClone(long npcPointer, object? ocCheckpoint)
    {
        if (npcPointer == 0 || ocCheckpoint is null)
            return;
        lock (_gate)
            _checkpoints[npcPointer] = ocCheckpoint;
    }

    public static void UnmapClone(long npcPointer)
    {
        lock (_gate)
            _checkpoints.Remove(npcPointer);
    }

    public static void Clear()
    {
        lock (_gate)
            _checkpoints.Clear();
    }

    public static bool TryGetCheckpoint(long npcPointer, out object? ocCheckpoint)
    {
        lock (_gate)
            return _checkpoints.TryGetValue(npcPointer, out ocCheckpoint);
    }

    public static OcCheckpointSwap BeginSwap(
        long npcPointer,
        NativeLocation assigned,
        Func<object?> readField,
        Action<object?> writeField)
    {
        if (assigned != CloneLocation)
            return OcCheckpointSwap.None;
        if (!TryGetCheckpoint(npcPointer, out var ocCheckpoint) || ocCheckpoint is null)
            return OcCheckpointSwap.None;
        try
        {
            var original = readField();
            writeField(ocCheckpoint);
            return new OcCheckpointSwap(Swapped: true, Original: original);
        }
        catch
        {
            return OcCheckpointSwap.None;
        }
    }

    public static bool EndSwap(OcCheckpointSwap swap, Action<object?> writeField)
    {
        if (swap is null || !swap.Swapped)
            return true;
        try
        {
            writeField(swap.Original);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
