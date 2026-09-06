using OrganizedCrime.PropertyProbe.Model;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubEventObserverTests
{
    [Fact]
    public void Event_from_exact_candidate_A_records_A_as_ordinal_one()
    {
        var candidateA = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Front Door A");
        var candidateB = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Front Door B");
        var ledger = new NightclubEventObserverLedger(new[] { candidateA, candidateB });

        var result = ledger.Record(candidateA.StableIdentity, timestampSeconds: 1.00f);

        Assert.Equal(NightclubEventReceiptDisposition.Accepted, result.Disposition);
        Assert.Equal(candidateA.StableIdentity, result.Receipt!.DoorStableIdentity);
        Assert.Equal(candidateA.HierarchyPath, result.Receipt.DoorPath);
        Assert.Equal(1, result.Receipt.Ordinal);
        Assert.Equal(0, result.Receipt.DuplicateCount);
    }

    [Fact]
    public void Callback_inside_debounce_window_does_not_create_a_second_interaction()
    {
        var candidateA = NightclubDoorFingerprint.Test();
        var ledger = new NightclubEventObserverLedger(new[] { candidateA });
        ledger.Record(candidateA.StableIdentity, timestampSeconds: 2.00f);

        var duplicate = ledger.Record(candidateA.StableIdentity, timestampSeconds: 2.10f);

        Assert.Equal(NightclubEventReceiptDisposition.DebouncedDuplicate, duplicate.Disposition);
        Assert.Equal(1, ledger.AcceptedInteractionCount);
        Assert.Equal(1, duplicate.Receipt!.Ordinal);
        Assert.Equal(1, duplicate.Receipt.DuplicateCount);
    }

    [Fact]
    public void Later_event_records_ordinal_two_and_preserves_duplicate_evidence()
    {
        var candidateA = NightclubDoorFingerprint.Test();
        var ledger = new NightclubEventObserverLedger(new[] { candidateA });
        ledger.Record(candidateA.StableIdentity, timestampSeconds: 3.00f);
        ledger.Record(candidateA.StableIdentity, timestampSeconds: 3.10f);

        var later = ledger.Record(candidateA.StableIdentity, timestampSeconds: 3.50f);

        Assert.Equal(NightclubEventReceiptDisposition.Accepted, later.Disposition);
        Assert.Equal(2, later.Receipt!.Ordinal);
        Assert.Contains(ledger.Receipts, receipt =>
            receipt.Disposition == NightclubEventReceiptDisposition.DebouncedDuplicate &&
            receipt.Ordinal == 1 &&
            receipt.DuplicateCount == 1);
    }

    [Fact]
    public void Unrelated_interactable_event_cannot_be_recorded()
    {
        var candidateA = NightclubDoorFingerprint.Test();
        var ledger = new NightclubEventObserverLedger(new[] { candidateA });

        var result = ledger.Record("unrelated-door|instance:999", timestampSeconds: 4.00f);

        Assert.Equal(NightclubEventReceiptDisposition.IgnoredUntrackedDoor, result.Disposition);
        Assert.Null(result.Receipt);
        Assert.Empty(ledger.Receipts);
    }
}
