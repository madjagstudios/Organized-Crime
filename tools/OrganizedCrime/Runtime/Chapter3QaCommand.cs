using System.Globalization;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Police;
using OrganizedCrime.Runtime.Chapter3;

namespace OrganizedCrime.Runtime;

public sealed record Chapter3QaResult(bool Ok, string Message);

/// <summary>
/// What the Chapter 3 QA console command drives. Implemented by
/// <see cref="Chapter3RuntimeService"/>; faked in tests.
/// Owner QA only (OwnerQaKeys): every member here is owner QA. The oc_ch3 command
/// is registered only when OwnerQaKeys is on.
/// </summary>
public interface IChapter3QaTarget
{
    Chapter3State? State { get; }
    Chapter3QaResult QaAcceptOpenOffer();
    Chapter3QaResult QaFailActiveJob();
    Chapter3QaResult QaPayMakeGood(int id);
    Chapter3QaResult QaRecordWarning(int id);
    Chapter3QaResult QaSimulateWeekPass();
    Chapter3QaResult QaPayCut(int weekIndex);
    Chapter3QaResult QaDeliverActiveJob();
    Chapter3QaResult QaOfferEmergency();
    int? QaEmergencyMinutesLeft();
    Chapter3QaResult QaOfferWarehouse();
    bool QaWarehouseOwned();
    Chapter3QaResult QaArthurCome();
    Chapter3QaResult QaArthurSay(string text);
    Chapter3QaResult QaArthurLeave();
    IReadOnlyList<string> QaArthurStatus();
    Chapter3QaResult QaArthurHq(string mode);
    Chapter3QaResult QaArthurHqHere();
    Chapter3QaResult QaArthurVisit(string kind);
    Chapter3QaResult QaArthurBeat();
    IReadOnlyList<string> QaBeatSnapshot();
    IReadOnlyList<string> QaBeatInvincible(bool on);
    IReadOnlyList<string> QaBeatAnim(string? trigger);
    Chapter3QaResult QaDemandNow();
    IReadOnlyList<string> QaDemandEligible();
    IReadOnlyList<string> QaDemandFresh();
    Chapter3QaResult QaDemandExpire();
    Chapter3QaResult QaDemandFreshAdd(int units);
    Chapter3QaResult QaDemandEligibleAdd(string productId);
    IReadOnlyList<string> QaCapacityStatus();
    Chapter3QaResult QaCapacityUnlock();
    Chapter3QaResult QaCapacitySet(int tier);
    Chapter3QaResult QaCapacityHere() => new(false, "Chapter 3 QA capacity here refused: not supported.");
    IReadOnlyList<string> QaWarehouseSpawns() => new[] { "warehouse spawns: not supported." };
    // True moves the exterior point.
    Chapter3QaResult QaWarehouseSpawnHere(bool exterior) => new(false, "Chapter 3 QA spawn here refused: not supported.");
    Chapter3QaResult QaNoticeShow() => new(false, "Chapter 3 QA notice refused: not supported.");
    Chapter3QaResult QaNoticeHide() => new(false, "Chapter 3 QA notice refused: not supported.");
    Chapter3QaResult QaProtectionOffer() => new(false, "Chapter 3 QA protection refused: not supported.");
    Chapter3QaResult QaProtectionLapse() => new(false, "Chapter 3 QA protection refused: not supported.");
    Release1ChiefProtectionSnapshot? QaProtectionSnapshot() => null;
    Chapter3QaResult QaLockdownAge(double hours) => new(false, "Chapter 3 QA lockdown refused: not supported.");
    Release1ChiefForcedWantedSnapshot? QaForcedWantedSnapshot() => null;
    IReadOnlyList<string> QaOfficersStatus() => new[] { "officers: not supported." };
    Chapter3QaResult QaOfficersSpawn(int count) => new(false, "Chapter 3 QA officers spawn refused: not supported.");
    Chapter3QaResult QaOfficersClear() => new(false, "Chapter 3 QA officers clear refused: not supported.");
    IReadOnlyList<string> QaCheckpointStatus() => new[] { "checkpoint: not supported." };
    Chapter3QaResult QaCheckpointSpotHere(string label) => new(false, "Chapter 3 QA checkpoint spot here refused: not supported.");
    Chapter3QaResult QaCheckpointTest(string label) => new(false, "Chapter 3 QA checkpoint test refused: not supported.");
    Chapter3QaResult QaCheckpointClear() => new(false, "Chapter 3 QA checkpoint clear refused: not supported.");
    Chapter3QaResult QaCheckpointPick() => new(false, "Chapter 3 QA checkpoint pick refused: not supported.");
}

/// <summary>
/// Hands the Chapter 3 service to the QA console command. Wired by the mod
/// shell at startup and cleared at deinitialization; the command refuses to
/// run while QA keys are off; registration also requires OwnerQaKeys.
/// </summary>
public static class Chapter3QaAccess
{
    public static IChapter3QaTarget? Service { get; set; }
}

internal interface IChapter3PressureQaTarget
{
    IReadOnlyList<string> PressureStatus();
}

/// <summary>
/// The oc_ch3 console command's dispatch: pure argument handling over an
/// <see cref="IChapter3QaTarget"/>, with no S1API dependency so tests cover it.
/// The thin S1API shim lives in <c>Chapter3QaConsoleCommand</c>.
/// Owner QA only (OwnerQaKeys): every member here is owner QA. The oc_ch3 command
/// is registered only when OwnerQaKeys is on.
/// </summary>
public static class Chapter3QaCommand
{
    public const string CommandWord = "oc_ch3";

    public static IReadOnlyList<string> Execute(IChapter3QaTarget target, IReadOnlyList<string> args, bool qaEnabled)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (args is null)
            throw new ArgumentNullException(nameof(args));
        if (!qaEnabled)
            return new[] { "Chapter 3 QA commands are disabled: OwnerQaKeys is off." };
        if (args.Count == 0)
            return Usage();

        switch (args[0].ToLowerInvariant())
        {
            case "raid":
                if (args.Count == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
                    return target is IChapter3RaidSeizureQaTarget raidStatus ? raidStatus.RaidStatus() : new[] { "raid status: not supported." };
                if (args.Count == 2 && args[1].Equals("skipcap", StringComparison.OrdinalIgnoreCase))
                    return new[] { target is IChapter3RaidSeizureQaTarget raidCap ? raidCap.RaidSkipCap().Message : "raid: not supported." };
                if (args.Count is >= 2 and <= 4 && args[1].Equals("now", StringComparison.OrdinalIgnoreCase))
                {
                    var forceAway = args.Count >= 3 && args[^1].Equals("force-away", StringComparison.OrdinalIgnoreCase);
                    if (args.Count == 4 && !forceAway) return new[] { "Usage: oc_ch3 raid now [siteCode] [force-away]" };
                    var code = args.Count >= 3 && !(args.Count == 3 && forceAway) ? args[2] : null;
                    return new[] { target is IChapter3RaidSeizureQaTarget raidNow ? raidNow.RaidNow(code, forceAway).Message : "raid: not supported." };
                }
                if (args.Count is < 2 or > 3 || !args[1].Equals("preview", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Usage: oc_ch3 raid preview [siteCode] | now [siteCode] [force-away] | skipcap | status" };
                return target is IChapter3RaidQaTarget raid ? raid.RaidPreview(args.Count == 3 ? args[2] : null)
                    : new[] { "raid preview: not supported." };
            case "shutdown":
                if (target is not IChapter3ShutdownQaTarget shutdown) return new[] { "shutdown: not supported." };
                if (args.Count == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase)) return shutdown.ShutdownStatus();
                if (args.Count == 2 && args[1].Equals("expire", StringComparison.OrdinalIgnoreCase)) return new[] { shutdown.ShutdownCommand("expire").Message };
                if (args.Count == 3 && args[1].Equals("hold", StringComparison.OrdinalIgnoreCase)) return new[] { shutdown.ShutdownCommand(args[2].ToLowerInvariant()).Message };
                return new[] { "Usage: oc_ch3 shutdown hold <minutes|done> | expire | status" };
            case "cash":
                if (target is not IChapter3CashQaTarget cash) return new[] { "cash: not supported." };
                if (args.Count == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase)) return cash.CashStatus();
                if (args.Count == 2 && args[1].Equals("expire", StringComparison.OrdinalIgnoreCase)) return new[] { cash.CashCommand("expire").Message };
                return new[] { "Usage: oc_ch3 cash status | expire" };
            case "storage":
                if (target is not IChapter3StorageQaTarget storage) return new[] { "storage: not supported." };
                if (args.Count == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase)) return storage.StorageStatus();
                if (args.Count == 2 && args[1].Equals("expire", StringComparison.OrdinalIgnoreCase)) return new[] { storage.StorageCommand("expire").Message };
                if (args.Count == 3 && args[1].Equals("hold", StringComparison.OrdinalIgnoreCase)) return new[] { storage.StorageCommand(args[2].ToLowerInvariant()).Message };
                return new[] { "Usage: oc_ch3 storage hold <minutes|done> | expire | status" };
            case "status":
                return target is IChapter3PressureQaTarget pressure
                    ? Status(target).Concat(pressure.PressureStatus()).ToArray() : Status(target);
            case "accept":
                return new[] { target.QaAcceptOpenOffer().Message };
            case "fail":
                return new[] { target.QaFailActiveJob().Message };
            case "deliver":
                return new[] { target.QaDeliverActiveJob().Message };
            case "offer":
                if (args.Count == 2 && args[1].Equals("shutdown", StringComparison.OrdinalIgnoreCase) && target is IChapter3ShutdownQaTarget shutdownOffer)
                    return new[] { shutdownOffer.ShutdownCommand("offer").Message };
                if (args.Count == 2 && args[1].Equals("cash", StringComparison.OrdinalIgnoreCase) && target is IChapter3CashQaTarget cashOffer)
                    return new[] { cashOffer.CashCommand("offer").Message };
                if (args.Count == 2 && args[1].Equals("storage", StringComparison.OrdinalIgnoreCase) &&
                    target is IChapter3StorageQaTarget storageOffer)
                    return new[] { storageOffer.StorageCommand("offer").Message };
                if (args.Count < 2 || !string.Equals(args[1], "emergency", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Usage: oc_ch3 offer emergency" };
                return new[] { target.QaOfferEmergency().Message };
            case "warehouse":
                return Warehouse(target, args);
            case "pay":
                if (args.Count < 2 || !TryParseId(args[1], out var payId))
                    return new[] { "Usage: oc_ch3 pay <id>" };
                return new[] { target.QaPayMakeGood(payId).Message };
            case "paycut":
                if (args.Count < 2 || !TryParseId(args[1], out var cutWeek))
                    return new[] { "Usage: oc_ch3 paycut <week>" };
                return new[] { target.QaPayCut(cutWeek).Message };
            case "warn":
                if (args.Count < 2 || !TryParseId(args[1], out var warnId))
                    return new[] { "Usage: oc_ch3 warn <id>" };
                return new[] { target.QaRecordWarning(warnId).Message };
            case "weekpass":
                return new[] { target.QaSimulateWeekPass().Message };
            case "arthur":
                return Arthur(target, args);
            case "beat":
                return Beat(target, args);
            case "demand":
                return Demand(target, args);
            case "capacity":
                return Capacity(target, args);
            case "notice":
                return Notice(target, args);
            case "protection":
                return Protection(target, args);
            case "lockdown":
                return Lockdown(target, args);
            case "wanted":
                return Wanted(target, args);
            case "officers":
                return Officers(target, args);
            case "checkpoint":
                return Checkpoint(target, args);
            case "heat":
                return Heat(target, args);
            case "sweep":
                if (target is not IChapter3SweepQaTarget sweep)
                    return new[] { "sweep: not supported." };
                if (args.Count != 2) return new[] { "Usage: oc_ch3 sweep start | end | skipcap | status" };
                var sweepCommand = args[1].ToLowerInvariant();
                return sweepCommand == "status" ? sweep.SweepStatus() : new[] { sweep.SweepCommand(sweepCommand).Message };
            case "patrol":
                return Patrol(target, args);
            default:
                return new[] { $"Unknown oc_ch3 subcommand: '{args[0]}'." }.Concat(Usage()).ToArray();
        }
    }

    // Accept the "#1" form that status prints, as well as a bare "1".
    private static bool TryParseId(string text, out int id) =>
        int.TryParse(text.TrimStart('#'), out id);

    private static string[] Usage() => new[]
    {
        "oc_ch3 status",
        "oc_ch3 accept",
        "oc_ch3 fail",
        "oc_ch3 deliver",
        "oc_ch3 offer emergency",
        "oc_ch3 warehouse",
        "oc_ch3 warehouse spawns | spawn here <exterior | interior>",
        "oc_ch3 pay <id>",
        "oc_ch3 paycut <week>",
        "oc_ch3 warn <id>",
        "oc_ch3 weekpass",
        "oc_ch3 arthur come | say <text> | leave | status | hq <on | off | auto | here> | visit <warn | coop | beat | call> | beat",
        "oc_ch3 beat probe snapshot | invincible <on | off> | anim [trigger]",
        "oc_ch3 demand now | eligible [productId] | fresh [units] | expire",
        "oc_ch3 capacity [unlock | set <1 | 2 | 3> | here]",
        "oc_ch3 notice show | hide",
        "oc_ch3 protection offer | status | lapse",
        "oc_ch3 lockdown age <hours>",
        "oc_ch3 wanted status",
        "oc_ch3 officers status | spawn <1-8> | clear",
        "oc_ch3 checkpoint status | spot here <label> | test <label> | clear | pick"
    };

    private static IReadOnlyList<string> Heat(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !Enum.TryParse<LocalPressureTier>(args[1], true, out var tier) ||
            !Enum.IsDefined(typeof(LocalPressureTier), tier) || !Enum.GetNames<LocalPressureTier>().Any(n => string.Equals(n, args[1], StringComparison.OrdinalIgnoreCase)))
            return new[] { "Usage: oc_ch3 heat Quiet|Noticed|Watched|Critical" };
        return new[] { target is IChapter3PatrolQaTarget patrol ? patrol.PatrolHeat(tier).Message : "heat: not supported." };
    }

    private static bool TrySwitch(string value, out bool enabled)
    {
        enabled = string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        return enabled || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Patrol(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        const string usage = "Usage: oc_ch3 patrol foot here | vehicle here | stop | status | auto on|off";
        if (args.Count < 2) return new[] { usage };
        if (target is not IChapter3PatrolQaTarget patrol) return new[] { "patrol: not supported." };
        if (args.Count == 3 && string.Equals(args[1], "auto", StringComparison.OrdinalIgnoreCase) && TrySwitch(args[2], out var enabled))
            return new[] { patrol.PatrolAuto(enabled).Message };
        if (args.Count == 2 && string.Equals(args[1], "status", StringComparison.OrdinalIgnoreCase)) return patrol.PatrolStatus();
        if (args.Count == 2 && string.Equals(args[1], "stop", StringComparison.OrdinalIgnoreCase)) return new[] { patrol.PatrolStop().Message };
        if (args.Count != 3 || !string.Equals(args[2], "here", StringComparison.OrdinalIgnoreCase)) return new[] { usage };
        if (string.Equals(args[1], "foot", StringComparison.OrdinalIgnoreCase)) return new[] { patrol.PatrolHere(OcPatrolDuty.Foot).Message };
        if (string.Equals(args[1], "vehicle", StringComparison.OrdinalIgnoreCase)) return new[] { patrol.PatrolHere(OcPatrolDuty.Vehicle).Message };
        return new[] { usage };
    }

    private static IReadOnlyList<string> Arthur(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 arthur come | say <text> | leave | status | hq <on | off | auto | here> | visit <warn | coop | beat | call> | beat" };
        switch (args[1].ToLowerInvariant())
        {
            case "come":
                return new[] { target.QaArthurCome().Message };
            case "say":
                if (args.Count < 3)
                    return new[] { "Usage: oc_ch3 arthur say <text>" };
                return new[] { target.QaArthurSay(string.Join(" ", args.Skip(2))).Message };
            case "leave":
                return new[] { target.QaArthurLeave().Message };
            case "status":
                return target.QaArthurStatus();
            case "hq":
                if (args.Count < 3)
                    return new[] { "Usage: oc_ch3 arthur hq <on | off | auto | here>" };
                if (string.Equals(args[2], "here", StringComparison.OrdinalIgnoreCase))
                    return new[] { target.QaArthurHqHere().Message };
                return new[] { target.QaArthurHq(args[2]).Message };
            case "visit":
                if (args.Count < 3)
                    return new[] { "Usage: oc_ch3 arthur visit <warn | coop | beat | call>" };
                return new[] { target.QaArthurVisit(args[2]).Message };
            case "beat":
                return new[] { target.QaArthurBeat().Message };
            default:
                return new[] { $"Unknown oc_ch3 arthur subcommand: '{args[1]}'." };
        }
    }

    private static IReadOnlyList<string> Beat(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 3 || !string.Equals(args[1], "probe", StringComparison.OrdinalIgnoreCase))
            return new[] { "Usage: oc_ch3 beat probe snapshot | invincible <on | off> | anim [trigger]" };
        switch (args[2].ToLowerInvariant())
        {
            case "snapshot":
                return target.QaBeatSnapshot();
            case "invincible":
                if (args.Count < 4)
                    return new[] { "Usage: oc_ch3 beat probe invincible <on | off>" };
                if (string.Equals(args[3], "on", StringComparison.OrdinalIgnoreCase))
                    return target.QaBeatInvincible(true);
                if (string.Equals(args[3], "off", StringComparison.OrdinalIgnoreCase))
                    return target.QaBeatInvincible(false);
                return new[] { "Usage: oc_ch3 beat probe invincible <on | off>" };
            case "anim":
                return target.QaBeatAnim(args.Count < 4 ? null : string.Join(" ", args.Skip(3)));
            default:
                return new[] { $"Unknown oc_ch3 beat probe subcommand: '{args[2]}'." };
        }
    }

    private static IReadOnlyList<string> Demand(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 demand now | eligible [productId] | fresh [units] | expire" };
        switch (args[1].ToLowerInvariant())
        {
            case "now":
                return new[] { target.QaDemandNow().Message };
            case "eligible":
                if (args.Count >= 3)
                    return new[] { target.QaDemandEligibleAdd(args[2]).Message };
                return target.QaDemandEligible();
            case "fresh":
                if (args.Count >= 3)
                {
                    if (!int.TryParse(args[2], out var units))
                        return new[] { "Usage: oc_ch3 demand fresh [units]" };
                    return new[] { target.QaDemandFreshAdd(units).Message };
                }
                return target.QaDemandFresh();
            case "expire":
                return new[] { target.QaDemandExpire().Message };
            default:
                return new[] { $"Unknown oc_ch3 demand subcommand: '{args[1]}'." };
        }
    }

    // Bare `warehouse` still offers; spawn inspection and placement forms sit beside it.
    private static IReadOnlyList<string> Warehouse(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { target.QaOfferWarehouse().Message };
        if (args.Count == 2 && string.Equals(args[1], "spawns", StringComparison.OrdinalIgnoreCase))
            return target.QaWarehouseSpawns();
        if (args.Count == 4 &&
            string.Equals(args[1], "spawn", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[2], "here", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(args[3], "exterior", StringComparison.OrdinalIgnoreCase))
                return new[] { target.QaWarehouseSpawnHere(exterior: true).Message };
            if (string.Equals(args[3], "interior", StringComparison.OrdinalIgnoreCase))
                return new[] { target.QaWarehouseSpawnHere(exterior: false).Message };
        }
        return new[] { "Usage: oc_ch3 warehouse [spawns | spawn here <exterior | interior>]" };
    }

    private static IReadOnlyList<string> Capacity(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return target.QaCapacityStatus();
        switch (args[1].ToLowerInvariant())
        {
            case "unlock":
                return new[] { target.QaCapacityUnlock().Message };
            case "set":
                if (args.Count < 3 || !int.TryParse(args[2], out var tier))
                    return new[] { "Usage: oc_ch3 capacity set <1 | 2 | 3>" };
                return new[] { target.QaCapacitySet(tier).Message };
            case "here":
                return new[] { target.QaCapacityHere().Message };
            default:
                return new[] { $"Unknown oc_ch3 capacity subcommand: '{args[1]}'." };
        }
    }

    private static IReadOnlyList<string> Protection(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 protection offer | status | lapse" };
        switch (args[1].ToLowerInvariant())
        {
            case "offer":
                return new[] { target.QaProtectionOffer().Message };
            case "lapse":
                return new[] { target.QaProtectionLapse().Message };
            case "status":
                return ProtectionStatus(target);
            default:
                return new[] { "Usage: oc_ch3 protection offer | status | lapse" };
        }
    }

    private static IReadOnlyList<string> ProtectionStatus(IChapter3QaTarget target)
    {
        var lines = new List<string>();
        var snapshot = target.QaProtectionSnapshot();
        if (snapshot is null)
        {
            lines.Add("protection: the Chief service is not wired.");
        }
        else
        {
            var until = "none";
            if (snapshot.ProtectedUntilDay is { } day)
            {
                until = $"day {day}";
                if (target.State?.ChapterDayAt(day) is { } chapterDay)
                    until += $" (chapter {chapterDay})";
            }
            lines.Add($"protection: offered {(snapshot.Offered ? "yes" : "no")}, until {until}, covered now {(snapshot.CoveredNow ? "yes" : "no")}");
        }
        var gates = Release1ProtectionGateState.Snapshot();
        lines.Add($"gates: calls dropped {gates.CallsDropped}, foot {gates.FootPatrolsSkipped} skipped / {gates.FootPatrolsRun} run, " +
            $"vehicle {gates.VehiclePatrolsSkipped} skipped / {gates.VehiclePatrolsRun} run, sentries skipped {gates.SentriesSkipped}, " +
            $"checkpoints blocked {gates.CheckpointsBlocked}");
        return lines;
    }

    private static IReadOnlyList<string> Lockdown(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count == 2 && TrySwitch(args[1], out var enabled))
            return new[] { target is IChapter3PatrolQaTarget patrol ? patrol.PatrolLockdown(enabled).Message : "lockdown: not supported." };
        if (args.Count < 3 || !string.Equals(args[1], "age", StringComparison.OrdinalIgnoreCase))
            return new[] { "Usage: oc_ch3 lockdown age <hours>" };
        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            return new[] { "Usage: oc_ch3 lockdown age <hours>" };
        return new[] { target.QaLockdownAge(hours).Message };
    }

    private static IReadOnlyList<string> Wanted(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !string.Equals(args[1], "status", StringComparison.OrdinalIgnoreCase))
            return new[] { "Usage: oc_ch3 wanted status" };
        return WantedStatus(target);
    }

    private static IReadOnlyList<string> Officers(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 officers status | spawn <1-8> | clear" };
        switch (args[1].ToLowerInvariant())
        {
            case "status":
                return target.QaOfficersStatus();
            case "spawn":
                if (args.Count < 3 || !int.TryParse(args[2], out var count) || count < 1 || count > OcOfficerRoster.MaxOfficers)
                    return new[] { "Usage: oc_ch3 officers spawn <1-8>" };
                return new[] { target.QaOfficersSpawn(count).Message };
            case "clear":
                return new[] { target.QaOfficersClear().Message };
            default:
                return new[] { $"Unknown oc_ch3 officers subcommand: '{args[1]}'." };
        }
    }

    private static IReadOnlyList<string> Checkpoint(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 checkpoint status | spot here <label> | test <label> | clear | pick" };
        switch (args[1].ToLowerInvariant())
        {
            case "status":
                return target.QaCheckpointStatus();
            case "spot":
                if (args.Count < 4 || !string.Equals(args[2], "here", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[3]))
                    return new[] { "Usage: oc_ch3 checkpoint spot here <label>" };
                return new[] { target.QaCheckpointSpotHere(args[3]).Message };
            case "test":
                if (args.Count < 3 || string.IsNullOrWhiteSpace(args[2]))
                    return new[] { "Usage: oc_ch3 checkpoint test <label>" };
                return new[] { target.QaCheckpointTest(args[2]).Message };
            case "clear":
                return new[] { target.QaCheckpointClear().Message };
            case "pick":
                return new[] { target.QaCheckpointPick().Message };
            default:
                return new[] { $"Unknown oc_ch3 checkpoint subcommand: '{args[1]}'." };
        }
    }

    private static IReadOnlyList<string> WantedStatus(IChapter3QaTarget target)
    {
        var snapshot = target.QaForcedWantedSnapshot();
        if (snapshot is null)
            return new[] { "wanted: the Chief service is not wired." };
        if (!snapshot.HasRecord)
            return new[] { "wanted: no Chief record." };
        var age = snapshot.LockdownAgeHours is { } hours ? $"{hours.ToString("F1", CultureInfo.InvariantCulture)} h" : "unknown";
        return new[]
        {
            $"wanted: lockdown {(snapshot.LockdownEngaged ? "engaged" : "off")} (age {age}), warned {(snapshot.Warned ? "yes" : "no")}",
            $"wanted: curfew {YesNo(snapshot.CurfewActive)}, outdoors {YesNo(snapshot.Outdoors)}, protected {(snapshot.Protected ? "yes" : "no")}, pursuit {PursuitName(snapshot.PursuitLevel)}",
            $"wanted: armed {(snapshot.Armed ? "yes" : "no")}, held {(snapshot.Held ? "yes" : "no")}, sighted {Sighted(snapshot.TimeSinceSighted, snapshot.SearchTime)}, arrested {YesNo(snapshot.Arrested)}",
            $"wanted: last night fired (info) {(snapshot.LastNightFired is { } night ? $"night {night}" : "never")}, fires {snapshot.Fires}, dispatched {snapshot.Dispatches}, refused {snapshot.Refusals}"
        };
    }

    private static string Sighted(float? since, float? search) =>
        since is { } s && search is { } t
            ? $"{s.ToString("F1", CultureInfo.InvariantCulture)}/{t.ToString("F1", CultureInfo.InvariantCulture)} s"
            : "unknown";

    private static string YesNo(bool? value) => value is null ? "unknown" : value.Value ? "yes" : "no";

    private static string PursuitName(int? level) => level switch
    {
        0 => "None",
        1 => "Investigating",
        2 => "Arresting",
        3 => "NonLethal",
        4 => "Lethal",
        _ => level is null ? "unknown" : $"level {level}"
    };

    private static IReadOnlyList<string> Notice(IChapter3QaTarget target, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new[] { "Usage: oc_ch3 notice show | hide" };
        switch (args[1].ToLowerInvariant())
        {
            case "show":
                return new[] { target.QaNoticeShow().Message };
            case "hide":
                return new[] { target.QaNoticeHide().Message };
            default:
                return new[] { "Usage: oc_ch3 notice show | hide" };
        }
    }

    private static IReadOnlyList<string> Status(IChapter3QaTarget target)
    {
        var state = target.State;
        if (state is null)
            return new[] { "Chapter 3 QA status: no loaded state." };
        if (state.Phase != Chapter3Phase.Active)
            return new[] { "Chapter 3 QA status: not started." };

        var day = state.ChapterDayAt(state.LastProcessedElapsedDay);
        var lines = new List<string> { $"Chapter 3 QA status: day {day}" };
        var offer = state.JobBoard.OpenOffer;
        lines.Add(offer is null
            ? "offer: none"
            : $"offer: #{offer.OfferId} {offer.JobType}, ${offer.Reward:N0}, respond by day {offer.RespondByDay}");
        var job = state.JobBoard.ActiveJob;
        // The window is live: minutes left needs the clock, not just the state.
        var minutesLeft = job is not null && job.JobType == Chapter3JobType.EmergencySupply && job.ExpiresGameMinutes.HasValue
            ? target.QaEmergencyMinutesLeft()
            : null;
        lines.Add(job is null ? "job: none" : DescribeJob(job, minutesLeft));
        if (target is IChapter3CashQaTarget cashStatus && (job?.CashMovement is not null || offer?.CashMovement is not null || state.JobBoard.LastCompletedJob?.CashMovement is not null))
            lines.AddRange(cashStatus.CashStatus());
        if (target is IChapter3StorageQaTarget storageStatus && (job?.StorageRun is not null || offer?.StorageRun is not null))
            lines.AddRange(storageStatus.StorageStatus());
        if (target is IChapter3ShutdownQaTarget shutdownStatus && (job?.Shutdown is not null || offer?.Shutdown is not null || state.JobBoard.LastCompletedJob?.Shutdown is not null))
            lines.AddRange(shutdownStatus.ShutdownStatus());
        lines.Add(DescribeWarehouseOffer(state, target));
        if (state.Ledger.Entries.Count == 0)
        {
            lines.Add("make-goods: none");
        }
        else
        {
            foreach (var entry in state.Ledger.Entries)
                lines.Add(DescribeMakeGood(entry));
        }
        if (state.Cuts.Entries.Count == 0)
        {
            lines.Add("cuts: none");
        }
        else
        {
            foreach (var entry in state.Cuts.Entries)
                lines.Add(DescribeCut(entry));
        }
        lines.Add(DescribeDemand(state));
        lines.Add(DescribeDemandEligible(state));
        lines.Add(DescribeLastDemand(state));
        lines.Add($"streak {state.JobBoard.RefusalStreak}, successes {state.JobBoard.SuccessfulJobs}, " +
            $"warehouse eligible: {(state.IsWarehouseOfferEligible() ? "yes" : "no")}");
        lines.Add(DescribeArthurVisit(state));
        // The unyielded plan: what Chapter 3 wants ignoring Release 1, so the
        // owner sees pending traffic even while Release 1 holds the replies.
        var plan = Chapter3PresentationPlanner.Build(state, release1HasNellDecision: false, warehouseOwned: target.QaWarehouseOwned());
        if (plan.Messages.Count == 0)
        {
            lines.Add("nell messages: none");
        }
        else
        {
            lines.Add($"nell messages ({plan.Messages.Count}):");
            foreach (var message in plan.Messages)
                lines.Add($"  \"{message.Text}\"");
        }
        lines.Add(plan.Decision is null
            ? "nell buttons: none"
            : $"nell buttons: {string.Join(" | ", plan.Decision.Options.Select(option => option.Label))}");
        var overnight = day.HasValue ? Chapter3OvernightLine.Select(state, day.Value) : null;
        lines.Add(overnight is null ? "overnight: none" : $"overnight: \"{overnight}\"");
        return lines;
    }

    private static string DescribeArthurVisit(Chapter3State state)
    {
        var warned = state.Ledger.Entries.Count(entry => entry.WarningDeliveredDay.HasValue);
        var visit = state.ArthurVisitPending == Chapter3ArthurVisitKind.None
            ? "none pending"
            : $"{state.ArthurVisitPending.ToString().ToLowerInvariant()} pending, missed {state.ArthurVisitMissedEvenings}, last attempt day {state.ArthurVisitLastAttemptedDay}";
        return $"arthur visit: {visit}; delivered: cooperation {(state.ArthurCooperationDelivered ? "yes" : "no")}, warnings {warned}";
    }

    private static string DescribeWarehouseOffer(Chapter3State state, IChapter3QaTarget target)
    {
        var offer = state.WarehouseOffer;
        if (offer is not null && offer.Purchased)
            return target.QaWarehouseOwned()
                ? $"warehouse: purchased day {offer.PurchasedDay}"
                : $"warehouse: purchased day {offer.PurchasedDay}, build pending";
        if (offer is not null)
            return $"warehouse: open since day {offer.OpenedDay}, reminder week {offer.LastReminderWeek}";
        return state.IsWarehouseOfferEligible() ? "warehouse: eligible" : "warehouse: not eligible";
    }

    private static string DescribeJob(Chapter3ActiveJob job, int? minutesLeft)
    {
        var line = $"job: #{job.JobId} {job.JobType}, accepted day {job.AcceptedDay}, deadline day {job.DeadlineDay}";
        if (job.Delivery is not null)
        {
            var product = string.IsNullOrWhiteSpace(job.Delivery.ProductName) ? "your best product" : job.Delivery.ProductName;
            var drop = string.IsNullOrWhiteSpace(job.Delivery.DropName) ? "the drop" : job.Delivery.DropName;
            line += $", deliver {job.Delivery.BrickCount} bricks of {product} at {drop}";
            if (job.Delivery.RemovalConfirmed)
                line += ", removal confirmed";
        }
        if (job.Recovery is not null)
        {
            var recovery = job.Recovery;
            var product = string.IsNullOrWhiteSpace(recovery.ProductName) ? "your best product" : recovery.ProductName;
            var pickup = string.IsNullOrWhiteSpace(recovery.Pickup.Name) ? "the drop" : recovery.Pickup.Name;
            var handoff = string.IsNullOrWhiteSpace(recovery.Handoff.Name) ? "the drop" : recovery.Handoff.Name;
            line += $", recover {recovery.BrickCount} bricks of {product} from {pickup} to {handoff}";
            line += recovery.Custody ? ", in custody" : recovery.Staged ? ", staged" : ", not staged";
            if (recovery.RemovalConfirmed)
                line += ", removal confirmed";
        }
        if (job.JobType == Chapter3JobType.EmergencySupply && job.ExpiresGameMinutes.HasValue)
            line += minutesLeft.HasValue ? $", {minutesLeft.Value} minutes left" : ", minutes left unknown";
        return line;
    }

    private static string DescribeDemand(Chapter3State state)
    {
        var demand = state.ActiveDemand;
        if (demand is null)
            return "demand: none";
        var line = $"demand: {demand.DemandId} {demand.Quantity} {demand.ProductName} ({demand.ProductType.ToString().ToLowerInvariant()}), " +
            $"due day {demand.DueDay}, made {demand.FreshCount}, handed {demand.Delivered}, paid ${demand.PaidTotal:N0}";
        if (demand.PendingRemoval is not null)
            line += $", removal pending locker slot {demand.PendingRemoval.SlotIndex}";
        return line;
    }

    private static string DescribeDemandEligible(Chapter3State state) =>
        state.MadeProductIds.Count == 0
            ? "demand eligible: none"
            : $"demand eligible: {string.Join(", ", state.MadeProductIds)} (last week {state.LastDemandWeek}, next id {state.NextDemandId})";

    private static string DescribeLastDemand(Chapter3State state)
    {
        var outcome = state.LastDemandOutcome;
        if (outcome is null)
            return "last demand: none";
        return outcome.Completed
            ? $"last demand: {outcome.DemandId} completed"
            : $"last demand: {outcome.DemandId} missed, {outcome.LeftUnits} short, ${outcome.Amount:N0} make-good";
    }

    private static string DescribeCut(Chapter3WeeklyCut entry) =>
        $"cut #{entry.WeekIndex}: ${entry.Amount:N0}, due day {entry.DueDay}, grace until day {entry.GraceUntilDay}, {entry.Status}";

    private static string DescribeMakeGood(Chapter3MakeGood entry)
    {
        var stage = entry.Stage switch
        {
            Chapter3MakeGoodStage.WarningDue => ", warning due",
            Chapter3MakeGoodStage.BeatingDue => ", beating due",
            _ => string.Empty
        };
        var warned = entry.WarningDeliveredDay.HasValue ? $", warning delivered day {entry.WarningDeliveredDay.Value}" : string.Empty;
        var beaten = entry.BeatingDeliveredDay.HasValue ? $", beating delivered day {entry.BeatingDeliveredDay.Value}" : string.Empty;
        return $"make-good #{entry.Id}: {Chapter3MakeGoods.DescribeSource(entry.Source)}, ${entry.Amount:N0}, " +
            $"due day {entry.DueDay}, {entry.Status}{stage}{warned}{beaten}";
    }

}
