using System.Reflection;
using System.Text.RegularExpressions;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-152 characterization: pins the Chapter3RuntimeService split contract
// before the move. Passes on the unrefactored code and unchanged after:
// the same locks, the same public surface, the same ctor validation.
public sealed class Chapter3SplitCharacterizationTests
{
    // FACT (2026-10-09, 74e8cc2): 111 lock (_gate) sites in
    // Chapter3RuntimeService.cs plus 7 in Runtime/Chapter3/Chapter3QaFacade.cs
    // (Checkpoints get/set + 5 checkpoint members). Case-sensitive count;
    // _arthurClock(/Unlock(/comments are not lock statements.
    [Fact]
    public void Split_keeps_the_total_gate_lock_sites()
    {
        Assert.Equal(118, CountGateLocks(SplitFiles()));
    }

    [Fact]
    public void Split_locks_only_the_shared_gate()
    {
        foreach (var (file, text) in SplitFiles())
        {
            foreach (Match match in Regex.Matches(StripLineComments(text), @"(?<![\w])lock\s*\(\s*(\w+)\s*\)"))
                Assert.True(
                    match.Groups[1].Value == "_gate",
                    $"{file} locks {match.Groups[1].Value}, expected _gate: {match.Value}");
        }
    }

    [Fact]
    public void Split_takes_no_locks_outside_the_coordinator_and_facade()
    {
        foreach (var (file, text) in SplitFiles())
        {
            if (file.EndsWith("Chapter3RuntimeService.cs", StringComparison.Ordinal) ||
                file.EndsWith("Chapter3QaFacade.cs", StringComparison.Ordinal))
                continue;
            Assert.DoesNotMatch(@"(?<![\w])lock\s*\(", StripLineComments(text));
        }
    }

    [Fact]
    public void Split_keeps_the_public_surface()
    {
        Assert.Equal(ExpectedSurface, ActualSurface());
    }

    [Fact]
    public void Split_rejects_a_null_context_first()
    {
        Assert.Equal("context", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(null!, new FakeRepository(), new FakeGate(), new FakeClock())).ParamName);
        Assert.Equal("context", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(null!, _ => new FakeRepository(), new FakeGate(), new FakeClock())).ParamName);
    }

    [Fact]
    public void Split_rejects_null_collaborators_by_name()
    {
        var context = new FakeContext();
        var gate = new FakeGate();
        var clock = new FakeClock();
        Assert.Equal("storyGate", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(context, new FakeRepository(), null!, clock)).ParamName);
        Assert.Equal("clock", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(context, new FakeRepository(), gate, null!)).ParamName);
        Assert.Equal("repository", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(context, (IChapter3Repository)null!, gate, clock)).ParamName);
        Assert.Equal("repositoryFactory", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3RuntimeService(context, (Func<string, IChapter3Repository>)null!, gate, clock)).ParamName);
    }

    // OC-152 commit 3: post-move pins. The full-constructor facade is the
    // production wiring; these prove the moved members refuse through it
    // exactly as they did on the service, and that the coordinator adds
    // nothing but delegation on the QA surface.
    [Fact]
    public void Split_full_ctor_facade_refuses_moved_members_when_disposed()
    {
        var facade = CreateFullFacade(() => true);

        var accept = facade.QaAcceptOpenOffer();
        Assert.False(accept.Ok);
        Assert.Equal("Chapter 3 QA accept refused: Chapter 3 is not active.", accept.Message);
        Assert.Equal(new[] { "checkpoints: Chapter 3 is not active." }, facade.QaCheckpointStatus());
    }

    [Fact]
    public void Split_full_ctor_facade_reads_liveness_from_the_context()
    {
        var facade = CreateFullFacade(() => false);

        var accept = facade.QaAcceptOpenOffer();
        Assert.False(accept.Ok);
        Assert.Equal("Chapter 3 QA accept refused: Chapter 3 is not active.", accept.Message);
    }

    [Fact]
    public void Split_full_ctor_facade_rejects_null_collaborators_by_name()
    {
        var gate = new object();
        Func<bool> isDisposed = () => false;
        var ctx = new Chapter3FeatureContext();
        var delivery = new Chapter3DeliveryFeature(ctx, _ => { });
        var demand = new Chapter3DemandFeature(ctx);
        var payments = new Chapter3PaymentsFeature(ctx);
        var arthur = new Chapter3ArthurFeature(ctx);
        var offers = new Chapter3OffersFeature(ctx, delivery);
        Assert.Equal("gate", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(null!, isDisposed, ctx, offers, delivery, demand, payments, arthur)).ParamName);
        Assert.Equal("isDisposed", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, null!, ctx, offers, delivery, demand, payments, arthur)).ParamName);
        Assert.Equal("ctx", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, null!, offers, delivery, demand, payments, arthur)).ParamName);
        Assert.Equal("offers", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, ctx, null!, delivery, demand, payments, arthur)).ParamName);
        Assert.Equal("delivery", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, ctx, offers, null!, demand, payments, arthur)).ParamName);
        Assert.Equal("demand", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, ctx, offers, delivery, null!, payments, arthur)).ParamName);
        Assert.Equal("payments", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, ctx, offers, delivery, demand, null!, arthur)).ParamName);
        Assert.Equal("arthur", Assert.Throws<ArgumentNullException>(() =>
            new Chapter3QaFacade(gate, isDisposed, ctx, offers, delivery, demand, payments, null!)).ParamName);
    }

    [Fact]
    public void Split_coordinator_qa_members_delegate_to_the_facade()
    {
        var root = FindRepositoryRoot();
        var text = StripLineComments(File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3RuntimeService.cs")));
        var names = typeof(Chapter3RuntimeService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.Name.StartsWith("Qa", StringComparison.Ordinal))
            .Select(m => m.Name).ToList();
        Assert.NotEmpty(names);
        foreach (var name in names)
            Assert.Contains($"=> _qaFacade.{name}(", text);
        Assert.Equal(names.Count, Regex.Matches(text, @"=> _qaFacade\.Qa\w+\(").Count);
    }

    private static Chapter3QaFacade CreateFullFacade(Func<bool> isDisposed)
    {
        var ctx = new Chapter3FeatureContext();
        var delivery = new Chapter3DeliveryFeature(ctx, _ => { });
        return new Chapter3QaFacade(
            new object(),
            isDisposed,
            ctx,
            new Chapter3OffersFeature(ctx, delivery),
            delivery,
            new Chapter3DemandFeature(ctx),
            new Chapter3PaymentsFeature(ctx),
            new Chapter3ArthurFeature(ctx));
    }

    private static List<string> ActualSurface()
    {
        var type = typeof(Chapter3RuntimeService);
        var lines = new List<string>();
        foreach (var ctor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
            lines.Add("C:(" + string.Join(",", ctor.GetParameters().Select(FormatParameter)) + ")");
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(p => p.Name))
            lines.Add("P:" + property.Name + ":" + FormatType(property.PropertyType) + ":" +
                (property.CanRead ? "get;" : "") + (property.CanWrite ? "set;" : ""));
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length))
            lines.Add("M:" + method.Name + "(" + string.Join(",", method.GetParameters().Select(FormatParameter)) + "):" + FormatType(method.ReturnType));
        return lines;
    }

    private static string FormatParameter(ParameterInfo parameter) =>
        (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "") + FormatType(parameter.ParameterType);

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
            return FormatType(type.GetElementType()!) + "&";
        if (type.IsArray)
            return FormatType(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
            return type.GetGenericTypeDefinition().FullName!.Replace('+', '.') +
                "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    // Reviewed against the 74e8cc2 source census: 2 ctors, 22 properties,
    // 76 methods. Reflection erases nullable annotations, so this pins the
    // nullability-oblivious contract; declarations move verbatim regardless.
    private static readonly string[] ExpectedSurface = new[]
    {
        "C:(OrganizedCrime.Runtime.IRelease1StoryHostContext,OrganizedCrime.Runtime.IChapter3Repository,OrganizedCrime.Runtime.IChapter3StoryGate,OrganizedCrime.Runtime.IChapter3DayClock,System.Action`1<System.String>,OrganizedCrime.Runtime.IChapter3WarehouseOwnership)",
        "C:(OrganizedCrime.Runtime.IRelease1StoryHostContext,System.Func`2<System.String,OrganizedCrime.Runtime.IChapter3Repository>,OrganizedCrime.Runtime.IChapter3StoryGate,OrganizedCrime.Runtime.IChapter3DayClock,System.Action`1<System.String>,OrganizedCrime.Runtime.IChapter3WarehouseOwnership)",
        "P:ArthurClock:System.Func`1<System.Single>:get;set;",
        "P:ArthurPhone:OrganizedCrime.Runtime.IChapter3ArthurPhone:get;set;",
        "P:ArthurWorld:OrganizedCrime.Runtime.IChapter3ArthurWorld:get;set;",
        "P:BeatingHq:OrganizedCrime.Runtime.IChapter3BeatingHq:get;set;",
        "P:BeatingWorld:OrganizedCrime.Runtime.IChapter3BeatingWorld:get;set;",
        "P:BeatProbeWorld:OrganizedCrime.Runtime.IChapter3BeatProbeWorld:get;set;",
        "P:Checkpoints:OrganizedCrime.Runtime.Chapter3CheckpointService:get;set;",
        "P:ChiefForcedWantedQa:OrganizedCrime.Runtime.IRelease1ChiefForcedWantedQa:get;set;",
        "P:ChiefProtectionQa:OrganizedCrime.Runtime.IRelease1ChiefProtectionQa:get;set;",
        "P:DeadDropWorld:OrganizedCrime.Runtime.Release1.World.IChapter3DeliveryWorld:get;set;",
        "P:DemandWorld:OrganizedCrime.Runtime.IChapter3ProductionDemandWorld:get;set;",
        "P:HasPendingWarehouseBuild:System.Boolean:get;",
        "P:HqPresence:OrganizedCrime.Runtime.IChapter3HqPresence:get;set;",
        "P:OfficerRoster:OrganizedCrime.Runtime.OcOfficerRosterService:get;set;",
        "P:Phase:OrganizedCrime.Runtime.Chapter3RuntimePhase:get;",
        "P:ProtectionSnapshot:System.Func`1<OrganizedCrime.Model.Release1ChiefProtectionSnapshot>:get;set;",
        "P:StaffReturnBannerSender:OrganizedCrime.Runtime.IFishWarehouseStaffReturnBannerSender:get;set;",
        "P:State:OrganizedCrime.Model.Chapter3State:get;",
        "P:VisitConditions:OrganizedCrime.Runtime.IChapter3ArthurVisitConditions:get;set;",
        "P:Wallet:OrganizedCrime.Runtime.IChapter3Wallet:get;set;",
        "P:WarehouseAcquisition:OrganizedCrime.Runtime.IChapter3WarehouseAcquisition:get;set;",
        "P:WarehouseCapacity:OrganizedCrime.Runtime.IChapter3WarehouseCapacity:get;set;",
        "M:Dispose():System.Void",
        "M:GetOvernightLineForSleep():System.String",
        "M:GetPresentationPlan(System.Boolean):OrganizedCrime.Model.Chapter3PresentationPlan",
        "M:NotifySleepStarted():System.Void",
        "M:OnDayPassed():System.Void",
        "M:OnLoadComplete():OrganizedCrime.Runtime.Chapter3RuntimeLifecycleResult",
        "M:OnPreLoad():OrganizedCrime.Runtime.Chapter3RuntimeLifecycleResult",
        "M:OnSaveComplete():OrganizedCrime.Runtime.Chapter3RuntimeLifecycleResult",
        "M:OnSaveStart():OrganizedCrime.Runtime.Chapter3RuntimeLifecycleResult",
        "M:OnWeekPassed():System.Void",
        "M:PollArthurVisit():System.Void",
        "M:PollEmergencyExpiry():System.Void",
        "M:PollPendingWarehouseBuild(System.Func`1<System.Boolean>):System.Void",
        "M:PollProductionDemand():System.Void",
        "M:QaAcceptOpenOffer():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurBeat():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurCome():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurHq(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurHqHere():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurLeave():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurSay(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaArthurStatus():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaArthurVisit(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaBeatAnim(System.String):System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaBeatInvincible(System.Boolean):System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaBeatSnapshot():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaCapacityHere():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCapacitySet(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCapacityStatus():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaCapacityUnlock():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCheckpointClear():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCheckpointPick():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCheckpointSpotHere(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaCheckpointStatus():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaCheckpointTest(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaDeliverActiveJob():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaDemandEligible():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaDemandEligibleAdd(System.String):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaDemandExpire():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaDemandFresh():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaDemandFreshAdd(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaDemandNow():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaEmergencyMinutesLeft():System.Nullable`1<System.Int32>",
        "M:QaFailActiveJob():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaForcedWantedSnapshot():OrganizedCrime.Runtime.Release1ChiefForcedWantedSnapshot",
        "M:QaLockdownAge(System.Double):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaNoticeHide():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaNoticeShow():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaOfferEmergency():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaOfferWarehouse():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaOfficersClear():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaOfficersSpawn(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaOfficersStatus():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:QaPayCut(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaPayMakeGood(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaProtectionLapse():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaProtectionOffer():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaProtectionSnapshot():OrganizedCrime.Model.Release1ChiefProtectionSnapshot",
        "M:QaRecordWarning(System.Int32):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaSimulateWeekPass():OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaWarehouseOwned():System.Boolean",
        "M:QaWarehouseSpawnHere(System.Boolean):OrganizedCrime.Runtime.Chapter3QaResult",
        "M:QaWarehouseSpawns():System.Collections.Generic.IReadOnlyList`1<System.String>",
        "M:ReadCheckpointTarget():OrganizedCrime.Model.Chapter3CheckpointTarget",
        "M:ReadWarehouseTierCapacity():System.Int32",
        "M:TryAcceptOffer():OrganizedCrime.Runtime.Chapter3OfferResult",
        "M:TryBuildPendingWarehouse():System.Boolean",
        "M:TryBuyTime():OrganizedCrime.Runtime.Chapter3MoneyResult",
        "M:TryBuyWarehouse():OrganizedCrime.Runtime.Chapter3MoneyResult",
        "M:TryBuyWarehouseCapacity():OrganizedCrime.Runtime.Chapter3MoneyResult",
        "M:TryDeclineOffer():OrganizedCrime.Runtime.Chapter3DeclineResult",
        "M:TryPayCut(System.Int32):OrganizedCrime.Runtime.Chapter3MoneyResult",
        "M:TryPayMakeGood(System.Int32):OrganizedCrime.Runtime.Chapter3MoneyResult",
        "M:TryQaStartNow():System.Boolean",
        "M:TryRecordPresentationReceipt(System.String):System.Boolean",
        "M:TryWriteCheckpointTarget(OrganizedCrime.Model.Chapter3CheckpointTarget):System.Boolean",
    };

    private static List<(string file, string text)> SplitFiles()
    {
        var root = FindRepositoryRoot();
        var files = new List<(string, string)>
        {
            ("Chapter3RuntimeService.cs", File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3RuntimeService.cs")))
        };
        var chapter3 = Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Chapter3");
        foreach (var file in Directory.EnumerateFiles(chapter3, "*.cs"))
            files.Add((Path.GetFileName(file), File.ReadAllText(file)));
        return files;
    }

    private static int CountGateLocks(List<(string file, string text)> files)
    {
        var count = 0;
        foreach (var (_, text) in files)
            count += Regex.Matches(StripLineComments(text), @"(?<![\w])lock\s*\(\s*_gate\s*\)").Count;
        return count;
    }

    // Comments name locks ("under this lock (the entry ...)"); only
    // statements count. No string literal in these files contains //.
    private static string StripLineComments(string text) =>
        string.Join("\n", text.Split('\n').Select(line =>
        {
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment < 0 ? line : line.Substring(0, comment);
        }));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new Release1StoryHostContextSnapshot(Guid.Empty, 0, "player", Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() => throw new NotImplementedException();
        public Chapter3StoreUpdateResult Update(Chapter3State? state) => throw new NotImplementedException();
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool IsEnvelopeSatisfied() => false;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = 0;
            throw new NotImplementedException();
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }
}
