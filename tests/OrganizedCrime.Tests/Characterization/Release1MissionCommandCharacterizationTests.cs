using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-153 characterization: per-mission accept, reconcile-accepted and progress
// pins for the generic mission command handler. Each mission's durable accept
// success order, every mission-specific refusal message, the gate-first order,
// the ReconcileAccepted already-active NoOp, the transition-reject passthrough,
// the candidate-Validate failure, and the defer-while-applied-ahead boundary
// with its retry after the save covers. Reuses (not duplicates) OC-148's
// Release1StoryLoadReconcileTests (Accepted-activates, Deferred-reoffers,
// missing-assignment rejections, short-circuit order), the lifecycle sequence
// journal boundary test, and the per-mission *StoryTests accept/progress pins
// noted on each test. Passes on the unrefactored service; must pass unchanged
// after the handler move.
public sealed class Release1MissionCommandCharacterizationTests
{
    private const string PlayerId = "76561190000000001";
    private const string SmallCourtesyTerms = "small-courtesy-v1";
    private const string WrongAddressTerms = "wrong-address-v1";
    private const string RoomWithNoNameTerms = "room-with-no-name-v1";
    private const string ShortNoticeTerms = "short-notice-v1";
    private const string KeepTheLightsOffTerms = "keep-the-lights-off-v1";
    private const string TheEnvelopeTerms = "the-envelope-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("53535353-5353-5353-5353-535353535353");

    private static readonly string[] NoPriors = Array.Empty<string>();
    private static readonly string[] WrongAddressPriors = new[] { Release1MissionCatalog.SmallCourtesy };
    private static readonly string[] RoomWithNoNamePriors =
        new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress };
    private static readonly string[] ShortNoticePriors =
        new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress, Release1MissionCatalog.RoomWithNoName };
    private static readonly string[] KeepTheLightsOffPriors =
        new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress, Release1MissionCatalog.RoomWithNoName, Release1MissionCatalog.ShortNotice };
    private static readonly string[] TheEnvelopePriors =
        new[] { Release1MissionCatalog.SmallCourtesy, Release1MissionCatalog.WrongAddress, Release1MissionCatalog.RoomWithNoName, Release1MissionCatalog.ShortNotice, Release1MissionCatalog.KeepTheLightsOff };

    [Fact]
    public void SmallCourtesy_accept_refusals_pin_each_message_and_persist_nothing()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);

        var nullAssignment = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-null", SmallCourtesyTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("Small Courtesy assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-mismatch", SmallCourtesyTerms);
        var mismatch = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            mismatchCommand,
            SmallCourtesyAssignment(
                Release1SmallCourtesyAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth"),
                "jar"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("Small Courtesy assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            termsCommand,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, termsCommand.CorrelationId, "brick"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("Small Courtesy primary terms version was not canonical.", terms.Message);

        var acceptance = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-valid", SmallCourtesyTerms);
        var assignment = SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();
        var conflict = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            acceptance,
            assignment with { DeadDropGuid = "other-drop", DeadDropName = "Other drop" });
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal("Small Courtesy attempt already had a different assignment.", conflict.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void SmallCourtesy_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-no-assignment", SmallCourtesyTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            acceptance,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted Small Courtesy correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void SmallCourtesy_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-active", SmallCourtesyTerms);
        var assignment = SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Small Courtesy stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            makeGood,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("Small Courtesy stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[0].State);
    }

    [Fact]
    public void SmallCourtesy_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);
        AcceptPrimarySmallCourtesy(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            attempt,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, attempt.CorrelationId, "jar"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void SmallCourtesy_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);
        AcceptPrimarySmallCourtesy(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.SmallCourtesy);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            makeGood,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar"));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            makeGood,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "jar"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[0].State);
        Assert.Equal(2, harness.Service.State.SmallCourtesyAssignments.Count);
    }

    [Fact]
    public void SmallCourtesy_accepted_without_assignment_is_lenient_on_load()
    {
        // The Small Courtesy per-mission difference: the other five missions
        // reject with EffectNotFound here (OC-148 pins), Small Courtesy
        // returns null and the load succeeds with the mission still Accepted.
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, NoPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: SmallCourtesyTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.None, result.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Accepted, loaded.Service.State!.Missions[0].State);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void SmallCourtesy_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), NoPriors);
        var command = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-disposed", SmallCourtesyTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void WrongAddress_accept_refusals_pin_each_message_and_persist_nothing()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);

        var nullAssignment = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-null", WrongAddressTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("Wrong Address assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-mismatch", WrongAddressTerms);
        var mismatch = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            mismatchCommand,
            WrongAddressAssignment(
                Release1WrongAddressAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.WrongAddress, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth"),
                "jar"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("Wrong Address assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            termsCommand,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.Primary, 1, termsCommand.CorrelationId, "brick"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("Wrong Address primary terms version was not canonical.", terms.Message);

        var acceptance = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-valid", WrongAddressTerms);
        var assignment = WrongAddressAssignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();
        var conflict = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            acceptance,
            assignment with { SourceDropName = "Other source" });
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal("Wrong Address attempt already had a different assignment.", conflict.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void WrongAddress_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-no-assignment", WrongAddressTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            acceptance,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted Wrong Address correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void WrongAddress_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-active", WrongAddressTerms);
        var assignment = WrongAddressAssignment(Release1WrongAddressAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteWrongAddressAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Wrong Address stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            makeGood,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("Wrong Address stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
    }

    [Fact]
    public void WrongAddress_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        AcceptPrimaryWrongAddress(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            attempt,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.MakeGood, 2, attempt.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void WrongAddress_candidate_with_drifted_product_rejects_without_persisting()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        AcceptPrimaryWrongAddress(harness);
        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        harness.Repository.ResetEvidence();
        var makeGood = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MakeGoodAccepted, "accept-make-good-drift", attempt: 2);
        var drifted = WrongAddressAssignment(Release1WrongAddressAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick")
            with { ProductId = "other-product" };

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(makeGood, drifted);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Wrong Address product identity must remain frozen across attempts. (Parameter 'WrongAddressAssignments')", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void WrongAddress_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        AcceptPrimaryWrongAddress(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.WrongAddress);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            makeGood,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            makeGood,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(2, harness.Service.State.WrongAddressAssignments.Count);
    }

    [Fact]
    public void WrongAddress_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        var command = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-disposed", WrongAddressTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteWrongAddressAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void WrongAddress_progress_rejects_a_non_canonical_attempt()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);

        var result = harness.Service.TrySetWrongAddressStaged(0);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Wrong Address progress attempt was not canonical.", result.Message);
    }

    [Fact]
    public void WrongAddress_progress_rejects_an_attempt_with_no_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);

        var result = harness.Service.TrySetWrongAddressCustody(1);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Wrong Address progress had no accepted assignment for its attempt.", result.Message);
    }

    [Fact]
    public void WrongAddress_progress_gate_refusal_precedes_attempt_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), WrongAddressPriors);
        harness.Service.Dispose();

        var result = harness.Service.TrySetWrongAddressStaged(0);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void RoomWithNoName_acceptance_persists_assignment_atomically_then_activates()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-primary", RoomWithNoNameTerms, accepted: 10, deadline: 34);
        var assignment = RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);

        var result = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[index].State);
        Assert.Equal(assignment, Assert.Single(accepted.RoomWithNoNameAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[index].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[index].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
        Assert.Contains(active.Missions[index].AcceptedLogicalCorrelations, value =>
            Release1LogicalCorrelation.TryParse(value, out var correlation) &&
            correlation.TransitionKind == Release1TransitionKind.MissionActivated);
    }

    [Fact]
    public void RoomWithNoName_accept_refusals_pin_each_message_and_persist_nothing()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);

        var nullAssignment = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-null", RoomWithNoNameTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("Room With No Name assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-mismatch", RoomWithNoNameTerms);
        var mismatch = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            mismatchCommand,
            RoomWithNoNameAssignment(
                Release1RoomWithNoNameAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.RoomWithNoName, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth")));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("Room With No Name assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            termsCommand,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, termsCommand.CorrelationId));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("Room With No Name primary terms version was not canonical.", terms.Message);

        var acceptance = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-valid", RoomWithNoNameTerms);
        var assignment = RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();
        var conflict = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            acceptance,
            assignment with { SourceDropName = "Other source" });
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal("Room With No Name attempt already had a different assignment.", conflict.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void RoomWithNoName_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-no-assignment", RoomWithNoNameTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            acceptance,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted Room With No Name correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void RoomWithNoName_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-active", RoomWithNoNameTerms);
        var assignment = RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Room With No Name stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            makeGood,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("Room With No Name stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(new[] { 1, 2 }, harness.Service.State.RoomWithNoNameAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void RoomWithNoName_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        AcceptPrimaryRoomWithNoName(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            attempt,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.MakeGood, 2, attempt.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void RoomWithNoName_persist_failure_rejects_without_publishing_state()
    {
        var repository = new FakeRepository();
        using var harness = ActiveHarnessWithPriorsSatisfied(repository, RoomWithNoNamePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        repository.FailOnUpdateCall = 1;
        var acceptance = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-fails", RoomWithNoNameTerms);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            acceptance,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[index].State);
        Assert.Empty(harness.Service.State.RoomWithNoNameAssignments);
        Assert.Empty(repository.Updates);
    }

    [Fact]
    public void RoomWithNoName_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        AcceptPrimaryRoomWithNoName(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.RoomWithNoName);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            makeGood,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            makeGood,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(2, harness.Service.State.RoomWithNoNameAssignments.Count);
    }

    [Fact]
    public void RoomWithNoName_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        var command = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-disposed", RoomWithNoNameTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void RoomWithNoName_progress_records_in_memory_then_replaces_and_reports_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        AcceptPrimaryRoomWithNoName(harness);
        harness.Repository.ResetEvidence();
        var persistedBefore = harness.Service.LastPersistedRevision;
        var first = new Release1RoomWithNoNameProgress(Release1MissionCatalog.RoomWithNoName, 1, true, false, false, false, null, null, null);

        var recorded = harness.Service.TrySetRoomWithNoNameProgress(first);

        Assert.Equal(Release1StoryCommandStatus.Accepted, recorded.Status);
        Assert.Equal("Room With No Name progress was recorded in memory.", recorded.Message);
        Assert.Empty(harness.Repository.Updates);
        Assert.Equal(persistedBefore, harness.Service.LastPersistedRevision);
        Assert.True(harness.Service.State!.Revision > persistedBefore);

        var duplicate = harness.Service.TrySetRoomWithNoNameProgress(first);
        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Room With No Name progress was already recorded.", duplicate.Message);

        var advanced = harness.Service.TrySetRoomWithNoNameProgress(first with { Custody = true });
        Assert.True(advanced.Accepted);
        Assert.Equal(first with { Custody = true }, Assert.Single(harness.Service.State!.RoomWithNoNameProgress));

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(first with { Custody = true }, Assert.Single(harness.Repository.StoredState!.RoomWithNoNameProgress));
    }

    [Fact]
    public void RoomWithNoName_progress_rejects_null_and_invalid_with_validator_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        AcceptPrimaryRoomWithNoName(harness);

        var nullProgress = harness.Service.TrySetRoomWithNoNameProgress(null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullProgress.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullProgress.RejectReason);
        Assert.Equal("Room With No Name progress was null.", nullProgress.Message);

        var invalid = harness.Service.TrySetRoomWithNoNameProgress(
            new Release1RoomWithNoNameProgress(Release1MissionCatalog.RoomWithNoName, 1, false, true, false, false, null, null, null));
        Assert.Equal(Release1StoryCommandStatus.Rejected, invalid.Status);
        Assert.Equal("Custody cannot be recorded before the consignment was staged. (Parameter 'Custody')", invalid.Message);
        Assert.Empty(harness.Service.State!.RoomWithNoNameProgress);
    }

    [Fact]
    public void RoomWithNoName_progress_rejects_an_attempt_with_no_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);

        var result = harness.Service.TrySetRoomWithNoNameProgress(Release1RoomWithNoNameProgress.Fresh(1));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Room With No Name progress had no accepted assignment for its attempt.", result.Message);
    }

    [Fact]
    public void RoomWithNoName_progress_refuses_any_flag_regression_within_an_attempt()
    {
        // Each regressed row still passes Validate (Validate runs before the
        // regression check), so each rejection pins the monotonicity arm.
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        AcceptPrimaryRoomWithNoName(harness);
        var staged = new Release1RoomWithNoNameProgress(Release1MissionCatalog.RoomWithNoName, 1, true, false, false, false, null, null, null);
        Assert.True(harness.Service.TrySetRoomWithNoNameProgress(staged).Accepted);

        var regressStaged = harness.Service.TrySetRoomWithNoNameProgress(Release1RoomWithNoNameProgress.Fresh(1));
        Assert.Equal(Release1StoryCommandStatus.Rejected, regressStaged.Status);
        Assert.Equal("Room With No Name progress flags cannot regress within an attempt.", regressStaged.Message);

        var custody = staged with { Custody = true };
        Assert.True(harness.Service.TrySetRoomWithNoNameProgress(custody).Accepted);

        var regressCustody = harness.Service.TrySetRoomWithNoNameProgress(staged);
        Assert.Equal(Release1StoryCommandStatus.Rejected, regressCustody.Status);
        Assert.Equal("Room With No Name progress flags cannot regress within an attempt.", regressCustody.Message);

        var stowed = custody with { Stowed = true, StowedAtGameMinutes = 12d, HoldingClosetGuid = "closet-1" };
        Assert.True(harness.Service.TrySetRoomWithNoNameProgress(stowed).Accepted);

        var regressStowed = harness.Service.TrySetRoomWithNoNameProgress(custody);
        Assert.Equal(Release1StoryCommandStatus.Rejected, regressStowed.Status);
        Assert.Equal("Room With No Name progress flags cannot regress within an attempt.", regressStowed.Message);

        var hold = stowed with { HoldSatisfied = true };
        Assert.True(harness.Service.TrySetRoomWithNoNameProgress(hold).Accepted);

        var regressHold = harness.Service.TrySetRoomWithNoNameProgress(stowed);
        Assert.Equal(Release1StoryCommandStatus.Rejected, regressHold.Status);
        Assert.Equal("Room With No Name progress flags cannot regress within an attempt.", regressHold.Message);

        Assert.Equal(hold, Assert.Single(harness.Service.State!.RoomWithNoNameProgress));
    }

    [Fact]
    public void RoomWithNoName_progress_accepts_a_non_flag_change_within_an_attempt()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        AcceptPrimaryRoomWithNoName(harness);
        var first = new Release1RoomWithNoNameProgress(Release1MissionCatalog.RoomWithNoName, 1, true, false, false, false, null, null, 7d);
        Assert.True(harness.Service.TrySetRoomWithNoNameProgress(first).Accepted);

        var cleared = harness.Service.TrySetRoomWithNoNameProgress(first with { MissingSincePassGameMinutes = null });

        Assert.Equal(Release1StoryCommandStatus.Accepted, cleared.Status);
        Assert.Equal(first with { MissingSincePassGameMinutes = null }, Assert.Single(harness.Service.State!.RoomWithNoNameProgress));
    }

    [Fact]
    public void RoomWithNoName_progress_gate_refusal_precedes_null_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), RoomWithNoNamePriors);
        harness.Service.Dispose();

        var result = harness.Service.TrySetRoomWithNoNameProgress(null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void ShortNotice_acceptance_persists_assignment_atomically_then_activates()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-primary", ShortNoticeTerms, accepted: 10, deadline: 34);
        var assignment = ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId, "brick");

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[index].State);
        Assert.Equal(assignment, Assert.Single(accepted.ShortNoticeAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[index].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[index].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
    }

    [Fact]
    public void ShortNotice_accept_refusals_pin_each_message_and_persist_nothing()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);

        var nullAssignment = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-null", ShortNoticeTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("Short Notice assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-mismatch", ShortNoticeTerms);
        var mismatch = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            mismatchCommand,
            ShortNoticeAssignment(
                Release1ShortNoticeAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.ShortNotice, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth"),
                "jar"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("Short Notice assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            termsCommand,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, termsCommand.CorrelationId, "brick"));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("Short Notice primary terms version was not canonical.", terms.Message);

        var acceptance = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-valid", ShortNoticeTerms);
        var assignment = ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();
        var conflict = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            acceptance,
            assignment with { HandoffDropName = "Other handoff" });
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal("Short Notice attempt already had a different assignment.", conflict.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void ShortNotice_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-no-assignment", ShortNoticeTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            acceptance,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted Short Notice correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void ShortNotice_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-active", ShortNoticeTerms);
        var assignment = ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick");
        Assert.True(harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteShortNoticeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Short Notice stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            makeGood,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("Short Notice stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(new[] { 1, 2 }, harness.Service.State.ShortNoticeAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void ShortNotice_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        AcceptPrimaryShortNotice(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            attempt,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, attempt.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void ShortNotice_candidate_with_drifted_product_rejects_without_persisting()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        AcceptPrimaryShortNotice(harness);
        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        harness.Repository.ResetEvidence();
        var makeGood = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MakeGoodAccepted, "accept-make-good-drift", attempt: 2);
        var drifted = ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick")
            with { ProductId = "other-product" };

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(makeGood, drifted);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Short Notice product, packaging, and value convention must remain frozen across attempts. (Parameter 'ShortNoticeAssignments')", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void ShortNotice_persist_failure_rejects_without_publishing_state()
    {
        var repository = new FakeRepository();
        using var harness = ActiveHarnessWithPriorsSatisfied(repository, ShortNoticePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        repository.FailOnUpdateCall = 1;
        var acceptance = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-fails", ShortNoticeTerms);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            acceptance,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, acceptance.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[index].State);
        Assert.Empty(harness.Service.State.ShortNoticeAssignments);
        Assert.Empty(repository.Updates);
    }

    [Fact]
    public void ShortNotice_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        AcceptPrimaryShortNotice(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.ShortNotice);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            makeGood,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            makeGood,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.MakeGood, 2, makeGood.CorrelationId, "brick"));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(2, harness.Service.State.ShortNoticeAssignments.Count);
    }

    [Fact]
    public void ShortNotice_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        var command = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-disposed", ShortNoticeTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteShortNoticeAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void ShortNotice_progress_replaces_and_reports_already_recorded_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        AcceptPrimaryShortNotice(harness);
        var first = new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 2, null, false);
        Assert.True(harness.Service.TrySetShortNoticeProgress(first).Accepted);

        var duplicate = harness.Service.TrySetShortNoticeProgress(first);
        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Short Notice progress was already recorded.", duplicate.Message);

        var advanced = harness.Service.TrySetShortNoticeProgress(first with { ObservedQuantity = 3 });
        Assert.True(advanced.Accepted);
        Assert.Equal(first with { ObservedQuantity = 3 }, Assert.Single(harness.Service.State!.ShortNoticeProgress));
    }

    [Fact]
    public void ShortNotice_progress_rejects_null_and_invalid_with_validator_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        AcceptPrimaryShortNotice(harness);

        var nullProgress = harness.Service.TrySetShortNoticeProgress(null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullProgress.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullProgress.RejectReason);
        Assert.Equal("Short Notice progress was null.", nullProgress.Message);

        var invalid = harness.Service.TrySetShortNoticeProgress(
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, -1, null, false));
        Assert.Equal(Release1StoryCommandStatus.Rejected, invalid.Status);
        Assert.Equal("Specified argument was out of the range of valid values. (Parameter 'ObservedQuantity')", invalid.Message);
        Assert.Empty(harness.Service.State!.ShortNoticeProgress);
    }

    [Fact]
    public void ShortNotice_spread_withdrawal_pins_the_regression_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        AcceptPrimaryShortNotice(harness);
        Assert.True(harness.Service.TrySetShortNoticeProgress(
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 3, null, true)).Accepted);

        var withdrawn = harness.Service.TrySetShortNoticeProgress(
            new Release1ShortNoticeProgress(Release1MissionCatalog.ShortNotice, 1, 3, null, false));

        Assert.Equal(Release1StoryCommandStatus.Rejected, withdrawn.Status);
        Assert.Equal("Short Notice spread notice cannot be withdrawn within an attempt.", withdrawn.Message);
    }

    [Fact]
    public void ShortNotice_progress_gate_refusal_precedes_null_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), ShortNoticePriors);
        harness.Service.Dispose();

        var result = harness.Service.TrySetShortNoticeProgress(null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void KeepTheLightsOff_acceptance_persists_assignment_atomically_then_activates()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-primary", KeepTheLightsOffTerms, accepted: 10, deadline: 34);
        var assignment = KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[index].State);
        Assert.Equal(assignment, Assert.Single(accepted.KeepTheLightsOffAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[index].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[index].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
    }

    [Fact]
    public void KeepTheLightsOff_accept_refusals_pin_each_message_and_persist_nothing()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);

        var nullAssignment = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-null", KeepTheLightsOffTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("Keep the Lights Off assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-mismatch", KeepTheLightsOffTerms);
        var mismatch = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            mismatchCommand,
            KeepTheLightsOffAssignment(
                Release1KeepTheLightsOffAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.KeepTheLightsOff, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth")));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("Keep the Lights Off assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            termsCommand,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, termsCommand.CorrelationId));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("Keep the Lights Off primary terms version was not canonical.", terms.Message);

        var acceptance = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-valid", KeepTheLightsOffTerms);
        var assignment = KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();
        var conflict = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            acceptance,
            assignment with { ExpectedOwnedPropertyCount = 2 });
        Assert.Equal(Release1StoryCommandStatus.Rejected, conflict.Status);
        Assert.Equal("Keep the Lights Off attempt already had a different assignment.", conflict.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void KeepTheLightsOff_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-no-assignment", KeepTheLightsOffTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            acceptance,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted Keep the Lights Off correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void KeepTheLightsOff_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-active", KeepTheLightsOffTerms);
        var assignment = KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Keep the Lights Off stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            makeGood,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("Keep the Lights Off stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(new[] { 1, 2 }, harness.Service.State.KeepTheLightsOffAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void KeepTheLightsOff_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        AcceptPrimaryKeepTheLightsOff(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            attempt,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.MakeGood, 2, attempt.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void KeepTheLightsOff_persist_failure_rejects_without_publishing_state()
    {
        var repository = new FakeRepository();
        using var harness = ActiveHarnessWithPriorsSatisfied(repository, KeepTheLightsOffPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        repository.FailOnUpdateCall = 1;
        var acceptance = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-fails", KeepTheLightsOffTerms);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            acceptance,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[index].State);
        Assert.Empty(harness.Service.State.KeepTheLightsOffAssignments);
        Assert.Empty(repository.Updates);
    }

    [Fact]
    public void KeepTheLightsOff_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        AcceptPrimaryKeepTheLightsOff(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.KeepTheLightsOff);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            makeGood,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            makeGood,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(2, harness.Service.State.KeepTheLightsOffAssignments.Count);
    }

    [Fact]
    public void KeepTheLightsOff_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        var command = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-disposed", KeepTheLightsOffTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void KeepTheLightsOff_progress_replaces_and_reports_already_recorded_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        AcceptPrimaryKeepTheLightsOff(harness);
        var first = new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 9d, null);
        Assert.True(harness.Service.TrySetKeepTheLightsOffProgress(first).Accepted);

        var duplicate = harness.Service.TrySetKeepTheLightsOffProgress(first);
        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("Keep the Lights Off progress was already recorded.", duplicate.Message);

        var advanced = harness.Service.TrySetKeepTheLightsOffProgress(first with { BreachSincePassGameMinutes = 11d });
        Assert.True(advanced.Accepted);
        Assert.Equal(first with { BreachSincePassGameMinutes = 11d }, Assert.Single(harness.Service.State!.KeepTheLightsOffProgress));
    }

    [Fact]
    public void KeepTheLightsOff_progress_rejects_null_and_invalid_with_validator_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        AcceptPrimaryKeepTheLightsOff(harness);

        var nullProgress = harness.Service.TrySetKeepTheLightsOffProgress(null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullProgress.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullProgress.RejectReason);
        Assert.Equal("Keep the Lights Off progress was null.", nullProgress.Message);

        var invalid = harness.Service.TrySetKeepTheLightsOffProgress(
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, null, 11d));
        Assert.Equal(Release1StoryCommandStatus.Rejected, invalid.Status);
        Assert.Equal("A breach cannot be recorded before the clear was confirmed. (Parameter 'BreachSincePassGameMinutes')", invalid.Message);
        Assert.Empty(harness.Service.State!.KeepTheLightsOffProgress);
    }

    [Fact]
    public void KeepTheLightsOff_clear_withdrawal_pins_the_regression_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        AcceptPrimaryKeepTheLightsOff(harness);
        Assert.True(harness.Service.TrySetKeepTheLightsOffProgress(
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 9d, null)).Accepted);

        var withdrawn = harness.Service.TrySetKeepTheLightsOffProgress(
            Release1KeepTheLightsOffProgress.Fresh(1));

        Assert.Equal(Release1StoryCommandStatus.Rejected, withdrawn.Status);
        Assert.Equal("Keep the Lights Off clear confirmation cannot be withdrawn within an attempt.", withdrawn.Message);
    }

    [Fact]
    public void KeepTheLightsOff_progress_gate_refusal_precedes_null_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), KeepTheLightsOffPriors);
        harness.Service.Dispose();

        var result = harness.Service.TrySetKeepTheLightsOffProgress(null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void TheEnvelope_acceptance_persists_assignment_atomically_then_activates()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-primary", TheEnvelopeTerms, accepted: 10, deadline: 34);
        var assignment = TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, acceptance.Attempt, acceptance.CorrelationId);

        var result = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Equal(2, harness.Repository.Updates.Count);
        var accepted = harness.Repository.Updates[0];
        Assert.Equal(Release1MissionState.Accepted, accepted.Missions[index].State);
        Assert.Equal(assignment, Assert.Single(accepted.TheEnvelopeAssignments));
        Assert.Contains(acceptance.CorrelationId, accepted.Missions[index].AcceptedLogicalCorrelations);
        var active = harness.Repository.Updates[1];
        Assert.Equal(Release1MissionState.Active, active.Missions[index].State);
        Assert.Equal(active.Revision, harness.Service.LastPersistedRevision);
    }

    [Fact]
    public void TheEnvelope_accept_refusals_pin_each_message_and_persist_nothing()
    {
        // No conflict sub-case: every The Envelope assignment field is either
        // match-critical (mission, attempt, mode, authorization) or init-frozen
        // to its stage value (amount, hold room, closet count, deadline), so a
        // differing assignment for the same attempt is unconstructable and the
        // conflict arm is unreachable for this mission.
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);

        var nullAssignment = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-null", TheEnvelopeTerms),
            null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullAssignment.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullAssignment.RejectReason);
        Assert.Equal("The Envelope assignment was null.", nullAssignment.Message);

        var mismatchCommand = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-mismatch", TheEnvelopeTerms);
        var mismatch = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            mismatchCommand,
            TheEnvelopeAssignment(
                Release1TheEnvelopeAssignmentMode.MakeGood,
                1,
                Correlation(Release1MissionCatalog.TheEnvelope, 1, Release1TransitionKind.MakeGoodAccepted, "mismatch-auth")));
        Assert.Equal(Release1StoryCommandStatus.Rejected, mismatch.Status);
        Assert.Equal("The Envelope assignment did not match its acceptance command.", mismatch.Message);

        var termsCommand = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-terms", "wrong-terms");
        var terms = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            termsCommand,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, 1, termsCommand.CorrelationId));
        Assert.Equal(Release1StoryCommandStatus.Rejected, terms.Status);
        Assert.Equal("The Envelope primary terms version was not canonical.", terms.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void TheEnvelope_idempotent_reaccept_without_assignment_rejects_no_atomic_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-no-assignment", TheEnvelopeTerms);
        Assert.True(harness.Service.TryExecuteDurably(acceptance).Accepted);
        harness.Repository.ResetEvidence();

        var result = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            acceptance,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Accepted The Envelope correlation had no atomic assignment.", result.Message);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void TheEnvelope_reconcile_accepted_reports_already_active_as_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        var acceptance = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-active", TheEnvelopeTerms);
        var assignment = TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, 1, acceptance.CorrelationId);
        Assert.True(harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(acceptance, assignment).Accepted);
        harness.Repository.ResetEvidence();

        var duplicate = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(acceptance, assignment);

        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("The Envelope stage was already active.", duplicate.Message);
        Assert.Empty(harness.Repository.Updates);

        Assert.True(harness.Service.TryExecuteDurably(MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.RequiredFailure, "fail-primary")).Accepted);
        var makeGood = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MakeGoodAccepted, "accept-make-good", attempt: 2);
        var reconciled = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            makeGood,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, reconciled.Status);
        Assert.Equal("The Envelope stage was already active.", reconciled.Message);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(new[] { 1, 2 }, harness.Service.State.TheEnvelopeAssignments.Select(value => value.Attempt));
    }

    [Fact]
    public void TheEnvelope_transition_reject_passes_the_transition_message_through()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        AcceptPrimaryTheEnvelope(harness);
        var attempt = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MakeGoodAccepted, "makegood-while-active", attempt: 2);
        var direct = harness.Service.TryExecuteDurably(attempt);
        Assert.Equal(Release1StoryCommandStatus.Rejected, direct.Status);

        var result = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            attempt,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.MakeGood, 2, attempt.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal(direct.Message, result.Message);
    }

    [Fact]
    public void TheEnvelope_persist_failure_rejects_without_publishing_state()
    {
        var repository = new FakeRepository();
        using var harness = ActiveHarnessWithPriorsSatisfied(repository, TheEnvelopePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        repository.FailOnUpdateCall = 1;
        var acceptance = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-fails", TheEnvelopeTerms);
        var before = harness.Service.State;

        var result = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            acceptance,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, 1, acceptance.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.RepositoryFaulted, result.RejectReason);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(Release1MissionState.Offered, harness.Service.State!.Missions[index].State);
        Assert.Empty(harness.Service.State.TheEnvelopeAssignments);
        Assert.Empty(repository.Updates);
    }

    [Fact]
    public void TheEnvelope_durable_accept_defers_while_applied_ahead_and_retries_after_cover()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        AcceptPrimaryTheEnvelope(harness);
        ArmAppliedAhead(harness, Release1MissionCatalog.TheEnvelope);
        Assert.True(harness.Service.TryExecute(MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.RequiredFailure, "fail-memory")).Accepted);
        harness.Repository.ResetEvidence();
        var before = harness.Service.State;

        var makeGood = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MakeGoodAccepted, "accept-makegood-deferred", attempt: 2);
        var deferred = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            makeGood,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, deferred.Status);
        Assert.Same(before, harness.Service.State);
        Assert.Equal(0, harness.Repository.UpdateCalls);

        SaveBoundary(harness);
        var retry = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            makeGood,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.MakeGood, 2, makeGood.CorrelationId));

        Assert.Equal(Release1StoryCommandStatus.NoOp, retry.Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Service.State!.Missions[index].State);
        Assert.Equal(2, harness.Service.State.TheEnvelopeAssignments.Count);
    }

    [Fact]
    public void TheEnvelope_gate_refusal_precedes_assignment_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        var command = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-disposed", TheEnvelopeTerms);
        harness.Service.Dispose();

        var result = harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(command, null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    [Fact]
    public void TheEnvelope_progress_records_in_memory_then_replaces_and_reports_noop()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        AcceptPrimaryTheEnvelope(harness);
        harness.Repository.ResetEvidence();
        var persistedBefore = harness.Service.LastPersistedRevision;
        var first = new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 100d, null, false);

        var recorded = harness.Service.TrySetTheEnvelopeProgress(first);

        Assert.Equal(Release1StoryCommandStatus.Accepted, recorded.Status);
        Assert.Equal("The Envelope progress was recorded in memory.", recorded.Message);
        Assert.Empty(harness.Repository.Updates);
        Assert.Equal(persistedBefore, harness.Service.LastPersistedRevision);
        Assert.True(harness.Service.State!.Revision > persistedBefore);

        var duplicate = harness.Service.TrySetTheEnvelopeProgress(first);
        Assert.Equal(Release1StoryCommandStatus.NoOp, duplicate.Status);
        Assert.Equal("The Envelope progress was already recorded.", duplicate.Message);

        var advanced = harness.Service.TrySetTheEnvelopeProgress(first with { ObservedBalance = 250d });
        Assert.True(advanced.Accepted);
        Assert.Equal(first with { ObservedBalance = 250d }, Assert.Single(harness.Service.State!.TheEnvelopeProgress));

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(first with { ObservedBalance = 250d }, Assert.Single(harness.Repository.StoredState!.TheEnvelopeProgress));
    }

    [Fact]
    public void TheEnvelope_progress_rejects_null_and_invalid_with_validator_message()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        AcceptPrimaryTheEnvelope(harness);

        var nullProgress = harness.Service.TrySetTheEnvelopeProgress(null!);
        Assert.Equal(Release1StoryCommandStatus.Rejected, nullProgress.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, nullProgress.RejectReason);
        Assert.Equal("The Envelope progress was null.", nullProgress.Message);

        var invalid = harness.Service.TrySetTheEnvelopeProgress(
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, -5d, null, false));
        Assert.Equal(Release1StoryCommandStatus.Rejected, invalid.Status);
        Assert.Equal("Specified argument was out of the range of valid values. (Parameter 'ObservedBalance')", invalid.Message);
        Assert.Empty(harness.Service.State!.TheEnvelopeProgress);
    }

    [Fact]
    public void TheEnvelope_progress_rejects_an_attempt_with_no_assignment()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);

        var result = harness.Service.TrySetTheEnvelopeProgress(Release1TheEnvelopeProgress.Fresh(1));

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("The Envelope progress had no accepted assignment for its attempt.", result.Message);
    }

    [Fact]
    public void TheEnvelope_spread_withdrawal_rejects_but_the_observed_balance_may_fall()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        AcceptPrimaryTheEnvelope(harness);
        Assert.True(harness.Service.TrySetTheEnvelopeProgress(
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 300d, null, true)).Accepted);

        var withdrawn = harness.Service.TrySetTheEnvelopeProgress(
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 300d, null, false));
        Assert.Equal(Release1StoryCommandStatus.Rejected, withdrawn.Status);
        Assert.Equal("The Envelope spread notice cannot be withdrawn within an attempt.", withdrawn.Message);

        var fallen = harness.Service.TrySetTheEnvelopeProgress(
            new Release1TheEnvelopeProgress(Release1MissionCatalog.TheEnvelope, 1, 120d, 180d, true));
        Assert.True(fallen.Accepted);
        Assert.Equal(120d, harness.Service.State!.TheEnvelopeProgress.Single().ObservedBalance);
    }

    [Fact]
    public void TheEnvelope_progress_gate_refusal_precedes_null_validation()
    {
        using var harness = ActiveHarnessWithPriorsSatisfied(new FakeRepository(), TheEnvelopePriors);
        harness.Service.Dispose();

        var result = harness.Service.TrySetTheEnvelopeProgress(null!);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Equal(Release1StoryRuntimeRejectReason.Disposed, result.RejectReason);
    }

    private static void AcceptPrimarySmallCourtesy(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.SmallCourtesy, Release1TransitionKind.MissionAccepted, "accept-primary-helper", SmallCourtesyTerms);
        Assert.True(harness.Service.TryExecuteSmallCourtesyAcceptanceDurably(
            command,
            SmallCourtesyAssignment(Release1SmallCourtesyAssignmentMode.Primary, 1, command.CorrelationId, "brick")).Accepted);
    }

    private static void AcceptPrimaryWrongAddress(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted, "accept-primary-helper", WrongAddressTerms);
        Assert.True(harness.Service.TryExecuteWrongAddressAcceptanceDurably(
            command,
            WrongAddressAssignment(Release1WrongAddressAssignmentMode.Primary, 1, command.CorrelationId, "brick")).Accepted);
    }

    private static void AcceptPrimaryRoomWithNoName(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted, "accept-primary-helper", RoomWithNoNameTerms);
        Assert.True(harness.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
            command,
            RoomWithNoNameAssignment(Release1RoomWithNoNameAssignmentMode.Primary, 1, command.CorrelationId)).Accepted);
    }

    private static void AcceptPrimaryShortNotice(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted, "accept-primary-helper", ShortNoticeTerms);
        Assert.True(harness.Service.TryExecuteShortNoticeAcceptanceDurably(
            command,
            ShortNoticeAssignment(Release1ShortNoticeAssignmentMode.Primary, 1, command.CorrelationId, "brick")).Accepted);
    }

    private static void AcceptPrimaryKeepTheLightsOff(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted, "accept-primary-helper", KeepTheLightsOffTerms);
        Assert.True(harness.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
            command,
            KeepTheLightsOffAssignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, command.CorrelationId)).Accepted);
    }

    private static void AcceptPrimaryTheEnvelope(Harness harness)
    {
        var command = MissionCommand(harness, Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted, "accept-primary-helper", TheEnvelopeTerms);
        Assert.True(harness.Service.TryExecuteTheEnvelopeAcceptanceDurably(
            command,
            TheEnvelopeAssignment(Release1TheEnvelopeAssignmentMode.Primary, 1, command.CorrelationId)).Accepted);
    }

    private static void ArmAppliedAhead(Harness harness, string missionKey)
    {
        var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(missionKey)];
        var activation = FindCorrelation(mission, Release1TransitionKind.MissionActivated);
        var effect = new Release1NativeEffectJournalEntry(
            $"oc153-{mission.Attempt}-effect",
            missionKey,
            mission.Attempt,
            "CargoTransfer",
            "oc-source",
            "native-dest",
            "cargo",
            Release1NativeEffectPhase.Prepared,
            null,
            harness.Service.State!.Revision + 1,
            AuthorizedStoryCorrelationId: activation,
            AuthorizedMissionRevision: mission.Revision);
        Assert.True(harness.Service.TryPrepareNativeEffect(effect).Accepted);
        SaveBoundary(harness);
        Assert.True(harness.Service.TryMarkNativeEffectApplied(effect.EffectId, "native-receipt").Accepted);
        harness.Repository.ResetEvidence();
    }

    private static void SaveBoundary(Harness harness)
    {
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
    }

    private static string FindCorrelation(Release1MissionRecord mission, Release1TransitionKind kind) =>
        mission.AcceptedLogicalCorrelations.Single(value =>
            Release1LogicalCorrelation.TryParse(value, out var correlation) && correlation.TransitionKind == kind);

    private static string Correlation(string missionKey, int attempt, Release1TransitionKind kind, string receipt) =>
        Release1LogicalCorrelation.Create(PlayerId, missionKey, attempt, kind, receipt).Value;

    private static Harness ActiveHarnessWithPriorsSatisfied(FakeRepository repository, string[] priorKeys)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        var intro = new Release1StoryCommand(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            "intro-oc-153",
            Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-oc-153").Value);
        Assert.True(service.TryExecuteDurably(intro).Accepted);
        var harness = new Harness(service, context, repository);
        foreach (var missionKey in priorKeys)
            SatisfyMission(harness, missionKey);
        repository.ResetEvidence();
        return harness;
    }

    private static void SatisfyMission(Harness harness, string missionKey)
    {
        var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(missionKey)];
        var acceptReceipt = $"accept-{missionKey}-unlock";
        var accept = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt,
            Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
            "terms-v1");
        Assert.True(harness.Service.TryExecuteDurably(accept).Accepted);

        var activateReceipt = $"activate-{missionKey}-unlock";
        var activate = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt,
            Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
        Assert.True(harness.Service.TryExecuteDurably(activate).Accepted);

        var completeReceipt = $"complete-{missionKey}-unlock";
        var complete = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch, harness.Context.Snapshot.PlayerId,
            missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt,
            Release1LogicalCorrelation.Create(PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
            CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: $"reward-{missionKey}-unlock");
        Assert.True(harness.Service.TryExecuteDurably(complete).Accepted);
    }

    private static Release1StoryCommand MissionCommand(
        Harness harness,
        string missionKey,
        Release1TransitionKind kind,
        string receipt,
        string? terms = null,
        int? attempt = null,
        double? accepted = null,
        double? deadline = null)
    {
        var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(missionKey)];
        var commandAttempt = attempt ?? mission.Attempt;
        return new(
            harness.Context.Snapshot.SessionEpoch,
            harness.Context.Snapshot.LoadEpoch,
            harness.Context.Snapshot.PlayerId,
            missionKey,
            commandAttempt,
            kind,
            receipt,
            Release1LogicalCorrelation.Create(harness.Context.Snapshot.PlayerId, missionKey, commandAttempt, kind, receipt).Value,
            terms,
            accepted,
            deadline);
    }

    private static Release1SmallCourtesyAssignment SmallCourtesyAssignment(
        Release1SmallCourtesyAssignmentMode mode,
        int attempt,
        string authorization,
        string packaging) =>
        new(
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            500,
            packaging,
            packaging == "brick" ? "Brick" : "Jar",
            $"drop-{attempt}",
            $"Drop {attempt}",
            "A dead drop",
            attempt,
            2,
            3,
            1.25);

    private static Release1WrongAddressAssignment WrongAddressAssignment(
        Release1WrongAddressAssignmentMode mode,
        int attempt,
        string authorization,
        string packagingId) =>
        new(
            Release1MissionCatalog.WrongAddress,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            packagingId,
            packagingId == "brick" ? "Brick" : "Jar",
            1,
            $"drop-source-{attempt}",
            $"Source {attempt}",
            "A wrong address",
            attempt,
            2,
            3,
            $"drop-handoff-{attempt}",
            $"Handoff {attempt}",
            "The right address",
            attempt + 10,
            2,
            3,
            1.25);

    private static Release1RoomWithNoNameAssignment RoomWithNoNameAssignment(
        Release1RoomWithNoNameAssignmentMode mode,
        int attempt,
        string authorization) =>
        new(
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            mode,
            authorization,
            "cocaine",
            "Cocaine",
            "brick",
            "Brick",
            1,
            "drop-source",
            "Source Drop",
            "Behind the laundromat.",
            1,
            2,
            3,
            "drop-handoff",
            "Handoff Drop",
            "Under the pier.",
            4,
            5,
            6,
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            Release1RoomWithNoNameAssignment.OneInGameDayMinutes,
            Release1RoomWithNoNameAssignment.RoomRewardMultiplier);

    private static Release1ShortNoticeAssignment ShortNoticeAssignment(
        Release1ShortNoticeAssignmentMode mode,
        int attempt,
        string authorization,
        string packaging) =>
        new(
            Release1MissionCatalog.ShortNotice,
            attempt,
            mode,
            authorization,
            "product",
            "Product",
            packaging,
            packaging == "brick" ? "Brick" : "Jar",
            Release1ShortNoticeAssignment.QuantityFor(mode),
            $"drop-handoff-{attempt}",
            $"Handoff {attempt}",
            "The right address",
            attempt + 10,
            2,
            3,
            mode == Release1ShortNoticeAssignmentMode.Recovery ? null : Release1ShortNoticeAssignment.TimedStageGameMinutes,
            Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
            Release1ShortNoticeValueConvention.PerUnit);

    private static Release1KeepTheLightsOffAssignment KeepTheLightsOffAssignment(
        Release1KeepTheLightsOffAssignmentMode mode,
        int attempt,
        string authorization) =>
        new(
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            mode,
            authorization,
            1,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);

    private static Release1TheEnvelopeAssignment TheEnvelopeAssignment(
        Release1TheEnvelopeAssignmentMode mode,
        int attempt,
        string authorization) =>
        new(
            Release1MissionCatalog.TheEnvelope,
            attempt,
            mode,
            authorization,
            Release1TheEnvelopeAssignment.AmountFor(mode),
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : Release1TheEnvelopeAssignment.TimedStageGameMinutes);

    private sealed class Harness : IDisposable
    {
        public Harness(Release1StoryRuntimeService service, FakeContext context, FakeRepository repository)
        {
            Service = service;
            Context = context;
            Repository = repository;
        }

        public Release1StoryRuntimeService Service { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }

        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; set; }
        public List<Release1StoryState> Updates { get; } = new();
        public int UpdateCalls { get; private set; }
        public int? FailOnUpdateCall { get; set; }

        public Release1StoryStoreLoadResult Load() =>
            new(
                true,
                StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
                Release1StoryStoreFailureReason.None,
                string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            UpdateCalls++;
            if (FailOnUpdateCall == UpdateCalls)
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public void ResetEvidence()
        {
            Updates.Clear();
            UpdateCalls = 0;
            FailOnUpdateCall = null;
        }
    }
}
