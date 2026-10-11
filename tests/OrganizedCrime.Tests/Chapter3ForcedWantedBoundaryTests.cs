using OrganizedCrime.Runtime;
using S1API.Entities;
using S1API.Law;
using Xunit;
using NativePlayer = Il2CppScheduleOne.PlayerScripts.Player;
using NativePlayerCrimeData = Il2CppScheduleOne.PlayerScripts.PlayerCrimeData;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-74 structural pins: every S1API member the forced-Wanted path calls, resolved by
/// reflection against the installed game assemblies the test host loads (S1API 3.2.1, f9
/// interop), plus the Arresting-only, dispatch-reuse, and production-wiring pins.
/// </summary>
[Collection("OC-151 composition scope")]
public sealed class Chapter3ForcedWantedBoundaryTests
{
    [Fact]
    public void SetWantedLevel_has_the_proven_static_signature()
    {
        var method = typeof(LawManager).GetMethod(
            nameof(LawManager.SetWantedLevel),
            new[] { typeof(Player), typeof(PursuitLevel) });

        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void PursuitLevel_values_match_the_trigger_constants_and_qa_names()
    {
        Assert.Equal(0, (int)PursuitLevel.None);
        Assert.Equal(1, (int)PursuitLevel.Investigating);
        Assert.Equal(2, (int)PursuitLevel.Arresting);
        Assert.Equal(3, (int)PursuitLevel.NonLethal);
        Assert.Equal(4, (int)PursuitLevel.Lethal);
        Assert.Equal(Chapter3PursuitLevels.None, (int)PursuitLevel.None);
        Assert.Equal(Chapter3PursuitLevels.Arresting, (int)PursuitLevel.Arresting);
        Assert.Equal("None", Enum.GetName(typeof(PursuitLevel), 0));
        Assert.Equal("Investigating", Enum.GetName(typeof(PursuitLevel), 1));
        Assert.Equal("Arresting", Enum.GetName(typeof(PursuitLevel), 2));
        Assert.Equal("NonLethal", Enum.GetName(typeof(PursuitLevel), 3));
        Assert.Equal("Lethal", Enum.GetName(typeof(PursuitLevel), 4));
    }

    [Fact]
    public void CurfewManager_exposes_the_active_read_and_the_2100_anchor()
    {
        Assert.True(typeof(CurfewManager).IsAbstract && typeof(CurfewManager).IsSealed);
        var active = typeof(CurfewManager).GetProperty(nameof(CurfewManager.IsCurrentlyActive));
        Assert.NotNull(active);
        Assert.True(active!.GetMethod!.IsStatic);
        Assert.Equal(typeof(bool), active.PropertyType);
        var start = typeof(CurfewManager).GetField(nameof(CurfewManager.CurfewStartTime));
        Assert.NotNull(start);
        Assert.True(start!.IsLiteral);
        Assert.Equal(2100, start.GetRawConstantValue());
        Assert.Equal(1260, Release1ChiefLedger.ForcedWantedCurfewStartMinutes);
    }

    [Fact]
    public void Player_exposes_the_exposure_and_wanted_reads()
    {
        var local = typeof(Player).GetProperty(nameof(Player.Local));
        Assert.NotNull(local);
        Assert.True(local!.GetMethod!.IsStatic);
        Assert.Equal(typeof(Player), local.PropertyType);
        Assert.Equal(typeof(bool), typeof(Player).GetProperty(nameof(Player.IsInVehicle))!.PropertyType);
        var property = typeof(Player).GetProperty(nameof(Player.CurrentProperty));
        Assert.NotNull(property);
        Assert.Equal("S1API.Property.PropertyWrapper", property!.PropertyType.FullName);
        Assert.Equal(typeof(PlayerCrimeData), typeof(Player).GetProperty(nameof(Player.CrimeData))!.PropertyType);
    }

    [Fact]
    public void PlayerCrimeData_exposes_the_pursuit_read_and_write()
    {
        var level = typeof(PlayerCrimeData).GetProperty(nameof(PlayerCrimeData.CurrentPursuitLevel));
        Assert.NotNull(level);
        Assert.False(level!.GetMethod!.IsStatic);
        Assert.Equal(typeof(PursuitLevel), level.PropertyType);
        var set = typeof(PlayerCrimeData).GetMethod(
            nameof(PlayerCrimeData.SetPursuitLevel),
            new[] { typeof(PursuitLevel) });
        Assert.NotNull(set);
        Assert.False(set!.IsStatic);
        Assert.Equal(typeof(void), set.ReturnType);
    }

    [Fact]
    public void Production_wanted_writer_sets_nothing_but_arresting()
    {
        var source = ReadProductionSource("S1ApiChapter3WantedWriter.cs");

        Assert.Contains("PursuitLevel.Arresting", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "SetWantedLevel("));
        Assert.DoesNotContain("NonLethal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Lethal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Investigating", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Forced_wanted_dispatch_reuses_the_vehicle_two_officer_profile_with_no_native_call()
    {
        var source = ReadProductionSource("Chapter3ForcedWantedDispatch.cs");

        Assert.Contains("NativeLawResponseProfile.VehicleTwoOfficerV1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("station.Dispatch(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PoliceStation.Dispatch", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_wires_every_forced_wanted_seam_at_the_chief_composition()
    {
        var release1 = ReadCompositionSource("Release1Module.cs");
        var chapter3 = ReadCompositionSource("Chapter3Module.cs");
        var melon = ReadCompositionSource("MelonEngineAdapters.cs");

        Assert.Contains("isAuthoritativeHost: engine.IsHost", release1, StringComparison.Ordinal);
        Assert.Contains("exposureReader: engine.CreateExposureReader(serviceWarn)", release1, StringComparison.Ordinal);
        Assert.Contains("curfewReader: engine.CreateCurfewReader(serviceWarn)", release1, StringComparison.Ordinal);
        Assert.Contains("wantedWriter: engine.CreateWantedWriter(serviceWarn)", release1, StringComparison.Ordinal);
        Assert.Contains("forcedWantedDispatch: new Chapter3ForcedWantedDispatch(", release1, StringComparison.Ordinal);
        Assert.Contains("pursuitHold: engine.CreatePursuitHold(serviceWarn)", release1, StringComparison.Ordinal);
        Assert.Contains("ChiefForcedWantedQa = chiefService", chapter3, StringComparison.Ordinal);
        Assert.Contains("new S1ApiChapter3PlayerExposureReader(warn)", melon, StringComparison.Ordinal);
        Assert.Contains("new S1ApiChapter3CurfewReader(warn)", melon, StringComparison.Ordinal);
        Assert.Contains("new S1ApiChapter3WantedWriter(warn)", melon, StringComparison.Ordinal);
        Assert.Contains("new S1ApiChapter3PursuitHold(warn)", melon, StringComparison.Ordinal);

        using var scope = new CompositionScope();

        Assert.Same(scope.Root.Chief!.Service, scope.Root.Chapter3!.ChiefForcedWantedQa);
    }

    [Fact]
    public void Player_exposes_the_arrest_read()
    {
        var arrested = typeof(Player).GetProperty(nameof(Player.IsArrested));
        Assert.NotNull(arrested);
        Assert.False(arrested!.GetMethod!.IsStatic);
        Assert.Equal(typeof(bool), arrested.PropertyType);
    }

    [Fact]
    public void PlayerCrimeData_exposes_the_hold_reads_with_a_get_only_timer()
    {
        var timer = typeof(PlayerCrimeData).GetProperty(nameof(PlayerCrimeData.TimeSinceSighted));
        Assert.NotNull(timer);
        Assert.False(timer!.GetMethod!.IsStatic);
        Assert.Equal(typeof(float), timer.PropertyType);
        // S1API 3.2.1 reads the timer but never writes it: the hold's write drops to the
        // native interop below, and this pin fails loudly if a later S1API adds a setter.
        Assert.False(timer.CanWrite);
        var search = typeof(PlayerCrimeData).GetMethod(nameof(PlayerCrimeData.GetSearchTime), Type.EmptyTypes);
        Assert.NotNull(search);
        Assert.False(search!.IsStatic);
        Assert.Equal(typeof(float), search.ReturnType);
    }

    [Fact]
    public void Native_crime_data_exposes_the_hold_write_path()
    {
        var local = typeof(NativePlayer).GetProperty("Local");
        Assert.NotNull(local);
        Assert.True(local!.GetMethod!.IsStatic);
        Assert.Equal(typeof(NativePlayer), local.PropertyType);
        var crime = typeof(NativePlayer).GetProperty("CrimeData");
        Assert.NotNull(crime);
        Assert.False(crime!.GetMethod!.IsStatic);
        Assert.Equal(typeof(NativePlayerCrimeData), crime.PropertyType);
        var timer = typeof(NativePlayerCrimeData).GetProperty("TimeSinceSighted");
        Assert.NotNull(timer);
        Assert.True(timer!.CanWrite);
        Assert.Equal(typeof(float), timer.PropertyType);
    }

    [Fact]
    public void Production_pursuit_hold_clamps_the_timer_and_never_touches_the_level()
    {
        var source = ReadProductionSource("S1ApiChapter3PursuitHold.cs");

        Assert.Contains("TimeSinceSighted", source, StringComparison.Ordinal);
        Assert.Contains("GetSearchTime()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPursuitLevel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SEARCH_TIME", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_exposure_reader_reports_the_arrest_read()
    {
        var source = ReadProductionSource("S1ApiChapter3PlayerExposureReader.cs");

        Assert.Contains("IsArrested", source, StringComparison.Ordinal);
    }

    private static string ReadProductionSource(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", fileName));

    private static string ReadCompositionSource(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", "Composition", fileName));

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
