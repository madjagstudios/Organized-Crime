using OrganizedCrime.Core.Diagnostics;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-147: the shared warn-once set behind every ReportHoldOnce/WarnOnce/LogOnce copy.
public sealed class OnceLogTests
{
    [Fact]
    public void Report_first_time_logs_and_returns_true()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);

        Assert.True(log.Report("a", "first"));
        Assert.Equal(new[] { "first" }, lines);
    }

    [Fact]
    public void Report_repeat_is_silent_and_returns_false()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);

        Assert.True(log.Report("a", "first"));
        Assert.False(log.Report("a", "second"));
        Assert.Equal(new[] { "first" }, lines);
    }

    [Fact]
    public void Distinct_keys_log_independently()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);

        Assert.True(log.Report("a", "for-a"));
        Assert.True(log.Report("b", "for-b"));
        Assert.Equal(new[] { "for-a", "for-b" }, lines);
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);

        Assert.True(log.Report("A", "upper"));
        Assert.True(log.Report("a", "lower"));
        Assert.Equal(new[] { "upper", "lower" }, lines);
    }

    [Fact]
    public void Clear_rearms_the_key()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);

        Assert.True(log.Report("a", "first"));
        Assert.False(log.Report("a", "suppressed"));
        Assert.True(log.Clear("a"));
        Assert.True(log.Report("a", "again"));
        Assert.Equal(new[] { "first", "again" }, lines);
    }

    [Fact]
    public void Clear_missing_key_returns_false()
    {
        var log = new OnceLog(_ => { });

        Assert.False(log.Clear("missing"));
    }

    [Fact]
    public void ClearWhere_removes_only_matching_keys_and_returns_the_count()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);
        Assert.True(log.Report("1:unreadable", "job 1 unreadable"));
        Assert.True(log.Report("1:staging", "job 1 staging"));
        Assert.True(log.Report("2:unreadable", "job 2 unreadable"));

        var removed = log.ClearWhere(key => !key.StartsWith("2:", StringComparison.Ordinal));

        Assert.Equal(2, removed);
        Assert.False(log.Contains("1:unreadable"));
        Assert.False(log.Contains("1:staging"));
        Assert.True(log.Contains("2:unreadable"));
        Assert.True(log.Report("1:unreadable", "job 1 unreadable again"));
        Assert.False(log.Report("2:unreadable", "still suppressed"));
    }

    [Fact]
    public void ClearAll_rearms_every_key()
    {
        var lines = new List<string>();
        var log = new OnceLog(lines.Add);
        Assert.True(log.Report("a", "for-a"));
        Assert.True(log.Report("b", "for-b"));

        log.ClearAll();

        Assert.False(log.Contains("a"));
        Assert.True(log.Report("a", "for-a again"));
        Assert.True(log.Report("b", "for-b again"));
    }

    [Fact]
    public void Contains_tracks_reported_keys()
    {
        var log = new OnceLog(_ => { });

        Assert.False(log.Contains("a"));
        Assert.True(log.Report("a", "line"));
        Assert.True(log.Contains("a"));
    }

    [Fact]
    public void Null_sink_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OnceLog(null!));
    }
}
