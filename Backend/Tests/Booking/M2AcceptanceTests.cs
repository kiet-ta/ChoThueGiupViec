using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
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

namespace CommonService.Tests.Booking;

/// <summary>
/// BE-M2-11: the REAL Booking and Payments services wired together over one in-memory store and the existing Fakes
/// (no SQL Server, no HTTP host): IPN replay, the Premium last-position race, the 80 m2 price boundary, and "money is never lost".
/// </summary>
public sealed class M2AcceptanceTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc); // 07:00 local
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = Start;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    private sealed class RecordingPublisher : IPublisher
    {
        private readonly List<object> _published = [];
        public IReadOnlyList<object> Published { get { lock (_published) { return _published.ToArray(); } } }
        public int Count<T>() => Published.OfType<T>().Count();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            lock (_published) { _published.Add(notification); }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class NoNotifications : INotificationService
    {
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The Q01 DEFAULT prices of Backend/Infrastructure/Persistence/DefaultDataSeeder.cs.</summary>
    private sealed class DefaultPrices : IPriceRuleRepository
    {
        private readonly List<PriceRule> _rules =
        [
            new() { RuleId = 1, ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 160000m, IsActive = true },
            new() { RuleId = 2, ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.From31To80, UnitPrice = 260000m, IsActive = true },
            new() { RuleId = 3, ServiceTier = ServiceTier.Economy, AreaBracket = AreaBrackets.Over80, UnitPrice = 260000m, IsActive = true },
            new() { RuleId = 4, ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.UpTo30, UnitPrice = 240000m, IsActive = true },
            new() { RuleId = 5, ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.From31To80, UnitPrice = 390000m, IsActive = true },
            new() { RuleId = 6, ServiceTier = ServiceTier.Premium, AreaBracket = AreaBrackets.Over80, UnitPrice = 390000m, IsActive = true },
        ];

        public Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.FirstOrDefault(r => r.ServiceTier == serviceTier && r.AreaBracket == areaBracket && r.IsActive));

        public Task<IReadOnlyList<PriceRule>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PriceRule>>(_rules);
        public Task<PriceRule?> GetForUpdateAsync(int ruleId, CancellationToken cancellationToken = default) => Task.FromResult(_rules.FirstOrDefault(r => r.RuleId == ruleId));
    }

    /// <summary>
    /// JOB_ORDER and PAYMENT_TRANSACTION in memory. One lock makes every conditional update atomic like its SQL WHERE clause;
    /// a failing <see cref="ExecuteInTransactionAsync{TResult}"/> removes the rows it inserted (rollback). Status changes made on
    /// a tracked entity inside a failed transaction are not undone (the services under test throw before changing them).
    /// </summary>
    private sealed class Store : IOrderRepository, IPaymentRepository, IUnitOfWork
    {
        private readonly object _gate = new();
        private readonly AsyncLocal<List<Action>?> _undo = new();
        private long _nextOrderId = 1;
        private long _nextPaymentId = 1;
        private readonly List<JobOrder> _orders = [];
        private readonly List<PaymentTransaction> _transactions = [];

        public IReadOnlyList<JobOrder> Orders { get { lock (_gate) { return _orders.ToArray(); } } }
        public IReadOnlyList<PaymentTransaction> Transactions { get { lock (_gate) { return _transactions.ToArray(); } } }

        // ---- unit of work ----
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            var undo = new List<Action>();
            _undo.Value = undo;
            try
            {
                return await action();
            }
            catch
            {
                lock (_gate)
                {
                    for (var i = undo.Count - 1; i >= 0; i--)
                    {
                        undo[i]();
                    }
                }

                throw;
            }
            finally
            {
                _undo.Value = null;
            }
        }

        // ---- IOrderRepository ----
        public void Add(JobOrder order)
        {
            lock (_gate)
            {
                order.OrderId = _nextOrderId++;
                _orders.Add(order);
            }

            _undo.Value?.Add(() => _orders.Remove(order));
        }

        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default)
        {
            lock (_gate) { return Task.FromResult(_orders.Any(o => o.OrderCode == orderCode)); }
        }

        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default)
        {
            lock (_gate) { return Task.FromResult(_orders.FirstOrDefault(o => o.OrderId == orderId)); }
        }

        // ---- IPaymentRepository ----
        public Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var o = _orders.FirstOrDefault(x => x.OrderId == orderId);
                return Task.FromResult(o is null ? null : new OrderForPayment(o.OrderId, o.CustomerId, o.OrderCode, o.TotalAmount, o.OrderStatus, o.CreatedAt));
            }
        }

        public Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentTransaction>> ListPaymentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => GetForUpdateAsync(orderId, cancellationToken);

        public void Add(PaymentTransaction transaction)
        {
            lock (_gate)
            {
                transaction.PaymentId = _nextPaymentId++;
                _transactions.Add(transaction);
            }

            _undo.Value?.Add(() => _transactions.Remove(transaction));
        }

        private Task<PaymentTransaction?> Find(Func<PaymentTransaction, bool> predicate)
        {
            lock (_gate) { return Task.FromResult(_transactions.LastOrDefault(predicate)); }
        }

        public Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
            Find(t => t.OrderId == orderId && t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending);

        public Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) =>
            Find(t => t.GatewayTxnRef == gatewayTxnRef);

        public Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
            Find(t => t.OrderId == orderId && t.Purpose == PaymentPurpose.Order && t.TxnStatus is PaymentStatus.Success or PaymentStatus.Refunded);

        public Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var t = _transactions.Single(x => x.PaymentId == paymentId);
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
                var t = _transactions.Single(x => x.PaymentId == paymentId);
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
                var t = _transactions.Single(x => x.PaymentId == paymentId);
                if (t.TxnStatus != PaymentStatus.Pending)
                {
                    return Task.FromResult(false);
                }

                t.TxnStatus = PaymentStatus.Expired;
                t.IpnPayload = ipnPayload;
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var t = _transactions.Single(x => x.PaymentId == paymentId);
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
                var t = _transactions.Single(x => x.PaymentId == paymentId);
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

        public Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<PaymentTransaction>>(_transactions
                    .Where(t => t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending && t.CreatedAt <= cutoffUtc)
                    .OrderBy(t => t.CreatedAt).Take(take).ToList());
            }
        }

        public Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<long>>(_orders
                    .Where(o => o.OrderStatus == JobOrderStatus.PendingPayment && o.CreatedAt <= createdBeforeUtc)
                    .Where(o => !_transactions.Any(t => t.OrderId == o.OrderId && t.Purpose == PaymentPurpose.Order
                        && t.TxnStatus is PaymentStatus.Pending or PaymentStatus.Success))
                    .OrderBy(o => o.CreatedAt).Select(o => o.OrderId).Take(take).ToList());
            }
        }

        // Extensions are covered by their own tests (BE-M2-08); none exist in these flows.
        public Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) => Task.FromResult<ExtensionForPayment?>(null);
        public Task<PaymentTransaction?> FindPendingExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentTransaction?>(null);
        public Task<PaymentTransaction?> FindRefundableExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentTransaction?>(null);
        public Task<JobOrderExtension?> GetExtensionForUpdateAsync(int extensionId, CancellationToken cancellationToken = default) => Task.FromResult<JobOrderExtension?>(null);
        public Task<IReadOnlyList<PaymentTransaction>> ListPendingExtensionPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentTransaction>>([]);
    }

    /// <summary>Everything a customer journey touches, wired like the application wires it.</summary>
    private sealed class World
    {
        public Store Db { get; } = new();
        public MutableClock Clock { get; } = new();
        public RecordingPublisher Events { get; } = new();
        public FakePaymentGateway Gateway { get; } = new();
        public FakeAgencyCapacityService Capacity { get; } = new();
        public FakeCustomerAddressQuery Addresses { get; } = new();
        private readonly Microsoft.Extensions.Options.IOptions<BusinessRules> _rules = Microsoft.Extensions.Options.Options.Create(new BusinessRules());
        private readonly NoNotifications _notifications = new();

        public void AddAddress(int customerId, int addressId, decimal areaM2) =>
            Addresses.Add(customerId, new CustomerAddressInfo(addressId, areaM2, 10.76m, 106.66m));

        public PricingService Pricing() => new(new DefaultPrices());

        public OrderCreationService OrderCreation() => new(Addresses, Pricing(), Db, Capacity, Db, Clock, _rules);

        public PaymentQrService Qr() => new(Db, Gateway, Db, Clock, _rules);

        public PaymentSettlementService Settlement() =>
            new(Db, Db, Clock, Events, _notifications, Refunds(), Refunds(), NullLogger<PaymentSettlementService>.Instance);

        public IpnService Ipn() => new(Gateway, Db, Settlement(), NullLogger<IpnService>.Instance);

        public PaymentReconciliationService Reconciliation() =>
            new(Db, Gateway, Settlement(), Db, Capacity, Clock, Events, _rules, NullLogger<PaymentReconciliationService>.Instance);

        public RefundService Refunds() => new(Db, Gateway, Db, Clock, Events, _notifications, NullLogger<RefundService>.Instance);

        public OrderCancellationService Cancellation() =>
            new(Db, Db, Db, Refunds(), Capacity, Clock, Events, _rules, NullLogger<OrderCancellationService>.Instance);

        public AssignmentFailedHandler AssignmentFailed() =>
            new(Db, Db, Refunds(), Capacity, Clock, Events, NullLogger<AssignmentFailedHandler>.Instance);

        public Task<CreatedOrder> CreateOrderAsync(int customerId, int addressId, ServiceTier tier = ServiceTier.Economy) =>
            OrderCreation().CreateAsync(new CreateOrderRequest(customerId, addressId, tier, ShiftDate, BookingShifts.Morning, null, null));

        /// <summary>The IPN the Fake gateway would send for a transaction.</summary>
        public Task<IpnOutcome> SendIpnAsync(PaymentTransaction txn, decimal? amount = null, string signature = "valid", string status = "success")
        {
            var value = (amount ?? txn.Amount).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            var payload = new Dictionary<string, string>
            {
                ["gatewayTxnRef"] = txn.GatewayTxnRef,
                ["amount"] = value,
                ["status"] = status,
                ["signature"] = signature,
            };
            return Ipn().HandleAsync(payload, $"{{\"gatewayTxnRef\":\"{txn.GatewayTxnRef}\",\"amount\":{value},\"status\":\"{status}\"}}");
        }

        public JobOrder Order(long orderId) => Db.Orders.Single(o => o.OrderId == orderId);
        public PaymentTransaction TxnOf(long orderId) => Db.Transactions.Last(t => t.OrderId == orderId);
    }

    private const int Customer = 7;
    private const int Address = 3;

    private static async Task<(World W, CreatedOrder Order, PaymentTransaction Txn)> PaidUpToQrAsync(decimal areaM2 = 50m, ServiceTier tier = ServiceTier.Economy)
    {
        var w = new World();
        w.AddAddress(Customer, Address, areaM2);
        var order = await w.CreateOrderAsync(Customer, Address, tier);
        await w.Qr().CreateOrderQrAsync(Customer, order.OrderId);
        return (w, order, w.TxnOf(order.OrderId));
    }

    // ---------------------------------------------------------------- IPN replay

    [Fact]
    public async Task Order_Qr_Ipn_MarksTheOrderPaidOnce_AndAReplayedIpnChangesNothing()
    {
        var (w, order, txn) = await PaidUpToQrAsync();
        Assert.Equal(JobOrderStatus.PendingPayment, w.Order(order.OrderId).OrderStatus);

        var first = await w.SendIpnAsync(txn);
        var paidAt = txn.PaidAt;
        w.Clock.UtcNow = Start.AddMinutes(1);
        var replays = new List<IpnOutcome>();
        for (var i = 0; i < 5; i++)
        {
            replays.Add(await w.SendIpnAsync(txn));
        }

        Assert.Equal(IpnOutcome.Accepted, first);
        Assert.All(replays, r => Assert.Equal(IpnOutcome.AlreadyProcessed, r));
        Assert.Equal(JobOrderStatus.Dispatching, w.Order(order.OrderId).OrderStatus); // B4
        Assert.Equal(PaymentStatus.Success, txn.TxnStatus);
        Assert.Equal(paidAt, txn.PaidAt);
        var paid = Assert.Single(w.Events.Published.OfType<OrderPaid>());
        Assert.Equal(order.OrderId, paid.OrderId);
        Assert.Equal(Customer, paid.CustomerId);
        Assert.Equal(260000m, paid.Amount);
        Assert.Equal(1, paid.RequiredWorkers);
        Assert.Single(w.Db.Transactions);
    }

    [Fact]
    public async Task TheSameIpn_SentTwentyTimesAtOnce_PaysExactlyOnce()
    {
        var (w, order, txn) = await PaidUpToQrAsync();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => w.SendIpnAsync(txn))));

        Assert.Equal(1, outcomes.Count(o => o == IpnOutcome.Accepted));
        Assert.Equal(19, outcomes.Count(o => o == IpnOutcome.AlreadyProcessed));
        Assert.Equal(JobOrderStatus.Dispatching, w.Order(order.OrderId).OrderStatus); // B4
        Assert.Equal(1, w.Events.Count<OrderPaid>());
    }

    [Fact]
    public async Task AnIpnWithAWrongAmountOrSignature_NeverPaysTheOrder_AndTheRightOneStillWorksAfterwards()
    {
        var (w, order, txn) = await PaidUpToQrAsync();

        var less = await w.SendIpnAsync(txn, amount: 259999m);
        var more = await w.SendIpnAsync(txn, amount: 260001m);
        var forged = await w.SendIpnAsync(txn, signature: "forged");

        Assert.All(new[] { less, more, forged }, outcome => Assert.Equal(IpnOutcome.Rejected, outcome));
        Assert.Equal(JobOrderStatus.PendingPayment, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(PaymentStatus.Pending, txn.TxnStatus);
        Assert.Equal(0, w.Events.Count<OrderPaid>());

        Assert.Equal(IpnOutcome.Accepted, await w.SendIpnAsync(txn));
        Assert.Equal(JobOrderStatus.Dispatching, w.Order(order.OrderId).OrderStatus); // B4
    }

    [Fact]
    public async Task AskingForTheQrTwice_GivesOneTransaction_AndAfterPaymentTheQrIsRefused()
    {
        var (w, order, txn) = await PaidUpToQrAsync();

        var again = await w.Qr().CreateOrderQrAsync(Customer, order.OrderId);
        await w.SendIpnAsync(txn);
        var afterPayment = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => w.Qr().CreateOrderQrAsync(Customer, order.OrderId));

        Assert.False(again.Created);
        Assert.Equal(txn.PaymentId, again.Payment.PaymentId);
        Assert.Single(w.Db.Transactions);
        Assert.Equal(PaymentErrorCodes.InvalidState, afterPayment.Code);
    }

    // ---------------------------------------------------------------- price boundary (Q01 defaults, BR-01/02)

    [Theory]
    [InlineData(ServiceTier.Economy, "30", 1, AreaBrackets.UpTo30, 160000, 160000)]
    [InlineData(ServiceTier.Economy, "30.01", 1, AreaBrackets.From31To80, 260000, 260000)]
    [InlineData(ServiceTier.Economy, "80", 1, AreaBrackets.From31To80, 260000, 260000)]
    [InlineData(ServiceTier.Economy, "80.01", 2, AreaBrackets.Over80, 260000, 520000)]
    [InlineData(ServiceTier.Premium, "30", 1, AreaBrackets.UpTo30, 240000, 240000)]
    [InlineData(ServiceTier.Premium, "30.01", 1, AreaBrackets.From31To80, 390000, 390000)]
    [InlineData(ServiceTier.Premium, "80", 1, AreaBrackets.From31To80, 390000, 390000)]
    [InlineData(ServiceTier.Premium, "80.01", 2, AreaBrackets.Over80, 390000, 780000)]
    public async Task TheAreaBoundary_DecidesWorkersBracketAndTotal_AndThatTotalIsWhatIsPaid(
        ServiceTier tier, string area, int workers, string bracket, int unitPrice, int total)
    {
        var areaM2 = decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture);
        var (w, order, txn) = await PaidUpToQrAsync(areaM2, tier);
        var quote = await w.Pricing().QuoteAsync(tier, areaM2, workers);

        Assert.Equal(bracket, quote.AreaBracket);
        Assert.Equal(unitPrice, quote.UnitPrice);
        Assert.Equal(workers, order.RequiredWorkers);
        Assert.Equal(total, order.TotalAmount);
        Assert.Equal(areaM2, order.AreaSnapshotM2);
        Assert.Equal(total, w.Order(order.OrderId).TotalAmount); // frozen in JOB_ORDER
        Assert.Equal(total, txn.Amount); // the QR asks for exactly that
        Assert.Equal(total, Assert.Single(w.Gateway.Created).Amount);

        // Only the frozen total is accepted as payment.
        Assert.Equal(IpnOutcome.Rejected, await w.SendIpnAsync(txn, amount: total - 1));
        Assert.Equal(IpnOutcome.Accepted, await w.SendIpnAsync(txn));
        Assert.Equal(total, Assert.Single(w.Events.Published.OfType<OrderPaid>()).Amount);
        Assert.Equal(workers, Assert.Single(w.Events.Published.OfType<OrderPaid>()).RequiredWorkers);
    }

    // ---------------------------------------------------------------- Premium last-position race (PRD 2.5, Q13)

    [Fact]
    public async Task TwoCustomers_RacingForTheLastPremiumPosition_GiveOneOrderAndOneFullyBooked_EveryTime()
    {
        for (var round = 0; round < 25; round++)
        {
            var w = new World();
            w.Capacity.RemainingSlots = 1;
            w.AddAddress(1, 11, 50m);
            w.AddAddress(2, 22, 50m);
            using var go = new ManualResetEventSlim(false);

            Task<object> Attempt(int customer, int address) => Task.Run(async () =>
            {
                go.Wait();
                try
                {
                    return (object)await w.CreateOrderAsync(customer, address, ServiceTier.Premium);
                }
                catch (BusinessRuleViolationException ex)
                {
                    return ex;
                }
            });

            var a = Attempt(1, 11);
            var b = Attempt(2, 22);
            go.Set();
            var results = await Task.WhenAll(a, b);

            var created = results.OfType<CreatedOrder>().ToList();
            var refused = results.OfType<BusinessRuleViolationException>().ToList();
            Assert.Single(created);
            Assert.Equal(BookingErrorCodes.FullyBooked, Assert.Single(refused).Code);
            Assert.Equal(created[0].OrderId, Assert.Single(w.Db.Orders).OrderId); // the loser left no order behind
            Assert.Equal(0, w.Capacity.RemainingSlots); // the position was used once, not lost and not double-used
        }
    }

    [Fact]
    public async Task TwoLargeAreaPremiumOrders_WithThreePositionsLeft_OnlyOneGetsItsTwoWorkers()
    {
        for (var round = 0; round < 25; round++)
        {
            var w = new World();
            w.Capacity.RemainingSlots = 3;
            w.AddAddress(1, 11, 90m);
            w.AddAddress(2, 22, 90m);
            using var go = new ManualResetEventSlim(false);

            Task<bool> Attempt(int customer, int address) => Task.Run(async () =>
            {
                go.Wait();
                try
                {
                    await w.CreateOrderAsync(customer, address, ServiceTier.Premium);
                    return true;
                }
                catch (BusinessRuleViolationException ex) when (ex.Code == BookingErrorCodes.FullyBooked)
                {
                    return false;
                }
            });

            var a = Attempt(1, 11);
            var b = Attempt(2, 22);
            go.Set();
            var results = await Task.WhenAll(a, b);

            Assert.Equal(1, results.Count(r => r));
            Assert.Equal(2, Assert.Single(w.Db.Orders).RequiredWorkers);
            Assert.Equal(1, w.Capacity.RemainingSlots);
        }
    }

    [Fact]
    public async Task AnEconomyOrder_NeverUsesAgencyCapacity_EvenWhenNoneIsLeft()
    {
        var w = new World();
        w.Capacity.RemainingSlots = 0;
        w.AddAddress(Customer, Address, 50m);

        var order = await w.CreateOrderAsync(Customer, Address);

        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Equal(0, w.Capacity.RemainingSlots);
    }

    // ---------------------------------------------------------------- Premium hold released (booking.md 3.3 rule 6, payments.md 3.3)

    [Fact]
    public async Task APremiumOrderNobodyPays_GivesItsPositionsBack_WhenItsQrExpires_AndTheNextCustomerCanBook()
    {
        var w = new World();
        w.Capacity.RemainingSlots = 2;
        w.AddAddress(1, 11, 90m); // 2 workers: takes both positions
        w.AddAddress(2, 22, 90m);
        var first = await w.CreateOrderAsync(1, 11, ServiceTier.Premium);
        await w.Qr().CreateOrderQrAsync(1, first.OrderId);
        var refused = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => w.CreateOrderAsync(2, 22, ServiceTier.Premium));
        w.Clock.UtcNow = Start.AddMinutes(15);

        await w.Reconciliation().RunOnceAsync();
        await w.Reconciliation().RunOnceAsync(); // a second pass gives nothing back twice

        Assert.Equal(BookingErrorCodes.FullyBooked, refused.Code);
        Assert.Equal(JobOrderStatus.Cancelled, w.Order(first.OrderId).OrderStatus);
        Assert.Equal(2, w.Capacity.RemainingSlots);
        var second = await w.CreateOrderAsync(2, 22, ServiceTier.Premium);
        Assert.Equal(JobOrderStatus.PendingPayment, second.OrderStatus);
        Assert.Equal(0, w.Capacity.RemainingSlots);
    }

    [Fact]
    public async Task APremiumOrder_CancelledByItsCustomer_GivesItsPositionBack()
    {
        var (w, order, txn) = await PaidUpToQrAsync(tier: ServiceTier.Premium);
        await w.SendIpnAsync(txn);
        Assert.Equal(99, w.Capacity.RemainingSlots);

        await w.Cancellation().CancelAsync(Customer, order.OrderId, new CancelOrderRequest("Change of plans"));

        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(100, w.Capacity.RemainingSlots);
    }

    [Fact]
    public async Task APremiumOrder_CancelledAfterAssignmentFailed_GivesItsPositionBack_Once()
    {
        var (w, order, txn) = await PaidUpToQrAsync(tier: ServiceTier.Premium);
        await w.SendIpnAsync(txn);
        var failed = new AssignmentFailed(order.OrderId, "No agency could staff the shift", w.Clock.UtcNow);

        await w.AssignmentFailed().Handle(failed, default);
        await w.AssignmentFailed().Handle(failed, default); // delivered twice

        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(100, w.Capacity.RemainingSlots);
    }

    // ---------------------------------------------------------------- money is never lost

    [Fact]
    public async Task ALostIpn_IsRecoveredByTheReconciliationJob_AndTheLateIpnThenChangesNothing()
    {
        var (w, order, txn) = await PaidUpToQrAsync();
        w.Gateway.MarkPaid(txn.GatewayTxnRef); // the customer paid, the IPN never arrived
        w.Clock.UtcNow = Start.AddMinutes(4);

        var result = await w.Reconciliation().RunOnceAsync();
        var late = await w.SendIpnAsync(txn);

        Assert.Equal(new ReconciliationResult(1, 0, 0, 0), result);
        Assert.Equal(JobOrderStatus.Dispatching, w.Order(order.OrderId).OrderStatus); // B4
        Assert.Equal(IpnOutcome.AlreadyProcessed, late);
        Assert.Equal(1, w.Events.Count<OrderPaid>());
    }

    [Fact]
    public async Task AnOrderNobodyPays_IsCancelledAfterFifteenMinutes_WithNoCharge_AndCannotBePaidAnyMore()
    {
        var (w, order, txn) = await PaidUpToQrAsync();
        w.Clock.UtcNow = Start.AddMinutes(15);

        var result = await w.Reconciliation().RunOnceAsync();
        var qr = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => w.Qr().CreateOrderQrAsync(Customer, order.OrderId));

        Assert.Equal(new ReconciliationResult(0, 1, 1, 0), result);
        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal("PAYMENT_EXPIRED", w.Order(order.OrderId).CancelReason);
        Assert.Equal(PaymentStatus.Expired, txn.TxnStatus);
        Assert.Equal(0m, txn.RefundedAmount);
        Assert.Equal(1, w.Events.Count<OrderCancelled>());
        Assert.Equal(0, w.Events.Count<OrderRefunded>());
        Assert.Equal(PaymentErrorCodes.InvalidState, qr.Code);
    }

    [Fact]
    public async Task APaidOrder_CancelledByItsCustomer_IsRefunded100Percent_ExactlyOnce()
    {
        var (w, order, txn) = await PaidUpToQrAsync(areaM2: 90m); // 2 workers, 520,000
        await w.SendIpnAsync(txn);

        var cancel = await w.Cancellation().CancelAsync(Customer, order.OrderId, new CancelOrderRequest("Change of plans"));
        var second = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            w.Cancellation().CancelAsync(Customer, order.OrderId, new CancelOrderRequest("again")));

        Assert.Null(cancel.RefundProblem);
        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(PaymentStatus.Refunded, txn.TxnStatus);
        Assert.Equal(520000m, txn.RefundedAmount);
        Assert.Equal("Change of plans", txn.RefundReason);
        var refunded = Assert.Single(w.Events.Published.OfType<OrderRefunded>());
        Assert.Equal(520000m, refunded.Amount);
        Assert.Equal(1, w.Events.Count<OrderCancelled>());
        Assert.Equal(CancelErrorCodes.InvalidState, second.Code);
    }

    [Fact]
    public async Task APaidOrder_ThatDispatchCannotStaff_IsCancelledAndRefunded100Percent_ExactlyOnce()
    {
        var (w, order, txn) = await PaidUpToQrAsync();
        await w.SendIpnAsync(txn);
        var failed = new AssignmentFailed(order.OrderId, "No worker within 10 km", w.Clock.UtcNow);

        await w.AssignmentFailed().Handle(failed, default);
        await w.AssignmentFailed().Handle(failed, default); // delivered twice

        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal("No worker within 10 km", w.Order(order.OrderId).CancelReason);
        Assert.Equal(PaymentStatus.Refunded, txn.TxnStatus);
        Assert.Equal(260000m, txn.RefundedAmount);
        Assert.Equal(1, w.Events.Count<OrderCancelled>());
        Assert.Equal(1, w.Events.Count<OrderRefunded>());
    }

    /// <summary>
    /// Decision Q24 / P4 (BE-M2-13): the customer cancels an unpaid order (its QR is closed), then pays anyway. The money was received,
    /// so it is recorded and refunded 100 % with PAID_AFTER_EXPIRY; the order stays cancelled and Dispatch is never started.
    /// </summary>
    [Fact]
    public async Task AnUnpaidOrder_CancelledByItsCustomer_ThenPaidAnyway_GetsTheMoneyBack_AndStaysCancelled()
    {
        var (w, order, txn) = await PaidUpToQrAsync();

        await w.Cancellation().CancelAsync(Customer, order.OrderId, new CancelOrderRequest("Mistake"));
        Assert.Equal(PaymentStatus.Expired, txn.TxnStatus);
        var late = await w.SendIpnAsync(txn);
        var replay = await w.SendIpnAsync(txn);

        Assert.Equal(IpnOutcome.Accepted, late);
        Assert.Equal(IpnOutcome.AlreadyProcessed, replay);
        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(PaymentStatus.Refunded, txn.TxnStatus);
        Assert.Equal(260000m, txn.RefundedAmount);
        Assert.Equal("PAID_AFTER_EXPIRY", txn.RefundReason);
        Assert.Equal(0, w.Events.Count<OrderPaid>());
        var refunded = Assert.Single(w.Events.Published.OfType<OrderRefunded>());
        Assert.Equal(260000m, refunded.Amount);
    }

    [Fact]
    public async Task AnOrderExpiredByTheJob_ThenPaidAnyway_GetsTheMoneyBack_AndIsNotDispatched()
    {
        var (w, order, txn) = await PaidUpToQrAsync();
        w.Clock.UtcNow = Start.AddMinutes(15);
        await w.Reconciliation().RunOnceAsync(); // cancels the order and expires the QR

        var late = await w.SendIpnAsync(txn);

        Assert.Equal(IpnOutcome.Accepted, late);
        Assert.Equal(JobOrderStatus.Cancelled, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(PaymentStatus.Refunded, txn.TxnStatus);
        Assert.Equal(260000m, txn.RefundedAmount);
        Assert.Equal(0, w.Events.Count<OrderPaid>());
        Assert.Equal(1, w.Events.Count<OrderRefunded>());
    }

    [Fact]
    public async Task AfterPayment_TheOrderIsAlreadyDispatching_WhenOrderPaidIsPublished()
    {
        var (w, order, txn) = await PaidUpToQrAsync();

        await w.SendIpnAsync(txn);

        // B4: nobody ever observes PAID outside the settlement transaction.
        Assert.Equal(JobOrderStatus.Dispatching, w.Order(order.OrderId).OrderStatus);
        Assert.Equal(1, w.Events.Count<OrderPaid>());
        var qr = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => w.Qr().CreateOrderQrAsync(Customer, order.OrderId));
        Assert.Equal(PaymentErrorCodes.InvalidState, qr.Code);
    }
}
