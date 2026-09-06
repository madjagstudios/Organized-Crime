namespace OrganizedCrime.Model;

public sealed record LocalPressureState
{
    public LocalPressureState(
        int LocalHeat,
        bool KnownOffender,
        double? LastEvidenceGameTime,
        double? QuietGraceUntil,
        double? LastDecayEvaluation,
        string? PlayerId,
        string? Region,
        string? PropertyCode,
        long Revision)
    {
        if (LocalHeat < LocalPressureProfile.MinimumHeat || LocalHeat > LocalPressureProfile.MaximumHeat)
            throw new ArgumentOutOfRangeException(nameof(LocalHeat), "Local Heat must be between 0 and 100.");
        if (Revision < 0)
            throw new ArgumentOutOfRangeException(nameof(Revision), "State revision cannot be negative.");

        ValidateGameTime(LastEvidenceGameTime, nameof(LastEvidenceGameTime));
        ValidateGameTime(QuietGraceUntil, nameof(QuietGraceUntil));
        ValidateGameTime(LastDecayEvaluation, nameof(LastDecayEvaluation));

        this.LocalHeat = LocalHeat;
        this.KnownOffender = KnownOffender;
        this.LastEvidenceGameTime = LastEvidenceGameTime;
        this.QuietGraceUntil = QuietGraceUntil;
        this.LastDecayEvaluation = LastDecayEvaluation;
        this.PlayerId = PlayerId;
        this.Region = Region;
        this.PropertyCode = PropertyCode;
        this.Revision = Revision;
    }

    public int LocalHeat { get; }
    public bool KnownOffender { get; }
    public double? LastEvidenceGameTime { get; }
    public double? QuietGraceUntil { get; }
    public double? LastDecayEvaluation { get; }
    public string? PlayerId { get; }
    public string? Region { get; }
    public string? PropertyCode { get; }
    public long Revision { get; }

    public static LocalPressureState Quiet(string? playerId = null) => new(
        LocalHeat: 0,
        KnownOffender: false,
        LastEvidenceGameTime: null,
        QuietGraceUntil: null,
        LastDecayEvaluation: null,
        PlayerId: playerId,
        Region: null,
        PropertyCode: null,
        Revision: 0);

    private static void ValidateGameTime(double? gameTimeHours, string parameterName)
    {
        if (gameTimeHours is not null &&
            (double.IsNaN(gameTimeHours.Value) || double.IsInfinity(gameTimeHours.Value) || gameTimeHours.Value < 0))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Game time must be finite and non-negative.");
        }
    }
}
