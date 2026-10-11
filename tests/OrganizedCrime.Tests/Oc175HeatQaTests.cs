using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc175HeatQaTests
{
    [Theory]
    [InlineData(LocalPressureTier.Noticed, 37)]
    [InlineData(LocalPressureTier.Watched, 62)]
    [InlineData(LocalPressureTier.Critical, 87)]
    public void Requested_tier_survives_the_next_short_decay_without_new_evidence(LocalPressureTier tier, int midpoint)
    {
        var old = new LocalPressureState(65, true, 1d, 2d, 3d, "player-1", "north", "home", 7)
        { LastAppliedRaidCoolingId = "previous-raid" };
        var repo = new Repository(old);
        using var host = new Host { Minutes = 180 };
        using var pressure = new LocalPressureRuntimeService(host, _ => repo);
        Load(pressure);
        // Move the host clock far ahead without a decay boundary: QA must reset
        // catch-up from the fresh clock read rather than the accepted baseline.
        host.Minutes = 1800;
        Assert.True(pressure.QaSetTier(tier).Ok);
        Assert.True(pressure.TryGetState("player-1", out var set));
        Assert.Equal(midpoint, set!.LocalHeat);
        Assert.Equal(30d, set.LastDecayEvaluation);
        Assert.Equal(old.QuietGraceUntil, set.QuietGraceUntil);
        Assert.Equal(old.LastEvidenceGameTime, set.LastEvidenceGameTime);
        Assert.Equal(old.KnownOffender, set.KnownOffender);
        Assert.Equal(old.LastAppliedRaidCoolingId, set.LastAppliedRaidCoolingId);
        Assert.Equal(old.Revision + 1, set.Revision);
        host.Minutes += 6;
        pressure.OnClockBoundary(LocalPressureClockBoundary.Hour);
        Assert.True(pressure.TryGetState("player-1", out var evaluated));
        Assert.Equal(tier, LocalPressureTransitions.GetTier(evaluated!.LocalHeat, LocalPressureProfile.Moderate));
        Assert.Equal(0, repo.Writes);
    }

    internal static void Load(LocalPressureRuntimeService pressure)
    {
        pressure.OnPreLoad(); pressure.OnLoadComplete();
        pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.Equal(LocalPressureRuntimePhase.Active, pressure.Phase);
    }

    internal sealed class Host : ILocalPressureRuntimeHostAdapter
    {
        internal long Minutes;
        internal LocalPressureClockReadStatus ClockStatus = LocalPressureClockReadStatus.Ready;
        public string ActiveSaveFolder => @"C:\saves\oc175-qa";
        public string CanonicalHostIdentity => "player-1";
        public event Action? PreLoad { add { } remove { } }
        public event Action? LoadComplete { add { } remove { } }
        public event Action? SaveStart { add { } remove { } }
        public event Action? SaveComplete { add { } remove { } }
        public event Action<LocalPressureClockBoundary>? ClockBoundary { add { } remove { } }
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => LocalPressureHostAuthorityReadStatus.Ready;
        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() => LocalPressureClockBoundaryBindingStatus.Ready;
        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = new(Minutes, (int)(Minutes / 1440L), 700, null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
            return ClockStatus;
        }
        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = new[] { new LocalPressurePlayerSample("player-1", true, true, "player-1", "north", "home") };
            return LocalPressurePlayerReadStatus.Ready;
        }
        public void Dispose() { }
    }

    internal sealed class Repository : ILocalPressureStateRepository
    {
        private LocalPressureSaveEnvelope _envelope;
        internal int Writes;
        internal Repository(LocalPressureState state) => _envelope = new(1, new[] { LocalPressurePlayerRecord.FromState(state) });
        public LocalPressureStoreLoadResult Load() => new(true, LocalPressureStoreLoadStatus.Loaded,
            _envelope, LocalPressureStoreFailureReason.None, "loaded");
        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            Writes++;
            _envelope = new(1, new[] { record });
            return new(true, LocalPressureStoreUpdateStatus.Updated, null, LocalPressureStoreFailureReason.None, "stored");
        }
    }
}