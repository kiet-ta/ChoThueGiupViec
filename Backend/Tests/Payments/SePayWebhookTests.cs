using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommonService.Infrastructure.Modules.Payments.SePay;
using CommonService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class SePayWebhookTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const string Ref = "TA0123456789";
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

        public Task<bool> TryMarkSuccessFromExpiredAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) => MarkSuccessFromExpired(paymentId, paidAtUtc, ipnPayload);

        private Task<bool> MarkSuccessFromExpired(long paymentId, DateTime paidAtUtc, string ipnPayload)
        {
            lock (_gate)
            {
                var t = Transactions.Single(x => x.PaymentId == paymentId);
                if (t.TxnStatus != PaymentStatus.Expired)
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
        public Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPaymentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public RecordingLateRefunds Refunds { get; } = new();
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
                Gateway = "SEPAY",
                Amount = Amount,
                TxnStatus = status,
                CreatedAt = Now.AddMinutes(-4),
            });
        }

        public PaymentTransaction Txn => Db.Transactions.Single();

        public IpnService Service() =>
            new(
                new SePayPaymentGateway(new HttpClient(), new SePayOptions { WebhookSecret = Secret }, new WebhookClock()), Db,
                new PaymentSettlementService(Db, Db, new TestClock(), Publisher, Notifications, Refunds, Refunds, NullLogger<PaymentSettlementService>.Instance),
                NullLogger<IpnService>.Instance);
    }

    private const string Secret = "fake-webhook-secret";
    private const string Body = "{\"transferType\":\"in\",\"code\":\"TA0123456789\",\"transferAmount\":260000}";

    private sealed class WebhookClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class RecordingIpn : IIpnService
    {
        public IReadOnlyDictionary<string, string>? Payload { get; private set; }
        public string? Raw { get; private set; }

        public Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default)
        {
            Payload = payload;
            Raw = rawBody;
            return Task.FromResult(IpnOutcome.Accepted);
        }
    }

    [Fact]
    public async Task Controller_PassesRawBodyFieldsAndActualHeaders()
    {
        const string raw = "{ \"string\":\"hello\", \"number\":12, \"none\":null, \"x-sepay-signature\":\"fake-body-value\" }";
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        http.Request.Headers["X-SePay-Signature"] = "fake-header-signature";
        http.Request.Headers["X-SePay-Timestamp"] = "123";
        var service = new RecordingIpn();
        var controller = new PaymentIpnController(service) { ControllerContext = new ControllerContext { HttpContext = http } };
        Assert.IsType<OkObjectResult>(await controller.SePay(default));
        Assert.Equal(raw, service.Raw);
        Assert.Equal(raw, service.Payload!["x-sepay-raw-body"]);
        Assert.Equal("hello", service.Payload["string"]);
        Assert.Equal("12", service.Payload["number"]);
        Assert.Equal("", service.Payload["none"]);
        Assert.Equal("fake-header-signature", service.Payload["x-sepay-signature"]);
        Assert.Equal("123", service.Payload["x-sepay-timestamp"]);
    }

    private static string Sign(string body, long timestamp)
    {
        var bytes = Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}");
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bytes));
    }

    private static async Task<(int Status, string Body)> Send(Fixture fixture, string body = Body, string? signature = null, long? timestamp = null, bool headers = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/payments/ipn/sepay";
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var seconds = timestamp ?? new DateTimeOffset(Now).ToUnixTimeSeconds();
        if (headers)
        {
            context.Request.Headers["X-SePay-Timestamp"] = seconds.ToString(CultureInfo.InvariantCulture);
            context.Request.Headers["X-SePay-Signature"] = signature ?? Sign(body, seconds);
        }
        using var services = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        var middleware = new ResponseWrapperMiddleware(async http =>
        {
            var controller = new PaymentIpnController(fixture.Service()) { ControllerContext = new ControllerContext { HttpContext = http } };
            var result = await controller.SePay(default);
            await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        });
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SignedCallback_SettlesOrder_AndReplayDoesNotSettleAgain()
    {
        var fixture = new Fixture();
        Assert.Equal((200, "{\"success\":true}"), await Send(fixture));
        Assert.Equal(PaymentStatus.Success, fixture.Txn.TxnStatus);
        Assert.Equal(JobOrderStatus.Dispatching, fixture.Order.OrderStatus);
        Assert.Equal(Body, fixture.Txn.IpnPayload);
        Assert.Single(fixture.Publisher.Published);
        Assert.Equal((200, "{\"success\":true}"), await Send(fixture));
        Assert.Single(fixture.Publisher.Published);
    }

    [Fact]
    public async Task InvalidCallbacks_ReturnExactFailureAndLeavePaymentUnchanged()
    {
        var fixture = new Fixture();
        var seconds = new DateTimeOffset(Now).ToUnixTimeSeconds();
        var cases = new[]
        {
            await Send(fixture, signature: Sign(Body, seconds).Replace("sha256=", "sha256=00")),
            await Send(fixture, timestamp: seconds - 301),
            await Send(fixture, headers: false),
            await Send(fixture, body: Body.Replace(Ref, "TA9999999999")),
            await Send(fixture, body: Body.Replace("260000", "260001")),
            await Send(fixture, body: Body.Replace(",", ", "), signature: Sign(Body, seconds)),
            await Send(fixture, body: "[1]"),
            await Send(fixture, body: "not json"),
        };
        Assert.All(cases, result => Assert.Equal((400, "{\"success\":false}"), result));
        Assert.Equal(PaymentStatus.Pending, fixture.Txn.TxnStatus);
        Assert.Equal(JobOrderStatus.PendingPayment, fixture.Order.OrderStatus);
        Assert.Empty(fixture.Publisher.Published);
        Assert.Empty(fixture.Refunds.OrderRefunds);
    }

    [Fact]
    public async Task LateCallback_RecordsAndRefundsOnceWithoutRevivingOrder()
    {
        var fixture = new Fixture(PaymentStatus.Expired);
        fixture.Order.TransitionTo(JobOrderStatus.Cancelled);
        Assert.Equal((200, "{\"success\":true}"), await Send(fixture));
        Assert.Equal((200, "{\"success\":true}"), await Send(fixture));
        Assert.Equal(PaymentStatus.Success, fixture.Txn.TxnStatus);
        Assert.Equal(JobOrderStatus.Cancelled, fixture.Order.OrderStatus);
        Assert.Equal(new RefundRequest(OrderId, Amount, "PAID_AFTER_EXPIRY"), Assert.Single(fixture.Refunds.OrderRefunds));
        Assert.Empty(fixture.Publisher.Published);
    }
}

