using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1KeepTheLightsOffProductionGateHarnessTests
{
    private static Release1ProductionActivitySnapshot Sample() =>
        new(new[]
        {
            new Release1ProductionPropertySnapshot("barn", "Barn",
                new[]
                {
                    new Release1ProductionEmployeeSnapshot("npc-1", "Alice Chen", "Chemist", false, true, false, 2,
                        true, "Cook", "StartChemistryStationBehaviour", true),
                    new Release1ProductionEmployeeSnapshot("npc-2", "Ben Ortiz", "Botanist", false, false, false, 900,
                        false, "Idle", "IdleBehaviour", true)
                },
                new[]
                {
                    new Release1ProductionStationSnapshot("cauldron-guid", Release1ProductionStationKind.Cauldron,
                        true, "cauldron-cook", 40, CookTime: 100, RemainingCookTime: 60),
                    new Release1ProductionStationSnapshot("rack-guid", Release1ProductionStationKind.DryingRack,
                        true, "leaf-a", 0)
                }),
            new Release1ProductionPropertySnapshot("motel", "Motel Room",
                new[]
                {
                    new Release1ProductionEmployeeSnapshot("npc-3", "Cass Poole", "Cleaner", false, false, true, 5,
                        false, null, null, false)
                },
                Array.Empty<Release1ProductionStationSnapshot>())
        });

    private sealed class FakeWorld : Release1ProductionOnlyWorld
    {
        public Release1SmallCourtesyWorldReadStatus Status { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;
        public Release1ProductionActivitySnapshot Activity { get; set; } = Release1ProductionActivitySnapshot.Empty;

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(
            out Release1ProductionActivitySnapshot activity)
        {
            activity = Activity;
            return Status;
        }
    }

    [Fact]
    public void A_null_world_faults_rather_than_throwing()
    {
        Assert.Equal(Release1StagingHarnessStatus.Faulted,
            Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(null!).Status);
        Assert.Equal(Release1StagingHarnessStatus.Faulted,
            Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(null!).Status);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldReadStatus.Pending, Release1StagingHarnessStatus.Unavailable)]
    [InlineData(Release1SmallCourtesyWorldReadStatus.NotAuthoritative, Release1StagingHarnessStatus.Rejected)]
    [InlineData(Release1SmallCourtesyWorldReadStatus.Unavailable, Release1StagingHarnessStatus.Unavailable)]
    [InlineData(Release1SmallCourtesyWorldReadStatus.Faulted, Release1StagingHarnessStatus.Faulted)]
    public void A_read_that_is_not_ready_is_reported_and_never_treated_as_an_idle_world(
        Release1SmallCourtesyWorldReadStatus readStatus, Release1StagingHarnessStatus expected)
    {
        var world = new FakeWorld { Status = readStatus };
        var dump = Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(world);
        Assert.Equal(expected, dump.Status);
        Assert.Contains(dump.Lines, line => line.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_owned_world_is_unavailable_and_says_so()
    {
        var world = new FakeWorld { Activity = Release1ProductionActivitySnapshot.Empty };
        var dump = Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(world);
        Assert.Equal(Release1StagingHarnessStatus.Unavailable, dump.Status);
        Assert.Contains(dump.Lines, line => line.Contains("no owned property", StringComparison.Ordinal));
    }

    [Fact]
    public void The_first_press_dumps_every_property_every_employee_and_every_station()
    {
        var world = new FakeWorld { Activity = Sample() };
        var dump = Release1KeepTheLightsOffProductionGateHarness.TryDumpProductionActivity(world);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, dump.Status);
        Assert.Contains(dump.Lines, line => line.Contains("property barn (Barn)", StringComparison.Ordinal));
        Assert.Contains(dump.Lines, line => line.Contains("property motel (Motel Room)", StringComparison.Ordinal));
        Assert.Contains(dump.Lines, line =>
            line.Contains("employee npc-1", StringComparison.Ordinal) &&
            line.Contains("role Chemist", StringComparison.Ordinal) &&
            line.Contains("isAnyWorkInProgress True", StringComparison.Ordinal) &&
            line.Contains("ticksSinceLastWork 2", StringComparison.Ordinal) &&
            line.Contains("behaviour Cook", StringComparison.Ordinal) &&
            line.Contains("behaviourType StartChemistryStationBehaviour", StringComparison.Ordinal));
        Assert.Contains(dump.Lines, line =>
            line.Contains("station cauldron-guid", StringComparison.Ordinal) &&
            line.Contains("kind Cauldron", StringComparison.Ordinal) &&
            line.Contains("running True", StringComparison.Ordinal) &&
            line.Contains("progress 40", StringComparison.Ordinal) &&
            line.Contains("cookTime 100", StringComparison.Ordinal) &&
            line.Contains("remainingCookTime 60", StringComparison.Ordinal));
        // A running drying rack's progress is permanently 0 by design (decision 4/Task 4's
        // identity-only rule): it carries no cookTime/remainingCookTime pair at all, unlike the
        // cauldron, since only the Cauldron has that raw counter.
        Assert.Contains(dump.Lines, line =>
            line.Contains("station rack-guid", StringComparison.Ordinal) &&
            line.Contains("kind DryingRack", StringComparison.Ordinal) &&
            line.Contains("running True", StringComparison.Ordinal) &&
            line.Contains("progress 0", StringComparison.Ordinal) &&
            line.Contains("cookTime n/a", StringComparison.Ordinal) &&
            line.Contains("remainingCookTime n/a", StringComparison.Ordinal));
        Assert.Contains(dump.Lines, line => line.Contains("press this key three times a few seconds apart", StringComparison.Ordinal));
    }

    [Fact]
    public void The_second_press_reports_only_unpaid_employees_and_cross_checks_the_two_reads()
    {
        var world = new FakeWorld { Activity = Sample() };
        var result = Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(world);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.DoesNotContain(result.Lines, line => line.Contains("npc-1", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line =>
            line.Contains("unpaid employee npc-2", StringComparison.Ordinal) &&
            line.Contains("isAnyWorkInProgress False", StringComparison.Ordinal) &&
            line.Contains("heuristic False", StringComparison.Ordinal) &&
            line.Contains("agree True", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line => line.Contains("2 unpaid employee(s), 0 disagreement(s), 0 working", StringComparison.Ordinal));
    }

    [Fact]
    public void A_disagreement_between_the_two_reads_is_reported_and_faults_the_gate()
    {
        var activity = new Release1ProductionActivitySnapshot(new[]
        {
            new Release1ProductionPropertySnapshot("barn", "Barn",
                new[]
                {
                    new Release1ProductionEmployeeSnapshot("npc-1", "Alice Chen", "Chemist", false, false, false, 1,
                        false, "Water", "WaterPotBehaviour", true)
                },
                Array.Empty<Release1ProductionStationSnapshot>())
        });
        var world = new FakeWorld { Activity = activity };
        var result = Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(world);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("agree False", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line => line.Contains("1 unpaid employee(s), 1 disagreement(s), 0 working", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unpaid_employee_that_is_working_faults_the_gate_even_when_the_two_reads_agree()
    {
        var activity = new Release1ProductionActivitySnapshot(new[]
        {
            new Release1ProductionPropertySnapshot("barn", "Barn",
                new[]
                {
                    new Release1ProductionEmployeeSnapshot("npc-1", "Alice Chen", "Chemist", false, false, false, 1,
                        true, "Cook", "StartChemistryStationBehaviour", true)
                },
                Array.Empty<Release1ProductionStationSnapshot>())
        });
        var world = new FakeWorld { Activity = activity };
        var result = Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(world);
        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("1 unpaid employee(s), 0 disagreement(s), 1 working", StringComparison.Ordinal));
    }

    [Fact]
    public void No_unpaid_employee_at_all_is_unavailable_rather_than_a_pass()
    {
        var activity = new Release1ProductionActivitySnapshot(new[]
        {
            new Release1ProductionPropertySnapshot("barn", "Barn",
                new[]
                {
                    new Release1ProductionEmployeeSnapshot("npc-1", "Alice Chen", "Chemist", false, true, false, 1,
                        false, null, null, false)
                },
                Array.Empty<Release1ProductionStationSnapshot>())
        });
        var world = new FakeWorld { Activity = activity };
        var result = Release1KeepTheLightsOffProductionGateHarness.TryCrossCheckUnpaidEmployees(world);
        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("no unpaid employee", StringComparison.Ordinal));
    }
}

/// <summary>
/// Shared stub world: the only abstract member is TryReadProductionActivity, and every other
/// IRelease1SmallCourtesyWorld member throws. Every harness test then proves by construction that the
/// harness touches nothing but the one read, and Tasks 3 to 5 reuse this exact stub.
/// </summary>
internal abstract class Release1ProductionOnlyWorld : IRelease1SmallCourtesyWorld
{
        public abstract Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity);

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new NotSupportedException();
        public virtual Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new NotSupportedException();
        public virtual Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new NotSupportedException();
        public virtual Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new NotSupportedException();
        public virtual Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new NotSupportedException();
        public virtual Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }
        public virtual Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new NotSupportedException();
}
