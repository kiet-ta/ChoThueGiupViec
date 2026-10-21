using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>WORKERS-EXT-01 (BE-M4-09): the worker accepts or declines an extension the customer paid for (contract workers.md 2.6.1). No database.</summary>
public class WorkerExtensionServiceTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;
    private const int ExtensionId = 301;
    private const long OrderId = 9;

    private sealed class User(int? id, UserRole? role, bool authenticated = true) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
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

    /// <summary>One extension of one worker; a write other than the conditional accept is a test failure.</summary>
    private sealed class Extensions(string status, string shiftCode = BookingShifts.Morning, decimal hours = 2m, bool acceptWins = true) : IWorkerExtensionRepository
    {
        public int Lookups { get; private set; }
        public int AcceptCalls { get; private set; }
        public DateTime? DecidedAt { get; private set; }

        public Task<ExtensionForWorker?> FindForWorkerAsync(long assignmentId, int workerId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            ExtensionForWorker? found = assignmentId == AssignmentId && workerId == WorkerId
                ? new ExtensionForWorker(ExtensionId, OrderId, WorkerId, hours, 130000m, status, new DateOnly(2026, 10, 28), shiftCode)
                : null;
            return Task.FromResult(found);
        }

        public Task<bool> TryAcceptAsync(int extensionId, DateTime decidedAtUtc, CancellationToken cancellationToken = default)
        {
            AcceptCalls++;
            DecidedAt = decidedAtUtc;
            return Task.FromResult(acceptWins);
        }
    }

    private static (WorkerExtensionService Service, Extensions Repo, RecordingPublisher Publisher, FakeClock Clock) Build(
        string status = ExtensionStatuses.Paid, ICurrentUser? user = null, string shiftCode = BookingShifts.Morning, decimal hours = 2m, bool acceptWins = true)
    {
        var repo = new Extensions(status, shiftCode, hours, acceptWins);
        var publisher = new RecordingPublisher();
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));
        return (new WorkerExtensionService(repo, publisher, user ?? new User(WorkerId, UserRole.Worker), clock), repo, publisher, clock);
    }

    [Fact]
    public async Task Accept_OfAPaidExtension_IsAccepted_AtTheClockTime_AndTheShiftEndsLater()
    {
        var (service, repo, publisher, clock) = Build(hours: 2m);

        var result = await service.RespondAsync(AssignmentId, accept: true);

        var data = result.Data!;
        Assert.Equal(AssignmentId, data.AssignmentId);
        Assert.Equal(ExtensionId, data.ExtensionId);
        Assert.Equal(2m, data.ExtraHours);
        Assert.Equal(130000m, data.ExtraAmount);
        Assert.Equal(ExtensionStatuses.Accepted, data.Status);
        Assert.Equal("14:00", data.NewEndTime); // the morning shift ends 12:00
        Assert.Equal(1, repo.AcceptCalls);
        Assert.Equal(clock.UtcNow, repo.DecidedAt);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Accept_WithHalfHours_AddsThem()
    {
        var (service, _, _, _) = Build(hours: 1.5m, shiftCode: BookingShifts.Afternoon);

        var result = await service.RespondAsync(AssignmentId, accept: true);

        Assert.Equal("18:30", result.Data!.NewEndTime); // the afternoon shift ends 17:00
    }

    [Fact]
    public async Task Decline_PublishesExtensionDeclinedOnce_AndDoesNotWriteTheStatusItself()
    {
        var (service, repo, publisher, clock) = Build();

        var result = await service.RespondAsync(AssignmentId, accept: false);

        Assert.Equal(ExtensionStatuses.Declined, result.Data!.Status);
        Assert.Equal("12:00", result.Data.NewEndTime); // not extended
        var declined = Assert.IsType<ExtensionDeclined>(Assert.Single(publisher.Published));
        Assert.Equal(ExtensionId, declined.ExtensionId);
        Assert.Equal(OrderId, declined.OrderId);
        Assert.Equal(WorkerId, declined.WorkerId);
        Assert.Equal(clock.UtcNow, declined.DeclinedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(declined.Reason));
        Assert.Equal(0, repo.AcceptCalls); // Payments marks DECLINED and refunds; a DECLINED row written here would make it skip the refund
    }

    [Theory]
    [InlineData(ExtensionStatuses.PendingPayment)]
    [InlineData(ExtensionStatuses.Accepted)]
    [InlineData(ExtensionStatuses.Declined)]
    [InlineData(ExtensionStatuses.Expired)]
    public async Task AnExtensionThatIsNotPaid_IsA409_ForAcceptAndDecline_AndNothingHappens(string status)
    {
        foreach (var accept in new[] { true, false })
        {
            var (service, repo, publisher, _) = Build(status);

            await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.RespondAsync(AssignmentId, accept));

            Assert.Equal(0, repo.AcceptCalls);
            Assert.Empty(publisher.Published);
        }
    }

    [Fact]
    public async Task AnAcceptThatLostTheRace_IsA409()
    {
        var (service, repo, _, _) = Build(acceptWins: false);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.RespondAsync(AssignmentId, accept: true));

        Assert.Equal(1, repo.AcceptCalls);
    }

    [Fact]
    public async Task AnUnknownAssignmentAndAnotherWorkersOne_AreA404()
    {
        var (service, _, publisher, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => service.RespondAsync(999, accept: true));

        var (other, repo, otherPublisher, _) = Build(user: new User(WorkerId + 1, UserRole.Worker));
        await Assert.ThrowsAsync<NotFoundException>(() => other.RespondAsync(AssignmentId, accept: false));
        Assert.Equal(1, repo.Lookups);
        Assert.Empty(publisher.Published);
        Assert.Empty(otherPublisher.Published);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(WorkerId, UserRole.Customer, true)]
    [InlineData(WorkerId, UserRole.Admin, true)]
    [InlineData(WorkerId, UserRole.Worker, false)]
    [InlineData(0, UserRole.Worker, true)]
    [InlineData(null, UserRole.Worker, true)]
    public async Task ACallerThatIsNotAnAuthenticatedWorker_IsRefusedBeforeAnythingIsRead(int? id, UserRole? role, bool authenticated)
    {
        var (service, repo, publisher, _) = Build(user: new User(id, role, authenticated));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.RespondAsync(AssignmentId, accept: true));

        Assert.Equal(0, repo.Lookups);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public void TheEndpoint_IsLimitedToWorkers()
    {
        var method = typeof(CommonService.WebAPI.Controllers.Workers.WorkersController).GetMethod("RespondToExtension")!;

        var authorize = Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("WorkerOnly", authorize.Policy);
        var route = Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
        Assert.Equal("assignments/{assignmentId:long}/extension/respond", route.Template);
    }
}
