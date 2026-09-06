namespace OrganizedCrime.PoliceDispatchProof;

public static class DispatchProofClassifier
{
    public static DispatchProofClassification Classify(DispatchProofRun run)
    {
        var reasons = new List<string>();

        if (run.MorePatrolsPresent)
            return Stop("MorePatrols is active");
        if (run.UnsupportedPlayerObserved)
            return Stop("a non-supported player was observed in the single-player proof scope");
        if (run.DuplicateOrUnboundedInvocationObserved)
            return Stop("duplicate or unbounded Dispatch invocation was observed");
        if (run.PoolOrPatrolCorruptionObserved)
            return Stop("pool or patrol corruption was observed");
        if (run.SessionOrSaveCorruptionObserved)
            return Stop("session or save state became unrecoverable");
        if (string.IsNullOrWhiteSpace(run.CanonicalTargetIdentity))
            return Stop("canonical target identity is unavailable");
        if (run.Responses.Count == 0 || run.Responses.Count > 2)
            return new(DispatchProofDecision.Inconclusive, new[] { "one or two bounded response observations are required" });
        if (run.Responses.Count == 2 &&
            !run.Responses.Select(response => response.ResponseNumber).OrderBy(number => number).SequenceEqual(new[] { 1, 2 }))
            return Stop("response observations are not exactly response 1 and response 2");
        if (run.Responses.Count == 1 && run.Responses[0].ResponseNumber is not (1 or 2))
            return Stop("single response observation has an invalid response number");

        foreach (var response in run.Responses.OrderBy(response => response.ResponseNumber))
        {
            if (response.UnrelatedPlayerEffectObserved)
                return Stop($"response {response.ResponseNumber} affected an unrelated player");
            if (response.PersistentExceptionObserved)
                return Stop($"response {response.ResponseNumber} left a persistent exception state");
            if (!response.TargetAttributionAvailable ||
                !string.Equals(run.CanonicalTargetIdentity, response.ExpectedTargetIdentity, StringComparison.Ordinal) ||
                !string.Equals(response.ExpectedTargetIdentity, response.ObservedTargetIdentity, StringComparison.Ordinal))
                return Stop($"response {response.ResponseNumber} target attribution was wrong or unavailable");
            if (response.DuplicateInvocationObserved)
                return Stop($"response {response.ResponseNumber} duplicated its invocation");

            var consumed = response.ConsumedOfficerCount;
            if (response.CapacityShortfallObserved ||
                (consumed.IsAvailable && consumed.Value < DispatchProofPins.RequestedOfficerCount))
            {
                return new(DispatchProofDecision.NeedsOptionB, new[]
                {
                    $"response {response.ResponseNumber} did not produce the required two-officer native consumption"
                });
            }
            if (!response.DispatchCallCompleted)
                return new(DispatchProofDecision.NeedsOptionB, new[] { $"response {response.ResponseNumber} did not complete native admission" });
            if (!consumed.IsAvailable || !response.RecoveryObservationAvailable)
                return new(DispatchProofDecision.Inconclusive, new[] { $"response {response.ResponseNumber} capacity or recovery observations were unavailable" });
            if (!response.VehicleResponseObserved)
                return new(DispatchProofDecision.NeedsOptionB, new[] { $"response {response.ResponseNumber} did not produce a vehicle response" });
            if (!response.RecoveryAdequate)
                return new(DispatchProofDecision.NeedsOptionB, new[] { $"response {response.ResponseNumber} did not recover native capacity adequately" });
            if (!response.DeathObservationAvailable || !response.NaturalDeathObserved)
                reasons.Add($"response {response.ResponseNumber}: ordinary live capacity recovery passed; natural officer death/re-pooling was not observed");
        }

        if (run.Responses.Count != 2)
            return new(DispatchProofDecision.Inconclusive, new[] { "one complete recovered response is insufficient for PassOptionA" });

        reasons.Insert(0, "PASS OPTION A: both owner-triggered responses were correctly attributed, fully staffed, vehicle-backed, and recovered without OC intervention");
        return new(DispatchProofDecision.PassOptionA, reasons);
    }

    private static DispatchProofClassification Stop(string reason) =>
        new(DispatchProofDecision.Stop, new[] { reason });
}
