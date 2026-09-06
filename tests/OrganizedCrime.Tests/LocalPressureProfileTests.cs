using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureProfileTests
{
    public static IEnumerable<object[]> BuiltInProfiles()
    {
        yield return new object[] { "Moderate", LocalPressureProfile.Moderate, new[] { 8, 12, 20, 12, 1, 5, 6 } };
        yield return new object[] { "Forgiving", LocalPressureProfile.Forgiving, new[] { 6, 8, 15, 8, 1, 3, 4 } };
        yield return new object[] { "Punishing", LocalPressureProfile.Punishing, new[] { 12, 16, 25, 18, 1, 8, 10 } };
    }

    [Theory]
    [MemberData(nameof(BuiltInProfiles))]
    public void Built_in_profiles_have_exactly_one_delta_for_each_reason_code(
        string profileName,
        LocalPressureProfile profile,
        int[] expectedDeltas)
    {
        var reasonCodes = Enum.GetValues<LocalPressureReasonCode>();

        Assert.Equal(reasonCodes.Length, profile.HeatDeltas.Count);
        Assert.All(reasonCodes, reasonCode => Assert.True(profile.HeatDeltas.ContainsKey(reasonCode), $"{profileName} is missing {reasonCode}."));
        Assert.Equal(expectedDeltas, reasonCodes.Select(profile.GetHeatDelta).ToArray());
    }

    [Fact]
    public void Missing_reason_code_configuration_is_rejected()
    {
        var entries = AllDeltas().Where(entry => entry.Key != LocalPressureReasonCode.Arrest);

        var exception = Assert.Throws<ArgumentException>(() => CreateProfile(entries));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(LocalPressureReasonCode.Arrest), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Negative_reason_delta_is_rejected()
    {
        var entries = AllDeltas().Select(entry => entry.Key == LocalPressureReasonCode.Arrest
            ? new KeyValuePair<LocalPressureReasonCode, int>(entry.Key, -1)
            : entry);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateProfile(entries));

        Assert.Contains("non-negative", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_reason_code_entries_are_rejected()
    {
        var entries = AllDeltas().Append(
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.Arrest, 99));

        var exception = Assert.Throws<ArgumentException>(() => CreateProfile(entries));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(LocalPressureReasonCode.Arrest), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_reason_code_entries_are_rejected()
    {
        var unknownReasonCode = (LocalPressureReasonCode)int.MaxValue;
        var entries = AllDeltas().Append(
            new KeyValuePair<LocalPressureReasonCode, int>(unknownReasonCode, 1));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateProfile(entries));

        Assert.Contains("Unknown Local Pressure reason code", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_reason_code_lookup_is_rejected_clearly()
    {
        var invalidReasonCode = (LocalPressureReasonCode)int.MaxValue;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalPressureProfile.Moderate.GetHeatDelta(invalidReasonCode));

        Assert.Contains("Unknown Local Pressure reason code", exception.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<KeyValuePair<LocalPressureReasonCode, int>> AllDeltas() =>
        new[]
        {
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.WitnessedCrime, 8),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.ContrabandDiscovered, 12),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.Arrest, 20),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.ResistanceOrViolence, 12),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.PursuitEscalation, 1),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.EvadedPursuit, 5),
            new KeyValuePair<LocalPressureReasonCode, int>(LocalPressureReasonCode.VerifiedCurfewOrExposure, 6)
        };

    private static LocalPressureProfile CreateProfile(
        IEnumerable<KeyValuePair<LocalPressureReasonCode, int>> heatDeltas) =>
        new(
            quietUpperBound: 24,
            noticedUpperBound: 49,
            watchedUpperBound: 74,
            knownOffenderFloor: 25,
            quietGraceHours: 2,
            heatDecayPerHour: 1,
            maximumDecayCatchUpHours: 24,
            pursuitContributionIntervalHours: 1,
            pursuitHeatCap: 6,
            heatDeltas: heatDeltas);
}
