using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-07: real IRefundService (contract payments.md 3.4, P2), with in-memory ports (no DB).</summary>
public sealed class RefundServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const decimal Paid = 260000m;
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
        private readonly List<object> _published = [];
        public bool Fail { get; set; }
        public IReadOnlyList<object> Published { get { lock (_published) { return _published.ToArray(); } } }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("listener failed");
            }

            lock (_published) { _published.Add(notification); }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly List<NotificationMessage> _sent = [];
        public IReadOnlyList<NotificationMessage> Sent { get { lock (_sent) { return _sent.ToArray(); } } }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            lock (_sent) { _sent.Add(message); }
            return Task.CompletedTask;
        }
    }

    public enum GatewayMode { Succeeds, Refuses, Unsupported, Throws }

    private sealed class RefundGateway : IPaymentGateway
    {
        private readonly object _gate = new();
        public GatewayMode Mode { get; set; } = GatewayMode.Succeeds;
        public List<(string Ref, decimal Amount, string Reason)> Calls { get; } = [];
        public Barrier? CallBarrier { get; set; }

        public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default)
        {
            lock (_gate) { Calls.Add((gatewayTxnRef, amount, reason)); }
            CallBarrier?.SignalAndWait(TimeSpan.FromSeconds(2));
            return Mode switch
            {
                GatewayMode.Succeeds => Task.FromResult(new GatewayRefundResult(true, true, null)),
                GatewayMode.Refuses => Task.FromResult(new GatewayRefundResult(false, true, "refused by the gateway")),
                GatewayMode.Unsupported => Task.FromResult(new GatewayRefundResult(false, false, "no refund API")),
                _ => throw new HttpRequestException("sandbox down"),
            };
        }

        public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>One lock makes reserve and revert atomic like the SQL conditional updates.</summary>
    private sealed class InMemoryPayments : IPaymentRepository, IUnitOfWork
    {
        private readonly object _gate = new();
        public Dictionary<long, OrderForPayment> Orders { get; } = [];
        public List<PaymentTransaction> Transactions { get; } = [];

        public Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var t = Transactions
                    .Where(x => x.OrderId == orderId && x.Purpose == PaymentPurpose.Order && (x.TxnStatus == PaymentStatus.Success || x.TxnStatus == PaymentStatus.Refunded))
                    .OrderByDescending(x => x.PaidAt).FirstOrDefault();
                return Task.FromResult(t is null ? null : Copy(t));
            }
        }

        public Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var t = Transactions.Single(x => x.PaymentId == paymentId);
                if (!(t.TxnStatus is PaymentStatus.Success or PaymentStatus.Refunded) || t.RefundedAmount + refundAmount > t.Amount)
                {
                    return Task.FromResult(false);
                }

                t.RefundedAmount += refundAmount;
                t.TxnStatus = PaymentStatus.Refunded;
                t.RefundReason = reason;
                t.RefundedAt = refundedAtUtc;
                return Task.FromResult(true);
            }
        }

        public Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var t = Transactions.Single(x => x.PaymentId == paymentId);
                if (t.RefundedAmount >= refundAmount)
                {
                    t.RefundedAmount -= refundAmount;
                }

                if (t.TxnStatus == PaymentStatus.Refunded && t.RefundedAmount <= 0m)
                {
                    t.TxnStatus = PaymentStatus.Success;
                    t.RefundReason = null;
                    t.RefundedAt = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(PaymentTransaction transaction) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindPendingExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingExtensionPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindRefundableExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobOrderExtension?> GetExtensionForUpdateAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPaymentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();

        private static PaymentTransaction Copy(PaymentTransaction t) => new()
        {
            PaymentId = t.PaymentId,
            GatewayTxnRef = t.GatewayTxnRef,
            OrderId = t.OrderId,
            Purpose = t.Purpose,
            Gateway = t.Gateway,
            Amount = t.Amount,
            TxnStatus = t.TxnStatus,
            PaidAt = t.PaidAt,
            RefundedAmount = t.RefundedAmount,
        };
    }

    private sealed class Fixture
    {
        public InMemoryPayments Db { get; } = new();
        public RefundGateway Gateway { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public RecordingNotifications Notifications { get; } = new();

        public Fixture(PaymentStatus status = PaymentStatus.Success)
        {
            Db.Orders[OrderId] = new OrderForPayment(OrderId, CustomerId, "GV261014ABC123", Paid, JobOrderStatus.Paid, Now.AddHours(-1));
            Db.Transactions.Add(new PaymentTransaction
            {
                PaymentId = 1,
                GatewayTxnRef = "FAKE-abc",
                OrderId = OrderId,
                Purpose = PaymentPurpose.Order,
                Gateway = "FAKE",
                Amount = Paid,
                TxnStatus = status,
                PaidAt = Now.AddMinutes(-30),
                CreatedAt = Now.AddMinutes(-40),
            });
        }

        public PaymentTransaction Txn => Db.Transactions.Single();

        public RefundService Service() =>
            new(Db, Gateway, Db, new TestClock(), Publisher, Notifications, NullLogger<RefundService>.Instance);
    }

    private static RefundRequest Request(decimal amount, string reason = "NO_WORKER", long orderId = OrderId) => new(orderId, amount, reason);

    [Fact]
    public async Task FullRefund_ReservesTheAmount_CallsTheGatewayOnce_AndRecordsRefunded()
    {
        var f = new Fixture();

        var result = await f.Service().RefundAsync(Request(Paid));

        Assert.True(result.Succeeded);
        Assert.Equal(Paid, result.RefundedAmount);
        Assert.Equal(PaymentStatus.Refunded, f.Txn.TxnStatus);
        Assert.Equal(Paid, f.Txn.RefundedAmount);
        Assert.Equal("NO_WORKER", f.Txn.RefundReason);
        Assert.Equal(Now, f.Txn.RefundedAt);
        Assert.Equal(("FAKE-abc", Paid, "NO_WORKER"), Assert.Single(f.Gateway.Calls));
    }

    [Fact]
    public async Task Refund_PublishesOrderRefundedOnce_AndPushesPaymentStatus()
    {
        var f = new Fixture();

        await f.Service().RefundAsync(Request(156000m, "ABSENCE"));

        var refunded = Assert.IsType<OrderRefunded>(Assert.Single(f.Publisher.Published));
        Assert.Equal(new OrderRefunded(OrderId, CustomerId, 156000m, "ABSENCE", Now), refunded);
        var message = Assert.Single(f.Notifications.Sent);
        Assert.Equal(UserRole.Customer, message.RecipientRole);
        Assert.Equal(CustomerId, message.RecipientId);
        Assert.Equal("payment.status", message.Topic);
        Assert.Equal("REFUNDED", message.Data!["txnStatus"]);
    }

    [Fact]
    public async Task PartialRefunds_Add_UpToTheAmount_AndTheNextOneAboveItIsRefused()
    {
        var f = new Fixture();

        var first = await f.Service().RefundAsync(Request(156000m, "ABSENCE_60"));
        var second = await f.Service().RefundAsync(Request(100000m, "DISPUTE"));
        var third = await f.Service().RefundAsync(Request(100000m, "TOO_MUCH"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(third.Succeeded);
        Assert.Equal(256000m, f.Txn.RefundedAmount);
        Assert.Equal("DISPUTE", f.Txn.RefundReason);
        Assert.Equal(2, f.Gateway.Calls.Count);
        Assert.Equal(2, f.Publisher.Published.Count);
    }

    [Fact]
    public async Task ExactlyTheRemainingAmount_IsAccepted_ButOneDongMoreIsNot()
    {
        var f = new Fixture();
        await f.Service().RefundAsync(Request(100000m));

        var tooMuch = await f.Service().RefundAsync(Request(160001m));
        var exact = await f.Service().RefundAsync(Request(160000m));

        Assert.False(tooMuch.Succeeded);
        Assert.True(exact.Succeeded);
        Assert.Equal(Paid, f.Txn.RefundedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    [InlineData(0.4)]
    public async Task ANonPositiveAmount_IsRefused_AndNothingIsCalled(double amount)
    {
        var f = new Fixture();

        var result = await f.Service().RefundAsync(Request((decimal)amount));

        Assert.False(result.Succeeded);
        Assert.Empty(f.Gateway.Calls);
        Assert.Equal(0m, f.Txn.RefundedAmount);
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
    }

    [Fact]
    public async Task ANonWholeAmount_IsRoundedToWholeVnd()
    {
        var f = new Fixture();

        var result = await f.Service().RefundAsync(Request(99999.5m));

        Assert.True(result.Succeeded);
        Assert.Equal(100000m, result.RefundedAmount);
        Assert.Equal(100000m, f.Txn.RefundedAmount);
    }

    [Fact]
    public async Task AnUnknownOrder_AndAnOrderWithoutAPaidTransaction_AreRefused()
    {
        var unknown = new Fixture();
        var unpaid = new Fixture(PaymentStatus.Pending);
        var expired = new Fixture(PaymentStatus.Expired);

        var a = await unknown.Service().RefundAsync(Request(Paid, orderId: 999));
        var b = await unpaid.Service().RefundAsync(Request(Paid));
        var c = await expired.Service().RefundAsync(Request(Paid));

        Assert.False(a.Succeeded);
        Assert.False(b.Succeeded);
        Assert.False(c.Succeeded);
        Assert.Empty(unknown.Gateway.Calls);
        Assert.Empty(unpaid.Gateway.Calls);
        Assert.Empty(expired.Gateway.Calls);
    }

    [Theory]
    [InlineData(GatewayMode.Refuses)]
    [InlineData(GatewayMode.Throws)]
    public async Task WhenTheGatewayFails_TheReservationIsRevertedExactly_AndNothingIsPublished(GatewayMode mode)
    {
        var f = new Fixture();
        f.Gateway.Mode = mode;

        var result = await f.Service().RefundAsync(Request(Paid));

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
        Assert.Equal(0m, f.Txn.RefundedAmount);
        Assert.Null(f.Txn.RefundReason);
        Assert.Null(f.Txn.RefundedAt);
        Assert.Empty(f.Publisher.Published);
        Assert.Empty(f.Notifications.Sent);
    }

    [Fact]
    public async Task AFailedSecondRefund_KeepsTheFirstOneRecorded()
    {
        var f = new Fixture();
        await f.Service().RefundAsync(Request(100000m, "FIRST"));
        f.Gateway.Mode = GatewayMode.Refuses;

        var result = await f.Service().RefundAsync(Request(50000m, "SECOND"));

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Refunded, f.Txn.TxnStatus);
        Assert.Equal(100000m, f.Txn.RefundedAmount);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task AGatewayWithoutARefundApi_IsRecordedInTheDatabaseOnly_AndSaysSo()
    {
        var f = new Fixture();
        f.Gateway.Mode = GatewayMode.Unsupported;

        var result = await f.Service().RefundAsync(Request(Paid));

        Assert.True(result.Succeeded);
        Assert.Contains("database only", result.Message);
        Assert.Equal(PaymentStatus.Refunded, f.Txn.TxnStatus);
        Assert.Equal(Paid, f.Txn.RefundedAmount);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task TwoConcurrentFullRefunds_CannotExceedTheAmount_AndTheGatewayRefundsOnce()
    {
        var f = new Fixture();

        var results = await Task.WhenAll(
            Task.Run(() => f.Service().RefundAsync(Request(Paid, "A"))),
            Task.Run(() => f.Service().RefundAsync(Request(Paid, "B"))));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.Equal(Paid, f.Txn.RefundedAmount);
        Assert.Single(f.Gateway.Calls);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task AReasonLongerThan255Characters_IsTruncated_ForTheColumn()
    {
        var f = new Fixture();

        await f.Service().RefundAsync(Request(Paid, new string('r', 300)));

        Assert.Equal(255, f.Txn.RefundReason!.Length);
    }

    [Fact]
    public async Task AFailingListener_DoesNotTurnADoneRefundIntoAFailure()
    {
        var f = new Fixture();
        f.Publisher.Fail = true;

        var result = await f.Service().RefundAsync(Request(Paid));

        Assert.True(result.Succeeded);
        Assert.Equal(Paid, f.Txn.RefundedAmount);
    }
}
