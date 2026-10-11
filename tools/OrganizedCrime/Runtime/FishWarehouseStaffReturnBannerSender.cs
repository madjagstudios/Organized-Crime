using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-138: the game's top-screen banner behind a seam, so the sender
/// stays testable and the replay never depends on the live HUD. The
/// production implementation drives HUD.ShowTopScreenText and
/// HUD.HideTopScreenText; tests fake this.
/// </summary>
public interface IFishWarehouseHudBannerTarget
{
    bool IsAvailable();
    string? ReadText();
    void ShowText(string text);
    void HideText();

    /// <summary>
    /// Whether the banner is on screen right now. Native Hide only
    /// deactivates the background and leaves the label text behind, so a
    /// hidden banner still holds the last caller's text: visibility, not
    /// the text, says whether the banner is in use. Fakes that model only
    /// text read a non-empty text as visible.
    /// </summary>
    bool IsVisible() => !string.IsNullOrEmpty(ReadText());
}

/// <summary>
/// OC-138: the staff-return banner sender behind an interface, so the
/// announcer and the owner QA never depend on the live HUD. Show and
/// update share TryShow; TryHideOwn hides only OC's own text.
/// </summary>
public interface IFishWarehouseStaffReturnBannerSender
{
    bool TryShow(string text);
    bool TryHideOwn();
    void Reset();
}

/// <summary>
/// OC-138: shows the "warehouse employees loading" banner on the game's
/// top-screen text and guards it against other systems. Before each
/// show, update, and hide it reads the live banner text: it shows over
/// an empty banner or OC's own text, but never stomps a foreign one.
/// Once another system replaces OC's text mid-load, it latches: no more
/// updates, no hide, one log line. A throwing or missing HUD declines
/// instead of throwing, so the replay can never break on the banner.
/// </summary>
public sealed class FishWarehouseStaffReturnBannerSender : IFishWarehouseStaffReturnBannerSender
{
    private readonly IFishWarehouseHudBannerTarget _target;
    private readonly Action<string> _log;
    private bool _shown;
    private bool _lost;

    public FishWarehouseStaffReturnBannerSender(IFishWarehouseHudBannerTarget target, Action<string>? log = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _log = log ?? (_ => { });
    }

    public bool TryShow(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        try
        {
            if (_lost)
                return false;
            if (!_target.IsAvailable())
                return false;
            // A hidden banner is free whatever stale text it still holds
            // (native Hide leaves the label behind); a visible one is ours
            // only while it shows our prefix.
            var current = _target.ReadText() ?? string.Empty;
            if (!_target.IsVisible() || current.Length == 0 || IsOwnText(current))
            {
                _target.ShowText(text);
                _shown = true;
                return true;
            }
            // A foreign banner is up. If it replaced OC's mid-load,
            // latch: stop updating and never hide it.
            if (_shown)
            {
                _shown = false;
                _lost = true;
                _log("Fish Warehouse staff return banner: a foreign banner replaced ours mid-load; stopped updating and will not hide it.");
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool TryHideOwn()
    {
        try
        {
            // Never shown, or already latched on a foreign banner:
            // there is nothing of ours to hide.
            if (_lost || !_shown)
                return true;
            if (!_target.IsAvailable())
                return false;
            // Already off screen (another caller hid it): nothing of ours
            // is showing, so there is nothing to hide.
            if (!_target.IsVisible())
            {
                _shown = false;
                return true;
            }
            var current = _target.ReadText() ?? string.Empty;
            if (!IsOwnText(current))
            {
                _shown = false;
                _lost = true;
                _log("Fish Warehouse staff return banner: a foreign banner replaced ours mid-load; stopped updating and will not hide it.");
                return false;
            }
            _target.HideText();
            _shown = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Reset()
    {
        try
        {
            // A banner left from the old scene must not stick: hide it
            // while it still shows OC's text. A missing HUD or a foreign
            // banner simply ends the reset.
            if (_shown && !_lost && _target.IsAvailable())
            {
                var current = _target.ReadText() ?? string.Empty;
                if (IsOwnText(current))
                    _target.HideText();
            }
        }
        catch
        {
            // A destroyed HUD declines the reset silently; the latches
            // below still clear so the new load starts fresh.
        }
        _shown = false;
        _lost = false;
    }

    private static bool IsOwnText(string current) =>
        current.StartsWith(FishWarehouseStaffReturnBanner.TextPrefix, StringComparison.Ordinal);
}
