using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using static CommonService.Domain.Enums.JobAssignmentStatus;

namespace CommonService.Tests.Domain;

public class JobOrderProgressTests
{
    private static JobOrder Order(byte requiredWorkers, JobOrderStatus status)
    {
        var order = new JobOrder { RequiredWorkers = requiredWorkers };
        var path = status switch
        {
            JobOrderStatus.Dispatching => new[] { JobOrderStatus.Paid, JobOrderStatus.Dispatching },
            JobOrderStatus.Assigned => [JobOrderStatus.Paid, JobOrderStatus.Dispatching, JobOrderStatus.Assigned],
            JobOrderStatus.Paid => [JobOrderStatus.Paid],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        foreach (var s in path) order.TransitionTo(s);
        return order;
    }

    [Fact]
    public void One_worker_accepted_assigns_a_one_worker_order()
    {
        var order = Order(1, JobOrderStatus.Dispatching);

        Assert.True(order.SyncWithAssignments([Cancelled, Assigned]));
        Assert.Equal(JobOrderStatus.Assigned, order.OrderStatus);
    }

    [Fact]
    public void Two_worker_order_stays_dispatching_until_both_seats_are_taken()
    {
        var order = Order(2, JobOrderStatus.Dispatching);

        Assert.False(order.SyncWithAssignments([Assigned, Offered]));
        Assert.Equal(JobOrderStatus.Dispatching, order.OrderStatus);

        Assert.True(order.SyncWithAssignments([Assigned, Assigned]));
        Assert.Equal(JobOrderStatus.Assigned, order.OrderStatus);
    }

    [Theory]
    [InlineData(CancelledByWorker)]
    [InlineData(Incident)]
    [InlineData(Reassigned)]
    public void Losing_one_worker_sends_the_order_back_to_dispatching(JobAssignmentStatus lost)
    {
        var order = Order(2, JobOrderStatus.Assigned);

        Assert.True(order.SyncWithAssignments([CheckedIn, lost]));
        Assert.Equal(JobOrderStatus.Dispatching, order.OrderStatus);
    }

    [Fact]
    public void Order_completes_only_when_every_required_assignment_is_completed()
    {
        var order = Order(2, JobOrderStatus.Assigned);

        Assert.False(order.SyncWithAssignments([Completed, InProgress]));
        Assert.Equal(JobOrderStatus.Assigned, order.OrderStatus);

        Assert.True(order.SyncWithAssignments([Completed, Reassigned, Completed]));
        Assert.Equal(JobOrderStatus.Completed, order.OrderStatus);
    }

    [Fact]
    public void Fallback_same_worker_two_consecutive_shifts_counts_as_two_assignments()
    {
        var order = Order(2, JobOrderStatus.Assigned);

        Assert.True(order.SyncWithAssignments([Completed, Completed]));
        Assert.Equal(JobOrderStatus.Completed, order.OrderStatus);
    }

    [Fact]
    public void Absent_assignment_is_left_to_the_admin_approval_flow()
    {
        var order = Order(1, JobOrderStatus.Assigned);

        Assert.False(order.SyncWithAssignments([Absent]));
        Assert.Equal(JobOrderStatus.Assigned, order.OrderStatus);
    }

    [Fact]
    public void Orders_outside_dispatching_or_assigned_are_not_touched()
    {
        var order = Order(1, JobOrderStatus.Paid);

        Assert.False(order.SyncWithAssignments([Assigned]));
        Assert.Equal(JobOrderStatus.Paid, order.OrderStatus);
    }
}
