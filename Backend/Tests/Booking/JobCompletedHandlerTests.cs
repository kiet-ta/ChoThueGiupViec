using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BOOKING-COMPLETE-01: Booking completes the order when its workers have completed (contract booking.md section 5, decision Q22 D7).</summary>
public sealed class JobCompletedHandlerTests
{
    private const long OrderId = 42;
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private sealed class InMemoryOrders : IOrderRepository, IUnitOfWork
    {
        public Dictionary<long, JobOrder> Orders { get; } = [];
        public Dictionary<long, List<JobAssignmentStatus>> Assignments { get; } = [];
        public int Transactions { get; private set; }

        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<IReadOnlyList<JobAssignmentStatus>> GetAssignmentStatusesAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobAssignmentStatus>>(Assignments.GetValueOrDefault(orderId) ?? []);

        public void Add(JobOrder order) => throw new NotSupportedException();
        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            Transactions++;
            return action();
        }
    }

    private static JobOrder AssignedOrder(int requiredWorkers = 1)
    {
        var order = new JobOrder
        {
            OrderId = OrderId,
            OrderCode = "GV261021ABC123",
            CustomerId = 7,
            AddressId = 3,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2026, 10, 21),
            ShiftCode = "SHIFT_MORNING",
            AreaSnapshotM2 = 50m,
            RequiredWorkers = (byte)requiredWorkers,
            TotalAmount = 260000m,
            CreatedAt = Now.AddHours(-5),
            UpdatedAt = Now.AddHours(-5),
        };
        order.TransitionTo(JobOrderStatus.Paid);
        order.TransitionTo(JobOrderStatus.Dispatching);
        order.TransitionTo(JobOrderStatus.Assigned);
        return order;
    }

    private static JobCompleted Event(long assignmentId = 1, long orderId = OrderId) =>
        new(assignmentId, orderId, WorkerId: 5, AgencyId: null, PayoutAmount: 208000m, CompletedAtUtc: Now);

    private static (JobCompletedHandler Handler, InMemoryOrders Db) Build(JobOrder? order, params JobAssignmentStatus[] assignments)
    {
        var db = new InMemoryOrders();
        if (order is not null)
        {
            db.Orders[order.OrderId] = order;
            db.Assignments[order.OrderId] = [.. assignments];
        }

        return (new JobCompletedHandler(db, db, new TestClock(), NullLogger<JobCompletedHandler>.Instance), db);
    }

    [Fact]
    public async Task TheOnlyWorkerCompleting_CompletesTheOrder()
    {
        var order = AssignedOrder();
        var (handler, _) = Build(order, JobAssignmentStatus.Completed);

        await handler.Handle(Event(), CancellationToken.None);

        Assert.Equal(JobOrderStatus.Completed, order.OrderStatus);
        Assert.Equal(Now, order.UpdatedAt);
    }

    [Fact]
    public async Task WithTwoWorkers_TheOrderStaysAssignedUntilTheSecondOneCompletes()
    {
        var order = AssignedOrder(requiredWorkers: 2);
        var (handler, db) = Build(order, JobAssignmentStatus.Completed, JobAssignmentStatus.InProgress);

        await handler.Handle(Event(1), CancellationToken.None);
        Assert.Equal(JobOrderStatus.Assigned, order.OrderStatus);

        db.Assignments[OrderId] = [JobAssignmentStatus.Completed, JobAssignmentStatus.Completed];
        await handler.Handle(Event(2), CancellationToken.None);
        Assert.Equal(JobOrderStatus.Completed, order.OrderStatus);
    }

    [Fact]
    public async Task ARepeatedEvent_ChangesNothingAndDoesNotThrow()
    {
        var order = AssignedOrder();
        var (handler, _) = Build(order, JobAssignmentStatus.Completed);

        await handler.Handle(Event(), CancellationToken.None);
        await handler.Handle(Event(), CancellationToken.None);

        Assert.Equal(JobOrderStatus.Completed, order.OrderStatus);
    }

    [Fact]
    public async Task AnUnknownOrder_IsIgnored()
    {
        var (handler, db) = Build(order: null);

        await handler.Handle(Event(orderId: 999), CancellationToken.None);

        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task ACancelledOrder_IsNotTouched()
    {
        var order = AssignedOrder();
        order.TransitionTo(JobOrderStatus.Cancelled);
        var (handler, _) = Build(order, JobAssignmentStatus.Completed);

        await handler.Handle(Event(), CancellationToken.None);

        Assert.Equal(JobOrderStatus.Cancelled, order.OrderStatus);
    }

    [Fact]
    public async Task AnOrderThatIsNotAssigned_IsNotTouched()
    {
        var order = AssignedOrder();
        order.TransitionTo(JobOrderStatus.Dispatching);
        var (handler, _) = Build(order, JobAssignmentStatus.Completed);

        await handler.Handle(Event(), CancellationToken.None);

        Assert.Equal(JobOrderStatus.Dispatching, order.OrderStatus);
    }
}
