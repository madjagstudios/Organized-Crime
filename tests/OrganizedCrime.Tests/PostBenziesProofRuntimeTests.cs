using OrganizedCrime.PostBenziesProof;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class PostBenziesProofRuntimeTests
{
    private static readonly Release1StoryHostContextSnapshot SaveFiveHost = new(
        Guid.Parse("8f6bcfcb-f269-4fa2-96f3-d4c2507c904e"),
        1,
        "76561190000000001",
        @"C:\Saves\76561190000000001\SaveGame_5");

    [Fact]
    public void Pending_load_can_later_pass_once_for_defeated_save_five()
    {
        // Catches a proof runtime that treats early load timing as final or repeats a successful read.
        var now = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        var reader = new SequenceReader(
            Read(Release1PostBenziesUnlockReadStatus.Pending),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked, Release1CartelStatus.Defeated, SaveFiveHost));
        var receipts = new List<string>();
        var runtime = new PostBenziesProofRuntime(reader, "SaveGame_5", receipts.Add, () => now);

        runtime.Initialize();
        runtime.Update();
        Assert.Equal(PostBenziesProofClassification.Inconclusive, runtime.Evidence.Classification);

        now = now.AddSeconds(1);
        runtime.Update();
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Pass, runtime.Evidence.Classification);
        Assert.Equal(2, runtime.Evidence.ReadAttempts);
        Assert.Equal("SaveGame_5", runtime.Evidence.SaveName);
        Assert.Equal(Release1CartelStatus.Defeated, runtime.Evidence.CartelStatus);
        Assert.False(runtime.Evidence.NaturalTransitionProven);
        Assert.Single(receipts, receipt => receipt.Contains("\"Classification\":\"Pass\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Main_menu_does_not_read_until_save_five_is_active()
    {
        // Catches the main-menu host state stopping the proof before the requested save is loaded.
        var now = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        string? activeSaveFolder = null;
        var reader = new SequenceReader(Read(
            Release1PostBenziesUnlockReadStatus.Unlocked,
            Release1CartelStatus.Defeated,
            SaveFiveHost));
        var runtime = new PostBenziesProofRuntime(
            reader,
            "SaveGame_5",
            _ => { },
            () => now,
            activeSaveFolderProvider: () => activeSaveFolder);

        runtime.Initialize();
        runtime.Update();
        Assert.Equal(0, reader.ReadCount);
        Assert.Equal(PostBenziesProofClassification.Inconclusive, runtime.Evidence.Classification);

        activeSaveFolder = SaveFiveHost.ActiveSaveFolder;
        now = now.AddSeconds(1);
        runtime.Update();

        Assert.Equal(1, reader.ReadCount);
        Assert.Equal(PostBenziesProofClassification.Pass, runtime.Evidence.Classification);
    }

    [Fact]
    public void Transient_not_authoritative_during_host_startup_can_later_pass()
    {
        // Catches the proof finalizing before FishNet's host becomes authoritative during a normal load.
        var now = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        var reader = new SequenceReader(
            Read(Release1PostBenziesUnlockReadStatus.NotAuthoritative),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked, Release1CartelStatus.Defeated, SaveFiveHost));
        var runtime = new PostBenziesProofRuntime(reader, "SaveGame_5", _ => { }, () => now);

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(PostBenziesProofState.AwaitingRead, runtime.State);
        Assert.Equal(PostBenziesProofClassification.Inconclusive, runtime.Evidence.Classification);
        Assert.Equal(Release1PostBenziesUnlockReadStatus.NotAuthoritative, runtime.Evidence.ReadStatus);

        now = now.AddSeconds(1);
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Pass, runtime.Evidence.Classification);
        Assert.Equal(2, runtime.Evidence.ReadAttempts);
    }

    [Fact]
    public void Different_loaded_save_stops_before_reading_cartel_state()
    {
        // Catches a wrong save slot being sampled and mislabelled as the Save 5 control.
        var reader = new SequenceReader(Read(
            Release1PostBenziesUnlockReadStatus.Unlocked,
            Release1CartelStatus.Defeated,
            SaveFiveHost));
        var runtime = new PostBenziesProofRuntime(
            reader,
            "SaveGame_5",
            _ => { },
            activeSaveFolderProvider: () => @"C:\Saves\76561190000000001\SaveGame_4");

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(0, reader.ReadCount);
        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Contains("SaveGame_5", runtime.Evidence.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unlocked_result_from_an_unexpected_save_is_a_stop()
    {
        // Catches an accidental PASS after the owner loads a different canonical save slot.
        var saveFour = SaveFiveHost with { ActiveSaveFolder = @"C:\Saves\76561190000000001\SaveGame_4" };
        var runtime = Create(Read(
            Release1PostBenziesUnlockReadStatus.Unlocked,
            Release1CartelStatus.Defeated,
            saveFour));

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Contains("SaveGame_5", runtime.Evidence.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unlocked_result_without_defeated_status_is_a_stop()
    {
        // Catches a mismatched reader receipt being promoted to PASS.
        var runtime = Create(Read(
            Release1PostBenziesUnlockReadStatus.Unlocked,
            Release1CartelStatus.Hostile,
            SaveFiveHost));

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Contains("Defeated", runtime.Evidence.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Locked_save_is_a_stop_for_the_post_benzies_control()
    {
        // Catches a pre-defeat runtime value being reported as persistence PASS.
        var runtime = Create(Read(
            Release1PostBenziesUnlockReadStatus.Locked,
            Release1CartelStatus.Unknown,
            SaveFiveHost));

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Equal(Release1PostBenziesUnlockReadStatus.Locked, runtime.Evidence.ReadStatus);
    }

    [Theory]
    [InlineData(Release1PostBenziesUnlockReadStatus.UnsupportedMultiplayer)]
    [InlineData(Release1PostBenziesUnlockReadStatus.AmbiguousIdentity)]
    [InlineData(Release1PostBenziesUnlockReadStatus.Faulted)]
    public void Authority_identity_and_fault_failures_stop_the_proof(
        Release1PostBenziesUnlockReadStatus readStatus)
    {
        // Catches an invalid authority or identity boundary being softened into INCONCLUSIVE/PASS.
        var runtime = Create(Read(readStatus));

        runtime.Initialize();
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Equal(readStatus, runtime.Evidence.ReadStatus);
    }

    [Fact]
    public void Host_that_never_becomes_authoritative_stops_at_the_bounded_deadline()
    {
        // Catches a genuine client-only session being softened into an inconclusive readiness timeout.
        var now = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        var runtime = new PostBenziesProofRuntime(
            new SequenceReader(Read(Release1PostBenziesUnlockReadStatus.NotAuthoritative)),
            "SaveGame_5",
            _ => { },
            () => now,
            deadline: TimeSpan.FromSeconds(10));

        runtime.Initialize();
        runtime.Update();
        now = now.AddSeconds(10);
        runtime.Update();

        Assert.Equal(PostBenziesProofState.Complete, runtime.State);
        Assert.Equal(PostBenziesProofClassification.Stop, runtime.Evidence.Classification);
        Assert.Equal(Release1PostBenziesUnlockReadStatus.NotAuthoritative, runtime.Evidence.ReadStatus);
    }

    [Fact]
    public void Deadline_is_inconclusive_and_never_claims_natural_transition()
    {
        // Catches an unavailable load being promoted to PASS after the bounded deadline.
        var now = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        var reader = new SequenceReader(Read(Release1PostBenziesUnlockReadStatus.Pending));
        var runtime = new PostBenziesProofRuntime(
            reader,
            "SaveGame_5",
            _ => { },
            () => now,
            deadline: TimeSpan.FromSeconds(10));

        runtime.Initialize();
        runtime.Update();
        now = now.AddSeconds(10);
        runtime.Update();

        Assert.Equal(PostBenziesProofClassification.Inconclusive, runtime.Evidence.Classification);
        Assert.Equal(PostBenziesProofState.Complete, runtime.State);
        Assert.False(runtime.Evidence.NaturalTransitionProven);
    }

    [Fact]
    public void Disposal_before_a_terminal_read_is_inconclusive_and_prevents_late_reads()
    {
        // Catches teardown allowing a late read or silently claiming completion.
        var reader = new SequenceReader(Read(Release1PostBenziesUnlockReadStatus.Pending));
        var runtime = new PostBenziesProofRuntime(reader, "SaveGame_5", _ => { });

        runtime.Initialize();
        runtime.Dispose();
        runtime.Update();

        Assert.Equal(PostBenziesProofState.Disposed, runtime.State);
        Assert.Equal(PostBenziesProofClassification.Inconclusive, runtime.Evidence.Classification);
        Assert.Equal(0, reader.ReadCount);
    }

    private static PostBenziesProofRuntime Create(ProofRead read) => new(
        new SequenceReader(read),
        "SaveGame_5",
        _ => { });

    private static ProofRead Read(
        Release1PostBenziesUnlockReadStatus status,
        Release1CartelStatus cartelStatus = default,
        Release1StoryHostContextSnapshot host = default) =>
        new(status, new Release1PostBenziesUnlockSnapshot(host, cartelStatus));

    private readonly record struct ProofRead(
        Release1PostBenziesUnlockReadStatus Status,
        Release1PostBenziesUnlockSnapshot Snapshot);

    private sealed class SequenceReader : IRelease1PostBenziesUnlockReader
    {
        private readonly Queue<ProofRead> _reads;
        private ProofRead _last;

        public SequenceReader(params ProofRead[] reads)
        {
            _reads = new Queue<ProofRead>(reads);
            _last = reads.Length == 0 ? Read(Release1PostBenziesUnlockReadStatus.Pending) : reads[^1];
        }

        public int ReadCount { get; private set; }

        public Release1PostBenziesUnlockReadStatus TryRead(out Release1PostBenziesUnlockSnapshot snapshot)
        {
            ReadCount++;
            if (_reads.Count > 0)
                _last = _reads.Dequeue();
            snapshot = _last.Snapshot;
            return _last.Status;
        }
    }
}
