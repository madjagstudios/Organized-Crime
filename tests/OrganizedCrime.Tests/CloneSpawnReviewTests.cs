using System.Text.RegularExpressions;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class CloneSpawnReviewTests
{
    [Theory]
    [InlineData(false, "combat.VirtualPunchWeapon")]
    [InlineData(true, "combat.VirtualPunchWeapon.owner")]
    public void Owned_combat_kinds_warn_independently_when_punch_is_null_or_foreign(bool alive, string issue)
    {
        var kinds = new[] { "CombatBehaviour", "PursuitBehaviour", "OtherCombatBehaviour" };
        var warnings = new OcOfficerDependencyWarnings();
        var clone = new OcOfficerCloneRef(null, 123, 1);
        var logged = new List<string>();
        IReadOnlyList<string> Issues() => kinds.SelectMany(kind => OcOfficerCombatIssues.Read(
            new(true, alive, false, false, false, false, kind))).ToArray();
        var status = "dependencies: missing=" + string.Join(",", Issues()) + "; active=True";
        warnings.Observe(clone, OcOfficerDependencyWarnings.Problems(status), logged.Add);
        warnings.Observe(clone, Issues(), logged.Add);
        Assert.Equal(kinds.Select(kind => $"{issue}@{kind}"), logged);
        Assert.Equal("combat warnings=3; observations=6", warnings.CombatStatus(clone));
        // Repairing one kind leaves the other kinds visible without repeated warnings.
        Assert.Empty(OcOfficerCombatIssues.Read(new(true, true, true, false, false, false, kinds[0])));
        warnings.Observe(clone, Issues().Skip(1), logged.Add);
        Assert.Equal(3, logged.Count);
        Assert.Equal("combat warnings=3; observations=8", warnings.CombatStatus(clone));
        warnings.Reset();
        warnings.Observe(clone, Issues(), logged.Add);
        Assert.Equal(6, logged.Count);
    }

    [Fact]
    public void Native_diagnostics_scan_every_owned_combat_kind_and_have_one_inactive_weapon_skip()
    {
        var source = ReadNative();
        source = source[source.IndexOf("internal static string Dependencies", StringComparison.Ordinal)..];
        // Inspect executable code; older source assertions also retain the historical comment.
        var code = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        Assert.Contains("GetComponentsInChildren<CombatBehaviour>(true)", code);
        Assert.Contains(".Where(other => Inside(root, other))", code);
        Assert.Contains("foreach (var combat in combats)", code);
        Assert.Contains("combat.GetIl2CppType().Name", code);
        Assert.Contains("missing.AddRange(OcOfficerCombatIssues.Read(combatState))", code);
        Assert.Contains("if (!Inside(root, defaultCombat)) missing.Add(\"combat.behaviour\");", code);
        Assert.DoesNotContain("if (item == punch) continue;", code);
        Assert.Single(Regex.Matches(code, @"if\s*\(!root\.activeInHierarchy[^\r\n]*continue;").Cast<Match>());
    }

    [Fact]
    public void Template_failure_warns_once_per_wait_and_rearms_after_Ok_in_the_same_load()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        var world = new ReviewWorld { Now = 5000, EnsureFailure = true };
        var roster = new OcOfficerRosterService(world, "review", policy.Information, () => world.Now);
        roster.OnLoadComplete();
        roster.TryRequest("first", new(1, 0, 0), out _);
        for (var frame = 0; frame < 20; frame++) roster.Update();
        Assert.Contains("combat candidates=2", Assert.Single(warnings));
        Assert.Equal(0, Assert.Single(roster.Snapshot()).Attempts);
        world.EnsureFailure = false;
        roster.Update();
        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(roster.Snapshot()).State);
        world.EnsureFailure = true;
        roster.TryRequest("second", new(2, 0, 0), out _);
        for (var frame = 0; frame < 20; frame++) roster.Update();
        Assert.Equal(2, warnings.Count);
        Assert.Equal(warnings[0], warnings[1]);
        Assert.Equal(0, Assert.Single(roster.Snapshot(), slot => slot.OwnerKey == "second").Attempts);
    }

    private static string ReadNative()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tools/OrganizedCrime/Runtime/Police/OcOfficerNativeLifetime.cs");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new InvalidOperationException("Worktree source not found.");
    }
    private sealed class ReviewWorld : IOcOfficerWorld
    {
        public double Now;
        public bool EnsureFailure;
        public bool SpawnRejected = false;
        public bool RemovalBlocked = false;
        public int Activations;
        private bool _registered;
        public bool IsHost => true;
        public OcOfficerWorldStatus TryEnsureTemplate(string id, out OcOfficerTemplateStatus status, out string detail)
        {
            status = new(false, false, 0, id);
            detail = "source officergreen stabilizing (0s of 5s)";
            if (Now < 5000) return OcOfficerWorldStatus.NotReady;
            if (EnsureFailure) { detail = "template build rejected: combat candidates=2"; return OcOfficerWorldStatus.Failed; }
            try { OcOfficerCloneLifetime.OwnedCombat(new[] { "default", "pursuit" }, _ => true); }
            catch (InvalidOperationException ex) { detail = ex.Message; return OcOfficerWorldStatus.Failed; }
            _registered = true;
            status = new(true, true, 120, id);
            detail = "template ready";
            return OcOfficerWorldStatus.Ok;
        }
        public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string id, out OcOfficerCloneRef clone, out string detail)
        {
            clone = default;
            detail = "stale equippable contains a clone combat dependency";
            if (SpawnRejected) return OcOfficerWorldStatus.Rejected;
            Activations++;
            clone = new(new object(), Activations, Activations);
            detail = "activated " + id;
            return OcOfficerWorldStatus.Ok;
        }
        public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
        { status = new(_registered, _registered, 120, "template"); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
        { _registered = false; detail = "destroyed"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
        { detail = "removal pending"; return RemovalBlocked ? OcOfficerWorldStatus.NotReady : OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadGameMinutes(out double minutes) { minutes = 0; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut) { knockedOut = false; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position) { position = new(1, 0, 0); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
        { activeBehaviour = "Checkpoint"; checkpointActive = true; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBoundState state)
        { state = new(true, true, OcCheckpointKind.Ours, null); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? checkpoint, out OcOfficerBehaviourDiagnostics diagnostics)
        { diagnostics = new(Array.Empty<OcOfficerBehaviourInfo>(), "Checkpoint", new(true, true, "ours", OcCheckpointKind.Ours)); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? checkpoint, OcOfficerPosition? position, out string detail)
        { detail = "bound"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryClearSwapMap(out string detail) { detail = "cleared"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail) { detail = "unmapped"; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeLeak(string? label, out bool leaked, out string detail)
        { leaked = false; detail = ""; return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts) { counts = new(Activations, Activations); return OcOfficerWorldStatus.Ok; }
        public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw) { position = new(0, 0, 0); yaw = 0; return OcOfficerWorldStatus.Ok; }
    }
}
