using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseGarageUnloadTests
{
    [Fact]
    public void OC164_live_target_lookup_preserves_identity_and_reference()
    {
        var first = new Target("panel", enabled: true);
        var second = new Target("Panel", enabled: false);

        var lookup = Create(first, second);

        Assert.Equal(2, lookup.Count);
        Assert.Same(first, lookup["panel"]);
        Assert.Same(second, lookup["Panel"]);
    }

    [Fact]
    public void OC164_duplicate_live_identities_still_reject_the_lookup()
    {
        Assert.Throws<ArgumentException>(() => Create(
            new Target("42", enabled: true), new Target("42", enabled: false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OC164_live_target_writes_preserve_the_requested_state(bool enabled)
    {
        var target = new Target("42", !enabled);

        Assert.True(Set(Create(target), "42", enabled));

        Assert.Equal(enabled, target.Enabled);
        Assert.Equal(1, target.Writes);
    }

    [Fact]
    public void OC164_opening_rejects_missing_targets()
    {
        var target = new Target("42", enabled: true);

        Assert.False(Set(Create(target), "missing", enabled: false));

        Assert.True(target.Enabled);
        Assert.Equal(0, target.Writes);
    }

    [Fact]
    public void OC164_destroyed_zero_identities_do_not_collide_with_live_targets()
    {
        var firstDead = new Target("0", enabled: true) { Alive = false };
        var secondDead = new Target("0", enabled: false) { Alive = false };
        var live = new Target("42", enabled: true);

        var lookup = Create(firstDead, live, secondDead);

        Assert.Single(lookup);
        Assert.Same(live, lookup["42"]);
    }

    [Fact]
    public void OC164_destroyed_targets_are_filtered_before_identity_is_read()
    {
        var dead = new Target("0", enabled: true) { Alive = false };
        var live = new Target("42", enabled: true);
        var reads = new List<Target>();

        var lookup = FishWarehouseGarageTargetLookup.Create(
            new[] { dead, live }, static target => target.Alive,
            target =>
            {
                Assert.True(target.Alive);
                reads.Add(target);
                return target.Identity;
            });

        Assert.Same(live, Assert.Single(reads));
        Assert.Same(live, lookup["42"]);
    }

    [Fact]
    public void OC164_restoring_a_missing_target_is_already_clean()
    {
        var lookup = Create();

        Assert.True(Set(lookup, "destroyed", enabled: true, restoring: true));
    }

    [Fact]
    public void OC164_restoring_a_target_destroyed_after_lookup_does_not_write_it()
    {
        var target = new Target("42", enabled: false);
        var lookup = Create(target);
        target.Alive = false;

        Assert.True(Set(lookup, "42", enabled: true, restoring: true));

        Assert.False(target.Enabled);
        Assert.Equal(0, target.Writes);
    }

    [Fact]
    public void OC164_opening_still_rejects_a_target_destroyed_after_lookup()
    {
        var target = new Target("42", enabled: true);
        var lookup = Create(target);
        target.Alive = false;

        Assert.False(Set(lookup, "42", enabled: false));

        Assert.True(target.Enabled);
        Assert.Equal(0, target.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OC164_restore_loop_cleans_destroyed_targets_and_restores_live_snapshot(bool destroyAll)
    {
        var targets = new[]
        {
            new Target("1", enabled: true),
            new Target("2", enabled: false),
            new Target("3", enabled: true)
        };
        var targetSet = TargetSet(targets);
        var mutation = new FishWarehouseGarageTargetMutationState(targetSet);
        Assert.True(mutation.TryOpen(true, new TargetState(targets), static () => true));
        targets[0].Alive = false;
        if (destroyAll)
        {
            targets[1].Alive = false;
            targets[2].Alive = false;
        }

        var restoreState = new TargetState(targets, restoring: true);
        Assert.True(mutation.Restore(restoreState));
        Assert.True(mutation.Restore(restoreState));

        foreach (var target in targets)
            Assert.Equal(target.Alive ? 2 : 1, target.Writes);
        Assert.False(targets[0].Enabled);
        Assert.False(targets[1].Enabled);
        Assert.Equal(!destroyAll, targets[2].Enabled);
    }

    [Fact]
    public void OC164_live_restore_failure_still_reports_failure_and_attempts_remaining_targets()
    {
        var failing = new Target("1", enabled: true);
        var live = new Target("2", enabled: true);
        var targets = new[] { failing, live };
        var mutation = new FishWarehouseGarageTargetMutationState(TargetSet(targets));
        Assert.True(mutation.TryOpen(true, new TargetState(targets), static () => true));
        failing.RejectWrites = true;
        var restoreState = new TargetState(targets, restoring: true);

        Assert.False(mutation.Restore(restoreState));

        Assert.False(failing.Enabled);
        Assert.True(live.Enabled);
        Assert.Equal(2, live.Writes);
        failing.RejectWrites = false;
        Assert.True(mutation.Restore(restoreState));
        Assert.True(failing.Enabled);
    }

    private static FishWarehouseGarageTargetSet TargetSet(Target[] targets) =>
        new(targets.Take(2).Select(target => Component(target, FishWarehouseGarageTargetComponentKind.Renderer)).ToArray(),
            targets.Skip(2).Select(target => Component(target, FishWarehouseGarageTargetComponentKind.Collider)).ToArray());

    private static FishWarehouseGarageTargetComponent Component(Target target, FishWarehouseGarageTargetComponentKind kind) =>
        new("garage", target.Identity, kind, target.Enabled);

    private static IReadOnlyDictionary<string, Target> Create(params Target[] targets) =>
        FishWarehouseGarageTargetLookup.Create(targets, static target => target.Alive, static target => target.Identity);

    private static bool Set(IReadOnlyDictionary<string, Target> targets, string identity, bool enabled, bool restoring = false) =>
        FishWarehouseGarageTargetLookup.TrySetEnabled(
            targets, identity, enabled, static target => target.Alive,
            static (target, value) =>
            {
                if (target.RejectWrites)
                    throw new InvalidOperationException("Live target rejected the write.");
                target.Enabled = value;
                target.Writes++;
            }, restoring);

    private sealed class TargetState : IFishWarehouseGarageTargetState
    {
        private readonly IReadOnlyDictionary<string, Target> _targets;
        private readonly bool _restoring;

        public TargetState(Target[] targets, bool restoring = false)
        {
            _targets = Create(targets);
            _restoring = restoring;
        }

        public bool TryGetEnabled(FishWarehouseGarageTargetComponent component, out bool enabled)
        {
            enabled = false;
            if (!_targets.TryGetValue(component.Identity, out var target) || !target.Alive)
                return false;
            enabled = target.Enabled;
            return true;
        }

        public bool TrySetEnabled(FishWarehouseGarageTargetComponent component, bool enabled)
        {
            try
            {
                return Set(_targets, component.Identity, enabled, _restoring);
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class Target
    {
        public Target(string identity, bool enabled)
        {
            Identity = identity;
            Enabled = enabled;
        }

        public string Identity { get; }
        public bool Alive { get; set; } = true;
        public bool Enabled { get; set; }
        public int Writes { get; set; }
        public bool RejectWrites { get; set; }
    }
}
