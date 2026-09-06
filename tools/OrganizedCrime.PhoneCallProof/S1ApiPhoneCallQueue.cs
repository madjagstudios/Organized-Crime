using S1API.PhoneCalls;

namespace OrganizedCrime.PhoneCallProof;

public sealed class S1ApiPhoneCallQueue : IPhoneCallQueue
{
    private readonly Func<bool> _isCallManagerAvailable;

    public S1ApiPhoneCallQueue(Func<bool>? isCallManagerAvailable = null)
    {
        _isCallManagerAvailable = isCallManagerAvailable ?? (() => true);
    }

    public bool TryQueue(PhoneCallProofRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_isCallManagerAvailable())
            return false;

        var definition = new OcPhoneCallDefinition(request.CallerName, request.StageTexts.ToArray());
        CallManager.QueueCall(definition);
        return true;
    }
}
