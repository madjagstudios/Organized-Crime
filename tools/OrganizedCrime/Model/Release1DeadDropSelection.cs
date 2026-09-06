using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

/// <summary>
/// The one deterministic drop rule shared by every Release 1 mission that needs a vanilla dead drop
/// chosen from the acceptance correlation: keep the empty drops, sort by GUID ordinally, hash the
/// stage acceptance correlation with SHA-256, take the first eight digest bytes as an unsigned big
/// endian integer, and choose at hash mod count. Missions that need two drops take the handoff at
/// (hash shifted right by eight) mod (count minus one) over the remainder. Pure and free of any
/// process randomized hashing, so the same correlation always chooses the same drop or pair.
/// </summary>
public static class Release1DeadDropSelection
{
    public static bool TryChooseSingle(
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        string? authorizationCorrelationId,
        out Release1SmallCourtesyDropCandidate? chosen)
    {
        chosen = null;
        var empty = EmptyOrdered(drops);
        if (empty.Length < 1) return false;
        chosen = empty[SourceIndex(empty.Length, Hash(authorizationCorrelationId))];
        return true;
    }

    public static bool TryChoosePair(
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops,
        string? authorizationCorrelationId,
        out Release1SmallCourtesyDropCandidate? source,
        out Release1SmallCourtesyDropCandidate? handoff)
    {
        source = null;
        handoff = null;
        var empty = EmptyOrdered(drops);
        if (empty.Length < 2) return false;

        var hash = Hash(authorizationCorrelationId);
        var sourceIndex = SourceIndex(empty.Length, hash);
        source = empty[sourceIndex];
        var remainder = empty.Where((_, index) => index != sourceIndex).ToArray();
        handoff = remainder[(int)((hash >> 8) % (ulong)remainder.Length)];
        return true;
    }

    private static Release1SmallCourtesyDropCandidate[] EmptyOrdered(
        IReadOnlyList<Release1SmallCourtesyDropCandidate>? drops) =>
        drops is null
            ? Array.Empty<Release1SmallCourtesyDropCandidate>()
            : drops
                .Where(candidate => candidate is not null && candidate.IsEmpty)
                .OrderBy(candidate => candidate.Guid, StringComparer.Ordinal)
                .ToArray();

    private static ulong Hash(string? authorizationCorrelationId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationCorrelationId ?? string.Empty));
        return BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, sizeof(ulong)));
    }

    private static int SourceIndex(int count, ulong hash) => (int)(hash % (ulong)count);
}
