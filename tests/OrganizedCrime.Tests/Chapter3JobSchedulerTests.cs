using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3JobSchedulerTests
{
    private const string PlayerId = "76561190000000001";

    private static Chapter3JobTransition Tick(Chapter3JobBoard board, int chapterDay) =>
        Chapter3JobScheduler.OnDayTick(board, PlayerId, chapterDay);

    [Fact]
    public void Job_types_are_the_six_release1_archetypes()
    {
        var catalog = Release1MissionCatalog.All.Select(mission => mission.Archetype).OrderBy(name => name);
        var scheduler = Enum.GetNames(typeof(Chapter3JobType)).OrderBy(name => name);

        Assert.Equal(catalog, scheduler);
    }

    [Fact]
    public void First_offer_is_DeadDrop_with_terms_in_range()
    {
        var result = Tick(Chapter3JobBoard.Empty, 0);

        var created = Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(result.Effects));
        Assert.Equal(Chapter3JobType.DeadDrop, created.Offer.JobType);
        Assert.InRange(created.Offer.Reward, Chapter3JobTuning.MinReward, Chapter3JobTuning.MaxReward);
        Assert.Equal(0, created.Offer.OfferedDay);
        Assert.Equal(1, created.Offer.RespondByDay);
        Assert.Equal(1, created.Offer.OfferId);
        Assert.Equal(created.Offer, result.Board.OpenOffer);
    }

    [Fact]
    public void Accept_sets_a_two_day_completion_deadline()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;

        var accepted = Chapter3JobScheduler.Accept(board, board.OpenOffer!.OfferId, 0);

        var job = accepted.Board.ActiveJob;
        Assert.NotNull(job);
        Assert.Equal(Chapter3JobType.DeadDrop, job!.JobType);
        Assert.Equal(board.OpenOffer.Reward, job.Reward);
        Assert.Equal(2, job.DeadlineDay);
        Assert.Equal(Chapter3JobTuning.CompletionDeadlineDays, job.DeadlineDay - job.AcceptedDay);
        Assert.Null(accepted.Board.OpenOffer);
    }

    [Fact]
    public void No_second_offer_while_one_is_open()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;

        var day1 = Tick(board, 1);

        Assert.Empty(day1.Effects);
        Assert.Same(board.OpenOffer, day1.Board.OpenOffer);
        Assert.Equal(1, day1.Board.LastProcessedDay);
    }

    [Fact]
    public void No_offer_while_a_job_is_active()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;

        var day1 = Tick(board, 1);
        var day2 = Tick(day1.Board, 2);

        Assert.Empty(day1.Effects);
        Assert.Empty(day2.Effects);
        Assert.Null(day1.Board.OpenOffer);
        Assert.Null(day2.Board.OpenOffer);
        Assert.NotNull(day2.Board.ActiveJob);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Offer_can_be_accepted_on_its_day_and_the_next(int answerDay)
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        for (var day = 1; day <= answerDay; day++)
            board = Tick(board, day).Board;

        var accepted = Chapter3JobScheduler.Accept(board, 1, answerDay);

        Assert.NotNull(accepted.Board.ActiveJob);
        Assert.Null(accepted.Board.OpenOffer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Offer_can_be_refused_on_its_day_and_the_next(int answerDay)
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        for (var day = 1; day <= answerDay; day++)
            board = Tick(board, day).Board;

        var refused = Chapter3JobScheduler.Refuse(board, 1, answerDay);

        Assert.Empty(refused.Effects);
        Assert.Null(refused.Board.OpenOffer);
        Assert.Equal(1, refused.Board.RefusalStreak);
    }

    [Fact]
    public void Offer_expires_at_the_tick_for_day_two_with_one_streak()
    {
        var board = Tick(Tick(Chapter3JobBoard.Empty, 0).Board, 1).Board;

        var expired = Tick(board, 2);

        var effect = Assert.IsType<Chapter3OfferExpiredEffect>(Assert.Single(expired.Effects));
        Assert.Equal(Chapter3JobType.DeadDrop, effect.Offer.JobType);
        Assert.Equal(1, effect.RefusalStreak);
        Assert.Null(expired.Board.OpenOffer);
        Assert.Equal(1, expired.Board.RefusalStreak);
    }

    [Fact]
    public void Expiry_counts_once()
    {
        var board = Tick(Tick(Chapter3JobBoard.Empty, 0).Board, 1).Board;
        board = Tick(board, 2).Board;

        var next = Tick(board, 3);

        Assert.Empty(next.Effects);
        Assert.Equal(1, next.Board.RefusalStreak);
    }

    [Fact]
    public void Cooldown_after_expiry_blocks_two_days_then_offers_a_runnable_type()
    {
        var board = Tick(Tick(Chapter3JobBoard.Empty, 0).Board, 1).Board;
        board = Tick(board, 2).Board;

        var day3 = Tick(board, 3);
        var day4 = Tick(day3.Board, 4);
        var day5 = Tick(day4.Board, 5);

        Assert.Empty(day3.Effects);
        Assert.Empty(day4.Effects);
        var created = Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(day5.Effects));
        Assert.Contains(created.Offer.JobType, Chapter3JobTuning.RunnableJobTypes);
        Assert.Equal(2, created.Offer.OfferId);
    }

    [Fact]
    public void Cooldown_after_refusal_blocks_two_days()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 1).Board;

        var day2 = Tick(board, 2);
        var day3 = Tick(day2.Board, 3);
        var day4 = Tick(day3.Board, 4);

        Assert.Empty(day2.Effects);
        Assert.Empty(day3.Effects);
        Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(day4.Effects));
    }

    [Fact]
    public void Cooldown_after_completion_blocks_two_days()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        board = Chapter3JobScheduler.Complete(board, 1, succeeded: true, chapterDay: 1).Board;

        var day2 = Tick(board, 2);
        var day3 = Tick(day2.Board, 3);
        var day4 = Tick(day3.Board, 4);

        Assert.Empty(day2.Effects);
        Assert.Empty(day3.Effects);
        Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(day4.Effects));
    }

    [Fact]
    public void Cooldown_after_failure_blocks_two_days()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        board = Chapter3JobScheduler.Complete(board, 1, succeeded: false, chapterDay: 0).Board;

        var day1 = Tick(board, 1);
        var day2 = Tick(day1.Board, 2);
        var day3 = Tick(day2.Board, 3);

        Assert.Empty(day1.Effects);
        Assert.Empty(day2.Effects);
        Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(day3.Effects));
    }

    [Fact]
    public void Only_runnable_types_are_ever_offered()
    {
        var (offers, _) = RefuseCycles(8);

        Assert.Equal(8, offers.Count);
        Assert.All(offers, offer => Assert.Contains(offer.JobType, Chapter3JobTuning.RunnableJobTypes));
    }

    [Fact]
    public void Early_offers_alternate_paid_types_until_the_first_emergency()
    {
        var (offers, _) = RefuseCycles(5);

        Assert.Equal(
            new[]
            {
                Chapter3JobType.DeadDrop,
                Chapter3JobType.PackageRecovery,
                Chapter3JobType.DeadDrop,
                Chapter3JobType.PackageRecovery,
                Chapter3JobType.EmergencySupply
            },
            offers.Select(offer => offer.JobType));
    }

    [Fact]
    public void Emergency_supply_never_appears_in_the_first_three_offers()
    {
        var (offers, _) = RefuseCycles(3);

        Assert.DoesNotContain(offers, offer => offer.JobType == Chapter3JobType.EmergencySupply);
    }

    [Fact]
    public void Emergency_supply_is_occasional_never_adjacent_and_capped_over_a_long_run()
    {
        var (offers, board) = RefuseCycles(120);

        var emergencies = offers.Where(offer => offer.JobType == Chapter3JobType.EmergencySupply).ToList();
        Assert.True(emergencies.Count > 0);
        Assert.True(emergencies.Count * 3 <= offers.Count);
        for (var i = 1; i < offers.Count; i++)
            Assert.NotEqual(offers[i - 1].JobType, offers[i].JobType);
        for (var i = 1; i < emergencies.Count; i++)
            Assert.True(emergencies[i].OfferId - emergencies[i - 1].OfferId >= 4);
        Assert.Equal(emergencies.Last().OfferId, board.LastEmergencySupplyOfferId);
        Assert.All(emergencies, offer => Assert.Null(offer.ProductId));
    }

    [Fact]
    public void Paid_offer_after_a_paid_offer_is_the_other_paid_type()
    {
        var (offers, _) = RefuseCycles(120);

        for (var i = 1; i < offers.Count; i++)
        {
            if (offers[i].JobType == Chapter3JobType.EmergencySupply ||
                offers[i - 1].JobType == Chapter3JobType.EmergencySupply)
                continue;
            Assert.NotEqual(offers[i - 1].JobType, offers[i].JobType);
        }
    }

    [Fact]
    public void Emergency_product_is_deterministic_per_player_and_offer()
    {
        var candidates = new[]
        {
            new Release1SmallCourtesyProductCandidate("c", "C", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("a", "A", 9_000d, true),
            new Release1SmallCourtesyProductCandidate("b", "B", 5_000d, true)
        };

        var first = Chapter3JobScheduler.SelectEmergencyProduct(candidates, PlayerId, 5);
        var second = Chapter3JobScheduler.SelectEmergencyProduct(candidates, PlayerId, 5);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Emergency_product_ignores_order_and_picks_beyond_the_best()
    {
        var ordered = new[]
        {
            new Release1SmallCourtesyProductCandidate("a", "A", 9_000d, true),
            new Release1SmallCourtesyProductCandidate("b", "B", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("c", "C", 1_000d, true)
        };
        var shuffled = new[] { ordered[2], ordered[0], ordered[1] };

        Assert.Equal(
            Chapter3JobScheduler.SelectEmergencyProduct(ordered, PlayerId, 5),
            Chapter3JobScheduler.SelectEmergencyProduct(shuffled, PlayerId, 5));

        var picks = Enumerable.Range(1, 20)
            .Select(offerId => Chapter3JobScheduler.SelectEmergencyProduct(ordered, PlayerId, offerId)!.ProductId)
            .Distinct()
            .ToList();
        Assert.True(picks.Count > 1);
    }

    [Fact]
    public void Emergency_product_skips_undiscovered_and_blank_candidates()
    {
        var candidates = new[]
        {
            new Release1SmallCourtesyProductCandidate("hidden", "Hidden", 99_000d, false),
            new Release1SmallCourtesyProductCandidate("  ", "Blank", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("ok", "Ok", 1_000d, true)
        };

        var picked = Chapter3JobScheduler.SelectEmergencyProduct(candidates, PlayerId, 5);

        Assert.Equal("ok", picked!.ProductId);
        Assert.Null(Chapter3JobScheduler.SelectEmergencyProduct(
            new[] { candidates[0] }, PlayerId, 5));
        Assert.Null(Chapter3JobScheduler.SelectEmergencyProduct(null, PlayerId, 5));
        Assert.Null(Chapter3JobScheduler.SelectEmergencyProduct(
            Array.Empty<Release1SmallCourtesyProductCandidate>(), PlayerId, 5));
    }

    [Fact]
    public void Fallback_rewrites_an_unseen_emergency_to_the_other_paid_type()
    {
        var before = Chapter3JobBoard.Empty with
        {
            NextOfferId = 5,
            LastTypeOffered = Chapter3JobType.PackageRecovery,
            TypesSeen = new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery }
        };
        var board = before with
        {
            OpenOffer = new Chapter3JobOffer(5, Chapter3JobType.EmergencySupply, 15_500, 9, 10),
            LastTypeOffered = Chapter3JobType.EmergencySupply,
            TypesSeen = new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery, Chapter3JobType.EmergencySupply },
            NextOfferId = 6,
            LastEmergencySupplyOfferId = 5
        };

        var fallen = Chapter3JobScheduler.FallbackEmergencyOffer(board, before, PlayerId);

        Assert.NotNull(fallen.OpenOffer);
        Assert.Equal(5, fallen.OpenOffer!.OfferId);
        Assert.Equal(Chapter3JobType.DeadDrop, fallen.OpenOffer.JobType);
        Assert.Null(fallen.OpenOffer.ProductId);
        Assert.Equal(fallen.OpenOffer.JobType, fallen.LastTypeOffered);
        Assert.DoesNotContain(Chapter3JobType.EmergencySupply, fallen.TypesSeen);
        Assert.Null(fallen.LastEmergencySupplyOfferId);
        Assert.Equal(6, fallen.NextOfferId);
    }

    [Fact]
    public void Fallback_keeps_a_previously_seen_emergency_in_types_seen()
    {
        var before = Chapter3JobBoard.Empty with
        {
            NextOfferId = 9,
            TypesSeen = new[] { Chapter3JobType.DeadDrop, Chapter3JobType.EmergencySupply },
            LastEmergencySupplyOfferId = 5
        };
        var board = before with
        {
            OpenOffer = new Chapter3JobOffer(9, Chapter3JobType.EmergencySupply, 15_500, 17, 18),
            LastTypeOffered = Chapter3JobType.EmergencySupply,
            NextOfferId = 10,
            LastEmergencySupplyOfferId = 9
        };

        var fallen = Chapter3JobScheduler.FallbackEmergencyOffer(board, before, PlayerId);

        Assert.Contains(fallen.OpenOffer!.JobType, Chapter3JobTuning.PaidJobTypes);
        Assert.Contains(Chapter3JobType.EmergencySupply, fallen.TypesSeen);
        Assert.Equal(5, fallen.LastEmergencySupplyOfferId);
    }

    [Fact]
    public void Force_rewrites_an_offer_to_emergency_supply()
    {
        var board = Chapter3JobBoard.Empty with
        {
            OpenOffer = new Chapter3JobOffer(2, Chapter3JobType.PackageRecovery, 15_500, 3, 4),
            LastTypeOffered = Chapter3JobType.PackageRecovery,
            TypesSeen = new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery },
            NextOfferId = 3
        };

        var forced = Chapter3JobScheduler.ForceEmergencyOffer(board);

        Assert.Equal(Chapter3JobType.EmergencySupply, forced.OpenOffer!.JobType);
        Assert.Equal(2, forced.OpenOffer.OfferId);
        Assert.Equal(15_500, forced.OpenOffer.Reward);
        Assert.Equal(Chapter3JobType.EmergencySupply, forced.LastTypeOffered);
        Assert.Contains(Chapter3JobType.EmergencySupply, forced.TypesSeen);
        Assert.Equal(2, forced.LastEmergencySupplyOfferId);
        Assert.Equal(3, forced.NextOfferId);
    }

    [Fact]
    public void Emergency_job_has_no_deadline_extension()
    {
        var board = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 5,
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.EmergencySupply, 17_500, 10, 12, false)
        };

        Assert.False(Chapter3JobScheduler.CanExtendDeadline(board, 11));

        var refused = Chapter3JobScheduler.TryExtendDeadline(board, 1, 11);

        Assert.False(refused.Extended);
        Assert.Equal(0, refused.Cost);
        Assert.Empty(refused.Effects);
    }

    [Fact]
    public void Unseen_preference_stays_inside_the_runnable_set()
    {
        var board = Chapter3JobBoard.Empty with
        {
            LastProcessedDay = 4,
            LastTypeOffered = Chapter3JobType.DeadDrop,
            TypesSeen = new[]
            {
                Chapter3JobType.DeadDrop,
                Chapter3JobType.PackageRecovery,
                Chapter3JobType.TemporaryStorageSafehouse,
                Chapter3JobType.EmergencySupply,
                Chapter3JobType.OperationalShutdown
            },
            NextOfferId = 6
        };

        var result = Tick(board, 5);

        var created = Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(result.Effects));
        Assert.Contains(created.Offer.JobType, Chapter3JobTuning.RunnableJobTypes);
    }

    [Fact]
    public void Seen_types_fall_back_inside_the_runnable_set()
    {
        var board = Chapter3JobBoard.Empty with
        {
            LastProcessedDay = 9,
            LastTypeOffered = Chapter3JobType.DeadDrop,
            TypesSeen = Enum.GetValues(typeof(Chapter3JobType)).Cast<Chapter3JobType>().ToArray(),
            NextOfferId = 7
        };

        var result = Tick(board, 10);

        var created = Assert.IsType<Chapter3OfferCreatedEffect>(Assert.Single(result.Effects));
        Assert.Contains(created.Offer.JobType, Chapter3JobTuning.RunnableJobTypes);
    }

    [Fact]
    public void Selection_is_deterministic_for_the_same_state()
    {
        Assert.Equal(RefuseCycles(6).Offers, RefuseCycles(6).Offers);
    }

    [Fact]
    public void Refusal_and_expiry_each_add_one_to_the_streak()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 0).Board;
        Assert.Equal(1, board.RefusalStreak);

        board = Tick(Tick(Tick(board, 1).Board, 2).Board, 3).Board;
        board = Tick(board, 4).Board;
        board = Tick(board, 5).Board;

        Assert.Equal(2, board.RefusalStreak);
    }

    [Fact]
    public void Completed_job_resets_the_streak()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 0).Board;
        board = Tick(Tick(Tick(board, 1).Board, 2).Board, 3).Board;
        board = Chapter3JobScheduler.Accept(board, 2, 3).Board;

        var completed = Chapter3JobScheduler.Complete(board, 1, succeeded: true, chapterDay: 4);

        Assert.Equal(0, completed.Board.RefusalStreak);
        var done = Assert.IsType<Chapter3JobCompletedEffect>(Assert.Single(completed.Effects));
        Assert.True(done.Succeeded);
    }

    [Fact]
    public void Accept_then_fail_keeps_the_streak_and_requires_a_make_good()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 0).Board;
        board = Tick(Tick(Tick(board, 1).Board, 2).Board, 3).Board;
        board = Chapter3JobScheduler.Accept(board, 2, 3).Board;
        var reward = board.ActiveJob!.Reward;

        var failed = Chapter3JobScheduler.Complete(board, 1, succeeded: false, chapterDay: 4);

        Assert.Equal(1, failed.Board.RefusalStreak);
        Assert.Equal(2, failed.Effects.Count);
        var done = Assert.IsType<Chapter3JobCompletedEffect>(failed.Effects[0]);
        Assert.False(done.Succeeded);
        var makeGood = Assert.IsType<Chapter3MakeGoodRequiredEffect>(failed.Effects[1]);
        Assert.Equal(reward, makeGood.ReferenceAmount);
    }

    [Fact]
    public void Deadline_passing_fails_the_job_without_touching_the_streak()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        var reward = board.ActiveJob!.Reward;

        board = Tick(board, 1).Board;
        board = Tick(board, 2).Board;
        Assert.NotNull(board.ActiveJob);
        var failed = Tick(board, 3);

        Assert.Null(failed.Board.ActiveJob);
        Assert.Equal(0, failed.Board.RefusalStreak);
        Assert.Equal(2, failed.Effects.Count);
        Assert.IsType<Chapter3JobCompletedEffect>(failed.Effects[0]);
        Assert.False(((Chapter3JobCompletedEffect)failed.Effects[0]).Succeeded);
        var makeGood = Assert.IsType<Chapter3MakeGoodRequiredEffect>(failed.Effects[1]);
        Assert.Equal(reward, makeGood.ReferenceAmount);
    }

    [Fact]
    public void Complete_success_records_the_completion_for_Nell()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        var job = board.ActiveJob!;

        var completed = Chapter3JobScheduler.Complete(board, job.JobId, succeeded: true, chapterDay: 1);

        Assert.Equal(new Chapter3JobCompletion(job.JobId, job.JobType, job.Reward), completed.Board.LastCompletedJob);
    }

    [Fact]
    public void Complete_failure_records_no_completion()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        var job = board.ActiveJob!;

        var failed = Chapter3JobScheduler.Complete(board, job.JobId, succeeded: false, chapterDay: 1);

        Assert.Null(failed.Board.LastCompletedJob);
    }

    [Fact]
    public void Consumed_unpaid_job_survives_its_deadline()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;
        var job = board.ActiveJob!;
        board = board with
        {
            ActiveJob = job with
            {
                Delivery = new Chapter3DeadDropDelivery(
                    "drop-a", "Drop A", 1d, 2d, 3d, "best", "Best", "brick", "Brick", 2, RemovalConfirmed: true)
            }
        };

        board = Tick(board, 1).Board;
        board = Tick(board, 2).Board;
        var ticked = Tick(board, 3);

        Assert.NotNull(ticked.Board.ActiveJob);
        Assert.DoesNotContain(ticked.Effects, effect => effect is Chapter3JobCompletedEffect);
        Assert.DoesNotContain(ticked.Effects, effect => effect is Chapter3MakeGoodRequiredEffect);
    }

    [Fact]
    public void Consumed_unpaid_recovery_survives_its_deadline()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Chapter3JobScheduler.Refuse(board, 1, 0).Board;
        board = Tick(board, 1).Board;
        board = Tick(board, 2).Board;
        board = Tick(board, 3).Board;
        Assert.Equal(Chapter3JobType.PackageRecovery, board.OpenOffer!.JobType);
        board = Chapter3JobScheduler.Accept(board, board.OpenOffer.OfferId, 3).Board;
        var job = board.ActiveJob!;
        board = board with
        {
            ActiveJob = job with
            {
                Recovery = new Chapter3PackageRecovery(
                    new Chapter3RecoveryDrop("a", "A", 1d, 2d, 3d),
                    new Chapter3RecoveryDrop("b", "B", 4d, 5d, 6d),
                    "best", "Best", "brick", "Brick", 2,
                    StagedSlotIndex: 0, Staged: true, Custody: true, RemovalConfirmed: true)
            }
        };

        board = Tick(board, 4).Board;
        board = Tick(board, 5).Board;
        var ticked = Tick(board, 6);

        Assert.NotNull(ticked.Board.ActiveJob);
        Assert.DoesNotContain(ticked.Effects, effect => effect is Chapter3JobCompletedEffect);
        Assert.DoesNotContain(ticked.Effects, effect => effect is Chapter3MakeGoodRequiredEffect);
    }

    [Fact]
    public void Nell_hint_fires_once_at_three()
    {
        var board = Chapter3JobBoard.Empty;
        var day = 0;
        for (var streak = 1; streak <= 3; streak++)
        {
            board = Tick(board, day).Board;
            var refused = Chapter3JobScheduler.Refuse(board, streak, day);
            board = refused.Board;
            if (streak == 3)
            {
                var hint = Assert.IsType<Chapter3NellHintEffect>(Assert.Single(refused.Effects));
                Assert.Equal(3, hint.RefusalStreak);
            }
            else
            {
                Assert.Empty(refused.Effects);
            }
            day += 3;
        }

        board = Tick(Tick(Tick(board, day - 2).Board, day - 1).Board, day).Board;
        var fourth = Chapter3JobScheduler.Refuse(board, 4, day);

        Assert.Empty(fourth.Effects);
        Assert.Equal(4, fourth.Board.RefusalStreak);
    }

    [Fact]
    public void Arthur_visit_fires_once_at_five()
    {
        var board = Chapter3JobBoard.Empty;
        var day = 0;
        for (var streak = 1; streak <= 5; streak++)
        {
            board = Tick(board, day).Board;
            var refused = Chapter3JobScheduler.Refuse(board, streak, day);
            board = refused.Board;
            if (streak == 5)
            {
                var visit = Assert.IsType<Chapter3ArthurCooperationVisitEffect>(Assert.Single(refused.Effects));
                Assert.Equal(5, visit.RefusalStreak);
            }
            day += 3;
        }

        board = Tick(Tick(Tick(board, day - 2).Board, day - 1).Board, day).Board;
        var sixth = Chapter3JobScheduler.Refuse(board, 6, day);

        Assert.Empty(sixth.Effects);
        Assert.Equal(6, sixth.Board.RefusalStreak);
    }

    [Fact]
    public void Success_rearms_the_streak_thresholds()
    {
        var board = Chapter3JobBoard.Empty;
        var day = 0;
        for (var streak = 1; streak <= 3; streak++)
        {
            board = Tick(board, day).Board;
            board = Chapter3JobScheduler.Refuse(board, streak, day).Board;
            day += 3;
        }
        Assert.True(board.NellHintIssued);

        board = Tick(Tick(Tick(board, day - 2).Board, day - 1).Board, day).Board;
        board = Chapter3JobScheduler.Accept(board, 4, day).Board;
        board = Chapter3JobScheduler.Complete(board, 1, succeeded: true, chapterDay: day).Board;
        Assert.Equal(0, board.RefusalStreak);
        Assert.False(board.NellHintIssued);

        day += 3;
        for (var streak = 1; streak <= 3; streak++)
        {
            board = Tick(board, day).Board;
            var refused = Chapter3JobScheduler.Refuse(board, 4 + streak, day);
            board = refused.Board;
            if (streak == 3)
                Assert.IsType<Chapter3NellHintEffect>(Assert.Single(refused.Effects));
            day += 3;
        }
    }

    [Fact]
    public void Extension_is_locked_before_the_third_success()
    {
        var board = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 2,
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };

        var result = Chapter3JobScheduler.TryExtendDeadline(board, 1, 11);

        Assert.False(result.Extended);
        Assert.Equal(0, result.Cost);
        Assert.Empty(result.Effects);
        Assert.Same(board, result.Board);
    }

    [Fact]
    public void Extension_adds_a_day_for_a_quarter_of_the_reward()
    {
        var board = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 3,
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };

        var result = Chapter3JobScheduler.TryExtendDeadline(board, 1, 11);

        Assert.True(result.Extended);
        Assert.Equal(4_375, result.Cost);
        Assert.Equal(13, result.Board.ActiveJob!.DeadlineDay);
        Assert.True(result.Board.ActiveJob.ExtensionUsed);
        var extended = Assert.IsType<Chapter3DeadlineExtendedEffect>(Assert.Single(result.Effects));
        Assert.Equal(1, extended.JobId);
        Assert.Equal(13, extended.NewDeadlineDay);
        Assert.Equal(4_375, extended.Cost);
    }

    [Fact]
    public void Extension_is_once_per_job()
    {
        var board = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 3,
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };
        board = Chapter3JobScheduler.TryExtendDeadline(board, 1, 11).Board;

        var again = Chapter3JobScheduler.TryExtendDeadline(board, 1, 11);

        Assert.False(again.Extended);
        Assert.Equal(0, again.Cost);
        Assert.Empty(again.Effects);
    }

    [Fact]
    public void Extension_is_allowed_on_the_deadline_day_but_not_after()
    {
        var board = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 3,
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };

        Assert.True(Chapter3JobScheduler.TryExtendDeadline(board, 1, 12).Extended);

        var late = Chapter3JobBoard.Empty with
        {
            SuccessfulJobs = 3,
            ActiveJob = new Chapter3ActiveJob(2, 2, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };

        Assert.False(Chapter3JobScheduler.TryExtendDeadline(late, 2, 13).Extended);
    }

    [Fact]
    public void Extension_needs_the_live_job()
    {
        var board = Chapter3JobBoard.Empty with { SuccessfulJobs = 3 };

        Assert.False(Chapter3JobScheduler.TryExtendDeadline(board, 1, 11).Extended);

        board = board with
        {
            ActiveJob = new Chapter3ActiveJob(1, 1, Chapter3JobType.DeadDrop, 17_500, 10, 12, false)
        };

        Assert.False(Chapter3JobScheduler.TryExtendDeadline(board, 999, 11).Extended);
    }

    [Fact]
    public void Success_increments_only_the_plain_success_counter()
    {
        var board = Chapter3JobScheduler.Accept(Tick(Chapter3JobBoard.Empty, 0).Board, 1, 0).Board;

        var completed = Chapter3JobScheduler.Complete(board, 1, succeeded: true, chapterDay: 1);

        Assert.Equal(1, completed.Board.SuccessfulJobs);
        Assert.Equal(0, completed.Board.SuccessfulJobsSinceWarehousePurchase);
    }

    [Theory]
    [InlineData(17, 5, false, false)]
    [InlineData(18, 5, false, true)]
    [InlineData(18, 4, false, false)]
    [InlineData(18, 5, true, false)]
    [InlineData(19, 6, false, true)]
    [InlineData(0, 0, false, false)]
    public void Warehouse_eligibility_needs_day_successes_and_no_make_good(
        int chapterDay, int successes, bool hasOpenMakeGood, bool expected)
    {
        Assert.Equal(expected, Chapter3JobScheduler.IsWarehouseOfferEligible(chapterDay, successes, hasOpenMakeGood));
    }

    [Fact]
    public void Accepting_the_same_offer_twice_answers_once()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        var first = Chapter3JobScheduler.Accept(board, 1, 0);

        var again = Chapter3JobScheduler.Accept(first.Board, 1, 0);

        Assert.Same(first.Board, again.Board);
        Assert.Empty(again.Effects);
    }

    [Fact]
    public void Refusing_the_same_offer_twice_counts_once()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        var first = Chapter3JobScheduler.Refuse(board, 1, 0);

        var again = Chapter3JobScheduler.Refuse(first.Board, 1, 0);

        Assert.Same(first.Board, again.Board);
        Assert.Empty(again.Effects);
        Assert.Equal(1, again.Board.RefusalStreak);
    }

    [Fact]
    public void Ticks_for_processed_days_change_nothing()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;
        board = Tick(board, 1).Board;

        var repeat = Tick(board, 1);
        var older = Tick(board, 0);

        Assert.Same(board, repeat.Board);
        Assert.Same(board, older.Board);
        Assert.Empty(repeat.Effects);
        Assert.Empty(older.Effects);
    }

    [Fact]
    public void Unknown_ids_are_noops()
    {
        var board = Tick(Chapter3JobBoard.Empty, 0).Board;

        Assert.Same(board, Chapter3JobScheduler.Refuse(board, 999, 0).Board);
        Assert.Same(board, Chapter3JobScheduler.Accept(board, 999, 0).Board);
        Assert.Same(board, Chapter3JobScheduler.Complete(board, 999, succeeded: true, chapterDay: 0).Board);
        Assert.Empty(Chapter3JobScheduler.Complete(board, 999, succeeded: true, chapterDay: 0).Effects);
    }

    [Fact]
    public void Accept_after_expiry_is_stale()
    {
        var board = Tick(Tick(Chapter3JobBoard.Empty, 0).Board, 1).Board;
        board = Tick(board, 2).Board;

        var late = Chapter3JobScheduler.Accept(board, 1, 2);

        Assert.Same(board, late.Board);
        Assert.Empty(late.Effects);
    }

    private static (List<Chapter3JobOffer> Offers, Chapter3JobBoard Board) RefuseCycles(int count)
    {
        var offers = new List<Chapter3JobOffer>();
        var board = Chapter3JobBoard.Empty;
        var day = 0;
        var offerId = 0;
        while (offers.Count < count)
        {
            var ticked = Tick(board, day);
            board = ticked.Board;
            foreach (var effect in ticked.Effects)
            {
                if (effect is Chapter3OfferCreatedEffect created)
                {
                    offers.Add(created.Offer);
                    offerId = created.Offer.OfferId;
                    board = Chapter3JobScheduler.Refuse(board, offerId, day).Board;
                }
            }
            day++;
        }
        return (offers, board);
    }
}
