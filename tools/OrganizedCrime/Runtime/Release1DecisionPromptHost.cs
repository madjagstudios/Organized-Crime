using System.Globalization;
using GamePauseMenu = Il2CppScheduleOne.UI.PauseMenu;
using GamePhone = Il2CppScheduleOne.UI.Phone.Phone;
using GamePlayerCamera = Il2CppScheduleOne.PlayerScripts.PlayerCamera;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public enum Release1DecisionPromptInput
{
    None,
    Open,
    Primary,
    Secondary,
    Close
}

public enum Release1CursorLockState
{
    None,
    Locked,
    Confined
}

public sealed record Release1DecisionPromptCard
{
    public Release1DecisionPromptCard(
        string id,
        string passiveText,
        string title,
        string body,
        string primaryLabel,
        Action primaryAction,
        string? secondaryLabel = null,
        Action? secondaryAction = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Prompt id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(passiveText)) throw new ArgumentException("Passive text is required.", nameof(passiveText));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Body is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(primaryLabel)) throw new ArgumentException("Primary label is required.", nameof(primaryLabel));
        ArgumentNullException.ThrowIfNull(primaryAction);
        if ((secondaryLabel is null) != (secondaryAction is null))
            throw new ArgumentException("Secondary label and action must either both be present or both be absent.");

        Id = id;
        PassiveText = passiveText;
        Title = title;
        Body = body;
        PrimaryLabel = primaryLabel;
        PrimaryAction = primaryAction;
        SecondaryLabel = secondaryLabel;
        SecondaryAction = secondaryAction;
    }

    public string Id { get; }
    public string PassiveText { get; }
    public string Title { get; }
    public string Body { get; }
    public string PrimaryLabel { get; }
    public Action PrimaryAction { get; }
    public string? SecondaryLabel { get; }
    public Action? SecondaryAction { get; }
}

public interface IRelease1DecisionPromptPlatform
{
    bool CameraAvailable { get; }
    bool VanillaUiOwnsCursor { get; }
    Release1CursorLockState CursorLockState { get; }
    bool CursorVisible { get; }
    string OpenKeyLabel { get; }
    Release1DecisionPromptInput ReadInput(bool modalOpen);
    void AddActiveUiElement(string token);
    void RemoveActiveUiElement(string token);
    void SetCanLook(bool canLook);
    void FreeMouse();
    void LockMouse();
    void SetCursor(Release1CursorLockState lockState, bool visible);
}

public static class Release1PlayerCopy
{
    /// <summary>
    /// The recognized organization's name, used by The Envelope's recognition message (OC-56 owner
    /// edit). Proposed by the owner; the name may still change before ship.
    /// </summary>
    public const string FamilyName = "the Cheeky Blinders";

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace(" \u2014 ", " - ", StringComparison.Ordinal)
            .Replace(" \u2013 ", " - ", StringComparison.Ordinal)
            .Replace("\u2014", "-", StringComparison.Ordinal)
            .Replace("\u2013", "-", StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared attempt header convention (OC-56 spec, section 3): a primary offer gets no header
    /// line at all; a make-good offer gets "Make good, attempt {n}"; a recovery offer gets
    /// "Recovery, attempt {n}". Every mission's assignment-mode enum shares the same three member
    /// names (Primary, MakeGood, Recovery) but is its own distinct type, so this is generic over any
    /// of them rather than duplicated per mission. Replaces the six near-identical StageLabel helpers
    /// that used to live one per mission presentation file.
    /// </summary>
    public static string? AttemptHeader<TMode>(TMode mode, int attempt) where TMode : struct, Enum =>
        mode.ToString() switch
        {
            "Primary" => null,
            "MakeGood" => $"Make good, attempt {attempt}",
            "Recovery" => $"Recovery, attempt {attempt}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unrecognized attempt mode.")
        };

    /// <summary>
    /// The shared deadline convention (OC-56 spec, section 2.3): the actual window in whole (or, on
    /// the rare fractional case, trimmed) hours, "one day" when that window is exactly 24 hours, and
    /// "none" when there is no deadline at all. Every mission's Deadline line goes through this one
    /// implementation so a make-good or recovery window can never again render the primary mission's
    /// constant by accident.
    /// </summary>
    public static string Deadline(double? hours)
    {
        if (hours is null) return "none";
        if (hours.Value == 24d) return "one day";
        return $"{FormatHours(hours.Value)} hours";
    }

    /// <summary>
    /// Joins terms/active body lines with newlines, dropping any null entries. Lets every mission's
    /// terms builder pass <see cref="AttemptHeader{TMode}"/>'s possibly-null result straight into the
    /// line list instead of branching on it manually.
    /// </summary>
    public static string JoinLines(params string?[] lines) =>
        string.Join("\n", lines.Where(line => line is not null));

    private static string FormatHours(double hours) =>
        hours == Math.Floor(hours)
            ? ((long)hours).ToString(CultureInfo.InvariantCulture)
            : hours.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed class Release1DecisionPromptHost : IDisposable
{
    public const string UiToken = "OrganizedCrime.DecisionPrompt";

    private readonly Func<Release1DecisionPromptCard?> _cardProvider;
    private readonly IRelease1DecisionPromptPlatform _platform;
    private readonly Action<string>? _log;
    private Release1DecisionPromptCard? _activeCard;
    private Release1CursorLockState _previousLockState;
    private bool _previousCursorVisible;
    private bool _cameraCaptured;
    private bool _commandIssuedThisFrame;
    private bool _disposed;

    public Release1DecisionPromptHost(
        Func<Release1DecisionPromptCard?> cardProvider,
        IRelease1DecisionPromptPlatform platform,
        Action<string>? log = null)
    {
        _cardProvider = cardProvider ?? throw new ArgumentNullException(nameof(cardProvider));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _log = log;
    }

    public bool ModalOpen { get; private set; }

    public void Update()
    {
        if (_disposed) return;
        _commandIssuedThisFrame = false;
        var card = SafeGetCard();

        if (ModalOpen && card is null)
        {
            CloseModal(restoreGameplay: true);
            return;
        }

        if (ModalOpen && _platform.VanillaUiOwnsCursor)
        {
            CloseModal(restoreGameplay: false);
            return;
        }

        if (!ModalOpen && _platform.VanillaUiOwnsCursor)
            return;

        var input = _platform.ReadInput(ModalOpen);
        if (!ModalOpen)
        {
            if (card is not null && input == Release1DecisionPromptInput.Open)
                OpenModal(card);
            return;
        }

        _activeCard = card;
        switch (input)
        {
            case Release1DecisionPromptInput.Close:
                CloseModal(restoreGameplay: true);
                return;
            case Release1DecisionPromptInput.Primary:
                Execute(_activeCard!.PrimaryAction);
                break;
            case Release1DecisionPromptInput.Secondary when _activeCard!.SecondaryAction is not null:
                Execute(_activeCard.SecondaryAction);
                break;
        }

        if (!ModalOpen) return;
        KeepMouseFree();
    }

    public void OnGUI()
    {
        if (_disposed) return;
        var card = ModalOpen ? _activeCard : SafeGetCard();
        if (card is null) return;

        if (!ModalOpen)
        {
            DrawPassive(card);
            return;
        }

        DrawModal(card);
    }

    public void OnPreLoad() => CloseModal(restoreGameplay: true);

    public void OnPreSceneChange() => CloseModal(restoreGameplay: true);

    public void Dispose()
    {
        if (_disposed) return;
        CloseModal(restoreGameplay: true);
        _disposed = true;
    }

    private Release1DecisionPromptCard? SafeGetCard()
    {
        try
        {
            return _cardProvider();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Decision prompt view failed: {ex.Message}");
            if (ModalOpen) CloseModal(restoreGameplay: true);
            return null;
        }
    }

    private void OpenModal(Release1DecisionPromptCard card)
    {
        if (ModalOpen || _disposed) return;
        _activeCard = card;
        _previousLockState = _platform.CursorLockState;
        _previousCursorVisible = _platform.CursorVisible;
        _cameraCaptured = _platform.CameraAvailable;
        if (_cameraCaptured)
        {
            _platform.AddActiveUiElement(UiToken);
            _platform.SetCanLook(false);
            _platform.FreeMouse();
        }
        _platform.SetCursor(Release1CursorLockState.None, true);
        ModalOpen = true;
    }

    private void KeepMouseFree()
    {
        if (_cameraCaptured) _platform.FreeMouse();
        _platform.SetCursor(Release1CursorLockState.None, true);
    }

    private void CloseModal(bool restoreGameplay)
    {
        if (!ModalOpen) return;
        ModalOpen = false;
        _activeCard = null;

        if (_cameraCaptured)
        {
            _platform.RemoveActiveUiElement(UiToken);
            _platform.SetCanLook(true);
            if (restoreGameplay)
            {
                if (_previousLockState == Release1CursorLockState.Locked)
                    _platform.LockMouse();
            }
        }

        if (restoreGameplay)
            _platform.SetCursor(_previousLockState, _previousCursorVisible);

        _cameraCaptured = false;
    }

    private void Execute(Action action)
    {
        if (_commandIssuedThisFrame || !ModalOpen) return;
        _commandIssuedThisFrame = true;
        try
        {
            action();
            var next = SafeGetCard();
            if (next is null)
                CloseModal(restoreGameplay: true);
            else
                _activeCard = next;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Decision prompt action failed: {ex.Message}");
            CloseModal(restoreGameplay: true);
        }
    }

    private void DrawPassive(Release1DecisionPromptCard card)
    {
        var width = Mathf.Min(520f, Mathf.Max(320f, Screen.width - 32f));
        var box = new Rect(Screen.width - width - 24f, 42f, width, 88f);
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            wordWrap = true,
            padding = new RectOffset(18, 18, 12, 12)
        };
        style.normal.textColor = Color.white;
        GUI.Box(box, Release1PlayerCopy.Normalize($"{card.PassiveText}\nPress {_platform.OpenKeyLabel} to review."), style);
    }

    private void DrawModal(Release1DecisionPromptCard card)
    {
        var width = Mathf.Min(620f, Mathf.Max(360f, Screen.width - 32f));
        var height = 340f;
        var box = new Rect(Screen.width / 2f - width / 2f, Screen.height / 2f - height / 2f, width, height);
        var boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 19,
            wordWrap = true,
            padding = new RectOffset(22, 22, 20, 18)
        };
        boxStyle.normal.textColor = Color.white;
        GUI.Box(box, Release1PlayerCopy.Normalize($"{card.Title}\n\n{card.Body}"), boxStyle);

        var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18 };
        var buttonTop = box.yMax - 58f;
        if (card.SecondaryAction is null)
        {
            if (!_commandIssuedThisFrame && GUI.Button(
                    new Rect(box.x + 22f, buttonTop, box.width - 44f, 40f),
                    Release1PlayerCopy.Normalize($"{card.PrimaryLabel}  [Enter]"),
                    buttonStyle))
                Execute(card.PrimaryAction);
            return;
        }

        var buttonWidth = (box.width - 66f) / 2f;
        if (!_commandIssuedThisFrame && GUI.Button(
                new Rect(box.x + 22f, buttonTop, buttonWidth, 40f),
                Release1PlayerCopy.Normalize($"{card.PrimaryLabel}  [Enter]"),
                buttonStyle))
            Execute(card.PrimaryAction);
        if (!_commandIssuedThisFrame && GUI.Button(
                new Rect(box.x + 44f + buttonWidth, buttonTop, buttonWidth, 40f),
                Release1PlayerCopy.Normalize($"{card.SecondaryLabel}  [N]"),
                buttonStyle))
            Execute(card.SecondaryAction);
    }
}

public sealed class Release1DecisionPromptUnityPlatform : IRelease1DecisionPromptPlatform
{
    private readonly KeyCode _openKey;

    public Release1DecisionPromptUnityPlatform(KeyCode openKey = KeyCode.F7) => _openKey = openKey;

    public bool CameraAvailable
    {
        get
        {
            try { return GamePlayerCamera.InstanceExists; }
            catch { return false; }
        }
    }

    public bool VanillaUiOwnsCursor
    {
        get
        {
            try
            {
                if (GamePhone.InstanceExists && GamePhone.Instance.IsOpen) return true;
            }
            catch { }
            try
            {
                if (GamePauseMenu.InstanceExists && GamePauseMenu.Instance.IsPaused) return true;
            }
            catch { }
            return false;
        }
    }

    public Release1CursorLockState CursorLockState => Cursor.lockState switch
    {
        CursorLockMode.Locked => Release1CursorLockState.Locked,
        CursorLockMode.Confined => Release1CursorLockState.Confined,
        _ => Release1CursorLockState.None
    };

    public bool CursorVisible => Cursor.visible;
    public string OpenKeyLabel => _openKey.ToString();

    public Release1DecisionPromptInput ReadInput(bool modalOpen)
        => MapInput(modalOpen, _openKey, Input.GetKeyDown);

    internal static Release1DecisionPromptInput MapInput(
        bool modalOpen,
        KeyCode openKey,
        Func<KeyCode, bool> getKeyDown)
    {
        if (!modalOpen)
            return getKeyDown(openKey) ? Release1DecisionPromptInput.Open : Release1DecisionPromptInput.None;
        if (getKeyDown(KeyCode.Escape)) return Release1DecisionPromptInput.Close;
        if (getKeyDown(KeyCode.N)) return Release1DecisionPromptInput.Secondary;
        if (getKeyDown(KeyCode.Return) || getKeyDown(KeyCode.KeypadEnter))
            return Release1DecisionPromptInput.Primary;
        return Release1DecisionPromptInput.None;
    }

    public void AddActiveUiElement(string token)
    {
        try { if (GamePlayerCamera.InstanceExists) GamePlayerCamera.Instance.AddActiveUIElement(token); }
        catch { }
    }

    public void RemoveActiveUiElement(string token)
    {
        try { if (GamePlayerCamera.InstanceExists) GamePlayerCamera.Instance.RemoveActiveUIElement(token); }
        catch { }
    }

    public void SetCanLook(bool canLook)
    {
        try { if (GamePlayerCamera.InstanceExists) GamePlayerCamera.Instance.SetCanLook(canLook); }
        catch { }
    }

    public void FreeMouse()
    {
        try { if (GamePlayerCamera.InstanceExists) GamePlayerCamera.Instance.FreeMouse(); }
        catch { }
    }

    public void LockMouse()
    {
        try { if (GamePlayerCamera.InstanceExists) GamePlayerCamera.Instance.LockMouse(); }
        catch { }
    }

    public void SetCursor(Release1CursorLockState lockState, bool visible)
    {
        Cursor.lockState = lockState switch
        {
            Release1CursorLockState.Locked => CursorLockMode.Locked,
            Release1CursorLockState.Confined => CursorLockMode.Confined,
            _ => CursorLockMode.None
        };
        Cursor.visible = visible;
    }
}
