using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-138: the "warehouse employees loading" top-screen banner while the
/// staff replay runs after a load. The pure banner model, the sender that
/// guards the game's banner text, the announcer that feeds it replay
/// results, and the owner QA dispatch.
/// </summary>
public sealed class FishWarehouseStaffReturnBannerTests
{
    // ---- Model: show ----

    [Fact]
    public void Shows_on_the_first_post_navigation_pending_with_staff()
    {
        var banner = new FishWarehouseStaffReturnBanner();

        var beforeReady = banner.Observe(navigationReady: false, Pending(20));
        var first = banner.Observe(navigationReady: true, Pending(20));

        Assert.Equal(FishWarehouseStaffReturnBannerOp.None, beforeReady.Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show, first.Op);
        Assert.Equal("Organized Crime: warehouse employees loading (0/20)", first.Text);
    }

    [Fact]
    public void Updates_only_when_the_count_changes()
    {
        var banner = new FishWarehouseStaffReturnBanner();
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show,
            banner.Observe(navigationReady: true, Pending(20, replayed: 0)).Op);

        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Pending(20, replayed: 0)).Op);

        var update = banner.Observe(navigationReady: true, Pending(20, replayed: 5));

        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show, update.Op);
        Assert.Equal("Organized Crime: warehouse employees loading (5/20)", update.Text);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Pending(20, replayed: 5)).Op);
    }

    [Fact]
    public void Never_shows_with_no_staff_or_before_navigation_is_ready()
    {
        var banner = new FishWarehouseStaffReturnBanner();

        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Pending(0)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Succeeded(0, 0)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: false, Pending(20)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: false, Succeeded(20, 20)).Op);
    }

    [Fact]
    public void Never_hides_without_a_show()
    {
        var banner = new FishWarehouseStaffReturnBanner();

        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Succeeded(20, 20)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Failed(20, 12)).Op);
    }

    // ---- Model: hide ----

    [Fact]
    public void Hides_on_success_after_a_show()
    {
        var banner = new FishWarehouseStaffReturnBanner();
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show,
            banner.Observe(navigationReady: true, Pending(20)).Op);

        var hide = banner.Observe(navigationReady: true, Succeeded(20, 20));

        Assert.Equal(FishWarehouseStaffReturnBannerOp.Hide, hide.Op);
        Assert.Null(hide.Text);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Succeeded(20, 20)).Op);
    }

    [Fact]
    public void Hides_on_failure_after_a_show()
    {
        var banner = new FishWarehouseStaffReturnBanner();
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show,
            banner.Observe(navigationReady: true, Pending(20)).Op);

        var hide = banner.Observe(navigationReady: true, Failed(20, 12));

        Assert.Equal(FishWarehouseStaffReturnBannerOp.Hide, hide.Op);
    }

    [Fact]
    public void New_load_resets_the_banner()
    {
        var banner = new FishWarehouseStaffReturnBanner();
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show,
            banner.Observe(navigationReady: true, Pending(4)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Hide,
            banner.Observe(navigationReady: true, Succeeded(4, 4)).Op);
        Assert.Equal(FishWarehouseStaffReturnBannerOp.None,
            banner.Observe(navigationReady: true, Pending(4)).Op);

        banner.Reset();

        var first = banner.Observe(navigationReady: true, Pending(6, replayed: 1));
        Assert.Equal(FishWarehouseStaffReturnBannerOp.Show, first.Op);
        Assert.Equal("Organized Crime: warehouse employees loading (1/6)", first.Text);
    }

    [Fact]
    public void FormatText_spells_the_banner()
    {
        Assert.Equal(
            "Organized Crime: warehouse employees loading (5/20)",
            FishWarehouseStaffReturnBanner.FormatText(5, 20));
    }

    [Fact]
    public void Observe_rejects_a_null_result()
    {
        var banner = new FishWarehouseStaffReturnBanner();

        Assert.Throws<ArgumentNullException>(() => banner.Observe(navigationReady: true, null!));
    }

    // ---- Sender: show and update ----

    [Fact]
    public void TryShow_shows_when_the_hud_is_clear()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });

        Assert.True(sender.TryShow("Organized Crime: warehouse employees loading (0/20)"));

        Assert.Equal(
            new[] { "Organized Crime: warehouse employees loading (0/20)" },
            target.Shown);
    }

    [Fact]
    public void TryShow_updates_over_our_own_text()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));

        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(5, 20)));

        Assert.Equal(2, target.Shown.Count);
        Assert.Equal(FishWarehouseStaffReturnBanner.FormatText(5, 20), target.Shown[1]);
    }

    [Fact]
    public void TryShow_declines_a_foreign_banner_without_stomping_it()
    {
        var target = new FakeBannerTarget { CurrentText = "Wanted: reach the hideout" };
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });

        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));

        Assert.Empty(target.Shown);
        target.CurrentText = string.Empty;
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
    }

    [Fact]
    public void TryShow_takes_a_hidden_banner_that_still_holds_stale_text()
    {
        // Native Hide leaves the label text behind: a task instruction the
        // player finished earlier is still in the hidden label.
        var target = new FakeBannerTarget { CurrentText = "Place the mixing station", Visible = false };
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });

        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));

        Assert.Equal(new[] { FishWarehouseStaffReturnBanner.FormatText(0, 20) }, target.Shown);
    }

    [Fact]
    public void TryHideOwn_skips_a_banner_another_caller_already_hid()
    {
        var target = new FakeBannerTarget();
        var logs = new List<string>();
        var sender = new FishWarehouseStaffReturnBannerSender(target, logs.Add);
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(3, 20)));
        target.Visible = false;

        Assert.True(sender.TryHideOwn());

        Assert.Equal(0, target.HideCalls);
        Assert.Empty(logs);
    }

    [Fact]
    public void TryShow_rejects_null_text()
    {
        var sender = new FishWarehouseStaffReturnBannerSender(new FakeBannerTarget(), _ => { });

        Assert.Throws<ArgumentNullException>(() => sender.TryShow(null!));
    }

    // ---- Sender: the foreign-text guard ----

    [Fact]
    public void Foreign_text_mid_load_stops_updates_and_suppresses_the_hide_and_logs_once()
    {
        var target = new FakeBannerTarget();
        var logs = new List<string>();
        var sender = new FishWarehouseStaffReturnBannerSender(target, logs.Add);
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        target.CurrentText = "Wanted: reach the hideout";

        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(5, 20)));
        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(6, 20)));
        Assert.True(sender.TryHideOwn());

        Assert.Single(target.Shown);
        Assert.Equal(0, target.HideCalls);
        var warning = Assert.Single(logs);
        Assert.Contains("foreign", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryHideOwn_hides_only_our_own_text()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });
        Assert.True(sender.TryHideOwn());
        Assert.Equal(0, target.HideCalls);
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));

        Assert.True(sender.TryHideOwn());

        Assert.Equal(1, target.HideCalls);
        Assert.True(sender.TryHideOwn());
        Assert.Equal(1, target.HideCalls);
    }

    [Fact]
    public void TryHideOwn_suppresses_the_hide_when_foreign_replaced_us()
    {
        var target = new FakeBannerTarget();
        var logs = new List<string>();
        var sender = new FishWarehouseStaffReturnBannerSender(target, logs.Add);
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        target.CurrentText = "Wanted: reach the hideout";

        Assert.False(sender.TryHideOwn());

        Assert.Equal(0, target.HideCalls);
        Assert.Single(logs);
    }

    [Fact]
    public void Reset_hides_our_banner_and_clears_the_foreign_latch()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        target.CurrentText = "Wanted: reach the hideout";
        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(5, 20)));

        sender.Reset();

        Assert.Equal(0, target.HideCalls);
        target.CurrentText = string.Empty;
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
    }

    [Fact]
    public void Reset_hides_a_banner_left_from_the_old_scene()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));

        sender.Reset();

        Assert.Equal(1, target.HideCalls);
    }

    // ---- Sender: faults ----

    [Fact]
    public void Throwing_target_declines_without_throwing()
    {
        var sender = new FishWarehouseStaffReturnBannerSender(new FakeBannerTarget { Throw = true }, _ => { });

        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        Assert.True(sender.TryHideOwn());
        sender.Reset();
    }

    [Fact]
    public void Missing_hud_declines()
    {
        var target = new FakeBannerTarget { Available = false };
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });

        Assert.False(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        Assert.True(sender.TryHideOwn());
    }

    [Fact]
    public void Missing_hud_after_a_show_declines_the_hide()
    {
        var target = new FakeBannerTarget();
        var sender = new FishWarehouseStaffReturnBannerSender(target, _ => { });
        Assert.True(sender.TryShow(FishWarehouseStaffReturnBanner.FormatText(0, 20)));
        target.Available = false;

        Assert.False(sender.TryHideOwn());

        Assert.Equal(0, target.HideCalls);
    }

    // ---- Announcer ----

    [Fact]
    public void Announcer_shows_updates_then_hides_through_the_sender()
    {
        var sender = new FakeBannerSender();
        var announcer = new FishWarehouseStaffReturnAnnouncer(sender, _ => { });

        announcer.Observe(navigationReady: false, Pending(20));
        announcer.Observe(navigationReady: true, Pending(20, replayed: 0));
        announcer.Observe(navigationReady: true, Pending(20, replayed: 0));
        announcer.Observe(navigationReady: true, Pending(20, replayed: 7));
        announcer.Observe(navigationReady: true, Succeeded(20, 20));
        announcer.Observe(navigationReady: true, Succeeded(20, 20));

        Assert.Equal(
            new[]
            {
                "Organized Crime: warehouse employees loading (0/20)",
                "Organized Crime: warehouse employees loading (7/20)"
            },
            sender.Shown);
        Assert.Equal(1, sender.HideCalls);
    }

    [Fact]
    public void Announcer_never_shows_without_staff()
    {
        var sender = new FakeBannerSender();
        var announcer = new FishWarehouseStaffReturnAnnouncer(sender, _ => { });

        announcer.Observe(navigationReady: true, Pending(0));
        announcer.Observe(navigationReady: true, Succeeded(0, 0));

        Assert.Empty(sender.Shown);
        Assert.Equal(0, sender.HideCalls);
    }

    [Fact]
    public void Throwing_sender_logs_once_per_failure_and_the_replay_keeps_going()
    {
        var logs = new List<string>();
        var announcer = new FishWarehouseStaffReturnAnnouncer(new ThrowingBannerSender(), logs.Add);

        announcer.Observe(navigationReady: true, Pending(20));

        var warning = Assert.Single(logs);
        Assert.Contains("staff return banner", warning, StringComparison.OrdinalIgnoreCase);
        announcer.Observe(navigationReady: true, Succeeded(20, 20));
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void Declining_sender_logs_and_keeps_going()
    {
        var logs = new List<string>();
        var announcer = new FishWarehouseStaffReturnAnnouncer(new DecliningBannerSender(), logs.Add);

        announcer.Observe(navigationReady: true, Pending(20));
        announcer.Observe(navigationReady: true, Succeeded(20, 20));

        Assert.Equal(2, logs.Count);
        Assert.All(logs, line => Assert.Contains("staff return banner", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Announcer_reset_hides_the_banner_and_starts_a_new_load()
    {
        var sender = new FakeBannerSender();
        var announcer = new FishWarehouseStaffReturnAnnouncer(sender, _ => { });
        announcer.Observe(navigationReady: true, Pending(4));

        announcer.Reset();
        announcer.Observe(navigationReady: true, Pending(4));

        Assert.Equal(1, sender.ResetCalls);
        Assert.Equal(2, sender.Shown.Count);
    }

    // ---- QA dispatch ----

    [Fact]
    public void Notice_show_dispatches_through_the_target()
    {
        var target = new FakeQaTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "notice", "show" }, qaEnabled: true);

        Assert.Equal(new[] { "banner shown" }, lines);
        Assert.Equal(new[] { "notice-show" }, target.Calls);
    }

    [Fact]
    public void Notice_hide_dispatches_through_the_target()
    {
        var target = new FakeQaTarget();

        var lines = Chapter3QaCommand.Execute(target, new[] { "notice", "hide" }, qaEnabled: true);

        Assert.Equal(new[] { "banner hidden" }, lines);
        Assert.Equal(new[] { "notice-hide" }, target.Calls);
    }

    [Fact]
    public void Notice_without_a_verb_prints_usage()
    {
        var lines = Chapter3QaCommand.Execute(new FakeQaTarget(), new[] { "notice" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 notice show | hide" }, lines);
    }

    [Fact]
    public void Notice_with_an_unknown_verb_prints_usage()
    {
        var lines = Chapter3QaCommand.Execute(new FakeQaTarget(), new[] { "notice", "test" }, qaEnabled: true);

        Assert.Equal(new[] { "Usage: oc_ch3 notice show | hide" }, lines);
    }

    [Fact]
    public void Notice_usage_lists_show_and_hide()
    {
        var lines = Chapter3QaCommand.Execute(new FakeQaTarget(), Array.Empty<string>(), qaEnabled: true);

        Assert.Contains("oc_ch3 notice show | hide", lines);
    }

    // ---- Helpers ----

    private static FishWarehouseEmployeeReplayResult Pending(int supported, int replayed = 0) =>
        new(FishWarehouseEmployeeReplayState.Pending, supported, supported, replayed,
            Array.Empty<OrganizedCrime.Persistence.FishWarehouseUnsupportedEmployeeRecord>(),
            CanAdvanceEmployeesReplayed: false, PrimaryFailureReason: null, FailureReason: null);

    private static FishWarehouseEmployeeReplayResult Succeeded(int supported, int replayed) =>
        new(FishWarehouseEmployeeReplayState.Succeeded, supported, supported, replayed,
            Array.Empty<OrganizedCrime.Persistence.FishWarehouseUnsupportedEmployeeRecord>(),
            CanAdvanceEmployeesReplayed: replayed == supported, PrimaryFailureReason: null, FailureReason: null);

    private static FishWarehouseEmployeeReplayResult Failed(int supported, int replayed) =>
        new(FishWarehouseEmployeeReplayState.Failed, supported, supported, replayed,
            Array.Empty<OrganizedCrime.Persistence.FishWarehouseUnsupportedEmployeeRecord>(),
            CanAdvanceEmployeesReplayed: false, PrimaryFailureReason: "boom", FailureReason: "boom");

    private sealed class FakeBannerTarget : IFishWarehouseHudBannerTarget
    {
        public bool Available = true;
        public string? CurrentText = string.Empty;
        public bool Throw;
        public List<string> Shown { get; } = new();
        public int HideCalls;

        // Null derives visibility from the text (a foreign banner set up
        // by a test is on screen); Show and Hide drive it like the native
        // background, and Hide leaves the text behind like the native label.
        public bool? Visible;

        public bool IsVisible() => Visible ?? !string.IsNullOrEmpty(CurrentText);

        public bool IsAvailable()
        {
            if (Throw)
                throw new InvalidOperationException("the HUD exploded.");
            return Available;
        }

        public string? ReadText()
        {
            if (Throw)
                throw new InvalidOperationException("the HUD exploded.");
            return CurrentText;
        }

        public void ShowText(string text)
        {
            if (Throw)
                throw new InvalidOperationException("the HUD exploded.");
            Shown.Add(text);
            CurrentText = text;
            Visible = true;
        }

        public void HideText()
        {
            if (Throw)
                throw new InvalidOperationException("the HUD exploded.");
            HideCalls++;
            Visible = false;
        }
    }

    private sealed class FakeBannerSender : IFishWarehouseStaffReturnBannerSender
    {
        public List<string> Shown { get; } = new();
        public int HideCalls;
        public int ResetCalls;

        public bool TryShow(string text)
        {
            Shown.Add(text);
            return true;
        }

        public bool TryHideOwn()
        {
            HideCalls++;
            return true;
        }

        public void Reset() => ResetCalls++;
    }

    private sealed class ThrowingBannerSender : IFishWarehouseStaffReturnBannerSender
    {
        public bool TryShow(string text) =>
            throw new InvalidOperationException("the native banner rejected the call.");

        public bool TryHideOwn() =>
            throw new InvalidOperationException("the native banner rejected the call.");

        public void Reset() => throw new InvalidOperationException("the native banner rejected the call.");
    }

    private sealed class DecliningBannerSender : IFishWarehouseStaffReturnBannerSender
    {
        public bool TryShow(string text) => false;
        public bool TryHideOwn() => false;
        public void Reset() { }
    }

    private sealed class FakeQaTarget : IChapter3QaTarget
    {
        public List<string> Calls { get; } = new();

        public Chapter3QaResult QaNoticeShow()
        {
            Calls.Add("notice-show");
            return new Chapter3QaResult(true, "banner shown");
        }

        public Chapter3QaResult QaNoticeHide()
        {
            Calls.Add("notice-hide");
            return new Chapter3QaResult(true, "banner hidden");
        }

        public Chapter3State? State => null;
        public Chapter3QaResult QaAcceptOpenOffer() => throw new NotSupportedException();
        public Chapter3QaResult QaFailActiveJob() => throw new NotSupportedException();
        public Chapter3QaResult QaDeliverActiveJob() => throw new NotSupportedException();
        public Chapter3QaResult QaOfferEmergency() => throw new NotSupportedException();
        public Chapter3QaResult QaOfferWarehouse() => throw new NotSupportedException();
        public Chapter3QaResult QaPayMakeGood(int id) => throw new NotSupportedException();
        public Chapter3QaResult QaPayCut(int week) => throw new NotSupportedException();
        public Chapter3QaResult QaRecordWarning(int id) => throw new NotSupportedException();
        public Chapter3QaResult QaSimulateWeekPass() => throw new NotSupportedException();
        public int? QaEmergencyMinutesLeft() => null;
        public bool QaWarehouseOwned() => false;
        public Chapter3QaResult QaArthurCome() => throw new NotSupportedException();
        public Chapter3QaResult QaArthurSay(string text) => throw new NotSupportedException();
        public Chapter3QaResult QaArthurLeave() => throw new NotSupportedException();
        public IReadOnlyList<string> QaArthurStatus() => throw new NotSupportedException();
        public Chapter3QaResult QaArthurHq(string mode) => throw new NotSupportedException();
        public Chapter3QaResult QaArthurHqHere() => throw new NotSupportedException();
        public Chapter3QaResult QaArthurVisit(string kind) => throw new NotSupportedException();
        public Chapter3QaResult QaArthurBeat() => throw new NotSupportedException();
        public IReadOnlyList<string> QaBeatSnapshot() => throw new NotSupportedException();
        public IReadOnlyList<string> QaBeatInvincible(bool on) => throw new NotSupportedException();
        public IReadOnlyList<string> QaBeatAnim(string? trigger) => throw new NotSupportedException();
        public Chapter3QaResult QaDemandNow() => throw new NotSupportedException();
        public IReadOnlyList<string> QaDemandEligible() => throw new NotSupportedException();
        public IReadOnlyList<string> QaDemandFresh() => throw new NotSupportedException();
        public Chapter3QaResult QaDemandExpire() => throw new NotSupportedException();
        public Chapter3QaResult QaDemandFreshAdd(int units) => throw new NotSupportedException();
        public Chapter3QaResult QaDemandEligibleAdd(string productId) => throw new NotSupportedException();
        public IReadOnlyList<string> QaCapacityStatus() => throw new NotSupportedException();
        public Chapter3QaResult QaCapacityUnlock() => throw new NotSupportedException();
        public Chapter3QaResult QaCapacitySet(int tier) => throw new NotSupportedException();
        public Chapter3QaResult QaCapacityHere() => throw new NotSupportedException();
    }
}
