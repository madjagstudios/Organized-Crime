using OrganizedCrime.Core.Lifecycle;
using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148: unit tests for the shared SaveLifecycle skeleton. A fake host pins
// every guard, hook order, and failure path; the two services supply the same
// skeleton with their own phases, results, and hooks.
public sealed class SaveLifecycleTests
{
    private enum FakePhase { Detached, AwaitingLoad, Active, Saving, Quarantined, Disposed }

    private sealed record FakeEnvelope(string? PlayerId);

    private sealed record FakeResult(FakePhase Phase, SaveLifecycleOutcome? Outcome, string? Message);

    [Fact]
    public void PreLoad_guards_disposed_repeat_then_clears_in_order()
    {
        var host = Host.Fresh();
        host.Disposed = true;
        Assert.Equal(SaveLifecycleOutcome.Disposed, host.Lifecycle.OnPreLoad().Outcome);
        Assert.Empty(host.Calls);

        host = Host.Fresh();
        host.Phase = FakePhase.AwaitingLoad;
        Assert.Equal(SaveLifecycleOutcome.PreLoadRepeat, host.Lifecycle.OnPreLoad().Outcome);
        Assert.Empty(host.Calls);

        host = Host.Fresh();
        host.Phase = FakePhase.Detached;
        var result = host.Lifecycle.OnPreLoad();
        Assert.Equal(SaveLifecycleOutcome.PreLoadOk, result.Outcome);
        Assert.Equal(new[] { "before-clear", "clear" }, host.Calls);
    }

    [Fact]
    public void LoadComplete_disposed_skips_before_load_guard()
    {
        var host = Host.Fresh();
        host.Disposed = true;

        Assert.Equal(SaveLifecycleOutcome.Disposed, host.Lifecycle.OnLoadComplete().Outcome);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void LoadComplete_runs_before_load_guard_before_the_repeat_guard()
    {
        var host = Host.Fresh();
        host.Phase = FakePhase.Active;

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal(SaveLifecycleOutcome.LoadRepeat, result.Outcome);
        Assert.Equal(new[] { "before-load-guard" }, host.Calls);
    }

    [Fact]
    public void LoadComplete_happy_path_sequence()
    {
        var host = Host.Fresh();

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal(SaveLifecycleOutcome.LoadOk, result.Outcome);
        Assert.Equal(FakePhase.Active, host.Phase);
        Assert.Equal(FakePhase.Active, result.Phase);
        Assert.Equal(
            new[] { "before-load-guard", "read-context", "create-repository", "bound-folder", "same-folder", "load", "activate", "after-load" },
            host.Calls);
        Assert.NotNull(host.ActivatedEnvelope);
        Assert.Equal("player-1", host.ActivatedSnapshot.PlayerId);
    }

    [Fact]
    public void Context_pending_and_not_authoritative_do_not_quarantine()
    {
        var host = Host.Fresh();
        host.ContextStatus = Release1StoryHostContextReadStatus.Pending;
        Assert.Equal(SaveLifecycleOutcome.ContextPending, host.Lifecycle.OnLoadComplete().Outcome);
        Assert.Equal(FakePhase.AwaitingLoad, host.Phase);

        host.ContextStatus = Release1StoryHostContextReadStatus.NotAuthoritative;
        Assert.Equal(SaveLifecycleOutcome.NotAuthoritative, host.Lifecycle.OnLoadComplete().Outcome);
        Assert.Equal(FakePhase.AwaitingLoad, host.Phase);
        Assert.DoesNotContain("create-repository", host.Calls);
    }

    [Fact]
    public void Context_other_status_quarantines_via_map()
    {
        var host = Host.Fresh();
        host.ContextStatus = Release1StoryHostContextReadStatus.AmbiguousIdentity;

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal("Mapped:AmbiguousIdentity", result.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.Equal(FakePhase.Quarantined, result.Phase);
    }

    [Fact]
    public void Context_throw_quarantines_with_message()
    {
        var host = Host.Fresh();
        host.ThrowOnReadContext = true;

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal(SaveLifecycleOutcome.ContextFaulted, result.Outcome);
        Assert.Equal("context boom", result.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
    }

    [Fact]
    public void Factory_throw_quarantines()
    {
        var host = Host.Fresh();
        host.ThrowOnCreateRepository = true;

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal(SaveLifecycleOutcome.FactoryFaulted, result.Outcome);
        Assert.Equal("factory boom", result.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
    }

    [Fact]
    public void Unbound_repository_quarantines_without_checking_folders()
    {
        var host = Host.Fresh();
        host.RepositoryBound = false;

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Equal(SaveLifecycleOutcome.RepositoryUnbound, result.Outcome);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.DoesNotContain("same-folder", host.Calls);
    }

    [Fact]
    public void Path_mismatch_quarantines_and_folder_check_throw_maps_to_factory_fault()
    {
        var host = Host.Fresh();
        host.SameFolder = false;
        var mismatch = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.PathMismatch, mismatch.Outcome);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.DoesNotContain("load", host.Calls);

        host = Host.Fresh();
        host.ThrowOnSameSaveFolder = true;
        var faulted = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.FactoryFaulted, faulted.Outcome);
        Assert.Equal("same-folder boom", faulted.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
    }

    [Fact]
    public void Load_throw_and_load_failure_quarantine()
    {
        var host = Host.Fresh();
        host.ThrowOnLoad = true;
        var thrown = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.LoadFaulted, thrown.Outcome);
        Assert.Equal("load boom", thrown.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);

        host = Host.Fresh();
        host.LoadAttempt = SaveLifecycleLoadAttempt<FakeEnvelope>.Failed("sidecar was corrupt");
        var failed = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.LoadFailed, failed.Outcome);
        Assert.Equal("sidecar was corrupt", failed.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.DoesNotContain("activate", host.Calls);
    }

    [Fact]
    public void Identity_mismatch_quarantines_and_empty_state_skips_the_check()
    {
        var host = Host.Fresh();
        host.LoadAttempt = SaveLifecycleLoadAttempt<FakeEnvelope>.Loaded(true, "player-2", new FakeEnvelope("player-2"));
        var mismatch = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.IdentityMismatch, mismatch.Outcome);
        Assert.Equal(FakePhase.Quarantined, host.Phase);

        host = Host.Fresh();
        host.LoadAttempt = SaveLifecycleLoadAttempt<FakeEnvelope>.Loaded(true, null, new FakeEnvelope(null));
        var nullPlayer = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.IdentityMismatch, nullPlayer.Outcome);
        Assert.Equal(FakePhase.Quarantined, host.Phase);

        host = Host.Fresh();
        host.LoadAttempt = SaveLifecycleLoadAttempt<FakeEnvelope>.Loaded(false, null, null);
        var empty = host.Lifecycle.OnLoadComplete();
        Assert.Equal(SaveLifecycleOutcome.LoadOk, empty.Outcome);
        Assert.Equal(FakePhase.Active, host.Phase);
    }

    [Fact]
    public void AfterLoad_short_circuit_keeps_phase_active()
    {
        var host = Host.Fresh();
        host.AfterLoadResult = new FakeResult(FakePhase.Active, null, "reconcile rejected");

        var result = host.Lifecycle.OnLoadComplete();

        Assert.Same(host.AfterLoadResult, result);
        Assert.Equal(FakePhase.Active, host.Phase);
    }

    [Fact]
    public void SaveStart_guards_and_preserve_is_not_repeat_guarded()
    {
        var host = Host.Fresh();
        host.Disposed = true;
        Assert.Equal(SaveLifecycleOutcome.Disposed, host.Lifecycle.OnSaveStart().Outcome);

        host = Host.Fresh();
        host.Phase = FakePhase.Saving;
        Assert.Equal(SaveLifecycleOutcome.SaveStartRepeat, host.Lifecycle.OnSaveStart().Outcome);
        Assert.Empty(host.Calls);

        host = Host.Fresh();
        host.Phase = FakePhase.Quarantined;
        Assert.Equal("Preserved", host.Lifecycle.OnSaveStart().Message);
        Assert.Equal("Preserved", host.Lifecycle.OnSaveStart().Message);
        Assert.Equal(new[] { "preserve", "preserve" }, host.Calls);

        host = Host.Fresh();
        host.Phase = FakePhase.Detached;
        Assert.Equal(SaveLifecycleOutcome.SaveInactive, host.Lifecycle.OnSaveStart().Outcome);
    }

    [Fact]
    public void SaveStart_captures_snapshot_and_enters_saving()
    {
        var host = Host.Fresh();
        host.Phase = FakePhase.Active;

        var result = host.Lifecycle.OnSaveStart();

        Assert.Equal(SaveLifecycleOutcome.SaveStartOk, result.Outcome);
        Assert.Equal(FakePhase.Saving, host.Phase);
        Assert.Equal(FakePhase.Saving, result.Phase);
        Assert.Equal(new[] { "capture-snapshot" }, host.Calls);
    }

    [Fact]
    public void SaveComplete_guards_disposed_and_not_saving_without_updating()
    {
        var host = Host.Fresh();
        host.Disposed = true;
        Assert.Equal(SaveLifecycleOutcome.Disposed, host.Lifecycle.OnSaveComplete().Outcome);

        host = Host.Fresh();
        host.Phase = FakePhase.Active;
        var noop = host.Lifecycle.OnSaveComplete();
        Assert.Equal(SaveLifecycleOutcome.SaveCompleteNoop, noop.Outcome);
        Assert.Equal(FakePhase.Active, noop.Phase);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void SaveComplete_success_runs_after_save_then_restores_phase()
    {
        var host = Host.Fresh();
        host.Phase = FakePhase.Active;
        host.Lifecycle.OnSaveStart();
        Assert.Equal(FakePhase.Saving, host.Phase);

        var result = host.Lifecycle.OnSaveComplete();

        Assert.Equal(SaveLifecycleOutcome.SaveCompleteOk, result.Outcome);
        Assert.Equal(FakePhase.Active, host.Phase);
        Assert.Equal(FakePhase.Active, result.Phase);
        Assert.Equal(new[] { "capture-snapshot", "update", "after-save", "clear-snapshot" }, host.Calls);
    }

    [Fact]
    public void SaveComplete_update_failure_and_throw_quarantine_and_clear_snapshot()
    {
        var host = Host.Fresh();
        host.Phase = FakePhase.Saving;
        host.SaveAttempt = SaveLifecycleSaveAttempt.Failed("disk was full");
        var failed = host.Lifecycle.OnSaveComplete();
        Assert.Equal(SaveLifecycleOutcome.SaveFailed, failed.Outcome);
        Assert.Equal("disk was full", failed.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.Equal(new[] { "update", "clear-snapshot" }, host.Calls);

        host = Host.Fresh();
        host.Phase = FakePhase.Saving;
        host.ThrowOnSave = true;
        var thrown = host.Lifecycle.OnSaveComplete();
        Assert.Equal(SaveLifecycleOutcome.SaveFaulted, thrown.Outcome);
        Assert.Equal("save boom", thrown.Message);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
    }

    [Fact]
    public void SaveComplete_failed_attempt_with_null_message_still_fails()
    {
        var host = Host.Fresh();
        host.Phase = FakePhase.Saving;
        host.SaveAttempt = SaveLifecycleSaveAttempt.Failed(null);
        var failed = host.Lifecycle.OnSaveComplete();
        Assert.Equal(SaveLifecycleOutcome.SaveFailed, failed.Outcome);
        Assert.Equal(FakePhase.Quarantined, host.Phase);
        Assert.Equal(new[] { "update", "clear-snapshot" }, host.Calls);
    }

    [Fact]
    public void Full_cycle_transitions_phases()
    {
        var host = Host.Fresh();
        Assert.Equal(FakePhase.AwaitingLoad, host.Phase);

        host.Lifecycle.OnLoadComplete();
        Assert.Equal(FakePhase.Active, host.Phase);

        host.Lifecycle.OnSaveStart();
        Assert.Equal(FakePhase.Saving, host.Phase);

        host.Lifecycle.OnSaveComplete();
        Assert.Equal(FakePhase.Active, host.Phase);
    }

    [Fact]
    public void Constructor_rejects_null_hooks_and_delegates()
    {
        Assert.Throws<ArgumentNullException>(() => new SaveLifecycle<FakePhase, FakeResult, FakeEnvelope>(null!));
        Assert.Throws<ArgumentNullException>(() => new SaveLifecycle<FakePhase, FakeResult, FakeEnvelope>(new SaveLifecycleHooks<FakePhase, FakeResult, FakeEnvelope>()));

        void Missing(Action<SaveLifecycleHooks<FakePhase, FakeResult, FakeEnvelope>> clear) =>
            Assert.Throws<ArgumentNullException>(() =>
            {
                var hooks = Host.Fresh().Hooks;
                clear(hooks);
                return new SaveLifecycle<FakePhase, FakeResult, FakeEnvelope>(hooks);
            });

        Missing(hooks => hooks.GetPhase = null!);
        Missing(hooks => hooks.SetPhase = null!);
        Missing(hooks => hooks.IsDisposed = null!);
        Missing(hooks => hooks.BuildResult = null!);
        Missing(hooks => hooks.MapContextStatus = null!);
        Missing(hooks => hooks.ReadContext = null!);
        Missing(hooks => hooks.CreateRepository = null!);
        Missing(hooks => hooks.GetBoundSaveFolder = null!);
        Missing(hooks => hooks.SameSaveFolder = null!);
        Missing(hooks => hooks.Load = null!);
        Missing(hooks => hooks.Activate = null!);
        Missing(hooks => hooks.AfterLoad = null!);
        Missing(hooks => hooks.ClearForLoad = null!);
        Missing(hooks => hooks.PreserveQuarantined = null!);
        Missing(hooks => hooks.CaptureSaveSnapshot = null!);
        Missing(hooks => hooks.ClearSaveSnapshot = null!);
        Missing(hooks => hooks.SaveUpdate = null!);

        var optional = Host.Fresh();
        optional.Hooks.BeforeLoadGuard = null;
        optional.Hooks.BeforeClear = null;
        optional.Hooks.AfterSave = null;
        var lifecycle = new SaveLifecycle<FakePhase, FakeResult, FakeEnvelope>(optional.Hooks);
        Assert.Equal(SaveLifecycleOutcome.LoadOk, lifecycle.OnLoadComplete().Outcome);
    }

    private sealed class Host
    {
        public FakePhase Phase = FakePhase.AwaitingLoad;
        public bool Disposed;
        public List<string> Calls = new();
        public Release1StoryHostContextReadStatus ContextStatus = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot = new(new Guid("33333333-3333-3333-3333-333333333333"), 1, "player-1", "save-folder");
        public bool ThrowOnReadContext;
        public bool ThrowOnCreateRepository;
        public bool RepositoryBound = true;
        public string? BoundFolder = "save-folder";
        public bool ThrowOnSameSaveFolder;
        public bool SameFolder = true;
        public bool ThrowOnLoad;
        public SaveLifecycleLoadAttempt<FakeEnvelope> LoadAttempt =
            SaveLifecycleLoadAttempt<FakeEnvelope>.Loaded(true, "player-1", new FakeEnvelope("player-1"));
        public FakeEnvelope? ActivatedEnvelope;
        public Release1StoryHostContextSnapshot ActivatedSnapshot;
        public FakeResult? AfterLoadResult;
        public SaveLifecycleSaveAttempt SaveAttempt = SaveLifecycleSaveAttempt.Ok();
        public bool ThrowOnSave;
        public SaveLifecycleHooks<FakePhase, FakeResult, FakeEnvelope> Hooks = null!;
        public SaveLifecycle<FakePhase, FakeResult, FakeEnvelope> Lifecycle = null!;

        public static Host Fresh()
        {
            var host = new Host();
            host.Hooks = new SaveLifecycleHooks<FakePhase, FakeResult, FakeEnvelope>
            {
                AwaitingLoadPhase = FakePhase.AwaitingLoad,
                ActivePhase = FakePhase.Active,
                SavingPhase = FakePhase.Saving,
                QuarantinedPhase = FakePhase.Quarantined,
                GetPhase = () => host.Phase,
                SetPhase = phase => host.Phase = phase,
                IsDisposed = () => host.Disposed,
                BuildResult = (outcome, message) => new FakeResult(host.Phase, outcome, message ?? outcome.ToString()),
                MapContextStatus = status => new FakeResult(host.Phase, null, "Mapped:" + status),
                BeforeLoadGuard = () => host.Calls.Add("before-load-guard"),
                ReadContext = () =>
                {
                    host.Calls.Add("read-context");
                    if (host.ThrowOnReadContext) throw new InvalidOperationException("context boom");
                    return new SaveLifecycleContextRead(host.ContextStatus, host.Snapshot);
                },
                CreateRepository = _ =>
                {
                    host.Calls.Add("create-repository");
                    if (host.ThrowOnCreateRepository) throw new InvalidOperationException("factory boom");
                },
                GetBoundSaveFolder = () =>
                {
                    host.Calls.Add("bound-folder");
                    return new SaveLifecycleBoundFolder(host.RepositoryBound, host.BoundFolder);
                },
                SameSaveFolder = (bound, active) =>
                {
                    host.Calls.Add("same-folder");
                    if (host.ThrowOnSameSaveFolder) throw new InvalidOperationException("same-folder boom");
                    return host.SameFolder && string.Equals(bound, active, StringComparison.Ordinal);
                },
                Load = () =>
                {
                    host.Calls.Add("load");
                    if (host.ThrowOnLoad) throw new InvalidOperationException("load boom");
                    return host.LoadAttempt;
                },
                Activate = (envelope, snapshot) =>
                {
                    host.Calls.Add("activate");
                    host.ActivatedEnvelope = envelope;
                    host.ActivatedSnapshot = snapshot;
                },
                AfterLoad = () =>
                {
                    host.Calls.Add("after-load");
                    return host.AfterLoadResult;
                },
                BeforeClear = () => host.Calls.Add("before-clear"),
                ClearForLoad = () => host.Calls.Add("clear"),
                PreserveQuarantined = () =>
                {
                    host.Calls.Add("preserve");
                    return new FakeResult(host.Phase, null, "Preserved");
                },
                CaptureSaveSnapshot = () => host.Calls.Add("capture-snapshot"),
                ClearSaveSnapshot = () => host.Calls.Add("clear-snapshot"),
                SaveUpdate = () =>
                {
                    host.Calls.Add("update");
                    if (host.ThrowOnSave) throw new InvalidOperationException("save boom");
                    return host.SaveAttempt;
                },
                AfterSave = () => host.Calls.Add("after-save"),
            };
            host.Lifecycle = new SaveLifecycle<FakePhase, FakeResult, FakeEnvelope>(host.Hooks);
            return host;
        }
    }
}
