using S1API.Law;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-118. The production protection checkpoint world: the S1API checkpoint surface with
/// verification reads. Every S1API call here is null-safe and void, so the booleans this seam
/// returns come from reading back through <c>IsCheckpointEnabled</c>: the disable verifies all
/// four read off, the restore verifies every masked location reads on. Reads map each S1API
/// CheckpointLocation to its <see cref="Release1ProtectionCheckpoints"/> bit explicitly. Any
/// throw reads as failure with one log line; behavior without a game is untestable, so the
/// companion interop test pins the S1API member shapes instead.
/// </summary>
public sealed class S1ApiRelease1ProtectionCheckpointWorld : IRelease1ProtectionCheckpointWorld
{
    private readonly Action<string> _log;

    public S1ApiRelease1ProtectionCheckpointWorld(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
    }

    public bool TryDisableAllCheckpoints()
    {
        try
        {
            CheckpointManager.DisableAllCheckpoints();
            return ReadMask() == 0;
        }
        catch (Exception exception)
        {
            _log($"Chief protection checkpoint disable threw: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    public bool TryReadEnabled(out int mask)
    {
        mask = 0;
        try
        {
            mask = ReadMask();
            return true;
        }
        catch (Exception exception)
        {
            _log($"Chief protection checkpoint read threw: {exception.GetType().Name}: {exception.Message}");
            mask = 0;
            return false;
        }
    }

    public bool TryRestoreEnabled(int mask)
    {
        try
        {
            mask &= Release1ProtectionCheckpoints.MaskAll;
            EnableIfOff(CheckpointLocation.Western, (mask & Release1ProtectionCheckpoints.WesternBit) != 0);
            EnableIfOff(CheckpointLocation.Docks, (mask & Release1ProtectionCheckpoints.DocksBit) != 0);
            EnableIfOff(CheckpointLocation.NorthResidential, (mask & Release1ProtectionCheckpoints.NorthResidentialBit) != 0);
            EnableIfOff(CheckpointLocation.WestResidential, (mask & Release1ProtectionCheckpoints.WestResidentialBit) != 0);
            return (ReadMask() & mask) == mask;
        }
        catch (Exception exception)
        {
            _log($"Chief protection checkpoint restore threw: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static int ReadMask()
    {
        var mask = 0;
        if (CheckpointManager.IsCheckpointEnabled(CheckpointLocation.Western)) mask |= Release1ProtectionCheckpoints.WesternBit;
        if (CheckpointManager.IsCheckpointEnabled(CheckpointLocation.Docks)) mask |= Release1ProtectionCheckpoints.DocksBit;
        if (CheckpointManager.IsCheckpointEnabled(CheckpointLocation.NorthResidential)) mask |= Release1ProtectionCheckpoints.NorthResidentialBit;
        if (CheckpointManager.IsCheckpointEnabled(CheckpointLocation.WestResidential)) mask |= Release1ProtectionCheckpoints.WestResidentialBit;
        return mask;
    }

    // Enables only what reads off: enabling an already-enabled checkpoint would pull another
    // officer from the station for nothing. The S1API default officer count applies.
    private static void EnableIfOff(CheckpointLocation location, bool wanted)
    {
        if (wanted && !CheckpointManager.IsCheckpointEnabled(location))
            CheckpointManager.SetCheckpointEnabled(location, enabled: true);
    }
}
