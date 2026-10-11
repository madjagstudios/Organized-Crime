using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc176IntegrationTests
{
    [Fact]
    public void Respawns_and_failed_retries_receive_new_valid_native_guids()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => OcOfficerCloneLifetime.NewGuid()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, Guid.ParseExact(id, "D")));
    }

    [Fact]
    public void Roster_diagnostic_runs_only_on_explicit_host_poll_and_resets_only_on_load()
    {
        var world = new World(); using var roster = new OcOfficerRosterService(world, "health");
        roster.TryRequest("checkpoint", default, out _); roster.Update();
        Assert.Equal(0, world.Polls);
        roster.PollDependencies(); Assert.Equal(1, world.Polls);
        world.Inner.Native.IsHost = false; roster.PollDependencies(); Assert.Equal(1, world.Polls);
        world.Inner.Native.IsHost = true;
        roster.OnSaveStart(); roster.PollDependencies(); Assert.Equal(1, world.Polls); Assert.Equal(0, world.Resets);
        roster.OnSaveComplete(); roster.PollDependencies(); Assert.Equal(2, world.Polls);
        roster.OnPreLoad(); Assert.Equal(0, world.Resets);
        roster.OnLoadComplete(); Assert.Equal(1, world.Resets);
        roster.Dispose(); roster.PollDependencies(); Assert.Equal(2, world.Polls);
    }

    [Fact]
    public void Status_names_each_clone_pointer_and_dependency_and_contains_throwing_reads()
    {
        var world = new World(); using var roster = new OcOfficerRosterService(world, "health");
        roster.TryRequest("checkpoint", default, out _); roster.TryRequest("patrol", default, out _);
        roster.Update(); roster.Update();
        var lines = roster.QaStatus();
        Assert.Contains(lines, line => line.Contains("oc_officer_health_1 pointer=1 dependencies: missing=movement.agent"));
        Assert.Contains(lines, line => line.Contains("oc_officer_health_2 pointer=2 dependencies: missing=movement.agent"));
        world.ThrowRead = true;
        Assert.Equal(2, roster.QaStatus().Count(line => line.EndsWith("dependencies: unknown.")));
    }

    [Fact]
    public void Retained_cleanup_orphan_remains_visible_to_dependency_status()
    {
        var world = new World { FailRemove = true }; using var roster = new OcOfficerRosterService(world, "health");
        roster.TryRequest("checkpoint", default, out var handle); roster.Update(); roster.Release(handle);
        Assert.Contains(roster.QaStatus(), line => line.StartsWith("orphan pointer=1 dependencies: missing=movement.agent"));
        world.FailRemove = false; roster.Update();
        Assert.DoesNotContain(roster.QaStatus(), line => line.StartsWith("orphan pointer="));
    }

    [Fact]
    public void Preview_poll_caches_stock_but_samples_presence_each_time_and_explicit_preview_is_fresh()
    {
        var world = new RaidWorld(); double now = 100;
        var preview = new Chapter3RaidPreview(world, () => now, () => null);
        for (; now <= 110; now++) preview.Poll();
        Assert.Equal(1, world.Scans); Assert.Equal(11, world.PresenceReads);
        world.Quantity = 20; now = 110;
        Assert.Contains(preview.Preview("a"), line => line.Contains("eligible units=20") && line.Contains("clear=True"));
        Assert.Equal(2, world.Scans);
        world.Presence = Chapter3RaidPresence.Inside; preview.Poll();
        world.Presence = Chapter3RaidPresence.Clear; now = 111; preview.Poll();
        Assert.Contains(preview.Preview("a"), line => line.Contains("clear=False"));
        preview.Reset(); preview.Poll(); Assert.Equal(4, world.Scans);
    }

    [Fact]
    public void Native_integration_keeps_copy_cleanup_identity_and_quiescence_before_activation_or_despawn()
    {
        var spawn = Read("Runtime/Police/OcOfficerSpawner.cs");
        Assert.True(spawn.IndexOf("OcOfficerCloneLifetime.Prepare(", StringComparison.Ordinal) < spawn.IndexOf("instance.SetActive(true)", StringComparison.Ordinal));
        var remove = spawn[spawn.IndexOf("public OcOfficerWorldStatus TryRemoveClone", StringComparison.Ordinal)..];
        Assert.True(remove.IndexOf("_context._ownPointers.Contains(pointer)", StringComparison.Ordinal) < remove.IndexOf("UnbindCloneForRemove", StringComparison.Ordinal));
        Assert.True(remove.IndexOf("OcOfficerNativeLifetime.Quiesce", StringComparison.Ordinal) < remove.IndexOf("ServerManager.Despawn", StringComparison.Ordinal));
        Assert.Contains("_context._ownCloneRoots[pointer] = instance", spawn);
        Assert.Contains("OcOfficerNativeLifetime.Quiesce(root!, officer!)", remove);
        Assert.True(spawn.IndexOf("OcOfficerNativeLifetime.BindDependencies(instance, officer)", StringComparison.Ordinal) < spawn.IndexOf("BindSpeedController(instance, officer", StringComparison.Ordinal));
        var template = Read("Runtime/Police/OcOfficerTemplateBuilder.cs");
        Assert.True(template.IndexOf("StripCopies(templateRoot", StringComparison.Ordinal) < template.IndexOf("spawnables.AddObject", StringComparison.Ordinal));
        var native = Read("Runtime/Police/OcOfficerNativeLifetime.cs");
        var strip = native[..native.IndexOf("internal static void BindDependencies", StringComparison.Ordinal)];
        Assert.DoesNotContain(".Unequip()", strip);
        Assert.Contains("finally { root.SetActive(false); }", native);
        Assert.Contains("movement._agent = agents[0]", native);
        Assert.Contains("movement._npc = officer", native);
        Assert.Contains("officer.BakedGUID = guid", native);
        Assert.DoesNotContain("officer.SetGUID(", native);
        var runtime = Read("Runtime/Chapter3RuntimeService.cs");
        var poll = runtime[runtime.IndexOf("public void PollProductionDemand()", StringComparison.Ordinal)..];
        Assert.Contains("_ctx.OfficerRoster?.PollDependencies()", poll[..poll.IndexOf("public void PollArthurVisit()", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Referenced_official_api_and_native_projection_have_the_exact_dependency_surface()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        Assert.Equal(new Version(3, 2, 1, 0), typeof(S1API.Storages.StorageInstance).Assembly.GetName().Version);
        Assert.NotNull(typeof(Il2CppScheduleOne.NPCs.NPC).GetProperty("BakedGUID", flags)?.SetMethod);
        Assert.NotNull(typeof(Il2CppScheduleOne.NPCs.NPCMovement).GetProperty("_agent", flags)?.SetMethod);
        Assert.NotNull(typeof(Il2CppScheduleOne.NPCs.NPCMovement).GetProperty("_npc", flags)?.SetMethod);
        Assert.NotNull(typeof(Il2CppScheduleOne.AvatarFramework.Equipping.FlashlightAvatarEquippable).GetProperty("Light", flags));
        Assert.True(typeof(UnityEngine.Behaviour).IsAssignableFrom(typeof(Il2CppScheduleOne.AvatarFramework.Equipping.AvatarEquippable)));
    }

    private static string Read(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tools/OrganizedCrime/Mod.cs")))
                return File.ReadAllText(Path.Combine(dir.FullName, "tools/OrganizedCrime", relative));
        throw new InvalidOperationException();
    }

    private sealed class RaidWorld : IChapter3RaidWorld, IChapter3RaidPollingWorld
    {
        private readonly Chapter3RaidScanCache _cache = new();
        internal int Scans, PresenceReads, Quantity = 12;
        internal Chapter3RaidPresence Presence = Chapter3RaidPresence.Clear;
        public bool IsHost => true;
        public void Reset() => _cache.Reset();
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions) => Read(exclusions, 110, true);
        public IReadOnlyList<Chapter3RaidSite> ReadForPoll(Chapter3RaidExclusions exclusions, double? minutes) => Read(exclusions, minutes, false);
        private IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions, double? minutes, bool force)
        {
            PresenceReads++;
            return new[] { _cache.Read("a", 1, minutes, force, () =>
            {
                Scans++;
                return new("a", "Property", true, new[] { new Chapter3RaidSlot(new(Guid.Parse("00000001-0000-0000-0000-000000000000"), 0), "fp", "ogkush", "baggie", Quantity, 1) }, Presence);
            }, Presence, exclusions) };
        }
    }

    private sealed class World : IOcOfficerWorld, IOcOfficerDependencyWorld
    {
        internal readonly Chapter3PatrolServiceTests.World Inner = new();
        internal int Polls, Resets;
        internal bool ThrowRead, FailRemove;
        public void PollDependencies() => Polls++;
        public void ResetDependencyWarnings() => Resets++;
        public string ReadDependencies(OcOfficerCloneRef clone) => ThrowRead ? throw new InvalidOperationException() : "dependencies: missing=movement.agent";
        public bool IsHost => Inner.IsHost;
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) => Inner.TryReadGameMinutes(out minutes);
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail) => Inner.TryEnsureTemplate(id, out status, out detail);
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail) => Inner.TrySpawnClone(position, id, out clone, out detail);
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) => Inner.TryReadKnockedOut(clone, out knockedOut);
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) => Inner.TryReadClonePosition(clone, out position);
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string active, out bool checkpoint) => Inner.TryReadBehaviourState(clone, out active, out checkpoint);
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state) => Inner.TryReadBoundState(clone, checkpoint, out state);
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics) => Inner.TryReadBehaviourDiagnostics(clone, checkpoint, out diagnostics);
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? stand, out string detail) => Inner.TryBindToCheckpoint(clone, checkpoint, stand, out detail);
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        { if (FailRemove) { detail = "pending"; return OcOfficerWorldStatus.Failed; } return Inner.TryRemoveClone(clone, out detail); }
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail) => Inner.TryDestroyTemplate(out detail);
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) => Inner.TryClearSwapMap(out detail);
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) => Inner.TryUnmapClone(clone, out detail);
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail) => Inner.TryReadNativeLeak(label, out leaked, out detail);
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status) => Inner.TryReadTemplateStatus(out status);
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) => Inner.TryReadNativeCounts(out counts);
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) => Inner.TryReadPlayerPose(out position, out yaw);
    }
}
