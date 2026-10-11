using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PatrolRulesTests
{
    public static IEnumerable<object[]> Combinations()
    {
        foreach (var tier in Enum.GetValues<LocalPressureTier>())
        foreach (var covered in new[] { false, true })
        foreach (var lockdown in new[] { false, true })
        foreach (var minute in new[] { 0d, 1079.99d, 1080d, 1379.99d, 1380d, 1439d, 1440d, 2520d, 2820d })
            yield return new object[] { tier, covered, lockdown, minute };
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Profile_for_every_tier_protection_lockdown_and_window_edge(
        LocalPressureTier tier, bool covered, bool lockdown, double minute)
    {
        var result = Chapter3PatrolRules.Select(tier, covered, lockdown, minute);
        var inWindow = minute % 1440d >= 1080d && minute % 1440d < 1380d;
        var foot = covered ? 0 : lockdown ? 3 : inWindow && tier >= LocalPressureTier.Watched ? 1 : 0;
        var car = covered ? 0 : lockdown ? 1 : inWindow && tier == LocalPressureTier.Critical ? 1 : 0;
        Assert.Equal(foot, result.Foot);
        Assert.Equal(car, result.Vehicle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Unreadable_clock_does_not_create_duty(double? minute)
        => Assert.Equal(0, Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, true, minute).Total);

    [Fact]
    public void Lockdown_does_not_require_a_known_heat_tier()
        => Assert.Equal(4, Chapter3PatrolRules.Select(null, false, true, 0d).Total);

    [Fact]
    public void Larger_profile_replaces_instead_of_stacking_and_allows_D6_protected_input()
    {
        var sweep = new Chapter3PatrolProfile("sweep", 1, 1);
        Assert.Equal(4, Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, true, 1080d, sweep).Total);
        Assert.Equal(sweep, Chapter3PatrolRules.Select(LocalPressureTier.Watched, false, false, 1080d, sweep));
        var protectedSweep = new Chapter3PatrolProfile("warned sweep", 1, 0);
        Assert.Equal(protectedSweep, Chapter3PatrolRules.Select(LocalPressureTier.Critical, true, false, 0d, protectedSweep));
        Assert.Equal(2, Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, false, 1080d, sweep).Total);
    }
}
