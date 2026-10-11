using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;
using FakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StorageRuntimeTests
{
    [Fact]
    public void Offer_freezes_deterministic_best_product_and_terms_before_accept()
    {
        var a = new Harness();
        var b = new Harness();
        Assert.True(a.Storage.TryFreezeOffer(a.Offer, out var first));
        b.World.Products.Reverse();
        b.World.Drops.Reverse();
        Assert.True(b.Storage.TryFreezeOffer(b.Offer, out var second));
        Assert.Equal(first, second);
        Assert.Equal("rich", first.StorageRun!.Route.ProductId);
        Assert.NotEqual(first.StorageRun.Route.Pickup.Guid, first.StorageRun.Route.Handoff.Guid);
        var plan = Chapter3PresentationPlanner.Build(a.Ctx.State! with
        {
            JobBoard = a.Ctx.State!.JobBoard with { OpenOffer = first }
        }, false, false);
        Assert.Equal("Got a storage run. 2 bricks through the HQ room, held for 12 hours, then another drop. $16,000. Three days once you say yes.", plan.Decision!.Prompt);
        Assert.Empty(a.World.Inserts);
    }

    [Theory]
    [InlineData(15_000, 2, 720)]
    [InlineData(17_000, 2, 720)]
    [InlineData(17_500, 3, 1440)]
    [InlineData(20_000, 3, 1440)]
    public void Both_reward_tiers_freeze_bricks_and_hours(int reward, int bricks, int minutes)
    {
        var h = new Harness(reward);
        Assert.True(h.Storage.TryFreezeOffer(h.Offer, out var offer));
        Assert.Equal(bricks, offer.StorageRun!.Route.BrickCount);
        Assert.Equal(minutes, offer.StorageRun.HoldMinutes);
    }

    [Fact]
    public void Accept_revalidates_frozen_pair_and_never_silently_substitutes()
    {
        var h = new Harness();
        h.Freeze();
        var quote = h.Ctx.State!.JobBoard.OpenOffer!;
        h.World.Drops.RemoveAll(drop => drop.Guid == quote.StorageRun!.Route.Handoff.Guid);
        Assert.False(h.Offers.AcceptOfferLocked(quote, 0).Applied);
        Assert.Equal(quote, h.Ctx.State.JobBoard.OpenOffer);
        Assert.Empty(h.World.Inserts);
    }

    [Fact]
    public void Three_stage_quest_and_exact_accept_copy()
    {
        var h = new Harness();
        h.Accept();
        var route = h.Run.Route;
        var plan = Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false);
        Assert.Equal(3, plan.Quest!.Entries.Count);
        Assert.All(plan.Quest.Entries, e => Assert.Equal(Release1DesiredEntryState.Active, e.State));
        Assert.Contains(plan.Messages, m => m.Text == $"They're at {route.Pickup.Name}. Player closet at HQ, whole stack. Keep it out of the family locker. Don't move it until I fucking call.");
        Assert.Single(h.World.DropClosedCallbacks);
    }

    [Fact]
    public void Family_locker_J_is_excluded()
    {
        var h = new Harness();
        h.Accept(); h.Collect();
        h.Room = h.RoomWith(SyndicateHqStorageContract.Definitions.Last().Guid.ToString());
        h.Poll();
        Assert.Null(h.Run.StowedAt);
    }

    [Fact]
    public void Away_sleep_and_reload_preserve_stow_and_release()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow();
        h.World.CanonicalMinutes = 400;
        h.Reload(); h.Poll();
        Assert.Equal(100d, h.Run.StowedAt);
        h.World.CanonicalMinutes = 820;
        h.Poll();
        Assert.True(h.Run.Released);
        h.Reload();
        Assert.True(h.Run.Released);
        Assert.Equal(100d, h.Run.StowedAt);
        var plan = Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false);
        Assert.Equal(Release1DesiredEntryState.Complete, plan.Quest!.Entries[1].State);
    }

    [Fact]
    public void Early_delivery_is_a_no_op()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow();
        h.Deposit();
        h.Poll();
        Assert.Empty(h.World.QuantityChanges);
        Assert.Empty(h.World.CashChanges);
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Missing_twice_at_sixty_minutes_fails_but_unreadable_does_not()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow();
        h.Room = h.RoomWith(null);
        h.World.CanonicalMinutes = 110; h.Poll();
        Assert.Equal(110d, h.Run.MissingSince);
        h.Room = Release1HoldRoomSnapshot.NotReady();
        h.World.CanonicalMinutes = 500; h.Poll();
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
        h.Room = h.RoomWith(null); h.Poll();
        Assert.Null(h.Ctx.State.JobBoard.ActiveJob);
        var debt = Assert.Single(h.Ctx.State.Ledger.Entries);
        Assert.Equal(16_000, debt.Amount);
        Assert.Equal(2, debt.DueDay);
    }

    [Fact]
    public void Closet_move_in_grace_preserves_original_timer()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow();
        h.Room = h.RoomWith(null);
        h.World.CanonicalMinutes = 110; h.Poll();
        h.Room = h.RoomWith(Chapter3StorageRun.PlayerClosetIds.OrderBy(x => x).Last());
        h.World.CanonicalMinutes = 169; h.Poll();
        Assert.Null(h.Run.MissingSince);
        Assert.Equal(100d, h.Run.StowedAt);
        Assert.True(h.Run.ClosetGuid == Chapter3StorageRun.PlayerClosetIds.OrderBy(x => x).Last());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_or_merged_stacks_are_ambiguous(bool duplicate)
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow();
        var room = h.Room!;
        var closets = room.Closets.ToArray();
        var target = Array.FindIndex(closets, c => c.ClosetGuid == h.Run.ClosetGuid);
        var slot = closets[target].Slots[0];
        closets[target] = closets[target] with
        {
            SlotCount = duplicate ? 2 : 1,
            Slots = duplicate ? new[] { slot, slot with { SlotIndex = 1 } } : new[] { slot with { Quantity = 4 } }
        };
        h.Room = room with { Closets = closets };
        h.World.CanonicalMinutes = 900; h.Poll();
        Assert.False(h.Run.Released);
        Assert.Null(h.Run.MissingSince);
    }

    [Fact]
    public void Verified_handoff_pays_once_and_completes_with_exact_copy()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.Poll(); h.Poll(); h.Reload(); h.Poll();
        Assert.Single(h.World.QuantityChanges);
        Assert.Equal(new[] { 16_000f }, h.World.CashChanges);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Contains(Chapter3PresentationPlanner.Build(h.Ctx.State, false, false).Messages,
            m => m.Text == "It landed. $16,000's yours. Room did its job.");
    }

    [Fact]
    public void Ambiguous_removal_survives_reload_and_deadline_without_second_take()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        h.World.ApplyQuantityChangeOnAmbiguous = true;
        h.Poll();
        Assert.NotNull(h.Run.PendingSlot);
        h.Reload();
        h.Ctx.State = h.Ctx.State! with { JobBoard = Chapter3JobScheduler.OnDayTick(h.Ctx.State!.JobBoard, "player", 4).Board };
        h.Poll();
        Assert.Single(h.World.QuantityChanges);
        Assert.Single(h.World.CashChanges);
        Assert.Empty(h.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Ambiguous_credit_that_landed_is_reconciled_after_reload_once()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        h.World.ApplyCashChangeOnAmbiguous = true;
        h.Poll();
        h.Reload(); h.Poll();
        Assert.Single(h.World.CashChanges);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Failure_cleans_pickup_remnants_and_keeps_stowed_stock()
    {
        var h = new Harness();
        h.Accept();
        var pickup = h.Run.Route.Pickup.Guid;
        h.World.SeedSlots(pickup, Slot(1));
        h.Storage.Fail(0);
        Assert.Equal(0, h.World.SlotQuantity(pickup, 0));
        Assert.Single(h.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Client_or_loading_passes_do_nothing()
    {
        var h = new Harness();
        h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.Context.Ready = false; h.Poll();
        Assert.Empty(h.World.QuantityChanges);
        h.Context.Ready = true; h.Ctx.Phase = Chapter3RuntimePhase.AwaitingLoad; h.Poll();
        Assert.Empty(h.World.QuantityChanges);
    }

    [Fact]
    public void Missing_grace_is_inclusive_at_sixty_and_reload_keeps_the_first_check()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow();
        h.Room = h.RoomWith(null); h.World.CanonicalMinutes = 110; h.Poll();
        h.Reload(); h.World.CanonicalMinutes = 169; h.Poll();
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
        h.World.CanonicalMinutes = 170; h.Poll();
        Assert.Null(h.Ctx.State.JobBoard.ActiveJob);
    }

    [Fact]
    public void Unreadable_clock_or_room_neither_stows_nor_releases()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow();
        h.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        h.World.CanonicalMinutes = 900; h.Poll();
        Assert.False(h.Run.Released);
        h.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Ready;
        h.Room = Release1HoldRoomSnapshot.NotReady(); h.Poll();
        Assert.False(h.Run.Released); Assert.Null(h.Run.MissingSince);
    }

    [Fact]
    public void Busy_handoff_does_not_begin_reconciliation_or_suppress_deadline()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.World.LockStatus = Release1SmallCourtesyWorldMutationStatus.Rejected; h.Poll();
        Assert.False(h.Run.Reconciling);
        var expired = Chapter3JobScheduler.OnDayTick(h.Ctx.State!.JobBoard, "player", 4);
        Assert.Null(expired.Board.ActiveJob);
        Assert.Single(expired.Effects.OfType<Chapter3MakeGoodRequiredEffect>());
    }

    [Fact]
    public void Diverged_pending_handoff_never_removes_or_pays_again()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.World.QuantityStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous; h.Poll();
        h.World.SeedSlots(h.Run.Route.Handoff.Guid, Slot(1));
        h.Reload(); h.Poll();
        Assert.Single(h.World.QuantityChanges); Assert.Empty(h.World.CashChanges);
        Assert.True(h.Run.Reconciling);
    }

    [Fact]
    public void Cleanup_follows_remaining_pickup_stock_across_slots_but_never_touches_closets()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow();
        var closet = h.Room;
        var route = h.Run.Route;
        h.World.SeedSlots(route.Pickup.Guid, Slot(0), Slot(1) with { SlotIndex = 3 });
        h.Storage.Fail(0);
        Assert.Equal(closet, h.Room);
        Assert.Equal(0, h.World.SlotQuantity(route.Pickup.Guid, 3));
        Assert.Single(h.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Storage_offer_fallback_keeps_refusal_and_introductory_sibling_sequence()
    {
        var h = new Harness();
        var before = Chapter3JobBoard.Empty with { NextOfferId = 6 };
        var created = Chapter3JobScheduler.OnDayTick(before, "player", 10).Board;
        var eligible = h.Offers.FreezeFreshStorageOfferLocked(before, created);
        Assert.Equal(Chapter3JobType.TemporaryStorageSafehouse, eligible.OpenOffer!.JobType);
        h.Room = Release1HoldRoomSnapshot.NotReady();
        var fallback = h.Offers.FreezeFreshStorageOfferLocked(before, created);
        Assert.Equal(created, fallback);
        Assert.Equal(0, fallback.RefusalStreak);
    }

    [Fact]
    public void Expired_storage_offer_never_stages_pickup_stock()
    {
        var h = new Harness(); h.Freeze();
        Assert.False(h.Offers.AcceptOfferLocked(h.Offer, 2).Applied);
        Assert.Empty(h.World.Inserts);
    }

    [Fact]
    public void Queued_QA_force_hold_and_expiry_use_the_host_poll()
    {
        var h = new Harness();
        Assert.True(h.Storage.Queue("offer").Ok); h.Poll();
        Assert.NotNull(h.Offer.StorageRun);
        h.Accept(); h.Collect(); h.Stow();
        h.World.CanonicalMinutes = 2000;
        Assert.True(h.Storage.Queue("30").Ok); h.Poll();
        Assert.False(h.Run.Released);
        Assert.Contains(h.Storage.Status(), line => line.Contains("hold remaining 30"));
        Assert.True(h.Storage.Queue("done").Ok); h.Poll();
        Assert.True(h.Run.Released);
        Assert.Contains(h.Storage.Status(), line => line.Contains("stage deliver"));
        Assert.True(h.Storage.Queue("expire").Ok); h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Single(h.Ctx.State.Ledger.Entries);
        h.Context.Ready = false;
        Assert.False(h.Storage.Queue("offer").Ok);
    }

    [Fact]
    public void Pending_payout_suppresses_forced_expiry()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        h.World.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        h.Poll();
        Assert.True(h.Run.Reconciling);
        h.Storage.Queue("expire"); h.Poll();
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob); Assert.Empty(h.Ctx.State.Ledger.Entries);
    }

    [Fact]
    public void Runtime_room_marker_projects_on_the_hold_entry()
    {
        var h = new Harness(); h.Accept();
        var marker = new Release1WorldPoint(10, 20, 30);
        h.Ctx.StorageRoomMarker = () => marker;
        var plan = h.Storage.Project(Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false));
        Assert.Equal(marker, plan.Quest!.Entries[1].Marker);
    }

    [Fact]
    public void Schema16_migrates_with_no_storage_assignment_and_schema17_round_trips_progress()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow();
        var expected = h.Ctx.State;
        h.Reload();
        Assert.Equal(expected, h.Ctx.State);
        Assert.True(Chapter3SaveCodec.TrySerialize(h.Ctx.State, out var json, out _));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 16;
        node["state"]!["jobBoard"]!["activeJob"]!.AsObject().Remove("storageRun");
        node["state"]!["jobBoard"]!["activeJob"]!.AsObject().Remove("cashMovement");
        node["state"]!["jobBoard"]!["openOffer"]?.AsObject().Remove("shutdown");
        node["state"]!["jobBoard"]!["activeJob"]?.AsObject().Remove("shutdown");
        node["state"]!["jobBoard"]!["lastCompletedJob"]?.AsObject().Remove("shutdown");
        Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var migrated, out var read), read.Message);
        Assert.Equal(20, migrated!.SchemaVersion);
        Assert.Null(migrated.State!.JobBoard.ActiveJob!.StorageRun);
    }

    [Fact]
    public void Frozen_offer_and_release_round_trip_and_new_fields_are_rejected_in_schema16()
    {
        var h = new Harness(); h.Freeze();
        var expected = h.Ctx.State; h.Reload(); Assert.Equal(expected, h.Ctx.State);
        h.Accept(); h.Collect(); h.Stow(); h.Release();
        Assert.True(Chapter3SaveCodec.TrySerialize(h.Ctx.State, out var json, out _));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!; node["schemaVersion"] = 16;
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out var read));
        Assert.Equal(Chapter3CodecFailureReason.UnknownField, read.Reason);
        h.Reload(); Assert.True(h.Run.Released);
    }

    [Theory]
    [InlineData("holdMinutes", "60")]
    [InlineData("closetGuid", "\"outside\"")]
    [InlineData("pendingSlot", "1")]
    [InlineData("unknown", "true")]
    public void Corrupt_storage_fields_are_rejected(string field, string value)
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow();
        Assert.True(Chapter3SaveCodec.TrySerialize(h.Ctx.State, out var json, out _));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["state"]!["jobBoard"]!["activeJob"]!["storageRun"]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out _));
    }
    [Fact]
    public void Save_captured_inside_credit_reconciles_after_reload_without_a_second_payment()
    {
        var h = new Harness(); h.Accept(); h.Collect(); h.Stow(); h.Release(); h.Deposit();
        string? snapshot = null;
        h.Ctx.Wallet = new DuringCreditWallet(new Chapter3Wallet(h.World), () =>
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(h.Ctx.State, out var json, out _));
            snapshot = json;
        });
        h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.True(Chapter3SaveCodec.TryDeserialize(snapshot, out var loaded, out var read), read.Message);
        h.Ctx.State = loaded!.State; h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Single(h.World.CashChanges); Assert.Single(h.World.QuantityChanges);
    }

    [Theory]
    [InlineData(15_000)]
    [InlineData(17_000)]
    [InlineData(17_500)]
    [InlineData(20_000)]
    public void Reward_bounds_are_valid_and_off_step_terms_are_rejected(int reward)
    {
        var h = new Harness(reward); h.Freeze();
        h.Offer.StorageRun!.Validate(reward, true);
        Assert.Throws<ArgumentException>(() => h.Offer.StorageRun!.Validate(reward + 1, true));
    }

    private sealed class DuringCreditWallet : IChapter3Wallet
    {
        private readonly IChapter3Wallet _inner;
        private readonly Action _capture;
        internal DuringCreditWallet(IChapter3Wallet inner, Action capture) { _inner = inner; _capture = capture; }
        public Chapter3WalletDebit TryDebit(int amount) => _inner.TryDebit(amount);
        public Chapter3WalletCredit TryCredit(int amount) { _capture(); return _inner.TryCredit(amount); }
    }
    internal static Release1SmallCourtesySlotSnapshot Slot(int quantity) =>
        new(0, quantity == 0 ? null : "rich", quantity == 0 ? null : "brick", quantity, quantity > 0, 0);

    private sealed class Harness
    {
        internal readonly FakeWorld World = new();
        internal readonly Chapter3FeatureContext Ctx;
        internal readonly Chapter3OffersFeature Offers;
        internal readonly Chapter3StorageFeature Storage;
        internal readonly HostContext Context = new();
        internal Release1HoldRoomSnapshot? Room;
        internal Chapter3JobOffer Offer => Ctx.State!.JobBoard.OpenOffer!;
        internal Chapter3StorageRun Run => Ctx.State!.JobBoard.ActiveJob!.StorageRun!;

        internal Harness(int reward = 16_000)
        {
            World.SeedProducts(new("cheap", "Cheap", 10, true), new("rich", "Rich", 100, true), new("hidden", "Hidden", 1000, false));
            World.SeedDrops(new("a", "A", "description", 1, 2, 3, true), new("b", "B", "description", 4, 5, 6, true), new("full", "Full", "description", 7, 8, 9, false));
            World.SeedSlots("a", Slot(0)); World.SeedSlots("b", Slot(0));
            Ctx = new Chapter3FeatureContext
            {
                Context = Context, Phase = Chapter3RuntimePhase.Active,
                State = Chapter3State.CreateInitial("player") with
                {
                    Phase = Chapter3Phase.Active, StartElapsedDay = 0, LastProcessedElapsedDay = 0,
                    JobBoard = Chapter3JobBoard.Empty with
                    {
                        OpenOffer = new(1, Chapter3JobType.TemporaryStorageSafehouse, reward, 0, 1)
                    }
                },
                DeadDropWorld = World, Wallet = new Chapter3Wallet(World), Log = _ => { },
                LoggedDeliveryHolds = new OnceLog(_ => { }), StorageRoomReader = () => Room
            };
            var delivery = new Chapter3DeliveryFeature(Ctx, _ => Poll());
            Storage = new Chapter3StorageFeature(Ctx, delivery); Ctx.Storage = Storage;
            Offers = new Chapter3OffersFeature(Ctx, delivery);
            Room = RoomWith(null);
        }
        internal void Freeze()
        {
            Assert.True(Storage.TryFreezeOffer(Offer, out var offer));
            Ctx.State = Ctx.State! with { JobBoard = Ctx.State!.JobBoard with { OpenOffer = offer } };
        }
        internal void Accept() { Freeze(); Assert.True(Offers.AcceptOfferLocked(Offer, 0).Applied); }
        internal void Collect()
        {
            World.SeedSlots(Run.Route.Pickup.Guid, Slot(0)); Poll();
            Assert.True(Run.Route.Custody);
        }
        internal void Stow() { Room = RoomWith(Chapter3StorageRun.PlayerClosetIds.First()); World.CanonicalMinutes = 100; Poll(); }
        internal void Release() { World.CanonicalMinutes = 100 + Run.HoldMinutes; Poll(); Assert.True(Run.Released); }
        internal void Deposit() => World.SeedSlots(Run.Route.Handoff.Guid, Slot(Run.Route.BrickCount));
        internal void Poll() => Storage.Poll(0);
        internal Release1HoldRoomSnapshot RoomWith(string? guid) =>
            new(Release1HoldRoomReadiness.Ready, SyndicateHqStorageContract.Definitions.Select(d =>
                new Release1HoldRoomClosetSnapshot(d.Guid.ToString(), 1,
                    new[] { Slot(d.Guid.ToString() == guid ? Ctx.State!.JobBoard.ActiveJob!.StorageRun!.Route.BrickCount : 0) })).ToArray());
        internal void Reload()
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(Ctx.State, out var json, out var serialized), serialized.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var loaded, out var read), read.Message);
            Ctx.State = loaded!.State; Ctx.PendingRemoval = null;
        }
    }

    private sealed class HostContext : IRelease1StoryHostContext
    {
        internal bool Ready = true;
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, "player", System.IO.Path.GetTempPath());
            return Ready ? Release1StoryHostContextReadStatus.Ready : Release1StoryHostContextReadStatus.NotAuthoritative;
        }
    }
}
