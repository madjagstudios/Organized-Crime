using Il2CppInterop.Runtime;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI.SleepMessage;
using OrganizedCrime.Model;
using S1API.Lifecycle;
using S1TimeManager = S1API.GameTime.TimeManager;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Enqueues the Chapter 3 overnight line on the game's sleep screen. Subscribes
/// to S1API TimeManager.OnSleepStart (a static Action field, like OnDayPass) and
/// asks the runtime service for the line; the service owns selection and the
/// once-per-night guard. Every native call is guarded; failures log once and skip
/// that night. Not linked into tests: S1API and Il2Cpp only.
/// </summary>
public sealed class Chapter3OvernightLineAdapter : IDisposable
{
    private readonly Chapter3RuntimeService _service;
    private readonly Action<string> _log;
    private Action? _sleepHandler;
    private bool _lifecycleSubscribed;
    private bool _disposed;

    public Chapter3OvernightLineAdapter(Chapter3RuntimeService service, Action<string>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _log = log ?? OrganizedCrimeLog.Warning;
        SubscribeLifecycleEvents();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnsubscribeSleep();

        if (_lifecycleSubscribed)
        {
            try { GameLifecycle.OnPreLoad -= HandlePreLoad; } catch { }
            try { GameLifecycle.OnLoadComplete -= HandleLoadComplete; } catch { }
            try { GameLifecycle.OnPreSceneChange -= HandlePreSceneChange; } catch { }
            _lifecycleSubscribed = false;
        }
    }

    private void SubscribeLifecycleEvents()
    {
        GameLifecycle.OnPreLoad += HandlePreLoad;
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;
        _lifecycleSubscribed = true;
    }

    private void HandlePreLoad() => UnsubscribeSleep();

    private void HandleLoadComplete() => SubscribeSleep();

    private void HandlePreSceneChange() => UnsubscribeSleep();

    private void HandleSleepStart()
    {
        if (_disposed)
            return;

        try
        {
            _service.NotifySleepStarted();
            var line = _service.GetOvernightLineForSleep();
            if (line is null)
                return;
            var controller = SleepController.Instance;
            if (controller is null || controller == null)
            {
                _log("Chapter 3 overnight line skipped: no sleep controller.");
                return;
            }
            var message = new SleepMessage(line, Chapter3OvernightLine.DurationSeconds, Chapter3OvernightLine.Order).TryCast<ISleepEvent>();
            if (message is null)
            {
                _log("Chapter 3 overnight line skipped: the sleep message did not cast.");
                return;
            }
            controller.AddSleepEvent(message);
            _log($"Chapter 3 overnight line: \"{line}\"");
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 overnight line skipped: {exception.GetType().Name}.");
        }
    }

    private void SubscribeSleep()
    {
        if (_disposed)
            return;

        try
        {
            _sleepHandler = HandleSleepStart;
            // OnSleepStart is a static delegate field, so remove before re-adding to
            // avoid a double subscription across same-session reloads.
            S1TimeManager.OnSleepStart = (S1TimeManager.OnSleepStart - _sleepHandler) ?? new Action(() => { });
            S1TimeManager.OnSleepStart += _sleepHandler;
        }
        catch (Exception exception)
        {
            UnsubscribeSleep();
            _log($"Chapter 3 overnight line binding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UnsubscribeSleep()
    {
        try
        {
            S1TimeManager.OnSleepStart = (S1TimeManager.OnSleepStart - _sleepHandler) ?? new Action(() => { });
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 overnight line unbinding failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _sleepHandler = null;
        }
    }
}
