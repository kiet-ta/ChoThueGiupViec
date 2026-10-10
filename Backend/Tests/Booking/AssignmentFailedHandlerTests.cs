using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-07: Booking cancels the order and refunds 100 % on AssignmentFailed (contract booking.md section 5).</summary>
public sealed class AssignmentFailedHandlerTests
{
    private const long OrderId = 42;
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class RecordingRefunds(bool succeed = true) : IRefundService
    {
        public List<RefundRequest> Requests { get; } = [];

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(succeed ? new RefundResult(true, request.Amount, null) : new RefundResult(false, 0m, "gateway down"));
        }
    }

    private sealed class InMemoryOrders : IOrderRepository, IUnitOfWork
    {
        public Dictionary<long, JobOrder> Orders { get; } = [];

        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public void Add(JobOrder order) => throw new NotSupportedException();
        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class Fixture
    {
        public InMemoryOrders Db { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public RecordingRefunds Refunds { get; }
        public RecordingCapacity Capacity { get; } = new();
        public JobOrder Order { get; }

        public Fixture(JobOrderStatus status = JobOrderStatus.Paid, bool refundSucceeds = true)
        {
            Refunds = new RecordingRefunds(refundSucceeds);
            Order = new JobOrder
            {
                OrderId = OrderId,
                OrderCode = "GV261014ABC123",
                CustomerId = 7,
                AddressId = 3,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = new DateOnly(2026, 10, 15),
                ShiftCode = "SHIFT_MORNING",
                AreaSnapshotM2 = 50m,
                RequiredWorkers = 1,
                TotalAmount = 260000m,
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
            };
            Advance(status);
            Db.Orders[OrderId] = Order;
        }

        private void Advance(JobOrderStatus target)
        {
            JobOrderStatus[] path = [JobOrderStatus.Paid, JobOrderStatus.Dispatching, JobOrderStatus.Assigned, JobOrderStatus.Completed];
            if (target == JobOrderStatus.PendingPayment)
            {
                return;
            }

            if (target == JobOrderStatus.Cancelled)
            {
                Order.TransitionTo(JobOrderStatus.Cancelled);
                return;
            }

            foreach (var step in path)
            {
                Order.TransitionTo(step);
                if (step == target)
                {
                    return;
                }
            }
        }

        public AssignmentFailedHandler Handler() =>
            new(Db, Db, Refunds, Capacity, new TestClock(), Publisher, NullLogger<AssignmentFailedHandler>.Instance);
    }

    private static AssignmentFailed Failed(string reason = "No worker within 10 km", long orderId = OrderId) => new(orderId, reason, Now);

    [Theory]
    [InlineData(JobOrderStatus.Paid)]
    [InlineData(JobOrderStatus.Dispatching)]
    [InlineData(JobOrderStatus.Assigned)]
    public async Task APaidOrder_IsCancelledWithTheReason_AndRefunded100Percent(JobOrderStatus status)
    {
        var f = new Fixture(status);

        await f.Handler().Handle(Failed(), default);

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Equal("No worker within 10 km", f.Order.CancelReason);
        Assert.Equal(Now, f.Order.UpdatedAt);
        Assert.Equal(new OrderCancelled(OrderId, 7, "No worker within 10 km", Now), Assert.Single(f.Publisher.Published));
        Assert.Equal(new RefundRequest(OrderId, 260000m, "No worker within 10 km"), Assert.Single(f.Refunds.Requests));
    }

    [Fact]
    public async Task ASecondAssignmentFailed_ChangesNothing_AndIsNotRefundedTwice()
    {
        var f = new Fixture();
        await f.Handler().Handle(Failed(), default);

        await f.Handler().Handle(Failed("again"), default);

        Assert.Equal("No worker within 10 km", f.Order.CancelReason);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Refunds.Requests);
    }

    [Theory]
    [InlineData(JobOrderStatus.PendingPayment)]
    [InlineData(JobOrderStatus.Completed)]
    [InlineData(JobOrderStatus.Cancelled)]
    public async Task AnOrderThatIsUnpaidCompletedOrAlreadyCancelled_IsLeftAlone(JobOrderStatus status)
    {
        var f = new Fixture(status);

        await f.Handler().Handle(Failed(), default);

        Assert.Equal(status, f.Order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
        Assert.Empty(f.Refunds.Requests);
    }

    [Fact]
    public async Task AnUnknownOrder_IsIgnored()
    {
        var f = new Fixture();

        await f.Handler().Handle(Failed(orderId: 999), default);

        Assert.Equal(JobOrderStatus.Paid, f.Order.OrderStatus);
        Assert.Empty(f.Refunds.Requests);
    }

    [Fact]
    public async Task WhenTheRefundFails_TheOrderStaysCancelled_AndTheHandlerDoesNotThrow()
    {
        var f = new Fixture(refundSucceeds: false);

        await f.Handler().Handle(Failed(), default);

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Refunds.Requests);
    }

    [Fact]
    public async Task APremiumOrder_GivesItsCapacityHoldBack_Once_AndARepeatReleasesNothingMore()
    {
        var f = new Fixture();
        f.Order.ServiceTier = ServiceTier.Premium;

        await f.Handler().Handle(Failed(), default);
        await f.Handler().Handle(Failed("again"), default);

        Assert.Equal([OrderId], f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task AnEconomyOrder_NeverTouchesAgencyCapacity()
    {
        var f = new Fixture();

        await f.Handler().Handle(Failed(), default);

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Empty(f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task APremiumOrderThatIsNotCancelled_KeepsItsHold()
    {
        var f = new Fixture(JobOrderStatus.Completed);
        f.Order.ServiceTier = ServiceTier.Premium;

        await f.Handler().Handle(Failed(), default);

        Assert.Empty(f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task WhenTheReleaseFails_TheOrderStaysCancelled_TheEventIsPublished_AndTheRefundIsStillAsked()
    {
        var f = new Fixture();
        f.Order.ServiceTier = ServiceTier.Premium;
        f.Capacity.ThrowOnRelease = true;

        await f.Handler().Handle(Failed(), default);

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Refunds.Requests);
    }

    [Fact]
    public async Task AReasonLongerThan255Characters_IsTruncated_ForTheCancelReasonColumn()
    {
        var f = new Fixture();

        await f.Handler().Handle(Failed(new string('r', 300)), default);

        Assert.Equal(255, f.Order.CancelReason!.Length);
        Assert.Equal(255, f.Refunds.Requests.Single().Reason.Length);
    }
}
