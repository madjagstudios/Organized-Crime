using MelonLoader;
using S1API.Entities;
using S1API.Messaging;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The Release 1 contact rendered through S1API: a persistent custom NPC named Nell Grey.
/// S1API instantiates this type itself on load (through <c>NPCPatches.NPCsLoadersLoad</c>) once it
/// exists in a save's NPC data, calling <see cref="OnResponseLoaded"/> once per restored response on
/// the freshly-constructed instance before the boundary's <c>OnLoadComplete</c> reconciliation pass
/// runs; production otherwise creates it at most once, guarded by an <see cref="NPC.All"/> lookup,
/// in <see cref="S1ApiRelease1NativePresentation"/>.
///
/// Nothing here is static: every response S1API hands to <see cref="OnResponseLoaded"/> during a
/// load is kept on this instance, keyed by <see cref="Release1PlayerCopy.Normalize"/>d label, in
/// <see cref="LoadedResponses"/>. S1API reconstructs <see cref="Response"/> instances from save data
/// without their original C# <c>OnTriggered</c> delegate, so the boundary locates the restored
/// <see cref="Response"/> for a desired option through <see cref="TryGetResponse"/> (by normalized
/// label) and reattaches a fresh callback to it, rather than relying on anything that survives across
/// load boundaries by itself. <see cref="LoadedResponses"/> exists only for this binding lookup: the
/// boundary's observation of what is currently on screen reads native
/// <c>MSGConversation.currentResponses</c> exclusively (see <c>S1ApiRelease1NativePresentation</c>).
/// Implements <see cref="IRelease1MessagingNpc"/> so the boundary can be made contact generic (OC-73).
/// </summary>
public sealed class Release1NellNpc : NPC, IRelease1MessagingNpc
{
#pragma warning disable CS0618 // The installed S1API build deprecates this ctor in favor of ConfigurePrefab/NPCPrefabBuilder.WithIdentity,
    // which targets physical NPCs (prefab, avatar, nav mesh); the obsolete message itself says this
    // ctor remains the supported path for a non-physical contact like Nell. See task-5-report.md.
    public Release1NellNpc() : base(Release1NativePresentationSupport.NellNpcId, "Nell", "Grey", null)
    {
        TryApplyPortrait();
    }
#pragma warning restore CS0618

    /// <summary>
    /// OC-70: the vanilla NPC id Nell's portrait borrows from by default. Ratified by the owner on
    /// 2026-09-06 after one look in game (morning protocol item 130): Lily Turner's face is Nell's
    /// default, and <c>OrganizedCrime.NellPortraitNpcId</c> stays an override for anyone who wants a
    /// different one. An NPC id is game data, not a game or S1API member, so it was verified live, not
    /// by reflection. If it is ever wrong, <see cref="TryApplyPortrait"/> still fails soft, same as a
    /// bad preference does.
    /// </summary>
    private const string DefaultPortraitNpcId = "lily_turner";

    /// <summary>
    /// Owner-configured portrait borrowing (OC-56 spec, section 5). When
    /// <see cref="Release1NellPortraitNpcIdPreference"/> names a vanilla NPC id, this borrows that
    /// NPC's current <see cref="NPC.Icon"/> sprite for Nell's own icon and repaints the conversation
    /// list through <see cref="NPC.RefreshMessagingIcons"/>. The spec's reflection note describes
    /// <c>RefreshMessagingIcons</c> as static; the installed build (S1API 3.2.0.0) actually declares
    /// it an instance method, so it is called on this instance, not on the type. Every step is wrapped
    /// in one try/catch: any failure (unknown id, no icon on that NPC, a future S1API change) is
    /// logged once and Nell silently keeps the S1API default icon. No art ships in this ticket; an
    /// empty preference no longer skips the borrow (OC-70), it falls back to a fixed default instead.
    /// </summary>
    private void TryApplyPortrait()
    {
        var npcId = Release1NellPortraitNpcIdPreference.Read();
        if (string.IsNullOrEmpty(npcId)) npcId = DefaultPortraitNpcId; // "lily_turner"

        try
        {
            var source = NPC.Get(npcId);
            if (source?.Icon is null)
            {
                MelonLogger.Msg($"[Organized Crime] Nell portrait: no icon found on '{npcId}'; keeping the default.");
                return;
            }

            Icon = source.Icon;
            RefreshMessagingIcons();
            MelonLogger.Msg($"[Organized Crime] Nell portrait borrowed from NPC '{npcId}'.");
        }
        catch (Exception exception)
        {
            MelonLogger.Msg($"[Organized Crime] Nell portrait borrow from '{npcId}' failed ({exception.GetType().Name}); keeping the default.");
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
