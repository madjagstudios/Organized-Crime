using Xunit;

namespace OrganizedCrime.Tests;

// SyndicateHqDoorObserver touches real UnityEngine/IL2CPP objects with no live process here (see
// SyndicateHqUnityNullOverloadTests.cs), so this is a source-scan guard, not a behavioral test.
public sealed class SyndicateHqDoorObserverTests
{
    private static string Source => File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqDoorObserver.cs"));

    [Fact]
    public void The_observer_declares_a_settable_timing_receipts_property_defaulting_to_disabled()
    {
        Assert.Contains(
            "public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_observer_retains_a_resolved_identity_across_detach_and_only_rescans_when_it_reads_destroyed()
    {
        var source = Source;

        // Exactly one call site does the expensive full-scene scan; TryAttach must not call it directly.
        Assert.Equal(1, Count(source, "TryResolveExact(out"));
        var attachStart = source.IndexOf("public bool TryAttach()", StringComparison.Ordinal);
        var attachEnd = source.IndexOf("public bool Detach()", attachStart, StringComparison.Ordinal);
        Assert.True(attachStart >= 0 && attachEnd > attachStart, "TryAttach could not be sliced.");
        Assert.DoesNotContain("TryResolveExact(", source[attachStart..attachEnd], StringComparison.Ordinal);
        Assert.Contains("Timing.Measure(\"hq/door-resolve\"", source, StringComparison.Ordinal);

        // Detach clears only the subscription-facing _resolution field, never the retained cache.
        var detachStart = source.IndexOf("public bool Detach()", StringComparison.Ordinal);
        var detachEnd = source.IndexOf("public bool TryDispose()", detachStart, StringComparison.Ordinal);
        Assert.True(detachStart >= 0 && detachEnd > detachStart, "Detach could not be sliced.");
        Assert.DoesNotContain("_retainedResolution", source[detachStart..detachEnd], StringComparison.Ordinal);

        Assert.Contains("_retainedResolution", source, StringComparison.Ordinal);
        // Liveness uses the UnityEngine.Object overload, not reference equality.
        Assert.Contains("IsUnityNull(_retainedResolution.InteractableObject)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Evict_retained_for_load_clears_the_retained_resolution_so_the_next_resolve_rescans()
    {
        // Final review fix (finding 5): the load-boundary eviction hook must clear exactly
        // _retainedResolution, the same field TryResolveRetainedOrScan checks before falling through
        // to the identity-checked TryResolveExact scan.
        Assert.Contains(
            "public void EvictRetainedForLoad() => _retainedResolution = null;",
            Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_resolve_fresh_evicts_the_retained_identity_before_resolving_so_it_never_returns_a_cache_hit()
    {
        // Final review fix (finding 2): TryResolveFresh's only caller
        // (SyndicateHqRuntimeService.TryReturnForTeardown, taken only when a teardown-time return has
        // already failed once, i.e. exactly when the world may have changed under the mod) plans a
        // player teleport from the result, so a cached-but-superseded access point must never come
        // back. TryResolveFresh must evict _retainedResolution before it calls
        // TryResolveRetainedOrScan, forcing that call through TryResolveExact's full scene scan and
        // identity check every single time, not just on the first call after a load.
        var source = Source;
        var start = source.IndexOf("public bool TryResolveFresh(", StringComparison.Ordinal);
        var end = source.IndexOf("public bool TryAttach()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "TryResolveFresh could not be sliced.");
        var body = source[start..end];

        var evictIndex = body.IndexOf("EvictRetainedForLoad();", StringComparison.Ordinal);
        var resolveIndex = body.IndexOf("TryResolveRetainedOrScan(", StringComparison.Ordinal);
        Assert.True(evictIndex >= 0 && resolveIndex > evictIndex,
            "TryResolveFresh must evict the retained identity before resolving so it can never return a cached hit.");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
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
