using OrganizedCrime.Model;
using S1API.PhoneCalls;
using NativeCallManager = Il2CppScheduleOne.Calling.CallManager;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: the Release 1 phone mechanism reused for Arthur's Chapter 3
/// fallback call: the same S1API <c>CallManager.QueueCall</c> path, the
/// same <see cref="OcRelease1PhoneCallDefinition"/>, the same "Arthur
/// Selby" caller label, and the shared payphone banner as the cue. It
/// deliberately does not go through <c>Release1PhoneCallService</c>:
/// that service files every call under a Release 1 mission attempt
/// with a Nell prerequisite, and a Chapter 3 visit is neither, so the
/// delivery receipt lives in chapter3.json (the delivered flags) rather
/// than the Release 1 story sidecar. Total: false plus a reason, never
/// an exception out. Host only: refuses without authority.
/// </summary>
public sealed class S1ApiChapter3ArthurPhone : IChapter3ArthurPhone
{
    private readonly IRelease1PayphoneCue? _cue;
    private readonly Func<bool> _isAuthoritativeHost;
    private readonly Func<bool> _isAvailable;
    private readonly Action<string, string[]> _queueCall;
    private readonly Action<string> _warn;
    private bool _disposed;

    public S1ApiChapter3ArthurPhone(
        IRelease1PayphoneCue? cue,
        Func<bool> isAuthoritativeHost,
        Action<string> warn,
        Func<bool>? isAvailable = null,
        Action<string, string[]>? queueCall = null)
    {
        _cue = cue;
        _isAuthoritativeHost = isAuthoritativeHost ?? throw new ArgumentNullException(nameof(isAuthoritativeHost));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _isAvailable = isAvailable ?? IsNativeManagerAvailable;
        _queueCall = queueCall ?? QueueWithS1Api;
    }

    public bool TryCall(Chapter3ArthurVisitKind kind, IReadOnlyList<string> stages, int chapterDay, out string reason)
    {
        reason = "the Chapter 3 Arthur call was refused.";
        if (_disposed)
        {
            reason = "the Chapter 3 Arthur phone was disposed.";
            return false;
        }
        if (!_isAuthoritativeHost())
        {
            reason = "Chapter 3 Arthur calls are host only.";
            return false;
        }
        if (kind != Chapter3ArthurVisitKind.Warning && kind != Chapter3ArthurVisitKind.Cooperation)
        {
            reason = "the Chapter 3 Arthur call kind was invalid.";
            return false;
        }
        if (stages is null || stages.Count == 0 || stages.Any(stage => string.IsNullOrWhiteSpace(stage)))
        {
            reason = "the Chapter 3 Arthur call had no stages.";
            return false;
        }
        try
        {
            if (!_isAvailable())
            {
                reason = "the native call manager was unavailable.";
                return false;
            }
            _queueCall(Chapter3ArthurCopy.CallerLabel, stages.ToArray());
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 Arthur call failed closed: {exception.GetType().Name}.");
            reason = "the Chapter 3 Arthur call threw.";
            return false;
        }
        var correlationId = $"oc11-ch3-arthur-call-{kind.ToString().ToLowerInvariant()}-day-{chapterDay}";
        try
        {
            _cue?.TryShow(correlationId);
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 Arthur payphone cue failed closed: {exception.GetType().Name}.");
        }
        reason = $"called as {Chapter3ArthurCopy.CallerLabel} ({correlationId}).";
        return true;
    }

    public void Dispose() => _disposed = true;

    private static void QueueWithS1Api(string callerLabel, string[] stages) =>
        CallManager.QueueCall(new OcRelease1PhoneCallDefinition(callerLabel, stages));

    private static bool IsNativeManagerAvailable()
    {
        try { return NativeCallManager.Instance != null; }
        catch { return false; }
    }
}
