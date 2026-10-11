namespace OrganizedCrime.Core.Time;

/// <summary>
/// Canonical game-clock maths: total minutes from the engine's elapsed-days
/// and minute-of-day reads, and back. Pure arithmetic; every engine read
/// stays in its adapter.
/// </summary>
public static class GameClock
{
    public const int MinutesPerDay = 1440;
    public const int MinutesPerWeek = 10080;

    /// <summary>Canonical total minutes: elapsed days times 1440 plus minute of day.</summary>
    public static double TotalMinutes(int elapsedDays, int minuteOfDay) =>
        (double)elapsedDays * MinutesPerDay + minuteOfDay;

    /// <summary>Whole days below the canonical minute count.</summary>
    public static int Day(double totalMinutes) =>
        (int)Math.Floor(totalMinutes / MinutesPerDay);

    /// <summary>Minutes past midnight within the canonical minute count.</summary>
    public static int MinuteOfDay(double totalMinutes) =>
        (int)(totalMinutes % MinutesPerDay);

    /// <summary>Whole seven-day blocks below the canonical minute count.</summary>
    public static int Week(double totalMinutes) =>
        (int)Math.Floor(totalMinutes / MinutesPerWeek);
}
