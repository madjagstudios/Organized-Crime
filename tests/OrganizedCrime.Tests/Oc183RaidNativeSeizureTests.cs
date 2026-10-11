using System.Reflection;
using System.Reflection.Emit;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using S1API.Items;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc183RaidNativeSeizureTests
{
    [Theory]
    [InlineData(20, 10)] [InlineData(10, 10)] [InlineData(1, 1)]
    public void Native_write_uses_negative_delta_internal_true_and_syncs_native_post(int pre, int packages)
    {
        var native = pre;
        var events = new List<string>();
        Chapter3RaidNativeQuantity.Take(packages, () => native,
            (delta, internalWrite) => { Assert.True(internalWrite); events.Add($"delta:{delta}"); native += delta; },
            quantity => events.Add($"sync:{quantity}"), () => events.Add("dirty"));
        Assert.Equal(pre - packages, native);
        Assert.Equal(new[] { "dirty", $"delta:{-packages}", $"sync:{pre - packages}", "dirty" }, events);
    }

    [Theory]
    [InlineData(20, 0)] [InlineData(20, -1)] [InlineData(9, 10)] [InlineData(0, 1)]
    public void Invalid_or_insufficient_native_quantity_never_writes(int pre, int packages)
    {
        Chapter3RaidNativeQuantity.Take(packages, () => pre,
            (_, _) => Assert.Fail("native write"), _ => Assert.Fail("sync"), () => Assert.Fail("dirty"));
    }

    [Theory]
    [InlineData("change")] [InlineData("sync")]
    public void Partial_native_fault_still_enlists_changed_storage_for_save(string fault)
    {
        var native = 20; var dirty = 0;
        Assert.Throws<InvalidOperationException>(() => Chapter3RaidNativeQuantity.Take(10, () => native,
            (delta, _) => { native += delta; if (fault == "change") throw new InvalidOperationException(); },
            _ => { if (fault == "sync") throw new InvalidOperationException(); }, () => dirty++));
        Assert.Equal(10, native); Assert.Equal(2, dirty);
    }

    [Fact]
    public void Sync_uses_actual_native_post_instead_of_overwriting_a_divergent_write()
    {
        var native = 20; var synchronized = -1;
        Chapter3RaidNativeQuantity.Take(10, () => native, (_, _) => native--,
            quantity => synchronized = quantity, () => { });
        Assert.Equal(19, native); Assert.Equal(19, synchronized);
    }

    [Theory]
    [InlineData("motel")] [InlineData("fishwarehouse")] [InlineData("oc_syndicatehq")]
    public void Native_post_confirms_even_when_wrapper_still_reports_pre(string site)
    {
        var h = new Harness(site); h.Freeze(); h.Poll();
        Assert.Equal(20, h.World.WrapperQuantity); Assert.Equal(15, h.World.NativeQuantity);
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.State.Raid!.Slots[0].State);
        Assert.Equal(-5, Assert.Single(h.World.Deltas));
        h.Reload(); h.Poll(); Assert.Single(h.World.Deltas);
    }

    [Theory]
    [InlineData("motel")] [InlineData("fishwarehouse")] [InlineData("oc_syndicatehq")]
    public void Wrapper_post_with_native_pre_cannot_confirm(string site)
    {
        var h = new Harness(site); h.World.WriteMode = "wrapper-only";
        h.Freeze(); h.Poll();
        Assert.Equal(15, h.World.WrapperQuantity); Assert.Equal(20, h.World.NativeQuantity);
        Assert.Equal(Chapter3RaidRemovalState.Pending, h.State.Raid!.Slots[0].State);
        Assert.Equal(0, h.State.Raid.ActualUnits); Assert.Null(h.State.LastRaidSuccessMinutes);
        Assert.Empty(h.Coolings);
    }

    [Theory]
    [InlineData("partial")] [InlineData("replacement")]
    public void Divergent_native_post_holds_and_never_retries_after_reload(string mode)
    {
        var h = new Harness("motel"); h.World.WriteMode = mode; h.Freeze(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Ambiguous, h.State.Raid!.Slots[0].State);
        h.World.NativeQuantity = 20; h.World.NativeFingerprint = Harness.Fingerprint;
        h.Reload(); h.Poll(); Assert.Single(h.World.Deltas); Assert.Empty(h.Coolings);
    }

    [Fact]
    public void Missing_native_post_waits_then_confirms_once_without_retake()
    {
        var h = new Harness("motel"); h.World.HideAfterWrite = true; h.Freeze(); h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Pending, h.State.Raid!.Slots[0].State);
        h.Reload(); h.World.HideAfterWrite = false; h.Poll();
        Assert.Equal(Chapter3RaidRemovalState.Confirmed, h.State.Raid!.Slots[0].State);
        Assert.Single(h.World.Deltas);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Confirmed_pre_stock_warns_once_without_loss_and_next_raid_works(bool completed)
    {
        var h = new Harness("motel"); h.Freeze();
        var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select(s => s with
        { State = Chapter3RaidRemovalState.Confirmed }).ToArray(), Completed = completed,
            CoolingApplied = true, NoticeReceipt = completed ? raid.NoticeCorrelation("player") : null }, LastRaidSuccessMinutes = raid.StartMinutes };
        h.Reload(); h.Poll(); h.Poll();
        Assert.Equal(20, h.World.NativeQuantity); Assert.Empty(h.World.Deltas);
        Assert.Single(h.Lines, s => s.Contains("confirmed raid stock still at pre-quantity", StringComparison.Ordinal));
        h.Context.State = h.State with { Raid = h.State.Raid! with { NoticeReceipt = raid.NoticeCorrelation("player") } };
        Assert.True(h.Feature.SkipCap().Ok);
        Assert.True(h.Feature.Queue("motel", true).Ok);
        h.Poll(); h.Poll();
        Assert.NotEqual(raid.Id, h.State.Raid!.Id); Assert.Single(h.World.Deltas);
        Assert.Equal(15, h.World.NativeQuantity);
    }

    [Fact]
    public void Confirmed_post_stock_does_not_warn_and_logger_fault_never_authorizes_retake()
    {
        var h = new Harness("motel"); h.Freeze(); h.Poll();
        h.Poll(); Assert.DoesNotContain(h.Lines, s => s.Contains("confirmed raid stock", StringComparison.Ordinal));
        h.World.NativeQuantity = 20; h.ThrowLog = true;
        Assert.Null(Record.Exception(h.Poll)); Assert.Single(h.World.Deltas);
        h.ThrowLog = false; h.Poll();
        Assert.DoesNotContain(h.Lines, s => s.Contains("confirmed raid stock", StringComparison.Ordinal));
    }

    [Fact]
    public void Installed_321_AddQuantity_IL_calls_ChangeQuantity_with_false_and_Quantity_forwards_native()
    {
        var type = typeof(ItemSlotInstance);
        Assert.Equal(new Version(3, 2, 1, 0), type.Assembly.GetName().Version);
        var method = type.GetMethod("AddQuantity", new[] { typeof(int) })!;
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        // ldc.i4.0 immediately precedes callvirt ChangeQuantity(int,bool).
        var call = Array.IndexOf(il, (byte)OpCodes.Callvirt.Value);
        Assert.True(call > 0); Assert.Equal((byte)OpCodes.Ldc_I4_0.Value, il[call - 1]);
        var target = (MethodInfo)method.Module.ResolveMethod(BitConverter.ToInt32(il, call + 1))!;
        Assert.Equal(typeof(ItemSlot), target.DeclaringType); Assert.Equal("ChangeQuantity", target.Name);
        Assert.Equal(new[] { typeof(int), typeof(bool) }, target.GetParameters().Select(p => p.ParameterType));
        var getter = type.GetProperty("Quantity")!.GetMethod!;
        il = getter.GetMethodBody()!.GetILAsByteArray()!;
        call = Array.IndexOf(il, (byte)OpCodes.Callvirt.Value);
        target = (MethodInfo)getter.Module.ResolveMethod(BitConverter.ToInt32(il, call + 1))!;
        Assert.Equal(typeof(ItemSlot), target.DeclaringType); Assert.Equal("get_Quantity", target.Name);
        Assert.NotNull(typeof(StorageEntity).GetMethod("SetItemSlotQuantity", new[] { typeof(int), typeof(int) }));
        Assert.True(typeof(BuildableItem).GetProperty("HasChanged")!.CanWrite);
        Assert.NotNull(typeof(BuildableItem).GetMethod("WriteData", new[] { typeof(string) }));
        Assert.Equal(Enum.GetNames(typeof(EQuality)), Enum.GetNames(typeof(S1API.Products.ProductInstance).GetProperty("Quality")!.PropertyType));
    }

    [Fact]
    public void Property_mutation_and_confirmation_are_wired_to_native_slots_and_Hq_already_reads_native()
    {
        var source = Source("Chapter3", "Il2CppChapter3RaidWorld.cs");
        var read = source[source.IndexOf("public bool TryReadSlot", StringComparison.Ordinal)..source.IndexOf("public bool TryLock", StringComparison.Ordinal)];
        Assert.Contains("TryReadNativeSlot", read, StringComparison.Ordinal);
        Assert.DoesNotContain("storage.Slots", read, StringComparison.Ordinal);
        var take = source[source.IndexOf("public void Take", StringComparison.Ordinal)..source.IndexOf("public IReadOnlyList<Chapter3RaidSite> Read(", StringComparison.Ordinal)];
        Assert.Contains("Chapter3RaidNativeQuantity.Take", take, StringComparison.Ordinal);
        Assert.Contains("slot.ChangeQuantity", take, StringComparison.Ordinal);
        Assert.Contains("storage.SetItemSlotQuantity", take, StringComparison.Ordinal);
        Assert.Contains("owner.HasChanged = true", take, StringComparison.Ordinal);
        Assert.DoesNotContain("AddQuantity", take, StringComparison.Ordinal);
        var hq = Source("SyndicateHqNativeStorageBoundary.cs");
        take = hq[hq.IndexOf("public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotQuantity", StringComparison.Ordinal)..hq.IndexOf("public Release1SmallCourtesyWorldMutationStatus TrySetClosetStorageName", StringComparison.Ordinal)];
        Assert.Contains("Chapter3RaidNativeQuantity.Take", take, StringComparison.Ordinal);
        Assert.Contains("storage.SetItemSlotQuantity", take, StringComparison.Ordinal);
        Assert.Contains("owner.HasChanged = true", take, StringComparison.Ordinal);
        var nativeRead = hq[hq.IndexOf("bool Chapter3.IChapter3RaidHqSlotReader.TryReadRaidSlot", StringComparison.Ordinal)..hq.IndexOf("private const string GridRootName", StringComparison.Ordinal)];
        Assert.Contains("TryResolveClosetSlot", nativeRead, StringComparison.Ordinal);
        Assert.Contains("slot.Quantity", nativeRead, StringComparison.Ordinal);
        Assert.Contains("slot.ItemInstance", nativeRead, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmed_warnings_dedupe_across_slots_and_do_not_enter_refusal_sink()
    {
        var h = new Harness("motel"); h.Freeze(); var raid = h.State.Raid!;
        var removal = raid.Slots[0] with { State = Chapter3RaidRemovalState.Confirmed };
        var second = removal with { Take = removal.Take with { Identity = new(Guid.NewGuid(), 1) } };
        h.Context.State = h.State with { Raid = raid with { Slots = new[] { removal, second }, Completed = true, CoolingApplied = true } };
        h.Poll(); h.Poll();
        Assert.Single(h.Lines); Assert.Contains("confirmed raid stock still at pre-quantity", h.Lines[0]);
        Assert.Single(h.Refusals); Assert.Contains("previous raid cooling/notice pending", h.Refusals[0]);
        Assert.Empty(h.World.Deltas);
    }

    [Theory]
    [InlineData("wrapper-only", "still at pre-quantity")]
    [InlineData("partial", "divergent")]
    [InlineData("unreadable", "unavailable")]
    public void Native_read_back_warnings_use_separate_sink_once_per_raid_reason(string mode, string reason)
    {
        var h = new Harness("motel");
        h.World.WriteMode = mode; h.World.HideAfterWrite = mode == "unreadable";
        h.Freeze(); h.Poll(); h.Poll();
        Assert.Single(h.Lines); Assert.Contains("raid native read-back", h.Lines[0]); Assert.Contains(reason, h.Lines[0]);
        Assert.DoesNotContain(h.Refusals, line => line.Contains("read-back", StringComparison.Ordinal));
    }

    [Fact]
    public void Seizure_dedupe_survives_Qa_requeue_and_rearms_for_new_raid_or_reset()
    {
        var h = new Harness("motel"); h.Freeze(); var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select(s => s with
        { State = Chapter3RaidRemovalState.Confirmed }).ToArray(), Completed = true, CoolingApplied = true } };
        h.Poll(); Assert.True(h.Feature.Queue("motel", true).Ok); h.Poll(); Assert.Single(h.Lines);
        h.Context.State = h.State with { Raid = h.State.Raid! with { Id = Guid.NewGuid().ToString("D") } };
        h.Poll(); Assert.Equal(2, h.Lines.Count);
        h.Feature.ResetTransient(); h.Poll(); Assert.Equal(3, h.Lines.Count); Assert.Empty(h.World.Deltas);
    }

    [Fact]
    public void Default_seizure_sink_is_visible_in_normal_mode_with_separate_refusal_sink()
    {
        var h = new Harness("motel"); h.Freeze(); var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Slots = raid.Slots.Select(s => s with
        { State = Chapter3RaidRemovalState.Confirmed }).ToArray(), Completed = true, CoolingApplied = true } };
        var visible = new List<string>();
        var policy = new OcLoggingPolicy(_ => Assert.Fail("diagnostic receipt"), visible.Add);
        var prior = OrganizedCrimeLog.Warning;
        try
        {
            OrganizedCrimeLog.Warning = policy.Warning;
            h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: h.Refusals.Add);
            h.Poll(); h.Poll(); Assert.Single(visible); Assert.Contains("confirmed raid stock", visible[0]);
            Assert.Single(h.Refusals); Assert.Empty(h.World.Deltas);
        }
        finally { OrganizedCrimeLog.Warning = prior; }
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "OrganizedCrime"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "tools", "OrganizedCrime", "Runtime" }.Concat(parts).ToArray()));
    }

    private sealed class Harness
    {
        internal const string Fingerprint = "ogkush|brick|20|quality=Standard";
        internal Chapter3FeatureContext Context = new() { State = new("player", Chapter3Phase.Active, 0, 0, 0), Phase = Chapter3RuntimePhase.Active };
        internal Chapter3RaidInput Input = new(true, 1080, LocalPressureTier.Critical, false, false);
        internal NativeWorld World;
        internal Chapter3RaidFeature Feature;
        internal List<string> Lines = new(), Coolings = new();
        internal bool ThrowLog;
        internal Chapter3State State => Context.State!;
        internal Harness(string site) { World = new(site); Feature = Create(); }
        private Chapter3RaidFeature Create() => new(Context, World, World, () => Input,
            id => { Coolings.Add(id); return true; }, report: Refusals.Add,
            seizureReport: line => { if (ThrowLog) throw new InvalidOperationException(); Lines.Add(line); });
        internal List<string> Refusals = new();
        internal void Poll() => Feature.PollLocked();
        internal void Freeze() { Assert.True(Feature.Queue(World.Site, true).Ok); Poll(); Assert.NotNull(State.Raid); }
        internal void Reload()
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(State, out var json, out var write), write.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);
            Context.State = envelope!.State; Feature = Create();
        }
    }

    private sealed class NativeWorld : IChapter3RaidWorld, IChapter3RaidSeizureWorld
    {
        private static readonly Chapter3RaidSlotIdentity Identity = new(Guid.Parse("5dd9e755-23d7-40ef-9822-97f7092a0f5d"), 0);
        internal string Site, WriteMode = "native", NativeFingerprint = Harness.Fingerprint;
        internal int NativeQuantity = 20, WrapperQuantity = 20;
        internal bool HideAfterWrite;
        internal List<int> Deltas = new();
        internal NativeWorld(string site) => Site = site;
        public bool IsHost => true;
        public bool IsRestored(string site) => true;
        public void Reset() { }
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions) => new[]
        { new Chapter3RaidSite(Site, Site, true, new[] { new Chapter3RaidSlot(Identity, Harness.Fingerprint, "ogkush", "brick", WrapperQuantity, 20) }, Chapter3RaidPresence.Clear) };
        public bool TryReadSlot(string site, Chapter3RaidSlotIdentity id, out Chapter3RaidSlotRead read)
        {
            read = new(NativeQuantity, NativeQuantity == 0 ? null : NativeFingerprint, Harness.Fingerprint);
            return !(HideAfterWrite && Deltas.Count > 0);
        }
        public bool TryLock(string site, Chapter3RaidSlotIdentity id) => true;
        public void Unlock(string site, Chapter3RaidSlotIdentity id) { }
        public void Take(string site, Chapter3RaidSlotIdentity id, int packages) => Chapter3RaidNativeQuantity.Take(packages,
            () => NativeQuantity, (delta, internalWrite) =>
            {
                Assert.True(internalWrite); Deltas.Add(delta);
                if (WriteMode == "wrapper-only") WrapperQuantity += delta;
                else NativeQuantity += WriteMode == "partial" ? -1 : delta;
                if (WriteMode == "replacement") NativeFingerprint = "replacement";
            }, _ => { }, () => { });
    }
}
