using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-04: payment QR of an order (contract payments.md 2.1), with in-memory ports.</summary>
public sealed class PaymentQrServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OrderCreatedAt = Now.AddMinutes(-5);

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    /// <summary>Order and transaction tables in one; the unit of work assigns ids on save.</summary>
    private sealed class InMemoryPayments : IPaymentRepository, IUnitOfWork
    {
        private long _nextId = 1;
        private readonly List<PaymentTransaction> _pending = [];
        public Dictionary<long, OrderForPayment> Orders { get; } = [];
        public List<PaymentTransaction> Saved { get; } = [];

        public Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.LastOrDefault(t => t.OrderId == orderId && t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending));

        public void Add(PaymentTransaction transaction) => _pending.Add(transaction);

        public Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(t => t.GatewayTxnRef == gatewayTxnRef));

        public Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var t in _pending)
            {
                t.PaymentId = _nextId++;
                Saved.Add(t);
            }

            var count = _pending.Count;
            _pending.Clear();
            return Task.FromResult(count);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class FailingGateway : IPaymentGateway
    {
        public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("sandbox unreachable");

        public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Fixture
    {
        public InMemoryPayments Db { get; } = new();
        public FakePaymentGateway Gateway { get; } = new();
        public DateTime NowUtc { get; set; } = Now;

        public Fixture(JobOrderStatus status = JobOrderStatus.PendingPayment) =>
            Db.Orders[OrderId] = new OrderForPayment(OrderId, CustomerId, "GV261014ABC123", 260000m, status, OrderCreatedAt);

        public PaymentQrService Service(IPaymentGateway? gateway = null) =>
            new(Db, gateway ?? Gateway, Db, new TestClock(NowUtc), Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()));
    }

    [Fact]
    public async Task FirstCall_CreatesOnePendingTransaction_WithTheOrderAmountAndTheGatewayQr()
    {
        var f = new Fixture();

        var result = await f.Service().CreateOrderQrAsync(CustomerId, OrderId);

        Assert.True(result.Created);
        var saved = Assert.Single(f.Db.Saved);
        Assert.Equal(PaymentStatus.Pending, saved.TxnStatus);
        Assert.Equal(PaymentPurpose.Order, saved.Purpose);
        Assert.Equal(OrderId, saved.OrderId);
        Assert.Equal(260000m, saved.Amount);
        Assert.Equal("FAKE", saved.Gateway);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.StartsWith("FAKE-", saved.GatewayTxnRef);
        Assert.Equal($"fake-qr://{saved.GatewayTxnRef}", saved.QrPayload);

        var payment = result.Payment;
        Assert.Equal(saved.PaymentId, payment.PaymentId);
        Assert.Equal("ORDER", payment.Purpose);
        Assert.Equal("PENDING", payment.TxnStatus);
        Assert.Equal(260000m, payment.Amount);
        Assert.Equal(saved.QrPayload, payment.QrPayload);
        Assert.NotNull(payment.PayUrl);
        Assert.Equal(OrderCreatedAt.AddMinutes(15), payment.ExpiresAt);
        Assert.Null(payment.PaidAt);
        Assert.True(payment.Sandbox);
    }

    [Fact]
    public async Task TheGatewayIsAskedForTheOrder_WithItsAmountCodeAndDeadline()
    {
        var f = new Fixture();

        await f.Service().CreateOrderQrAsync(CustomerId, OrderId);

        var request = Assert.Single(f.Gateway.Created);
        Assert.Equal(PaymentPurpose.Order, request.Purpose);
        Assert.Equal("42", request.PaymentRef);
        Assert.Equal(260000m, request.Amount);
        Assert.Equal("GV261014ABC123", request.Description);
        Assert.Equal(OrderCreatedAt.AddMinutes(15), request.ExpiresAtUtc);
    }

    [Fact]
    public async Task SecondCall_ReturnsTheSamePayment_WithoutAskingTheGatewayAgain()
    {
        var f = new Fixture();
        var first = await f.Service().CreateOrderQrAsync(CustomerId, OrderId);

        var second = await f.Service().CreateOrderQrAsync(CustomerId, OrderId);

        Assert.False(second.Created);
        Assert.Equal(first.Payment.PaymentId, second.Payment.PaymentId);
        Assert.Equal(first.Payment.QrPayload, second.Payment.QrPayload);
        Assert.Single(f.Db.Saved);
        Assert.Single(f.Gateway.Created);
    }

    [Fact]
    public async Task AnotherCustomersOrder_AndAnUnknownOrder_AreNotFound_AndNothingIsStored()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateOrderQrAsync(CustomerId + 1, OrderId));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateOrderQrAsync(CustomerId, 999));

        Assert.Empty(f.Db.Saved);
        Assert.Empty(f.Gateway.Created);
    }

    [Theory]
    [InlineData(JobOrderStatus.Paid)]
    [InlineData(JobOrderStatus.Dispatching)]
    [InlineData(JobOrderStatus.Assigned)]
    [InlineData(JobOrderStatus.Completed)]
    [InlineData(JobOrderStatus.Cancelled)]
    public async Task AnOrderThatIsNotWaitingForPayment_IsInvalidState(JobOrderStatus status)
    {
        var f = new Fixture(status);

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CreateOrderQrAsync(CustomerId, OrderId));

        Assert.Equal(PaymentErrorCodes.InvalidState, error.Code);
        Assert.Empty(f.Db.Saved);
        Assert.Empty(f.Gateway.Created);
    }

    [Fact]
    public async Task AtOrPastTheDeadline_IsPaymentExpired_ButOneMinuteBeforeIsAccepted()
    {
        var late = new Fixture { NowUtc = OrderCreatedAt.AddMinutes(15) };
        var inTime = new Fixture { NowUtc = OrderCreatedAt.AddMinutes(14).AddSeconds(59) };

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => late.Service().CreateOrderQrAsync(CustomerId, OrderId));
        var ok = await inTime.Service().CreateOrderQrAsync(CustomerId, OrderId);

        Assert.Equal(PaymentErrorCodes.PaymentExpired, error.Code);
        Assert.Empty(late.Db.Saved);
        Assert.Empty(late.Gateway.Created);
        Assert.True(ok.Created);
    }

    [Fact]
    public async Task WhenTheGatewayFails_NothingIsStored_AndTheFailureIsReportedAsUnavailable()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => f.Service(new FailingGateway()).CreateOrderQrAsync(CustomerId, OrderId));

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Empty(f.Db.Saved);
    }

    [Fact]
    public async Task TheGatewayTxnRefAndIpnPayload_AreNeverExposed()
    {
        var f = new Fixture();

        await f.Service().CreateOrderQrAsync(CustomerId, OrderId);

        var names = typeof(PaymentDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("GatewayTxnRef", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Ipn", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubUser(int? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public int? UserId => userId;
        public UserRole? Role => userId.HasValue ? UserRole.Customer : null;
    }

    private sealed class StubService(Func<PaymentQrResult> result) : IPaymentQrService
    {
        public Task<PaymentQrResult> CreateOrderQrAsync(int customerId, long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(result());
    }

    private static PaymentDto Dto() => new(1, "ORDER", OrderId, null, "FAKE", 260000m, "PENDING", "qr", null, Now, null, Now, true);

    [Fact]
    public async Task Controller_Answers201WhenCreated_200WhenExisting_502WhenTheGatewayFails_401WithoutAUser()
    {
        var created = new PaymentsController(new StubService(() => new PaymentQrResult(Dto(), true)), new StubUser(CustomerId));
        var existing = new PaymentsController(new StubService(() => new PaymentQrResult(Dto(), false)), new StubUser(CustomerId));
        var failing = new PaymentsController(new StubService(() => throw new PaymentGatewayUnavailableException("down")), new StubUser(CustomerId));
        var anonymous = new PaymentsController(new StubService(() => new PaymentQrResult(Dto(), true)), new StubUser(null));

        Assert.Equal(201, ((ObjectResult)await created.CreateOrderQr(OrderId, default)).StatusCode);
        Assert.Equal(200, ((ObjectResult)await existing.CreateOrderQr(OrderId, default)).StatusCode);
        Assert.Equal(502, ((ObjectResult)await failing.CreateOrderQr(OrderId, default)).StatusCode);
        Assert.Equal(401, ((ObjectResult)await anonymous.CreateOrderQr(OrderId, default)).StatusCode);
    }

    [Fact]
    public void Controller_IsCustomerOnly()
    {
        var authorize = typeof(PaymentsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal("CustomerOnly", authorize.Policy);
    }
}
