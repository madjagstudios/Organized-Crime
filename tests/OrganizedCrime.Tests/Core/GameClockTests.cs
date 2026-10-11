using OrganizedCrime.Core.Time;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-147: the canonical game-clock maths behind the ElapsedDays*1440 adapters.
public sealed class GameClockTests
{
    [Fact]
    public void Day_and_week_lengths()
    {
        Assert.Equal(1440, GameClock.MinutesPerDay);
        Assert.Equal(10080, GameClock.MinutesPerWeek);
    }

    [Theory]
    [InlineData(0, 0, 0d)]
    [InlineData(0, 1439, 1439d)]
    [InlineData(1, 0, 1440d)]
    [InlineData(5, 360, 7560d)]
    [InlineData(11, 720, 16560d)]
    public void TotalMinutes_combines_days_and_minute_of_day(int elapsedDays, int minuteOfDay, double expected) =>
        Assert.Equal(expected, GameClock.TotalMinutes(elapsedDays, minuteOfDay));

    [Theory]
    [InlineData(0d, 0)]
    [InlineData(1439.9d, 0)]
    [InlineData(1440d, 1)]
    [InlineData(7560d, 5)]
    [InlineData(-0.5d, -1)]
    public void Day_floors_total_minutes(double totalMinutes, int expected) =>
        Assert.Equal(expected, GameClock.Day(totalMinutes));

    [Theory]
    [InlineData(0d, 0)]
    [InlineData(1439.9d, 1439)]
    [InlineData(1440d, 0)]
    [InlineData(7560d, 360)]
    [InlineData(-1d, -1)]
    public void MinuteOfDay_is_the_remainder(double totalMinutes, int expected) =>
        Assert.Equal(expected, GameClock.MinuteOfDay(totalMinutes));

    [Theory]
    [InlineData(0d, 0)]
    [InlineData(10079.9d, 0)]
    [InlineData(10080d, 1)]
    [InlineData(-0.5d, -1)]
    public void Week_floors_total_minutes(double totalMinutes, int expected) =>
        Assert.Equal(expected, GameClock.Week(totalMinutes));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 360)]
    [InlineData(11, 1439)]
    public void Day_and_minute_round_trip_through_total_minutes(int elapsedDays, int minuteOfDay)
    {
        var total = GameClock.TotalMinutes(elapsedDays, minuteOfDay);

        Assert.Equal(elapsedDays, GameClock.Day(total));
        Assert.Equal(minuteOfDay, GameClock.MinuteOfDay(total));
    }

    [Theory]
    [InlineData(6, 1439, 0)]
    [InlineData(7, 0, 1)]
    [InlineData(13, 1439, 1)]
    public void Week_counts_seven_day_blocks(int elapsedDays, int minuteOfDay, int expected) =>
        Assert.Equal(expected, GameClock.Week(GameClock.TotalMinutes(elapsedDays, minuteOfDay)));
}
