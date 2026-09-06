using System.Diagnostics;

namespace OrganizedCrime.Runtime;

/// <summary>
/// A cheap, side effect free timing seam for the mod's lifecycle boundaries and its per frame
/// convergence passes. Each measured phase reports one receipt line at the existing Msg level in
/// the form <c>[Organized Crime] timing: &lt;phase&gt; &lt;ms&gt; ms</c>, and only when the phase
/// met or exceeded the configured threshold, so a normal play session stays quiet.
///
/// The seam is pure: it owns no MelonLoader type, it never touches world or native state, and it
/// never changes the outcome of the work it wraps. A sink that throws is isolated so a diagnostic
/// receipt can never fail the mod work it was measuring. When the seam is disabled (no sink, or a
/// threshold of zero or less) the wrappers run the work directly and never allocate a stopwatch.
/// </summary>
public sealed class OrganizedCrimeTimingReceipts
{
    /// <summary>The threshold the owner preference defaults to, in milliseconds.</summary>
    public const int DefaultThresholdMs = 50;

    /// <summary>The fixed prefix every receipt line carries.</summary>
    public const string ReceiptPrefix = "[Organized Crime] timing: ";

    /// <summary>A seam that measures nothing and logs nothing.</summary>
    public static readonly OrganizedCrimeTimingReceipts Disabled = new(null, 0);

    private readonly Action<string>? _sink;
    private readonly int _thresholdMs;

    public OrganizedCrimeTimingReceipts(Action<string>? sink, int thresholdMs = DefaultThresholdMs)
    {
        _sink = sink;
        _thresholdMs = thresholdMs;
    }

    /// <summary>The configured threshold in milliseconds; zero or less disables the seam.</summary>
    public int ThresholdMs => _thresholdMs;

    /// <summary>True when a sink is present and the threshold is positive.</summary>
    public bool IsEnabled => _sink is not null && _thresholdMs > 0;

    /// <summary>
    /// Runs <paramref name="work"/> and reports one receipt when it met or exceeded the threshold.
    /// The work runs exactly once whether or not the seam is enabled, and an exception it throws
    /// still reports the elapsed time before it propagates.
    /// </summary>
    public void Measure(string phase, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!IsEnabled) { work(); return; }
        var stopwatch = Stopwatch.StartNew();
        try { work(); }
        finally { stopwatch.Stop(); Report(phase, stopwatch.Elapsed.TotalMilliseconds); }
    }

    /// <summary>The value returning form of <see cref="Measure(string, Action)"/>.</summary>
    public T Measure<T>(string phase, Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!IsEnabled) return work();
        var stopwatch = Stopwatch.StartNew();
        try { return work(); }
        finally { stopwatch.Stop(); Report(phase, stopwatch.Elapsed.TotalMilliseconds); }
    }

    /// <summary>
    /// Reports an already measured elapsed time. Nothing is written when the seam is disabled,
    /// when the phase name is blank, when the elapsed time is not a finite number, or when it fell
    /// below the threshold.
    /// </summary>
    public void Report(string phase, double elapsedMs)
    {
        if (!IsEnabled) return;
        if (string.IsNullOrWhiteSpace(phase)) return;
        if (!double.IsFinite(elapsedMs) || elapsedMs < _thresholdMs) return;
        try { _sink!(FormatReceipt(phase, elapsedMs)); }
        catch
        {
            // A diagnostic receipt never fails the work it measured.
        }
    }

    /// <summary>The one canonical receipt line shape, rounded to whole milliseconds.</summary>
    public static string FormatReceipt(string phase, double elapsedMs) =>
        $"{ReceiptPrefix}{phase} {(long)Math.Round(elapsedMs, MidpointRounding.AwayFromZero)} ms";
}
