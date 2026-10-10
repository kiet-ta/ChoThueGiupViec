using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Booking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-12: the customer's booking endpoints (contract booking.md 3.1 to 3.6), with in-memory ports (no DB).</summary>
public sealed class OrderQueryServiceTests
{
    private const int CustomerId = 7;
    private const int AddressId = 3;
    private static readonly DateTime Now = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private sealed class Orders : IOrderRepository
    {
        public List<JobOrder> Items { get; } = [];

        public Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(o => o.OrderId == orderId && o.CustomerId == customerId));

        public Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(int customerId, JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = Items.Where(o => o.CustomerId == customerId && (status is null || o.OrderStatus == status)).ToList();
            IReadOnlyList<JobOrder> page = query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.OrderId).Skip(skip).Take(take).ToList();
            return Task.FromResult((page, query.Count));
        }

        public void Add(JobOrder order) => throw new NotSupportedException();
        public Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Tracking : IOrderTrackingQuery
    {
        public List<AssignmentProgress> Rows { get; } = [];
        public Task<IReadOnlyList<AssignmentProgress>> GetOrderProgressAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AssignmentProgress>>(Rows);
        public Task<IReadOnlyList<CustomerAssignmentSummary>> GetCustomerHistoryAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Extensions : IExtensionRepository
    {
        public JobOrderExtension? Extension { get; set; }
        public Task<JobOrderExtension?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default) => Task.FromResult(Extension);
        public Task<OrderForExtension?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AssignmentForExtension?> GetAssignmentOfOrderAsync(long orderId, long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExtensionExistsAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(JobOrderExtension extension) => throw new NotSupportedException();
    }

    private sealed class DefaultPrices : IPriceRuleRepository
    {
        public Task<PriceRule?> GetActiveAsync(ServiceTier serviceTier, string areaBracket, CancellationToken cancellationToken = default)
        {
            decimal price = (serviceTier, areaBracket) switch
            {
                (ServiceTier.Economy, AreaBrackets.UpTo30) => 160000m,
                (ServiceTier.Economy, _) => 260000m,
                (ServiceTier.Premium, AreaBrackets.UpTo30) => 240000m,
                _ => 390000m,
            };
            return Task.FromResult<PriceRule?>(new PriceRule { ServiceTier = serviceTier, AreaBracket = areaBracket, UnitPrice = price, IsActive = true });
        }

        public Task<IReadOnlyList<PriceRule>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PriceRule?> GetForUpdateAsync(int ruleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingCreation : IOrderCreationService
    {
        public CreateOrderRequest? Last { get; private set; }

        public Task<CreatedOrder> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
        {
            Last = request;
            return Task.FromResult(new CreatedOrder(
                100, "GV261014ABC123", request.ServiceTier, request.AddressId, request.ScheduledDate, request.ShiftCode,
                Now.AddDays(1), Now.AddDays(1).AddHours(4), 50m, 1, request.RequiredSkill, 260000m, JobOrderStatus.PendingPayment,
                request.CustomerNote, null, Now.AddMinutes(15), Now, Now));
        }
    }

    private sealed class Fixture
    {
        public Orders Db { get; } = new();
        public Tracking Track { get; } = new();
        public Extensions Ext { get; } = new();
        public FakeCustomerAddressQuery Addresses { get; } = new();
        public FakeWorkerProfileQuery Workers { get; } = new();
        public RecordingCreation Creation { get; } = new();

        public Fixture() => Addresses.Add(CustomerId, new CustomerAddressInfo(AddressId, 90m, 10.76m, 106.66m));

        public OrderQueryService Service() => new(
            Db, Track, Ext, Addresses, new PricingService(new DefaultPrices()), Creation, Workers, new TestClock(),
            Microsoft.Extensions.Options.Options.Create(new BusinessRules()));

        public JobOrder AddOrder(long id, int customerId = CustomerId, JobOrderStatus status = JobOrderStatus.PendingPayment, int ageMinutes = 5)
        {
            var order = new JobOrder
            {
                OrderId = id,
                OrderCode = $"GV{id}",
                CustomerId = customerId,
                AddressId = AddressId,
                ServiceTier = ServiceTier.Premium,
                ScheduledDate = new DateOnly(2026, 10, 15),
                ShiftCode = BookingShifts.Evening,
                AreaSnapshotM2 = 90m,
                RequiredWorkers = 2,
                TotalAmount = 780000m,
                CustomerNote = "note",
                CreatedAt = Now.AddMinutes(-ageMinutes),
                UpdatedAt = Now.AddMinutes(-ageMinutes),
            };
            foreach (var step in new[] { JobOrderStatus.Paid, JobOrderStatus.Dispatching, JobOrderStatus.Assigned, JobOrderStatus.Completed })
            {
                if (status == JobOrderStatus.PendingPayment)
                {
                    break;
                }

                if (status == JobOrderStatus.Cancelled)
                {
                    order.TransitionTo(JobOrderStatus.Cancelled);
                    break;
                }

                order.TransitionTo(step);
                if (step == status)
                {
                    break;
                }
            }

            Db.Items.Add(order);
            return order;
        }
    }

    [Fact]
    public void Options_ComeFromBusinessRules_WithTheThreeShiftsAndBothTiers()
    {
        var options = new Fixture().Service().GetOptions();

        Assert.Equal(["ECONOMY", "PREMIUM"], options.ServiceTiers);
        Assert.Equal(
            [new ShiftOptionDto("SHIFT_MORNING", "08:00", "12:00"), new ShiftOptionDto("SHIFT_AFTERNOON", "13:00", "17:00"), new ShiftOptionDto("SHIFT_EVENING", "17:30", "20:30")],
            options.Shifts);
        Assert.Equal(4, options.PremiumMinLeadHours);
        Assert.Equal(4, options.ShiftMaxHours);
        Assert.Equal(80m, options.StandardMaxAreaM2);
        Assert.Equal(15, options.PaymentQrExpiryMinutes);
        Assert.True(options.Sandbox);
    }

    [Fact]
    public async Task PriceQuote_ShowsTheFixedPrice_ForTheCallersAddress_AndCreatesNothing()
    {
        var f = new Fixture();

        var quote = await f.Service().QuoteAsync(CustomerId, AddressId, "PREMIUM");

        Assert.Equal(new PriceQuoteDto(AddressId, "PREMIUM", 90m, AreaBrackets.Over80, 2, 390000m, 780000m, "VND"), quote);
        Assert.Null(f.Creation.Last);
    }

    [Theory]
    [InlineData(0, "ECONOMY", "addressId")]
    [InlineData(3, "economy", "serviceTier")]
    [InlineData(3, "GOLD", "serviceTier")]
    [InlineData(3, null, "serviceTier")]
    public async Task PriceQuote_RejectsABadInput(int addressId, string? tier, string field)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => new Fixture().Service().QuoteAsync(CustomerId, addressId, tier));

        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public async Task PriceQuote_ForAnotherCustomersAddress_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => new Fixture().Service().QuoteAsync(CustomerId + 1, AddressId, "ECONOMY"));
        await Assert.ThrowsAsync<NotFoundException>(() => new Fixture().Service().QuoteAsync(CustomerId, 999, "ECONOMY"));
    }

    [Fact]
    public async Task Create_PassesTheBodyToOrderCreation_AndAnswersTheOrderWithStringEnums()
    {
        var f = new Fixture();
        var body = new CreateOrderBody(AddressId, "PREMIUM", new DateOnly(2026, 10, 15), "SHIFT_EVENING", "ring the bell", "deep clean");

        var order = await f.Service().CreateAsync(CustomerId, body);

        Assert.Equal(new CreateOrderRequest(CustomerId, AddressId, ServiceTier.Premium, new DateOnly(2026, 10, 15), "SHIFT_EVENING", "ring the bell", "deep clean"), f.Creation.Last);
        Assert.Equal("PREMIUM", order.ServiceTier);
        Assert.Equal("PENDING_PAYMENT", order.OrderStatus);
        Assert.Equal(100, order.OrderId);
        Assert.Equal(Now.AddMinutes(15), order.PaymentDeadlineAt);
    }

    [Theory]
    [InlineData(null, "2026-10-15", "serviceTier")]
    [InlineData("premium", "2026-10-15", "serviceTier")]
    [InlineData("PREMIUM", null, "scheduledDate")]
    public async Task Create_RejectsAMissingTierOrDate_BeforeCallingOrderCreation(string? tier, string? date, string field)
    {
        var f = new Fixture();
        var body = new CreateOrderBody(AddressId, tier, date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), "SHIFT_EVENING", null, null);

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(CustomerId, body));

        Assert.Contains(field, error.Errors.Keys);
        Assert.Null(f.Creation.Last);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersOrders_NewestFirst_WithDefaultPaging()
    {
        var f = new Fixture();
        f.AddOrder(1, ageMinutes: 30);
        f.AddOrder(2, ageMinutes: 10);
        f.AddOrder(3, customerId: CustomerId + 1, ageMinutes: 1);

        var page = await f.Service().ListAsync(CustomerId, null, null, null);

        Assert.Equal([2L, 1L], page.Items.Select(o => o.OrderId));
        Assert.Equal((1, 20, 2), (page.Page, page.PageSize, page.Total));
        Assert.Equal(new OrderSummaryDto(2, "GV2", "PREMIUM", new DateOnly(2026, 10, 15), "SHIFT_EVENING", 2, 780000m, "PENDING_PAYMENT", Now.AddMinutes(-10)), page.Items[0]);
    }

    [Fact]
    public async Task List_FiltersByStatus_AndPages()
    {
        var f = new Fixture();
        f.AddOrder(1, status: JobOrderStatus.Paid, ageMinutes: 30);
        f.AddOrder(2, status: JobOrderStatus.Cancelled, ageMinutes: 20);
        f.AddOrder(3, status: JobOrderStatus.Paid, ageMinutes: 10);

        var paid = await f.Service().ListAsync(CustomerId, "PAID", null, null);
        var secondPage = await f.Service().ListAsync(CustomerId, null, 2, 2);

        Assert.Equal([3L, 1L], paid.Items.Select(o => o.OrderId));
        Assert.Equal(2, paid.Total);
        Assert.Equal([1L], secondPage.Items.Select(o => o.OrderId));
        Assert.Equal((2, 2, 3), (secondPage.Page, secondPage.PageSize, secondPage.Total));
    }

    [Theory]
    [InlineData("paid", 1, 20, "status")]
    [InlineData("DONE", 1, 20, "status")]
    [InlineData(null, 0, 20, "page")]
    [InlineData(null, 1, 0, "pageSize")]
    [InlineData(null, 1, 101, "pageSize")]
    public async Task List_RejectsAnUnknownStatusAndBadPaging(string? status, int page, int pageSize, string field)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => new Fixture().Service().ListAsync(CustomerId, status, page, pageSize));

        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public async Task List_AcceptsThePageSizeLimitOf100()
    {
        var page = await new Fixture().Service().ListAsync(CustomerId, null, 1, 100);

        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task Get_ReturnsTheCallersOrder_WithShiftTimesInUtc_AndThePaymentDeadlineOnlyWhileUnpaid()
    {
        var f = new Fixture();
        f.AddOrder(1);
        f.AddOrder(2, status: JobOrderStatus.Paid);

        var unpaid = await f.Service().GetAsync(CustomerId, 1);
        var paid = await f.Service().GetAsync(CustomerId, 2);

        Assert.Equal("PREMIUM", unpaid.ServiceTier);
        Assert.Equal("PENDING_PAYMENT", unpaid.OrderStatus);
        Assert.Equal(new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc), unpaid.ShiftStartAt); // 17:30 local
        Assert.Equal(new DateTime(2026, 10, 15, 13, 30, 0, DateTimeKind.Utc), unpaid.ShiftEndAt); // 20:30 local
        Assert.Equal(Now.AddMinutes(-5).AddMinutes(15), unpaid.PaymentDeadlineAt);
        Assert.Equal("PAID", paid.OrderStatus);
        Assert.Null(paid.PaymentDeadlineAt);
    }

    [Fact]
    public async Task Get_AndProgress_AreNotFound_ForAnotherCustomersOrder()
    {
        var f = new Fixture();
        f.AddOrder(1, customerId: CustomerId + 1);

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().GetAsync(CustomerId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().GetProgressAsync(CustomerId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().GetAsync(CustomerId, 999));
    }

    [Fact]
    public async Task Progress_HidesOfferedCancelledAndReassignedSeats_AndTakesWorkerNamesFromThePort()
    {
        var f = new Fixture();
        f.AddOrder(1, status: JobOrderStatus.Assigned);
        f.Workers.Add(new WorkerProfileSummary(11, "Nguyen Van A", 4.8m, 120, WorkStatus.Busy));
        f.Track.Rows.AddRange(
        [
            new AssignmentProgress(100, 1, 11, JobAssignmentStatus.InProgress, Now.AddMinutes(-60), null),
            new AssignmentProgress(101, 2, 12, JobAssignmentStatus.Assigned, Now.AddMinutes(-50), null),
            new AssignmentProgress(102, 2, 13, JobAssignmentStatus.Offered, null, null),
            new AssignmentProgress(103, 2, 14, JobAssignmentStatus.Cancelled, null, null),
            new AssignmentProgress(104, 2, 15, JobAssignmentStatus.Reassigned, Now.AddMinutes(-90), null),
        ]);

        var progress = await f.Service().GetProgressAsync(CustomerId, 1);

        Assert.Equal(1, progress.OrderId);
        Assert.Equal("ASSIGNED", progress.OrderStatus);
        Assert.Equal(2, progress.RequiredWorkers);
        Assert.Equal([100L, 101L], progress.Assignments.Select(a => a.AssignmentId));
        Assert.Equal(new ProgressAssignmentDto(100, 1, 11, "Nguyen Van A", 4.8m, "IN_PROGRESS", Now.AddMinutes(-60), null), progress.Assignments[0]);
        Assert.Null(progress.Assignments[1].WorkerName); // unknown to the port: no name, rating 0
        Assert.Equal(0m, progress.Assignments[1].WorkerRatingAvg);
        Assert.Equal("ASSIGNED", progress.Assignments[1].AssignmentStatus);
        Assert.Null(progress.Extension);
    }

    [Fact]
    public async Task Progress_IncludesTheExtension_AndIsEmptyBeforeAnyWorkerAccepted()
    {
        var f = new Fixture();
        f.AddOrder(1, status: JobOrderStatus.Dispatching);
        f.Ext.Extension = new JobOrderExtension
        {
            ExtensionId = 9,
            OrderId = 1,
            WorkerId = 11,
            ExtraHours = 1.5m,
            ExtraAmount = 97500m,
            ExtStatus = ExtensionStatuses.Paid,
            WorkerDecision = WorkerDecisions.Pending,
            RequestedAt = Now,
        };

        var progress = await f.Service().GetProgressAsync(CustomerId, 1);

        Assert.Empty(progress.Assignments);
        Assert.Equal("DISPATCHING", progress.OrderStatus);
        Assert.Equal(new ExtensionDto(9, 1, 11, 1.5m, 97500m, "PAID", "PENDING", Now, null), progress.Extension);
    }

    [Fact]
    public void TheOrderShape_OnTheWire_UsesStringsForEveryEnum()
    {
        var created = new CreatedOrder(
            1, "GV1", ServiceTier.Economy, 3, new DateOnly(2026, 10, 15), "SHIFT_MORNING", Now, Now, 50m, 1, null, 260000m,
            JobOrderStatus.Cancelled, null, "x", null, Now, Now);

        var dto = OrderDto.From(created);

        Assert.Equal("ECONOMY", dto.ServiceTier);
        Assert.Equal("CANCELLED", dto.OrderStatus);
        Assert.DoesNotContain(typeof(OrderDto).GetProperties(), p => p.PropertyType.IsEnum);
        Assert.DoesNotContain(typeof(OrderSummaryDto).GetProperties(), p => p.PropertyType.IsEnum);
        Assert.DoesNotContain(typeof(ProgressAssignmentDto).GetProperties(), p => p.PropertyType.IsEnum);
    }

    private sealed class StubUser(int? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public int? UserId => userId;
        public UserRole? Role => userId.HasValue ? UserRole.Customer : null;
    }

    [Fact]
    public async Task Controller_MapsTheStatusCodes_AndIsCustomerOnly()
    {
        var f = new Fixture();
        f.AddOrder(1);
        var controller = new BookingOrdersController(f.Service(), new StubUser(CustomerId));
        var anonymous = new BookingOrdersController(f.Service(), new StubUser(null));
        var body = new CreateOrderBody(AddressId, "ECONOMY", new DateOnly(2026, 10, 15), "SHIFT_MORNING", null, null);

        Assert.Equal(200, ((ObjectResult)controller.Options()).StatusCode);
        Assert.Equal(200, ((ObjectResult)await controller.PriceQuote(AddressId, "ECONOMY", default)).StatusCode);
        Assert.Equal(201, ((ObjectResult)await controller.Create(body, default)).StatusCode);
        Assert.Equal(200, ((ObjectResult)await controller.List(null, null, null, default)).StatusCode);
        Assert.Equal(200, ((ObjectResult)await controller.Get(1, default)).StatusCode);
        Assert.Equal(200, ((ObjectResult)await controller.Progress(1, default)).StatusCode);
        Assert.Equal(401, ((ObjectResult)await anonymous.Create(body, default)).StatusCode);
        Assert.Equal(401, ((ObjectResult)await anonymous.Get(1, default)).StatusCode);
        var authorize = typeof(BookingOrdersController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("CustomerOnly", authorize.Policy);
    }
}
