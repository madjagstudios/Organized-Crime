using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcPatrolBindingTests
{
    private static readonly OcOfficerCloneRef Clone = new(new object(), 10, 1);
    private static OcPatrolRoute Route(string name, params OcOfficerPosition[] points) =>
        new(name, new object(), points);

    [Fact]
    public void Selection_uses_closest_waypoint_not_route_origin_or_input_order()
    {
        var near = Route("near", new OcOfficerPosition(100, 0, 100), new OcOfficerPosition(1, 0, 0));
        var far = Route("far", new OcOfficerPosition(5, 0, 0));
        Assert.Same(near, OcPatrolRouteSelector.Closest(new[] { far, near }, default));
    }

    [Fact]
    public void Equal_distances_use_ordinal_route_key()
    {
        var a = Route("A", new OcOfficerPosition(1, 0, 0));
        var z = Route("Z", new OcOfficerPosition(-1, 0, 0));
        Assert.Same(a, OcPatrolRouteSelector.Closest(new[] { z, a }, default));
    }

    [Fact]
    public void Empty_or_nonfinite_routes_and_targets_fail_closed()
    {
        var routes = new[] { Route("empty"), Route("bad", new OcOfficerPosition(float.NaN, 0, 0)) };
        Assert.Null(OcPatrolRouteSelector.Closest(routes, default));
        Assert.Null(OcPatrolRouteSelector.Closest(new[] { Route("ok", default(OcOfficerPosition)) }, new(float.PositiveInfinity, 0, 0)));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void One_free_native_car_is_reserved_for_forced_wanted(int free, bool allowed) =>
        Assert.Equal(allowed, OcPatrolCarReservation.CanLease(free));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Rebind_same_clone_and_route_is_idempotent(int dutyValue)
    {
        var duty = (OcPatrolDuty)dutyValue;
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryBind(Clone, duty, default, out _));
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryBind(Clone, duty, default, out _));
        Assert.Equal(1, native.Starts);
        Assert.Equal(duty == OcPatrolDuty.Vehicle ? 1 : 0, native.Leases);
        Assert.Single(binding.Snapshot());
    }

    [Fact]
    public void Vehicle_unbind_stops_before_return_and_unbind_twice_returns_once()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        Assert.True(binding.Snapshot()[0].CarLeased);
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryUnbind(Clone, out _));
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryUnbind(Clone, out _));
        Assert.Equal(new[] { "start", "stop", "return" }, native.Events);
        Assert.Empty(binding.Snapshot());
    }

    [Fact]
    public void Rediscovered_route_wrapper_with_same_native_key_does_not_restart()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _);
        native.Routes = new[] { Route("route", new OcOfficerPosition(0, 0, 0)) };
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _));
        Assert.Equal(1, native.Starts);
        Assert.Equal(new[] { "start" }, native.Events);
    }

    [Fact]
    public void No_free_car_does_not_start_or_leave_binding()
    {
        var native = new Native { FreeCars = 1 };
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.NotReady, binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _));
        Assert.Equal(0, native.Starts);
        Assert.Equal(0, native.Leases);
        Assert.Empty(binding.Snapshot());
    }

    [Fact]
    public void Two_clones_cannot_consume_the_reserved_car()
    {
        var native = new Native { FreeCars = 2 };
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _));
        Assert.Equal(OcOfficerWorldStatus.NotReady, binding.TryBind(new(new object(), 11, 2), OcPatrolDuty.Vehicle, default, out _));
        Assert.Equal(1, native.FreeCars);
    }

    [Fact]
    public void Start_failure_rolls_back_the_car_and_group()
    {
        foreach (var duty in new[] { OcPatrolDuty.Foot, OcPatrolDuty.Vehicle })
        {
            var native = new Native { FailStart = true };
            var binding = new OcPatrolBindings(native);
            Assert.Equal(OcOfficerWorldStatus.Failed, binding.TryBind(Clone, duty, default, out _));
            Assert.Equal(duty == OcPatrolDuty.Vehicle ? new[] { "start", "stop", "return" } : new[] { "start", "stop" }, native.Events);
            Assert.Empty(binding.Snapshot());
        }
    }

    [Fact]
    public void Failed_stop_keeps_lease_and_does_not_return_until_cleanup_succeeds()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.FailStop = true;
        Assert.Equal(OcOfficerWorldStatus.Failed, binding.TryUnbind(Clone, out _));
        Assert.True(binding.Snapshot()[0].CarLeased);
        Assert.DoesNotContain("return", native.Events);
        native.FailStop = false;
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryUnbind(Clone, out _));
        Assert.Single(native.Events, x => x == "return");
    }

    [Fact]
    public void Failed_return_keeps_bookkeeping_but_does_not_stop_again()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.FailReturn = true;
        Assert.Equal(OcOfficerWorldStatus.Failed, binding.TryUnbind(Clone, out _));
        Assert.Equal("cleanup pending", binding.Snapshot()[0].State);
        native.FailReturn = false;
        Assert.Equal(OcOfficerWorldStatus.Ok, binding.TryUnbind(Clone, out _));
        Assert.Single(native.Events, x => x == "stop");
        Assert.Empty(binding.Snapshot());
    }

    [Fact]
    public void Changing_duty_cleans_previous_binding_before_starting_new_duty()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _);
        Assert.Equal(new[] { "start", "stop", "return", "start" }, native.Events);
        Assert.False(binding.Snapshot()[0].CarLeased);
    }

    [Fact]
    public void Client_cannot_bind_or_unbind_resources()
    {
        var native = new Native { IsHost = false };
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.NotHost, binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _));
        Assert.Equal(OcOfficerWorldStatus.NotHost, binding.TryUnbind(Clone, out _));
        Assert.Empty(native.Events);
    }

    [Fact]
    public void No_route_fails_before_allocating_group_or_car()
    {
        var native = new Native { Routes = Array.Empty<OcPatrolRoute>() };
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.NotReady, binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _));
        Assert.Empty(native.Events);
    }

    [Fact]
    public void Route_read_failure_preserves_existing_binding_and_lease()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.FailReadRoutes = true;
        Assert.Equal(OcOfficerWorldStatus.Failed, binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _));
        Assert.True(binding.Snapshot()[0].CarLeased);
        Assert.Equal("bound", binding.Snapshot()[0].State);
        Assert.Equal(new[] { "start" }, native.Events);
    }

    [Fact]
    public void Rebind_cannot_acquire_a_second_car_while_old_cleanup_is_pending()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _);
        native.Routes = new[] { Route("new", new OcOfficerPosition(1, 0, 0)) };
        native.FailReturn = true;
        Assert.Equal(OcOfficerWorldStatus.Failed, binding.TryBind(Clone, OcPatrolDuty.Vehicle, default, out _));
        Assert.Equal(1, native.Leases);
        Assert.Equal(1, native.Starts);
        Assert.True(binding.Snapshot()[0].CarLeased);
    }

    [Fact]
    public void Unbind_only_releases_that_clone_and_leaves_other_duty_running()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        var second = new OcOfficerCloneRef(new object(), 11, 2);
        binding.TryBind(Clone, OcPatrolDuty.Foot, default, out _);
        binding.TryBind(second, OcPatrolDuty.Vehicle, default, out _);
        binding.TryUnbind(Clone, out _);
        var remaining = Assert.Single(binding.Snapshot());
        Assert.Equal(second, remaining.Officer);
        Assert.True(remaining.CarLeased);
        Assert.DoesNotContain("return", native.Events);
    }

    [Fact]
    public void Invalid_handle_and_nonfinite_target_allocate_nothing()
    {
        var native = new Native();
        var binding = new OcPatrolBindings(native);
        Assert.Equal(OcOfficerWorldStatus.InvalidHandle, binding.TryBind(default, OcPatrolDuty.Vehicle, default, out _));
        Assert.Equal(OcOfficerWorldStatus.Rejected, binding.TryBind(Clone, OcPatrolDuty.Vehicle, new(float.NaN, 0, 0), out _));
        Assert.Empty(binding.Snapshot());
        Assert.Empty(native.Events);
    }

    internal sealed class Native : IOcPatrolNative
    {
        public bool IsHost { get; set; } = true;
        public int FreeCars = 3;
        public int Starts;
        public int Leases;
        public bool FailStart;
        public bool FailStop;
        public bool FailReturn;
        public bool FailReadRoutes;
        public List<string> Events { get; } = new();
        public IReadOnlyList<OcPatrolRoute> Routes = new[] { Route("route", new OcOfficerPosition(0, 0, 0)) };
        public IReadOnlyList<OcPatrolRoute> ReadRoutes(OcPatrolDuty duty) => FailReadRoutes ? throw new InvalidOperationException() : Routes;
        public object CreateFootGroup(OcPatrolRoute route) => new object();
        public object? LeaseVehicle(OcOfficerPosition target)
        {
            if (!OcPatrolCarReservation.CanLease(FreeCars)) return null;
            FreeCars--; Leases++; return new object();
        }
        public void StartFoot(OcOfficerCloneRef clone, object group) => Start();
        public void StartVehicle(OcOfficerCloneRef clone, OcPatrolRoute route, object car) => Start();
        private void Start()
        {
            Starts++; Events.Add("start");
            if (FailStart) throw new InvalidOperationException();
        }
        public void Stop(OcOfficerCloneRef clone, OcPatrolDuty duty, object? group, object? car)
        {
            Events.Add("stop");
            if (FailStop) throw new InvalidOperationException();
        }
        public void ReturnVehicle(object car)
        {
            if (FailReturn) throw new InvalidOperationException();
            Events.Add("return"); FreeCars++;
        }
    }
}
