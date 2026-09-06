using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1KeepTheLightsOffAssignmentTests
{
    private const string PlayerId = "player-one";

    [Fact]
    public void An_assignment_freezes_the_owned_property_count_read_at_acceptance()
    {
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, 3);

        Assert.Equal(3, assignment.ExpectedOwnedPropertyCount);
        Assert.Equal(1_440d, assignment.WindowDurationGameMinutes);
        Assert.Equal(4_320d, assignment.DeadlineGameMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_owned_property_count_below_one_is_refused(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, 1) with { ExpectedOwnedPropertyCount = count });
    }

    [Fact]
    public void The_window_is_exactly_one_in_game_day_and_nothing_else()
    {
        var assignment = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { WindowDurationGameMinutes = 1_439d });
        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { WindowDurationGameMinutes = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => assignment with { WindowDurationGameMinutes = double.PositiveInfinity });
    }

    [Fact]
    public void Recovery_has_no_deadline_and_a_timed_stage_has_exactly_4320()
    {
        var recovery = Assignment(Release1KeepTheLightsOffAssignmentMode.Recovery, 1, 1);
        var primary = Assignment(Release1KeepTheLightsOffAssignmentMode.Primary, 1, 1);

        Assert.Throws<ArgumentException>(() => recovery with { DeadlineGameMinutes = 4_320d });
        Assert.Throws<ArgumentOutOfRangeException>(() => primary with { DeadlineGameMinutes = 4_319d });
        Assert.Null((recovery with { DeadlineGameMinutes = null }).DeadlineGameMinutes);
    }

    [Theory]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.Primary, Release1TransitionKind.MakeGoodAccepted)]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.MakeGood, Release1TransitionKind.MissionAccepted)]
    [InlineData(Release1KeepTheLightsOffAssignmentMode.Recovery, Release1TransitionKind.MissionAccepted)]
    public void The_authorization_correlation_must_match_the_mission_attempt_and_mode(
        Release1KeepTheLightsOffAssignmentMode mode, Release1TransitionKind wrongKind)
    {
        var wrong = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.KeepTheLightsOff, 1, wrongKind, "r").Value;

        Assert.Throws<ArgumentException>(() => Assignment(mode, 1, 1) with { AuthorizationCorrelationId = wrong });

        var matching = Correlation(mode, 1);
        Assert.Equal(matching, (Assignment(mode, 1, 1) with { AuthorizationCorrelationId = matching }).AuthorizationCorrelationId);
    }

    [Fact]
    public void The_assignment_carries_no_product_field_at_all()
    {
        Assert.Empty(typeof(Release1KeepTheLightsOffAssignment).GetProperties().Where(p =>
            p.Name.Contains("Product") || p.Name.Contains("Price") || p.Name.Contains("Multiplier") ||
            p.Name.Contains("Drop") || p.Name.Contains("Closet")));
    }

    [Fact]
    public void The_selector_freezes_the_property_count_only_when_an_employee_is_assigned()
    {
        var correlation = Correlation(Release1KeepTheLightsOffAssignmentMode.Primary, 1);
        var withEmployee = Activity(TwoPropertiesOneEmployed());

        Assert.True(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            withEmployee, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var assignment, out var status));
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.Selected, status);
        Assert.Equal(2, assignment!.ExpectedOwnedPropertyCount);

        var noEmployee = Activity(TwoPropertiesNoEmployees());
        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            noEmployee, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var noEmployeeAssignment, out var noEmployeeStatus));
        Assert.Null(noEmployeeAssignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.NoAssignedEmployee, noEmployeeStatus);

        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            Release1ProductionActivitySnapshot.Empty, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var emptyAssignment, out var emptyStatus));
        Assert.Null(emptyAssignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.NoOwnedProperty, emptyStatus);

        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            null, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var nullAssignment, out var nullStatus));
        Assert.Null(nullAssignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.InvalidInput, nullStatus);

        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            withEmployee, Release1KeepTheLightsOffAssignmentMode.Primary, 1, "not-a-correlation",
            out var badCorrelationAssignment, out var badCorrelationStatus));
        Assert.Null(badCorrelationAssignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.InvalidInput, badCorrelationStatus);
    }

    [Fact]
    public void The_assigned_employee_precondition_is_skipped_for_a_restore_of_an_existing_attempt()
    {
        // Decision 10's assigned-employee precondition gates a new offer only. Restoring an attempt
        // whose acceptance is already on record (the v9 to v10 migration path) must not be blocked by
        // it, or a save with every employee unassigned could never restore the attempt it already owns.
        var correlation = Correlation(Release1KeepTheLightsOffAssignmentMode.Primary, 1);
        var noEmployee = Activity(TwoPropertiesNoEmployees());

        Assert.True(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            noEmployee, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var restored, out var restoredStatus, requireAssignedEmployee: false));

        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.Selected, restoredStatus);
        Assert.Equal(2, restored!.ExpectedOwnedPropertyCount);

        // The owned-property precondition still applies either way: there is nothing to restore an
        // assignment against when the player owns nothing at all.
        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            Release1ProductionActivitySnapshot.Empty, Release1KeepTheLightsOffAssignmentMode.Primary, 1, correlation,
            out var emptyAssignment, out var emptyStatus, requireAssignedEmployee: false));
        Assert.Null(emptyAssignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.NoOwnedProperty, emptyStatus);
    }

    [Fact]
    public void A_fired_employee_does_not_make_the_offer_eligible()
    {
        var activity = Activity(OnePropertyOneFiredEmployee());

        Assert.False(Release1KeepTheLightsOffAssignmentSelector.TrySelect(
            activity, Release1KeepTheLightsOffAssignmentMode.Primary, 1, Correlation(Release1KeepTheLightsOffAssignmentMode.Primary, 1),
            out var assignment, out var status));

        Assert.Null(assignment);
        Assert.Equal(Release1KeepTheLightsOffSelectionStatus.NoAssignedEmployee, status);
    }

    [Fact]
    public void Progress_rejects_negative_game_minutes_and_a_breach_recorded_before_a_clear_confirmation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, -1d, null).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, 10d, -1d).Validate());
        Assert.Throws<ArgumentException>(() =>
            new Release1KeepTheLightsOffProgress(Release1MissionCatalog.KeepTheLightsOff, 1, null, 5d).Validate());
    }

    [Fact]
    public void A_fresh_progress_row_validates_with_no_clear_or_breach()
    {
        Release1KeepTheLightsOffProgress.Fresh(1).Validate();
    }

    private static Release1ProductionActivitySnapshot Activity(params Release1ProductionPropertySnapshot[] properties) =>
        new(properties);

    private static Release1ProductionPropertySnapshot[] TwoPropertiesOneEmployed() => new[]
    {
        Property("prop-1", "Property One", Employee("emp-1", fired: false)),
        Property("prop-2", "Property Two")
    };

    private static Release1ProductionPropertySnapshot[] TwoPropertiesNoEmployees() => new[]
    {
        Property("prop-1", "Property One"),
        Property("prop-2", "Property Two")
    };

    private static Release1ProductionPropertySnapshot[] OnePropertyOneFiredEmployee() => new[]
    {
        Property("prop-1", "Property One", Employee("emp-1", fired: true))
    };

    private static Release1ProductionPropertySnapshot Property(string code, string name, params Release1ProductionEmployeeSnapshot[] employees) =>
        new(code, name, employees, Array.Empty<Release1ProductionStationSnapshot>());

    private static Release1ProductionEmployeeSnapshot Employee(string id, bool fired) =>
        new(id, "Employee", "Cook", fired, true, false, 0, false, null, null, false);

    private static string Correlation(Release1KeepTheLightsOffAssignmentMode mode, int attempt) =>
        Release1LogicalCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            mode switch
            {
                Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
                Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
                _ => Release1TransitionKind.RecoveryAccepted
            },
            $"keep-the-lights-off-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}").Value;

    private static Release1KeepTheLightsOffAssignment Assignment(Release1KeepTheLightsOffAssignmentMode mode, int attempt, int ownedPropertyCount) =>
        new(Release1MissionCatalog.KeepTheLightsOff, attempt, mode, Correlation(mode, attempt),
            ownedPropertyCount,
            Release1KeepTheLightsOffAssignment.WindowGameMinutes,
            mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : Release1KeepTheLightsOffAssignment.TimedStageGameMinutes);
}
