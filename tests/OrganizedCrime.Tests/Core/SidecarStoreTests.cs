using OrganizedCrime.Core.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148: unit tests for the shared SidecarStore mechanism. Each service keeps
// its typed results/messages, but the load/update/preserve skeleton, the lock
// table, the atomic write and both exception policies live here exactly once.
public sealed class SidecarStoreTests
{
    private sealed record TestEnvelope(int Revision, string PlayerId, string Data);

    [Fact]
    public void Load_missing_returns_empty_envelope()
    {
        var store = CreateStore(out _);

        Assert.True(store.TryLoad(out var load));

        Assert.Equal(SidecarLoadStatus.Empty, load.Status);
        Assert.Equal(new TestEnvelope(-1, string.Empty, string.Empty), load.Envelope);
        Assert.Equal(SidecarIoStage.None, load.IoFailure.Stage);
    }

    [Fact]
    public void Update_then_load_round_trips()
    {
        var store = CreateStore(out _);
        var incoming = new TestEnvelope(2, "player-1", "hello");

        Assert.True(store.TryUpdate(incoming, out var updated));
        Assert.Equal(SidecarUpdateStatus.Updated, updated.Status);
        Assert.Equal(incoming, updated.Envelope);
        Assert.True(store.TryLoad(out var load));
        Assert.Equal(SidecarLoadStatus.Loaded, load.Status);
        Assert.Equal(incoming, load.Envelope);
    }

    [Fact]
    public void Deserialize_failure_returns_raw_contents()
    {
        const string garbage = "{ not an envelope";
        var store = CreateStore(out var files);
        files.Seed(store.Path.SidecarFilePath, garbage);

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(SidecarLoadStatus.Failed, load.Status);
        Assert.Equal("BadJson", load.FailureKind);
        Assert.Equal(garbage, load.RawContents);
        Assert.Equal(SidecarIoStage.None, load.IoFailure.Stage);
    }

    [Fact]
    public void Read_throw_maps_to_io_read_failure()
    {
        var store = CreateStore(out var files);
        files.Seed(store.Path.SidecarFilePath, Encode(new TestEnvelope(1, "player-1", "x")));
        files.ThrowOnRead = true;

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(SidecarLoadStatus.Failed, load.Status);
        Assert.Equal(SidecarIoStage.Read, load.IoFailure.Stage);
        Assert.Equal("read boom", load.IoFailure.Detail);
    }

    [Fact]
    public void FileExists_throw_is_caught_by_the_catch_all_policy()
    {
        var store = CreateStore(out var files);
        files.ThrowOnExists = true;

        Assert.False(store.TryLoad(out var load));

        Assert.Equal(SidecarLoadStatus.Failed, load.Status);
        Assert.Equal(SidecarIoStage.Read, load.IoFailure.Stage);
        Assert.Equal("exists boom", load.IoFailure.Detail);
    }

    [Fact]
    public void Validation_failure_rejects_before_reading_current()
    {
        var store = CreateStore(out var files);
        files.ReadCalls = 0;

        Assert.False(store.TryUpdate(new TestEnvelope(1, "player-1", "bad"), out var rejected));

        Assert.Equal(SidecarUpdateStatus.Rejected, rejected.Status);
        Assert.Equal("InvalidData", rejected.FailureKind);
        Assert.Equal(0, files.ReadCalls);
        Assert.Equal(0, files.WriteCalls);
    }

    [Fact]
    public void Load_failure_inside_update_rejects_with_the_same_outcome()
    {
        const string garbage = "{ not an envelope";
        var store = CreateStore(out var files);
        files.Seed(store.Path.SidecarFilePath, garbage);

        Assert.False(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out var rejected));

        Assert.Equal(SidecarUpdateStatus.Rejected, rejected.Status);
        Assert.Equal("BadJson", rejected.FailureKind);
        Assert.Equal("decode boom: " + garbage, rejected.Message);
    }

    [Fact]
    public void Stale_and_conflicting_updates_reject()
    {
        var store = CreateStore(out _);
        Assert.True(store.TryUpdate(new TestEnvelope(2, "player-1", "first"), out _));

        Assert.False(store.TryUpdate(new TestEnvelope(1, "player-1", "stale"), out var stale));
        Assert.Equal("StaleRevision", stale.FailureKind);

        Assert.False(store.TryUpdate(new TestEnvelope(2, "player-1", "rival"), out var conflict));
        Assert.Equal("RevisionConflict", conflict.FailureKind);

        Assert.False(store.TryUpdate(new TestEnvelope(3, "player-2", "other"), out var identity));
        Assert.Equal("IdentityConflict", identity.FailureKind);
    }

    [Fact]
    public void Idempotent_update_returns_current_without_writing()
    {
        var store = CreateStore(out var files);
        var first = new TestEnvelope(2, "player-1", "same");
        Assert.True(store.TryUpdate(first, out _));
        files.WriteCalls = 0;

        Assert.True(store.TryUpdate(first, out var idempotent));

        Assert.Equal(SidecarUpdateStatus.Idempotent, idempotent.Status);
        Assert.Equal(first, idempotent.Envelope);
        Assert.Equal(0, files.WriteCalls);
    }

    [Fact]
    public void Serialize_for_write_failure_rejects_without_writing()
    {
        var failOnWriteEncode = new Flag();
        var store = CreateStore(out var files, writeEncodeFailFlag: failOnWriteEncode);
        Assert.True(store.TryUpdate(new TestEnvelope(1, "player-1", "seed"), out _));
        files.WriteCalls = 0;
        failOnWriteEncode.Value = true;

        Assert.False(store.TryUpdate(new TestEnvelope(2, "player-1", "next"), out var rejected));

        Assert.Equal(SidecarUpdateStatus.Rejected, rejected.Status);
        Assert.Equal("EncodeFailed", rejected.FailureKind);
        Assert.Equal(0, files.WriteCalls);
    }

    [Theory]
    [InlineData(SidecarIoStage.CreateDirectory)]
    [InlineData(SidecarIoStage.CreateTemporaryPath)]
    [InlineData(SidecarIoStage.WriteTemporary)]
    [InlineData(SidecarIoStage.ReplaceAtomically)]
    public void Write_stage_failures_map_per_stage_and_clean_the_temp_file(SidecarIoStage stage)
    {
        var store = CreateStore(out var files);
        files.FailStage = stage;

        Assert.False(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out var rejected));

        Assert.Equal(SidecarUpdateStatus.Rejected, rejected.Status);
        Assert.Equal(stage, rejected.IoFailure.Stage);
        Assert.Contains("boom", rejected.IoFailure.Detail, StringComparison.Ordinal);
        Assert.Equal(stage is SidecarIoStage.WriteTemporary or SidecarIoStage.ReplaceAtomically, files.DeleteCalls >= 1);
        Assert.False(store.TryLoad(out var load) && load.Status == SidecarLoadStatus.Loaded);
    }

    [Fact]
    public void Catch_all_success_leaves_no_temp_cleanup_behind()
    {
        var store = CreateStore(out var files);

        Assert.True(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out _));

        Assert.Equal(0, files.DeleteCalls);
        Assert.DoesNotContain(files.CallLog, call => call.StartsWith("delete", StringComparison.Ordinal));
    }

    [Fact]
    public void Preserve_raw_contents_writes_bytes_verbatim()
    {
        const string garbage = "{ quarantined bytes";
        var store = CreateStore(out var files);

        Assert.True(store.TryPreserveRawContents(garbage, out var preserved));

        Assert.Equal(SidecarUpdateStatus.Updated, preserved.Status);
        Assert.Null(preserved.Envelope);
        Assert.Equal(garbage, files.Seeded(store.Path.SidecarFilePath));
    }

    [Fact]
    public void Validation_runs_once_per_update()
    {
        var validations = 0;
        var store = CreateStore(out _, onValidate: () => validations++);

        Assert.True(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out _));

        Assert.Equal(1, validations);
    }

    [Fact]
    public void Locks_are_shared_per_path()
    {
        Assert.Same(SidecarLocks.For("C:\\saves\\a.json"), SidecarLocks.For("c:\\SAVES\\a.json"));
        Assert.NotSame(SidecarLocks.For("C:\\saves\\a.json"), SidecarLocks.For("C:\\saves\\b.json"));
    }

    [Fact]
    public void Storage_failures_file_exists_throw_propagates()
    {
        var store = CreateStore(out var files, SidecarExceptionPolicy.StorageFailures);
        files.ThrowOnExists = true;

        Assert.Throws<ArgumentException>(() => store.TryLoad(out _));
    }

    [Fact]
    public void Storage_failures_read_throw_maps_filtered_and_propagates_the_rest()
    {
        var store = CreateStore(out var files, SidecarExceptionPolicy.StorageFailures);
        files.Seed(store.Path.SidecarFilePath, Encode(new TestEnvelope(1, "player-1", "x")));

        files.ThrowOnReadFiltered = true;
        Assert.False(store.TryLoad(out var mapped));
        Assert.Equal(SidecarIoStage.Read, mapped.IoFailure.Stage);

        files.ThrowOnReadFiltered = false;
        files.ThrowOnRead = true;
        Assert.Throws<ArgumentException>(() => store.TryLoad(out _));
    }

    [Fact]
    public void Storage_failures_temp_path_throw_maps_filtered_and_propagates_the_rest()
    {
        var filtered = CreateStore(out var filteredFiles, SidecarExceptionPolicy.StorageFailures);
        filteredFiles.FailTempPathFiltered = true;
        Assert.False(filtered.TryUpdate(new TestEnvelope(1, "player-1", "x"), out var mapped));
        Assert.Equal(SidecarIoStage.CreateTemporaryPath, mapped.IoFailure.Stage);

        var unfiltered = CreateStore(out var unfilteredFiles, SidecarExceptionPolicy.StorageFailures);
        unfilteredFiles.FailStage = SidecarIoStage.CreateTemporaryPath;
        Assert.Throws<ArgumentException>(() => unfiltered.TryUpdate(new TestEnvelope(1, "player-1", "x"), out _));
    }

    [Fact]
    public void Storage_failures_cleanup_throw_is_filtered()
    {
        var store = CreateStore(out var files, SidecarExceptionPolicy.StorageFailures);
        files.FailWriteFiltered = true;
        files.ThrowOnDeleteFiltered = true;

        Assert.False(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out var rejected));

        Assert.Equal(SidecarIoStage.WriteTemporary, rejected.IoFailure.Stage);

        files.ThrowOnDeleteFiltered = false;
        files.ThrowOnDelete = true;
        Assert.Throws<ArgumentException>(() => store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out _));
    }

    [Fact]
    public void Storage_failures_deserialize_throw_propagates()
    {
        var store = CreateStore(out var files, SidecarExceptionPolicy.StorageFailures, throwOnDecode: true);
        files.Seed(store.Path.SidecarFilePath, "whatever");

        Assert.Throws<ArgumentException>(() => store.TryLoad(out _));
    }

    [Fact]
    public void Storage_failures_success_still_runs_temp_cleanup()
    {
        var store = CreateStore(out var files, SidecarExceptionPolicy.StorageFailures);

        Assert.True(store.TryUpdate(new TestEnvelope(1, "player-1", "x"), out _));

        Assert.True(files.DeleteCalls >= 1);
    }

    [Fact]
    public void Constructor_rejects_null_delegates()
    {
        Assert.True(SidecarPath.TryValidate(Path.GetTempPath(), "test.json", SidecarPathWording.Short, out var path, out _));
        var fileSystem = new FakeFileSystem();

        Assert.Throws<ArgumentNullException>(() => new SidecarStore<TestEnvelope>(null!, fileSystem, new SidecarStoreOptions<TestEnvelope>()));
        Assert.Throws<ArgumentNullException>(() => new SidecarStore<TestEnvelope>(path!, null!, new SidecarStoreOptions<TestEnvelope>()));
        Assert.Throws<ArgumentNullException>(() => new SidecarStore<TestEnvelope>(path!, fileSystem, null!));
        Assert.Throws<ArgumentNullException>(() => new SidecarStore<TestEnvelope>(path!, fileSystem, new SidecarStoreOptions<TestEnvelope>()));
    }

    private sealed class Flag { public bool Value; }

    private static string Encode(TestEnvelope envelope) => $"{envelope.Revision}|{envelope.PlayerId}|{envelope.Data}";

    private static SidecarStore<TestEnvelope> CreateStore(
        out FakeFileSystem files,
        SidecarExceptionPolicy policy = SidecarExceptionPolicy.CatchAll,
        Flag? writeEncodeFailFlag = null,
        bool throwOnDecode = false,
        Action? onValidate = null)
    {
        files = new FakeFileSystem();
        Assert.True(SidecarPath.TryValidate(Path.GetTempPath(), $"oc-148-{Guid.NewGuid():N}.json", SidecarPathWording.Short, out var path, out _));
        string? cachedJson = null;
        return new SidecarStore<TestEnvelope>(
            path!,
            files,
            new SidecarStoreOptions<TestEnvelope>
            {
                ExceptionPolicy = policy,
                CreateEmpty = () => new TestEnvelope(-1, string.Empty, string.Empty),
                Deserialize = json =>
                {
                    if (throwOnDecode) throw new ArgumentException("decode boom");
                    var parts = json.Split('|');
                    return parts.Length == 3 && int.TryParse(parts[0], out var revision)
                        ? SidecarDecodeOutcome<TestEnvelope>.Succeeded(new TestEnvelope(revision, parts[1], parts[2]))
                        : SidecarDecodeOutcome<TestEnvelope>.Failed("BadJson", "decode boom: " + json);
                },
                SerializeForValidation = envelope =>
                {
                    onValidate?.Invoke();
                    if (envelope.Data == "bad")
                        return SidecarEncodeOutcome.Failed("InvalidData", "data was bad");
                    cachedJson = Encode(envelope);
                    return SidecarEncodeOutcome.Succeeded(cachedJson);
                },
                MergeCurrent = (current, incoming) =>
                {
                    if (incoming.PlayerId != current.PlayerId && current.Revision >= 0 && incoming.Revision >= 0 && current.PlayerId.Length > 0)
                        return SidecarMergeOutcome<TestEnvelope>.Reject("IdentityConflict", "player conflict");
                    if (incoming.Revision < current.Revision)
                        return SidecarMergeOutcome<TestEnvelope>.Reject("StaleRevision", "stale");
                    if (incoming.Revision == current.Revision)
                        return incoming == current
                            ? SidecarMergeOutcome<TestEnvelope>.ReplayCurrent(current)
                            : SidecarMergeOutcome<TestEnvelope>.Reject("RevisionConflict", "conflict");
                    return SidecarMergeOutcome<TestEnvelope>.ProceedWith(incoming);
                },
                SerializeForWrite = _ => writeEncodeFailFlag?.Value == true
                    ? SidecarEncodeOutcome.Failed("EncodeFailed", "write encode boom")
                    : SidecarEncodeOutcome.Succeeded(cachedJson!),
            });
    }

    private sealed class FakeFileSystem : ISidecarFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOnExists { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnReadFiltered { get; set; }
        public bool ThrowOnDelete { get; set; }
        public bool ThrowOnDeleteFiltered { get; set; }
        public bool FailTempPathFiltered { get; set; }
        public bool FailWriteFiltered { get; set; }
        public SidecarIoStage? FailStage { get; set; }
        public int ReadCalls { get; set; }
        public int WriteCalls { get; set; }
        public int DeleteCalls { get; set; }
        public List<string> CallLog { get; } = new();

        public void Seed(string path, string contents) => _files[path] = contents;
        public string? Seeded(string path) => _files.TryGetValue(path, out var contents) ? contents : null;

        public bool FileExists(string path)
        {
            if (ThrowOnExists) throw new ArgumentException("exists boom");
            return _files.ContainsKey(path);
        }

        public string ReadAllText(string path)
        {
            ReadCalls++;
            if (ThrowOnRead) throw new ArgumentException("read boom");
            if (ThrowOnReadFiltered) throw new IOException("filtered read boom");
            return _files[path];
        }

        public void CreateDirectory(string path)
        {
            CallLog.Add("create-directory");
            if (FailStage == SidecarIoStage.CreateDirectory) throw new ArgumentException("create-directory boom");
        }

        public string CreateTemporaryPath(string directory, string targetPath)
        {
            CallLog.Add("create-temp");
            if (FailTempPathFiltered) throw new IOException("filtered temp boom");
            if (FailStage == SidecarIoStage.CreateTemporaryPath) throw new ArgumentException("temp boom");
            return targetPath + ".tmp";
        }

        public void WriteAllTextAndFlush(string path, string contents)
        {
            WriteCalls++;
            CallLog.Add("write");
            if (FailWriteFiltered) throw new IOException("filtered write boom");
            if (FailStage == SidecarIoStage.WriteTemporary) throw new ArgumentException("write boom");
            _files[path] = contents;
        }

        public void ReplaceAtomically(string temporaryPath, string targetPath)
        {
            CallLog.Add("replace");
            if (FailStage == SidecarIoStage.ReplaceAtomically) throw new ArgumentException("replace boom");
            _files[targetPath] = _files[temporaryPath];
            _files.Remove(temporaryPath);
        }

        public void DeleteFile(string path)
        {
            DeleteCalls++;
            CallLog.Add("delete");
            if (ThrowOnDelete) throw new ArgumentException("delete boom");
            if (ThrowOnDeleteFiltered) throw new IOException("filtered delete boom");
            _files.Remove(path);
        }
    }
}
