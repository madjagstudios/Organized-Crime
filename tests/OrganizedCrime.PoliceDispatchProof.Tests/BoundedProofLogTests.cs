using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class BoundedProofLogTests
{
    [Fact]
    public void Distinguishes_zero_from_unavailable()
    {
        var zero = ProofObservation<int>.Available(0);
        var unavailable = ProofObservation<int>.Unavailable("native getter threw");

        Assert.True(zero.IsAvailable);
        Assert.Equal(0, zero.Value);
        Assert.False(unavailable.IsAvailable);
        Assert.Equal(0, unavailable.Value);
        Assert.NotEqual(zero, unavailable);
    }

    [Fact]
    public void Enforces_duplicate_keys_row_cap_and_byte_cap_without_retry()
    {
        var log = new BoundedProofLog(maxRows: 2, maxBytes: 500);

        Assert.True(log.TryAppend(new DispatchProofLogRow("baseline", "one")));
        Assert.False(log.TryAppend(new DispatchProofLogRow("baseline", "duplicate")));
        Assert.True(log.TryAppend(new DispatchProofLogRow("response-1", "two")));
        Assert.False(log.TryAppend(new DispatchProofLogRow("response-2", "three")));
        Assert.True(log.Truncated);
        Assert.Equal(2, log.Rows.Count);
        Assert.Equal(2, log.AcceptedRowCount);
    }

    [Fact]
    public void Assigns_bounded_monotonic_sequence_numbers()
    {
        var log = new BoundedProofLog(maxRows: 3, maxBytes: 500);

        Assert.True(log.TryAppend(new DispatchProofLogRow("one", "one")));
        Assert.True(log.TryAppend(new DispatchProofLogRow("two", "two")));

        Assert.Equal(new long[] { 1, 2 }, log.Rows.Select(row => row.Sequence));
    }
}
