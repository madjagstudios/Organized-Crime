using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.PostBenziesProof;

public enum PostBenziesProofClassification
{
    Pass,
    Inconclusive,
    Stop
}

public enum PostBenziesProofState
{
    Detached,
    AwaitingRead,
    Complete,
    Disposed
}

public sealed record PostBenziesProofEvidence(
    PostBenziesProofClassification Classification,
    Release1PostBenziesUnlockReadStatus? ReadStatus,
    Release1CartelStatus? CartelStatus,
    string? PlayerId,
    string? SaveName,
    int ReadAttempts,
    bool NaturalTransitionProven,
    string Message)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed class PostBenziesProofRuntime : IDisposable
{
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly IRelease1PostBenziesUnlockReader _reader;
    private readonly string _expectedSaveName;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string?> _activeSaveFolderProvider;
    private readonly TimeSpan _deadline;
    private readonly TimeSpan _pollInterval;
    private DateTime _startedUtc;
    private DateTime _nextReadUtc;
    private bool _disposed;

    public PostBenziesProofRuntime(
        IRelease1PostBenziesUnlockReader reader,
        string expectedSaveName,
        Action<string>? log = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? deadline = null,
        TimeSpan? pollInterval = null,
        Func<string?>? activeSaveFolderProvider = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        if (string.IsNullOrWhiteSpace(expectedSaveName))
            throw new ArgumentException("Expected save name is required.", nameof(expectedSaveName));
        _expectedSaveName = expectedSaveName.Trim();
        _log = log ?? (_ => { });
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _activeSaveFolderProvider = activeSaveFolderProvider ?? (() => _expectedSaveName);
        _deadline = deadline ?? DefaultDeadline;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        Evidence = Inconclusive("not initialized");
    }

    public PostBenziesProofState State { get; private set; } = PostBenziesProofState.Detached;

    public PostBenziesProofEvidence Evidence { get; private set; }

    public void Initialize()
    {
        if (_disposed || State != PostBenziesProofState.Detached)
            return;

        _startedUtc = _utcNow();
        _nextReadUtc = _startedUtc;
        State = PostBenziesProofState.AwaitingRead;
        Evidence = Inconclusive("waiting for the authoritative host and cartel state");
    }

    public void Update()
    {
        if (_disposed || State != PostBenziesProofState.AwaitingRead)
            return;

        var now = _utcNow();
        if (now - _startedUtc >= _deadline)
        {
            Complete(Evidence.ReadStatus == Release1PostBenziesUnlockReadStatus.NotAuthoritative
                ? Stop(
                    Release1PostBenziesUnlockReadStatus.NotAuthoritative,
                    Evidence.ReadAttempts,
                    "runtime did not become authoritative before the bounded deadline")
                : Inconclusive(
                    "bounded readiness deadline elapsed",
                    Evidence.ReadStatus,
                    Evidence.ReadAttempts));
            return;
        }
        if (now < _nextReadUtc)
            return;

        string? activeSaveFolder;
        try
        {
            activeSaveFolder = _activeSaveFolderProvider();
        }
        catch
        {
            Complete(Stop(
                Release1PostBenziesUnlockReadStatus.Faulted,
                Evidence.ReadAttempts,
                "active save folder provider faulted"));
            return;
        }

        var activeSaveName = GetSaveName(activeSaveFolder);
        if (activeSaveName is null)
            return;
        if (!string.Equals(activeSaveName, _expectedSaveName, StringComparison.OrdinalIgnoreCase))
        {
            Complete(new PostBenziesProofEvidence(
                PostBenziesProofClassification.Stop,
                null,
                null,
                null,
                activeSaveName,
                Evidence.ReadAttempts,
                NaturalTransitionProven: false,
                $"expected {_expectedSaveName} but the active save was {activeSaveName}"));
            return;
        }

        _nextReadUtc = now + _pollInterval;
        var attempts = Evidence.ReadAttempts + 1;
        Release1PostBenziesUnlockReadStatus readStatus;
        Release1PostBenziesUnlockSnapshot snapshot;
        try
        {
            readStatus = _reader.TryRead(out snapshot);
        }
        catch
        {
            Complete(Stop(
                Release1PostBenziesUnlockReadStatus.Faulted,
                attempts,
                "unlock reader threw while collecting the bounded receipt"));
            return;
        }

        if (readStatus == Release1PostBenziesUnlockReadStatus.Pending)
        {
            Evidence = Inconclusive(
                "waiting for the authoritative host and cartel state",
                readStatus,
                attempts);
            return;
        }

        if (readStatus == Release1PostBenziesUnlockReadStatus.NotAuthoritative)
        {
            Evidence = Inconclusive(
                "waiting for the FishNet host to become authoritative",
                readStatus,
                attempts);
            return;
        }

        if (readStatus == Release1PostBenziesUnlockReadStatus.Unlocked)
        {
            var saveName = GetSaveName(snapshot.HostContext.ActiveSaveFolder);
            if (!string.Equals(saveName, _expectedSaveName, StringComparison.OrdinalIgnoreCase))
            {
                Complete(Stop(
                    readStatus,
                    attempts,
                    $"expected {_expectedSaveName} but the active save was {saveName ?? "unavailable"}",
                    snapshot));
                return;
            }
            if (snapshot.CartelStatus != Release1CartelStatus.Defeated)
            {
                Complete(Stop(
                    readStatus,
                    attempts,
                    "reader reported Unlocked without the required Defeated cartel status",
                    snapshot));
                return;
            }

            Complete(new PostBenziesProofEvidence(
                PostBenziesProofClassification.Pass,
                readStatus,
                snapshot.CartelStatus,
                snapshot.HostContext.PlayerId,
                saveName,
                attempts,
                NaturalTransitionProven: false,
                "restart persistence and the OC-50 reader matched; natural quest transition remains unproven"));
            return;
        }

        var message = readStatus switch
        {
            Release1PostBenziesUnlockReadStatus.Locked => "loaded cartel state was not Defeated",
            Release1PostBenziesUnlockReadStatus.NotAuthoritative => "runtime was not the authoritative host",
            Release1PostBenziesUnlockReadStatus.UnsupportedMultiplayer => "multiplayer is outside the proof boundary",
            Release1PostBenziesUnlockReadStatus.AmbiguousIdentity => "player and save identities did not agree",
            _ => "unlock reader faulted"
        };
        Complete(Stop(readStatus, attempts, message, snapshot));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (State == PostBenziesProofState.AwaitingRead)
        {
            Evidence = Inconclusive(
                "disposed before a terminal read",
                Evidence.ReadStatus,
                Evidence.ReadAttempts);
            _log(Evidence.ToJson());
        }

        _disposed = true;
        State = PostBenziesProofState.Disposed;
    }

    private void Complete(PostBenziesProofEvidence evidence)
    {
        Evidence = evidence;
        State = PostBenziesProofState.Complete;
        _log(evidence.ToJson());
    }

    private PostBenziesProofEvidence Inconclusive(
        string message,
        Release1PostBenziesUnlockReadStatus? readStatus = null,
        int readAttempts = 0) =>
        new(
            PostBenziesProofClassification.Inconclusive,
            readStatus,
            null,
            null,
            null,
            readAttempts,
            NaturalTransitionProven: false,
            message);

    private static PostBenziesProofEvidence Stop(
        Release1PostBenziesUnlockReadStatus? readStatus,
        int attempts,
        string message,
        Release1PostBenziesUnlockSnapshot snapshot = default) =>
        new(
            PostBenziesProofClassification.Stop,
            readStatus,
            snapshot.CartelStatus,
            string.IsNullOrWhiteSpace(snapshot.HostContext.PlayerId) ? null : snapshot.HostContext.PlayerId,
            GetSaveName(snapshot.HostContext.ActiveSaveFolder),
            attempts,
            NaturalTransitionProven: false,
            message);

    private static string? GetSaveName(string? activeSaveFolder)
    {
        if (string.IsNullOrWhiteSpace(activeSaveFolder))
            return null;

        var segments = activeSaveFolder
            .Trim()
            .TrimEnd('\\', '/')
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : segments[^1];
    }
}
