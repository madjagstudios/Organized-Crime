using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StorageRunTests
{
    [Fact]
    public void Storage_accept_uses_three_day_deadline()
    {
        var board = Chapter3JobBoard.Empty with
        {
            OpenOffer = new Chapter3JobOffer(1, Chapter3JobType.TemporaryStorageSafehouse, 16_000, 0, 1)
        };
        var accepted = Chapter3JobScheduler.Accept(board, 1, 1).Board.ActiveJob!;
        Assert.Equal(4, accepted.DeadlineDay);
    }

    [Fact]
    public void Storage_schema_is_seventeen()
    {
        Assert.Equal(20, Chapter3SaveCodec.CurrentSchemaVersion);
    }
}
