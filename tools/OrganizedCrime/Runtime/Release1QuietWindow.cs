using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>What one census pass should do. Pure; the mission service performs the effect.</summary>
public enum Release1QuietDecision
{
    Hold,
    ConfirmClear,
    ClearBreach,
    RecordBreach,
    FailWindow,
    Complete,
    None
}

/// <summary>
/// Keep the Lights Off's view of the shared <see cref="Release1ConditionWindow"/>: a thin mapping from
/// the census vocabulary and the mission's own progress record into the engine's projection, and back
/// from the engine decision into this mission's decision enum. The grace protects the player from a
/// single pass that finds the product briefly present, because closing a dead drop's UI a frame after
/// taking the last unit out can still read as present on that exact pass. Only a second consecutive pass
/// at least BreachGraceGameMinutes later that still shows presence can fail the window.
///
/// This mission is HealBeforeElapse: a clean pass that carries a stale breach mark heals the mark this
/// pass and completes on the next one, which is exactly what it shipped and what passed live on
/// 2026-09-03.
/// </summary>
public static class Release1QuietWindow
{
    public const double BreachGraceGameMinutes = 60d;

    public static Release1QuietDecision Decide(
        Release1QuietCensusObservation observation,
        Release1KeepTheLightsOffProgress progress,
        double nowGameMinutes,
        double windowDurationGameMinutes)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var decision = Release1ConditionWindow.Decide(
            Observe(observation),
            // Satisfied is constant false: this mission has no satisfaction latch and is gated out
            // instead by the existence of its reward effect, which ReconcileCensus checks above the
            // call.
            new Release1WindowProgress(
                progress.ClearConfirmedAtGameMinutes,
                progress.BreachSincePassGameMinutes,
                false),
            nowGameMinutes,
            new Release1WindowProfile(
                windowDurationGameMinutes,
                BreachGraceGameMinutes,
                Release1WindowHealPolicy.HealBeforeElapse));

        return decision switch
        {
            Release1WindowDecision.OpenWindow => Release1QuietDecision.ConfirmClear,
            Release1WindowDecision.ClearBreach => Release1QuietDecision.ClearBreach,
            Release1WindowDecision.RecordBreach => Release1QuietDecision.RecordBreach,
            Release1WindowDecision.Elapsed => Release1QuietDecision.Complete,
            Release1WindowDecision.Fail => Release1QuietDecision.FailWindow,
            Release1WindowDecision.None => Release1QuietDecision.None,
            _ => Release1QuietDecision.Hold
        };
    }

    private static Release1WindowObservation Observe(Release1QuietCensusObservation observation) => observation switch
    {
        Release1QuietCensusObservation.Clear => Release1WindowObservation.Clean,
        Release1QuietCensusObservation.Present => Release1WindowObservation.Breached,
        _ => Release1WindowObservation.Unreadable
    };
}
