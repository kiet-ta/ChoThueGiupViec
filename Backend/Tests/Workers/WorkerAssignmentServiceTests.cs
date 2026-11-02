using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>GET /api/workers/me/assignments (contract workers.md 2.3.3): range rules, which statuses count as booked, order, own jobs only.</summary>
public class WorkerAssignmentServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
        public UserRole? Role { get; set; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    /// <summary>Holds the jobs of several workers, like the table, and answers for the worker and range it is asked for only.</summary>
    private sealed class FakeRepository(params (int WorkerId, WorkerAssignmentRow Row)[] all) : IWorkerAssignmentRepository
    {
        public (int WorkerId, DateOnly Start, DateOnly End)? LastCall { get; private set; }

        public Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
            int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            LastCall = (workerId, startDate, endDate);
            IReadOnlyList<WorkerAssignmentRow> rows = all
                .Where(x => x.WorkerId == workerId && x.Row.SlotDate >= startDate && x.Row.SlotDate <= endDate)
                .Select(x => x.Row)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    private static WorkerAssignmentRow Row(
        long id, DateOnly date, string shift, string start, string end, JobAssignmentStatus status,
        ServiceTier tier = ServiceTier.Economy, string district = "Quận 1", decimal payout = 128000m) =>
        new(id, id + 100, $"GV-{id}", date, shift, TimeOnly.Parse(start), TimeOnly.Parse(end), status, tier, district, payout);

    private static WorkerAssignmentService CreateService(IWorkerAssignmentRepository repository, int? workerId = 7)
    {
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc)); // 10:00 on 30/10 in Vietnam
        return new WorkerAssignmentService(repository, new TestCurrentUser { UserId = workerId }, clock);
    }

    [Fact]
    public async Task Defaults_to_today_and_the_next_13_days()
    {
        var repo = new FakeRepository();
        await CreateService(repo).GetAsync(null, null);

        Assert.Equal((7, Today, Today.AddDays(13)), repo.LastCall);
    }

    [Fact]
    public async Task Rejects_an_end_before_the_start_and_a_range_over_31_days()
    {
        var service = CreateService(new FakeRepository());

        var reversed = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(Today, Today.AddDays(-1)));
        Assert.Contains("endDate", reversed.Errors.Keys);

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(Today, Today.AddDays(31)));
        Assert.Contains("endDate", tooLong.Errors.Keys);

        // 31 days exactly (start + 30) is allowed.
        var ok = await service.GetAsync(Today, Today.AddDays(30));
        Assert.True(ok.Success);
    }

    [Fact]
    public async Task Without_a_signed_in_worker_the_call_is_refused()
    {
        var service = CreateService(new FakeRepository(), workerId: null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetAsync(null, null));
    }

    [Fact]
    public async Task Keeps_only_booked_jobs_in_date_then_shift_order_with_the_wire_spelling()
    {
        var repo = new FakeRepository(
            (7, Row(1, Today.AddDays(2), "SHIFT_AFTERNOON", "13:00", "17:00", JobAssignmentStatus.Assigned)),
            (7, Row(2, Today.AddDays(1), "SHIFT_MORNING", "08:00", "12:00", JobAssignmentStatus.CheckedIn, ServiceTier.Premium, "Quận 3", 240000m)),
            (7, Row(3, Today.AddDays(1), "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.AwaitingAcceptance)),
            (7, Row(4, Today, "SHIFT_MORNING", "08:00", "12:00", JobAssignmentStatus.InProgress)),
            (7, Row(5, Today, "SHIFT_AFTERNOON", "13:00", "17:00", JobAssignmentStatus.Completed)),
            // Not booked jobs: an offer and every way a job can end without being done.
            (7, Row(6, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.Offered)),
            (7, Row(7, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.Cancelled)),
            (7, Row(8, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.CancelledByWorker)),
            (7, Row(9, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.Absent)),
            (7, Row(10, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.Incident)),
            (7, Row(11, Today, "SHIFT_EVENING", "17:30", "20:30", JobAssignmentStatus.Reassigned)));

        var result = await CreateService(repo).GetAsync(Today, Today.AddDays(5));

        Assert.Equal([4L, 5L, 2L, 3L, 1L], result.Data!.Select(x => x.AssignmentId).ToArray());

        var premium = result.Data!.Single(x => x.AssignmentId == 2);
        Assert.Equal("CHECKED_IN", premium.Status);
        Assert.Equal("PREMIUM", premium.ServiceTier);
        Assert.Equal("Quận 3", premium.District);
        Assert.Equal(240000m, premium.PayoutAmount);
        Assert.Equal("08:00", premium.StartTime);
        Assert.Equal("12:00", premium.EndTime);
        Assert.Equal("GV-2", premium.OrderCode);
        Assert.Equal(102, premium.OrderId);
        Assert.Equal("AWAITING_ACCEPTANCE", result.Data!.Single(x => x.AssignmentId == 3).Status);
    }

    [Fact]
    public async Task Returns_only_the_signed_in_workers_own_jobs()
    {
        var repo = new FakeRepository(
            (7, Row(1, Today.AddDays(1), "SHIFT_MORNING", "08:00", "12:00", JobAssignmentStatus.Assigned)),
            (8, Row(2, Today.AddDays(1), "SHIFT_MORNING", "08:00", "12:00", JobAssignmentStatus.Assigned)));

        var mine = await CreateService(repo, workerId: 7).GetAsync(Today, Today.AddDays(3));
        var theirs = await CreateService(repo, workerId: 8).GetAsync(Today, Today.AddDays(3));
        var nobody = await CreateService(repo, workerId: 9).GetAsync(Today, Today.AddDays(3));

        Assert.Equal([1L], mine.Data!.Select(x => x.AssignmentId).ToArray());
        Assert.Equal([2L], theirs.Data!.Select(x => x.AssignmentId).ToArray());
        Assert.Empty(nobody.Data!);
    }

    [Fact]
    public void The_response_has_no_street_address_coordinates_or_customer_data()
    {
        var names = typeof(CommonService.Application.Features.Workers.Dtos.WorkerAssignmentResponse)
            .GetProperties().Select(p => p.Name).ToArray();

        Assert.Contains("District", names);
        Assert.DoesNotContain(names, n =>
            n.Contains("Address", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Latitude", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Longitude", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Customer", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Phone", StringComparison.OrdinalIgnoreCase));
    }
}
