namespace OrganizedCrime.Model;

public enum Release1TheEnvelopeAssignmentMode
{
    Primary,
    MakeGood,
    Recovery
}

public enum Release1TheEnvelopeSelectionStatus
{
    Selected,
    InvalidInput
}

/// <summary>
/// One immutable Envelope stage assignment, frozen in the same durable story revision as the
/// acceptance transition that authorizes it. There is no product, no packaging, and no drop: the
/// deposit is cash, left in the Syndicate HQ hold room's closets, identified only by the amount,
/// which is fixed by the stage rather than chosen by any candidate list. The hold room is never
/// selected: it is always the nine OC-48 HQ closets, exactly as Room With No Name and Keep the
/// Lights Off freeze it.
/// </summary>
public sealed record Release1TheEnvelopeAssignment
{
    public const double PrimaryAmount = 20000d;
    public const double MakeGoodAmount = 10000d;
    public const double RecoveryAmount = 5000d;
    public const double TimedStageGameMinutes = 1440d;

    private string _missionKey = string.Empty;
    private int _attempt;
    private Release1TheEnvelopeAssignmentMode _mode;
    private string _authorizationCorrelationId = string.Empty;
    private double _amountWholeDollars;
    private string _holdRoomKey = string.Empty;
    private int _expectedClosetCount;
    private double? _deadlineGameMinutes;

    public Release1TheEnvelopeAssignment(
        string MissionKey,
        int Attempt,
        Release1TheEnvelopeAssignmentMode Mode,
        string AuthorizationCorrelationId,
        double AmountWholeDollars,
        string HoldRoomKey,
        int ExpectedClosetCount,
        double? DeadlineGameMinutes)
    {
        this.MissionKey = MissionKey;
        this.Attempt = Attempt;
        this.Mode = Mode;
        this.AuthorizationCorrelationId = AuthorizationCorrelationId;
        this.AmountWholeDollars = AmountWholeDollars;
        this.HoldRoomKey = HoldRoomKey;
        this.ExpectedClosetCount = ExpectedClosetCount;
        this.DeadlineGameMinutes = DeadlineGameMinutes;
        Validate();
    }

    public string MissionKey
    {
        get => _missionKey;
        init
        {
            if (!string.Equals(value, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal))
                throw new ArgumentException("Assignment mission must be The Envelope.", nameof(MissionKey));
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

    public Release1TheEnvelopeAssignmentMode Mode
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

    public double AmountWholeDollars
    {
        get => _amountWholeDollars;
        init
        {
            if (value != AmountFor(Mode))
                throw new ArgumentOutOfRangeException(nameof(AmountWholeDollars), "The Envelope amount must match the stage.");
            _amountWholeDollars = value;
        }
    }

    public string HoldRoomKey
    {
        get => _holdRoomKey;
        init
        {
            if (!string.Equals(value, Release1RoomWithNoNameAssignment.SyndicateHqRoomKey, StringComparison.Ordinal))
                throw new ArgumentException("The hold room is always the Syndicate HQ storage room.", nameof(HoldRoomKey));
            _holdRoomKey = value;
        }
    }

    public int ExpectedClosetCount
    {
        get => _expectedClosetCount;
        init
        {
            if (value != Release1RoomWithNoNameAssignment.SyndicateHqClosetCount)
                throw new ArgumentOutOfRangeException(nameof(ExpectedClosetCount), "The hold room has exactly nine closets.");
            _expectedClosetCount = value;
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

    public static double AmountFor(Release1TheEnvelopeAssignmentMode mode) => mode switch
    {
        Release1TheEnvelopeAssignmentMode.Primary => PrimaryAmount,
        Release1TheEnvelopeAssignmentMode.MakeGood => MakeGoodAmount,
        Release1TheEnvelopeAssignmentMode.Recovery => RecoveryAmount,
        _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
    };

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal))
            throw new ArgumentException("Assignment mission must be The Envelope.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Assignment mode is not defined.", nameof(Mode));
        ValidateAuthorization(AuthorizationCorrelationId, Attempt, Mode);
        if (AmountWholeDollars != AmountFor(Mode)) throw new ArgumentOutOfRangeException(nameof(AmountWholeDollars));
        if (!string.Equals(HoldRoomKey, Release1RoomWithNoNameAssignment.SyndicateHqRoomKey, StringComparison.Ordinal))
            throw new ArgumentException("The hold room is always the Syndicate HQ storage room.", nameof(HoldRoomKey));
        if (ExpectedClosetCount != Release1RoomWithNoNameAssignment.SyndicateHqClosetCount)
            throw new ArgumentOutOfRangeException(nameof(ExpectedClosetCount));
        ValidateDeadline(DeadlineGameMinutes, Mode);
    }

    private static void ValidateDeadline(double? value, Release1TheEnvelopeAssignmentMode mode)
    {
        if (mode == Release1TheEnvelopeAssignmentMode.Recovery)
        {
            if (value is not null) throw new ArgumentException("Recovery has no deadline.", nameof(DeadlineGameMinutes));
            return;
        }
        if (value is null || !double.IsFinite(value.Value) || value.Value != TimedStageGameMinutes)
            throw new ArgumentOutOfRangeException(nameof(DeadlineGameMinutes), "Timed Envelope stages run for 1440 game minutes.");
    }

    private void ValidateAuthorizationIfPresent()
    {
        if (_authorizationCorrelationId.Length != 0)
            ValidateAuthorization(_authorizationCorrelationId, Attempt, Mode);
    }

    private static void ValidateAuthorization(string? value, int attempt, Release1TheEnvelopeAssignmentMode mode)
    {
        var expected = mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1TheEnvelopeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new ArgumentException("Assignment mode is not defined.", nameof(mode))
        };
        if (!Release1LogicalCorrelation.TryParse(value, out var correlation) ||
            correlation.MissionKey != Release1MissionCatalog.TheEnvelope ||
            correlation.Attempt != attempt || correlation.TransitionKind != expected)
            throw new ArgumentException("Assignment authorization correlation did not match mission, attempt, and mode.", nameof(AuthorizationCorrelationId));
    }
}

/// <summary>
/// The Envelope fields that change during play. Written in memory through the story runtime and
/// never persisted immediately, so they revert with the native save exactly as the drop's own cash
/// instance does. There is no staged flag and no custody flag: the player supplies and deposits the
/// cash directly, and OC never takes custody of it. ObservedBalance and LastShortfallNoticed are not
/// monotonic, because the player may withdraw cash before the deposit is complete; SpreadNoticed
/// cannot regress within an attempt.
/// </summary>
public sealed record Release1TheEnvelopeProgress(
    string MissionKey,
    int Attempt,
    double ObservedBalance,
    double? LastShortfallNoticed,
    bool SpreadNoticed)
{
    public static Release1TheEnvelopeProgress Fresh(int attempt) =>
        new(Release1MissionCatalog.TheEnvelope, attempt, 0d, null, false);

    public void Validate()
    {
        if (!string.Equals(MissionKey, Release1MissionCatalog.TheEnvelope, StringComparison.Ordinal))
            throw new ArgumentException("Progress mission must be The Envelope.", nameof(MissionKey));
        if (Attempt < 1) throw new ArgumentOutOfRangeException(nameof(Attempt));
        if (!double.IsFinite(ObservedBalance) || ObservedBalance < 0d) throw new ArgumentOutOfRangeException(nameof(ObservedBalance));
        if (LastShortfallNoticed is not null && (!double.IsFinite(LastShortfallNoticed.Value) || LastShortfallNoticed.Value <= 0d))
            throw new ArgumentOutOfRangeException(nameof(LastShortfallNoticed), "A noticed shortfall is a positive dollar amount.");
    }
}

/// <summary>
/// The room is the destination, so there is nothing to select from the world: the assignment is
/// fully determined by the stage, the attempt, and the acceptance correlation. The shipped drop
/// selection and its NoEmptyDeadDrop refusal are deleted with the dead drop destination.
/// </summary>
public static class Release1TheEnvelopeAssignmentSelector
{
    public static bool TrySelect(
        Release1TheEnvelopeAssignmentMode mode,
        int attempt,
        string? authorizationCorrelationId,
        out Release1TheEnvelopeAssignment? assignment,
        out Release1TheEnvelopeSelectionStatus status)
    {
        assignment = null;
        status = Release1TheEnvelopeSelectionStatus.InvalidInput;
        try
        {
            assignment = new Release1TheEnvelopeAssignment(
                Release1MissionCatalog.TheEnvelope,
                attempt,
                mode,
                authorizationCorrelationId!,
                Release1TheEnvelopeAssignment.AmountFor(mode),
                Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
                Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
                mode == Release1TheEnvelopeAssignmentMode.Recovery
                    ? null
                    : Release1TheEnvelopeAssignment.TimedStageGameMinutes);
            status = Release1TheEnvelopeSelectionStatus.Selected;
            return true;
        }
        catch (ArgumentException)
        {
            assignment = null;
            status = Release1TheEnvelopeSelectionStatus.InvalidInput;
            return false;
        }
    }
}
