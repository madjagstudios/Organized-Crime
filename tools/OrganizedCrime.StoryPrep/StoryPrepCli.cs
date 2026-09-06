using System.Text;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.StoryPrep;

/// <summary>
/// Parses arguments, reads and backs up the existing sidecar, builds the prepared story with
/// StoryPrepBuilder, writes it to the sidecar path with the same UTF-8-without-BOM encoding
/// Release1StoryStateStore's DefaultFileSystem uses, and prints a summary read back through the
/// codec. Kept separate from Program.cs so it can also be exercised directly if needed; Program.cs
/// stays a thin wrapper that just forwards args and the exit code.
/// </summary>
internal static class StoryPrepCli
{
    public static int Run(string[] args)
    {
        Console.WriteLine("Organized Crime story prep tool");
        Console.WriteLine("================================");
        Console.WriteLine("Before running this: close Schedule I completely, and make sure Steam Cloud");
        Console.WriteLine("saves are OFF for Schedule I. This tool writes the sidecar file directly on");
        Console.WriteLine("disk; a running game or a cloud sync can overwrite or restore over that write.");
        Console.WriteLine();

        string? sidecarPath = null;
        string? throughKey = null;
        string? playerOverride = null;
        var list = false;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--sidecar":
                        sidecarPath = RequireValue(args, ref i, "--sidecar");
                        break;
                    case "--through":
                        throughKey = RequireValue(args, ref i, "--through");
                        break;
                    case "--player":
                        playerOverride = RequireValue(args, ref i, "--player");
                        break;
                    case "--list":
                        list = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unrecognized argument: {args[i]}");
                        PrintUsage();
                        return 1;
                }
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 1;
        }

        if (list)
        {
            PrintMissionList();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(sidecarPath) || string.IsNullOrWhiteSpace(throughKey))
        {
            if (string.IsNullOrWhiteSpace(sidecarPath))
                Console.Error.WriteLine("Missing required argument: --sidecar <path to release1-story.json>");
            if (string.IsNullOrWhiteSpace(throughKey))
                Console.Error.WriteLine("Missing required argument: --through <missionKey|none>");
            PrintUsage();
            return 1;
        }

        var fullSidecarPath = Path.GetFullPath(sidecarPath);

        string? existingPlayerId = null;
        if (File.Exists(fullSidecarPath))
        {
            string existingJson;
            try
            {
                existingJson = File.ReadAllText(fullSidecarPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to read the existing sidecar at {fullSidecarPath}: {ex.Message}");
                return 1;
            }

            if (!Release1StorySaveCodec.TryDeserialize(existingJson, out var existingEnvelope, out var decodeResult))
            {
                Console.Error.WriteLine(
                    $"The existing sidecar at {fullSidecarPath} failed to decode; refusing to touch it. " +
                    $"Reason: {decodeResult.Reason}. {decodeResult.Message}");
                return 1;
            }

            existingPlayerId = existingEnvelope?.Story?.PlayerId;
        }
        else
        {
            Console.WriteLine($"No existing sidecar found at {fullSidecarPath}; a new one will be created.");
        }

        var playerId = string.IsNullOrWhiteSpace(playerOverride) ? existingPlayerId : playerOverride;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            Console.Error.WriteLine(
                "No player id available. Pass --player <steamId>, or point --sidecar at a file whose " +
                "story.playerId is already set.");
            return 1;
        }

        var buildResult = StoryPrepBuilder.Build(playerId, throughKey);
        if (!buildResult.Succeeded)
        {
            Console.Error.WriteLine($"Failed to prepare the story ({buildResult.Status}): {buildResult.Message}");
            return 1;
        }

        if (File.Exists(fullSidecarPath))
        {
            var backupPath = $"{fullSidecarPath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            try
            {
                File.Copy(fullSidecarPath, backupPath, overwrite: false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to write a backup at {backupPath}: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"Backed up the existing sidecar to {backupPath}");
        }

        if (!Release1StorySaveCodec.TrySerialize(buildResult.State, out var json, out var serializeResult))
        {
            Console.Error.WriteLine($"Failed to serialize the prepared story: {serializeResult.Reason}. {serializeResult.Message}");
            return 1;
        }

        try
        {
            var directory = Path.GetDirectoryName(fullSidecarPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var stream = new FileStream(fullSidecarPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write the sidecar at {fullSidecarPath}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Wrote the prepared story to {fullSidecarPath}");
        Console.WriteLine();

        string readBackJson;
        try
        {
            readBackJson = File.ReadAllText(fullSidecarPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read back the sidecar after writing it: {ex.Message}");
            return 1;
        }

        if (!Release1StorySaveCodec.TryDeserialize(readBackJson, out var readBackEnvelope, out var readBackResult))
        {
            Console.Error.WriteLine(
                $"The sidecar failed to round-trip through the codec right after writing it: " +
                $"{readBackResult.Reason}. {readBackResult.Message}");
            return 1;
        }

        PrintSummary(readBackEnvelope!);
        return 0;
    }

    private static string RequireValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");
        index++;
        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  OrganizedCrime.StoryPrep --sidecar <path to release1-story.json> --through <missionKey|none>");
        Console.WriteLine("  OrganizedCrime.StoryPrep --list");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --sidecar <path>   Path to the release1-story.json sidecar to prepare.");
        Console.WriteLine("  --through <key>    Mission key to satisfy through, in arc order, or 'none' for");
        Console.WriteLine("                     intro accepted only. Run --list for the valid keys.");
        Console.WriteLine("  --player <steamId> Overrides the player id. Defaults to the existing sidecar's");
        Console.WriteLine("                     story.playerId; required if the sidecar does not exist yet.");
        Console.WriteLine("  --list             Prints the mission keys in arc order and exits.");
    }

    private static void PrintMissionList()
    {
        Console.WriteLine("Release 1 mission keys, in arc order:");
        Console.WriteLine($"  {StoryPrepBuilder.None,-28} intro accepted only, no mission touched");
        foreach (var definition in Release1MissionCatalog.All)
        {
            var usable = definition.MissionKey != Release1MissionCatalog.TheEnvelope;
            var key = $"{definition.Sequence}. {definition.MissionKey}";
            var note = usable
                ? string.Empty
                : "cannot be used with --through; completing it requires story recognition";
            Console.WriteLine($"  {key,-28} {note}");
        }
    }

    private static void PrintSummary(Release1StorySaveEnvelope envelope)
    {
        var story = envelope.Story;
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Schema version:       {envelope.SchemaVersion}");
        if (story is null)
        {
            Console.WriteLine("  Story:                (none)");
            return;
        }

        Console.WriteLine($"  Player id:            {story.PlayerId}");
        Console.WriteLine($"  Standing:             {story.Standing}");
        Console.WriteLine($"  Relationship state:   {story.RelationshipState}");
        Console.WriteLine($"  Release 1 recognized: {story.Release1Recognized}");
        Console.WriteLine("  Missions:");
        foreach (var mission in story.Missions)
            Console.WriteLine($"    {mission.MissionKey,-30} {mission.State}");
    }
}
