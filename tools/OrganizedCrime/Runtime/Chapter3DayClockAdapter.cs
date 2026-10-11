using MelonLoader;
using S1API.Lifecycle;
using S1TimeManager = S1API.GameTime.TimeManager;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Subscribes to S1API TimeManager.OnDayPass and OnWeekPass at load complete and
/// re-raises each as a plain C# event, following LocalPressureHostLifecycleAdapter.
/// Nothing else in OC touches either member; both were verified by reflection
/// against the installed S1API DLL (public static System.Action fields on
/// S1API.GameTime.TimeManager).
/// </summary>
public sealed class Chapter3DayClockAdapter : IChapter3DayClock, IDisposable
{
    private readonly Action<string> _log;
    private Action? _dayPassHandler;
    private Action? _weekPassHandler;
    private bool _lifecycleSubscribed;
    private bool _disposed;

    public Chapter3DayClockAdapter(Action<string>? log = null)
    {
        _log = log ?? OrganizedCrimeLog.Warning;
        SubscribeLifecycleEvents();
    }

    public event Action? DayPassed;

    public event Action? WeekPassed;

    public bool TryReadElapsedDays(out int elapsedDays)
    {
        elapsedDays = 0;
        try
        {
            elapsedDays = S1TimeManager.ElapsedDays;
            return true;
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 clock read failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnsubscribeDayPass();
        UnsubscribeWeekPass();

        if (_lifecycleSubscribed)
        {
            try { GameLifecycle.OnPreLoad -= HandlePreLoad; } catch { }
            try { GameLifecycle.OnLoadComplete -= HandleLoadComplete; } catch { }
            try { GameLifecycle.OnPreSceneChange -= HandlePreSceneChange; } catch { }
            _lifecycleSubscribed = false;
        }

        DayPassed = null;
        WeekPassed = null;
    }

    private void SubscribeLifecycleEvents()
    {
        GameLifecycle.OnPreLoad += HandlePreLoad;
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;
        _lifecycleSubscribed = true;
    }

    private void HandlePreLoad()
    {
        UnsubscribeDayPass();
        UnsubscribeWeekPass();
    }

    private void HandleLoadComplete()
    {
        SubscribeDayPass();
        SubscribeWeekPass();
    }

    private void HandlePreSceneChange()
    {
        UnsubscribeDayPass();
        UnsubscribeWeekPass();
    }

    private void HandleDayPassed()
    {
        if (_disposed)
            return;

        try
        {
            DayPassed?.Invoke();
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 day-pass forwarding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void HandleWeekPassed()
    {
        if (_disposed)
            return;

        try
        {
            WeekPassed?.Invoke();
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 week-pass forwarding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void SubscribeDayPass()
    {
        if (_disposed)
            return;

        try
        {
            _dayPassHandler = HandleDayPassed;
            // OnDayPass is a static delegate field, so remove before re-adding to
            // avoid a double subscription across same-session reloads.
            S1TimeManager.OnDayPass = (S1TimeManager.OnDayPass - _dayPassHandler) ?? new Action(() => { });
            S1TimeManager.OnDayPass += _dayPassHandler;
        }
        catch (Exception exception)
        {
            UnsubscribeDayPass();
            _log($"Chapter 3 day-pass binding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UnsubscribeDayPass()
    {
        try
        {
            if (_dayPassHandler is not null)
                S1TimeManager.OnDayPass = (S1TimeManager.OnDayPass - _dayPassHandler) ?? new Action(() => { });
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 day-pass unsubscription failed: {exception.GetType().Name}: {exception.Message}");
        }

        _dayPassHandler = null;
    }

    private void SubscribeWeekPass()
    {
        if (_disposed)
            return;

        try
        {
            _weekPassHandler = HandleWeekPassed;
            // OnWeekPass is a static delegate field, so remove before re-adding to
            // avoid a double subscription across same-session reloads.
            S1TimeManager.OnWeekPass = (S1TimeManager.OnWeekPass - _weekPassHandler) ?? new Action(() => { });
            S1TimeManager.OnWeekPass += _weekPassHandler;
        }
        catch (Exception exception)
        {
            UnsubscribeWeekPass();
            _log($"Chapter 3 week-pass binding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UnsubscribeWeekPass()
    {
        try
        {
            if (_weekPassHandler is not null)
                S1TimeManager.OnWeekPass = (S1TimeManager.OnWeekPass - _weekPassHandler) ?? new Action(() => { });
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 week-pass unsubscription failed: {exception.GetType().Name}: {exception.Message}");
        }

        _weekPassHandler = null;
    }
}
