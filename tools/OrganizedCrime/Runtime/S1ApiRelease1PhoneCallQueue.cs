using S1API.PhoneCalls;
using NativeCallManager = Il2CppScheduleOne.Calling.CallManager;

namespace OrganizedCrime.Runtime;

public sealed record Release1PhoneCallProjection(string CallerName, IReadOnlyList<string> StageTexts)
{
    public static Release1PhoneCallProjection Create(Release1PhoneCallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return new Release1PhoneCallProjection(request.CallerLabel, request.StageTexts.ToArray());
    }
}

public sealed class S1ApiRelease1PhoneCallQueue : IRelease1PhoneCallQueue
{
    private readonly Func<bool> _isAvailable;
    private readonly Action<Release1PhoneCallProjection> _queueCall;

    public S1ApiRelease1PhoneCallQueue(
        Func<bool>? isAvailable = null,
        Action<Release1PhoneCallProjection>? queueCall = null)
    {
        _isAvailable = isAvailable ?? IsNativeManagerAvailable;
        _queueCall = queueCall ?? QueueWithS1Api;
    }

    public bool IsAvailable(Release1PhoneCallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _isAvailable();
    }

    public void Invoke(Release1PhoneCallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _queueCall(Release1PhoneCallProjection.Create(request));
    }

    private static void QueueWithS1Api(Release1PhoneCallProjection projection) =>
        CallManager.QueueCall(new OcRelease1PhoneCallDefinition(projection.CallerName, projection.StageTexts.ToArray()));

    private static bool IsNativeManagerAvailable()
    {
        try { return NativeCallManager.Instance != null; }
        catch { return false; }
    }
}
