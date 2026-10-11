namespace OrganizedCrime.Model;

// OC-166: frozen terms and durable quiet-window/payment receipt.
internal sealed record Chapter3Shutdown(
    string PropertyCode,
    string PropertyName,
    double? QuietSince = null,
    double? BreachSince = null,
    int BreachObservations = 0,
    bool QuietComplete = false,
    double? CreditBefore = null,
    bool CreditAmbiguous = false,
    bool Paid = false)
{
    internal const double HoldMinutes = 720d;
    internal const double GraceMinutes = 60d;
    internal bool Reconciling => QuietComplete;

    internal void Validate(int reward, bool offered)
    {
        if (string.IsNullOrWhiteSpace(PropertyCode) || string.IsNullOrWhiteSpace(PropertyName) ||
            reward < 15000 || reward > 20000 || reward % 500 != 0)
            throw new ArgumentException("Shutdown terms were invalid.");
        foreach (var value in new[] { QuietSince, BreachSince, CreditBefore })
            if (value is { } number && (!double.IsFinite(number) || number < 0))
                throw new ArgumentException("Shutdown progress was invalid.");
        if ((BreachSince.HasValue && (!QuietSince.HasValue || BreachSince < QuietSince || BreachObservations == 0)) ||
            (!BreachSince.HasValue && BreachObservations != 0) || BreachObservations < 0 ||
            (QuietComplete && (!QuietSince.HasValue || BreachSince.HasValue)) ||
            (CreditBefore.HasValue && !QuietComplete) || (CreditAmbiguous && !CreditBefore.HasValue) ||
            (Paid && (!CreditBefore.HasValue || CreditAmbiguous)) ||
            (offered && (QuietSince.HasValue || BreachSince.HasValue || QuietComplete || CreditBefore.HasValue || Paid)))
            throw new ArgumentException("Shutdown progress was inconsistent.");
    }

    // The production snapshot contains owned properties only, by contract.
    internal static Chapter3Shutdown? Select(Release1ProductionActivitySnapshot? snapshot, string playerId, int offerId)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(playerId) || offerId <= 0) return null;
        try { snapshot.Validate(); }
        catch (ArgumentException) { return null; }
        var eligible = snapshot.Properties.Where(p => p.Employees.Any(e => e.IsAnyWorkInProgress) ||
            p.Stations.Any(s => s.Running)).OrderBy(p => p.PropertyCode, StringComparer.Ordinal).ToArray();
        if (eligible.Length == 0) return null;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"chapter3-shutdown|{playerId}|{offerId}"));
        var index = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash) % (uint)eligible.Length;
        var picked = eligible[index];
        return new(picked.PropertyCode, picked.PropertyName);
    }
}
