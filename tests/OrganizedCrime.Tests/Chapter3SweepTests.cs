using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3SweepTests
{
    private const string Player = "76561190000000001";

    internal sealed class Fixture
    {
        internal Chapter3FeatureContext Context = new()
        {
            Phase = Chapter3RuntimePhase.Active,
            State = new Chapter3State(Player, Chapter3Phase.Active, 0, 0, 0,
                CheckpointTargetCode: "barn", CheckpointTargetDay: 0)
        };
        internal Chapter3SweepInput Input = new(true, 359d, LocalPressureTier.Critical, false);
        internal readonly WarningNative Native = new();
        internal readonly Chapter3SweepFeature Feature;
        internal Fixture()
        {
            Feature = new(Context);
            Feature.Wire(() => Input, Native);
        }
        internal void Poll(double minutes) { Input = Input with { Minutes = minutes }; Feature.PollLocked(); }
        internal void Start()
        {
            Feature.PollLocked();
            Poll(360d);
            Assert.Equal(360d, Context.State!.Sweep!.ActiveStartMinutes);
        }
        internal Chapter3State RoundTrip()
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(Context.State, out var json, out var written), written.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var loaded), loaded.Message);
            return envelope!.State!;
        }
    }

    [Theory]
    [InlineData(0d, 360d)]
    [InlineData(359d, 360d)]
    [InlineData(360d, 1800d)]
    [InlineData(1439d, 1800d)]
    [InlineData(1800d, 3240d)]
    public void Scheduling_uses_the_next_six_am(double now, double planned)
    {
        var f = new Fixture();
        f.Poll(now);
        Assert.Equal(planned, f.Context.State!.Sweep!.PlannedStartMinutes);
        Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
    }

    [Fact]
    public void Active_expiry_and_target_survive_heat_change_and_evening_reload()
    {
        var f = new Fixture(); f.Start();
        f.Context.State = f.Context.State! with { CheckpointTargetCode = "other" };
        f.Input = f.Input with { Tier = LocalPressureTier.Quiet };
        f.Poll(1200d);
        var loaded = f.RoundTrip();
        Assert.Equal(f.Context.State, loaded);
        var reload = new Fixture(); reload.Context.State = loaded;
        reload.Input = reload.Input with { Tier = LocalPressureTier.Quiet };
        reload.Poll(1799d);
        Assert.Equal("barn", reload.Context.State!.Sweep!.TargetCode);
        Assert.Equal(1800d, reload.Context.State.Sweep.ActiveEndMinutes);
        Assert.Equal(2, Chapter3SweepFeature.Profile(loaded, 1799d, false)!.Total);
        reload.Poll(1800d);
        Assert.Null(reload.Context.State.Sweep);
        Assert.Equal(360d, reload.Context.State.LastSweepStartMinutes);
    }

    [Theory]
    [InlineData(LocalPressureTier.Quiet)]
    [InlineData(LocalPressureTier.Noticed)]
    [InlineData(LocalPressureTier.Watched)]
    public void Cooling_cancels_pending_without_consuming_the_cap(LocalPressureTier tier)
    {
        var f = new Fixture(); f.Feature.PollLocked();
        f.Input = f.Input with { Tier = tier }; f.Poll(360d);
        Assert.Null(f.Context.State!.Sweep);
        Assert.Null(f.Context.State.LastSweepStartMinutes);
    }

    [Fact]
    public void Protected_warning_is_receipted_once_and_waits_a_full_day_even_after_lapse()
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true };
        f.Feature.PollLocked();
        Assert.Equal(new[] { Chapter3SweepFeature.WarningText }, f.Native.Messages);
        Assert.Equal(359d, f.Context.State!.Sweep!.WarningMinutes);
        Assert.NotNull(f.Context.State.Sweep.WarningReceipt);
        f.Context.State = f.RoundTrip(); f.Feature.ResetTransient();
        f.Input = f.Input with { Protected = false };
        f.Poll(1798d); Assert.Null(f.Context.State!.Sweep!.ActiveStartMinutes);
        f.Poll(1800d); Assert.Equal(1800d, f.Context.State.Sweep.ActiveStartMinutes);
        Assert.True(f.Context.State.Sweep.Small);
        Assert.Equal(1, Chapter3SweepFeature.Profile(f.Context.State, 1800d, false)!.Foot);
        Assert.Equal(0, Chapter3SweepFeature.Profile(f.Context.State, 1800d, false)!.Vehicle);
        Assert.Single(f.Native.Messages);
    }

    [Theory]
    [InlineData(Release1NativePresentationStatus.Unavailable)]
    [InlineData(Release1NativePresentationStatus.Rejected)]
    [InlineData(Release1NativePresentationStatus.Faulted)]
    public void Failed_warning_delivery_never_starts_or_receipts(Release1NativePresentationStatus status)
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true }; f.Native.SendStatus = status;
        f.Poll(359d); f.Poll(1800d);
        Assert.Null(f.Context.State!.Sweep!.WarningMinutes);
        Assert.Null(f.Context.State.Sweep.WarningReceipt);
        Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
        f.Native.SendStatus = Release1NativePresentationStatus.Succeeded;
        f.Poll(1801d);
        Assert.Equal(1801d, f.Context.State.Sweep.WarningMinutes);
        Assert.True(f.Context.State.Sweep.PlannedStartMinutes >= 3241d);
        f.Poll(3241d); Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
        f.Poll(f.Context.State.Sweep.PlannedStartMinutes);
        Assert.NotNull(f.Context.State.Sweep.ActiveStartMinutes);
    }

    [Fact]
    public void Warning_contact_unavailability_and_throw_are_safe()
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true };
        f.Native.ContactStatus = Release1NativePresentationStatus.Unavailable; f.Feature.PollLocked();
        Assert.Equal(0, f.Native.SendCalls);
        f.Native.ContactStatus = Release1NativePresentationStatus.Succeeded; f.Native.ThrowSend = true;
        f.Poll(360d);
        Assert.Null(f.Context.State!.Sweep!.WarningReceipt);
        f.Feature.Wire(() => f.Input, null); f.Poll(1800d);
        Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
    }

    [Fact]
    public void Buying_cover_ends_ordinary_sweep_without_a_new_warning_or_immediate_sweep()
    {
        var f = new Fixture(); f.Start();
        f.Input = f.Input with { Protected = true }; f.Poll(600d); f.Poll(601d);
        Assert.Null(f.Context.State!.Sweep);
        Assert.Equal(360d, f.Context.State.LastSweepStartMinutes);
        Assert.Empty(f.Native.Messages);
    }

    [Fact]
    public void Buying_cover_before_start_converts_pending_to_warned_small()
    {
        var f = new Fixture(); f.Feature.PollLocked();
        f.Input = f.Input with { Protected = true }; f.Poll(360d);
        Assert.True(f.Context.State!.Sweep!.Small);
        Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
        Assert.Equal(1800d, f.Context.State.Sweep.PlannedStartMinutes);
        Assert.Single(f.Native.Messages);
    }

    [Fact]
    public void Seven_day_cap_is_between_starts_and_QA_skip_does_not_erase_the_saved_start()
    {
        var f = new Fixture(); f.Start(); f.Poll(1800d);
        Assert.True(f.Feature.Queue("start").Ok); f.Poll(10439d);
        Assert.Null(f.Context.State!.Sweep);
        Assert.True(f.Feature.Queue("skipcap").Ok); f.Poll(10439d);
        Assert.Equal(360d, f.Context.State.LastSweepStartMinutes);
        Assert.True(f.Feature.Queue("start").Ok); f.Poll(10439d);
        Assert.Equal(10439d, f.Context.State.LastSweepStartMinutes);
        Assert.Equal(10439d, f.Context.State.Sweep!.ActiveStartMinutes);
    }

    [Fact]
    public void Cap_allows_a_start_exactly_seven_days_after_the_previous_start()
    {
        var f = new Fixture(); f.Start(); f.Poll(1800d); f.Poll(10439d);
        Assert.Equal(10440d, f.Context.State!.Sweep!.PlannedStartMinutes);
        f.Poll(10440d);
        Assert.Equal(10440d, f.Context.State.LastSweepStartMinutes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Invalid_clock_never_schedules(double now)
    {
        var f = new Fixture(); f.Poll(now); Assert.Null(f.Context.State!.Sweep);
    }

    [Fact]
    public void Missing_clock_cover_heat_target_and_non_host_fail_closed()
    {
        var f = new Fixture();
        var baseline = f.Context.State;
        foreach (var input in new[]
        {
            f.Input with { Minutes = null }, f.Input with { Protected = null },
            f.Input with { Tier = null }, f.Input with { IsHost = false }
        })
        {
            f.Input = input; f.Feature.PollLocked(); Assert.Equal(baseline, f.Context.State);
        }
        f.Feature.Wire(() => throw new InvalidOperationException(), f.Native);
        f.Feature.PollLocked(); Assert.Equal(baseline, f.Context.State);
        f = new Fixture(); f.Context.State = f.Context.State! with { CheckpointTargetCode = null };
        f.Feature.PollLocked(); Assert.Null(f.Context.State.Sweep);
    }

    [Fact]
    public void Pending_start_requires_readable_cover_and_heat_but_active_expiry_does_not()
    {
        var f = new Fixture(); f.Feature.PollLocked();
        f.Input = f.Input with { Protected = null }; f.Poll(360d);
        Assert.Null(f.Context.State!.Sweep!.ActiveStartMinutes);
        f.Input = f.Input with { Protected = false, Tier = null }; f.Poll(361d);
        Assert.Null(f.Context.State.Sweep.ActiveStartMinutes);
        f.Input = f.Input with { Tier = LocalPressureTier.Critical }; f.Poll(362d);
        f.Input = f.Input with { Tier = null, Protected = null }; f.Poll(1802d);
        Assert.Null(f.Context.State.Sweep);
    }

    [Fact]
    public void Expired_active_and_missed_pending_sweeps_do_not_replay()
    {
        var f = new Fixture(); f.Start(); f.Context.State = f.RoundTrip(); f.Poll(5000d);
        Assert.Null(f.Context.State!.Sweep); Assert.Equal(360d, f.Context.State.LastSweepStartMinutes);
        f = new Fixture(); f.Feature.PollLocked(); f.Context.State = f.RoundTrip(); f.Poll(5000d);
        Assert.Null(f.Context.State!.Sweep); Assert.Null(f.Context.State.LastSweepStartMinutes);
    }

    [Fact]
    public void Surge_replaces_ordinary_and_larger_lockdown_wins_without_stacking()
    {
        var f = new Fixture(); f.Start();
        var surge = Chapter3SweepFeature.Profile(f.Context.State, 1080d, false);
        var ordinary = Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, false, 1080d, surge);
        Assert.Equal(2, ordinary.Total);
        var lockdown = Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, true, 1080d, surge);
        Assert.Equal("lockdown", lockdown.Name); Assert.Equal(4, lockdown.Total);
        Assert.Equal(2, Chapter3PatrolRules.Select(LocalPressureTier.Quiet, false, false, 500d, surge).Total);
        Assert.Null(Chapter3SweepFeature.Profile(f.Context.State, 359d, false));
        Assert.Null(Chapter3SweepFeature.Profile(f.Context.State, 1800d, false));
        Assert.Null(Chapter3SweepFeature.Profile(f.Context.State, 500d, null));
        Assert.Null(Chapter3SweepFeature.Profile(f.Context.State, 500d, true));
    }

    [Fact]
    public void Warned_sweep_stays_small_after_lapse_in_the_ordinary_evening_window()
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true }; f.Feature.PollLocked(); f.Poll(1800d);
        var profile = Chapter3SweepFeature.Profile(f.Context.State, 2520d, false);
        var evening = Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, false, 2520d, profile);
        Assert.Equal("sweep-small", evening.Name); Assert.Equal(1, evening.Foot); Assert.Equal(0, evening.Vehicle);
        var lockdown = Chapter3PatrolRules.Select(LocalPressureTier.Critical, false, true, 2520d, profile);
        Assert.Equal("lockdown", lockdown.Name); Assert.Equal(4, lockdown.Total);
    }

    [Fact]
    public void Ordinary_sweep_roster_does_not_stack_and_returns_the_car_before_save_removal()
    {
        var f = new Fixture(); f.Start();
        using var patrol = new Chapter3PatrolServiceTests.Fixture(); patrol.Checkpoints(6);
        patrol.Input = patrol.Input with { Minutes = 500d, Surge = Chapter3SweepFeature.Profile(f.Context.State, 500d, false) };
        patrol.Converge(); Assert.Equal(8, patrol.Roster.Count); Assert.Equal(2, patrol.World.Bindings.Snapshot().Count);
        patrol.Input = patrol.Input with { Minutes = 1080d }; patrol.Converge();
        Assert.Equal(8, patrol.Roster.Count); Assert.Equal(2, patrol.World.Bindings.Snapshot().Count);
        patrol.Service.OnSaveStart(); patrol.Roster.OnSaveStart();
        Assert.Empty(patrol.World.Bindings.Snapshot()); Assert.Equal(6, patrol.Roster.Count);
        Assert.All(patrol.Roster.Snapshot(), slot => Assert.False(slot.HasLiveClone));
        Assert.Contains("return", patrol.World.Native.Events);
        Assert.True(patrol.World.Native.Events.IndexOf("return") < patrol.World.Native.Events.LastIndexOf("remove"));
        patrol.Roster.OnSaveComplete(); patrol.Service.OnSaveComplete(); patrol.Converge();
        Assert.Equal(8, patrol.Roster.Count); Assert.Equal(2, patrol.World.Bindings.Snapshot().Count);
        patrol.Input = patrol.Input with { Minutes = 1800d, Surge = null }; patrol.Converge();
        Assert.Equal(6, patrol.Roster.Count);
    }

    [Fact]
    public void Protected_profile_uses_one_roster_slot_and_save_returns_all_patrols()
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true }; f.Feature.PollLocked(); f.Poll(1800d);
        using var patrol = new Chapter3PatrolServiceTests.Fixture();
        patrol.Input = patrol.Input with
        {
            Protected = true, Lockdown = false, Minutes = 1800d, CheckpointDesired = 0,
            Surge = Chapter3SweepFeature.Profile(f.Context.State, 1800d, true)
        };
        patrol.Converge(); Assert.Equal(1, patrol.Roster.Count);
        Assert.Single(patrol.World.Bindings.Snapshot());
        patrol.Service.OnSaveStart(); patrol.Roster.OnSaveStart();
        Assert.Empty(patrol.World.Bindings.Snapshot()); Assert.Equal(0, patrol.Roster.Count);
    }

    [Theory]
    [InlineData(Release1NativePresentationStatus.Succeeded)]
    [InlineData(Release1NativePresentationStatus.Unavailable)]
    public void QA_start_while_protected_cannot_bypass_warning_delay(Release1NativePresentationStatus status)
    {
        var f = new Fixture(); f.Input = f.Input with { Protected = true }; f.Native.SendStatus = status;
        Assert.True(f.Feature.Queue("start").Ok); f.Feature.PollLocked();
        Assert.Null(f.Context.State!.Sweep!.ActiveStartMinutes);
    }

    [Fact]
    public void QA_refuses_clients_saving_disposed_and_invalid_commands()
    {
        var f = new Fixture(); Assert.False(f.Feature.Queue("nonsense").Ok);
        f.Input = f.Input with { IsHost = false }; Assert.False(f.Feature.Queue("start").Ok);
        f.Input = f.Input with { IsHost = true }; f.Context.Phase = Chapter3RuntimePhase.Saving;
        Assert.False(f.Feature.Queue("start").Ok); f.Feature.PollLocked(); Assert.Null(f.Context.State!.Sweep);
        f.Context.Phase = Chapter3RuntimePhase.Active; f.Context.Disposed = true;
        Assert.False(f.Feature.Queue("start").Ok);
    }

    [Fact]
    public void QA_end_preserves_cooldown_and_reset_discards_pending_commands()
    {
        var f = new Fixture(); f.Start(); Assert.True(f.Feature.Queue("end").Ok); f.Poll(600d);
        Assert.Null(f.Context.State!.Sweep); Assert.Equal(360d, f.Context.State.LastSweepStartMinutes);
        Assert.Contains(f.Feature.Status(f.Context.State), s => s.Contains("cooldown minutes=9840", StringComparison.Ordinal));
        f = new Fixture(); Assert.True(f.Feature.Queue("start").Ok); f.Feature.ResetTransient(); f.Poll(500d);
        Assert.Null(f.Context.State!.Sweep!.ActiveStartMinutes);
        Assert.Equal(1800d, f.Context.State.Sweep.PlannedStartMinutes);
    }

    [Fact]
    public void Schema15_migrates_without_sweep_and_schema16_defaults_can_be_omitted()
    {
        var f = new Fixture();
        Assert.True(Chapter3SaveCodec.TrySerialize(f.Context.State, out var json, out _));
        var node = JsonNode.Parse(json)!;
        node["state"]!.AsObject().Remove("sweep"); node["state"]!.AsObject().Remove("lastSweepStartMinutes");
        foreach (var version in new[] { 15, 16 })
        {
            node["state"]!.AsObject().Remove("raid");
            node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
            node["schemaVersion"] = version;
            Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var envelope, out var result), result.Message);
            Assert.Equal(20, envelope!.SchemaVersion);
            Assert.Null(envelope.State!.Sweep); Assert.Null(envelope.State.LastSweepStartMinutes);
        }
    }

    [Theory]
    [InlineData("unknown", "true")]
    [InlineData("activeEndMinutes", "500")]
    [InlineData("warningReceipt", "\"forged\"")]
    [InlineData("targetCode", "\" \"")]
    [InlineData("plannedStartMinutes", "-1")]
    public void Corrupt_sweep_fields_are_rejected(string field, string value)
    {
        var f = new Fixture(); f.Start();
        Assert.True(Chapter3SaveCodec.TrySerialize(f.Context.State, out var json, out _));
        var node = JsonNode.Parse(json)!; node["state"]!["sweep"]![field] = JsonNode.Parse(value);
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out _));
    }

    [Fact]
    public void Older_schema_rejects_sweep_fields_and_missing_nested_fields_are_rejected()
    {
        var f = new Fixture(); f.Start();
        Assert.True(Chapter3SaveCodec.TrySerialize(f.Context.State, out var json, out _));
        var node = JsonNode.Parse(json)!; node["schemaVersion"] = 15;
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out var old));
        Assert.Equal(Chapter3CodecFailureReason.UnknownField, old.Reason);
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 16; node["state"]!["sweep"]!.AsObject().Remove("small");
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out var missing));
        Assert.Equal(Chapter3CodecFailureReason.MissingRequiredField, missing.Reason);
    }

    internal sealed class WarningNative : IRelease1NativePresentation
    {
        internal readonly List<string> Messages = new();
        internal Release1NativePresentationStatus SendStatus = Release1NativePresentationStatus.Succeeded;
        internal Release1NativePresentationStatus ContactStatus = Release1NativePresentationStatus.Succeeded;
        internal bool ThrowSend;
        internal int SendCalls;
        public Release1NativePresentationStatus TryEnsureContact() => ContactStatus;
        public Release1NativePresentationStatus TrySendMessage(string text)
        {
            SendCalls++; if (ThrowSend) throw new InvalidOperationException();
            if (SendStatus == Release1NativePresentationStatus.Succeeded) Messages.Add(text);
            return SendStatus;
        }
        public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts) { texts = Messages; return SendStatus; }
        public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision) { decision = null; return SendStatus; }
        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen) => SendStatus;
        public Release1NativePresentationStatus TrySetLabeledDecision(Release1LabeledDecision decision) => SendStatus;
        public Release1NativePresentationStatus TryClearDecision() => SendStatus;
        public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest) { quest = null; return SendStatus; }
        public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest) => SendStatus;
        public Release1NativePresentationStatus TryEndQuest(string key) => SendStatus;
        public void OnPreLoad() { }
        public void OnSaveStart() { }
    }
}
