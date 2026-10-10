using CommonService.Application.Features.Booking;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-05a: reconciliation job and QR expiry (contract payments.md 3.1, 3.3), with in-memory ports (no DB).</summary>
public sealed class PaymentReconciliationServiceTests
{
    private const int CustomerId = 7;
    private const decimal Amount = 260000m;
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

    private sealed class NoNotifications : INotificationService
    {
        public int Count { get; private set; }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The gateway answers per reference from a script; a reference in <see cref="Throwing"/> fails.</summary>
    private sealed class ScriptedGateway : IPaymentGateway
    {
        public Dictionary<string, (PaymentStatus Status, decimal Amount)> Statuses { get; } = [];
        public HashSet<string> Throwing { get; } = [];
        public List<string> Queried { get; } = [];

        public Task<GatewayTransactionStatus> QueryStatusAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
        {
            Queried.Add(gatewayTxnRef);
            if (Throwing.Contains(gatewayTxnRef))
            {
                throw new HttpRequestException("sandbox down");
            }

            var (status, amount) = Statuses.GetValueOrDefault(gatewayTxnRef, (PaymentStatus.Pending, Amount));
            return Task.FromResult(new GatewayTransactionStatus(gatewayTxnRef, status, amount));
        }

        public Task<PaymentQr> CreateQrAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IpnVerification> VerifyIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GatewayRefundResult> RefundAsync(string gatewayTxnRef, decimal amount, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryPayments : IPaymentRepository, IUnitOfWork
    {
        public List<PaymentTransaction> Transactions { get; } = [];
        public Dictionary<long, JobOrder> Orders { get; } = [];

        public Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            var o = Orders.GetValueOrDefault(orderId);
            return Task.FromResult(o is null ? null : new OrderForPayment(o.OrderId, o.CustomerId, o.OrderCode, o.TotalAmount, o.OrderStatus, o.CreatedAt));
        }

        public Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(PaymentTransaction transaction) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default)
        {
            var t = Transactions.Single(x => x.PaymentId == paymentId);
            if (t.TxnStatus != PaymentStatus.Pending)
            {
                return Task.FromResult(false);
            }

            t.TxnStatus = PaymentStatus.Success;
            t.PaidAt = paidAtUtc;
            t.IpnPayload = ipnPayload;
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default)
        {
            var t = Transactions.Single(x => x.PaymentId == paymentId);
            if (t.TxnStatus != PaymentStatus.Pending)
            {
                return Task.FromResult(false);
            }

            t.TxnStatus = PaymentStatus.Expired;
            t.IpnPayload = ipnPayload;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentTransaction>>(Transactions
                .Where(t => t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending && t.CreatedAt <= cutoffUtc)
                .OrderBy(t => t.CreatedAt).Take(take).ToList());

        public Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(Orders.Values
                .Where(o => o.OrderStatus == JobOrderStatus.PendingPayment && o.CreatedAt <= createdBeforeUtc)
                .Where(o => !Transactions.Any(t => t.OrderId == o.OrderId && t.Purpose == PaymentPurpose.Order
                    && (t.TxnStatus == PaymentStatus.Pending || t.TxnStatus == PaymentStatus.Success)))
                .OrderBy(o => o.CreatedAt).Select(o => o.OrderId).Take(take).ToList());

        public Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindPendingExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingExtensionPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentTransaction>>(Transactions
                .Where(t => t.Purpose == PaymentPurpose.Extension && t.TxnStatus == PaymentStatus.Pending && t.CreatedAt <= cutoffUtc)
                .OrderBy(t => t.CreatedAt).Take(take).ToList());
        public Task<PaymentTransaction?> FindRefundableExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Dictionary<int, JobOrderExtension> Extensions { get; } = [];

        public Task<JobOrderExtension?> GetExtensionForUpdateAsync(int extensionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Extensions.GetValueOrDefault(extensionId));
        public Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class Fixture
    {
        public InMemoryPayments Db { get; } = new();
        public ScriptedGateway Gateway { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public NoNotifications Notifications { get; } = new();

        /// <summary>An order created <paramref name="orderAgeMinutes"/> ago with one PENDING transaction created <paramref name="txnAgeMinutes"/> ago.</summary>
        public (JobOrder Order, PaymentTransaction? Txn) AddOrder(long orderId, int orderAgeMinutes, int? txnAgeMinutes, string? reference = null,
            JobOrderStatus status = JobOrderStatus.PendingPayment, PaymentPurpose purpose = PaymentPurpose.Order)
        {
            var order = new JobOrder
            {
                OrderId = orderId,
                OrderCode = $"GV{orderId}",
                CustomerId = CustomerId,
                AddressId = 3,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = new DateOnly(2026, 10, 15),
                ShiftCode = "SHIFT_MORNING",
                AreaSnapshotM2 = 50m,
                RequiredWorkers = 1,
                TotalAmount = Amount,
                CreatedAt = Now.AddMinutes(-orderAgeMinutes),
                UpdatedAt = Now.AddMinutes(-orderAgeMinutes),
            };
            if (status == JobOrderStatus.Cancelled)
            {
                order.TransitionTo(JobOrderStatus.Cancelled);
            }
            else
            {
                foreach (var step in new[] { JobOrderStatus.Paid, JobOrderStatus.Dispatching, JobOrderStatus.Assigned })
                {
                    if (status == JobOrderStatus.PendingPayment) { break; }
                    order.TransitionTo(step);
                    if (step == status) { break; }
                }
            }

            Db.Orders[orderId] = order;
            if (txnAgeMinutes is null)
            {
                return (order, null);
            }

            var txn = new PaymentTransaction
            {
                PaymentId = orderId * 10,
                GatewayTxnRef = reference ?? $"FAKE-{orderId}",
                OrderId = purpose == PaymentPurpose.Order ? orderId : null,
                ExtensionId = purpose == PaymentPurpose.Extension ? 9 : null,
                Purpose = purpose,
                Gateway = "FAKE",
                Amount = Amount,
                TxnStatus = PaymentStatus.Pending,
                CreatedAt = Now.AddMinutes(-txnAgeMinutes.Value),
            };
            Db.Transactions.Add(txn);
            return (order, txn);
        }

        public PaymentReconciliationService Service() => new(
            Db, Gateway,
            new PaymentSettlementService(Db, Db, new TestClock(), Publisher, Notifications, NullLogger<PaymentSettlementService>.Instance),
            Db, new TestClock(), Publisher,
            Microsoft.Extensions.Options.Options.Create(new BusinessRules()),
            NullLogger<PaymentReconciliationService>.Instance);
    }

    [Fact]
    public async Task ALostIpn_IsRecovered_TheTransactionBecomesSuccess_AndTheOrderPaid_ThroughTheJobAlone()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, orderAgeMinutes: 6, txnAgeMinutes: 5);
        f.Gateway.Statuses[txn!.GatewayTxnRef] = (PaymentStatus.Success, Amount);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(1, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(Now, txn.PaidAt);
        Assert.Equal(JobOrderStatus.Paid, order.OrderStatus);
        Assert.IsType<OrderPaid>(Assert.Single(f.Publisher.Published));
        Assert.Equal(1, f.Notifications.Count);
    }

    [Fact]
    public async Task ALateIpn_AfterTheReconciliation_ChangesNothing()
    {
        var f = new Fixture();
        var (_, txn) = f.AddOrder(1, 6, 5);
        f.Gateway.Statuses[txn!.GatewayTxnRef] = (PaymentStatus.Success, Amount);
        await f.Service().RunOnceAsync();
        var settlement = new PaymentSettlementService(f.Db, f.Db, new TestClock(), f.Publisher, f.Notifications, NullLogger<PaymentSettlementService>.Instance);

        var second = await settlement.SettlePaidAsync(txn.PaymentId, 1, Amount, "ipn");

        Assert.False(second);
        Assert.Single(f.Publisher.Published);
        Assert.Equal(1, f.Notifications.Count);
        Assert.Equal("{\"source\":\"reconciliation\",\"status\":\"SUCCESS\"}", txn.IpnPayload);
    }

    [Fact]
    public async Task PaidWithAWrongAmount_IsNotMarkedPaid()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, 6, 5);
        f.Gateway.Statuses[txn!.GatewayTxnRef] = (PaymentStatus.Success, Amount - 1);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Pending, txn.TxnStatus);
        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task UnpaidPastTheDeadline_ExpiresTheTransaction_CancelsTheOrder_AndPublishesOrderCancelledOnce()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, orderAgeMinutes: 15, txnAgeMinutes: 14);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 1, 1, 0), result);
        Assert.Equal(PaymentStatus.Expired, txn!.TxnStatus);
        Assert.Equal(JobOrderStatus.Cancelled, order.OrderStatus);
        Assert.Equal("PAYMENT_EXPIRED", order.CancelReason);
        Assert.Equal(Now, order.UpdatedAt);
        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(f.Publisher.Published));
        Assert.Equal(new OrderCancelled(1, CustomerId, "PAYMENT_EXPIRED", Now), cancelled);
        Assert.Empty(f.Publisher.Published.OfType<OrderPaid>());
        Assert.Equal(0, f.Notifications.Count);
    }

    [Fact]
    public async Task RunningTheJobAgain_ChangesNothingMore()
    {
        var f = new Fixture();
        f.AddOrder(1, 15, 14);
        await f.Service().RunOnceAsync();

        var second = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), second);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task NotYetPastTheDeadline_NothingChanges_ButTheGatewayIsAsked()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, orderAgeMinutes: 10, txnAgeMinutes: 9);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Pending, txn!.TxnStatus);
        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Equal([txn.GatewayTxnRef], f.Gateway.Queried);
    }

    [Fact]
    public async Task AGatewayStatusExpiredBeforeTheDeadline_OnlyExpiresTheTransaction()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, 10, 9);
        f.Gateway.Statuses[txn!.GatewayTxnRef] = (PaymentStatus.Expired, Amount);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 1, 0, 0), result);
        Assert.Equal(PaymentStatus.Expired, txn.TxnStatus);
        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task ATransactionYoungerThanThreeMinutes_IsNotQueried()
    {
        var f = new Fixture();
        f.AddOrder(1, orderAgeMinutes: 4, txnAgeMinutes: 2);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), result);
        Assert.Empty(f.Gateway.Queried);
    }

    [Fact]
    public async Task AnOrderWithNoTransaction_PastTheDeadline_IsCancelled_ButNotBefore()
    {
        var f = new Fixture();
        var (late, _) = f.AddOrder(1, orderAgeMinutes: 15, txnAgeMinutes: null);
        var (early, _) = f.AddOrder(2, orderAgeMinutes: 14, txnAgeMinutes: null);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 1, 0), result);
        Assert.Equal(JobOrderStatus.Cancelled, late.OrderStatus);
        Assert.Equal("PAYMENT_EXPIRED", late.CancelReason);
        Assert.Equal(JobOrderStatus.PendingPayment, early.OrderStatus);
        Assert.Equal(1, f.Publisher.Published.OfType<OrderCancelled>().Count());
    }

    [Fact]
    public async Task AnOrderWhoseTransactionAlreadyExpiredByIpn_PastTheDeadline_IsCancelled()
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, 20, 19);
        txn!.TxnStatus = PaymentStatus.Expired;

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 1, 0), result);
        Assert.Equal(JobOrderStatus.Cancelled, order.OrderStatus);
    }

    [Theory]
    [InlineData(JobOrderStatus.Paid)]
    [InlineData(JobOrderStatus.Cancelled)]
    public async Task AnOrderThatIsNoLongerUnpaid_IsNeverCancelled_AndItsPendingTransactionIsClosed(JobOrderStatus status)
    {
        var f = new Fixture();
        var (order, txn) = f.AddOrder(1, 30, 29, status: status);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(status, order.OrderStatus);
        Assert.Equal(PaymentStatus.Expired, txn!.TxnStatus);
        Assert.Equal(new ReconciliationResult(0, 1, 0, 0), result);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task OneFailingItem_IsCountedAndLogged_AndTheOthersAreStillProcessed()
    {
        var f = new Fixture();
        var (_, broken) = f.AddOrder(1, 6, 5, reference: "FAKE-broken");
        var (order, healthy) = f.AddOrder(2, 6, 4);
        f.Gateway.Throwing.Add(broken!.GatewayTxnRef);
        f.Gateway.Statuses[healthy!.GatewayTxnRef] = (PaymentStatus.Success, Amount);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(1, 0, 0, 1), result);
        Assert.Equal(PaymentStatus.Pending, broken.TxnStatus);
        Assert.Equal(JobOrderStatus.Paid, order.OrderStatus);
    }

    private static (JobOrderExtension Extension, PaymentTransaction Txn) AddExtension(Fixture f, int txnAgeMinutes, string extStatus = ExtensionStatuses.PendingPayment)
    {
        var (order, _) = f.AddOrder(1, orderAgeMinutes: 120, txnAgeMinutes: null, status: JobOrderStatus.Assigned);
        var extension = new JobOrderExtension
        {
            ExtensionId = 5,
            OrderId = order.OrderId,
            WorkerId = 11,
            ExtraHours = 1.5m,
            ExtraAmount = 97500m,
            WorkerDecision = WorkerDecisions.Pending,
            ExtStatus = extStatus,
            RequestedAt = Now.AddMinutes(-txnAgeMinutes - 1),
            CreatedAt = Now.AddMinutes(-txnAgeMinutes - 1),
        };
        f.Db.Extensions[5] = extension;
        var txn = new PaymentTransaction
        {
            PaymentId = 50,
            GatewayTxnRef = "FAKE-ext",
            ExtensionId = 5,
            Purpose = PaymentPurpose.Extension,
            Gateway = "FAKE",
            Amount = 97500m,
            TxnStatus = PaymentStatus.Pending,
            CreatedAt = Now.AddMinutes(-txnAgeMinutes),
        };
        f.Db.Transactions.Add(txn);
        return (extension, txn);
    }

    [Fact]
    public async Task ALostExtensionIpn_IsRecovered_TheExtensionIsPaid_OneExtensionPaid_AndTheOrderIsUntouched()
    {
        var f = new Fixture();
        var (extension, txn) = AddExtension(f, txnAgeMinutes: 5);
        f.Gateway.Statuses[txn.GatewayTxnRef] = (PaymentStatus.Success, 97500m);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(1, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.Paid, extension.ExtStatus);
        Assert.Equal(JobOrderStatus.Assigned, f.Db.Orders[1].OrderStatus);
        var paid = Assert.IsType<ExtensionPaid>(Assert.Single(f.Publisher.Published));
        Assert.Equal(new ExtensionPaid(5, 1, 11, 1.5m, 97500m, Now), paid);
        Assert.Equal(1, f.Notifications.Count);
    }

    [Fact]
    public async Task AnUnpaidExtension_PastItsOwnDeadline_ExpiresTheTransactionAndTheExtension_NotTheOrder()
    {
        var f = new Fixture();
        var (extension, txn) = AddExtension(f, txnAgeMinutes: 15);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 1, 0, 0), result);
        Assert.Equal(PaymentStatus.Expired, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.Expired, extension.ExtStatus);
        Assert.Equal(JobOrderStatus.Assigned, f.Db.Orders[1].OrderStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task AnExtensionBeforeItsDeadline_IsQueriedButNotExpired()
    {
        var f = new Fixture();
        var (extension, txn) = AddExtension(f, txnAgeMinutes: 14);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Pending, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.PendingPayment, extension.ExtStatus);
        Assert.Equal([txn.GatewayTxnRef], f.Gateway.Queried);
    }

    [Fact]
    public async Task AnExtensionPaidWithAWrongAmount_IsNotMarkedPaid()
    {
        var f = new Fixture();
        var (extension, txn) = AddExtension(f, txnAgeMinutes: 5);
        f.Gateway.Statuses[txn.GatewayTxnRef] = (PaymentStatus.Success, 1m);

        var result = await f.Service().RunOnceAsync();

        Assert.Equal(new ReconciliationResult(0, 0, 0, 0), result);
        Assert.Equal(PaymentStatus.Pending, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.PendingPayment, extension.ExtStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task APaidExtensionThatIsNoLongerPending_KeepsTheMoney_ButGetsNoEvent()
    {
        var f = new Fixture();
        var (extension, txn) = AddExtension(f, txnAgeMinutes: 5, extStatus: ExtensionStatuses.Expired);
        f.Gateway.Statuses[txn.GatewayTxnRef] = (PaymentStatus.Success, 97500m);

        await f.Service().RunOnceAsync();

        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.Expired, extension.ExtStatus);
        Assert.Empty(f.Publisher.Published);
    }
}
