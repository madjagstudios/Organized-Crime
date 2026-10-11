namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: the player-side facts that gate Arthur's Chapter 3 visits.
/// All plain data so this file stays test linkable; the S1API reads
/// live in <c>S1ApiChapter3ArthurVisitConditions</c> and the HQ pocket
/// check stays with the visit engine, which already owns that presence.
/// </summary>
public sealed record Chapter3ArthurVisitConditions(
    bool OnFoot,
    bool Outdoors,
    bool ClearOfPursuit,
    string Detail)
{
    public bool Met => OnFoot && Outdoors && ClearOfPursuit;

    public static Chapter3ArthurVisitConditions Clear(string detail = "on foot, outdoors, clear of pursuit") =>
        new(true, true, true, detail);

    public static Chapter3ArthurVisitConditions Blocked(bool onFoot, bool outdoors, bool clearOfPursuit, string detail) =>
        new(onFoot, outdoors, clearOfPursuit, detail);
}

/// <summary>
/// OC-134: reads the visit conditions. Total: false plus a reason when
/// the player or the clock cannot be read, so a broken read fails
/// closed and the engine starts nothing.
/// </summary>
public interface IChapter3ArthurVisitConditions
{
    bool TryRead(out Chapter3ArthurVisitConditions conditions);
}
