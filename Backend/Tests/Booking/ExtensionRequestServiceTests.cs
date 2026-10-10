using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.WebAPI.Controllers.Booking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-08: "Làm lần 2" request (contract booking.md 3.8, BR-08), with in-memory ports (no DB).</summary>
public sealed class ExtensionRequestServiceTests
{
    private const int CustomerId = 7;
    private const long OrderId = 42;
    private const long AssignmentId = 100;
    private const int WorkerId = 11;
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(Now));
    }

    private sealed class InMemoryExtensions : IExtensionRepository, IUnitOfWork
    {
        private int _nextId = 1;
        private readonly List<JobOrderExtension> _pending = [];
        public Dictionary<long, OrderForExtension> Orders { get; } = [];
        public Dictionary<long, AssignmentForExtension> Assignments { get; } = [];
        public List<JobOrderExtension> Saved { get; } = [];

        public Task<OrderForExtension?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.GetValueOrDefault(orderId));

        public Task<AssignmentForExtension?> GetAssignmentOfOrderAsync(long orderId, long assignmentId, CancellationToken cancellationToken = default)
        {
            var a = Assignments.GetValueOrDefault(assignmentId);
            return Task.FromResult(a is not null && a.OrderId == orderId ? a : null);
        }

        public Task<JobOrderExtension?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExtensionExistsAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.Any(e => e.OrderId == orderId));

        public void Add(JobOrderExtension extension) => _pending.Add(extension);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var e in _pending)
            {
                e.ExtensionId = _nextId++;
                Saved.Add(e);
            }

            var count = _pending.Count;
            _pending.Clear();
            return Task.FromResult(count);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class Fixture
    {
        public InMemoryExtensions Db { get; } = new();

        /// <summary>An ASSIGNED order of 2 workers, total 520,000 (so 260,000 per worker per 4 h shift), with the worker IN_PROGRESS.</summary>
        public Fixture(JobOrderStatus orderStatus = JobOrderStatus.Assigned, JobAssignmentStatus assignmentStatus = JobAssignmentStatus.InProgress)
        {
            Db.Orders[OrderId] = new OrderForExtension(OrderId, CustomerId, orderStatus, 520000m, 2);
            Db.Assignments[AssignmentId] = new AssignmentForExtension(AssignmentId, OrderId, WorkerId, assignmentStatus);
        }

        public ExtensionRequestService Service() =>
            new(Db, Db, new TestClock(), Microsoft.Extensions.Options.Options.Create(new CommonService.Application.Common.Options.BusinessRules()));
    }

    private static CreateExtensionRequest Request(decimal hours = 1.5m, long assignmentId = AssignmentId) => new(assignmentId, hours);

    [Fact]
    public async Task Create_MakesAPendingExtension_PricedFromTheFrozenUnitPrice_ForTheWorkerOnSite()
    {
        var f = new Fixture();

        var extension = await f.Service().CreateAsync(CustomerId, OrderId, Request(1.5m));

        // 520,000 / 2 workers = 260,000 per shift; / 4 h x 1.5 h = 97,500.
        Assert.Equal(97500m, extension.ExtraAmount);
        Assert.Equal(1.5m, extension.ExtraHours);
        Assert.Equal(WorkerId, extension.WorkerId);
        Assert.Equal(OrderId, extension.OrderId);
        Assert.Equal(ExtensionStatuses.PendingPayment, extension.ExtStatus);
        Assert.Equal(WorkerDecisions.Pending, extension.WorkerDecision);
        Assert.Equal(Now, extension.RequestedAt);
        Assert.Null(extension.DecidedAt);
        var saved = Assert.Single(f.Db.Saved);
        Assert.Equal(extension.ExtensionId, saved.ExtensionId);
        Assert.Equal(Now, saved.CreatedAt);
    }

    [Theory]
    [InlineData("0.5", "32500")]
    [InlineData("1", "65000")]
    [InlineData("2.5", "162500")]
    [InlineData("4", "260000")]
    public async Task ExtraAmount_IsTheUnitPricePerHour_TimesTheHours(string hours, string expected)
    {
        var f = new Fixture();

        var extension = await f.Service().CreateAsync(CustomerId, OrderId, Request(decimal.Parse(hours, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), extension.ExtraAmount);
    }

    [Fact]
    public async Task ExtraAmount_IsRoundedToWholeVnd()
    {
        var f = new Fixture();
        f.Db.Orders[OrderId] = new OrderForExtension(OrderId, CustomerId, JobOrderStatus.Assigned, 260001m, 1);

        var extension = await f.Service().CreateAsync(CustomerId, OrderId, Request(0.5m));

        Assert.Equal(32500m, extension.ExtraAmount); // 260,001 / 4 x 0.5 = 32,500.125
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.25")]
    [InlineData("0.4")]
    [InlineData("4.5")]
    [InlineData("-1")]
    [InlineData("1.75")]
    public async Task ExtraHours_OutsideHalfHourStepsBetweenHalfAndFour_AreRejected(string hours)
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service().CreateAsync(CustomerId, OrderId, Request(decimal.Parse(hours, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Contains("extraHours", error.Errors.Keys);
        Assert.Empty(f.Db.Saved);
    }

    [Fact]
    public async Task AnAssignmentIdBelowOne_IsAValidationError()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ValidationException>(() => f.Service().CreateAsync(CustomerId, OrderId, Request(assignmentId: 0)));

        Assert.Contains("assignmentId", error.Errors.Keys);
    }

    [Fact]
    public async Task AnotherCustomersOrder_AnUnknownOrder_AndAnAssignmentOfAnotherOrder_AreNotFound()
    {
        var f = new Fixture();
        f.Db.Assignments[200] = new AssignmentForExtension(200, 999, 12, JobAssignmentStatus.InProgress);

        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateAsync(CustomerId + 1, OrderId, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateAsync(CustomerId, 999, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateAsync(CustomerId, OrderId, Request(assignmentId: 200)));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service().CreateAsync(CustomerId, OrderId, Request(assignmentId: 12345)));

        Assert.Empty(f.Db.Saved);
    }

    [Theory]
    [InlineData(JobOrderStatus.Paid, JobAssignmentStatus.InProgress)]
    [InlineData(JobOrderStatus.Dispatching, JobAssignmentStatus.InProgress)]
    [InlineData(JobOrderStatus.Completed, JobAssignmentStatus.InProgress)]
    [InlineData(JobOrderStatus.Cancelled, JobAssignmentStatus.InProgress)]
    [InlineData(JobOrderStatus.Assigned, JobAssignmentStatus.Assigned)]
    [InlineData(JobOrderStatus.Assigned, JobAssignmentStatus.CheckedIn)]
    [InlineData(JobOrderStatus.Assigned, JobAssignmentStatus.Completed)]
    [InlineData(JobOrderStatus.Assigned, JobAssignmentStatus.Cancelled)]
    public async Task OnlyAnAssignedOrderWithTheWorkerOnSite_CanBeExtended(JobOrderStatus orderStatus, JobAssignmentStatus assignmentStatus)
    {
        var f = new Fixture(orderStatus, assignmentStatus);

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CreateAsync(CustomerId, OrderId, Request()));

        Assert.Equal(ExtensionErrorCodes.InvalidState, error.Code);
        Assert.Empty(f.Db.Saved);
    }

    [Fact]
    public async Task TheWorkerWaitingForAcceptance_CanBeExtended()
    {
        var f = new Fixture(assignmentStatus: JobAssignmentStatus.AwaitingAcceptance);

        var extension = await f.Service().CreateAsync(CustomerId, OrderId, Request());

        Assert.Equal(ExtensionStatuses.PendingPayment, extension.ExtStatus);
    }

    [Fact]
    public async Task ASecondExtensionOfTheSameOrder_IsExtensionExists()
    {
        var f = new Fixture();
        await f.Service().CreateAsync(CustomerId, OrderId, Request());

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => f.Service().CreateAsync(CustomerId, OrderId, Request(2m)));

        Assert.Equal(ExtensionErrorCodes.ExtensionExists, error.Code);
        Assert.Single(f.Db.Saved);
    }

    private sealed class StubUser(int? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public int? UserId => userId;
        public UserRole? Role => userId.HasValue ? UserRole.Customer : null;
    }

    private sealed class StubService : IExtensionRequestService
    {
        public Task<ExtensionDto> CreateAsync(int customerId, long orderId, CreateExtensionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtensionDto(1, orderId, WorkerId, request.ExtraHours, 1m, ExtensionStatuses.PendingPayment, WorkerDecisions.Pending, Now, null));
    }

    [Fact]
    public async Task Controller_Answers201_AndIs401WithoutAUser_AndCustomerOnly()
    {
        var ok = await new BookingExtensionsController(new StubService(), new StubUser(CustomerId)).Create(OrderId, Request(), default);
        var anonymous = await new BookingExtensionsController(new StubService(), new StubUser(null)).Create(OrderId, Request(), default);

        Assert.Equal(201, ((ObjectResult)ok).StatusCode);
        Assert.Equal(401, ((ObjectResult)anonymous).StatusCode);
        var authorize = typeof(BookingExtensionsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("CustomerOnly", authorize.Policy);
    }
}
