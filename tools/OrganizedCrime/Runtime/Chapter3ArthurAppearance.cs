using S1API.Entities;
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Entities.Appearances.FaceLayerFields;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-130: Arthur Selby's look, in one named place so the owner can
/// retune it later without hunting. Applied through
/// <c>builder.WithAppearanceDefaults</c> at prefab time, because S1API
/// refuses to spawn a physical custom NPC whose Avatar child is not
/// active ("native Awake reference graph is invalid: Avatar(active)"),
/// and only prefab-time appearance defaults give the native side a
/// buildable avatar before the spawn guard runs. A post-spawn clone
/// comes too late: the NPC never spawns.
/// Designed by the owner in npcworkbench; five entries arrived in the
/// wrong category and were corrected (see the notes). The S1API
/// constant entries keep S1API's own mixed-case path convention
/// (e.g. <c>Avatar/Accessories/Head/FlatCap/FlatCap</c>); the bare
/// strings match the game resource container's lowercase listing
/// exactly. OC-132: the chevron is an S1API constant too. It shipped
/// as a bare lowercase accessory path, which resolves through the
/// runtime verifier's fallbacks but never renders: the native outfit
/// resolution needs the exact library id, so the moustache now uses
/// <c>FacialHairAccessory.Chevron</c> like every other accessory.
/// <c>ExpectedPaths</c> lists every path the look needs so
/// the runtime can verify each resolves and warn per unresolved path.
/// </summary>
public static class Chapter3ArthurAppearance
{
    public const string OldPersonWrinklesPath = "avatar/layers/face/oldpersonwrinkles";
    public const string ButtonUpPath = "avatar/layers/top/buttonup";
    public const string JeansPath = "avatar/layers/bottom/jeans";
    public const string ChevronPath = FacialHairAccessory.Chevron;

    /// <summary>
    /// Every resource path the look needs, in application order: hair,
    /// face layers, body layers, then accessories. The runtime
    /// verifies each resolves and logs one line per unresolved path.
    /// </summary>
    public static readonly IReadOnlyList<string> ExpectedPaths = new[]
    {
        HairStyle.BowlCut,
        Face.Agitated,
        OldPersonWrinklesPath,
        ButtonUpPath,
        JeansPath,
        ChevronPath,
        Head.FlatCap,
        Chest.Blazer,
        Feet.DressShoes,
        Waist.Belt,
        Neck.GoldChain,
    };

    public static void ApplyTo(NPCPrefabBuilder builder)
    {
        if (builder is null) return;
        builder.WithAppearanceDefaults(appearance =>
        {
            appearance.Gender = 0.0f;
            appearance.Height = 0.9822339f;
            appearance.Weight = 0.700238f;
            appearance.SkinColor = new Color32(180, 144, 113, 255);
            appearance.LeftEyeLidColor = new Color32(0, 0, 0, 0);
            appearance.RightEyeLidColor = new Color32(0, 0, 0, 0);
            appearance.EyeBallTint = new Color32(255, 255, 255, 255);
            appearance.EyeballMaterialIdentifier = "";
            appearance.PupilDilation = 0.65f;
            appearance.EyebrowScale = 1.064336f;
            appearance.EyebrowThickness = 1.246624f;
            appearance.EyebrowRestingHeight = -0.521843f;
            appearance.EyebrowRestingAngle = 1.0f;
            appearance.LeftEye = (0.5197827f, 0.5f);
            appearance.RightEye = (0.5197827f, 0.5f);
            // The owner's pick; if it clips through the cap in game,
            // HairStyle.CloseBuzzCut is the fallback.
            appearance.HairPath = HairStyle.BowlCut;
            appearance.HairColor = new Color32(63, 49, 36, 255);
            appearance.WithFaceLayer<Face>(Face.Agitated, new Color32(0, 0, 0, 255));
            // CORRECTED: was a body layer with a bare path.
            appearance.WithFaceLayer(OldPersonWrinklesPath, new Color32(0, 0, 0, 133));
            // CORRECTED: was an accessory layer.
            appearance.WithBodyLayer(ButtonUpPath, new Color32(237, 237, 237, 255));
            // CORRECTED: was an accessory layer.
            appearance.WithBodyLayer(JeansPath, new Color32(31, 42, 68, 255));
            // OC-132: the exact library id through the facial-hair slot.
            // Was a bare lowercase path (and before that a face layer):
            // the lowercase accessory resolves but never renders.
            appearance.WithAccessoryLayer<FacialHairAccessory>(ChevronPath, new Color32(59, 42, 32, 255));
            appearance.WithAccessoryLayer<Head>(Head.FlatCap, new Color32(58, 58, 58, 255));
            appearance.WithAccessoryLayer<Chest>(Chest.Blazer, new Color32(31, 42, 68, 255));
            appearance.WithAccessoryLayer<Feet>(Feet.DressShoes, new Color32(26, 26, 26, 255));
            appearance.WithAccessoryLayer<Waist>(Waist.Belt, new Color32(26, 26, 26, 255));
            appearance.WithAccessoryLayer<Neck>(Neck.GoldChain, new Color32(212, 175, 55, 255));
            // REMOVED: the eye-shadow body layer, an incomplete path
            // left over from the Officer Davis source.
        });
    }
}
