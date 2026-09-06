using System.Collections.ObjectModel;

namespace OrganizedCrime.Model;

public sealed record LocalPressureProfile
{
    public const int MinimumHeat = 0;
    public const int MaximumHeat = 100;

    public LocalPressureProfile(
        int quietUpperBound,
        int noticedUpperBound,
        int watchedUpperBound,
        int knownOffenderFloor,
        double quietGraceHours,
        double heatDecayPerHour,
        double maximumDecayCatchUpHours,
        double pursuitContributionIntervalHours,
        int pursuitHeatCap,
        IEnumerable<KeyValuePair<LocalPressureReasonCode, int>> heatDeltas)
    {
        QuietUpperBound = quietUpperBound;
        NoticedUpperBound = noticedUpperBound;
        WatchedUpperBound = watchedUpperBound;
        KnownOffenderFloor = knownOffenderFloor;
        QuietGraceHours = quietGraceHours;
        HeatDecayPerHour = heatDecayPerHour;
        MaximumDecayCatchUpHours = maximumDecayCatchUpHours;
        PursuitContributionIntervalHours = pursuitContributionIntervalHours;
        PursuitHeatCap = pursuitHeatCap;
        HeatDeltas = CreateHeatDeltas(heatDeltas);
        Validate();
    }

    public static LocalPressureProfile Moderate { get; } = new(
        quietUpperBound: 24,
        noticedUpperBound: 49,
        watchedUpperBound: 74,
        knownOffenderFloor: 25,
        quietGraceHours: 2,
        heatDecayPerHour: 1,
        maximumDecayCatchUpHours: 24,
        pursuitContributionIntervalHours: 1,
        pursuitHeatCap: 6,
        heatDeltas: new[]
        {
            Delta(LocalPressureReasonCode.WitnessedCrime, 8),
            Delta(LocalPressureReasonCode.ContrabandDiscovered, 12),
            Delta(LocalPressureReasonCode.Arrest, 20),
            Delta(LocalPressureReasonCode.ResistanceOrViolence, 12),
            Delta(LocalPressureReasonCode.PursuitEscalation, 1),
            Delta(LocalPressureReasonCode.EvadedPursuit, 5),
            Delta(LocalPressureReasonCode.VerifiedCurfewOrExposure, 6)
        });

    public static LocalPressureProfile Forgiving { get; } = new(
        quietUpperBound: 14,
        noticedUpperBound: 49,
        watchedUpperBound: 79,
        knownOffenderFloor: 15,
        quietGraceHours: 1,
        heatDecayPerHour: 2,
        maximumDecayCatchUpHours: 24,
        pursuitContributionIntervalHours: 1.5,
        pursuitHeatCap: 4,
        heatDeltas: new[]
        {
            Delta(LocalPressureReasonCode.WitnessedCrime, 6),
            Delta(LocalPressureReasonCode.ContrabandDiscovered, 8),
            Delta(LocalPressureReasonCode.Arrest, 15),
            Delta(LocalPressureReasonCode.ResistanceOrViolence, 8),
            Delta(LocalPressureReasonCode.PursuitEscalation, 1),
            Delta(LocalPressureReasonCode.EvadedPursuit, 3),
            Delta(LocalPressureReasonCode.VerifiedCurfewOrExposure, 4)
        });

    public static LocalPressureProfile Punishing { get; } = new(
        quietUpperBound: 34,
        noticedUpperBound: 59,
        watchedUpperBound: 79,
        knownOffenderFloor: 35,
        quietGraceHours: 3,
        heatDecayPerHour: 0.5,
        maximumDecayCatchUpHours: 24,
        pursuitContributionIntervalHours: 0.75,
        pursuitHeatCap: 8,
        heatDeltas: new[]
        {
            Delta(LocalPressureReasonCode.WitnessedCrime, 12),
            Delta(LocalPressureReasonCode.ContrabandDiscovered, 16),
            Delta(LocalPressureReasonCode.Arrest, 25),
            Delta(LocalPressureReasonCode.ResistanceOrViolence, 18),
            Delta(LocalPressureReasonCode.PursuitEscalation, 1),
            Delta(LocalPressureReasonCode.EvadedPursuit, 8),
            Delta(LocalPressureReasonCode.VerifiedCurfewOrExposure, 10)
        });

    public int QuietUpperBound { get; }
    public int NoticedLowerBound => QuietUpperBound + 1;
    public int NoticedUpperBound { get; }
    public int WatchedLowerBound => NoticedUpperBound + 1;
    public int WatchedUpperBound { get; }
    public int CriticalLowerBound => WatchedUpperBound + 1;
    public int KnownOffenderFloor { get; }
    public double QuietGraceHours { get; }
    public double HeatDecayPerHour { get; }
    public double MaximumDecayCatchUpHours { get; }
    public double PursuitContributionIntervalHours { get; }
    public int PursuitHeatCap { get; }
    public IReadOnlyDictionary<LocalPressureReasonCode, int> HeatDeltas { get; }

    public int GetHeatFloor(bool knownOffender) =>
        knownOffender ? KnownOffenderFloor : MinimumHeat;

    public int GetHeatDelta(LocalPressureReasonCode reasonCode)
    {
        if (!Enum.IsDefined(typeof(LocalPressureReasonCode), reasonCode))
            throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode, "Unknown Local Pressure reason code.");

        if (!HeatDeltas.TryGetValue(reasonCode, out var heatDelta))
            throw new InvalidOperationException($"No heat delta is configured for Local Pressure reason code {reasonCode}.");

        return heatDelta;
    }

    private static KeyValuePair<LocalPressureReasonCode, int> Delta(
        LocalPressureReasonCode reasonCode,
        int heatDelta) => new(reasonCode, heatDelta);

    private static IReadOnlyDictionary<LocalPressureReasonCode, int> CreateHeatDeltas(
        IEnumerable<KeyValuePair<LocalPressureReasonCode, int>> heatDeltas)
    {
        ArgumentNullException.ThrowIfNull(heatDeltas);

        var configuredDeltas = new Dictionary<LocalPressureReasonCode, int>();
        foreach (var entry in heatDeltas)
        {
            if (!Enum.IsDefined(typeof(LocalPressureReasonCode), entry.Key))
                throw new ArgumentOutOfRangeException(nameof(heatDeltas), entry.Key, "Unknown Local Pressure reason code.");

            if (!configuredDeltas.TryAdd(entry.Key, entry.Value))
                throw new ArgumentException($"Duplicate heat delta configured for Local Pressure reason code {entry.Key}.", nameof(heatDeltas));

            if (entry.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(heatDeltas), entry.Value, $"Heat delta for Local Pressure reason code {entry.Key} must be non-negative.");
        }

        var missingReasonCodes = Enum.GetValues<LocalPressureReasonCode>()
            .Where(reasonCode => !configuredDeltas.ContainsKey(reasonCode))
            .ToArray();
        if (missingReasonCodes.Length > 0)
        {
            throw new ArgumentException(
                $"Missing heat delta configuration for Local Pressure reason code(s): {string.Join(", ", missingReasonCodes)}.",
                nameof(heatDeltas));
        }

        return new ReadOnlyDictionary<LocalPressureReasonCode, int>(configuredDeltas);
    }

    private void Validate()
    {
        if (QuietUpperBound < MinimumHeat || QuietUpperBound >= NoticedUpperBound ||
            NoticedUpperBound >= WatchedUpperBound || WatchedUpperBound >= MaximumHeat)
        {
            throw new ArgumentOutOfRangeException(nameof(QuietUpperBound), "Tier thresholds must cover 0 through 100 in ascending order.");
        }

        if (KnownOffenderFloor < NoticedLowerBound || KnownOffenderFloor > MaximumHeat)
            throw new ArgumentOutOfRangeException(nameof(KnownOffenderFloor), "Known Offender floor must be within the Noticed-or-higher range.");

        if (QuietGraceHours < 0 || double.IsNaN(QuietGraceHours) || double.IsInfinity(QuietGraceHours))
            throw new ArgumentOutOfRangeException(nameof(QuietGraceHours));
        if (HeatDecayPerHour < 0 || double.IsNaN(HeatDecayPerHour) || double.IsInfinity(HeatDecayPerHour))
            throw new ArgumentOutOfRangeException(nameof(HeatDecayPerHour));
        if (MaximumDecayCatchUpHours < 0 || double.IsNaN(MaximumDecayCatchUpHours) || double.IsInfinity(MaximumDecayCatchUpHours))
            throw new ArgumentOutOfRangeException(nameof(MaximumDecayCatchUpHours));
        if (PursuitContributionIntervalHours <= 0 || double.IsNaN(PursuitContributionIntervalHours) || double.IsInfinity(PursuitContributionIntervalHours))
            throw new ArgumentOutOfRangeException(nameof(PursuitContributionIntervalHours));
        if (PursuitHeatCap < 0)
            throw new ArgumentOutOfRangeException(nameof(PursuitHeatCap));
    }
}
