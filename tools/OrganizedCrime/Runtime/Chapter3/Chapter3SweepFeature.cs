using System.Globalization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

internal sealed record Chapter3SweepInput(bool IsHost, double? Minutes,
    LocalPressureTier? Tier, bool? Protected);

internal interface IChapter3SweepQaTarget
{
    Chapter3QaResult SweepCommand(string command);
    IReadOnlyList<string> SweepStatus();
}

// All saved-state writes run under the coordinator's existing demand-poll gate.
// Console commands enqueue transient intent on the same main thread as lifecycle.
internal sealed class Chapter3SweepFeature
{
    internal const double DayMinutes = 1440d;
    internal const double WeekMinutes = 7d * DayMinutes;
    internal const double MorningMinutes = 360d;
    internal const string WarningText = "A sweep is coming tomorrow. I've kept it small.";
    internal const int OrdinaryFoot = 1;
    internal const int OrdinaryVehicle = 1;
    internal const int ProtectedFoot = 1;
    private readonly Chapter3FeatureContext _ctx;
    private Func<Chapter3SweepInput>? _read;
    private IRelease1NativePresentation? _presentation;
    private string? _command;
    private bool _skipCap;
    private bool _suspended;

    internal Chapter3SweepFeature(Chapter3FeatureContext context) => _ctx = context;

    internal void Wire(Func<Chapter3SweepInput> read, IRelease1NativePresentation? presentation)
    {
        _read = read;
        _presentation = presentation;
    }

    internal void ResetTransient()
    {
        _command = null;
        _skipCap = false;
        _suspended = false;
    }

    internal void Suspend() { _suspended = true; _command = null; _skipCap = false; }

    private Chapter3SweepInput Read()
    {
        try { return _read?.Invoke() ?? new(false, null, null, null); }
        catch { return new(false, null, null, null); }
    }

    internal Chapter3QaResult Queue(string command)
    {
        if (_suspended || _ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active ||
            _ctx.State?.Phase != Chapter3Phase.Active || !Read().IsHost)
            return new(false, "sweep refused: active host Chapter 3 required.");
        if (command is not ("start" or "end" or "skipcap"))
            return new(false, "Usage: oc_ch3 sweep start | end | skipcap | status");
        if (command == "skipcap") _skipCap = true;
        else _command = command;
        return new(true, $"sweep {command} queued for the next host poll; safety and warning checks still apply.");
    }

    internal IReadOnlyList<string> Status(Chapter3State? state)
    {
        var input = Read();
        if (!input.IsHost) return new[] { "sweep status: host only, live service required." };
        var sweep = state?.Sweep;
        var cooldown = state?.LastSweepStartMinutes is not { } last ? "0"
            : input.Minutes is { } now ? Math.Max(0d, last + WeekMinutes - now).ToString(CultureInfo.InvariantCulture)
            : "unknown";
        return new[]
        {
            $"sweep inputs: minutes={input.Minutes}, tier={input.Tier}, protection={(input.Protected is { } cover ? cover.ToString() : "unknown")}, suspended={_suspended}.",
            sweep is null ? "sweep: none." :
                $"sweep {sweep.Id}: {(sweep.ActiveStartMinutes is null ? "pending" : "active")}, target={sweep.TargetCode}, size={(sweep.Small ? "small (1 foot)" : "ordinary (1 foot + 1 vehicle)")}, planned={sweep.PlannedStartMinutes}, warning={sweep.WarningMinutes}, start={sweep.ActiveStartMinutes}, end={sweep.ActiveEndMinutes}.",
            $"sweep cooldown minutes={cooldown}, last start={state?.LastSweepStartMinutes}, cap skip={_skipCap}, queued={_command ?? "none"}."
        };
    }

    internal void PollLocked()
    {
        if (_suspended || _ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active ||
            _ctx.State?.Phase != Chapter3Phase.Active) return;
        var input = Read();
        if (!input.IsHost) return;
        var command = _command;
        _command = null;
        if (command == "end")
        {
            SetSweep(null);
            return;
        }
        if (input.Minutes is not { } now || !Chapter3Sweep.ValidMinutes(now)) return;
        var sweep = _ctx.State.Sweep;
        // Expiry never depends on readable heat/protection. A time before start
        // never reconstructs officers (e.g. a clock rollback on a loaded file).
        if (sweep?.ActiveStartMinutes is { })
        {
            if (now >= sweep.ActiveEndMinutes || (!sweep.Small && input.Protected == true))
                SetSweep(null);
            return;
        }
        if (input.Tier is { } tier && tier != LocalPressureTier.Critical)
        {
            SetSweep(null);
            return;
        }
        if (input.Tier != LocalPressureTier.Critical || input.Protected is null) return;
        if (sweep is null)
        {
            var target = _ctx.State.CheckpointTargetCode;
            if (string.IsNullOrWhiteSpace(target)) return;
            var planned = command == "start" ? now : NextMorning(now);
            if (!Chapter3Sweep.ValidMinutes(planned) ||
                (!_skipCap && _ctx.State.LastSweepStartMinutes is { } last && planned < last + WeekMinutes)) return;
            sweep = new Chapter3Sweep(
                $"sweep-{_ctx.State.Revision + 1}-{now.ToString("R", CultureInfo.InvariantCulture)}",
                target, planned, input.Protected.Value);
            SetSweep(sweep);
        }
        // Cover purchased before an ordinary start converts it into a warned,
        // delayed small sweep. Once warned, lapse never increases its size.
        if (!sweep.Small && input.Protected == true)
        {
            sweep = sweep with { Small = true, PlannedStartMinutes = NextMorning(now) };
            SetSweep(sweep);
        }
        if (command == "start" && !sweep.Small && input.Protected == false &&
            (_skipCap || _ctx.State.LastSweepStartMinutes is null ||
             now >= _ctx.State.LastSweepStartMinutes + WeekMinutes))
        {
            sweep = sweep with { PlannedStartMinutes = now };
            SetSweep(sweep);
        }
        if (sweep.Small && sweep.WarningReceipt is null)
        {
            if (!TryDeliverWarning()) return;
            sweep = sweep with
            {
                WarningReceipt = $"{_ctx.State.PlayerId}:{sweep.Id}:warning",
                WarningMinutes = now,
                PlannedStartMinutes = MorningAtOrAfter(now + DayMinutes)
            };
            SetSweep(sweep);
        }
        if (now < sweep.PlannedStartMinutes) return;
        if (now >= sweep.PlannedStartMinutes + DayMinutes)
        {
            SetSweep(null); // Missed pending interval: no catch-up surge.
            return;
        }
        if (!_skipCap && _ctx.State.LastSweepStartMinutes is { } previous && now < previous + WeekMinutes) return;
        SetSweep(sweep with { ActiveStartMinutes = now, ActiveEndMinutes = now + DayMinutes }, now);
        _skipCap = false;
    }

    private bool TryDeliverWarning()
    {
        try
        {
            return _presentation is not null &&
                _presentation.TryEnsureContact() == Release1NativePresentationStatus.Succeeded &&
                _presentation.TrySendMessage(WarningText) == Release1NativePresentationStatus.Succeeded;
        }
        catch { return false; }
    }

    private void SetSweep(Chapter3Sweep? sweep, double? start = null)
    {
        if (_ctx.State is null || (_ctx.State.Sweep == sweep && start is null)) return;
        sweep?.Validate();
        _ctx.State = _ctx.State with
        {
            Sweep = sweep,
            LastSweepStartMinutes = start ?? _ctx.State.LastSweepStartMinutes,
            Revision = _ctx.State.Revision + 1
        };
    }

    internal static double NextMorning(double now) =>
        (Math.Floor((now - MorningMinutes) / DayMinutes) + 1d) * DayMinutes + MorningMinutes;

    private static double MorningAtOrAfter(double now) =>
        Math.Ceiling((now - MorningMinutes) / DayMinutes) * DayMinutes + MorningMinutes;

    internal static Chapter3PatrolProfile? Profile(Chapter3State? state, double? minutes, bool? covered)
    {
        var sweep = state?.Sweep;
        if (sweep?.ActiveStartMinutes is not { } start || minutes is not { } now ||
            !Chapter3Sweep.ValidMinutes(now) || now < start || now >= sweep.ActiveEndMinutes ||
            covered is null || (!sweep.Small && covered.Value)) return null;
        return new(sweep.Small ? "sweep-small" : "sweep", sweep.Small ? ProtectedFoot : OrdinaryFoot,
            sweep.Small ? 0 : OrdinaryVehicle, ReplacesOrdinary: true);
    }
}
