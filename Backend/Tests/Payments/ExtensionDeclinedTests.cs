using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-08: the worker declines an extension, so it is marked DECLINED and the paid extension is refunded 100 % (contract payments.md 3.4).</summary>
public sealed class ExtensionDeclinedTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const int ExtensionId = 9;
    private const decimal ExtraAmount = 97500m;
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DeclinedAt = Now.AddMinutes(7);

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

    private sealed class RecordingNotifications : INotificationService
    {
        public List<NotificationMessage> Sent { get; } = [];

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class Gateway(bool refuse = false) : IPaymentGateway
    {
        public List<(string Ref, decimal Amount, string Reason)> Refunds { get; } = [];

        public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default)
        {
            Refunds.Add((gatewayTxnRef, amount, reason));
            return Task.FromResult(refuse ? new GatewayRefundResult(false, true, "refused") : new GatewayRefundResult(true, true, null));
        }

        public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Db : PaymentRepositoryStub
    {
        public JobOrderExtension Extension { get; set; } = null!;
        public PaymentTransaction? Txn { get; set; }

        public override Task<JobOrderExtension?> GetExtensionForUpdateAsync(int extensionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobOrderExtension?>(extensionId == Extension.ExtensionId ? Extension : null);

        public override Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExtensionForPayment?>(extensionId == Extension.ExtensionId
                ? new ExtensionForPayment(Extension.ExtensionId, OrderId, "GV261014ABC123", CustomerId, Extension.WorkerId, Extension.ExtraHours, Extension.ExtraAmount, Extension.ExtStatus)
                : null);

        public override Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderForPayment?>(orderId == OrderId
                ? new OrderForPayment(OrderId, CustomerId, "GV261014ABC123", 260000m, JobOrderStatus.Assigned, Now.AddHours(-2))
                : null);

        public override Task<PaymentTransaction?> FindRefundableExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Txn is { TxnStatus: PaymentStatus.Success or PaymentStatus.Refunded } t ? Copy(t) : null);

        public override Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default)
        {
            var t = Txn!;
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

        public override Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default)
        {
            var t = Txn!;
            t.RefundedAmount -= refundAmount;
            if (t.RefundedAmount <= 0m)
            {
                t.TxnStatus = PaymentStatus.Success;
                t.RefundReason = null;
                t.RefundedAt = null;
            }

            return Task.CompletedTask;
        }

        private static PaymentTransaction Copy(PaymentTransaction t) => new()
        {
            PaymentId = t.PaymentId,
            GatewayTxnRef = t.GatewayTxnRef,
            ExtensionId = t.ExtensionId,
            Purpose = t.Purpose,
            Amount = t.Amount,
            TxnStatus = t.TxnStatus,
            RefundedAmount = t.RefundedAmount,
            PaidAt = t.PaidAt,
        };
    }

    private sealed class Fixture
    {
        public Db Repo { get; } = new();
        public Gateway Gw { get; }
        public RecordingPublisher Publisher { get; } = new();
        public RecordingNotifications Notifications { get; } = new();

        public Fixture(string extStatus = ExtensionStatuses.Paid, bool paidTransaction = true, bool gatewayRefuses = false)
        {
            Gw = new Gateway(gatewayRefuses);
            Repo.Extension = new JobOrderExtension
            {
                ExtensionId = ExtensionId,
                OrderId = OrderId,
                WorkerId = 11,
                ExtraHours = 1.5m,
                ExtraAmount = ExtraAmount,
                WorkerDecision = WorkerDecisions.Pending,
                ExtStatus = extStatus,
                RequestedAt = Now.AddMinutes(-10),
                CreatedAt = Now.AddMinutes(-10),
            };
            Repo.Txn = paidTransaction
                ? new PaymentTransaction
                {
                    PaymentId = 2,
                    GatewayTxnRef = "FAKE-ext",
                    ExtensionId = ExtensionId,
                    Purpose = PaymentPurpose.Extension,
                    Gateway = "FAKE",
                    Amount = ExtraAmount,
                    TxnStatus = PaymentStatus.Success,
                    PaidAt = Now.AddMinutes(-5),
                    CreatedAt = Now.AddMinutes(-6),
                }
                : null;
        }

        public RefundService Refunds() =>
            new(Repo, Gw, Repo, new TestClock(), Publisher, Notifications, NullLogger<RefundService>.Instance);

        public ExtensionDeclinedHandler Handler() =>
            new(Repo, Repo, Refunds(), NullLogger<ExtensionDeclinedHandler>.Instance);
    }

    private static ExtensionDeclined Declined(long extensionId = ExtensionId) =>
        new(extensionId, OrderId, 11, "Worker is busy", DeclinedAt);

    [Theory]
    [InlineData(ExtensionStatuses.Paid)]
    [InlineData(ExtensionStatuses.Accepted)]
    public async Task APaidExtension_IsMarkedDeclined_AndRefunded100Percent(string extStatus)
    {
        var f = new Fixture(extStatus);

        await f.Handler().Handle(Declined(), default);

        Assert.Equal(ExtensionStatuses.Declined, f.Repo.Extension.ExtStatus);
        Assert.Equal(WorkerDecisions.Declined, f.Repo.Extension.WorkerDecision);
        Assert.Equal(DeclinedAt, f.Repo.Extension.DecidedAt);
        Assert.Equal(PaymentStatus.Refunded, f.Repo.Txn!.TxnStatus);
        Assert.Equal(ExtraAmount, f.Repo.Txn.RefundedAmount);
        Assert.Equal("Worker is busy", f.Repo.Txn.RefundReason);
        Assert.Equal(("FAKE-ext", ExtraAmount, "Worker is busy"), Assert.Single(f.Gw.Refunds));
        Assert.Equal(new OrderRefunded(OrderId, CustomerId, ExtraAmount, "Worker is busy", Now), Assert.Single(f.Publisher.Published));
        var message = Assert.Single(f.Notifications.Sent);
        Assert.Equal("9", message.Data!["extensionId"]);
        Assert.Equal("REFUNDED", message.Data["txnStatus"]);
    }

    [Fact]
    public async Task ARepeatedDeclined_ChangesNothing_AndIsNotRefundedTwice()
    {
        var f = new Fixture();
        await f.Handler().Handle(Declined(), default);

        await f.Handler().Handle(new ExtensionDeclined(ExtensionId, OrderId, 11, "again", DeclinedAt.AddMinutes(1)), default);

        Assert.Single(f.Gw.Refunds);
        Assert.Single(f.Publisher.Published);
        Assert.Equal(DeclinedAt, f.Repo.Extension.DecidedAt);
    }

    [Theory]
    [InlineData(ExtensionStatuses.PendingPayment)]
    [InlineData(ExtensionStatuses.Expired)]
    public async Task AnUnpaidExtension_IsOnlyMarkedDeclined_WithNoRefund(string extStatus)
    {
        var f = new Fixture(extStatus, paidTransaction: false);

        await f.Handler().Handle(Declined(), default);

        Assert.Equal(ExtensionStatuses.Declined, f.Repo.Extension.ExtStatus);
        Assert.Empty(f.Gw.Refunds);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task AnUnknownOrInvalidExtension_IsIgnored()
    {
        var f = new Fixture();

        await f.Handler().Handle(Declined(999), default);
        await f.Handler().Handle(Declined(0), default);
        await f.Handler().Handle(Declined(long.MaxValue), default);

        Assert.Equal(ExtensionStatuses.Paid, f.Repo.Extension.ExtStatus);
        Assert.Empty(f.Gw.Refunds);
    }

    [Fact]
    public async Task WhenTheGatewayRefusesTheRefund_TheExtensionStaysDeclined_TheMoneyStaysRecordedAsPaid_AndNothingThrows()
    {
        var f = new Fixture(gatewayRefuses: true);

        await f.Handler().Handle(Declined(), default);

        Assert.Equal(ExtensionStatuses.Declined, f.Repo.Extension.ExtStatus);
        Assert.Equal(PaymentStatus.Success, f.Repo.Txn!.TxnStatus);
        Assert.Equal(0m, f.Repo.Txn.RefundedAmount);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task RefundExtension_WithoutAPaidTransaction_Fails_AndAFullyRefundedOneFails()
    {
        var unpaid = new Fixture(paidTransaction: false);
        var done = new Fixture();
        await done.Refunds().RefundExtensionAsync(ExtensionId, "first");

        var a = await unpaid.Refunds().RefundExtensionAsync(ExtensionId, "x");
        var b = await done.Refunds().RefundExtensionAsync(ExtensionId, "second");
        var unknown = await done.Refunds().RefundExtensionAsync(999, "x");

        Assert.False(a.Succeeded);
        Assert.False(b.Succeeded);
        Assert.False(unknown.Succeeded);
        Assert.Single(done.Gw.Refunds);
    }
}
