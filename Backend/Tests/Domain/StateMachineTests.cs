using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.StateMachines;

namespace CommonService.Tests.Domain;

/// <summary>
/// The expected transitions are written out here a second time on purpose: the test must fail when someone edits
/// the machine without editing (and reviewing) this list.
/// </summary>
public class StateMachineTests
{
    private static readonly HashSet<(JobOrderStatus, JobOrderStatus)> OrderAllowed =
    [
        (JobOrderStatus.PendingPayment, JobOrderStatus.Paid),
        (JobOrderStatus.PendingPayment, JobOrderStatus.Cancelled),
        (JobOrderStatus.Paid, JobOrderStatus.Dispatching),
        (JobOrderStatus.Paid, JobOrderStatus.Cancelled),
        (JobOrderStatus.Dispatching, JobOrderStatus.Assigned),
        (JobOrderStatus.Dispatching, JobOrderStatus.Cancelled),
        (JobOrderStatus.Assigned, JobOrderStatus.Dispatching),
        (JobOrderStatus.Assigned, JobOrderStatus.Completed),
        (JobOrderStatus.Assigned, JobOrderStatus.Cancelled),
    ];

    private static readonly HashSet<(JobAssignmentStatus, JobAssignmentStatus)> AssignmentAllowed =
    [
        (JobAssignmentStatus.Offered, JobAssignmentStatus.Assigned),
        (JobAssignmentStatus.Offered, JobAssignmentStatus.Cancelled),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Cancelled),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.CancelledByWorker),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Reassigned),
        (JobAssignmentStatus.Assigned, JobAssignmentStatus.Incident),
        (JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress),
        (JobAssignmentStatus.CheckedIn, JobAssignmentStatus.Absent),
        (JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance),
        (JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.InProgress),
        (JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed),
        (JobAssignmentStatus.Incident, JobAssignmentStatus.Reassigned),
        (JobAssignmentStatus.Incident, JobAssignmentStatus.Cancelled),
    ];

    private static readonly HashSet<(WorkStatus, WorkStatus)> WorkerAllowed =
    [
        (WorkStatus.Pending, WorkStatus.Idle),
        (WorkStatus.Pending, WorkStatus.Locked),
        (WorkStatus.Idle, WorkStatus.Busy),
        (WorkStatus.Idle, WorkStatus.Locked),
        (WorkStatus.Busy, WorkStatus.Idle),
        (WorkStatus.Busy, WorkStatus.Locked),
        (WorkStatus.Locked, WorkStatus.Idle),
    ];

    private static IEnumerable<object[]> Pairs<T>() where T : struct, Enum =>
        from a in Enum.GetValues<T>() from b in Enum.GetValues<T>() select new object[] { a, b };

    public static IEnumerable<object[]> OrderPairs() => Pairs<JobOrderStatus>();
    public static IEnumerable<object[]> AssignmentPairs() => Pairs<JobAssignmentStatus>();
    public static IEnumerable<object[]> WorkerPairs() => Pairs<WorkStatus>();

    [Theory]
    [MemberData(nameof(OrderPairs))]
    public void JobOrder_machine_allows_exactly_the_listed_transitions(JobOrderStatus from, JobOrderStatus to) =>
        Assert.Equal(OrderAllowed.Contains((from, to)), JobOrderStateMachine.Instance.CanTransition(from, to));

    [Theory]
    [MemberData(nameof(AssignmentPairs))]
    public void JobAssignment_machine_allows_exactly_the_listed_transitions(JobAssignmentStatus from, JobAssignmentStatus to) =>
        Assert.Equal(AssignmentAllowed.Contains((from, to)), JobAssignmentStateMachine.Instance.CanTransition(from, to));

    [Theory]
    [MemberData(nameof(WorkerPairs))]
    public void Worker_machine_allows_exactly_the_listed_transitions(WorkStatus from, WorkStatus to) =>
        Assert.Equal(WorkerAllowed.Contains((from, to)), WorkerStateMachine.Instance.CanTransition(from, to));

    [Theory]
    [MemberData(nameof(OrderPairs))]
    public void JobOrder_entity_follows_the_machine(JobOrderStatus from, JobOrderStatus to)
    {
        var order = OrderIn(from);

        if (OrderAllowed.Contains((from, to)))
        {
            order.TransitionTo(to);
            Assert.Equal(to, order.OrderStatus);
        }
        else
        {
            Assert.Throws<InvalidStateTransitionException>(() => order.TransitionTo(to));
            Assert.Equal(from, order.OrderStatus);
        }
    }

    [Theory]
    [MemberData(nameof(AssignmentPairs))]
    public void JobAssignment_entity_follows_the_machine(JobAssignmentStatus from, JobAssignmentStatus to)
    {
        var assignment = AssignmentIn(from);

        if (AssignmentAllowed.Contains((from, to)))
        {
            assignment.TransitionTo(to);
            Assert.Equal(to, assignment.AssignmentStatus);
        }
        else
        {
            Assert.Throws<InvalidStateTransitionException>(() => assignment.TransitionTo(to));
            Assert.Equal(from, assignment.AssignmentStatus);
        }
    }

    [Theory]
    [MemberData(nameof(WorkerPairs))]
    public void Worker_entity_follows_the_machine(WorkStatus from, WorkStatus to)
    {
        var worker = WorkerIn(from);

        if (WorkerAllowed.Contains((from, to)))
        {
            worker.TransitionTo(to);
            Assert.Equal(to, worker.WorkStatus);
        }
        else
        {
            Assert.Throws<InvalidStateTransitionException>(() => worker.TransitionTo(to));
            Assert.Equal(from, worker.WorkStatus);
        }
    }

    [Fact]
    public void Terminal_states_have_no_way_out()
    {
        Assert.All(
            [JobOrderStatus.Completed, JobOrderStatus.Cancelled],
            s => Assert.True(JobOrderStateMachine.Instance.IsTerminal(s)));
        Assert.All(
            [JobAssignmentStatus.Completed, JobAssignmentStatus.Cancelled, JobAssignmentStatus.CancelledByWorker, JobAssignmentStatus.Absent, JobAssignmentStatus.Reassigned],
            s => Assert.True(JobAssignmentStateMachine.Instance.IsTerminal(s)));
    }

    [Fact]
    public void New_entities_start_in_the_first_enum_member()
    {
        Assert.Equal(JobOrderStatus.PendingPayment, new JobOrder().OrderStatus);
        Assert.Equal(JobAssignmentStatus.Offered, new JobAssignment().AssignmentStatus);
        Assert.Equal(WorkStatus.Pending, new Worker().WorkStatus);
    }

    [Fact]
    public void Exception_reports_machine_and_both_states()
    {
        var ex = Assert.Throws<InvalidStateTransitionException>(
            () => JobOrderStateMachine.Instance.Require(JobOrderStatus.Completed, JobOrderStatus.Paid));

        Assert.Equal("JobOrder", ex.Machine);
        Assert.Equal("Completed", ex.From);
        Assert.Equal("Paid", ex.To);
    }

    // Walk the legal path from the initial state to the wanted state (breadth first), so every state is reachable in a test.
    private static JobOrder OrderIn(JobOrderStatus target)
    {
        var order = new JobOrder();
        foreach (var step in Path(JobOrderStateMachine.Instance, JobOrderStatus.PendingPayment, target)) order.TransitionTo(step);
        return order;
    }

    private static JobAssignment AssignmentIn(JobAssignmentStatus target)
    {
        var a = new JobAssignment();
        foreach (var step in Path(JobAssignmentStateMachine.Instance, JobAssignmentStatus.Offered, target)) a.TransitionTo(step);
        return a;
    }

    private static Worker WorkerIn(WorkStatus target)
    {
        var w = new Worker();
        foreach (var step in Path(WorkerStateMachine.Instance, WorkStatus.Pending, target)) w.TransitionTo(step);
        return w;
    }

    private static List<T> Path<T>(StateMachine<T> machine, T start, T target) where T : struct, Enum
    {
        var previous = new Dictionary<T, T>();
        var queue = new Queue<T>([start]);
        var seen = new HashSet<T> { start };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in machine.NextStates(current).Where(seen.Add))
            {
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        var path = new List<T>();
        for (var s = target; !EqualityComparer<T>.Default.Equals(s, start); s = previous[s]) path.Insert(0, s);
        return path;
    }
}
