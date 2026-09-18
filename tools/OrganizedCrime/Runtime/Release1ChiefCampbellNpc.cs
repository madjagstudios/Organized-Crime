using Il2CppInterop.Runtime;
using MelonLoader;
using NPCManager = Il2CppScheduleOne.NPCs.NPCManager;
using S1API.Entities;
using S1API.Messaging;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73. The second Release 1 phone only contact: Chief Campbell of the Hyland Point police. Built
/// exactly like <see cref="Release1NellNpc"/>, including the same deprecated four argument ctor (the
/// obsolete message itself says that ctor remains the supported path for a non physical contact), the
/// same <see cref="OnResponseLoaded"/> capture into a normalized label map, and the same
/// <see cref="TryGetResponse"/> rebind path. No prefab, no avatar, no nav mesh, nothing physical. He
/// is not a mission and never becomes one.
/// </summary>
public sealed class Release1ChiefCampbellNpc : NPC, IRelease1MessagingNpc
{
#pragma warning disable CS0618 // Same deprecated ctor Release1NellNpc uses; see that class's own note.
    public Release1ChiefCampbellNpc()
        : base(
            Release1NativePresentationSupport.ChiefNpcId,
            Release1ChiefCampbellCopy.ContactFirstName,
            Release1ChiefCampbellCopy.ContactLastName,
            null)
    {
        TryApplyPortrait();
    }
#pragma warning restore CS0618

    /// <summary>
    /// The vanilla NPC id the Chief's portrait borrows from by default. Proposed, not owner ratified:
    /// an NPC id is game data, not a game or S1API member, so it is confirmed live through the existing
    /// Insert registry dump, never by reflection. Spec open question 4 gates this exact string on that
    /// dump before it ships as the default. If it is ever wrong, <see cref="TryApplyPortrait"/> fails
    /// soft, the same way a bad preference does.
    /// </summary>
    private const string DefaultPortraitNpcId = "officerdavis"; // Proposed, pending the Insert dump

    /// <summary>
    /// Owner-configured portrait borrowing (OC-56 spec, section 5; OC-73 spec decision 2). When
    /// <see cref="Release1ChiefPortraitNpcIdPreference"/> names a vanilla NPC id, this borrows that
    /// NPC's current <see cref="NPC.Icon"/> sprite for the Chief's own icon and repaints the
    /// conversation list through <see cref="NPC.RefreshMessagingIcons"/>. Every step is wrapped in one
    /// try/catch: any failure (unknown id, no icon on that NPC, a future S1API change) is logged once
    /// and the Chief silently keeps the S1API default icon. No art ships in this ticket; an empty
    /// preference falls back to the fixed default instead of skipping the borrow.
    /// </summary>
    private void TryApplyPortrait()
    {
        var npcId = Release1ChiefPortraitNpcIdPreference.Read();
        if (string.IsNullOrEmpty(npcId)) npcId = DefaultPortraitNpcId; // "officerdavis"

        try
        {
            var source = NPC.Get(npcId);
            if (source?.Icon is not null)
            {
                Icon = source.Icon;
                RefreshMessagingIcons();
                MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait borrowed from NPC '{npcId}'.");
                return;
            }

            // Police officers carry no messaging mugshot on the installed build, so the borrow alone
            // leaves the default icon. Ask the officer's avatar to render one the way the game builds
            // its own faces; the texture arrives later through ApplyRenderedPortrait.
            if (TryRenderPortraitFromAvatar(npcId)) return;

            MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait: no icon found on '{npcId}'; keeping the default.");
        }
        catch (Exception exception)
        {
            MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait borrow from '{npcId}' failed ({exception.GetType().Name}); keeping the default.");
        }
    }

    /// <summary>
    /// Held for the life of this contact so the interop trampoline the game was handed cannot be
    /// collected before the mugshot callback fires.
    /// </summary>
    private Il2CppSystem.Action<Texture2D>? _mugshotCallback;

    /// <summary>
    /// Finds the native NPC for <paramref name="npcId"/> in the game's own registry (S1API keeps its
    /// native handle internal) and asks its <c>Avatar</c> for a mugshot. Returns false, with nothing
    /// logged, when there is no such NPC, no avatar, or no settings to render from, so the caller can
    /// fall through to the existing "no icon found" line. The render itself is asynchronous: a true
    /// return means the request was accepted, not that a face exists yet.
    /// </summary>
    private bool TryRenderPortraitFromAvatar(string npcId)
    {
        var registry = NPCManager.NPCRegistry;
        if (registry is null) return false;

        Il2CppScheduleOne.NPCs.NPC? native = null;
        for (var i = 0; i < registry.Count; i++)
        {
            var candidate = registry[i];
            if (candidate is not null && string.Equals(candidate.ID, npcId, StringComparison.Ordinal))
            {
                native = candidate;
                break;
            }
        }

        var avatar = native?.Avatar;
        if (avatar is null || avatar.CurrentSettings is null) return false;

        _mugshotCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Texture2D>>(
            new Action<Texture2D>(texture => ApplyRenderedPortrait(npcId, texture)));
        avatar.GetMugshot(_mugshotCallback);
        MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait: rendering from the avatar of '{npcId}'.");
        return true;
    }

    /// <summary>
    /// The mugshot callback. Fires on the game's schedule, possibly after a load has replaced this
    /// contact, so it checks it still has a texture to work with and swallows any failure: the worst
    /// case is the default icon the Chief already has.
    /// </summary>
    private void ApplyRenderedPortrait(string npcId, Texture2D? texture)
    {
        try
        {
            if (texture is null || texture.width <= 0 || texture.height <= 0)
            {
                MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait: the avatar of '{npcId}' rendered no texture; keeping the default.");
                return;
            }

            Icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait rendered from the avatar of '{npcId}' ({texture.width}x{texture.height}).");
        }
        catch (Exception exception)
        {
            MelonLogger.Msg($"[Organized Crime] Chief Campbell portrait render from '{npcId}' failed ({exception.GetType().Name}); keeping the default.");
        }
    }

    private readonly Dictionary<string, Response> _loadedResponses = new(StringComparer.Ordinal);

    /// <summary>
    /// Responses S1API restored from save data during this instance's load, keyed by
    /// <see cref="Release1PlayerCopy.Normalize"/>d label. Used by <see cref="TryGetResponse"/> to
    /// locate the restored <see cref="Response"/> for a desired option so the boundary can reattach a
    /// fresh <c>OnTriggered</c> callback to it. <c>NPC.Responses</c> (the base class's own tracking
    /// list) is not used for this: it is <c>protected</c>, not part of the public S1API surface, and
    /// gets cleared and replaced by <c>NPC.SendTextMessage</c> on every fresh send, so it does not
    /// reliably reflect what was restored from save data after this session has sent anything new.
    /// </summary>
    public IReadOnlyDictionary<string, Response> LoadedResponses => _loadedResponses;

    protected override void OnResponseLoaded(Response response)
    {
        if (response is null) return;
        var label = response.Label;
        if (label is null) return;
        _loadedResponses[Release1PlayerCopy.Normalize(label)] = response;
    }

    /// <summary>
    /// Finds a restored response by normalized label, for binding a fresh callback onto it. The
    /// caller is expected to pass an already-normalized label (every desired option label is
    /// normalized before this is called), but the lookup normalizes again defensively so a raw label
    /// still matches.
    /// </summary>
    public bool TryGetResponse(string label, out Response? response) =>
        _loadedResponses.TryGetValue(Release1PlayerCopy.Normalize(label), out response);
}
