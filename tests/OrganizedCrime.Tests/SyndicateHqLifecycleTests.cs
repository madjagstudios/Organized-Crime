using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class SyndicateHqLifecycleTests
{
    [Fact]
    public void Observer_subscription_state_blocks_replacement_until_exact_cleanup_succeeds()
    {
        var state = new SyndicateHqDoorSubscriptionState();
        var registration = new FakeRegistration("door", failuresBeforeSuccess: 1);

        var generation = state.BeginAttach();
        Assert.True(state.TryTrack(registration));
        Assert.True(state.CanAttach);
        state.RequestDetach();
        Assert.False(state.CanAttach);
        Assert.NotEqual(generation, state.Generation);
        Assert.False(state.TryRemoveAll());
        Assert.False(state.CanAttach);
        Assert.True(state.TryRemoveAll());
        Assert.True(state.CanAttach);
        state.MarkDisposed();
        Assert.False(state.CanAttach);
    }

    [Fact]
    public void Listener_registry_suppresses_duplicate_stable_targets()
    {
        var registry = new SyndicateHqListenerRegistry();
        var first = new FakeRegistration("door");

        Assert.True(registry.TryTrack(first));
        Assert.False(registry.TryTrack(new FakeRegistration("door")));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Listener_registry_removes_exact_registration_and_is_idempotent()
    {
        var registry = new SyndicateHqListenerRegistry();
        var registration = new FakeRegistration("door");
        registry.TryTrack(registration);

        registry.RemoveAll();
        registry.RemoveAll();

        Assert.Equal(0, registry.Count);
        Assert.Equal(1, registration.RemoveCalls);
    }

    [Fact]
    public void Failed_listener_removal_is_retained_for_retry_and_does_not_block_others()
    {
        var registry = new SyndicateHqListenerRegistry();
        var failing = new FakeRegistration("failing", failuresBeforeSuccess: 1);
        var succeeding = new FakeRegistration("succeeding");
        registry.TryTrack(failing);
        registry.TryTrack(succeeding);

        registry.RemoveAll();

        Assert.Equal(1, registry.Count);
        Assert.Equal(1, failing.RemoveCalls);
        Assert.Equal(1, succeeding.RemoveCalls);
        registry.RemoveAll();
        Assert.Equal(0, registry.Count);
        Assert.Equal(2, failing.RemoveCalls);
    }

    [Fact]
    public void Throwing_listener_removal_is_isolated_and_remains_pending()
    {
        var registry = new SyndicateHqListenerRegistry();
        var throwing = new FakeRegistration("throwing", throws: true);
        registry.TryTrack(throwing);

        var exception = Record.Exception(() => registry.RemoveAll());

        Assert.Null(exception);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, throwing.RemoveCalls);
    }

    [Fact]
    public void Teardown_removes_the_original_target_registration_after_scene_replacement()
    {
        var oldTarget = new object();
        var replacementTarget = new object();
        var registry = new SyndicateHqListenerRegistry();
        var registration = new FakeRegistration("door", target: oldTarget);
        registry.TryTrack(registration);

        registry.RemoveAll();

        Assert.Same(oldTarget, registration.RemovedTarget);
        Assert.NotSame(replacementTarget, registration.RemovedTarget);
    }

    [Fact]
    public void Unity_event_boundary_retains_exact_add_remove_member_shape()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqDoorObserver.cs"));

        Assert.Contains("onInteractStart.AddListener(listener)", source, StringComparison.Ordinal);
        Assert.Contains("onInteractStart.RemoveListener(_listener)", source, StringComparison.Ordinal);
        Assert.Contains("if (!Detach()) return false;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_transport_uses_the_proven_player_movement_singleton_seam()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqRuntimeService.cs"));

        Assert.DoesNotContain("_player.GetComponent<PlayerMovement>()", source, StringComparison.Ordinal);
        Assert.Contains("PlayerSingleton<PlayerMovement>.InstanceExists", source, StringComparison.Ordinal);
        Assert.Contains("PlayerSingleton<PlayerMovement>.Instance", source, StringComparison.Ordinal);
        Assert.Contains("movement.Teleport(ToUnity(position), true)", source, StringComparison.Ordinal);
        Assert.Contains("Physics.SyncTransforms()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_builds_the_visible_same_elevation_layout_and_readable_prompt()
    {
        var interior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqInteriorRuntime.cs"));
        var service = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "SyndicateHqRuntimeService.cs"));

        Assert.Contains("SyndicateHqInteriorDefinition.GetPocketRoot(exteriorAnchor)", interior, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector3.up * 30f", interior, StringComparison.Ordinal);
        Assert.Contains("foreach (var primitive in SyndicateHqInteriorDefinition.Primitives)", interior, StringComparison.Ordinal);
        Assert.Contains("foreach (var lightDefinition in SyndicateHqInteriorDefinition.Lights)", interior, StringComparison.Ordinal);
        Assert.Contains("new GUIStyle(GUI.skin.box)", service, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqPromptLayout.BodyFontSize", service, StringComparison.Ordinal);
        Assert.Contains("SyndicateHqPromptLayout.ActionFontSize", service, StringComparison.Ordinal);
    }

    private sealed class FakeRegistration : ISyndicateHqListenerRegistration
    {
        private int _failuresRemaining;
        private readonly bool _throws;
        private readonly object _target;
        public FakeRegistration(string stableIdentity, int failuresBeforeSuccess = 0, bool throws = false, object? target = null) { StableIdentity = stableIdentity; _failuresRemaining = failuresBeforeSuccess; _throws = throws; _target = target ?? new object(); }
        public string StableIdentity { get; }
        public int RemoveCalls { get; private set; }
        public object? RemovedTarget { get; private set; }
        public bool TryRemove(out string? failure)
        {
            RemoveCalls++;
            if (_throws) throw new InvalidOperationException("planned removal exception");
            if (_failuresRemaining > 0) { _failuresRemaining--; failure = "planned failure"; return false; }
            RemovedTarget = _target;
            failure = null;
            return true;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git", "HEAD")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The Organized Crime repository root could not be located.");
    }
}
