using OrganizedCrime.Model;

namespace OrganizedCrime.Core.Lifecycle;

/// <summary>
/// The shared save/load lifecycle skeleton for the sidecar-backed runtime
/// services (Chapter 3, Release 1 Story): disposed and repeat guards, the
/// context/factory/bound/folder/load/identity/activate load sequence, the
/// quarantined-preserve save-start branch, and the snapshot save completion.
/// The skeleton owns only the pre-save phase; every phase the services read
/// stays service-owned, every hook runs under the service lock, and this
/// class takes no lock of its own. Not thread-safe: call under the lock.
/// </summary>
/// <typeparam name="TPhase">The service phase enum.</typeparam>
/// <typeparam name="TResult">The service lifecycle-result type.</typeparam>
/// <typeparam name="TEnvelope">The service save-envelope type.</typeparam>
public sealed class SaveLifecycle<TPhase, TResult, TEnvelope>
    where TPhase : struct, Enum
    where TEnvelope : class
{
    private readonly SaveLifecycleHooks<TPhase, TResult, TEnvelope> _hooks;
    private TPhase _phaseBeforeSave;

    public SaveLifecycle(SaveLifecycleHooks<TPhase, TResult, TEnvelope> hooks)
    {
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        ArgumentNullException.ThrowIfNull(hooks.GetPhase);
        ArgumentNullException.ThrowIfNull(hooks.SetPhase);
        ArgumentNullException.ThrowIfNull(hooks.IsDisposed);
        ArgumentNullException.ThrowIfNull(hooks.BuildResult);
        ArgumentNullException.ThrowIfNull(hooks.MapContextStatus);
        ArgumentNullException.ThrowIfNull(hooks.ReadContext);
        ArgumentNullException.ThrowIfNull(hooks.CreateRepository);
        ArgumentNullException.ThrowIfNull(hooks.GetBoundSaveFolder);
        ArgumentNullException.ThrowIfNull(hooks.SameSaveFolder);
        ArgumentNullException.ThrowIfNull(hooks.Load);
        ArgumentNullException.ThrowIfNull(hooks.Activate);
        ArgumentNullException.ThrowIfNull(hooks.AfterLoad);
        ArgumentNullException.ThrowIfNull(hooks.ClearForLoad);
        ArgumentNullException.ThrowIfNull(hooks.PreserveQuarantined);
        ArgumentNullException.ThrowIfNull(hooks.CaptureSaveSnapshot);
        ArgumentNullException.ThrowIfNull(hooks.ClearSaveSnapshot);
        ArgumentNullException.ThrowIfNull(hooks.SaveUpdate);
    }

    public TResult OnPreLoad()
    {
        if (_hooks.IsDisposed())
            return Build(SaveLifecycleOutcome.Disposed);
        if (Is(GetPhase(), _hooks.AwaitingLoadPhase))
            return Build(SaveLifecycleOutcome.PreLoadRepeat);
        _hooks.BeforeClear?.Invoke();
        _hooks.ClearForLoad();
        return Build(SaveLifecycleOutcome.PreLoadOk);
    }

    public TResult OnLoadComplete()
    {
        if (_hooks.IsDisposed())
            return Build(SaveLifecycleOutcome.Disposed);
        _hooks.BeforeLoadGuard?.Invoke();
        var phase = GetPhase();
        if (Is(phase, _hooks.ActivePhase) || Is(phase, _hooks.SavingPhase))
            return Build(SaveLifecycleOutcome.LoadRepeat);

        SaveLifecycleContextRead read;
        try
        {
            read = _hooks.ReadContext();
        }
        catch (Exception ex)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.ContextFaulted, ex.Message);
        }

        if (read.Status == Release1StoryHostContextReadStatus.Pending)
            return Build(SaveLifecycleOutcome.ContextPending);
        if (read.Status == Release1StoryHostContextReadStatus.NotAuthoritative)
            return Build(SaveLifecycleOutcome.NotAuthoritative);
        if (read.Status != Release1StoryHostContextReadStatus.Ready)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return _hooks.MapContextStatus(read.Status);
        }

        try
        {
            _hooks.CreateRepository(read.Snapshot.ActiveSaveFolder);
        }
        catch (Exception ex)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.FactoryFaulted, ex.Message);
        }

        SaveLifecycleBoundFolder bound;
        try
        {
            bound = _hooks.GetBoundSaveFolder();
        }
        catch (Exception ex)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.FactoryFaulted, ex.Message);
        }
        if (!bound.IsBound)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.RepositoryUnbound);
        }

        bool same;
        try
        {
            same = _hooks.SameSaveFolder(bound.Folder!, read.Snapshot.ActiveSaveFolder);
        }
        catch (Exception ex)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.FactoryFaulted, ex.Message);
        }

        if (!same)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.PathMismatch);
        }

        SaveLifecycleLoadAttempt<TEnvelope> attempt;
        try
        {
            attempt = _hooks.Load();
        }
        catch (Exception ex)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.LoadFaulted, ex.Message);
        }

        if (!attempt.Succeeded)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.LoadFailed, attempt.Message);
        }

        if (attempt.HasState && attempt.EnvelopePlayerId != read.Snapshot.PlayerId)
        {
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.IdentityMismatch);
        }

        _hooks.Activate(attempt.Envelope, read.Snapshot);
        SetPhase(_hooks.ActivePhase);
        var afterLoad = _hooks.AfterLoad();
        if (afterLoad is not null)
            return afterLoad;
        return Build(SaveLifecycleOutcome.LoadOk);
    }

    public TResult OnSaveStart()
    {
        if (_hooks.IsDisposed())
            return Build(SaveLifecycleOutcome.Disposed);
        var phase = GetPhase();
        if (Is(phase, _hooks.SavingPhase))
            return Build(SaveLifecycleOutcome.SaveStartRepeat);
        if (Is(phase, _hooks.QuarantinedPhase))
            return _hooks.PreserveQuarantined();
        if (!Is(phase, _hooks.ActivePhase))
            return Build(SaveLifecycleOutcome.SaveInactive);
        _phaseBeforeSave = phase;
        _hooks.CaptureSaveSnapshot();
        SetPhase(_hooks.SavingPhase);
        return Build(SaveLifecycleOutcome.SaveStartOk);
    }

    public TResult OnSaveComplete()
    {
        if (_hooks.IsDisposed())
            return Build(SaveLifecycleOutcome.Disposed);
        if (!Is(GetPhase(), _hooks.SavingPhase))
            return Build(SaveLifecycleOutcome.SaveCompleteNoop);

        SaveLifecycleSaveAttempt attempt;
        try
        {
            attempt = _hooks.SaveUpdate();
        }
        catch (Exception ex)
        {
            _hooks.ClearSaveSnapshot();
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.SaveFaulted, ex.Message);
        }

        if (!attempt.Succeeded)
        {
            _hooks.ClearSaveSnapshot();
            SetPhase(_hooks.QuarantinedPhase);
            return Build(SaveLifecycleOutcome.SaveFailed, attempt.Message);
        }

        _hooks.AfterSave?.Invoke();
        _hooks.ClearSaveSnapshot();
        SetPhase(_phaseBeforeSave);
        return Build(SaveLifecycleOutcome.SaveCompleteOk);
    }

    private TResult Build(SaveLifecycleOutcome outcome, string? message = null) =>
        _hooks.BuildResult(outcome, message);

    private TPhase GetPhase() => _hooks.GetPhase();

    private void SetPhase(TPhase phase) => _hooks.SetPhase(phase);

    private static bool Is(TPhase left, TPhase right) =>
        EqualityComparer<TPhase>.Default.Equals(left, right);
}
