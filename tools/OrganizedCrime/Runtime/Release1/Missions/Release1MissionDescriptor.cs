using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.Missions;

// OC-153: the per-mission facts the shared command handler is driven by. Every
// difference the normalized six-way diff found between the TryExecute*,
// ReconcileAccepted* and Reconcile*OnLoad clones lives here as data or as a
// static (capture-free) callback, so the handler itself never branches on the
// mission identity.
internal sealed record Release1MissionDescriptor<TAssignment, TMode>(
    // The Release1MissionCatalog key, e.g. "release1.small-courtesy".
    string MissionKey,
    // The display name used in command messages, e.g. "Small Courtesy".
    string DisplayName,
    // The canonical primary terms version, e.g. "small-courtesy-v1".
    string TermsVersion,
    // Receipt prefix for the activation command, e.g. "small-courtesy-activate-v1-a".
    string ActivateReceiptPrefix,
    // Receipt prefix for the reoffer command, e.g. "small-courtesy-reoffer-v1-a".
    string ReofferReceiptPrefix,
    Func<Release1StoryState, IReadOnlyList<TAssignment>> GetAssignments,
    Func<Release1StoryState, TAssignment, Release1StoryState> WithAssignmentAppended,
    Action<TAssignment> ValidateAssignment,
    Func<TAssignment, string> GetMissionKey,
    Func<TAssignment, int> GetAttempt,
    Func<TAssignment, TMode> GetMode,
    Func<TAssignment, string> GetAuthorizationCorrelationId,
    // The verbatim per-mission mode switch: Primary to MissionAccepted,
    // MakeGood to MakeGoodAccepted, Recovery to RecoveryAccepted, else null.
    Func<TMode, Release1TransitionKind?> MapModeToTransition,
    Func<TMode, bool> IsPrimaryMode,
    // Concrete record equality: (left, right) => left == right.
    Func<TAssignment, TAssignment, bool> AssignmentsEqual,
    // The missing-assignment load message for the attempt, or null for the
    // lenient mission (Small Courtesy returns null there instead of rejecting).
    Func<int, string?> MissingAssignmentMessage);

// OC-153: the per-mission facts the shared whole-record progress setter is
// driven by. Only the four missions with a whole-record setter have one;
// Small Courtesy has no progress methods and Wrong Address keeps its
// staged/custody pair verbatim on the service.
internal sealed record Release1MissionProgressDescriptor<TProgress>(
    // The display name used in command messages, e.g. "Short Notice".
    string DisplayName,
    Func<Release1StoryState, IReadOnlyList<TProgress>> GetProgress,
    Func<Release1StoryState, IReadOnlyList<TProgress>, Release1StoryState> WithProgressList,
    Func<Release1StoryState, TProgress, bool> HasAssignmentForAttempt,
    Action<TProgress> ValidateProgress,
    Func<TProgress, int> GetAttempt,
    // The verbatim per-mission monotonicity rule: the rejection message when
    // next regresses from existing within the attempt, else null.
    Func<TProgress, TProgress, string?> CheckRegression,
    // Concrete record equality: (left, right) => left == right.
    Func<TProgress, TProgress, bool> ProgressEqual);

// OC-153: the six mission descriptors plus the four progress descriptors, as
// static readonly data with capture-free lambdas. Values are copied verbatim
// from the service bodies this table replaces.
internal static class Release1MissionDescriptors
{
    internal static readonly Release1MissionDescriptor<Release1SmallCourtesyAssignment, Release1SmallCourtesyAssignmentMode> SmallCourtesy = new(
        Release1MissionCatalog.SmallCourtesy,
        "Small Courtesy",
        "small-courtesy-v1",
        "small-courtesy-activate-v1-a",
        "small-courtesy-reoffer-v1-a",
        static state => state.SmallCourtesyAssignments,
        static (state, assignment) => state with { SmallCourtesyAssignments = state.SmallCourtesyAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1SmallCourtesyAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1SmallCourtesyAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1SmallCourtesyAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1SmallCourtesyAssignmentMode.Primary,
        static (left, right) => left == right,
        static _ => (string?)null);

    internal static readonly Release1MissionDescriptor<Release1WrongAddressAssignment, Release1WrongAddressAssignmentMode> WrongAddress = new(
        Release1MissionCatalog.WrongAddress,
        "Wrong Address",
        "wrong-address-v1",
        "wrong-address-activate-v1-a",
        "wrong-address-reoffer-v1-a",
        static state => state.WrongAddressAssignments,
        static (state, assignment) => state with { WrongAddressAssignments = state.WrongAddressAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1WrongAddressAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1WrongAddressAssignmentMode.Primary,
        static (left, right) => left == right,
        static attempt => $"Wrong Address mission was Accepted on load but attempt {attempt} had no assignment on file.");

    internal static readonly Release1MissionDescriptor<Release1RoomWithNoNameAssignment, Release1RoomWithNoNameAssignmentMode> RoomWithNoName = new(
        Release1MissionCatalog.RoomWithNoName,
        "Room With No Name",
        "room-with-no-name-v1",
        "room-with-no-name-activate-v1-a",
        "room-with-no-name-reoffer-v1-a",
        static state => state.RoomWithNoNameAssignments,
        static (state, assignment) => state with { RoomWithNoNameAssignments = state.RoomWithNoNameAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1RoomWithNoNameAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1RoomWithNoNameAssignmentMode.Primary,
        static (left, right) => left == right,
        static attempt => $"Room With No Name mission was Accepted on load but attempt {attempt} had no assignment on file.");

    internal static readonly Release1MissionDescriptor<Release1ShortNoticeAssignment, Release1ShortNoticeAssignmentMode> ShortNotice = new(
        Release1MissionCatalog.ShortNotice,
        "Short Notice",
        "short-notice-v1",
        "short-notice-activate-v1-a",
        "short-notice-reoffer-v1-a",
        static state => state.ShortNoticeAssignments,
        static (state, assignment) => state with { ShortNoticeAssignments = state.ShortNoticeAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1ShortNoticeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1ShortNoticeAssignmentMode.Primary,
        static (left, right) => left == right,
        static attempt => $"Short Notice mission was Accepted on load but attempt {attempt} had no assignment on file.");

    internal static readonly Release1MissionDescriptor<Release1KeepTheLightsOffAssignment, Release1KeepTheLightsOffAssignmentMode> KeepTheLightsOff = new(
        Release1MissionCatalog.KeepTheLightsOff,
        "Keep the Lights Off",
        "keep-the-lights-off-v1",
        "keep-the-lights-off-activate-v1-a",
        "keep-the-lights-off-reoffer-v1-a",
        static state => state.KeepTheLightsOffAssignments,
        static (state, assignment) => state with { KeepTheLightsOffAssignments = state.KeepTheLightsOffAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1KeepTheLightsOffAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1KeepTheLightsOffAssignmentMode.Primary,
        static (left, right) => left == right,
        static attempt => $"Keep the Lights Off mission was Accepted on load but attempt {attempt} had no assignment on file.");

    internal static readonly Release1MissionDescriptor<Release1TheEnvelopeAssignment, Release1TheEnvelopeAssignmentMode> TheEnvelope = new(
        Release1MissionCatalog.TheEnvelope,
        "The Envelope",
        "the-envelope-v1",
        "the-envelope-activate-v1-a",
        "the-envelope-reoffer-v1-a",
        static state => state.TheEnvelopeAssignments,
        static (state, assignment) => state with { TheEnvelopeAssignments = state.TheEnvelopeAssignments.Append(assignment).ToArray() },
        static assignment => assignment.Validate(),
        static assignment => assignment.MissionKey,
        static assignment => assignment.Attempt,
        static assignment => assignment.Mode,
        static assignment => assignment.AuthorizationCorrelationId,
        static mode => mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1TheEnvelopeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => (Release1TransitionKind?)null
        },
        static mode => mode == Release1TheEnvelopeAssignmentMode.Primary,
        static (left, right) => left == right,
        static attempt => $"The Envelope mission was Accepted on load but attempt {attempt} had no assignment on file.");

    internal static readonly Release1MissionProgressDescriptor<Release1RoomWithNoNameProgress> RoomWithNoNameProgress = new(
        "Room With No Name",
        static state => state.RoomWithNoNameProgress,
        static (state, progress) => state with { RoomWithNoNameProgress = progress, Revision = state.Revision + 1 },
        static (state, next) => state.RoomWithNoNameAssignments.Any(assignment => assignment.Attempt == next.Attempt),
        static next => next.Validate(),
        static progress => progress.Attempt,
        static (existing, next) => (existing.Staged && !next.Staged) || (existing.Custody && !next.Custody) ||
            (existing.Stowed && !next.Stowed) || (existing.HoldSatisfied && !next.HoldSatisfied)
            ? "Room With No Name progress flags cannot regress within an attempt."
            : null,
        static (left, right) => left == right);

    internal static readonly Release1MissionProgressDescriptor<Release1ShortNoticeProgress> ShortNoticeProgress = new(
        "Short Notice",
        static state => state.ShortNoticeProgress,
        static (state, progress) => state with { ShortNoticeProgress = progress, Revision = state.Revision + 1 },
        static (state, next) => state.ShortNoticeAssignments.Any(assignment => assignment.Attempt == next.Attempt),
        static next => next.Validate(),
        static progress => progress.Attempt,
        static (existing, next) => existing.SpreadNoticed && !next.SpreadNoticed
            ? "Short Notice spread notice cannot be withdrawn within an attempt."
            : null,
        static (left, right) => left == right);

    internal static readonly Release1MissionProgressDescriptor<Release1KeepTheLightsOffProgress> KeepTheLightsOffProgress = new(
        "Keep the Lights Off",
        static state => state.KeepTheLightsOffProgress,
        static (state, progress) => state with { KeepTheLightsOffProgress = progress, Revision = state.Revision + 1 },
        static (state, next) => state.KeepTheLightsOffAssignments.Any(assignment => assignment.Attempt == next.Attempt),
        static next => next.Validate(),
        static progress => progress.Attempt,
        static (existing, next) => existing.ClearConfirmedAtGameMinutes is not null && next.ClearConfirmedAtGameMinutes is null
            ? "Keep the Lights Off clear confirmation cannot be withdrawn within an attempt."
            : null,
        static (left, right) => left == right);

    internal static readonly Release1MissionProgressDescriptor<Release1TheEnvelopeProgress> TheEnvelopeProgress = new(
        "The Envelope",
        static state => state.TheEnvelopeProgress,
        static (state, progress) => state with { TheEnvelopeProgress = progress, Revision = state.Revision + 1 },
        static (state, next) => state.TheEnvelopeAssignments.Any(assignment => assignment.Attempt == next.Attempt),
        static next => next.Validate(),
        static progress => progress.Attempt,
        static (existing, next) => existing.SpreadNoticed && !next.SpreadNoticed
            ? "The Envelope spread notice cannot be withdrawn within an attempt."
            : null,
        static (left, right) => left == right);
}
