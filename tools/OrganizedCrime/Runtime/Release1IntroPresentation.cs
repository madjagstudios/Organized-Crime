using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class Release1PayphoneBanner : IRelease1PayphoneCue
{
    public const string Text = "A nearby payphone is ringing. Answer it.";
    private readonly Func<float> _now;
    private readonly float _durationSeconds;
    private string? _activeCorrelationId;
    private float _expiresAt;
    private bool _disposed;

    public Release1PayphoneBanner(Func<float>? now = null, float durationSeconds = 8f)
    {
        if (!float.IsFinite(durationSeconds) || durationSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        _now = now ?? (() => Time.unscaledTime);
        _durationSeconds = durationSeconds;
    }

    public bool Visible => !_disposed && _activeCorrelationId is not null && _now() < _expiresAt;

    public bool TryShow(string correlationId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(correlationId)) return false;
        Update();
        if (_activeCorrelationId is not null)
            return string.Equals(_activeCorrelationId, correlationId, StringComparison.Ordinal);
        _activeCorrelationId = correlationId;
        _expiresAt = _now() + _durationSeconds;
        return true;
    }

    public void Update()
    {
        if (_disposed || _activeCorrelationId is null || _now() < _expiresAt) return;
        _activeCorrelationId = null;
        _expiresAt = 0f;
    }

    public void End(string correlationId)
    {
        if (_disposed || !string.Equals(_activeCorrelationId, correlationId, StringComparison.Ordinal)) return;
        _activeCorrelationId = null;
        _expiresAt = 0f;
    }

    public void Reconcile(string correlationId)
    {
        // This cue is intentionally transient and owns no reload reconstruction.
    }

    public void OnGUI()
    {
        Update();
        if (!Visible) return;
        var width = Mathf.Min(620f, Mathf.Max(320f, Screen.width - 32f));
        var box = new Rect(Screen.width / 2f - width / 2f, 42f, width, 74f);
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 22,
            wordWrap = true,
            padding = new RectOffset(18, 18, 12, 12)
        };
        style.normal.textColor = Color.white;
        GUI.Box(box, Text, style);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _activeCorrelationId = null;
        _expiresAt = 0f;
        _disposed = true;
    }
}

public sealed class Release1IntroPromptPresenter
{
    private const string PromptId = "release1-intro";
    private const string PassiveText = "A discreet shipping offer is waiting.";
    public const string PromptText = "A number you do not know is offering a discreet shipping arrangement.";
    private readonly Release1TransitionPublisherService _publisher;

    public Release1IntroPromptPresenter(Release1TransitionPublisherService publisher) =>
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));

    public Release1DecisionPromptCard? DecisionPrompt => !_publisher.PromptVisible
        ? null
        : new(
            PromptId,
            PassiveText,
            "Discreet shipping offer",
            PromptText,
            "Accept",
            () => _publisher.TryAccept(),
            "Not now",
            () => _publisher.TryDefer());
}
