using S1API.Messaging;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The two members <see cref="S1ApiRelease1NativePresentation"/> reads off a restored contact once
/// that boundary is contact generic and its own field can no longer name one concrete subclass. Both
/// <see cref="Release1NellNpc"/> and <see cref="Release1ChiefCampbellNpc"/> already carry both members
/// with an identical body, so implementing this is a signature only change on each class and adds no
/// logic. Every other operation the boundary performs on a contact (ID, SendTextMessage, gameObject,
/// and passing itself to ResolveConversation) is on the S1API base type and needs no interface.
/// </summary>
public interface IRelease1MessagingNpc
{
    IReadOnlyDictionary<string, Response> LoadedResponses { get; }
    bool TryGetResponse(string label, out Response? response);
}
