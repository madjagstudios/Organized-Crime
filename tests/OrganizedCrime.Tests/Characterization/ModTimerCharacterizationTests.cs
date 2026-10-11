using Xunit;

namespace OrganizedCrime.Tests;

// OC-149: the 1 Hz timers cannot run in tests against the engine, so their
// behaviour was pinned through Mod.cs source text. OC-151 moved them verbatim
// into LifecycleFanout behind the engine DeltaTime/IsHost seam, so the shape
// pins below read the fanout while LifecycleFanoutTests proves the behaviour
// (1 Hz fire, overshoot discard, host gating) by running Update.
public sealed class ModTimerCharacterizationTests
{
    [Fact]
    public void Emergency_timer_accumulates_every_frame_and_fires_at_one_second()
    {
        var fanout = ReadFanoutCs();
        var update = Slice(fanout, "public void Update()", "public void OnGUI()");
        var block = Slice(update, "_chapter3EmergencyPollSeconds += _engine.DeltaTime();", "slots.ChiefUpdate?.Invoke();");

        Assert.Contains("_chapter3EmergencyPollSeconds += _engine.DeltaTime();", block, StringComparison.Ordinal);
        Assert.Contains("_chapter3EmergencyPollSeconds >= 1f", block, StringComparison.Ordinal);
        Assert.Contains("_chapter3EmergencyPollSeconds = 0f;", block, StringComparison.Ordinal);
        Assert.Contains("slots.EmergencyPoll?.Invoke();", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Demand_timer_accumulates_only_on_the_host_and_fires_at_one_second()
    {
        var fanout = ReadFanoutCs();
        var update = Slice(fanout, "public void Update()", "public void OnGUI()");
        var block = Slice(update, "_chapter3DemandPollSeconds += _engine.DeltaTime();", "slots.RosterUpdate?.Invoke();");

        Assert.Contains("_chapter3DemandPollSeconds += _engine.DeltaTime();", block, StringComparison.Ordinal);
        Assert.Contains("_chapter3DemandPollSeconds >= 1f", block, StringComparison.Ordinal);
        Assert.Contains("_chapter3DemandPollSeconds = 0f;", block, StringComparison.Ordinal);
        Assert.Contains("slots.DemandPoll?.Invoke();", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Emergency_timer_runs_ungated_while_demand_runs_inside_the_host_check()
    {
        var fanout = ReadFanoutCs();
        var update = Slice(fanout, "public void Update()", "public void OnGUI()");

        var beforeHost = Slice(update, "public void Update()", "if (_engine.IsHost())");
        Assert.Contains("_chapter3EmergencyPollSeconds += _engine.DeltaTime();", beforeHost, StringComparison.Ordinal);
        Assert.DoesNotContain("_chapter3DemandPollSeconds", beforeHost, StringComparison.Ordinal);

        var hostOnly = Slice(update, "if (_engine.IsHost())", "slots.EnsureUi?.Invoke();");
        Assert.Contains("_chapter3DemandPollSeconds += _engine.DeltaTime();", hostOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("_chapter3EmergencyPollSeconds", hostOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Neither_timer_is_reset_on_save_or_load()
    {
        var fanout = ReadFanoutCs();

        foreach (var (from, to) in new[]
                 {
                     ("public void HandleSaveComplete()", "public void HandleSaveStart()"),
                     ("public void HandleSaveStart()", "public void HandlePreLoad()"),
                     ("public void HandlePreLoad()", "public void HandleLoadComplete()"),
                     ("public void HandleLoadComplete()", "public void HandlePreSceneChange()"),
                 })
        {
            var handler = Slice(fanout, from, to);
            Assert.DoesNotContain("_chapter3EmergencyPollSeconds", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("_chapter3DemandPollSeconds", handler, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_timers_default_to_zero_so_the_first_poll_waits_a_full_second()
    {
        var fanout = ReadFanoutCs();

        Assert.Contains("private float _chapter3EmergencyPollSeconds;", fanout, StringComparison.Ordinal);
        Assert.Contains("private float _chapter3DemandPollSeconds;", fanout, StringComparison.Ordinal);
    }

    private static string ReadFanoutCs()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "LifecycleFanout.cs"));
    }

    private static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"slice {from}..{to} not found");
        return text.Substring(start, end - start);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
