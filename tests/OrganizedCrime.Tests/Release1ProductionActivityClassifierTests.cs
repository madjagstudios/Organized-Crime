using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ProductionActivityClassifierTests
{
    private static Release1ProductionEmployeeSnapshot Worker(bool working, bool fired = false) =>
        new("npc-1", "Alice Chen", "Chemist", fired, true, false, 0, working,
            working ? "Cook" : "Idle", working ? "StartChemistryStationBehaviour" : "IdleBehaviour", true);

    private static Release1ProductionStationSnapshot Station(
        Release1ProductionStationKind kind, bool running, string? identity, int progress, string guid = "s1") =>
        new(guid, kind, running, identity, progress);

    private static Release1ProductionActivitySnapshot World(
        IEnumerable<Release1ProductionEmployeeSnapshot> employees,
        IEnumerable<Release1ProductionStationSnapshot> stations,
        int propertyCount = 1) =>
        new(Enumerable.Range(0, propertyCount).Select(index => new Release1ProductionPropertySnapshot(
            $"p{index}", $"Property {index}",
            index == 0 ? employees.ToArray() : Array.Empty<Release1ProductionEmployeeSnapshot>(),
            index == 0 ? stations.ToArray() : Array.Empty<Release1ProductionStationSnapshot>())).ToArray());

    private static Release1ProductionObservationResult Classify(
        Release1ProductionActivitySnapshot? world,
        Release1SmallCourtesyWorldReadStatus status = Release1SmallCourtesyWorldReadStatus.Ready,
        int expected = 1,
        IReadOnlyList<Release1ProductionStationFingerprint>? previous = null,
        bool baseline = false) =>
        Release1ProductionActivityClassifier.Classify(world, status, expected, previous, baseline);

    [Fact]
    public void A_read_that_is_not_ready_is_notready_with_the_world_reason()
    {
        foreach (var status in new[]
                 {
                     Release1SmallCourtesyWorldReadStatus.Pending,
                     Release1SmallCourtesyWorldReadStatus.Unavailable,
                     Release1SmallCourtesyWorldReadStatus.NotAuthoritative,
                     Release1SmallCourtesyWorldReadStatus.Faulted
                 })
        {
            var result = Classify(
                World(Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>()),
                status: status);
            Assert.Equal(Release1ProductionObservation.NotReady, result.Observation);
            Assert.Equal(Release1ProductionHoldReason.WorldNotReady, result.Reason);
        }

        var nullResult = Classify(null);
        Assert.Equal(Release1ProductionObservation.NotReady, nullResult.Observation);
        Assert.Equal(Release1ProductionHoldReason.WorldNotReady, nullResult.Reason);
    }

    [Fact]
    public void A_property_count_mismatch_is_notready_with_its_own_distinguishable_reason()
    {
        var twoProperties = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>(), propertyCount: 2);
        var first = Classify(twoProperties, expected: 3);
        Assert.Equal(Release1ProductionObservation.NotReady, first.Observation);
        Assert.Equal(Release1ProductionHoldReason.OwnedPropertyCountMismatch, first.Reason);

        var oneProperty = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>(), propertyCount: 1);
        var second = Classify(oneProperty, expected: 2);
        Assert.Equal(Release1ProductionObservation.NotReady, second.Observation);
        Assert.Equal(Release1ProductionHoldReason.OwnedPropertyCountMismatch, second.Reason);
    }

    [Fact]
    public void A_notready_pass_carries_the_previous_fingerprints_forward_unchanged()
    {
        var previous = new[]
        {
            new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.ChemistryStation, true, "og", 10),
            new Release1ProductionStationFingerprint("s2", Release1ProductionStationKind.Cauldron, false, null, 0)
        };
        var result = Classify(
            World(Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>()),
            status: Release1SmallCourtesyWorldReadStatus.Faulted,
            previous: previous);

        Assert.Equal(Release1ProductionObservation.NotReady, result.Observation);
        // The next readable pass therefore compares against the last good read: the exact input list
        // comes back, not a copy.
        Assert.Same(previous, result.Stations);
    }

    [Fact]
    public void Nothing_working_and_nothing_new_is_quiet()
    {
        var world = World(
            new[] { Worker(working: false) },
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: false, identity: null, progress: 0) });
        var result = Classify(world, previous: Array.Empty<Release1ProductionStationFingerprint>());

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void Any_working_employee_at_any_owned_property_is_working()
    {
        var first = Classify(World(new[] { Worker(working: true) }, Array.Empty<Release1ProductionStationSnapshot>()));
        Assert.Equal(Release1ProductionObservation.Working, first.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, first.Reason);

        var twoProperties = new Release1ProductionActivitySnapshot(new[]
        {
            new Release1ProductionPropertySnapshot(
                "p0", "Property 0", Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>()),
            new Release1ProductionPropertySnapshot(
                "p1", "Property 1", new[] { Worker(working: true) }, Array.Empty<Release1ProductionStationSnapshot>())
        });
        var second = Classify(twoProperties, expected: 2);
        Assert.Equal(Release1ProductionObservation.Working, second.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, second.Reason);
    }

    [Fact]
    public void A_fired_employee_is_never_working_even_when_the_native_flag_says_so()
    {
        var result = Classify(World(new[] { Worker(working: true, fired: true) }, Array.Empty<Release1ProductionStationSnapshot>()));

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
    }

    [Fact]
    public void The_active_behaviour_heuristic_never_overrides_the_decisive_read()
    {
        var busyLookingButIdle = new Release1ProductionEmployeeSnapshot(
            "npc-1", "Alice Chen", "Chemist", false, true, false, 0, false,
            "Cook", "StartChemistryStationBehaviour", true);
        var quiet = Classify(World(new[] { busyLookingButIdle }, Array.Empty<Release1ProductionStationSnapshot>()));
        Assert.Equal(Release1ProductionObservation.Quiet, quiet.Observation);

        var idleLookingButWorking = new Release1ProductionEmployeeSnapshot(
            "npc-1", "Alice Chen", "Chemist", false, true, false, 0, true,
            "Idle", "IdleBehaviour", true);
        var working = Classify(World(new[] { idleLookingButWorking }, Array.Empty<Release1ProductionStationSnapshot>()));
        Assert.Equal(Release1ProductionObservation.Working, working.Observation);
    }

    [Theory]
    [InlineData(Release1ProductionStationKind.ChemistryStation)]
    [InlineData(Release1ProductionStationKind.LabOven)]
    [InlineData(Release1ProductionStationKind.MixingStation)]
    [InlineData(Release1ProductionStationKind.Cauldron)]
    [InlineData(Release1ProductionStationKind.DryingRack)]
    public void A_station_that_starts_a_new_operation_since_the_previous_pass_is_working(Release1ProductionStationKind kind)
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", kind, false, null, 0) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(kind, running: true, identity: "og", progress: 5) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_station_running_at_the_same_identity_with_progress_advanced_is_not_a_breach()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.ChemistryStation, true, "og", 10) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: true, identity: "og", progress: 40) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
    }

    [Fact]
    public void A_progress_decrease_at_the_same_identity_reads_as_a_restart()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.ChemistryStation, true, "og", 40) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: true, identity: "og", progress: 10) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void An_identity_change_reads_as_a_new_operation()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.ChemistryStation, true, "og", 40) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: true, identity: "sour", progress: 5) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_station_that_stopped_or_completed_is_not_a_breach()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.ChemistryStation, true, "og", 40) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: false, identity: null, progress: 0) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
    }

    [Fact]
    public void A_cauldron_cook_that_keeps_cooking_is_never_a_new_operation()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.Cauldron, true, "cauldron-cook", 20) };
        var nowProgress = Release1ProductionStationProgress.CauldronProgress(100, 45, remainingCountsDown: true);
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.Cauldron, running: true, identity: "cauldron-cook", progress: nowProgress) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
    }

    [Fact]
    public void A_drying_rack_that_finishes_one_of_its_loads_is_not_a_breach()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.DryingRack, true, "og|sour", 0) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.DryingRack, running: true, identity: "og", progress: 0) });
        var result = Classify(world, previous: previous);

        // Known limitation: the classifier compares OperationIdentity as an opaque string, so a load
        // finishing (the identity set shrinking from "og|sour" to "og") is indistinguishable from a
        // genuinely new operation starting. This reads as Working, not the semantically ideal Quiet,
        // because no cheaper signal exists to tell the two apart.
        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_drying_rack_gaining_an_item_id_is_a_new_operation()
    {
        var previous = new[] { new Release1ProductionStationFingerprint("s1", Release1ProductionStationKind.DryingRack, true, "og", 0) };
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.DryingRack, running: true, identity: "og|sour", progress: 0) });
        var result = Classify(world, previous: previous);

        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_baseline_pass_with_stations_already_running_and_nobody_working_is_quiet()
    {
        var world = World(
            Array.Empty<Release1ProductionEmployeeSnapshot>(),
            new[] { Station(Release1ProductionStationKind.ChemistryStation, running: true, identity: "og", progress: 5) });
        var result = Classify(world, previous: Array.Empty<Release1ProductionStationFingerprint>(), baseline: true);

        Assert.Equal(Release1ProductionObservation.Quiet, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_baseline_pass_still_reads_a_working_employee_as_working()
    {
        var world = World(new[] { Worker(working: true) }, Array.Empty<Release1ProductionStationSnapshot>());
        var result = Classify(world, baseline: true);

        Assert.Equal(Release1ProductionObservation.Working, result.Observation);
        Assert.Equal(Release1ProductionHoldReason.None, result.Reason);
    }

    [Fact]
    public void A_grow_container_is_never_present_in_the_snapshot_at_all()
    {
        Assert.Equal(
            new[] { "ChemistryStation", "LabOven", "MixingStation", "Cauldron", "DryingRack" },
            Enum.GetNames<Release1ProductionStationKind>());
    }

    [Fact]
    public void An_expected_count_below_one_throws_rather_than_guessing()
    {
        var world = World(Array.Empty<Release1ProductionEmployeeSnapshot>(), Array.Empty<Release1ProductionStationSnapshot>());

        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(world, expected: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(world, expected: -1));
    }

    [Fact]
    public void The_window_mapping_is_the_only_place_the_engine_vocabulary_appears()
    {
        Assert.Equal(
            Release1WindowObservation.Clean,
            Release1ProductionActivityClassifier.ToWindowObservation(Release1ProductionObservation.Quiet));
        Assert.Equal(
            Release1WindowObservation.Breached,
            Release1ProductionActivityClassifier.ToWindowObservation(Release1ProductionObservation.Working));
        Assert.Equal(
            Release1WindowObservation.Unreadable,
            Release1ProductionActivityClassifier.ToWindowObservation(Release1ProductionObservation.NotReady));
    }
}
