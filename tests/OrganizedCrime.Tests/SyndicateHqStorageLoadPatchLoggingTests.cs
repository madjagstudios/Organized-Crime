using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Exercises SyndicateHqStorageLoadPatch's log routing directly through its private static
/// fields (this test project compiles SyndicateHqStorageLoadPatch.cs as a linked source file,
/// so it lives in the same assembly). This avoids driving the class through Apply(), which
/// would perform a real Harmony patch against the installed game assemblies; every other test
/// covering this class asserts on its source text for the same reason.
/// </summary>
public sealed class SyndicateHqStorageLoadPatchLoggingTests
{
    [Fact]
    public void A_failed_native_load_context_logs_the_warning_delegate_and_never_the_receipt_delegate()
    {
        var warnings = new List<string>();
        var receipts = new List<string>();
        try
        {
            SetStaticField("_runtime", new NoopRuntime());
            SetStaticField("_context", new SyndicateHqStorageLoadContextAdapter());
            SetStaticField("_log", (Action<string>)warnings.Add);
            SetStaticField("_receiptLog", (Action<string>)receipts.Add);

            SyndicateHqStorageLoadPatch.BeginLoadCycle();
            SyndicateHqStorageLoadPatch.CompleteAfterVanillaLoad();

            Assert.Contains(
                warnings,
                log => log.StartsWith("Syndicate HQ storage remained inert at native load:", StringComparison.Ordinal));
            Assert.DoesNotContain(
                receipts,
                log => log.StartsWith("Syndicate HQ storage remained inert", StringComparison.Ordinal));
#if DEBUG
            Assert.Contains(
                receipts,
                log => log.StartsWith("Syndicate HQ storage receipt:", StringComparison.Ordinal));
#endif
        }
        finally
        {
            SyndicateHqStorageLoadPatch.Reset();
        }
    }

    private static void SetStaticField(string name, object? value)
    {
        var field = typeof(SyndicateHqStorageLoadPatch).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, $"Could not find static field: {name}");
        field!.SetValue(null, value);
    }

    private sealed class NoopRuntime : ISyndicateHqStorageRuntime
    {
        public SyndicateHqStorageResult CurrentResult { get; } =
            new(SyndicateHqStorageStatus.NotPrepared, "noop runtime for load-patch logging tests.");

        public SyndicateHqStorageResult BeginLoad(SyndicateHqStorageContext context) => CurrentResult;
        public SyndicateHqStorageResult PrepareAtVanillaLoadBoundary() => CurrentResult;
        public SyndicateHqStorageResult CompleteVanillaLoadBoundary() => CurrentResult;
        public SyndicateHqStorageResult PlaceAtPocket(SyndicateHqVector3 pocketRoot) => CurrentResult;
        public Release1HoldRoomSnapshot ReadHoldRoom() => Release1HoldRoomSnapshot.Unavailable();

        public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public void DisableInteractions() { }
        public void MarkTeardown() { }
        public void Dispose() { }
    }
}
