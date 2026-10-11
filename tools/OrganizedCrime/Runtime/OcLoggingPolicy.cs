using System.Collections.Concurrent;

namespace OrganizedCrime.Runtime;

// OC-84: only explicit diagnostic calls are gated. Ordinary information and
// warnings stay visible; Melon owns the mod prefix.
internal sealed class OcLoggingPolicy
{
    private const string Prefix = "[Organized Crime]";
    private readonly Action<string> _information;
    private readonly Action<string> _warning;
    private readonly ConcurrentDictionary<string, int> _warningCounts = new(StringComparer.Ordinal);

    internal OcLoggingPolicy(Action<string> information, Action<string> warning)
    {
        _information = information ?? throw new ArgumentNullException(nameof(information));
        _warning = warning ?? throw new ArgumentNullException(nameof(warning));
    }

    internal bool DiagnosticsEnabled { get; set; }

    internal static string WithoutPrefix(string message)
    {
        while (message.StartsWith(Prefix, StringComparison.Ordinal))
            message = message[Prefix.Length..].TrimStart();
        return message;
    }

    internal void Startup(string version) =>
        _information($"v{version} ready; diagnostics={(DiagnosticsEnabled ? "on" : "off")} (OwnerQaKeys).");

    // A command's output is its response, even when it looks like a routine receipt.
    internal void CommandResponse(string message) => _information(WithoutPrefix(message));

    internal void Information(string message) => Write(message, warning: false);
    internal void Warning(string message) => Write(message, warning: true);

    internal void Diagnostic(string message)
    {
        if (DiagnosticsEnabled) _information(WithoutPrefix(message));
    }

    private void Write(string message, bool warning)
    {
        message = WithoutPrefix(message);
        if (warning || IsFailure(message)) WriteWarning(message);
        else _information(message);
    }

    private void WriteWarning(string message)
    {
        // This exact family already has a keyed OnceLog at the producer. Normal
        // mode also bounds identical re-emissions across loads; owner detail stays
        // complete. Do not coalesce other warnings or different failure messages.
        if (!DiagnosticsEnabled && message.StartsWith("Chapter 3 raid entrance unavailable for ", StringComparison.Ordinal) &&
            message.EndsWith("; occupancy blocked, retrying every 30 seconds.", StringComparison.Ordinal))
        {
            var count = _warningCounts.AddOrUpdate(message, 1, (_, previous) => previous + 1);
            if (count == 1) _warning(message);
            else if (count % 10 == 0) Summary(message, count == 10 ? 9 : 10);
            return;
        }
        _warning(message);
    }

    internal void FlushWarningSummaries()
    {
        foreach (var pair in _warningCounts)
        {
            if (!_warningCounts.TryRemove(pair.Key, out var count)) continue;
            var remaining = count < 10 ? count - 1 : count % 10;
            if (remaining > 0) Summary(pair.Key, remaining);
        }
    }

    private void Summary(string message, int repeated) =>
        _warning($"Warning repeated {repeated} times: {message}");

    private static bool IsFailure(string message) => FailureTerms.Any(term =>
        message.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FailureTerms =
    {
        "skipped", "unreadable", "quarantin", "blocked", "partial", "exception", "error", "invalid", "missing",
        "failed", "faulted", "threw", "could not", "refused", "rejected", "unavailable",
        "incomplete", "not fully cleaned", "remains partial", "did not pass",
        "timed out", "warning:", "ClockRewound", "was not written", "was not loaded",
        "was not primed", "was not persisted", "template fault"
    };
}
