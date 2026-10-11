using Il2CppScheduleOne.UI;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-138: drives the game's own top-screen banner for the staff-return
/// sender. HUD is a Singleton{HUD} (f8 Atlas) with public
/// ShowTopScreenText/HideTopScreenText (f9 ISIL); Show sets the label,
/// resizes the background, and activates it until Hide runs. The label
/// field reads back the live text so the sender can refuse to stomp a
/// foreign banner. Production-only (Mod.cs wires it); tests fake
/// <see cref="IFishWarehouseHudBannerTarget"/>. A missing or destroyed
/// HUD declines instead of throwing.
/// </summary>
public sealed class FishWarehouseHudBannerTarget : IFishWarehouseHudBannerTarget
{
    public bool IsAvailable()
    {
        try
        {
            var hud = HUD.Instance;
            return hud is not null && hud != null;
        }
        catch
        {
            return false;
        }
    }

    public string? ReadText()
    {
        var hud = HUD.Instance;
        if (hud is null || hud == null)
            return null;
        var label = hud.topScreenText;
        if (label is null || label == null)
            return null;
        return label.text;
    }

    /// <summary>
    /// Show activates the banner background and Hide deactivates it while
    /// the label keeps its text (f9 ISIL), so the background's active
    /// state is the visibility.
    /// </summary>
    public bool IsVisible()
    {
        var hud = HUD.Instance;
        if (hud is null || hud == null)
            return false;
        var background = hud.topScreenText_Background;
        if (background is null || background == null)
            return false;
        return background.gameObject.activeSelf;
    }

    public void ShowText(string text)
    {
        HUD.Instance.ShowTopScreenText(text);
    }

    public void HideText()
    {
        HUD.Instance.HideTopScreenText();
    }
}
