using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-149: gap pins for the lockdown re-assert log throttle. First-seen,
// repeat-silence, next-key and Reset re-arm are already pinned by
// Release1LockdownGateThrottleTests; this file pins key change-back, the
// production -1 unknown-clock sentinel, and that Reset happens exactly on
// engage, release and teardown (never on save or load).
public sealed class LockdownGateThrottleCharacterizationTests
{
    [Fact]
    public void Returning_to_an_earlier_minute_key_passes_again()
    {
        var throttle = new Release1LockdownGateThrottle();

        Assert.True(throttle.TryMark(2100));
        Assert.True(throttle.TryMark(2101));
        Assert.True(throttle.TryMark(2100));
        Assert.False(throttle.TryMark(2100));
    }

    [Fact]
    public void The_unknown_clock_sentinel_is_an_ordinary_key()
    {
        var throttle = new Release1LockdownGateThrottle();

        Assert.True(throttle.TryMark(-1));
        Assert.False(throttle.TryMark(-1));
        Assert.True(throttle.TryMark(2100));
        Assert.True(throttle.TryMark(-1));
    }

    [Fact]
    public void The_throttle_resets_exactly_on_engage_release_and_teardown()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1LockdownGatePatch.cs"));

        Assert.Equal(3, Count(text, "Throttle.Reset()"));
        Assert.Equal(1, Count(Slice(text, "public static Release1StagingHarnessResult TryEngage(", "public static Release1StagingHarnessResult TryRelease("), "Throttle.Reset()"));
        Assert.Equal(1, Count(Slice(text, "public static Release1StagingHarnessResult TryRelease(", "public static void Reset()"), "Throttle.Reset()"));
        Assert.Equal(1, Count(Slice(text, "public static void Reset()", "private static bool EnsurePatched("), "Throttle.Reset()"));
    }

    private static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"slice {from}..{to} not found");
        return text.Substring(start, end - start);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
