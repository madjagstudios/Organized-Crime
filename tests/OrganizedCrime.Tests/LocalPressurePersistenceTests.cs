using System.Collections.Concurrent;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressurePersistenceTests
{
    [Fact]
    public void Empty_v1_envelope_round_trips_deterministically()
    {
        var original = LocalPressureSaveEnvelope.CreateEmpty();

        Assert.True(LocalPressureSaveCodec.TrySerialize(original, out var json, out var serializeResult), serializeResult.Message);
        Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var decoded, out var deserializeResult), deserializeResult.Message);
        Assert.NotNull(decoded);
        Assert.Equal(LocalPressureSaveCodec.CurrentSchemaVersion, decoded.SchemaVersion);
        Assert.Empty(decoded.Players);
        Assert.True(LocalPressureSaveCodec.TrySerialize(decoded, out var roundTripJson, out var roundTripResult), roundTripResult.Message);
        Assert.Equal(json, roundTripJson);
    }

    [Fact]
    public void One_and_multiple_players_round_trip_every_supported_value()
    {
        var original = new LocalPressureSaveEnvelope(1, new[]
        {
            new LocalPressurePlayerRecord("player-1", 42, true, 100.0, 102.0, 101.0, "north", "safehouse", 7),
            new LocalPressurePlayerRecord("player-2", 0, false, null, null, null, null, null, 0)
        });

        Assert.True(LocalPressureSaveCodec.TrySerialize(original, out var json, out var serializeResult), serializeResult.Message);
        Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var decoded, out var deserializeResult), deserializeResult.Message);
        Assert.NotNull(decoded);
        Assert.Equal(original.Players, decoded.Players);
        Assert.Equal(json, Serialize(decoded));
    }

    [Fact]
    public void Serialization_orders_players_by_ordinal_player_id_and_fields_stably()
    {
        var envelope = new LocalPressureSaveEnvelope(1, new[]
        {
            DefaultRecord("zeta"),
            DefaultRecord("Alpha"),
            DefaultRecord("alpha")
        });

        var json = Serialize(envelope);

        Assert.True(json.IndexOf("\"playerId\": \"Alpha\"", StringComparison.Ordinal) <
                    json.IndexOf("\"playerId\": \"alpha\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"playerId\": \"alpha\"", StringComparison.Ordinal) <
                    json.IndexOf("\"playerId\": \"zeta\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"playerId\"", StringComparison.Ordinal) <
                    json.IndexOf("\"localHeat\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"localHeat\"", StringComparison.Ordinal) <
                    json.IndexOf("\"knownOffender\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_player_ids_are_rejected_using_ordinal_identity()
    {
        var json = "{\"schemaVersion\":1,\"players\":[{\"playerId\":\"same\",\"localHeat\":0,\"knownOffender\":false,\"lastEvidenceGameTime\":null,\"quietGraceUntil\":null,\"lastDecayEvaluation\":null,\"region\":null,\"property\":null,\"revision\":0},{\"playerId\":\"same\",\"localHeat\":0,\"knownOffender\":false,\"lastEvidenceGameTime\":null,\"quietGraceUntil\":null,\"lastDecayEvaluation\":null,\"region\":null,\"property\":null,\"revision\":0}]}";

        AssertCodecRejects(json, LocalPressureCodecFailureReason.DuplicatePlayerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_player_ids_are_rejected(string playerId)
    {
        var json = SerializeWithoutValidation(new LocalPressureSaveEnvelope(1, new[] { DefaultRecord(playerId) }));

        AssertCodecRejects(json, LocalPressureCodecFailureReason.InvalidPlayerId);
    }

    [Fact]
    public void Missing_player_id_is_rejected()
    {
        AssertCodecRejects(ReplacePlayerProperty(DefaultJson(), "playerId", null), LocalPressureCodecFailureReason.MissingRequiredField);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Heat_outside_zero_to_one_hundred_is_rejected(int heat)
    {
        var json = ReplacePlayerProperty(DefaultJson(), "localHeat", heat.ToString());

        AssertCodecRejects(json, LocalPressureCodecFailureReason.InvalidHeat);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"not-a-number\"")]
    [InlineData("NaN")]
    public void Invalid_game_times_are_rejected(string time)
    {
        var json = ReplacePlayerProperty(DefaultJson(), "lastEvidenceGameTime", time);

        Assert.False(LocalPressureSaveCodec.TryDeserialize(json, out _, out var result));
        Assert.Contains(result.Reason, new[]
        {
            LocalPressureCodecFailureReason.InvalidGameTime,
            LocalPressureCodecFailureReason.MalformedJson
        });
    }

    [Fact]
    public void Negative_revision_is_rejected()
    {
        AssertCodecRejects(ReplacePlayerProperty(DefaultJson(), "revision", "-1"), LocalPressureCodecFailureReason.InvalidRevision);
    }

    [Fact]
    public void Missing_required_fields_are_rejected()
    {
        var json = DefaultJson().Replace(",\"revision\":0", string.Empty, StringComparison.Ordinal);

        AssertCodecRejects(json, LocalPressureCodecFailureReason.MissingRequiredField);
    }

    [Fact]
    public void Invalid_json_types_are_rejected()
    {
        AssertCodecRejects(ReplacePlayerProperty(DefaultJson(), "knownOffender", "\"true\""), LocalPressureCodecFailureReason.InvalidJsonType);
    }

    [Fact]
    public void Unknown_schema_is_rejected_without_a_schema_v0_migration()
    {
        var json = DefaultJson().Replace("\"schemaVersion\":1", "\"schemaVersion\":0", StringComparison.Ordinal);

        AssertCodecRejects(json, LocalPressureCodecFailureReason.UnsupportedSchema);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"schemaVersion\":1,\"players\":[")]
    public void Malformed_or_truncated_json_is_rejected(string json)
    {
        AssertCodecRejects(json, LocalPressureCodecFailureReason.MalformedJson);
    }

    [Fact]
    public void Non_object_root_and_missing_players_are_rejected()
    {
        AssertCodecRejects("[]", LocalPressureCodecFailureReason.InvalidRoot);
        AssertCodecRejects("{\"schemaVersion\":1}", LocalPressureCodecFailureReason.MissingPlayers);
        AssertCodecRejects("{\"schemaVersion\":1,\"players\":null}", LocalPressureCodecFailureReason.MissingPlayers);
    }

    [Fact]
    public void Optional_context_round_trips_and_is_bounded()
    {
        var record = DefaultRecord("player") with { Region = "district-1", Property = "safehouse-1" };
        var decoded = Deserialize(new LocalPressureSaveEnvelope(1, new[] { record })).Players.Single();

        Assert.Equal("district-1", decoded.Region);
        Assert.Equal("safehouse-1", decoded.Property);
        AssertCodecRejects(ReplacePlayerProperty(DefaultJson(), "region", "\"   \""), LocalPressureCodecFailureReason.InvalidContext);
        AssertCodecRejects(ReplacePlayerProperty(DefaultJson(), "region", "\"" + new string('x', 65) + "\""), LocalPressureCodecFailureReason.InvalidContext);
    }

    [Fact]
    public void New_state_defaults_to_zero_heat_false_offender_and_null_context_times()
    {
        var record = LocalPressurePlayerRecord.CreateDefault("new-player");

        Assert.Equal(0, record.LocalHeat);
        Assert.False(record.KnownOffender);
        Assert.Null(record.LastEvidenceGameTime);
        Assert.Null(record.QuietGraceUntil);
        Assert.Null(record.LastDecayEvaluation);
        Assert.Null(record.Region);
        Assert.Null(record.Property);
        Assert.Equal(0, record.Revision);
    }

    [Fact]
    public void Codec_does_not_infer_known_offender_or_serialize_profile_tuning()
    {
        var record = DefaultRecord("player") with { KnownOffender = false };
        var json = Serialize(new LocalPressureSaveEnvelope(1, new[] { record }));

        Assert.DoesNotContain("wanted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pursuit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arrest", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quietGraceHours", json, StringComparison.Ordinal);
        Assert.False(Deserialize(new LocalPressureSaveEnvelope(1, new[] { record })).Players.Single().KnownOffender);
    }

    [Fact]
    public void Domain_state_maps_to_and_from_persistence_without_profile_tuning()
    {
        var state = new LocalPressureState(42, true, 100, 102, 101, "player", "north", "safehouse", 7);
        var record = LocalPressurePlayerRecord.FromState(state);
        var restored = record.ToState();

        Assert.Equal(state, restored);
    }

    [Fact]
    public void Higher_revision_replaces_one_player()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Assert.True(store.TryUpdate(DefaultRecord("player") with { LocalHeat = 10, Revision = 1 }, out var first), first.Message);
        Assert.True(store.TryUpdate(DefaultRecord("player") with { LocalHeat = 20, Revision = 2 }, out var second), second.Message);

        Assert.Equal(LocalPressureStoreUpdateStatus.Updated, second.Status);
        Assert.Equal(20, Load(store).Players.Single().LocalHeat);
    }

    [Fact]
    public void Lower_revision_is_rejected_as_stale()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Assert.True(store.TryUpdate(DefaultRecord("player") with { Revision = 2 }, out var first), first.Message);
        Assert.False(store.TryUpdate(DefaultRecord("player") with { LocalHeat = 20, Revision = 1 }, out var result));

        Assert.Equal(LocalPressureStoreFailureReason.StaleRevision, result.FailureReason);
        Assert.Equal(0, Load(store).Players.Single().LocalHeat);
    }

    [Fact]
    public void Equal_identical_revision_is_idempotent()
    {
        using var temp = TestDirectory.Create();
        var fileSystem = new RecordingFileSystem();
        var store = CreateStore(temp.Path, fileSystem);
        var record = DefaultRecord("player") with { LocalHeat = 8, Revision = 3 };
        Assert.True(store.TryUpdate(record, out var first), first.Message);
        var replacementCount = fileSystem.ReplacementCount;

        Assert.True(store.TryUpdate(record, out var result), result.Message);
        Assert.Equal(LocalPressureStoreUpdateStatus.Idempotent, result.Status);
        Assert.Equal(replacementCount, fileSystem.ReplacementCount);
    }

    [Fact]
    public void Equal_conflicting_revision_is_rejected()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Assert.True(store.TryUpdate(DefaultRecord("player") with { Revision = 4 }, out var first), first.Message);

        Assert.False(store.TryUpdate(DefaultRecord("player") with { LocalHeat = 99, Revision = 4 }, out var result));
        Assert.Equal(LocalPressureStoreFailureReason.RevisionConflict, result.FailureReason);
    }

    [Fact]
    public void Updating_one_player_preserves_all_other_players()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Assert.True(store.TryUpdate(DefaultRecord("one") with { LocalHeat = 10, Revision = 1 }, out var first), first.Message);
        Assert.True(store.TryUpdate(DefaultRecord("two") with { LocalHeat = 20, Revision = 1 }, out var second), second.Message);
        Assert.True(store.TryUpdate(DefaultRecord("one") with { LocalHeat = 30, Revision = 2 }, out var third), third.Message);

        var players = Load(store).Players;
        Assert.Equal(2, players.Count);
        Assert.Equal(30, players.Single(p => p.PlayerId == "one").LocalHeat);
        Assert.Equal(20, players.Single(p => p.PlayerId == "two").LocalHeat);
    }

    [Fact]
    public void Omitted_players_are_not_deleted()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Assert.True(store.TryUpdate(DefaultRecord("one") with { Revision = 1 }, out var first), first.Message);
        Assert.True(store.TryUpdate(DefaultRecord("two") with { Revision = 1 }, out var second), second.Message);
        Assert.True(store.TryUpdate(DefaultRecord("one") with { Revision = 2 }, out var third), third.Message);

        Assert.Contains(Load(store).Players, player => player.PlayerId == "two");
    }

    [Fact]
    public void Two_save_folders_resolve_to_isolated_sidecars()
    {
        using var first = TestDirectory.Create();
        using var second = TestDirectory.Create();
        Assert.True(LocalPressureSavePath.TryCreate(first.Path, out var firstPath, out var firstFailure), firstFailure.Message);
        Assert.True(LocalPressureSavePath.TryCreate(second.Path, out var secondPath, out var secondFailure), secondFailure.Message);

        Assert.NotEqual(firstPath!.SidecarFilePath, secondPath!.SidecarFilePath);
        Assert.StartsWith(first.Path, firstPath.SidecarFilePath, StringComparison.Ordinal);
        Assert.StartsWith(second.Path, secondPath.SidecarFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_active_folder_fails_safely_and_does_not_construct_a_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "oc20-missing", Guid.NewGuid().ToString("N"));

        Assert.False(LocalPressureSavePath.TryCreate(missing, out var path, out var result));
        Assert.Null(path);
        Assert.Equal(LocalPressureSavePathFailureReason.ActiveSaveFolderMissing, result.Reason);
    }

    [Fact]
    public void Missing_sidecar_loads_empty_without_writing_or_creating_directory()
    {
        using var temp = TestDirectory.Create();
        Assert.True(LocalPressureSavePath.TryCreate(temp.Path, out var path, out var pathFailure), pathFailure.Message);
        var store = new LocalPressureStateStore(path!);

        Assert.True(store.TryLoad(out var result), result.Message);
        Assert.Equal(LocalPressureStoreLoadStatus.Empty, result.Status);
        Assert.Empty(result.Envelope!.Players);
        Assert.False(Directory.Exists(path!.SidecarDirectory));
    }

    [Fact]
    public void Successful_write_creates_sidecar_and_reads_back()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        var record = DefaultRecord("player") with { LocalHeat = 42, KnownOffender = true, Revision = 7 };

        Assert.True(store.TryUpdate(record, out var writeResult), writeResult.Message);
        Assert.True(File.Exists(store.SavePath.SidecarFilePath));
        Assert.Equal(record, Load(store).Players.Single());
    }

    [Fact]
    public void Failed_validation_leaves_existing_target_byte_for_byte_unchanged()
    {
        using var temp = TestDirectory.Create();
        var fileSystem = new RecordingFileSystem();
        var store = CreateStore(temp.Path, fileSystem);
        Assert.True(store.TryUpdate(DefaultRecord("player") with { Revision = 1 }, out var first), first.Message);
        var before = File.ReadAllBytes(store.SavePath.SidecarFilePath);

        Assert.False(store.TryUpdate(DefaultRecord("player") with { LocalHeat = 101, Revision = 2 }, out var result));

        Assert.Equal(LocalPressureStoreFailureReason.InvalidIncomingRecord, result.FailureReason);
        Assert.Equal(before, File.ReadAllBytes(store.SavePath.SidecarFilePath));
        Assert.Equal(1, fileSystem.ReplacementCount);
    }

    [Fact]
    public void Failed_replacement_leaves_existing_target_byte_identical_and_cleans_own_temp()
    {
        using var temp = TestDirectory.Create();
        var fileSystem = new RecordingFileSystem { ThrowOnReplace = true };
        var store = CreateStore(temp.Path, fileSystem);
        var existing = DefaultRecord("player") with { Revision = 1 };
        Directory.CreateDirectory(store.SavePath.SidecarDirectory);
        File.WriteAllText(store.SavePath.SidecarFilePath, Serialize(new LocalPressureSaveEnvelope(1, new[] { existing })));
        var before = File.ReadAllBytes(store.SavePath.SidecarFilePath);

        Assert.False(store.TryUpdate(existing with { LocalHeat = 9, Revision = 2 }, out var result));

        Assert.Equal(LocalPressureStoreFailureReason.AtomicReplacementFailed, result.FailureReason);
        Assert.Equal(before, File.ReadAllBytes(store.SavePath.SidecarFilePath));
        Assert.All(fileSystem.TemporaryPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void Unsupported_schema_remains_untouched_on_disk()
    {
        using var temp = TestDirectory.Create();
        var store = CreateStore(temp.Path);
        Directory.CreateDirectory(store.SavePath.SidecarDirectory);
        const string unsupported = "{\"schemaVersion\":99,\"players\":[]}";
        File.WriteAllText(store.SavePath.SidecarFilePath, unsupported);

        Assert.False(store.TryUpdate(DefaultRecord("player"), out var result));

        Assert.Equal(LocalPressureStoreFailureReason.UnsupportedSchema, result.FailureReason);
        Assert.Equal(unsupported, File.ReadAllText(store.SavePath.SidecarFilePath));
    }

    [Fact]
    public void Temporary_cleanup_does_not_delete_unrelated_files()
    {
        using var temp = TestDirectory.Create();
        var fileSystem = new RecordingFileSystem { ThrowOnReplace = true };
        var store = CreateStore(temp.Path, fileSystem);
        Directory.CreateDirectory(store.SavePath.SidecarDirectory);
        var unrelated = Path.Combine(store.SavePath.SidecarDirectory, "unrelated.tmp");
        File.WriteAllText(unrelated, "keep");

        Assert.False(store.TryUpdate(DefaultRecord("player"), out _));

        Assert.True(File.Exists(unrelated));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public async Task Concurrent_store_calls_leave_a_complete_valid_json_envelope()
    {
        using var temp = TestDirectory.Create();
        Assert.True(LocalPressureSavePath.TryCreate(temp.Path, out var path, out var pathFailure), pathFailure.Message);
        var stores = Enumerable.Range(0, 8).Select(_ => new LocalPressureStateStore(path!)).ToArray();

        var results = await Task.WhenAll(stores.Select((store, index) => Task.Run(() =>
        {
            var record = DefaultRecord($"player-{index}") with { LocalHeat = index, Revision = 1 };
            return store.TryUpdate(record, out var result) ? result : throw new InvalidOperationException(result.Message);
        })));

        Assert.All(results, result => Assert.Equal(LocalPressureStoreUpdateStatus.Updated, result.Status));
        var json = File.ReadAllText(path!.SidecarFilePath);
        Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var envelope, out var decodeResult), decodeResult.Message);
        Assert.Equal(8, envelope!.Players.Count);
    }

    private static LocalPressureStateStore CreateStore(string activeFolder, ILocalPressureFileSystem? fileSystem = null)
    {
        Assert.True(LocalPressureSavePath.TryCreate(activeFolder, out var path, out var result), result.Message);
        return new LocalPressureStateStore(path!, fileSystem);
    }

    private static LocalPressurePlayerRecord DefaultRecord(string playerId) => LocalPressurePlayerRecord.CreateDefault(playerId);

    private static string SerializeWithoutValidation(LocalPressureSaveEnvelope envelope) =>
        $"{{\"schemaVersion\":1,\"players\":[{{\"playerId\":\"{envelope.Players[0].PlayerId}\",\"localHeat\":0,\"knownOffender\":false,\"lastEvidenceGameTime\":null,\"quietGraceUntil\":null,\"lastDecayEvaluation\":null,\"region\":null,\"property\":null,\"revision\":0}}]}}";

    private static string Serialize(LocalPressureSaveEnvelope envelope)
    {
        Assert.True(LocalPressureSaveCodec.TrySerialize(envelope, out var json, out var result), result.Message);
        return json;
    }

    private static string DefaultJson() =>
        "{\"schemaVersion\":1,\"players\":[{\"playerId\":\"player\",\"localHeat\":0,\"knownOffender\":false,\"lastEvidenceGameTime\":null,\"quietGraceUntil\":null,\"lastDecayEvaluation\":null,\"region\":null,\"property\":null,\"revision\":0}]}";

    private static string ReplacePlayerProperty(string json, string propertyName, string? value)
    {
        if (value is null)
            return json.Replace($"\"{propertyName}\":\"player\",", string.Empty, StringComparison.Ordinal);

        var original = propertyName switch
        {
            "localHeat" or "revision" => $"\"{propertyName}\":0",
            "knownOffender" => $"\"{propertyName}\":false",
            _ => $"\"{propertyName}\":null"
        };
        return json.Replace(original, $"\"{propertyName}\":{value}", StringComparison.Ordinal);
    }

    private static void AssertCodecRejects(string json, LocalPressureCodecFailureReason reason)
    {
        Assert.False(LocalPressureSaveCodec.TryDeserialize(json, out _, out var result));
        Assert.Equal(reason, result.Reason);
    }

    private static LocalPressureSaveEnvelope Deserialize(LocalPressureSaveEnvelope envelope)
    {
        var decoded = LocalPressureSaveCodec.TryDeserialize(Serialize(envelope), out var value, out var result);
        Assert.True(decoded, result.Message);
        return value!;
    }

    private static LocalPressureSaveEnvelope Load(LocalPressureStateStore store)
    {
        Assert.True(store.TryLoad(out var result), result.Message);
        return result.Envelope!;
    }

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string path) => Path = path;

        public string Path { get; }

        public static TestDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrganizedCrimeTests", "LocalPressure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TestDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingFileSystem : ILocalPressureFileSystem
    {
        public bool ThrowOnReplace { get; init; }
        public int ReplacementCount { get; private set; }
        public ConcurrentBag<string> TemporaryPaths { get; } = new();

        public bool FileExists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public string CreateTemporaryPath(string directory, string targetPath)
        {
            var path = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            TemporaryPaths.Add(path);
            return path;
        }

        public void WriteAllTextAndFlush(string path, string contents)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceAtomically(string temporaryPath, string targetPath)
        {
            ReplacementCount++;
            if (ThrowOnReplace)
                throw new IOException("simulated replacement failure");
            if (File.Exists(targetPath))
                File.Replace(temporaryPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, targetPath);
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
