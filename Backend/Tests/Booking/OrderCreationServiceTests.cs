using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-03: POST /api/booking/orders rules in the contract's order, with in-memory ports (no DB).</summary>
public sealed class OrderCreationServiceTests
{
    private const int CustomerId = 7;
    private const int AddressId = 3;
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly DateTime Now = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private sealed class VietnamClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    /// <summary>Order table and unit of work in one: Save assigns ids, a thrown transaction drops what it added.</summary>
    private sealed class InMemoryOrders : IOrderRepository, IUnitOfWork
    {
        private long _nextId = 100;
        private readonly List<JobOrder> _pending = [];
        public List<JobOrder> Saved { get; } = [];
        public bool FailOnCommit { get; set; }
        public HashSet<string> TakenCodes { get; } = [];

        public void Add(JobOrder order) => _pending.Add(order);

        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(TakenCodes.Contains(orderCode));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var order in _pending)
            {
                order.OrderId = _nextId++;
                Saved.Add(order);
            }

            var count = _pending.Count;
            _pending.Clear();
            return Task.FromResult(count);
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            var before = Saved.Count;
            try
            {
                var result = await action();
                if (FailOnCommit)
                {
                    throw new InvalidOperationException("commit failed");
                }

                return result;
            }
            catch
            {
                Saved.RemoveRange(before, Saved.Count - before);
                _pending.Clear();
                throw;
            }
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>260,000 per worker per shift, like the Q01 default of a mid bracket; the real lookup is covered by BE-M2-02 tests.</summary>
    private sealed class StubPricing : IPricingService
    {
        public Task<PriceQuote> QuoteAsync(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, CancellationToken cancellationToken = default) =>
            Task.FromResult(PriceCalculator.Calculate(serviceTier, totalAreaM2, requiredWorkers, 260000m));
    }

    private sealed class Fixture
    {
        public InMemoryOrders Orders { get; } = new();
        public FakeCustomerAddressQuery Addresses { get; } = new();
        public FakeAgencyCapacityService Capacity { get; } = new();
        public DateTime NowUtc { get; set; } = Now;

        public Fixture(decimal areaM2 = 50m) =>
            Addresses.Add(CustomerId, new CustomerAddressInfo(AddressId, areaM2, 10.76m, 106.66m));

        public OrderCreationService Service() =>
            new(Addresses, new StubPricing(), Orders, Capacity, Orders, new VietnamClock(NowUtc), Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
    }

    private static CreateOrderRequest Request(
        ServiceTier tier = ServiceTier.Economy, DateOnly? date = null, string shift = BookingShifts.Morning,
        string? note = null, string? skill = null, int addressId = AddressId) =>
        new(CustomerId, addressId, tier, date ?? ShiftDate, shift, note, skill);

    private static async Task<BusinessRuleViolationException> ConflictAsync(Task task) =>
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => task);

    [Fact]
    public async Task Economy_CreatesAPendingPaymentOrder_WithTheFrozenPriceAndShiftTimes()
    {
        var f = new Fixture(50m);

        var order = await f.Service().CreateAsync(Request(note: "bring a ladder"));

        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Equal(1, order.RequiredWorkers);
        Assert.Equal(260000m, order.TotalAmount);
        Assert.Equal(50m, order.AreaSnapshotM2);
        Assert.Equal(new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc), order.ShiftStartAt);
        Assert.Equal(new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc), order.ShiftEndAt);
        Assert.Equal(Now.AddMinutes(15), order.PaymentDeadlineAt);
        Assert.Equal(Now, order.CreatedAt);
        Assert.Matches("^GV261014[A-Z0-9]{6}$", order.OrderCode);
        var saved = Assert.Single(f.Orders.Saved);
        Assert.Equal(order.OrderId, saved.OrderId);
        Assert.Equal(CustomerId, saved.CustomerId);
        Assert.Equal("bring a ladder", saved.CustomerNote);
    }

    [Fact]
    public async Task Economy_DoesNotTouchTheAgencyCapacity()
    {
        var f = new Fixture();

        await f.Service().CreateAsync(Request());

        Assert.Equal(100, f.Capacity.RemainingSlots);
    }

    [Theory]
    [InlineData(80, 1, 260000)]
    [InlineData(80.01, 2, 520000)]
    public async Task AreaOver80_NeedsTwoWorkers_AndThePriceIsPerWorker(double area, int workers, int total)
    {
        var f = new Fixture((decimal)area);

        var order = await f.Service().CreateAsync(Request());

        Assert.Equal(workers, order.RequiredWorkers);
        Assert.Equal(total, order.TotalAmount);
    }

    [Fact]
    public async Task AddressOfAnotherCustomer_IsNotFound_AndNothingIsWritten()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateAsync(Request(addressId: 999)));

        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task Premium_ReservesCapacityForTheNewOrder_PerWorker()
    {
        var f = new Fixture(90m);

        var order = await f.Service().CreateAsync(Request(ServiceTier.Premium, skill: "deep clean"));

        Assert.Equal(2, order.RequiredWorkers);
        Assert.Equal(98, f.Capacity.RemainingSlots);
        Assert.Equal("deep clean", order.RequiredSkill);
        Assert.Single(f.Orders.Saved);
    }

    [Fact]
    public async Task Premium_WithoutCapacity_IsFullyBooked_AndLeavesNoOrder()
    {
        var f = new Fixture();
        f.Capacity.RemainingSlots = 0;

        var error = await ConflictAsync(f.Service().CreateAsync(Request(ServiceTier.Premium)));

        Assert.Equal(BookingErrorCodes.FullyBooked, error.Code);
        Assert.Empty(f.Orders.Saved);
        Assert.Equal(0, f.Capacity.RemainingSlots);
    }

    [Fact]
    public async Task Premium_WhenTheCommitFailsAfterTheReservation_TheHoldIsReleased()
    {
        var f = new Fixture();
        f.Orders.FailOnCommit = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().CreateAsync(Request(ServiceTier.Premium)));

        Assert.Empty(f.Orders.Saved);
        Assert.Equal(100, f.Capacity.RemainingSlots);
    }

    [Fact]
    public async Task ShiftInThePast_Is409ShiftInPast()
    {
        var f = new Fixture();
        f.NowUtc = new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc); // exactly the 08:00 start

        var error = await ConflictAsync(f.Service().CreateAsync(Request()));

        Assert.Equal(BookingErrorCodes.ShiftInPast, error.Code);
        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task Premium_LessThanFourHoursAhead_Is409PremiumLeadTime_ButExactlyFourIsAccepted()
    {
        var tooLate = new Fixture { NowUtc = new DateTime(2026, 10, 14, 21, 1, 0, DateTimeKind.Utc) };
        var exact = new Fixture { NowUtc = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc) };

        var error = await ConflictAsync(tooLate.Service().CreateAsync(Request(ServiceTier.Premium)));
        var order = await exact.Service().CreateAsync(Request(ServiceTier.Premium));

        Assert.Equal(BookingErrorCodes.PremiumLeadTime, error.Code);
        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
    }

    [Fact]
    public async Task Economy_ThreeHoursAhead_IsAccepted()
    {
        var f = new Fixture { NowUtc = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc) };

        var order = await f.Service().CreateAsync(Request());

        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
    }

    [Fact]
    public async Task BookingHorizon_IsFourteenDaysAhead_ForBothTiers()
    {
        var f = new Fixture();
        var lastDay = new DateOnly(2026, 10, 28);

        var accepted = await f.Service().CreateAsync(Request(date: lastDay));
        var economy = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request(date: lastDay.AddDays(1))));
        var premium = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request(ServiceTier.Premium, lastDay.AddDays(1))));

        Assert.Equal(lastDay, accepted.ScheduledDate);
        Assert.Contains("scheduledDate", economy.Errors.Keys);
        Assert.Contains("scheduledDate", premium.Errors.Keys);
    }

    [Fact]
    public async Task Validation_ReportsEveryBadFieldAndWritesNothing()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(
            Request(shift: "SANG", note: new string('x', 501), skill: "deep clean", addressId: 0)));

        Assert.Contains("addressId", error.Errors.Keys);
        Assert.Contains("shiftCode", error.Errors.Keys);
        Assert.Contains("customerNote", error.Errors.Keys);
        Assert.Contains("requiredSkill", error.Errors.Keys);
        Assert.Empty(f.Orders.Saved);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public async Task CustomerNote_IsLimitedTo500Characters(int length, bool accepted)
    {
        var f = new Fixture();
        var call = () => f.Service().CreateAsync(Request(note: new string('x', length)));

        if (accepted)
        {
            Assert.NotNull(await call());
        }
        else
        {
            await Assert.ThrowsAsync<ValidationException>(call);
        }
    }

    [Fact]
    public async Task RequiredSkill_IsLimitedTo100Characters_AndOnlyForPremium()
    {
        var f = new Fixture();

        await f.Service().CreateAsync(Request(ServiceTier.Premium, skill: new string('s', 100)));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request(ServiceTier.Premium, skill: new string('s', 101))));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request(ServiceTier.Economy, skill: "deep clean")));
    }

    [Fact]
    public async Task OrderCode_IsRegeneratedWhileItIsTaken_ThenGivesUp()
    {
        var f = new Fixture();
        var first = await f.Service().CreateAsync(Request());
        f.Orders.TakenCodes.Add(first.OrderCode);

        var second = await f.Service().CreateAsync(Request());

        Assert.NotEqual(first.OrderCode, second.OrderCode);
    }

    [Fact]
    public async Task InvalidServiceTier_IsAValidationError()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request((ServiceTier)99)));

        Assert.Contains("serviceTier", error.Errors.Keys);
    }

    [Fact]
    public void BookingRules_DefaultHorizonIs14Days() => Assert.Equal(14, new BusinessRules().Booking.MaxDaysAhead);
}
