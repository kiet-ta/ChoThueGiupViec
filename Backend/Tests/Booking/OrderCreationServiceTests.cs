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

/// <summary>BE-M2-03: POST /api/v1/booking/orders rules in the contract's order, with in-memory ports (no DB).</summary>
public class OrderCreationServiceTests
{
    private const int CustomerId = 7;
    private const int AddressId = 3;
    private static readonly DateOnly ShiftDate = new(2026, 10, 15);
    private static readonly DateTime Now = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private class VietnamClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    /// <summary>Order table and unit of work in one: Save assigns ids, a thrown transaction drops what it added.</summary>
    private class InMemoryOrders : IOrderRepository, IUnitOfWork
    {
        private long _nextId = 100;
        private readonly List<JobOrder> _pending = [];
        public List<JobOrder> Saved { get; } = [];
        public bool FailOnCommit { get; set; }
        public HashSet<string> TakenCodes { get; } = [];

        public void Add(JobOrder order) => _pending.Add(order);

        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.SingleOrDefault(o => o.CustomerId == customerId && o.OrderId == orderId));
        public Task<IReadOnlyList<JobAssignmentStatus>> GetAssignmentStatusesAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
    private class StubPricing : IPricingService
    {
        public Task<PriceQuote> QuoteAsync(ServiceTier serviceTier, decimal totalAreaM2, int requiredWorkers, CancellationToken cancellationToken = default, int? priceUnits = null, bool isContinuousWork = false) =>
            Task.FromResult(PriceCalculator.Calculate(serviceTier, totalAreaM2, requiredWorkers, 260000m, priceUnits, isContinuousWork));
    }

    private class Fixture
    {
        public InMemoryOrders Orders { get; } = new();
        public FakeCustomerAddressQuery Addresses { get; } = new();
        public FakeAgencyCapacityService Capacity { get; } = new();
        public RecordingAvailability Availability { get; } = new();
        public BusinessRules Rules { get; } = new();
        public DateTime NowUtc { get; set; } = Now;

        public Fixture(decimal areaM2 = 50m) =>
            Addresses.Add(CustomerId, new CustomerAddressInfo(AddressId, areaM2, 10.76m, 106.66m));

        public OrderCreationService Service(
            ICustomerFavoriteQuery? favorites = null, IOrderTrackingQuery? tracking = null, IWorkerFreeShiftQuery? freeShifts = null) =>
            new(Addresses, new StubPricing(), Orders, Capacity, Orders, new VietnamClock(NowUtc), Microsoft.Extensions.Options.Options.Create(Rules),
                Availability, favorites, tracking, freeShifts);
    }

    private class RecordingAvailability : IWorkerAvailabilityQuery
    {
        public List<AvailableWorker> Workers { get; } = [new(42, 11, 1.0)];
        public List<AvailabilityQuery> Queries { get; } = [];

        public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers);
        }
    }

    /// <summary>The customer's favorites, as the Customers module would answer through the port.</summary>
    private class StubFavorites(params int[] workerIds) : ICustomerFavoriteQuery
    {
        public Task<bool> IsFavoriteAsync(int customerId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(customerId == CustomerId && workerIds.Contains(workerId));
    }

    private class StubTracking(params AssignmentProgress[] assignments) : IOrderTrackingQuery
    {
        public Task<IReadOnlyList<AssignmentProgress>> GetOrderProgressAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssignmentProgress>>(assignments);

        public Task<IReadOnlyList<CustomerAssignmentSummary>> GetCustomerHistoryAsync(int customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerAssignmentSummary>>([]);
    }

    [Fact]
    public async Task PreviousCompletedWorkerCanBeRebookedWithoutBeingAFavorite()
    {
        var f = new Fixture();
        f.Orders.Saved.Add(new JobOrder { OrderId = 42, CustomerId = CustomerId });
        var tracking = new StubTracking(new AssignmentProgress(11, 1, 58, JobAssignmentStatus.Completed, null, null));
        var request = Request(preferredWorkerId: 58) with { PreviousOrderId = 42 };

        var created = await f.Service(new StubFavorites(), tracking).CreateAsync(request);

        Assert.NotEqual(42, created.OrderId);
        Assert.Equal(58, f.Orders.Saved.Single(o => o.OrderId == created.OrderId).PreferredWorkerId);
    }

    [Fact]
    public async Task PreviousWorkerMustBelongToAnOwnedOrderAndHaveACompletedAssignment()
    {
        var f = new Fixture();
        f.Orders.Saved.Add(new JobOrder { OrderId = 42, CustomerId = CustomerId + 1 });
        var tracking = new StubTracking(new AssignmentProgress(11, 1, 58, JobAssignmentStatus.Completed, null, null));
        var request = Request(preferredWorkerId: 58) with { PreviousOrderId = 42 };
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service(new StubFavorites(), tracking).CreateAsync(request));

        f.Orders.Saved[0].CustomerId = CustomerId;
        var unfinished = new StubTracking(new AssignmentProgress(11, 1, 58, JobAssignmentStatus.InProgress, null, null));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service(new StubFavorites(), unfinished).CreateAsync(request));
    }

    private static CreateOrderRequest Request(
        ServiceTier tier = ServiceTier.Economy, DateOnly? date = null, string shift = BookingShifts.Morning,
        string? note = null, string? skill = null, int addressId = AddressId, int? preferredWorkerId = null, bool acceptsContinuousWork = false) =>
        new(CustomerId, addressId, tier, date ?? ShiftDate, shift, note, skill, preferredWorkerId, AcceptsContinuousWork: acceptsContinuousWork);

    private static async Task<BusinessRuleViolationException> ConflictAsync(Task task) =>
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => task);

    [Fact]
    public async Task Economy_WithoutAvailableFreelancers_AllowsOrderCreation()
    {
        var f = new Fixture();
        f.Availability.Workers.Clear();

        var order = await f.Service().CreateAsync(Request());

        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Single(f.Orders.Saved);
    }

    [Fact]
    public async Task EconomyLargeHouse_IsOneWorkerPricedAsTwoUnits_WhenTheCustomerAgrees_D36()
    {
        var f = new Fixture(90m);

        var order = await f.Service().CreateAsync(Request(acceptsContinuousWork: true));

        Assert.Equal(1, order.RequiredWorkers);
        Assert.Equal(520000, order.TotalAmount);
        Assert.True(order.IsContinuousWork);
        Assert.Single(f.Orders.Saved);
    }

    [Fact]
    public async Task EconomyLargeHouse_WithoutTheCustomersAgreement_IsA400_AndNothingIsWritten()
    {
        var f = new Fixture(90m);

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Request()));

        Assert.Contains("acceptsContinuousWork", error.Errors.Keys);
        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task EconomyLargeHouse_CanBeBookedInTheEveningShift_BecauseTheSecondJobIsExtraHours_D36()
    {
        var f = new Fixture(90m);

        var order = await f.Service().CreateAsync(Request(shift: BookingShifts.Evening, acceptsContinuousWork: true));

        Assert.True(order.IsContinuousWork);
        Assert.Single(f.Orders.Saved);
    }

    [Fact]
    public async Task EconomyLargeHouse_TimedJob_IsPricedPerHourAsTwoUnits_D34D36()
    {
        var f = new Fixture(90m);
        f.NowUtc = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

        // 3 hours of a 4 h shift: 260000 / 4 x 3 = 195000 per unit, 2 units = 390000 (before D36 it was 2 workers x 195000).
        var order = await f.Service().CreateAsync(
            Request(acceptsContinuousWork: true) with { StartTime = new TimeOnly(8, 30), DurationHours = 3m });

        Assert.Equal(1, order.RequiredWorkers);
        Assert.Equal(390000, order.TotalAmount);
        Assert.True(order.IsContinuousWork);
        Assert.Equal(new TimeOnly(8, 30), order.StartTime);
        Assert.Equal(new TimeOnly(11, 30), order.EndTime);
    }

    [Fact]
    public async Task EconomySmallHouse_NeedsNoAgreement_AndIsNotContinuous()
    {
        var f = new Fixture(50m);

        var order = await f.Service().CreateAsync(Request(shift: BookingShifts.Evening));

        Assert.Equal(1, order.RequiredWorkers);
        Assert.False(order.IsContinuousWork);
    }

    [Fact]
    public async Task Economy_FavoriteWorker_IsSavedOnOrder()
    {
        var f = new Fixture();
        await f.Service(new StubFavorites(77)).CreateAsync(Request(preferredWorkerId: 77));
        Assert.Equal(77, Assert.Single(f.Orders.Saved).PreferredWorkerId);
    }

    [Fact]
    public async Task Premium_SkipsFreelancerAvailability()
    {
        var f = new Fixture();
        f.Availability.Workers.Clear();

        var order = await f.Service().CreateAsync(Request(ServiceTier.Premium));

        Assert.Equal(JobOrderStatus.PendingPayment, order.OrderStatus);
        Assert.Empty(f.Availability.Queries);
    }

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
    [InlineData(80.01, 1, 520000)]
    public async Task AreaOver80_EconomyIsOneWorkerAndTheLargeHousePrice(double area, int workers, int total)
    {
        var f = new Fixture((decimal)area);

        var order = await f.Service().CreateAsync(Request(acceptsContinuousWork: true));

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

    [Fact]
    public async Task Economy_WithAFavoriteWorker_StoresThePreferredWorker()
    {
        var f = new Fixture();

        await f.Service(new StubFavorites(42)).CreateAsync(Request(preferredWorkerId: 42));

        Assert.Equal(42, Assert.Single(f.Orders.Saved).PreferredWorkerId);
    }

    [Fact]
    public async Task PreferredWorker_NotAFavorite_IsNotFound_AndNothingIsWritten()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service(new StubFavorites(42)).CreateAsync(Request(preferredWorkerId: 7)));

        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task PreferredWorker_OnAPremiumOrder_IsAValidationError()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => f.Service(new StubFavorites(42)).CreateAsync(Request(ServiceTier.Premium, preferredWorkerId: 42)));

        Assert.True(error.Errors.ContainsKey("preferredWorkerId"));
        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task WithoutAPreferredWorker_NothingIsStored()
    {
        var f = new Fixture();

        await f.Service().CreateAsync(Request());

        Assert.Null(Assert.Single(f.Orders.Saved).PreferredWorkerId);
    }

    // ---- Decisions D34 (BOOK-TIME-01): a timed Economy job inside the shift ----

    private static CreateOrderRequest Timed(string start, decimal hours, ServiceTier tier = ServiceTier.Economy, string shift = BookingShifts.Morning) =>
        Request(tier, shift: shift) with { StartTime = TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DurationHours = hours };

    [Fact]
    public async Task TimedJob_SavesItsWindow_AndIsPricedPerHour()
    {
        var f = new Fixture(50m);

        var order = await f.Service().CreateAsync(Timed("08:30", 3m));

        var saved = Assert.Single(f.Orders.Saved);
        Assert.Equal(new TimeOnly(8, 30), saved.StartTime);
        Assert.Equal(new TimeOnly(11, 30), saved.EndTime);
        Assert.Equal(195000m, order.TotalAmount); // 260,000 / 4 h x 3 h
        Assert.Equal(new DateTime(2026, 10, 15, 1, 30, 0, DateTimeKind.Utc), order.ShiftStartAt);
        Assert.Equal(new DateTime(2026, 10, 15, 4, 30, 0, DateTimeKind.Utc), order.ShiftEndAt);
        Assert.Equal(new TimeOnly(8, 30), order.StartTime);
    }

    [Fact]
    public async Task TimedJob_LargeEconomyHouse_IsOneWorkerPaidTwoUnitsOfTheHours_D36()
    {
        var f = new Fixture(90m);

        var order = await f.Service().CreateAsync(Timed("13:00", 2m, shift: BookingShifts.Afternoon) with { AcceptsContinuousWork = true });

        Assert.Equal(1, order.RequiredWorkers);
        Assert.Equal(260000m, order.TotalAmount); // 130,000 x 2 units: the same price as the 2 workers of D34
    }

    [Fact]
    public async Task TimedJob_PremiumLargeHouse_StaysTwoWorkers()
    {
        var f = new Fixture(90m);

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service().CreateAsync(Timed("13:00", 2m, ServiceTier.Premium, BookingShifts.Afternoon)));

        Assert.Contains("startTime", error.Errors.Keys); // PREMIUM + a time is a 400 (D34), unchanged
    }

    [Theory]
    [InlineData("08:15", 2, "startTime")]   // not on a 30-minute step
    [InlineData("10:30", 2, "startTime")]   // ends 12:30, after the morning shift
    [InlineData("07:30", 2, "startTime")]   // before the shift
    [InlineData("08:00", 1.5, "durationHours")] // shorter than 2 h
    [InlineData("08:00", 2.25, "durationHours")] // not on a 0.5 h step
    public async Task TimedJob_OutsideTheRules_IsAValidationError(string start, double hours, string field)
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Timed(start, (decimal)hours)));

        Assert.Contains(field, error.Errors.Keys);
        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task TimedJob_IsEconomyOnly()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(Timed("08:00", 2m, ServiceTier.Premium)));

        Assert.Contains("startTime", error.Errors.Keys);
    }

    [Fact]
    public async Task TimedJob_StartTimeWithoutDuration_IsAValidationError()
    {
        var f = new Fixture();

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service().CreateAsync(Request() with { StartTime = new TimeOnly(9, 0) }));
    }

    [Fact]
    public async Task TimedJob_LessThanAnHourAhead_IsLeadTimeTooShort()
    {
        // 07:30 local on the shift day; 08:00 is only 30 minutes away.
        var f = new Fixture { NowUtc = new DateTime(2026, 10, 15, 0, 30, 0, DateTimeKind.Utc) };

        var error = await ConflictAsync(f.Service().CreateAsync(Timed("08:00", 2m)));

        Assert.Equal(BookingErrorCodes.LeadTimeTooShort, error.Code);
        Assert.Empty(f.Orders.Saved);
        var later = await f.Service().CreateAsync(Timed("09:00", 2m)); // 90 minutes ahead is fine
        Assert.Equal(new TimeOnly(9, 0), later.StartTime);
    }

    [Fact]
    public async Task WithoutATime_TheOrderStaysAWholeShift()
    {
        var f = new Fixture(50m);

        var order = await f.Service().CreateAsync(Request());

        var saved = Assert.Single(f.Orders.Saved);
        Assert.Null(saved.StartTime);
        Assert.Null(saved.EndTime);
        Assert.Equal(260000m, order.TotalAmount);
    }

    // ---- BOOK-BUSY-01 (decisions D41): a preferred worker who is busy at the chosen time is refused ----

    private static FakeWorkerFreeShiftQuery FreeWorker(int workerId, params BusyWindow[] busy)
    {
        var free = new FakeWorkerFreeShiftQuery();
        free.Add(workerId, new WorkerFreeShift(ShiftDate, BookingShifts.Morning, busy, 8m));
        return free;
    }

    [Fact]
    public async Task PreferredWorker_BusyAtTheTime_Is409AndNothingIsWritten()
    {
        var f = new Fixture(50m);
        // A live job 08:00-10:30 widened by 30 minutes: 07:30-11:00.
        var free = FreeWorker(42, new BusyWindow("07:30", "11:00"));

        var error = await ConflictAsync(f.Service(new StubFavorites(42), freeShifts: free).CreateAsync(
            Timed("09:00", 2m) with { PreferredWorkerId = 42 }));

        Assert.Equal(BookingErrorCodes.PreferredWorkerBusy, error.Code);
        Assert.Empty(f.Orders.Saved);
    }

    [Theory]
    [InlineData("08:00")] // the same time as the other job
    [InlineData("10:00")] // overlaps its end
    public async Task PreferredWorker_OverlappingAnyPartOfTheirJob_IsRefused(string start)
    {
        var f = new Fixture(50m);
        var free = FreeWorker(42, new BusyWindow("07:30", "11:00"));

        var error = await ConflictAsync(f.Service(new StubFavorites(42), freeShifts: free).CreateAsync(
            Timed(start, 2m) with { PreferredWorkerId = 42 }));

        Assert.Equal(BookingErrorCodes.PreferredWorkerBusy, error.Code);
    }

    [Fact]
    public async Task PreferredWorker_FreeAtTheTime_GetsTheOrder()
    {
        var f = new Fixture(50m);
        // The shift is open and the worker has no live job that day.
        var free = FreeWorker(42);

        await f.Service(new StubFavorites(42), freeShifts: free).CreateAsync(Timed("09:00", 2m) with { PreferredWorkerId = 42 });

        Assert.Equal(42, Assert.Single(f.Orders.Saved).PreferredWorkerId);
    }

    [Fact]
    public async Task PreferredWorker_WithoutThatShiftOpen_IsRefused()
    {
        var f = new Fixture(50m);
        var free = new FakeWorkerFreeShiftQuery(); // the worker has no free shift at all

        var error = await ConflictAsync(f.Service(new StubFavorites(42), freeShifts: free).CreateAsync(
            Request(preferredWorkerId: 42)));

        Assert.Equal(BookingErrorCodes.PreferredWorkerBusy, error.Code);
        Assert.Empty(f.Orders.Saved);
    }

    [Fact]
    public async Task NoPreferredWorker_IsNeverChecked()
    {
        var f = new Fixture(50m);
        var free = new FakeWorkerFreeShiftQuery();

        await f.Service(freeShifts: free).CreateAsync(Timed("09:00", 2m));

        Assert.Single(f.Orders.Saved);
    }

    [Fact]
    public async Task PreferredWorker_WithAJobUntil10_GetsAnotherOrderFrom10InTheSameShift()
    {
        // SHIFT-MULTI-01 (decisions D42): one shift, several orders; the first job ends at 10:00 and the new one starts at 10:00.
        var f = new Fixture(50m);
        var free = FreeWorker(42, new BusyWindow("08:00", "10:00"));

        await f.Service(new StubFavorites(42), freeShifts: free).CreateAsync(Timed("10:00", 2m) with { PreferredWorkerId = 42 });

        Assert.Equal(new TimeOnly(10, 0), Assert.Single(f.Orders.Saved).StartTime);
    }
}
