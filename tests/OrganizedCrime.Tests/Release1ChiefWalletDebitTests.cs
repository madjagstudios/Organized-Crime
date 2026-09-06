using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefWalletDebitTests
{
    private static readonly Release1StoryHostContextSnapshot ReadyContext = new(
        Guid.Parse("71717171-7171-7171-7171-717171717171"), 4, "76561190000000001", @"C:\Saves\76561190000000001\SaveGame_5");

    [Fact]
    public void Zero_is_rejected() => AssertRejectedNoChange(0f);

    [Fact]
    public void Any_positive_amount_is_rejected() => AssertRejectedNoChange(100f);

    [Fact]
    public void NaN_is_rejected() => AssertRejectedNoChange(float.NaN);

    [Fact]
    public void Positive_infinity_is_rejected() => AssertRejectedNoChange(float.PositiveInfinity);

    [Fact]
    public void Negative_infinity_is_rejected() => AssertRejectedNoChange(float.NegativeInfinity);

    [Fact]
    public void Below_the_maximum_debit_is_rejected() => AssertRejectedNoChange(-25_001f);

    [Fact]
    public void A_debit_larger_than_the_pre_balance_is_rejected_with_no_native_call()
    {
        var access = new FakeAccess { Cash = 100f };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        var status = world.TryDebitCashBalance(-200f);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, status);
        Assert.Equal(0, access.ChangeCalls);
    }

    [Fact]
    public void A_non_authoritative_host_is_rejected_with_no_native_call()
    {
        var access = new FakeAccess { Cash = 100_000f };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(ready: false), access);

        var status = world.TryDebitCashBalance(-100f);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, status);
        Assert.Equal(0, access.ReadCalls);
        Assert.Equal(0, access.ChangeCalls);
    }

    [Fact]
    public void An_unreadable_pre_balance_is_unavailable_with_no_native_call()
    {
        var access = new FakeAccess { ReadStatus = Release1SmallCourtesyWorldReadStatus.Pending };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        var status = world.TryDebitCashBalance(-100f);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Unavailable, status);
        Assert.Equal(0, access.ChangeCalls);
    }

    [Fact]
    public void A_valid_debit_forwards_exactly_the_negative_amount_once()
    {
        var access = new FakeAccess { Cash = 100_000f };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        var status = world.TryDebitCashBalance(-15_000f);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Succeeded, status);
        Assert.Equal(1, access.ChangeCalls);
        Assert.Equal(-15_000f, access.LastChangeAmount);
    }

    [Fact]
    public void A_throwing_access_is_reported_ambiguous()
    {
        var access = new FakeAccess { Cash = 100_000f, ThrowOnChange = true };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        var status = world.TryDebitCashBalance(-15_000f);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, status);
    }

    [Fact]
    public void TryChangeCashBalance_still_rejects_every_non_positive_amount()
    {
        var access = new FakeAccess { Cash = 100_000f };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeCashBalance(0f));
        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, world.TryChangeCashBalance(-1f));
        Assert.Equal(0, access.ChangeCalls);
    }

    private static void AssertRejectedNoChange(float amount)
    {
        var access = new FakeAccess { Cash = 100_000f };
        var world = new S1ApiRelease1SmallCourtesyWorld(new FakeContext(), access);

        var status = world.TryDebitCashBalance(amount);

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Rejected, status);
        Assert.Equal(0, access.ChangeCalls);
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        private readonly bool _ready;
        public FakeContext(bool ready = true) => _ready = ready;

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = ReadyContext;
            return _ready ? Release1StoryHostContextReadStatus.Ready : Release1StoryHostContextReadStatus.NotAuthoritative;
        }
    }

    /// <summary>Chief-only fake: every member the debit path never touches throws.</summary>
    private sealed class FakeAccess : IRelease1SmallCourtesyWorldAccess
    {
        public float Cash { get; set; }
        public Release1SmallCourtesyWorldReadStatus ReadStatus { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;
        public bool ThrowOnChange { get; set; }
        public int ReadCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public float LastChangeAmount { get; private set; }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            ReadCalls++;
            balance = Cash;
            return ReadStatus;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            ChangeCalls++;
            LastChangeAmount = amount;
            if (ThrowOnChange) throw new InvalidOperationException("synthetic native failure");
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadClock(out int elapsedDays, out int minuteOfDay) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryAttachDropClosed(string deadDropGuid, Action callback, out IRelease1SmallCourtesyCloseRegistration? registration) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
