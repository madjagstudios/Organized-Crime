using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

public enum Release1KeepTheLightsOffAssignmentMode
{
    Primary,
    MakeGood,
    Recovery
}

public enum Release1KeepTheLightsOffSelectionStatus
{
    Selected,
    NoOwnedProperty,
    NoAssignedEmployee,
    InvalidInput
}

/// <summary>
/// One immutable Keep the Lights Off stage assignment, frozen in the same durable story revision as
/// the acceptance transition that authorizes it. There is no product, no dead drop, and no hold room:
/// the mission is a shutdown, and the only thing frozen at acceptance is how many properties the
/// player owned at that moment, so a later change in ownership is detected against a stable baseline
/// instead of silently shifting the goalposts mid attempt.
/// </summary>
public sealed record Release1KeepTheLightsOffAssignment
{
    public const double WindowGameMinutes = 1_440d;
    public const double TimedStageGameMinutes = 4_320d;

    private string _missionKey = string.Empty;
    private int _attempt;
    private Release1KeepTheLightsOffAssignmentMode _mode;
    private string _authorizationCorrelationId = string.Empty;
    private int _expectedOwnedPropertyCount;
    private double _windowDurationGameMinutes;
    private double? _deadlineGameMinutes;

    public Release1KeepTheLightsOffAssignment(
        string MissionKey,
        int Attempt,
        Release1KeepTheLightsOffAssignmentMode Mode,
        string AuthorizationCorrelationId,
        int ExpectedOwnedPropertyCount,
        double WindowDurationGameMinutes,
        double? DeadlineGameMinutes)
    {
        this.MissionKey = MissionKey;
        this.Attempt = Attempt;
        this.Mode = Mode;
        this.AuthorizationCorrelationId = AuthorizationCorrelationId;
        this.ExpectedOwnedPropertyCount = ExpectedOwnedPropertyCount;
        this.WindowDurationGameMinutes = WindowDurationGameMinutes;
        this.DeadlineGameMinutes = DeadlineGameMinutes;
        Validate();
    }

    public string MissionKey
    {
        get => _missionKey;
        init
        {
            if (!string.Equals(value, Release1MissionCatalog.KeepTheLightsOff, StringComparison.Ordinal))
                throw new ArgumentException("Assignment mission must be Keep the Lights Off.", nameof(MissionKey));
            _missionKey = value;
        }
    }

    public int Attempt
    {
        get => _attempt;
        init
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
            _attempt = value;
            ValidateAuthorizationIfPresent();
        }
    }

    public Release1KeepTheLightsOffAssignmentMode Mode
    {
        get => _mode;
        init
        {
            if (!Enum.IsDefined(value)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
            _mode = value;
            ValidateAuthorizationIfPresent();
        }
    }

    public string AuthorizationCorrelationId
    {
        get => _authorizationCorrelationId;
        init
        {
            ValidateAuthorization(value, Attempt, Mode);
            _authorizationCorrelationId = value;
        }
    }

    public int ExpectedOwnedPropertyCount
    {
        get => _expectedOwnedPropertyCount;
        init
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(ExpectedOwnedPropertyCount), "At least one owned property is required.");
            _expectedOwnedPropertyCount = value;
        }
    }

    public double WindowDurationGameMinutes
    {
        get => _windowDurationGameMinutes;
        init
        {
            if (!double.IsFinite(value) || value != WindowGameMinutes)
                throw new ArgumentOutOfRangeException(nameof(WindowDurationGameMinutes), "The quiet window is exactly one in-game day.");
            _windowDurationGameMinutes = value;
        }
    }

    public double? DeadlineGameMinutes
    {
        get => _deadlineGameMinutes;
        init
        {
            ValidateDeadline(value, Mode);
            _deadlineGameMinutes = value;
        }
    }

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.KeepTheLightsOff, StringComparison.Ordinal))
            throw new ArgumentException("Assignment mission must be Keep the Lights Off.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
        ValidateAuthorization(AuthorizationCorrelationId, Attempt, Mode);
        if (ExpectedOwnedPropertyCount < 1) throw new ArgumentOutOfRangeException(nameof(ExpectedOwnedPropertyCount));
        if (!double.IsFinite(WindowDurationGameMinutes) || WindowDurationGameMinutes != WindowGameMinutes)
            throw new ArgumentOutOfRangeException(nameof(WindowDurationGameMinutes));
        ValidateDeadline(DeadlineGameMinutes, Mode);
    }

    private static void ValidateDeadline(double? value, Release1KeepTheLightsOffAssignmentMode mode)
    {
        if (mode == Release1KeepTheLightsOffAssignmentMode.Recovery)
        {
            if (value is not null) throw new ArgumentException("Recovery has no deadline.", nameof(DeadlineGameMinutes));
            return;
        }
        if (value is null || !double.IsFinite(value.Value) || value.Value != TimedStageGameMinutes)
            throw new ArgumentOutOfRangeException(nameof(DeadlineGameMinutes), "Timed Keep the Lights Off stages run for 4320 game minutes.");
    }

    private void ValidateAuthorizationIfPresent()
    {
        if (_authorizationCorrelationId.Length != 0)
            ValidateAuthorization(_authorizationCorrelationId, Attempt, Mode);
    }

    private static void ValidateAuthorization(string? value, int attempt, Release1KeepTheLightsOffAssignmentMode mode)
    {
        var expected = mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1KeepTheLightsOffAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
        };
        if (!Release1LogicalCorrelation.TryParse(value, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.KeepTheLightsOff ||
            correlation.Attempt != attempt || correlation.TransitionKind != expected)
            throw new ArgumentException("Assignment authorization correlation did not match mission, attempt, and mode.", nameof(AuthorizationCorrelationId));
    }
}

/// <summary>
/// The two Keep the Lights Off fields that change during play. Written in memory through the story
/// runtime and never persisted immediately, so they revert with the native save exactly as the world's
/// dead drop and HQ closet contents do. ClearConfirmedAtGameMinutes is monotonic within an attempt
/// (the story runtime's setter refuses to withdraw it); BreachSincePassGameMinutes toggles freely,
/// because the player legitimately clears a breach before the grace elapses.
/// </summary>
public sealed record Release1KeepTheLightsOffProgress(
    string MissionKey,
    int Attempt,
    double? ClearConfirmedAtGameMinutes,
    double? BreachSincePassGameMinutes)
{
    public static Release1KeepTheLightsOffProgress Fresh(int attempt) =>
        new(Release1MissionCatalog.KeepTheLightsOff, attempt, null, null);

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.KeepTheLightsOff, StringComparison.Ordinal))
            throw new ArgumentException("Progress mission must be Keep the Lights Off.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        ValidateGameMinutes(ClearConfirmedAtGameMinutes, nameof(ClearConfirmedAtGameMinutes));
        ValidateGameMinutes(BreachSincePassGameMinutes, nameof(BreachSincePassGameMinutes));
        if (BreachSincePassGameMinutes is not null && ClearConfirmedAtGameMinutes is null)
            throw new ArgumentException("A breach cannot be recorded before the clear was confirmed.", nameof(BreachSincePassGameMinutes));
    }

    private static void ValidateGameMinutes(double? value, string parameterName)
    {
        if (value is null) return;
        if (!double.IsFinite(value.Value) || value.Value < 0d)
            throw new ArgumentOutOfRangeException(parameterName, "Game minutes must be finite and non-negative.");
    }
}

public static class Release1KeepTheLightsOffAssignmentSelector
{
    /// <summary>
    /// Selects an assignment from a production read. <paramref name="requireAssignedEmployee"/>
    /// defaults to true for the offer path, decision 10's precondition that Nell only asks for a
    /// shutdown when there is something to shut down. Restoring an assignment for an attempt whose
    /// acceptance is already on record is not a new offer, so that precondition does not apply there:
    /// the caller passes false so a save with every employee unassigned still restores the attempt it
    /// already owns, rather than being unable to restore at all and riding the deadline to
    /// RequiredFailure with no card and no quest ever shown.
    /// </summary>
    public static bool TrySelect(
        Release1ProductionActivitySnapshot? activity,
        Release1KeepTheLightsOffAssignmentMode mode,
        int attempt,
        string? authorizationCorrelationId,
        out Release1KeepTheLightsOffAssignment? assignment,
        out Release1KeepTheLightsOffSelectionStatus status,
        bool requireAssignedEmployee = true)
    {
        assignment = null;
        status = Release1KeepTheLightsOffSelectionStatus.InvalidInput;
        if (activity is null) return false;
        try
        {
            activity.Validate();
            if (activity.Properties.Count < 1)
            {
                status = Release1KeepTheLightsOffSelectionStatus.NoOwnedProperty;
                return false;
            }
            if (requireAssignedEmployee &&
                !activity.Properties.Any(property => property.Employees.Any(employee => !employee.Fired)))
            {
                // Decision 10: Nell only asks for a shutdown when there is something to shut down.
                // Offer path only; see the method doc comment above.
                status = Release1KeepTheLightsOffSelectionStatus.NoAssignedEmployee;
                return false;
            }
            assignment = new Release1KeepTheLightsOffAssignment(
                Release1MissionCatalog.KeepTheLightsOff,
                attempt,
                mode,
                authorizationCorrelationId!,
                activity.Properties.Count,
                Release1KeepTheLightsOffAssignment.WindowGameMinutes,
                mode == Release1KeepTheLightsOffAssignmentMode.Recovery
                    ? null
                    : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);
            status = Release1KeepTheLightsOffSelectionStatus.Selected;
            return true;
        }
        catch (ArgumentException)
        {
            assignment = null;
            status = Release1KeepTheLightsOffSelectionStatus.InvalidInput;
            return false;
        }
    }
}
