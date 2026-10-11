using System.Threading;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-135: the Arthur side of the native damage pipeline. The Harmony
/// patch scopes every prefix through <see cref="ShouldSuppress"/>, and
/// records each suppressed impact here so the visit pump can tell an
/// attack from silence: Arthur takes no damage, so the health-drop
/// polling alone can no longer see a punch.
/// </summary>
public static class Chapter3ArthurResponseFilter
{
    private static int _impactFlag;

    /// <summary>
    /// True only for Arthur's own NPC id. f7 keeps the id in the
    /// framework data (<c>NPCData.BasicInfo.ID</c>), which is what
    /// S1API's <c>WithIdentity</c> writes and what native
    /// <c>NPC.get_ID</c> reads back, so the native id matches the
    /// shipping id exactly.
    /// </summary>
    public static bool ShouldSuppress(string? npcId) =>
        string.Equals(npcId, Chapter3ArthurIds.NpcId, StringComparison.Ordinal);

    /// <summary>
    /// Records one suppressed native impact on Arthur. Called from the
    /// patch prefixes, which run on the game thread.
    /// </summary>
    public static void NoteImpact() => Interlocked.Exchange(ref _impactFlag, 1);

    /// <summary>
    /// Drains one recorded impact: true once per recorded hit, false
    /// when nothing was recorded since the last poll.
    /// </summary>
    public static bool PollImpact() => Interlocked.Exchange(ref _impactFlag, 0) == 1;
}
