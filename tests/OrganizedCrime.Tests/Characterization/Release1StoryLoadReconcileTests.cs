using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148 characterization (fix round, Major): the five non-Small-Courtesy
// load reconciliations (Wrong Address, Room With No Name, Short Notice, Keep
// The Lights Off, The Envelope). The first characterization round only
// exercised Small Courtesy, which is the lenient one: a missing assignment
// returns null there, while these five reject with EffectNotFound. Each
// mission pins Accepted-with-assignment activation, Deferred reoffer, and the
// missing-assignment rejection boundary; the order tests pin the fixed
// reconcile sequence through short-circuit and intermediate persists. Passes
// on the d850ff8 logic unchanged: the reconcile bodies and their call order
// moved verbatim into AfterLoadForLifecycle.
public sealed class Release1StoryLoadReconcileTests
{
    private const string PlayerId = "76561190000000001";
    private const string WrongAddressTerms = "wrong-address-v1";
    private const string RoomWithNoNameTerms = "room-with-no-name-v1";
    private const string ShortNoticeTerms = "short-notice-v1";
    private const string KeepTheLightsOffTerms = "keep-the-lights-off-v1";
    private const string TheEnvelopeTerms = "the-envelope-v1";
    private static readonly Guid SessionEpoch = Guid.Parse("44444444-4444-4444-4444-444444444444");

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
    public void WrongAddress_accepted_activates_and_deferred_reoffers_on_load()
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, WrongAddressPriors))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = MissionCommand(first, Release1MissionCatalog.WrongAddress,
                Release1TransitionKind.MissionAccepted, "accept-before-activation-fault",
                terms: WrongAddressTerms, accepted: 10, deadline: 34);
            var result = first.Service.TryExecuteWrongAddressAcceptanceDurably(
                acceptance,
                WrongAddressAssignment(1, acceptance.CorrelationId));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[index].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[index].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(deferring, WrongAddressPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[index].State);
        Assert.Equal(2, reoffered.Service.State.Missions[index].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void RoomWithNoName_accepted_activates_and_deferred_reoffers_on_load()
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, RoomWithNoNamePriors))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = MissionCommand(first, Release1MissionCatalog.RoomWithNoName,
                Release1TransitionKind.MissionAccepted, "accept-before-activation-fault",
                terms: RoomWithNoNameTerms, accepted: 10, deadline: 34);
            var result = first.Service.TryExecuteRoomWithNoNameAcceptanceDurably(
                acceptance,
                RoomWithNoNameAssignment(1, acceptance.CorrelationId));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[index].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[index].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(deferring, RoomWithNoNamePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[index].State);
        Assert.Equal(2, reoffered.Service.State.Missions[index].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void ShortNotice_accepted_activates_and_deferred_reoffers_on_load()
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, ShortNoticePriors))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = MissionCommand(first, Release1MissionCatalog.ShortNotice,
                Release1TransitionKind.MissionAccepted, "accept-before-activation-fault",
                terms: ShortNoticeTerms, accepted: 10, deadline: 34);
            var result = first.Service.TryExecuteShortNoticeAcceptanceDurably(
                acceptance,
                ShortNoticeAssignment(1, acceptance.CorrelationId));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[index].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[index].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(deferring, ShortNoticePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[index].State);
        Assert.Equal(2, reoffered.Service.State.Missions[index].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void KeepTheLightsOff_accepted_activates_and_deferred_reoffers_on_load()
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, KeepTheLightsOffPriors))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = MissionCommand(first, Release1MissionCatalog.KeepTheLightsOff,
                Release1TransitionKind.MissionAccepted, "accept-before-activation-fault",
                terms: KeepTheLightsOffTerms, accepted: 10, deadline: 34);
            var result = first.Service.TryExecuteKeepTheLightsOffAcceptanceDurably(
                acceptance,
                KeepTheLightsOffAssignment(1, acceptance.CorrelationId));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[index].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[index].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(deferring, KeepTheLightsOffPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[index].State);
        Assert.Equal(2, reoffered.Service.State.Missions[index].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void TheEnvelope_accepted_activates_and_deferred_reoffers_on_load()
    {
        var index = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, TheEnvelopePriors))
        {
            repository.FailOnUpdateCall = 2;
            var acceptance = MissionCommand(first, Release1MissionCatalog.TheEnvelope,
                Release1TransitionKind.MissionAccepted, "accept-before-activation-fault",
                terms: TheEnvelopeTerms, accepted: 10, deadline: 34);
            var result = first.Service.TryExecuteTheEnvelopeAcceptanceDurably(
                acceptance,
                TheEnvelopeAssignment(1, acceptance.CorrelationId));
            Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
            Assert.Equal(Release1MissionState.Accepted, repository.StoredState!.Missions[index].State);
        }
        repository.ResetEvidence();

        using var second = LoadedHarness(repository);
        Assert.Equal(Release1MissionState.Active, second.Service.State!.Missions[index].State);
        Assert.Single(repository.Updates);

        var deferring = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(deferring, TheEnvelopePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionDeferred, "defer-primary")).Accepted);
        deferring.ResetEvidence();

        using var reoffered = LoadedHarness(deferring);
        Assert.Equal(Release1MissionState.Offered, reoffered.Service.State!.Missions[index].State);
        Assert.Equal(2, reoffered.Service.State.Missions[index].Attempt);
        Assert.Single(deferring.Updates);
    }

    [Fact]
    public void WrongAddress_accepted_without_assignment_rejects_with_effect_not_found()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, WrongAddressPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: WrongAddressTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Wrong Address mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.Service.LastLifecycleRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void RoomWithNoName_accepted_without_assignment_rejects_with_effect_not_found()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, RoomWithNoNamePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: RoomWithNoNameTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Room With No Name mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.Service.LastLifecycleRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void ShortNotice_accepted_without_assignment_rejects_with_effect_not_found()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, ShortNoticePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: ShortNoticeTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Short Notice mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.Service.LastLifecycleRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void KeepTheLightsOff_accepted_without_assignment_rejects_with_effect_not_found()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, KeepTheLightsOffPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: KeepTheLightsOffTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Keep the Lights Off mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.Service.LastLifecycleRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void TheEnvelope_accepted_without_assignment_rejects_with_effect_not_found()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, TheEnvelopePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionAccepted,
                "accept-no-assignment", terms: TheEnvelopeTerms)).Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("The Envelope mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, loaded.Service.LastLifecycleRejectReason);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void SmallCourtesy_reconcile_precedes_WrongAddress_reconcile_on_load()
    {
        // Small Courtesy has no missing-assignment rejection (its reconcile
        // returns null there), so this pair pins the order through the
        // observable sequence instead of an EffectNotFound short-circuit:
        // Small Courtesy reoffers first, then Wrong Address hits the
        // prior-mission gate (base behaviour: a reoffer requires the prior
        // mission Satisfied, and Small Courtesy just left Satisfied for its
        // own reoffer), short-circuiting with InvalidTransition.
        var smallCourtesy = Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy);
        var wrongAddress = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, WrongAddressPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.WrongAddress, Release1TransitionKind.MissionDeferred, "defer-order-probe")).Accepted);
        SetMissionState(repository, smallCourtesy, Release1MissionState.Deferred);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
        Assert.Equal("Prior mission is not satisfied.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Offered, loaded.Service.State!.Missions[smallCourtesy].State);
        Assert.Equal(2, loaded.Service.State.Missions[smallCourtesy].Attempt);
        Assert.Equal(Release1MissionState.Deferred, loaded.Service.State.Missions[wrongAddress].State);
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void WrongAddress_rejection_short_circuits_before_RoomWithNoName_reconcile()
    {
        var wrongAddress = Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress);
        var roomWithNoName = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, RoomWithNoNamePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.RoomWithNoName, Release1TransitionKind.MissionDeferred, "defer-order-probe")).Accepted);
        SetMissionState(repository, wrongAddress, Release1MissionState.Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Wrong Address mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Accepted, loaded.Service.State!.Missions[wrongAddress].State);
        Assert.Equal(Release1MissionState.Deferred, loaded.Service.State.Missions[roomWithNoName].State);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void RoomWithNoName_rejection_short_circuits_before_ShortNotice_reconcile()
    {
        var roomWithNoName = Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName);
        var shortNotice = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, ShortNoticePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.ShortNotice, Release1TransitionKind.MissionDeferred, "defer-order-probe")).Accepted);
        SetMissionState(repository, roomWithNoName, Release1MissionState.Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Room With No Name mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Accepted, loaded.Service.State!.Missions[roomWithNoName].State);
        Assert.Equal(Release1MissionState.Deferred, loaded.Service.State.Missions[shortNotice].State);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void ShortNotice_rejection_short_circuits_before_KeepTheLightsOff_reconcile()
    {
        var shortNotice = Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice);
        var keepTheLightsOff = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, KeepTheLightsOffPriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.KeepTheLightsOff, Release1TransitionKind.MissionDeferred, "defer-order-probe")).Accepted);
        SetMissionState(repository, shortNotice, Release1MissionState.Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Short Notice mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Accepted, loaded.Service.State!.Missions[shortNotice].State);
        Assert.Equal(Release1MissionState.Deferred, loaded.Service.State.Missions[keepTheLightsOff].State);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public void KeepTheLightsOff_rejection_short_circuits_before_TheEnvelope_reconcile()
    {
        var keepTheLightsOff = Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff);
        var theEnvelope = Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope);
        var repository = new FakeRepository();
        using (var first = ActiveHarnessWithPriorsSatisfied(repository, TheEnvelopePriors))
            Assert.True(first.Service.TryExecuteDurably(MissionCommand(first,
                Release1MissionCatalog.TheEnvelope, Release1TransitionKind.MissionDeferred, "defer-order-probe")).Accepted);
        SetMissionState(repository, keepTheLightsOff, Release1MissionState.Accepted);
        repository.ResetEvidence();

        var context = new FakeContext();
        using var loaded = new Harness(new Release1StoryRuntimeService(context, repository), context, repository);
        loaded.Service.OnPreLoad();
        var result = loaded.Service.OnLoadComplete();

        Assert.Equal(Release1StoryRuntimeRejectReason.EffectNotFound, result.RejectReason);
        Assert.Equal("Keep the Lights Off mission was Accepted on load but attempt 1 had no assignment on file.", result.Message);
        Assert.Equal(Release1StoryRuntimePhase.Active, loaded.Service.Phase);
        Assert.Equal(Release1MissionState.Accepted, loaded.Service.State!.Missions[keepTheLightsOff].State);
        Assert.Equal(Release1MissionState.Deferred, loaded.Service.State.Missions[theEnvelope].State);
        Assert.Equal(0, repository.UpdateCalls);
    }

    private static void SetMissionState(FakeRepository repository, int missionIndex, Release1MissionState state)
    {
        // Synthetic combos only: each side (Accepted without an assignment,
        // Deferred) is reachable on its own, but the pair cannot arise
        // through the gated transitions. The stored state skips validation
        // on load, so this pins the reconcile order and short-circuit.
        var stored = repository.StoredState!;
        var missions = stored.Missions.ToArray();
        missions[missionIndex] = missions[missionIndex] with { State = state };
        repository.StoredState = stored with { Missions = missions };
    }

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
            "intro-oc-148-reconcile",
            Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-oc-148-reconcile").Value);
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

    private static Harness LoadedHarness(FakeRepository repository)
    {
        var context = new FakeContext();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        var loaded = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.None, loaded.RejectReason);
        return new Harness(service, context, repository);
    }

    private static Release1StoryCommand MissionCommand(
        Harness harness,
        string missionKey,
        Release1TransitionKind kind,
        string receipt,
        string? terms = null,
        double? accepted = null,
        double? deadline = null)
    {
        var mission = harness.Service.State!.Missions[Release1MissionCatalog.IndexOf(missionKey)];
        return new(
            harness.Context.Snapshot.SessionEpoch,
            harness.Context.Snapshot.LoadEpoch,
            harness.Context.Snapshot.PlayerId,
            missionKey,
            mission.Attempt,
            kind,
            receipt,
            Release1LogicalCorrelation.Create(harness.Context.Snapshot.PlayerId, missionKey, mission.Attempt, kind, receipt).Value,
            terms,
            accepted,
            deadline);
    }

    private static Release1WrongAddressAssignment WrongAddressAssignment(int attempt, string authorization) =>
        new(
            Release1MissionCatalog.WrongAddress,
            attempt,
            Release1WrongAddressAssignmentMode.Primary,
            authorization,
            "product",
            "Product",
            "brick",
            "Brick",
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

    private static Release1RoomWithNoNameAssignment RoomWithNoNameAssignment(int attempt, string authorization) =>
        new(
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            Release1RoomWithNoNameAssignmentMode.Primary,
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
            "syndicate-hq",
            9,
            1440d,
            1.5d);

    private static Release1ShortNoticeAssignment ShortNoticeAssignment(int attempt, string authorization) =>
        new(
            Release1MissionCatalog.ShortNotice,
            attempt,
            Release1ShortNoticeAssignmentMode.Primary,
            authorization,
            "product",
            "Product",
            "brick",
            "Brick",
            Release1ShortNoticeAssignment.QuantityFor(Release1ShortNoticeAssignmentMode.Primary),
            $"drop-handoff-{attempt}",
            $"Handoff {attempt}",
            "The right address",
            attempt + 10,
            2,
            3,
            Release1ShortNoticeAssignment.TimedStageGameMinutes,
            Release1ShortNoticeAssignment.ShortNoticeRewardMultiplier,
            Release1ShortNoticeValueConvention.PerUnit);

    private static Release1KeepTheLightsOffAssignment KeepTheLightsOffAssignment(int attempt, string authorization) =>
        new(
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            Release1KeepTheLightsOffAssignmentMode.Primary,
            authorization,
            1,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);

    private static Release1TheEnvelopeAssignment TheEnvelopeAssignment(int attempt, string authorization) =>
        new(
            Release1MissionCatalog.TheEnvelope,
            attempt,
            Release1TheEnvelopeAssignmentMode.Primary,
            authorization,
            Release1TheEnvelopeAssignment.AmountFor(Release1TheEnvelopeAssignmentMode.Primary),
            Release1RoomWithNoNameAssignment.SyndicateHqRoomKey,
            Release1RoomWithNoNameAssignment.SyndicateHqClosetCount,
            Release1TheEnvelopeAssignment.TimedStageGameMinutes);

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
        public int LoadCallCount { get; private set; }
        public int UpdateCalls { get; private set; }
        public int? FailOnUpdateCall { get; set; }

        public Release1StoryStoreLoadResult Load()
        {
            LoadCallCount++;
            return new(
                true,
                StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
                Release1StoryStoreFailureReason.None,
                string.Empty);
        }

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
            LoadCallCount = 0;
            FailOnUpdateCall = null;
        }
    }
}
