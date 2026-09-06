using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-63 Part 2: the timing seam. Pins the threshold gating and the one receipt line shape the
/// owner's log will show, so a normal play session stays quiet and a slow phase is unmistakable.
/// </summary>
public sealed class OrganizedCrimeTimingReceiptTests
{
    [Fact]
    public void The_default_threshold_is_fifty_milliseconds()
    {
        Assert.Equal(50, OrganizedCrimeTimingReceipts.DefaultThresholdMs);
    }

    [Fact]
    public void A_receipt_line_carries_the_prefix_the_phase_and_whole_milliseconds()
    {
        Assert.Equal(
            "[Organized Crime] timing: save-complete/hq 1234 ms",
            OrganizedCrimeTimingReceipts.FormatReceipt("save-complete/hq", 1_234d));
    }

    [Fact]
    public void A_fractional_elapsed_time_is_rounded_to_whole_milliseconds()
    {
        Assert.Equal(
            "[Organized Crime] timing: census/keep-the-lights-off 87 ms",
            OrganizedCrimeTimingReceipts.FormatReceipt("census/keep-the-lights-off", 86.5d));
    }

    [Fact]
    public void A_phase_below_the_threshold_reports_nothing()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 50);

        receipts.Report("save-start", 49.4d);

        Assert.Empty(lines);
    }

    [Fact]
    public void A_phase_at_or_above_the_threshold_reports_exactly_one_line()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 50);

        receipts.Report("save-start", 50d);
        receipts.Report("load-complete", 900d);

        Assert.Equal(
            new[]
            {
                "[Organized Crime] timing: save-start 50 ms",
                "[Organized Crime] timing: load-complete 900 ms"
            },
            lines);
    }

    [Fact]
    public void A_zero_threshold_disables_the_seam_entirely()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 0);

        Assert.False(receipts.IsEnabled);
        receipts.Report("save-start", 10_000d);

        Assert.Empty(lines);
    }

    [Fact]
    public void A_negative_threshold_disables_the_seam_the_same_way_zero_does()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, -1);

        Assert.False(receipts.IsEnabled);
        receipts.Report("save-start", 10_000d);

        Assert.Empty(lines);
    }

    [Fact]
    public void A_seam_with_no_sink_is_disabled()
    {
        Assert.False(OrganizedCrimeTimingReceipts.Disabled.IsEnabled);
        Assert.False(new OrganizedCrimeTimingReceipts(null, 50).IsEnabled);
    }

    [Fact]
    public void A_blank_phase_name_reports_nothing()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 1);

        receipts.Report(string.Empty, 500d);
        receipts.Report("   ", 500d);

        Assert.Empty(lines);
    }

    [Fact]
    public void A_non_finite_elapsed_time_reports_nothing()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 1);

        receipts.Report("save-start", double.NaN);
        receipts.Report("save-start", double.PositiveInfinity);

        Assert.Empty(lines);
    }

    [Fact]
    public void The_disabled_seam_still_runs_the_work_exactly_once()
    {
        var passes = 0;
        OrganizedCrimeTimingReceipts.Disabled.Measure("save-start", () => passes++);
        var value = OrganizedCrimeTimingReceipts.Disabled.Measure("save-start", () => ++passes);

        Assert.Equal(2, passes);
        Assert.Equal(2, value);
    }

    [Fact]
    public void An_enabled_seam_runs_the_work_exactly_once_and_returns_its_value()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 50);
        var passes = 0;

        var value = receipts.Measure("save-start", () => { passes++; return 7; });

        Assert.Equal(1, passes);
        Assert.Equal(7, value);
    }

    [Fact]
    public void A_fast_measured_phase_stays_quiet()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 50_000);

        receipts.Measure("save-start", () => { });

        Assert.Empty(lines);
    }

    [Fact]
    public void A_measured_phase_that_throws_still_propagates_the_exception()
    {
        var lines = new List<string>();
        var receipts = new OrganizedCrimeTimingReceipts(lines.Add, 50);

        Assert.Throws<InvalidOperationException>(() =>
            receipts.Measure("save-start", () => throw new InvalidOperationException("boom")));
        Assert.Throws<InvalidOperationException>(() =>
            receipts.Measure<int>("save-start", () => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public void A_throwing_sink_never_fails_the_work_it_measured()
    {
        var receipts = new OrganizedCrimeTimingReceipts(_ => throw new InvalidOperationException("sink"), 1);
        var passes = 0;

        var exception = Record.Exception(() => receipts.Measure("save-start", () => passes++));

        Assert.Null(exception);
        Assert.Equal(1, passes);
    }

    [Fact]
    public void Measuring_null_work_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => OrganizedCrimeTimingReceipts.Disabled.Measure("save-start", (Action)null!));
        Assert.Throws<ArgumentNullException>(() => OrganizedCrimeTimingReceipts.Disabled.Measure("save-start", (Func<int>)null!));
    }
}
