using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 payments feature (make-good, weekly-cut and
// deadline-extension taps, plus the week-pass cut step). Moved verbatim out
// of Chapter3RuntimeService; only the receiver changed. Called only while
// the coordinator holds its gate; takes no locks itself.
internal sealed class Chapter3PaymentsFeature
{
    private readonly Chapter3FeatureContext _ctx;

    public Chapter3PaymentsFeature(Chapter3FeatureContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    internal Chapter3MoneyResult TryPayMakeGoodLocked(int id)
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var entry = _ctx.State.Ledger.Entries.FirstOrDefault(candidate => candidate.Id == id);
        if (entry is null || entry.Status == Chapter3MakeGoodStatus.Paid)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        if (entry.Amount > Chapter3PaymentTuning.MaximumPayment)
        {
            _ctx.Log($"Chapter 3 payment refused: make-good {id} asks ${entry.Amount:N0}, past the ${Chapter3PaymentTuning.MaximumPayment:N0} bound.");
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        }
        if (entry.Amount <= 0)
        {
            var free = Chapter3MakeGoods.Pay(_ctx.State.Ledger, id);
            _ctx.State = _ctx.State with { Ledger = free.Ledger, Revision = _ctx.State.Revision + 1 };
            return new(Chapter3MoneyOutcome.Applied, 0);
        }
        var debit = _ctx.DebitLocked(entry.Amount);
        if (debit == Chapter3WalletDebit.Unavailable)
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        if (debit == Chapter3WalletDebit.Short)
        {
            _ctx.State = _ctx.State with { PaymentShortfallCount = _ctx.State.PaymentShortfallCount + 1, Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Chapter 3 make-good {id} short: the wallet cannot cover ${entry.Amount:N0}.");
            return new(Chapter3MoneyOutcome.Short, 0);
        }
        var paid = Chapter3MakeGoods.Pay(_ctx.State.Ledger, id);
        if (ReferenceEquals(paid.Ledger, _ctx.State.Ledger))
        {
            // Unreachable under this lock (the entry was payable when
            // read), but the money did move, so say so rather than
            // swallowing it.
            OrganizedCrimeLog.Warning($"Chapter 3 make-good {id} debit succeeded but the ledger no longer shows it payable.");
            return new(Chapter3MoneyOutcome.Applied, entry.Amount);
        }
        _ctx.State = _ctx.State with { Ledger = paid.Ledger, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in paid.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(effect));
        return new(Chapter3MoneyOutcome.Applied, entry.Amount);
    }

    internal Chapter3MoneyResult TryPayCutLocked(int weekIndex)
    {
        if (!_ctx.PresentationCommandReady(out var chapterDay))
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var entry = _ctx.State!.Cuts.Entries.FirstOrDefault(candidate => candidate.WeekIndex == weekIndex);
        if (entry is null || entry.Status != Chapter3WeeklyCutStatus.Due)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        if (entry.Amount > Chapter3PaymentTuning.MaximumPayment)
        {
            _ctx.Log($"Chapter 3 payment refused: week {weekIndex} cut asks ${entry.Amount:N0}, past the ${Chapter3PaymentTuning.MaximumPayment:N0} bound.");
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        }
        if (entry.Amount <= 0)
        {
            var free = Chapter3WeeklyCutTransitions.Pay(_ctx.State.Cuts, weekIndex, chapterDay);
            _ctx.State = _ctx.State with { Cuts = free.Cuts, Revision = _ctx.State.Revision + 1 };
            return new(Chapter3MoneyOutcome.Applied, 0);
        }
        var debit = _ctx.DebitLocked(entry.Amount);
        if (debit == Chapter3WalletDebit.Unavailable)
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        if (debit == Chapter3WalletDebit.Short)
        {
            _ctx.State = _ctx.State with { PaymentShortfallCount = _ctx.State.PaymentShortfallCount + 1, Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Chapter 3 week {weekIndex} cut short: the wallet cannot cover ${entry.Amount:N0}.");
            return new(Chapter3MoneyOutcome.Short, 0);
        }
        var paid = Chapter3WeeklyCutTransitions.Pay(_ctx.State.Cuts, weekIndex, chapterDay);
        if (ReferenceEquals(paid.Cuts, _ctx.State.Cuts))
        {
            OrganizedCrimeLog.Warning($"Chapter 3 week {weekIndex} cut debit succeeded but the cuts no longer show it due.");
            return new(Chapter3MoneyOutcome.Applied, entry.Amount);
        }
        _ctx.State = _ctx.State with { Cuts = paid.Cuts, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in paid.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatCutEffect(effect));
        return new(Chapter3MoneyOutcome.Applied, entry.Amount);
    }

    internal Chapter3MoneyResult TryBuyTimeLocked()
    {
        if (!_ctx.PresentationCommandReady(out var chapterDay))
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var job = _ctx.State!.JobBoard.ActiveJob;
        if (job is null)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        var extended = Chapter3JobScheduler.TryExtendDeadline(_ctx.State.JobBoard, job.JobId, chapterDay);
        if (!extended.Extended)
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        if (extended.Cost > Chapter3PaymentTuning.MaximumPayment)
        {
            _ctx.Log($"Chapter 3 payment refused: job {job.JobId} extension asks ${extended.Cost:N0}, past the ${Chapter3PaymentTuning.MaximumPayment:N0} bound.");
            return new(Chapter3MoneyOutcome.NotPayable, 0);
        }
        if (extended.Cost <= 0)
        {
            _ctx.State = _ctx.State with { JobBoard = extended.Board, Revision = _ctx.State.Revision + 1 };
            return new(Chapter3MoneyOutcome.Applied, 0);
        }
        var debit = _ctx.DebitLocked(extended.Cost);
        if (debit == Chapter3WalletDebit.Unavailable)
            return new(Chapter3MoneyOutcome.Unavailable, 0);
        if (debit == Chapter3WalletDebit.Short)
        {
            _ctx.State = _ctx.State with { PaymentShortfallCount = _ctx.State.PaymentShortfallCount + 1, Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Chapter 3 job {job.JobId} extension short: the wallet cannot cover ${extended.Cost:N0}.");
            return new(Chapter3MoneyOutcome.Short, 0);
        }
        _ctx.State = _ctx.State with { JobBoard = extended.Board, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in extended.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        return new(Chapter3MoneyOutcome.Applied, extended.Cost);
    }

    internal void ProcessWeekPassLocked(int weekIndex, int chapterDay)
    {
        var passed = Chapter3WeeklyCutTransitions.OnWeekPass(_ctx.State!.Cuts, weekIndex, chapterDay, _ctx.ReadOwnership());
        if (ReferenceEquals(passed.Cuts, _ctx.State.Cuts))
            return;
        _ctx.State = _ctx.State with { Cuts = passed.Cuts, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in passed.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatCutEffect(effect));
    }
}
