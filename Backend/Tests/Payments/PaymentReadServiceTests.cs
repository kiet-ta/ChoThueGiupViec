using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.WebAPI.Controllers.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Payments;

/// <summary>BE-M2-12: customer reads of payments (contract payments.md 2.3, shape 1.3).</summary>
public sealed class PaymentReadServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const int ExtensionId = 9;
    private static readonly DateTime OrderCreated = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    private sealed class Db : PaymentRepositoryStub
    {
        public List<PaymentTransaction> Transactions { get; } = [];
        public int OrderCustomer { get; set; } = CustomerId;

        public override Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Transactions.FirstOrDefault(t => t.PaymentId == paymentId));

        public override Task<IReadOnlyList<PaymentTransaction>> ListPaymentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentTransaction>>(Transactions
                .Where(t => (t.Purpose == PaymentPurpose.Order && t.OrderId == orderId) || (t.Purpose == PaymentPurpose.Extension && t.ExtensionId == ExtensionId && orderId == OrderId))
                .OrderByDescending(t => t.CreatedAt).ToList());

        public override Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderForPayment?>(orderId == OrderId
                ? new OrderForPayment(OrderId, OrderCustomer, "GV261014ABC123", 260000m, JobOrderStatus.Assigned, OrderCreated)
                : null);

        public override Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExtensionForPayment?>(extensionId == ExtensionId
                ? new ExtensionForPayment(ExtensionId, OrderId, "GV261014ABC123", OrderCustomer, 11, 1.5m, 97500m, "PAID")
                : null);
    }

    private static Db Seeded()
    {
        var db = new Db();
        db.Transactions.Add(new PaymentTransaction
        {
            PaymentId = 1,
            GatewayTxnRef = "SECRET-REF-1",
            OrderId = OrderId,
            Purpose = PaymentPurpose.Order,
            Gateway = "FAKE",
            Amount = 260000m,
            TxnStatus = PaymentStatus.Success,
            QrPayload = "qr-1",
            IpnPayload = "{\"raw\":1}",
            PaidAt = OrderCreated.AddMinutes(2),
            CreatedAt = OrderCreated.AddMinutes(1),
        });
        db.Transactions.Add(new PaymentTransaction
        {
            PaymentId = 2,
            GatewayTxnRef = "SECRET-REF-2",
            ExtensionId = ExtensionId,
            Purpose = PaymentPurpose.Extension,
            Gateway = "FAKE",
            Amount = 97500m,
            TxnStatus = PaymentStatus.Pending,
            QrPayload = "qr-2",
            CreatedAt = OrderCreated.AddHours(2),
        });
        db.Transactions.Add(new PaymentTransaction
        {
            PaymentId = 3,
            GatewayTxnRef = "SECRET-REF-3",
            SubscriptionId = 5,
            Purpose = PaymentPurpose.Subscription,
            Gateway = "FAKE",
            Amount = 1m,
            TxnStatus = PaymentStatus.Pending,
            CreatedAt = OrderCreated,
        });
        return db;
    }

    private static PaymentReadService Service(Db db) =>
        new(db, Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()));

    [Fact]
    public async Task Get_AnOrderPayment_HasTheContractShape_WithStringValues_AndNoQrOncePaid()
    {
        var payment = await Service(Seeded()).GetAsync(CustomerId, 1);

        Assert.Equal(1, payment.PaymentId);
        Assert.Equal("ORDER", payment.Purpose);
        Assert.Equal("SUCCESS", payment.TxnStatus);
        Assert.Equal(OrderId, payment.OrderId);
        Assert.Null(payment.ExtensionId);
        Assert.Equal(260000m, payment.Amount);
        Assert.Null(payment.QrPayload); // only while PENDING
        Assert.Null(payment.PayUrl);
        Assert.Equal(OrderCreated.AddMinutes(15), payment.ExpiresAt); // the order's deadline
        Assert.Equal(OrderCreated.AddMinutes(2), payment.PaidAt);
        Assert.True(payment.Sandbox);
    }

    [Fact]
    public async Task Get_APendingExtensionPayment_ShowsItsQr_AndExpiresFifteenMinutesAfterItsOwnCreation()
    {
        var payment = await Service(Seeded()).GetAsync(CustomerId, 2);

        Assert.Equal("EXTENSION", payment.Purpose);
        Assert.Equal("PENDING", payment.TxnStatus);
        Assert.Equal(ExtensionId, payment.ExtensionId);
        Assert.Equal("qr-2", payment.QrPayload);
        Assert.Equal(OrderCreated.AddHours(2).AddMinutes(15), payment.ExpiresAt);
    }

    [Fact]
    public async Task Get_IsNotFound_ForAnotherCustomersPayment_AnUnknownOne_AndASubscriptionPayment()
    {
        var db = Seeded();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetAsync(CustomerId + 1, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetAsync(CustomerId + 1, 2));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetAsync(CustomerId, 999));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetAsync(CustomerId, 3));
    }

    [Fact]
    public async Task List_ReturnsTheOrdersOrderAndExtensionPayments_NewestFirst_AndIsNotFoundForSomeoneElse()
    {
        var db = Seeded();

        var payments = await Service(db).ListForOrderAsync(CustomerId, OrderId);

        Assert.Equal([2L, 1L], payments.Select(p => p.PaymentId));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).ListForOrderAsync(CustomerId + 1, OrderId));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).ListForOrderAsync(CustomerId, 999));
    }

    [Fact]
    public void ThePaymentShape_NeverCarriesTheGatewayReferenceOrTheIpnPayload()
    {
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

    [Fact]
    public async Task Controller_Is200_400WithoutAnOrderId_401WithoutAUser_AndCustomerOnly()
    {
        var service = Service(Seeded());
        var controller = new PaymentsQueryController(service, new StubUser(CustomerId));
        var anonymous = new PaymentsQueryController(service, new StubUser(null));

        Assert.Equal(200, ((ObjectResult)await controller.Get(1, default)).StatusCode);
        Assert.Equal(200, ((ObjectResult)await controller.List(OrderId, default)).StatusCode);
        Assert.Equal(400, ((ObjectResult)await controller.List(null, default)).StatusCode);
        Assert.Equal(400, ((ObjectResult)await controller.List(0, default)).StatusCode);
        Assert.Equal(401, ((ObjectResult)await anonymous.Get(1, default)).StatusCode);
        Assert.Equal(401, ((ObjectResult)await anonymous.List(OrderId, default)).StatusCode);
        var authorize = typeof(PaymentsQueryController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("CustomerOnly", authorize.Policy);
    }
}
