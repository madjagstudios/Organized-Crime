using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ProductionActivityContractTests
{
    private static Release1ProductionEmployeeSnapshot Employee(
        string id = "npc-1", bool working = false, string? behaviourType = null, bool behaviourActive = true) =>
        new(id, "Alice Chen", "Chemist", false, true, false, 3, working,
            behaviourType is null ? null : "Cook", behaviourType, behaviourActive);

    private static Release1ProductionStationSnapshot Station(
        string guid = "station-1", bool running = false, string? identity = null, int progress = 0) =>
        new(guid, Release1ProductionStationKind.ChemistryStation, running, identity, progress);

    [Fact]
    public void An_employee_snapshot_refuses_a_blank_identity_a_blank_name_and_a_negative_tick_count()
    {
        Assert.Throws<ArgumentException>(() => Employee(id: " ").Validate());
        Assert.Throws<ArgumentException>(() => (Employee() with { DisplayName = "  " }).Validate());
        Assert.Throws<ArgumentException>(() => (Employee() with { Role = "" }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Employee() with { TicksSinceLastWork = -1 }).Validate());
        Employee().Validate();
    }

    [Fact]
    public void A_station_snapshot_refuses_a_blank_guid_an_undefined_kind_a_negative_progress_and_a_running_station_with_no_identity()
    {
        Assert.Throws<ArgumentException>(() => Station(guid: " ").Validate());
        Assert.Throws<ArgumentException>(() => (Station() with { Kind = (Release1ProductionStationKind)99 }).Validate());
        Assert.Throws<ArgumentException>(() => Station(running: true, identity: null).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Station() with { Progress = -1 }).Validate());

        Station(running: true, identity: "og-kush").Validate();
        Station().Validate();
    }

    [Fact]
    public void A_property_snapshot_refuses_duplicate_employee_ids_and_duplicate_station_guids()
    {
        Assert.Throws<ArgumentException>(() => new Release1ProductionPropertySnapshot(
            "barn", "Barn",
            new[] { Employee("npc-1"), Employee("npc-1") },
            Array.Empty<Release1ProductionStationSnapshot>()).Validate());

        Assert.Throws<ArgumentException>(() => new Release1ProductionPropertySnapshot(
            "barn", "Barn",
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station("s"), Station("s") }).Validate());

        new Release1ProductionPropertySnapshot(
            "barn", "Barn",
            new[] { Employee("npc-1") },
            new[] { Station("s") }).Validate();
    }

    [Fact]
    public void An_activity_snapshot_refuses_duplicate_property_codes_and_a_station_guid_reused_across_properties()
    {
        var barnProperty = new Release1ProductionPropertySnapshot(
            "barn", "Barn",
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            Array.Empty<Release1ProductionStationSnapshot>());
        Assert.Throws<ArgumentException>(() => new Release1ProductionActivitySnapshot(
            new[] { barnProperty, barnProperty }).Validate());

        var barnWithShared = new Release1ProductionPropertySnapshot(
            "barn", "Barn",
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station("shared") });
        var motelWithShared = new Release1ProductionPropertySnapshot(
            "motel", "Motel",
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station("shared") });
        Assert.Throws<ArgumentException>(() => new Release1ProductionActivitySnapshot(
            new[] { barnWithShared, motelWithShared }).Validate());

        Release1ProductionActivitySnapshot.Empty.Validate();
    }

    [Fact]
    public void The_work_behaviour_name_list_is_a_home_diagnostic_and_never_the_decisive_read()
    {
        var names = Release1ProductionWorkBehaviours.WorkBehaviourTypeNames;
        Assert.Contains("StartChemistryStationBehaviour", names);
        Assert.Contains("HarvestMushroomBedBehaviour", names);
        Assert.Equal(22, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());

        Assert.True(Release1ProductionWorkBehaviours.ReadsAsWorking(
            Employee(behaviourType: "StartChemistryStationBehaviour", behaviourActive: true)));
        Assert.False(Release1ProductionWorkBehaviours.ReadsAsWorking(
            Employee(behaviourType: "StartChemistryStationBehaviour", behaviourActive: false)));
        Assert.False(Release1ProductionWorkBehaviours.ReadsAsWorking(
            Employee(behaviourType: "IdleBehaviour", behaviourActive: true)));
        Assert.False(Release1ProductionWorkBehaviours.ReadsAsWorking(
            Employee(behaviourType: null, behaviourActive: true)));
        Assert.False(Release1ProductionWorkBehaviours.ReadsAsWorking(null));
    }

    [Fact]
    public void The_cauldron_progress_helper_reads_both_counter_directions_as_an_increasing_quantity()
    {
        Assert.Equal(40, Release1ProductionStationProgress.CauldronProgress(100, 60, remainingCountsDown: true));
        Assert.Equal(60, Release1ProductionStationProgress.CauldronProgress(100, 60, remainingCountsDown: false));
        Assert.Equal(0, Release1ProductionStationProgress.CauldronProgress(100, 140, remainingCountsDown: true));
        Assert.True(Release1ProductionStationProgress.CauldronRemainingCountsDown);
    }
}
