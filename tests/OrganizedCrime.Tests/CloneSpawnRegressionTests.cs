using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class CloneSpawnRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stabilizing_template_resumes_one_clone_per_update_without_live_save_respawns(bool emptySave)
    {
        var world = new SpawnWorld();
        var roster = new OcOfficerRosterService(world, "regression", null, () => world.Now);
        roster.OnLoadComplete();
        if (emptySave) { roster.OnSaveStart(); roster.OnSaveComplete(); }
        roster.TryRequest("checkpoint:a", new(1, 0, 0), out var first);
        roster.TryRequest("checkpoint:b", new(2, 0, 0), out _);
        for (var time = 0; time < 5000; time += 100)
        {
            world.Now = time;
            roster.Update();
        }
        Assert.All(roster.Snapshot(), slot => { Assert.Equal(OcOfficerSlotState.Requested, slot.State); Assert.Equal(0, slot.Attempts); });
        world.Now = 5000;
        roster.Update();
        Assert.Single(roster.Snapshot(), slot => slot.HasLiveClone);
        Assert.Equal(OcOfficerBindStatus.Bound, roster.TryBindToCheckpoint(first, new object()));
        roster.Update();
        Assert.All(roster.Snapshot(), slot => Assert.Equal(OcOfficerSlotState.Active, slot.State));
        Assert.Equal(2, world.Activations);
        Assert.Contains(roster.QaStatus(), line => line.Contains("Active", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_rejection_after_stabilizing_is_visible_once_with_reason_in_normal_mode()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        var world = new SpawnWorld { EnsureFailure = true };
        var roster = new OcOfficerRosterService(world, "regression", policy.Information, () => world.Now);
        roster.TryRequest("checkpoint:a", new(1, 0, 0), out _);
        roster.Update();
        world.Now = 5000;
        for (var frame = 0; frame < 100; frame++) roster.Update();
        Assert.Contains("combat candidates=2", Assert.Single(warnings));
        Assert.Equal(0, Assert.Single(roster.Snapshot()).Attempts);
    }

    [Fact]
    public void Ready_template_with_blocked_spawn_work_warns_once_after_thirty_seconds()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        var world = new SpawnWorld { Now = 5000 };
        var roster = new OcOfficerRosterService(world, "regression", policy.Information, () => world.Now);
        roster.TryRequest("old", new(1, 0, 0), out var old);
        roster.Update();
        world.RemovalBlocked = true;
        roster.Release(old);
        roster.TryRequest("checkpoint:waiting", new(2, 0, 0), out var waiting);
        roster.Update();
        warnings.Clear();
        world.Now = 35000;
        roster.Update();
        Assert.Empty(warnings);
        world.Now = 35001;
        roster.Update();
        for (var frame = 0; frame < 100; frame++) roster.Update();
        var warning = Assert.Single(warnings);
        Assert.Contains("spawn work stalled", warning);
        Assert.Contains("checkpoint:waiting", warning);
        Assert.Contains("pending removals=1", warning);
        world.RemovalBlocked = false;
        roster.Update();
        roster.Update();
        Assert.Equal(OcOfficerSlotState.Active, Assert.Single(roster.Snapshot()).State);
        roster.Release(waiting);
        world.Now += 60000;
        roster.Update();
        Assert.Single(warnings);
    }

    [Fact]
    public void Spawn_rejection_keeps_reason_visible_with_diagnostics_disabled()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        var world = new SpawnWorld { Now = 5000, SpawnRejected = true };
        var roster = new OcOfficerRosterService(world, "regression", policy.Information, () => world.Now);
        roster.TryRequest("checkpoint:a", new(1, 0, 0), out _);
        roster.Update();
        Assert.Contains("stale equippable contains a clone combat dependency", Assert.Single(warnings));
    }

    [Fact]
    public void Default_combat_and_derived_pursuit_both_survive_template_and_clone_preparation()
    {
        Assert.True(typeof(Il2CppScheduleOne.Combat.CombatBehaviour).IsAssignableFrom(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour)));
        var combat = OcOfficerCloneLifetime.OwnedCombat(new[] { "default", "pursuit" }, _ => true);
        var equipment = new[] { "default punch", "pursuit punch", "stale flashlight", "bound baton", "native gun" };
        IReadOnlyList<string> Strip(IEnumerable<string> items) => OcOfficerCloneLifetime.StaleCopies(items,
            item => item != "native gun", _ => true, item => item == "bound baton",
            item => combat.Any(owner => item == owner + " punch"));
        var removed = Strip(equipment);
        Assert.Equal(new[] { "stale flashlight" }, removed);
        var template = equipment.Except(removed).ToArray();
        Assert.Empty(Strip(template));
        Assert.Contains("default punch", template);
        Assert.Contains("pursuit punch", template);
    }

    [Fact]
    public void Empty_or_partly_foreign_combat_set_still_refuses_preparation()
    {
        Assert.Throws<InvalidOperationException>(() => OcOfficerCloneLifetime.OwnedCombat(Array.Empty<string>(), _ => true));
        Assert.Throws<InvalidOperationException>(() => OcOfficerCloneLifetime.OwnedCombat(new[] { "owned", "foreign" }, item => item == "owned"));
        Assert.Equal(new[] { "owned" }, OcOfficerCloneLifetime.OwnedCombat(new[] { "owned" }, _ => true));
    }
    [Fact]
    public void Immediate_activation_allows_only_required_punch_binding_to_wait_for_Start()
    {
        var equipment = new[]
        {
            new OcEquippableDependency("default punch", false, true, true),
            new OcEquippableDependency("pursuit punch", false, true, true),
            new OcEquippableDependency("stale flashlight", false, false)
        };
        Assert.Equal(new[] { "equippable.avatar@stale flashlight", "equippable.Light@stale flashlight" },
            OcOfficerDependencyIssues.Read(new(true, true, true, true, true), equipment, awaitingCombatStart: true));
        Assert.Equal(new[] { "equippable.avatar@default punch", "equippable.avatar@pursuit punch",
                "equippable.avatar@stale flashlight", "equippable.Light@stale flashlight" },
            OcOfficerDependencyIssues.Read(new(true, true, true, true, true), equipment));
        Assert.Equal(new[] { "movement.agent" }, OcOfficerDependencyIssues.Read(
            new(true, true, false, true, true), equipment.Take(2), awaitingCombatStart: true));
    }
    private sealed class SpawnWorld : IOcOfficerWorld
    {
        public double Now;
        public bool EnsureFailure;
        public bool SpawnRejected;
        public bool RemovalBlocked;
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
