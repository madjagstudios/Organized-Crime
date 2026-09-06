using System.Globalization;

namespace OrganizedCrime.Model;

/// <summary>One planned closet slot: its index, the balance read immediately before the decrement, and the exact balance expected immediately after it.</summary>
public sealed record Release1TheEnvelopePlannedSlot(int SlotIndex, double PreBalance, double PostBalance);

/// <summary>One closet cash slot as observed this pass.</summary>
public sealed record Release1TheEnvelopeCashSlot(int SlotIndex, double Balance);

/// <summary>
/// The Envelope's native effect payload: the whole ordered per slot plan, so every slot the effect
/// touches is verified against the exact pre and post balance the plan froze. Replaces the single
/// slot Release1TheEnvelopeCashIdentity, which could not express a twenty stack deposit. The
/// depositing closet lives on the effect's SourceIdentity, not in the encoding, so a twenty group
/// plan stays inside the 256 character identity bound. Whole dollar only: a fractional pre balance
/// makes the plan unencodable and the deposit holds rather than consuming anything.
/// </summary>
public sealed record Release1TheEnvelopeClosetPlan
{
    private const string Prefix = "te-closet-v1";
    private const char GroupDelimiter = '|';
    private const char FieldDelimiter = ',';
    public const int MaximumEncodedLength = 256;
    public const double WholeDollarTolerance = 0.01d;

    public Release1TheEnvelopeClosetPlan(string closetGuid, IReadOnlyList<Release1TheEnvelopePlannedSlot> slots)
    {
        Release1SmallCourtesyAssignment.ValidateStableId(closetGuid, nameof(closetGuid));
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0) throw new ArgumentException("A plan touches at least one slot.", nameof(slots));

        var lastIndex = -1;
        var total = 0d;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i] ?? throw new ArgumentException("Planned slots cannot be null.", nameof(slots));
            if (slot.SlotIndex < 0 || slot.SlotIndex <= lastIndex)
                throw new ArgumentException("Planned slots are strictly ascending by slot index.", nameof(slots));
            lastIndex = slot.SlotIndex;
            if (!double.IsFinite(slot.PreBalance) || slot.PreBalance <= 0d || slot.PreBalance != Math.Truncate(slot.PreBalance) ||
                !double.IsFinite(slot.PostBalance) || slot.PostBalance < 0d || slot.PostBalance >= slot.PreBalance ||
                slot.PostBalance != Math.Truncate(slot.PostBalance))
                throw new ArgumentException("Every planned balance is a whole dollar value, pre positive and post below it.", nameof(slots));
            if (i < slots.Count - 1 && slot.PostBalance != 0d)
                throw new ArgumentException("Only the last planned slot may keep a remainder.", nameof(slots));
            total += slot.PreBalance - slot.PostBalance;
        }

        ClosetGuid = closetGuid;
        Slots = slots.ToArray();
        ConsumedAmount = total;

        if (BuildEncoded().Length > MaximumEncodedLength)
            throw new ArgumentException("Closet plan exceeds the native effect field limit.", nameof(slots));
    }

    public string ClosetGuid { get; }
    public IReadOnlyList<Release1TheEnvelopePlannedSlot> Slots { get; }
    public double ConsumedAmount { get; }

    public string Serialize() => BuildEncoded();

    /// <summary>Whole stacks first in ascending slot index order, one partial stack last; surplus slots are never in the plan. Fails closed on a fractional pre balance, a sum below the amount, or an encoding past the 256 character bound.</summary>
    public static bool TryBuild(
        IReadOnlyList<Release1TheEnvelopeCashSlot>? cashSlots,
        string closetGuid,
        double amountWholeDollars,
        out Release1TheEnvelopeClosetPlan? plan)
    {
        plan = null;
        if (cashSlots is null || cashSlots.Count == 0) return false;
        if (!double.IsFinite(amountWholeDollars) || amountWholeDollars <= 0d ||
            amountWholeDollars != Math.Truncate(amountWholeDollars))
            return false;

        var planned = new List<Release1TheEnvelopePlannedSlot>();
        var remaining = amountWholeDollars;
        foreach (var slot in cashSlots.Where(candidate => candidate is not null).OrderBy(candidate => candidate.SlotIndex))
        {
            if (remaining <= 0d) break;
            if (!double.IsFinite(slot.Balance) || slot.Balance <= 0d) return false;
            var whole = Math.Round(slot.Balance, MidpointRounding.AwayFromZero);
            if (Math.Abs(slot.Balance - whole) > WholeDollarTolerance || whole <= 0d) return false;
            var take = Math.Min(whole, remaining);
            planned.Add(new(slot.SlotIndex, whole, whole - take));
            remaining -= take;
        }
        if (remaining > 0d) return false;

        try
        {
            plan = new(closetGuid, planned);
            return true;
        }
        catch (ArgumentException)
        {
            plan = null;
            return false;
        }
    }

    public static bool TryParse(string? encoded, string closetGuid, out Release1TheEnvelopeClosetPlan? plan)
    {
        plan = null;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumEncodedLength) return false;
        var groups = encoded.Split(GroupDelimiter);
        if (groups.Length < 2 || !string.Equals(groups[0], Prefix, StringComparison.Ordinal)) return false;

        var slots = new List<Release1TheEnvelopePlannedSlot>();
        for (var i = 1; i < groups.Length; i++)
        {
            var fields = groups[i].Split(FieldDelimiter);
            if (fields.Length != 3 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var slotIndex) ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pre) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var post))
                return false;
            slots.Add(new(slotIndex, pre, post));
        }

        // The constructor is the whole validator; a round trip check then rejects any encoding that
        // is not this type's own canonical form, such as padded or reordered groups.
        try { plan = new(closetGuid, slots); } catch (ArgumentException) { return false; }
        if (string.Equals(plan.Serialize(), encoded, StringComparison.Ordinal)) return true;
        plan = null;
        return false;
    }

    private string BuildEncoded() => string.Join(GroupDelimiter,
        new[] { Prefix }.Concat(Slots.Select(slot => string.Join(FieldDelimiter,
            slot.SlotIndex.ToString(CultureInfo.InvariantCulture),
            slot.PreBalance.ToString("0", CultureInfo.InvariantCulture),
            slot.PostBalance.ToString("0", CultureInfo.InvariantCulture)))));
}
