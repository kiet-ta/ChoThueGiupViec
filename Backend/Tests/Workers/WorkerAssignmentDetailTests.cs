using System.Reflection;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-ASSIGNMENT-READ (#442): <c>GET /api/v1/workers/assignments/{id}</c> (contract workers.md 2.3.4). The worker sees the full address from the
/// moment the job is accepted; an offer, an ended job, another worker's job and an unknown id are the same 404. No database: the repository is a stub.
/// </summary>
public class WorkerAssignmentDetailTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;

    private class TestCurrentUser(int? id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// <summary>Answers like the table: a row only for the worker it belongs to.</summary>
    private class StubRepository(int ownerWorkerId, WorkerAssignmentDetailRow? row) : IWorkerAssignmentRepository
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
            int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkerAssignmentRow>>([]);

        public Task<WorkerAssignmentDetailRow?> FindDetailForWorkerAsync(
            long assignmentId, int workerId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(row is not null && row.AssignmentId == assignmentId && workerId == ownerWorkerId ? row : null);
        }
    }

    private static WorkerAssignmentDetailRow Row(
        JobAssignmentStatus status = JobAssignmentStatus.Assigned, int? extensionId = null, decimal? hours = null, decimal? amount = null) =>
        new(AssignmentId, 155, "GV-55", new DateOnly(2026, 11, 5), "SHIFT_MORNING", new TimeOnly(8, 0), new TimeOnly(12, 0), status,
            ServiceTier.Premium, "12 Nguyễn Huệ", "Quận 1", "TP. Hồ Chí Minh", 10.774m, 106.703m, 128000m, extensionId, hours, amount);

    private static WorkerAssignmentService Service(IWorkerAssignmentRepository repository, int? workerId = WorkerId) =>
        new(repository, new TestCurrentUser(workerId), new FakeClock());

    [Fact]
    public async Task AnAcceptedJob_ShowsTheFullAddressAndTheCoordinates()
    {
        var result = await Service(new StubRepository(WorkerId, Row())).GetDetailAsync(AssignmentId);

        Assert.True(result.Success);
        var d = result.Data!;
        Assert.Equal("12 Nguyễn Huệ", d.AddressLine);
        Assert.Equal("Quận 1", d.District);
        Assert.Equal("TP. Hồ Chí Minh", d.City);
        Assert.Equal(10.774m, d.Latitude);
        Assert.Equal(106.703m, d.Longitude);
        Assert.Equal(new DateOnly(2026, 11, 5), d.SlotDate);
        Assert.Equal("SHIFT_MORNING", d.ShiftCode);
        Assert.Equal("08:00", d.StartTime);
        Assert.Equal("12:00", d.EndTime);
        Assert.Equal("ASSIGNED", d.Status);
        Assert.Equal("PREMIUM", d.ServiceTier);
        Assert.Equal(128000m, d.PayoutAmount);
        Assert.Null(d.PendingExtension);
    }

    [Theory]
    [InlineData(JobAssignmentStatus.Assigned)]
    [InlineData(JobAssignmentStatus.CheckedIn)]
    [InlineData(JobAssignmentStatus.InProgress)]
    [InlineData(JobAssignmentStatus.AwaitingAcceptance)]
    [InlineData(JobAssignmentStatus.Completed)]
    public async Task EveryBookedStatus_IsShown(JobAssignmentStatus status)
    {
        var result = await Service(new StubRepository(WorkerId, Row(status))).GetDetailAsync(AssignmentId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task OnlyTheBookedStatusesAreShown_AnOfferAndEveryEndedJobAreNotFound()
    {
        var booked = new[]
        {
            JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress,
            JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed,
        };
        foreach (var status in Enum.GetValues<JobAssignmentStatus>().Where(s => !booked.Contains(s)))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Service(new StubRepository(WorkerId, Row(status))).GetDetailAsync(AssignmentId));
        }
    }

    [Fact]
    public async Task AnotherWorkersJob_AndAnUnknownId_AreTheSameNotFound()
    {
        var anotherWorker = Service(new StubRepository(WorkerId, Row()), workerId: 99);
        var other = await Assert.ThrowsAsync<NotFoundException>(() => anotherWorker.GetDetailAsync(AssignmentId));

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => Service(new StubRepository(WorkerId, Row())).GetDetailAsync(9999));

        Assert.Equal($"Job assignment {AssignmentId} not found.", other.Message);
        Assert.Equal("Job assignment 9999 not found.", unknown.Message);
    }

    [Fact]
    public async Task WithoutASignedInWorker_TheRepositoryIsNotAsked()
    {
        var repository = new StubRepository(WorkerId, Row());

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(repository, workerId: null).GetDetailAsync(AssignmentId));
        Assert.Equal(0, repository.Lookups);
    }

    [Fact]
    public async Task APaidExtensionWaitingForTheWorker_IsReturned()
    {
        var result = await Service(new StubRepository(WorkerId, Row(extensionId: 12, hours: 2m, amount: 160000m))).GetDetailAsync(AssignmentId);

        var pending = Assert.IsType<WorkerPendingExtension>(result.Data!.PendingExtension);
        Assert.Equal(12, pending.ExtensionId);
        Assert.Equal(2m, pending.ExtraHours);
        Assert.Equal(160000m, pending.ExtraAmount);
    }

    [Fact]
    public void TheEndpoint_IsGetAssignmentsId_ForWorkersOnly()
    {
        var action = typeof(WorkersController).GetMethod(nameof(WorkersController.GetAssignment))!;

        var get = Assert.Single(action.GetCustomAttributes<HttpGetAttribute>());
        Assert.Equal("assignments/{assignmentId:long}", get.Template);
        var authorize = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("WorkerOnly", authorize.Policy);
    }
}
