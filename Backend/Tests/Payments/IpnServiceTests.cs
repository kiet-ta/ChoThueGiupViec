using CommonService.Application.Features.Booking;
using System.Text.Json;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-05: gateway IPN (contract payments.md 2.4), with in-memory ports (no DB).</summary>
public sealed class IpnServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const string Ref = "FAKE-abc";
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
        private readonly List<object> _published = [];
        public IReadOnlyList<object> Published { get { lock (_published) { return _published.ToArray(); } } }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            lock (_published) { _published.Add(notification); }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly List<NotificationMessage> _sent = [];
        public bool Fail { get; set; }
        public IReadOnlyList<NotificationMessage> Sent { get { lock (_sent) { return _sent.ToArray(); } } }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("hub down");
            }

            lock (_sent) { _sent.Add(message); }
            return Task.CompletedTask;
        }
    }

    /// <summary>One lock makes the conditional update atomic like the SQL <c>WHERE txn_status = 'PENDING'</c>.</summary>
    private sealed class InMemoryPayments : IPaymentRepository, IUnitOfWork
    {
        private readonly object _gate = new();
        public Barrier? ReadBarrier { get; set; }
        public List<PaymentTransaction> Transactions { get; } = [];
        public Dictionary<long, JobOrder> Orders { get; } = [];

        public Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            var o = Orders.GetValueOrDefault(orderId);
            return Task.FromResult(o is null ? null : new OrderForPayment(o.OrderId, o.CustomerId, o.OrderCode, o.TotalAmount, o.OrderStatus, o.CreatedAt));
        }
        public Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(PaymentTransaction transaction) => throw new NotSupportedException();

        public Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default)
        {
            PaymentTransaction? snapshot;
            lock (_gate)
            {
                var found = Transactions.FirstOrDefault(t => t.GatewayTxnRef == gatewayTxnRef);
                snapshot = found is null ? null : Copy(found);
            }

            ReadBarrier?.SignalAndWait(TimeSpan.FromSeconds(5));
            return Task.FromResult(snapshot);
        }

        public Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default)
        {
            lock (_gate)
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
        }

        public Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default)
        {
            lock (_gate)
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
        }

        public Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentTransaction?> FindPendingExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingExtensionPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

        private static PaymentTransaction Copy(PaymentTransaction t) => new()
        {
            PaymentId = t.PaymentId,
            GatewayTxnRef = t.GatewayTxnRef,
            OrderId = t.OrderId,
            ExtensionId = t.ExtensionId,
            Purpose = t.Purpose,
            Gateway = t.Gateway,
            Amount = t.Amount,
            TxnStatus = t.TxnStatus,
            CreatedAt = t.CreatedAt,
        };
    }

    private sealed class Fixture
    {
        public InMemoryPayments Db { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public JobOrder Order { get; } = new()
        {
            OrderId = OrderId,
            OrderCode = "GV261014ABC123",
            CustomerId = CustomerId,
            AddressId = 3,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2026, 10, 15),
            ShiftCode = "SHIFT_MORNING",
            AreaSnapshotM2 = 50m,
            RequiredWorkers = 1,
            TotalAmount = Amount,
            CreatedAt = Now.AddMinutes(-5),
            UpdatedAt = Now.AddMinutes(-5),
        };

        public Fixture(PaymentStatus status = PaymentStatus.Pending, PaymentPurpose purpose = PaymentPurpose.Order)
        {
            Db.Orders[OrderId] = Order;
            Db.Transactions.Add(new PaymentTransaction
            {
                PaymentId = 1,
                GatewayTxnRef = Ref,
                OrderId = purpose == PaymentPurpose.Order ? OrderId : null,
                ExtensionId = purpose == PaymentPurpose.Extension ? 9 : null,
                Purpose = purpose,
                Gateway = "FAKE",
                Amount = Amount,
                TxnStatus = status,
                CreatedAt = Now.AddMinutes(-4),
            });
        }

        public PaymentTransaction Txn => Db.Transactions.Single();

        public IpnService Service() =>
            new(
                new FakePaymentGateway(), Db,
                new PaymentSettlementService(Db, Db, new TestClock(), Publisher, Notifications, NullLogger<PaymentSettlementService>.Instance),
                NullLogger<IpnService>.Instance);
    }

    private static (Dictionary<string, string> Payload, string Raw) Ipn(
        string status = "success", string signature = "valid", string reference = Ref, string amount = "260000") =>
        (new Dictionary<string, string> { ["gatewayTxnRef"] = reference, ["amount"] = amount, ["status"] = status, ["signature"] = signature },
         JsonSerializer.Serialize(new { gatewayTxnRef = reference, amount, status }));

    private static Task<IpnOutcome> Send(Fixture f, (Dictionary<string, string> Payload, string Raw) ipn) =>
        f.Service().HandleAsync(ipn.Payload, ipn.Raw);

    private static void AssertNothingChanged(Fixture f, PaymentStatus status = PaymentStatus.Pending)
    {
        Assert.Equal(status, f.Txn.TxnStatus);
        Assert.Null(f.Txn.PaidAt);
        Assert.Null(f.Txn.IpnPayload);
        Assert.Equal(JobOrderStatus.PendingPayment, f.Order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
        Assert.Empty(f.Notifications.Sent);
    }

    [Fact]
    public async Task Success_MarksTheTransactionAndTheOrderPaid_PublishesOrderPaid_AndPushesPaymentStatus()
    {
        var f = new Fixture();
        var ipn = Ipn();

        var outcome = await Send(f, ipn);

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
        Assert.Equal(Now, f.Txn.PaidAt);
        Assert.Equal(ipn.Raw, f.Txn.IpnPayload);
        Assert.Equal(JobOrderStatus.Paid, f.Order.OrderStatus);
        Assert.Equal(Now, f.Order.UpdatedAt);
        var paid = Assert.IsType<OrderPaid>(Assert.Single(f.Publisher.Published));
        Assert.Equal(new OrderPaid(OrderId, CustomerId, Amount, "SHIFT_MORNING", new DateTime(2026, 10, 15), 1), paid);
        var message = Assert.Single(f.Notifications.Sent);
        Assert.Equal(UserRole.Customer, message.RecipientRole);
        Assert.Equal(CustomerId, message.RecipientId);
        Assert.Equal("payment.status", message.Topic);
        Assert.Equal("SUCCESS", message.Data!["txnStatus"]);
        Assert.Equal("1", message.Data["paymentId"]);
        Assert.Equal("42", message.Data["orderId"]);
    }

    [Fact]
    public async Task Replay_OfTheSameIpn_ChangesNothingTheSecondTime()
    {
        var f = new Fixture();
        await Send(f, Ipn());
        var paidAt = f.Txn.PaidAt;
        var updatedAt = f.Order.UpdatedAt;

        var second = await Send(f, Ipn());

        Assert.Equal(IpnOutcome.AlreadyProcessed, second);
        Assert.Equal(paidAt, f.Txn.PaidAt);
        Assert.Equal(updatedAt, f.Order.UpdatedAt);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Notifications.Sent);
    }

    [Fact]
    public async Task WrongSignature_IsRejected_AndChangesNothing()
    {
        var f = new Fixture();

        var outcome = await Send(f, Ipn(signature: "forged"));

        Assert.Equal(IpnOutcome.Rejected, outcome);
        AssertNothingChanged(f);
    }

    [Theory]
    [InlineData("FAKE-unknown")]
    [InlineData("")]
    public async Task UnknownReference_IsRejected_AndChangesNothing(string reference)
    {
        var f = new Fixture();

        var outcome = await Send(f, Ipn(reference: reference));

        Assert.Equal(IpnOutcome.Rejected, outcome);
        AssertNothingChanged(f);
    }

    [Theory]
    [InlineData("259999")]
    [InlineData("260001")]
    [InlineData("0")]
    public async Task WrongAmount_IsRejected_AndNeverMarksPaid(string amount)
    {
        var f = new Fixture();

        var outcome = await Send(f, Ipn(amount: amount));

        Assert.Equal(IpnOutcome.Rejected, outcome);
        AssertNothingChanged(f);
    }

    [Fact]
    public async Task ExpiredStatus_ExpiresTheTransaction_AndLeavesTheOrderToTheReconciliationJob()
    {
        var f = new Fixture();

        var outcome = await Send(f, Ipn(status: "expired"));

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Expired, f.Txn.TxnStatus);
        Assert.Null(f.Txn.PaidAt);
        Assert.Equal(JobOrderStatus.PendingPayment, f.Order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
        Assert.Empty(f.Notifications.Sent);
    }

    [Fact]
    public async Task PendingStatus_ChangesNothing()
    {
        var f = new Fixture();

        var outcome = await Send(f, Ipn(status: "pending"));

        Assert.Equal(IpnOutcome.Accepted, outcome);
        AssertNothingChanged(f);
    }

    [Theory]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Success)]
    public async Task AnAlreadyFinishedTransaction_IsAnIdempotentNoOp(PaymentStatus status)
    {
        var f = new Fixture(status);

        var outcome = await Send(f, Ipn());

        Assert.Equal(IpnOutcome.AlreadyProcessed, outcome);
        Assert.Equal(status, f.Txn.TxnStatus);
        Assert.Equal(JobOrderStatus.PendingPayment, f.Order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task TwoConcurrentIdenticalIpns_GiveOneSuccessAndOneOrderPaid()
    {
        var f = new Fixture();
        f.Db.ReadBarrier = new Barrier(2); // both see PENDING before either updates

        var outcomes = await Task.WhenAll(Task.Run(() => Send(f, Ipn())), Task.Run(() => Send(f, Ipn())));

        Assert.Equal(1, outcomes.Count(o => o == IpnOutcome.Accepted));
        Assert.Equal(1, outcomes.Count(o => o == IpnOutcome.AlreadyProcessed));
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Notifications.Sent);
    }

    [Theory]
    [InlineData(PaymentPurpose.Subscription)]
    public async Task ASubscriptionTransaction_IsLeftUntouched_AndRejected(PaymentPurpose purpose)
    {
        var f = new Fixture(purpose: purpose);

        var outcome = await Send(f, Ipn());

        Assert.Equal(IpnOutcome.Rejected, outcome);
        AssertNothingChanged(f);
    }

    // ---- EXTENSION payments (BE-M2-08) ----

    private const decimal ExtensionAmount = 97500m;

    /// <summary>The fixture's order is ASSIGNED (a worker is on site) and extension 9 waits for its payment (<c>ext</c> txn, ref FAKE-ext).</summary>
    private static (Fixture F, JobOrderExtension Extension, PaymentTransaction Txn) ExtensionFixture(string extStatus = ExtensionStatuses.PendingPayment)
    {
        var f = new Fixture();
        f.Order.TransitionTo(JobOrderStatus.Paid);
        f.Order.TransitionTo(JobOrderStatus.Dispatching);
        f.Order.TransitionTo(JobOrderStatus.Assigned);
        var extension = new JobOrderExtension
        {
            ExtensionId = 9,
            OrderId = OrderId,
            WorkerId = 11,
            ExtraHours = 1.5m,
            ExtraAmount = ExtensionAmount,
            WorkerDecision = WorkerDecisions.Pending,
            ExtStatus = extStatus,
            RequestedAt = Now.AddMinutes(-6),
            CreatedAt = Now.AddMinutes(-6),
        };
        f.Db.Extensions[9] = extension;
        var txn = new PaymentTransaction
        {
            PaymentId = 2,
            GatewayTxnRef = "FAKE-ext",
            ExtensionId = 9,
            Purpose = PaymentPurpose.Extension,
            Gateway = "FAKE",
            Amount = ExtensionAmount,
            TxnStatus = PaymentStatus.Pending,
            CreatedAt = Now.AddMinutes(-5),
        };
        f.Db.Transactions.Add(txn);
        return (f, extension, txn);
    }

    private static (Dictionary<string, string> Payload, string Raw) ExtensionIpn(string amount = "97500", string signature = "valid") =>
        Ipn(reference: "FAKE-ext", amount: amount, signature: signature);

    [Fact]
    public async Task ExtensionSuccess_MarksTheExtensionPaid_PublishesExtensionPaid_AndLeavesTheOrderAlone()
    {
        var (f, extension, txn) = ExtensionFixture();
        var ipn = ExtensionIpn();

        var outcome = await Send(f, ipn);

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(Now, txn.PaidAt);
        Assert.Equal(ipn.Raw, txn.IpnPayload);
        Assert.Equal(ExtensionStatuses.Paid, extension.ExtStatus);
        Assert.Equal(JobOrderStatus.Assigned, f.Order.OrderStatus);
        var paid = Assert.IsType<ExtensionPaid>(Assert.Single(f.Publisher.Published));
        Assert.Equal(new ExtensionPaid(9, OrderId, 11, 1.5m, ExtensionAmount, Now), paid);
        var message = Assert.Single(f.Notifications.Sent);
        Assert.Equal("payment.status", message.Topic);
        Assert.Equal(CustomerId, message.RecipientId);
        Assert.Equal("9", message.Data!["extensionId"]);
        Assert.Equal("42", message.Data["orderId"]);
        Assert.Equal("SUCCESS", message.Data["txnStatus"]);
    }

    [Fact]
    public async Task ExtensionReplay_ChangesNothingTheSecondTime()
    {
        var (f, _, _) = ExtensionFixture();
        await Send(f, ExtensionIpn());

        var second = await Send(f, ExtensionIpn());

        Assert.Equal(IpnOutcome.AlreadyProcessed, second);
        Assert.Single(f.Publisher.Published);
        Assert.Single(f.Notifications.Sent);
    }

    [Theory]
    [InlineData("97499")]
    [InlineData("260000")]
    public async Task ExtensionWithAWrongAmount_OrASignature_IsRejected_AndNothingChanges(string amount)
    {
        var (f, extension, txn) = ExtensionFixture();

        var wrongAmount = await Send(f, ExtensionIpn(amount: amount));
        var wrongSignature = await Send(f, ExtensionIpn(signature: "forged"));

        Assert.Equal(IpnOutcome.Rejected, wrongAmount);
        Assert.Equal(IpnOutcome.Rejected, wrongSignature);
        Assert.Equal(PaymentStatus.Pending, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.PendingPayment, extension.ExtStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task ExtensionPaidAfterItExpired_RecordsTheMoney_ButDoesNotTouchTheExtensionOrPublish()
    {
        var (f, extension, txn) = ExtensionFixture(ExtensionStatuses.Expired);

        var outcome = await Send(f, ExtensionIpn());

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(ExtensionStatuses.Expired, extension.ExtStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task TwoConcurrentIdenticalExtensionIpns_GiveOneSuccessAndOneExtensionPaid()
    {
        var (f, _, txn) = ExtensionFixture();
        f.Db.ReadBarrier = new Barrier(2);

        var outcomes = await Task.WhenAll(Task.Run(() => Send(f, ExtensionIpn())), Task.Run(() => Send(f, ExtensionIpn())));

        Assert.Equal(1, outcomes.Count(o => o == IpnOutcome.Accepted));
        Assert.Equal(1, outcomes.Count(o => o == IpnOutcome.AlreadyProcessed));
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Single(f.Publisher.Published);
    }

    [Fact]
    public async Task LateSuccess_ForACancelledOrder_RecordsTheMoney_ButDoesNotTouchTheOrderOrPublish()
    {
        var f = new Fixture();
        f.Order.TransitionTo(JobOrderStatus.Cancelled);

        var outcome = await Send(f, Ipn());

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
        Assert.Equal(JobOrderStatus.Cancelled, f.Order.OrderStatus);
        Assert.Empty(f.Publisher.Published);
    }

    [Fact]
    public async Task AFailingPushNotification_DoesNotFailTheIpn()
    {
        var f = new Fixture();
        f.Notifications.Fail = true;

        var outcome = await Send(f, Ipn());

        Assert.Equal(IpnOutcome.Accepted, outcome);
        Assert.Equal(PaymentStatus.Success, f.Txn.TxnStatus);
        Assert.Single(f.Publisher.Published);
    }

    private sealed class StubIpn(IpnOutcome outcome) : IIpnService
    {
        public IReadOnlyDictionary<string, string>? LastPayload { get; private set; }
        public string? LastRaw { get; private set; }

        public Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default)
        {
            LastPayload = payload;
            LastRaw = rawBody;
            return Task.FromResult(outcome);
        }
    }

    [Fact]
    public async Task Controller_Is204WhenAcceptedOrRepeated_AsMoMoRequires_400WhenRejectedOrNotAnObject_AndPassesTheRawPairs()
    {
        var body = JsonSerializer.Deserialize<JsonElement>("""{"gatewayTxnRef":"FAKE-abc","amount":260000,"status":"success","signature":"valid"}""");
        var accepted = new StubIpn(IpnOutcome.Accepted);

        var ok = await new PaymentIpnController(accepted).Momo(body, default);
        var repeat = await new PaymentIpnController(new StubIpn(IpnOutcome.AlreadyProcessed)).Momo(body, default);
        var rejected = await new PaymentIpnController(new StubIpn(IpnOutcome.Rejected)).Momo(body, default);
        var notAnObject = await new PaymentIpnController(accepted).Momo(JsonSerializer.Deserialize<JsonElement>("[1]"), default);

        Assert.IsType<NoContentResult>(ok);
        Assert.IsType<NoContentResult>(repeat);
        Assert.Equal(400, ((ObjectResult)rejected).StatusCode);
        Assert.Equal(400, ((ObjectResult)notAnObject).StatusCode);
        Assert.Equal("260000", accepted.LastPayload!["amount"]);
        Assert.Equal("valid", accepted.LastPayload["signature"]);
        Assert.Equal(body.GetRawText(), accepted.LastRaw);
    }

    [Fact]
    public void Controller_IsAnonymous_BecauseTheCallerIsTheGateway()
    {
        Assert.NotEmpty(typeof(PaymentIpnController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(typeof(PaymentIpnController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }
}
