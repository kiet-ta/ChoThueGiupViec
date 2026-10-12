using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Tests.Payments;
using CommonService.WebAPI.Controllers.Booking;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-09: the customer cancels an order (decisions Q15, contract booking.md 3.7), with in-memory ports (no DB).</summary>
public sealed class OrderCancellationServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;

    /// <summary>The shift starts 2026-10-15 08:00 local = 01:00 UTC.</summary>
    private static readonly DateTime ShiftStartUtc = new(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
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

    private sealed class Orders : IOrderRepository
    {
        public Dictionary<long, JobOrder> Items { get; } = [];
        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(Items.GetValueOrDefault(orderId));
        public void Add(JobOrder order) => throw new NotSupportedException();
        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Only what the cancel touches: the open QR transaction of the order.</summary>
    private sealed class Payments : PaymentRepositoryStub
    {
        public PaymentTransaction? Open { get; set; }
        public List<(long PaymentId, string Marker)> Expired { get; } = [];

        public override Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Open);

        public override Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default)
        {
            Expired.Add((paymentId, ipnPayload));
            return Task.FromResult(true);
        }
    }

    private sealed class Fixture
    {
        public Orders Db { get; } = new();
        public Payments Pay { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public RecordingRefunds Refunds { get; }
        public RecordingCapacity Capacity { get; } = new();
        public JobOrder Order { get; }
        public DateTime NowUtc { get; set; }

        public Fixture(JobOrderStatus status = JobOrderStatus.Paid, DateTime? nowUtc = null, bool refundSucceeds = true, bool openQr = false)
        {
            NowUtc = nowUtc ?? ShiftStartUtc.AddDays(-1);
            Refunds = new RecordingRefunds(refundSucceeds);
            Order = new JobOrder
            {
                OrderId = OrderId,
                OrderCode = "GV261014ABC123",
                CustomerId = CustomerId,
                AddressId = 3,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = new DateOnly(2026, 10, 15),
                ShiftCode = BookingShifts.Morning,
                AreaSnapshotM2 = 50m,
                RequiredWorkers = 1,
                TotalAmount = 260000m,
                CustomerNote = "note",
                CreatedAt = NowUtc.AddHours(-3),
                UpdatedAt = NowUtc.AddHours(-3),
            };
            Advance(status);
            Db.Items[OrderId] = Order;
            if (openQr)
            {
                Pay.Open = new PaymentTransaction { PaymentId = 77, GatewayTxnRef = "FAKE-1", OrderId = OrderId, Purpose = PaymentPurpose.Order, TxnStatus = PaymentStatus.Pending };
            }
        }

        private void Advance(JobOrderStatus target)
        {
            if (target == JobOrderStatus.PendingPayment)
            {
                return;
            }

            if (target == JobOrderStatus.Cancelled)
            {
                Order.TransitionTo(JobOrderStatus.Cancelled);
                return;
            }

            foreach (var step in new[] { JobOrderStatus.Paid, JobOrderStatus.Dispatching, JobOrderStatus.Assigned, JobOrderStatus.Completed })
            {
                Order.TransitionTo(step);
                if (step == target)
                {
                    return;
                }
            }
        }

        public OrderCancellationService Service() => new(
            Db, Pay, Pay, Refunds, Capacity, new TestClock(NowUtc), Publisher,
            Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()),
            NullLogger<OrderCancellationService>.Instance);
    }

    private static CancelOrderRequest Reason(string reason = "Change of plans") => new(reason);

    [Fact]
    public async Task AnUnpaidOrder_IsCancelled_ItsOpenQrExpires_AndNothingIsRefunded()
    {
        var f = new Fixture(JobOrderStatus.PendingPayment, openQr: true);

        var result = await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal(JobOrderStatus.Cancelled, result.Order.OrderStatus);
        Assert.Equal("Change of plans", result.Order.CancelReason);
        Assert.Null(result.Order.PaymentDeadlineAt);
        Assert.Null(result.RefundProblem);
        Assert.Equal(77, Assert.Single(f.Pay.Expired).PaymentId);
        Assert.Empty(f.Refunds.Requests);
        Assert.Equal(new OrderCancelled(OrderId, CustomerId, "Change of plans", f.NowUtc), Assert.Single(f.Publisher.Published));
    }

    [Fact]
    public async Task AnUnpaidOrderWithoutAnOpenQr_IsCancelled_WithoutTouchingAnyTransaction()
    {
        var f = new Fixture(JobOrderStatus.PendingPayment);

        await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Empty(f.Pay.Expired);
        Assert.Empty(f.Refunds.Requests);
    }

    [Theory]
    [InlineData(JobOrderStatus.PendingPayment)]
    [InlineData(JobOrderStatus.Paid)]
    public async Task ACancelledPremiumOrder_GivesItsCapacityHoldBack(JobOrderStatus status)
    {
        var f = new Fixture(status);
        f.Order.ServiceTier = ServiceTier.Premium;

        await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal([OrderId], f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task ACancelledEconomyOrder_NeverTouchesAgencyCapacity()
    {
        var f = new Fixture();

        await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Empty(f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task ARefusedCancel_KeepsThePremiumHold()
    {
        var f = new Fixture(JobOrderStatus.Completed);
        f.Order.ServiceTier = ServiceTier.Premium;

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CancelAsync(CustomerId, OrderId, Reason()));

        Assert.Empty(f.Capacity.ReleasedOrders);
    }

    [Fact]
    public async Task WhenTheReleaseFails_TheOrderIsStillCancelled_AndRefunded()
    {
        var f = new Fixture();
        f.Order.ServiceTier = ServiceTier.Premium;
        f.Capacity.ThrowOnRelease = true;

        var result = await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal(JobOrderStatus.Cancelled, result.Order.OrderStatus);
        Assert.Null(result.RefundProblem);
        Assert.Single(f.Refunds.Requests);
        Assert.Single(f.Publisher.Published);
    }

    [Theory]
    [InlineData(JobOrderStatus.Paid)]
    [InlineData(JobOrderStatus.Dispatching)]
    public async Task APaidOrderNotAssignedYet_IsCancelled_AndRefunded100Percent(JobOrderStatus status)
    {
        var f = new Fixture(status, openQr: true);

        var result = await f.Service().CancelAsync(CustomerId, OrderId, Reason("Found someone else"));

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Equal("Found someone else", f.Order.CancelReason);
        Assert.Null(result.RefundProblem);
        Assert.Equal(new RefundRequest(OrderId, 260000m, "Found someone else"), Assert.Single(f.Refunds.Requests));
        Assert.Empty(f.Pay.Expired); // only an unpaid order has an open QR to close
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task AnAssignedOrder_MoreThanTwoHoursBeforeTheShift_IsRefunded100Percent()
    {
        var f = new Fixture(JobOrderStatus.Assigned, nowUtc: ShiftStartUtc.AddHours(-2).AddMinutes(-1));

        await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Equal(260000m, Assert.Single(f.Refunds.Requests).Amount);
    }

    [Theory]
    [InlineData(120)] // exactly 2 hours = "within 2 h"
    [InlineData(119)]
    [InlineData(30)]
    [InlineData(-10)] // the shift already started
    public async Task AnAssignedOrder_WithinTwoHoursOfTheShift_IsRefused_BecauseTheLateFeeWaitsForB10(int minutesBeforeShift)
    {
        var f = new Fixture(JobOrderStatus.Assigned, nowUtc: ShiftStartUtc.AddMinutes(-minutesBeforeShift));

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CancelAsync(CustomerId, OrderId, Reason()));

        Assert.Equal(CancelErrorCodes.InvalidState, error.Code);
        Assert.Equal(JobOrderStatus.Assigned, f.Order.OrderStatus);
        Assert.Empty(f.Refunds.Requests);
        Assert.Empty(f.Publisher.Published);
    }

    [Theory]
    [InlineData(JobOrderStatus.Completed)]
    [InlineData(JobOrderStatus.Cancelled)]
    public async Task ACompletedOrACancelledOrder_IsInvalidState(JobOrderStatus status)
    {
        var f = new Fixture(status);

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CancelAsync(CustomerId, OrderId, Reason()));

        Assert.Equal(CancelErrorCodes.InvalidState, error.Code);
        Assert.Empty(f.Refunds.Requests);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task ASecondCancel_IsInvalidState_AndNeverRefundsTwice()
    {
        var f = new Fixture();
        await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CancelAsync(CustomerId, OrderId, Reason("again")));

        Assert.Single(f.Refunds.Requests);
        Assert.Single(f.Publisher.Published);
        Assert.Equal("Change of plans", f.Order.CancelReason);
    }

    [Fact]
    public async Task AnotherCustomersOrder_AndAnUnknownOrder_AreNotFound_AndChangeNothing()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CancelAsync(CustomerId + 1, OrderId, Reason()));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CancelAsync(CustomerId, 999, Reason()));

        Assert.Equal(JobOrderStatus.Paid, f.Order.OrderStatus);
        Assert.Empty(f.Refunds.Requests);
        Assert.Empty(f.Publisher.Published);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AMissingOrBlankReason_IsAValidationError(string? reason)
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CancelAsync(CustomerId, OrderId, new CancelOrderRequest(reason)));

        Assert.Contains("reason", error.Errors.Keys);
        Assert.Equal(JobOrderStatus.Paid, f.Order.OrderStatus);
    }

    [Fact]
    public async Task TheReasonLimitIs255Characters_AndItIsTrimmed()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<ValidationException>(() => f.Service().CancelAsync(CustomerId, OrderId, Reason(new string('r', 256))));
        var result = await f.Service().CancelAsync(CustomerId, OrderId, Reason("  " + new string('r', 255) + "  "));

        Assert.Equal(255, result.Order.CancelReason!.Length);
    }

    [Fact]
    public async Task WhenTheRefundFails_TheOrderStaysCancelled_TheProblemIsReported_AndNothingThrows()
    {
        var f = new Fixture(refundSucceeds: false);

        var result = await f.Service().CancelAsync(CustomerId, OrderId, Reason());

        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Equal("gateway down", result.RefundProblem);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task TheReturnedOrder_HasTheContractShape_WithTheShiftTimesInUtc()
    {
        var f = new Fixture();

        var order = (await f.Service().CancelAsync(CustomerId, OrderId, Reason())).Order;

        Assert.Equal(OrderId, order.OrderId);
        Assert.Equal("GV261014ABC123", order.OrderCode);
        Assert.Equal(ShiftStartUtc, order.ShiftStartAt);
        Assert.Equal(ShiftStartUtc.AddHours(4), order.ShiftEndAt);
        Assert.Equal(260000m, order.TotalAmount);
        Assert.Equal("note", order.CustomerNote);
    }

    private sealed class StubUser(int? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public int? UserId => userId;
        public UserRole? Role => userId.HasValue ? UserRole.Customer : null;
    }

    private sealed class StubService(string? refundProblem) : IOrderCancellationService
    {
        public Task<CancelOrderResult> CancelAsync(int customerId, long orderId, CancelOrderRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelOrderResult(
                new CreatedOrder(orderId, "GV1", ServiceTier.Economy, 3, new DateOnly(2026, 10, 15), BookingShifts.Morning, ShiftStartUtc, ShiftStartUtc.AddHours(4),
                    50m, 1, null, 260000m, JobOrderStatus.Cancelled, null, "x", null, ShiftStartUtc, ShiftStartUtc),
                refundProblem));
    }

    [Fact]
    public async Task Controller_Is200_SaysWhenTheRefundIsPending_Is401WithoutAUser_AndCustomerOnly()
    {
        var ok = await new BookingCancelController(new StubService(null), new StubUser(CustomerId)).Cancel(OrderId, Reason(), default);
        var pending = await new BookingCancelController(new StubService("gateway down"), new StubUser(CustomerId)).Cancel(OrderId, Reason(), default);
        var anonymous = await new BookingCancelController(new StubService(null), new StubUser(null)).Cancel(OrderId, Reason(), default);

        Assert.Equal(200, ((ObjectResult)ok).StatusCode);
        Assert.Equal(200, ((ObjectResult)pending).StatusCode);
        Assert.Contains("refund could not be completed", ((CommonService.Application.Common.Models.ApiResponse<OrderDto>)((ObjectResult)pending).Value!).Message);
        Assert.Equal(401, ((ObjectResult)anonymous).StatusCode);
        var authorize = typeof(BookingCancelController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("CustomerOnly", authorize.Policy);
    }
}
