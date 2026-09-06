using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1PhoneCallService : IDisposable
{
    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1PhoneCallQueue _queue;
    private readonly IRelease1PayphoneCue _cue;
    private readonly Action<string> _log;
    private bool _disposed;

    public Release1PhoneCallService(Release1StoryRuntimeService story, IRelease1PhoneCallQueue queue, IRelease1PayphoneCue cue, Action<string>? log = null)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _cue = cue ?? throw new ArgumentNullException(nameof(cue));
        _log = log ?? (_ => { });
    }

    public Release1PhoneCallResult TryQueue(Release1PhoneCallRequest request)
    {
        if (_disposed) return Result(Release1PhoneCallResultState.Disposed, false, "Phone-call service was disposed.");
        if (request is null) return Result(Release1PhoneCallResultState.InvalidRequest, false, "Phone-call request was null.");
        try { request.Validate(); }
        catch (ArgumentException exception)
        {
            _log($"Release 1 phone request rejected: {exception.GetType().Name}");
            return Result(Release1PhoneCallResultState.InvalidRequest, false, "Phone-call request was invalid.");
        }
        if (!_story.TryGetActiveContext(out var current, out var contextReject))
            return Result(Map(contextReject), false, "Story runtime was not ready on the canonical host.");
        if (request.Context.SessionEpoch != current.SessionEpoch || request.Context.LoadEpoch != current.LoadEpoch)
            return Result(Release1PhoneCallResultState.WrongEpoch, false, "Phone-call request epoch was stale.");
        if (!string.Equals(request.Context.PlayerId, current.PlayerId, StringComparison.Ordinal))
            return Result(Release1PhoneCallResultState.IdentityMismatch, false, "Phone-call request identity did not match the host.");
        try
        {
            if (!SameSaveFolder(request.Context.ActiveSaveFolder, current.ActiveSaveFolder))
                return Result(Release1PhoneCallResultState.InvalidRequest, false, "Phone-call request save path did not match the host.");
        }
        catch (Exception exception)
        {
            _log($"Release 1 phone save-path validation failed closed: {exception.GetType().Name}");
            return Result(Release1PhoneCallResultState.InvalidRequest, false, "Phone-call request save path was invalid.");
        }

        var existing = _story.GetPhonePresentationAttempt(request.CorrelationId);
        if (existing is not null && existing.State is Release1PhonePresentationAttemptState.Ambiguous or Release1PhonePresentationAttemptState.Delivered or Release1PhonePresentationAttemptState.Completed)
            return ExistingResult(existing.State);
        if (existing is not null && existing.State == Release1PhonePresentationAttemptState.Attempting)
            return Result(Release1PhoneCallResultState.Ambiguous, false, "Phone presentation was left attempting and will not be retried automatically.");

        var authorization = _story.TryAuthorizePhonePresentation(request.MissionKey, request.Attempt, request.CorrelationId, request.Role.ToString(), request.RequiredPriorCorrelationId);
        if (!authorization.Accepted) return Result(Map(authorization), false, authorization.Message);

        bool available;
        try { available = _queue.IsAvailable(request); }
        catch (Exception exception)
        {
            _log($"Release 1 phone preflight failed closed: {exception.GetType().Name}");
            available = false;
        }
        if (!available) return Result(Release1PhoneCallResultState.Pending, false, "Phone-call manager was unavailable before invocation.");

        var attempting = _story.TryTransitionPhonePresentation(request.CorrelationId, Release1PhonePresentationAttemptState.Attempting);
        if (!attempting.Accepted) return Result(Map(attempting), false, attempting.Message);
        try { _queue.Invoke(request); }
        catch (Exception exception)
        {
            _log($"Release 1 phone invocation became ambiguous: {exception.GetType().Name}");
            _story.TryTransitionPhonePresentation(request.CorrelationId, Release1PhonePresentationAttemptState.Ambiguous);
            return Result(Release1PhoneCallResultState.Ambiguous, false, "Phone-call invocation threw; automatic retry is forbidden.");
        }

        var delivered = _story.TryTransitionPhonePresentation(request.CorrelationId, Release1PhonePresentationAttemptState.Delivered);
        if (!delivered.Accepted) return Result(Release1PhoneCallResultState.Ambiguous, false, "Phone-call return could not be durably recorded.");
        var cueShown = false;
        try { cueShown = _cue.TryShow(request.CorrelationId); }
        catch (Exception exception) { _log($"Release 1 payphone cue failed closed: {exception.GetType().Name}"); }
        return Result(Release1PhoneCallResultState.Delivered, cueShown, cueShown ? "Phone call returned normally and cue was shown." : "Phone call returned normally; cue was unavailable.");
    }

    public bool ObserveCompleted(string correlationId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(correlationId)) return false;
        var current = _story.GetPhonePresentationAttempt(correlationId);
        if (current is null || current.State is Release1PhonePresentationAttemptState.Ambiguous or Release1PhonePresentationAttemptState.Pending) return false;
        if (current.State == Release1PhonePresentationAttemptState.Completed) return true;
        if (current.State != Release1PhonePresentationAttemptState.Delivered) return false;
        var completed = _story.TryTransitionPhonePresentation(correlationId, Release1PhonePresentationAttemptState.Completed);
        if (!completed.Accepted) return false;
        try { _cue.End(correlationId); }
        catch (Exception exception) { _log($"Release 1 payphone cue teardown failed closed: {exception.GetType().Name}"); }
        _log($"Release 1 phone presentation completed: {correlationId}");
        return true;
    }

    public void ReconcileAfterLoad()
    {
        if (_disposed) return;
        foreach (var attempt in _story.GetPhonePresentationAttempts())
        {
            try
            {
                if (attempt.State == Release1PhonePresentationAttemptState.Delivered) _cue.Reconcile(attempt.CorrelationId);
                else if (attempt.State == Release1PhonePresentationAttemptState.Completed) _cue.End(attempt.CorrelationId);
            }
            catch (Exception exception) { _log($"Release 1 payphone cue reconciliation failed closed: {exception.GetType().Name}"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cue.Dispose();
    }

    private Release1PhoneCallResult ExistingResult(Release1PhonePresentationAttemptState state) => Result(
        state switch
        {
            Release1PhonePresentationAttemptState.Delivered => Release1PhoneCallResultState.Delivered,
            Release1PhonePresentationAttemptState.Completed => Release1PhoneCallResultState.Completed,
            _ => Release1PhoneCallResultState.Ambiguous
        }, false, $"Phone presentation is already {state} and will not be invoked again.");

    private static Release1PhoneCallResultState Map(Release1StoryRuntimeRejectReason reason) => reason switch
    {
        Release1StoryRuntimeRejectReason.NotAuthoritative or Release1StoryRuntimeRejectReason.UnsupportedMultiplayer => Release1PhoneCallResultState.NotAuthoritative,
        Release1StoryRuntimeRejectReason.WrongEpoch => Release1PhoneCallResultState.WrongEpoch,
        Release1StoryRuntimeRejectReason.IdentityMismatch => Release1PhoneCallResultState.IdentityMismatch,
        Release1StoryRuntimeRejectReason.Quarantined => Release1PhoneCallResultState.Quarantined,
        Release1StoryRuntimeRejectReason.Disposed => Release1PhoneCallResultState.Disposed,
        Release1StoryRuntimeRejectReason.ContextPending or Release1StoryRuntimeRejectReason.Inactive or Release1StoryRuntimeRejectReason.RepositoryPathMismatch => Release1PhoneCallResultState.Pending,
        _ => Release1PhoneCallResultState.InvalidRequest
    };

    private static Release1PhoneCallResultState Map(Release1StoryRuntimeCommandResult result) => result.Status == Release1StoryCommandStatus.DeferredSaving ? Release1PhoneCallResultState.Pending : Map(result.RejectReason);
    private static Release1PhoneCallResult Result(Release1PhoneCallResultState state, bool cueShown, string message) => new(state, cueShown, message);
    private static bool SameSaveFolder(string left, string right) => string.Equals(
        Path.GetFullPath(left.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);
}
