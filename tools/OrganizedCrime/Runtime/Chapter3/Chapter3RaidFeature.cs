using System.Globalization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

internal sealed class Chapter3RaidFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private readonly IChapter3RaidWorld _world;
    private readonly IChapter3RaidSeizureWorld _seizure;
    private readonly Func<Chapter3RaidInput> _input;
    private readonly Func<string, bool> _cooling;
    private readonly Func<string, bool?>? _coolingApplied;
    private readonly Action<string> _report;
    private readonly Action<string> _seizureReport;
    private readonly HashSet<string> _seizureWarnings = new(StringComparer.Ordinal);
    private string? _seizureWarningRaid;
    private enum RefusalCategory
    {
        Suspended, Host, Clock, UnknownSite, Census, InProgress, Cooling,
        Expired, Window, Ambiguous, Sweep, Protection, Arrest, Cap,
        NoPlan, Restoration, Occupancy
    }
    private string? _lastRefusal;
    private RefusalCategory? _refusalKey;
    private long? _refusalEvening;
    private bool _attemptIsQa, _lastRefusalWasQa;
    private readonly Chapter3RaidAwayTracker _away = new();
    private (string? Code, bool Force, double Deadline)? _queued;
    private string? _target;
    private long? _evening;
    private bool _abandoned, _skipCap, _suspended;
    private string? _forcedRaid;
    internal const double WeekMinutes = 7d * 1440d;
    internal const double WindowStart = 18d * 60d;
    internal const double WindowEnd = 23d * 60d;

    internal Chapter3RaidFeature(Chapter3FeatureContext context, IChapter3RaidWorld world,
        IChapter3RaidSeizureWorld seizure, Func<Chapter3RaidInput> input, Func<string, bool> cooling,
        Func<string, bool?>? coolingApplied = null, Action<string>? report = null, Action<string>? seizureReport = null)
    { _ctx = context; _world = world; _seizure = seizure; _input = input; _cooling = cooling; _coolingApplied = coolingApplied; _report = report ?? OrganizedCrimeLog.Warning; _seizureReport = seizureReport ?? OrganizedCrimeLog.Warning; }

    private Chapter3RaidInput ReadInput()
    { try { return _input(); } catch { return new(false, null, null, null, null); } }
    private bool Ready(Chapter3RaidInput input) => !_suspended && !_ctx.Disposed &&
        _ctx.Phase == Chapter3RuntimePhase.Active && _ctx.State?.Phase == Chapter3Phase.Active && input.IsHost;

    internal void Suspend() { if (_queued is not null) { _attemptIsQa = true; Refuse("raid suspended for save/load", RefusalCategory.Suspended); } _suspended = true; _away.Reset(); _queued = null; _forcedRaid = null; _skipCap = false; }
    internal void Resume() { _suspended = false; }
    internal void ResetTransient()
    {
        _seizureWarningRaid = null; _seizureWarnings.Clear();
        _suspended = false; _away.Reset(); _queued = null; _target = null; _evening = null; _abandoned = false; _skipCap = false; _forcedRaid = null; _lastRefusal = null; _refusalKey = null; _refusalEvening = null; _attemptIsQa = false; _lastRefusalWasQa = false;
    }
    internal Chapter3QaResult Queue(string? code, bool forceAway)
    {
        // OC-182: owner-typed `oc_ch3 raid now` is an explicit re-arm
        // exception: every QA requeue must get its refusal reason again.
        // Repeated automatic polls of that request still dedupe by category.
        _refusalKey = null; _attemptIsQa = true;
        var input = ReadInput();
        if (!Ready(input)) return QueueRefusal("active host Chapter 3 required", RefusalCategory.Host);
        var now = input.Minutes;
        if (now is not { } minutes || !Chapter3Sweep.ValidMinutes(minutes)) return QueueRefusal("clock unavailable", RefusalCategory.Clock);
        if (code is not null)
        {
            try
            {
                var sites = _world.Read(Chapter3RaidExclusions.From(_ctx.State));
                if (sites.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sites.Count)
                    return QueueRefusal("site census unavailable", RefusalCategory.Census);
                var site = sites.SingleOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (site is null)
                    return QueueRefusal($"unknown site '{code}' (known: {string.Join(", ", sites.Select(s => s.Code).OrderBy(s => s, StringComparer.Ordinal))})", RefusalCategory.UnknownSite, preserveResponse: true);
                code = site.Code;
            }
            catch { return QueueRefusal("site census unavailable", RefusalCategory.Census); }
        }
        _lastRefusal = null;
        var time = minutes % 1440d;
        _queued = (code, forceAway, time >= WindowStart && time < WindowEnd
            ? Math.Floor(minutes / 1440d) * 1440d + WindowEnd : minutes + WindowEnd - WindowStart);
        return new(true, $"raid now queued; QA: tier gate bypassed (current tier {input.Tier?.ToString() ?? "unknown"}); protection, sweep, arrest, restoration and cap checks still apply.");
    }
    internal Chapter3QaResult SkipCap()
    {
        if (!Ready(ReadInput())) return new(false, "raid refused: active host Chapter 3 required.");
        _skipCap = true;
        return new(true, "raid cap skip armed for one successful raid.");
    }

    // Runs under the coordinator's existing gate. One planned slot per pass
    // makes each intermediate receipt visible to save snapshots and owner QA.
    internal void PollLocked()
    {
        _attemptIsQa = _queued is not null;
        var input = ReadInput();
        if (!Ready(input)) { if (_queued is not null) Refuse("active host Chapter 3 required", RefusalCategory.Host); _away.Reset(); return; }
        if (input.Minutes is not { } now || !Chapter3Sweep.ValidMinutes(now)) { if (_queued is not null) Refuse("clock unavailable", RefusalCategory.Clock); _away.Reset(); return; }
        // Reset reporting before any pending-receipt early return. This clock
        // identity is separate from the gameplay target/away evening state.
        var refusalDay = (long)Math.Floor(now / 1440d);
        if (_refusalEvening != refusalDay) { _refusalEvening = refusalDay; _refusalKey = null; }
        try
        {
            var raid = _ctx.State!.Raid;
            ReportConfirmedPreStock(raid);
            if (raid is { Completed: false })
            {
                if (_queued is not null) Refuse("raid already in progress", RefusalCategory.InProgress);
                if (!_seizure.IsRestored(raid.SiteCode)) { _away.Reset(); return; }
                var next = raid.Slots.Select((slot, index) => (slot, index))
                    .FirstOrDefault(s => s.slot.State != Chapter3RaidRemovalState.Confirmed);
                if (next.slot is null) { Set(raid with { Completed = true }); Finish(); return; }
                if (next.slot.State == Chapter3RaidRemovalState.Ambiguous)
                { Set(raid with { Completed = true }); Finish(); return; }
                if (next.slot.State == Chapter3RaidRemovalState.Pending)
                {
                    // Read-only reconciliation is safe even after the evening,
                    // under cover or arrest; a further take is gated below.
                    if (next.slot.PreFingerprintTotal is null) { Ambiguous(next.index); Finish(); return; }
                    if (!TryReadTotal(raid.SiteCode, next.slot.Take, out var total)) return;
                    var pre = PendingPreTotal(raid, next.slot);
                    if (total == pre - next.slot.Take.Packages)
                    { Confirm(next.index); EndIfExpired(now); Finish(); return; }
                    if (total != pre) { Ambiguous(next.index); Finish(); return; }
                    if (!TryRead(raid, next.slot.Take, out var read)) return;
                    if (!IsPre(next.slot.Take, read)) { Ambiguous(next.index); Finish(); return; }
                }
                if (now < raid.StartMinutes || now >= raid.DeadlineMinutes)
                { Set(raid with { Completed = true }); Finish(); return; }
                if (!Safe(input, now)) { _away.Reset(); Finish(); return; }
                var site = ReadSite(raid.SiteCode, now, out var clear);
                if (site is null || !site.StockKnown || (!clear && _forcedRaid != raid.Id)) { Finish(); return; }
                // Re-read exclusions on every pass. Stock reserved by a new job
                // since freeze cannot become permission for a raid write.
                if (!site.Slots.Any(s => s.Identity == next.slot.Take.Identity)) { Ambiguous(next.index); Finish(); return; }
                Take(next.index);
                Finish();
                return;
            }
            Finish();
            // Preserve an unsent notice; do not overwrite its journal with a
            // later raid while presentation/cooling is still unavailable.
            raid = _ctx.State!.Raid;
            if (raid?.ActualUnits > 0 && (!raid.CoolingApplied || raid.NoticeReceipt is null ||
                (_coolingApplied is not null && _coolingApplied(raid.Id) != true))) { Refuse("previous raid cooling/notice pending", RefusalCategory.Cooling); return; }
            var queued = _queued; _queued = null;
            var day = (long)Math.Floor(now / 1440d);
            if (_evening != day) { _evening = day; _target = null; _abandoned = false; _away.Reset(); }
            var time = now % 1440d;
            if (queued is { } request && now >= request.Deadline) { Refuse("queued raid expired", RefusalCategory.Expired); return; }
            if (queued is null && (time < WindowStart || time >= WindowEnd))
            {
                // Idle polling must not replace a consumed QA refusal before
                // the owner can read it. Critical natural attempts are visible.
                if (input.Tier == LocalPressureTier.Critical && (!_lastRefusalWasQa || _lastRefusal is null))
                    Refuse("outside 18:00-23:00 for natural raid", RefusalCategory.Window);
                _target = null; _abandoned |= time >= WindowEnd; _away.Reset(); return;
            }
            if (raid?.Slots.Any(s => s.State == Chapter3RaidRemovalState.Ambiguous) == true)
            { Refuse("previous raid ambiguous hold", RefusalCategory.Ambiguous); return; }
            if (queued is null && raid is { Completed: true } && Math.Floor(raid.StartMinutes / 1440d) == day) return;
            if (queued is null && (_abandoned || input.Tier != LocalPressureTier.Critical)) return;
            var safety = SafetyRefusal(input, now);
            if (safety is { } refusal) { Refuse(refusal.Reason, refusal.Category); _away.Reset(); return; }
            if (!_skipCap && _ctx.State.LastRaidSuccessMinutes is { } last && now < last + WeekMinutes)
            { Refuse($"cap; remaining minutes={Number(last + WeekMinutes - now)}", RefusalCategory.Cap); _away.Reset(); return; }
            var sites = _world.Read(Chapter3RaidExclusions.From(_ctx.State));
            if (sites.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sites.Count)
            { Refuse("site census unavailable", RefusalCategory.Census); _away.Reset(); return; }
            var selected = queued?.Code is { } code ? sites.SingleOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) :
                _target is not null && queued is null ? sites.SingleOrDefault(s => s.Code == _target) : Chapter3RaidPlanner.Pick(sites);
            if (selected is null)
            {
                Refuse(queued?.Code is { } missing ? $"unknown site '{missing}'" : "no removable package/plan; no eligible site", queued?.Code is not null ? RefusalCategory.UnknownSite : RefusalCategory.NoPlan);
                if (queued is null) _abandoned = true; return;
            }
            if (queued is null) _target = selected.Code;
            if (!_seizure.IsRestored(selected.Code)) { Refuse($"restoration pending; site={selected.Code}", RefusalCategory.Restoration); _away.Reset(); return; }
            var away = _away.Observe(selected.Code, now, selected.Presence);
            if (!away.CanRaid && queued?.Force != true)
            {
                Refuse($"occupancy not clear; site={selected.Code}; presence={away.Presence.ToString().ToLowerInvariant()}; away minutes={Number(away.AwayMinutes)}",
                    RefusalCategory.Occupancy);
                // Keep manual intent while it waits for the same occupancy rule.
                if (queued is { } waiting) _queued = waiting with { Code = selected.Code };
                return;
            }
            var plan = Chapter3RaidPlanner.Plan(selected);
            if (plan is null) { Refuse($"no removable package/plan; site={selected.Code}", RefusalCategory.NoPlan); if (queued is null) _abandoned = true; return; }
            var slots = new List<Chapter3RaidRemoval>();
            var totals = new Dictionary<(Guid Storage, string Fingerprint), long>();
            foreach (var take in plan.Takes)
            {
                if (!_seizure.TryReadSlot(selected.Code, take.Identity, out var read) || read.Quantity != take.PreQuantity ||
                    string.IsNullOrWhiteSpace(read.Fingerprint)) { Refuse("no removable package/plan; slot read unavailable or changed", RefusalCategory.NoPlan); return; }
                // Compare live D7a identity before adopting mutation identity.
                // Older readers can corroborate it through a fresh census.
                var liveFingerprint = read.PlanFingerprint;
                if (liveFingerprint is null)
                {
                    var live = _world.Read(Chapter3RaidExclusions.From(_ctx.State))
                        .SingleOrDefault(s => s.Code == selected.Code);
                    if (live is null || !live.StockKnown) { Refuse("no removable package/plan; stock unavailable", RefusalCategory.NoPlan); return; }
                    liveFingerprint = live.Slots.SingleOrDefault(s => s.Identity == take.Identity)?.Fingerprint;
                }
                if (liveFingerprint != take.Fingerprint)
                { slots.Add(new(take, Chapter3RaidRemovalState.Ambiguous)); continue; }
                // D7a's units, package counts, order and budget stay frozen. HQ
                // journal identity uses product/packaging/quality, never value
                // or quantity, both of which can change during a valid take.
                var nativeTake = take with { Fingerprint = read.Fingerprint };
                var key = (nativeTake.Identity.StorageGuid, nativeTake.Fingerprint);
                if (!totals.TryGetValue(key, out var total))
                {
                    if (!TryReadTotal(selected.Code, nativeTake, out total))
                    { Refuse("no removable package/plan; storage total unavailable", RefusalCategory.NoPlan); return; }
                    totals.Add(key, total);
                }
                if (total < take.PreQuantity) { Refuse("no removable package/plan; storage total changed", RefusalCategory.NoPlan); return; }
                slots.Add(new(nativeTake) { PreFingerprintTotal = total });
            }
            var id = Guid.NewGuid().ToString("D");
            Set(new(id, selected.Code, selected.Name, now,
                queued is null ? day * 1440d + WindowEnd : queued.Value.Deadline,
                plan.TotalUnits, plan.Budget, slots.ToArray(), Completed: slots.Any(s => s.State == Chapter3RaidRemovalState.Ambiguous)));
            _lastRefusal = null; _refusalKey = null;
            _forcedRaid = queued?.Force == true ? id : null;
            if (queued is null) _abandoned = true;
        }
        catch { if (_ctx.State?.Raid is not { Completed: false }) Refuse("site census unavailable", RefusalCategory.Census); _away.Reset(); } // Pending intent was recorded before any world write.
    }

    private static string Number(double minutes) => minutes.ToString("0.##", CultureInfo.InvariantCulture);
    // OC-183: an old confirmed receipt can outlive its native save. Diagnose it,
    // never reconcile it by taking again or blocking a later eligible raid.
    private void ReportConfirmedPreStock(Chapter3RaidJournal? raid)
    {
        if (raid is null || !raid.Slots.Any(s => s.State == Chapter3RaidRemovalState.Confirmed)) return;
        try { if (!_seizure.IsRestored(raid.SiteCode)) return; } catch { return; }
        foreach (var slot in raid.Slots.Where(s => s.State == Chapter3RaidRemovalState.Confirmed))
        {
            if (slot.PreFingerprintTotal is not { } pre || !TryReadTotal(raid.SiteCode, slot.Take, out var total)) continue;
            var taken = ConfirmedPackages(raid, slot.Take);
            var post = pre - taken;
            // A refilled slot is not evidence. Only an unchanged complete
            // fingerprint total diagnoses an absent take; drift is uncertain.
            if (total != pre || total == post) continue;
            ReportSeizure(raid, slot.Take, "confirmed raid stock still at pre-quantity",
                $"container pre={pre}; expected container post={post}; container actual={total}; leaving stock untouched, no re-take");
        }
    }
    private void ReportSeizure(Chapter3RaidJournal raid, Chapter3RaidTake take, string reason, string action)
    {
        if (_seizureWarningRaid != raid.Id)
        { _seizureWarningRaid = raid.Id; _seizureWarnings.Clear(); }
        if (!_seizureWarnings.Add(reason)) return;
        // OC-84: ordinary Warning visibility, independent of refusal reporting.
        // One warning per raid/reason, even across slots or logger faults.
        try
        {
            _seizureReport($"{reason}; raid={raid.Id}; site={raid.SiteCode}; " +
                $"storage={take.Identity.StorageGuid:D}; slot={take.Identity.SlotIndex}; " +
                $"pre={take.PreQuantity}; expected post={take.PreQuantity - take.Packages}; {action}.");
        }
        catch { }
    }
    private void Refuse(string reason, RefusalCategory category)
    {
        _lastRefusal = $"raid refused: {reason}.";
        _lastRefusalWasQa = _attemptIsQa;
        // Detail always refreshes status; only a different reason category re-arms polling.
        if (_refusalKey == category) return;
        _refusalKey = category;
        // OC-84: refusals must be visible even with diagnostics off. A logger
        // fault must never change admission, queued intent or storage behavior.
        try { _report(_lastRefusal); } catch { }
    }
    private Chapter3QaResult QueueRefusal(string reason, RefusalCategory category, bool preserveResponse = false)
    { Refuse(reason, category); return new(false, preserveResponse ? reason : _lastRefusal!); }
    private (string Reason, RefusalCategory Category)? SafetyRefusal(Chapter3RaidInput input, double now)
    {
        if (_ctx.State?.Sweep is { ActiveStartMinutes: { } start, ActiveEndMinutes: { } end } && now >= start && now < end)
            return ($"sweep active; end minutes={Number(end)} (day {Math.Floor(end / 1440d)}, {((int)(end % 1440d) / 60):00}:{((int)(end % 60)):00})", RefusalCategory.Sweep);
        if (input.Protected != false) return (input.Protected == true ? "protection active" : "protection unknown", RefusalCategory.Protection);
        if (input.ArrestTransition != false) return (input.ArrestTransition == true ? "arrest transition active" : "arrest transition unknown", RefusalCategory.Arrest);
        return null;
    }
    private bool Safe(Chapter3RaidInput input, double now) => input.Protected == false && input.ArrestTransition == false &&
        !(_ctx.State?.Sweep is { ActiveStartMinutes: { } start, ActiveEndMinutes: { } end } && now >= start && now < end);

    private Chapter3RaidSite? ReadSite(string code, double now, out bool clear)
    {
        var site = _world.Read(Chapter3RaidExclusions.From(_ctx.State)).SingleOrDefault(s => s.Code == code);
        clear = _away.Observe(code, now, site?.Presence ?? Chapter3RaidPresence.Unknown).CanRaid;
        return site;
    }
    private bool TryRead(Chapter3RaidJournal raid, Chapter3RaidTake take, out Chapter3RaidSlotRead read)
    {
        read = null!;
        try { return _seizure.TryReadSlot(raid.SiteCode, take.Identity, out read) && read.Quantity >= 0; }
        catch { return false; }
    }
    private static bool IsPre(Chapter3RaidTake take, Chapter3RaidSlotRead read) => read.Quantity == take.PreQuantity && read.Fingerprint == take.Fingerprint;
    private static long ConfirmedPackages(Chapter3RaidJournal raid, Chapter3RaidTake take) => raid.Slots
        .Where(s => s.State == Chapter3RaidRemovalState.Confirmed && s.Take.Identity.StorageGuid == take.Identity.StorageGuid &&
            s.Take.Fingerprint == take.Fingerprint).Sum(s => (long)s.Take.Packages);
    private static long PendingPreTotal(Chapter3RaidJournal raid, Chapter3RaidRemoval slot) =>
        slot.PreFingerprintTotal!.Value - ConfirmedPackages(raid, slot.Take);

    private bool TryReadTotal(string code, Chapter3RaidTake take, out long total)
    {
        total = 0;
        try
        {
            if (_seizure is IChapter3RaidStorageReader native)
                return native.TryReadStorageQuantity(code, take.Identity.StorageGuid, take.Fingerprint, out total) && total >= 0;
            // Engine-free adapters expose their whole inventory through the
            // unfiltered census. Production resolves native storage directly.
            var site = _world.Read(new()).SingleOrDefault(s => s.Code == code);
            if (site is null || !site.StockKnown) return false;
            var identities = new HashSet<Chapter3RaidSlotIdentity>();
            foreach (var slot in site.Slots.Where(s => s.Identity.StorageGuid == take.Identity.StorageGuid))
            {
                if (!identities.Add(slot.Identity) || !_seizure.TryReadSlot(code, slot.Identity, out var read) ||
                    read.Quantity < 0 || (read.Quantity > 0 && string.IsNullOrWhiteSpace(read.Fingerprint))) return false;
                if (read.Fingerprint == take.Fingerprint) total = checked(total + read.Quantity);
            }
            return identities.Count > 0;
        }
        catch { return false; }
    }

    private void Take(int index)
    {
        var raid = _ctx.State!.Raid!; var take = raid.Slots[index].Take;
        var locked = false;
        try
        {
            locked = _seizure.TryLock(raid.SiteCode, take.Identity);
            if (!locked) return;
            if (!TryRead(raid, take, out var before)) return;
            if (!TryReadTotal(raid.SiteCode, take, out var beforeTotal)) return;
            if (raid.Slots[index].State == Chapter3RaidRemovalState.Pending)
            {
                var slot = raid.Slots[index];
                if (slot.PreFingerprintTotal is null) { Ambiguous(index); return; }
                var pre = PendingPreTotal(raid, slot);
                if (beforeTotal == pre - take.Packages) { Confirm(index); return; }
                if (beforeTotal != pre) { Ambiguous(index); return; }
            }
            if (!IsPre(take, before)) { Ambiguous(index); return; }
            var input = ReadInput();
            if (!Ready(input) || input.Minutes is not { } now || !Chapter3Sweep.ValidMinutes(now) ||
                now < raid.StartMinutes || now >= raid.DeadlineMinutes || !Safe(input, now) || !_seizure.IsRestored(raid.SiteCode))
            { _away.Reset(); return; }
            var site = ReadSite(raid.SiteCode, now, out var clear);
            if (site is null || !site.StockKnown || (!clear && _forcedRaid != raid.Id) ||
                !site.Slots.Any(s => s.Identity == take.Identity)) return;
            // Safety/census callbacks may expose a late restored take. Recheck
            // the complete total and exact target immediately before retrying.
            if (!TryReadTotal(raid.SiteCode, take, out beforeTotal)) return;
            if (raid.Slots[index].State == Chapter3RaidRemovalState.Pending)
            {
                var pre = PendingPreTotal(raid, raid.Slots[index]);
                if (beforeTotal == pre - take.Packages) { Confirm(index); return; }
                if (beforeTotal != pre) { Ambiguous(index); return; }
            }
            if (!TryRead(raid, take, out before)) return;
            if (!IsPre(take, before)) { Ambiguous(index); return; }
            ChangeSlot(index, Chapter3RaidRemovalState.Pending);
            try { _seizure.Take(raid.SiteCode, take.Identity, take.Packages); } catch { }
            if (!TryReadTotal(raid.SiteCode, take, out var afterTotal))
            { ReportSeizure(raid, take, "raid native read-back unavailable", "holding pending receipt"); return; }
            if (afterTotal == beforeTotal - take.Packages) Confirm(index);
            else if (afterTotal != beforeTotal)
            { ReportSeizure(raid, take, "raid native read-back divergent", "holding ambiguous receipt"); Ambiguous(index); }
            else ReportSeizure(raid, take, "raid native read-back still at pre-quantity", "holding pending receipt");
        }
        finally
        {
            if (locked) { try { _seizure.Unlock(raid.SiteCode, take.Identity); } catch { } }
        }
    }
    private void ChangeSlot(int index, Chapter3RaidRemovalState state)
    {
        var raid = _ctx.State!.Raid!;
        Set(raid with { Slots = raid.Slots.Select((s, i) => i == index ? s with { State = state } : s).ToArray() });
    }
    private void Confirm(int index)
    {
        ChangeSlot(index, Chapter3RaidRemovalState.Confirmed);
        var raid = _ctx.State!.Raid!;
        _ctx.State = _ctx.State with { LastRaidSuccessMinutes = raid.StartMinutes, Revision = _ctx.State.Revision + 1 };
        _skipCap = false;
        if (raid.Slots.All(s => s.State == Chapter3RaidRemovalState.Confirmed)) Set(raid with { Completed = true });
    }
    private void Ambiguous(int index)
    { ChangeSlot(index, Chapter3RaidRemovalState.Ambiguous); Set(_ctx.State!.Raid! with { Completed = true }); }
    private void EndIfExpired(double now)
    {
        var raid = _ctx.State!.Raid!;
        if (now < raid.StartMinutes || now >= raid.DeadlineMinutes) Set(raid with { Completed = true });
    }
    private void Finish()
    {
        var raid = _ctx.State!.Raid;
        if (raid is null || raid.ActualUnits == 0) return;
        try
        {
            // The LP schema-2 marker owns cooling truth across independent saves.
            // Unknown means wait for LP hydration; never guess from stale state.
            if (raid.CoolingApplied)
            {
                if (_coolingApplied is null || _coolingApplied(raid.Id) != false) return;
                // A known missing LP marker makes the journal receipt pending,
                // including if the retry below is refused or throws.
                raid = raid with { CoolingApplied = false };
                Set(raid);
            }
            if (_cooling(raid.Id)) Set(raid with { CoolingApplied = true });
        }
        catch { }
    }
    private void Set(Chapter3RaidJournal raid) => _ctx.State = _ctx.State! with { Raid = raid, Revision = _ctx.State.Revision + 1 };

    internal IReadOnlyList<string> Status()
    {
        var input = ReadInput();
        if (!input.IsHost) return new[] { "raid status: host only." };
        var raid = _ctx.State?.Raid;
        var cooldown = _ctx.State?.LastRaidSuccessMinutes is { } last && input.Minutes is { } now
            ? Math.Max(0d, last + WeekMinutes - now).ToString(CultureInfo.InvariantCulture) : "unknown/none";
        var lines = new List<string> { $"raid: suspended={_suspended}; waiting site={_target}; abandoned evening={_abandoned}; cooldown minutes={cooldown}; cap skip={_skipCap}." };
        if (_lastRefusal is not null) lines.Add(_lastRefusal);
        if (raid is null) { lines.Add("raid state=none."); return lines; }
        lines.Add($"raid {raid.Id}: state={(raid.Slots.Any(s => s.State == Chapter3RaidRemovalState.Ambiguous) ? "ambiguous hold" : raid.Completed ? "completed" : "seizing")}; site={raid.SiteCode} ({raid.SiteName}); total={raid.TotalUnits}; budget={raid.Budget}; planned={raid.PlannedUnits}; confirmed={raid.ActualUnits}; cooling applied={raid.CoolingApplied}; notice sent={raid.NoticeReceipt is not null}.");
        lines.AddRange(raid.Slots.Select(s => $"storage={s.Take.Identity.StorageGuid:D}; slot={s.Take.Identity.SlotIndex}; fingerprint={s.Take.Fingerprint}; pre={s.Take.PreQuantity}; post={s.PostQuantity}; packages={s.Take.Packages}; units={s.Take.Units}; state={s.State}."));
        return lines;
    }
}
