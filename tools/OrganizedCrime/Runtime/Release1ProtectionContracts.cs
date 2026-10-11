using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-118. The one native mutation the protection retainer performs itself: switching every road
/// checkpoint off the moment cover starts. Best effort by contract: a false return retries on the
/// next pass, never holds it, since the money has already moved and the cover is owed regardless.
/// The schedule's own re-enable path is gated separately, by a Harmony prefix that consults the
/// protection gate state, so this seam stays a plain world call with a fake in tests.
/// </summary>
public interface IRelease1ProtectionCheckpointWorld
{
    /// <summary>Disables every road checkpoint. Returns false when the call did not go through.</summary>
    bool TryDisableAllCheckpoints();

    /// <summary>
    /// Reads which checkpoints are enabled now as a <see cref="Release1ProtectionCheckpoints"/>
    /// bitmask. Returns false (mask zero) when the read did not go through.
    /// </summary>
    bool TryReadEnabled(out int mask);

    /// <summary>
    /// Enables exactly the masked checkpoints. Best effort: the service filters the mask against
    /// a fresh read first, so this only ever receives locations that read off. Returns false when
    /// the call did not go through.
    /// </summary>
    bool TryRestoreEnabled(int mask);
}

/// <summary>
/// OC-118. The checkpoint bitmask order: bit i is S1API CheckpointLocation i (Western 0, Docks 1,
/// NorthResidential 2, WestResidential 3), the same order the native ECheckpointLocation declares
/// them in. The service stores the cover-start snapshot in this order; the adapter maps it back
/// explicitly, never by blind cast.
/// </summary>
public static class Release1ProtectionCheckpoints
{
    public const int WesternBit = 1 << 0;
    public const int DocksBit = 1 << 1;
    public const int NorthResidentialBit = 1 << 2;
    public const int WestResidentialBit = 1 << 3;
    public const int MaskAll = WesternBit | DocksBit | NorthResidentialBit | WestResidentialBit;
}

/// <summary>
/// What the oc_ch3 protection commands drive. Implemented by
/// <see cref="Release1ChiefService"/>; faked in tests.
/// </summary>
public interface IRelease1ChiefProtectionQa
{
    Chapter3QaResult QaProtectionOffer();
    Chapter3QaResult QaProtectionLapse();
    Release1ChiefProtectionSnapshot QaProtectionSnapshot();
}
