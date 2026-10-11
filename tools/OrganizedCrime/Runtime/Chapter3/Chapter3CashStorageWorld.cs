using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Release1.World;

namespace OrganizedCrime.Runtime.Chapter3;

// The Release 1 world filters non-positive cash reads. Chapter 3 must read
// a surviving zero-balance CashInstance to reconcile its decrement receipt.
// Host authority belongs to Chapter3CashFeature; native readiness and owned
// slot locks remain enforced by the existing storage runtime and boundary.
internal sealed class Chapter3CashStorageWorld : IHoldRoomWorld
{
    private readonly ISyndicateHqStorageRuntime _storage;
    internal Chapter3CashStorageWorld(ISyndicateHqStorageRuntime storage) => _storage = storage;
    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
    {
        room = _storage.ReadHoldRoom();
        return room.Readiness == Release1HoldRoomReadiness.Ready ? Release1SmallCourtesyWorldReadStatus.Ready : Release1SmallCourtesyWorldReadStatus.Unavailable;
    }
    public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string id, int slot, out float balance) =>
        _storage.TryReadClosetSlotCashBalance(id, slot, out balance);
    public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string id, int slot, bool locked) =>
        _storage.TrySetClosetSlotLocked(id, slot, locked);
    public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string id, int slot, float amount) =>
        _storage.TryChangeClosetSlotCashBalance(id, slot, amount);
}
