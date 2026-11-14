using CommonService.Application.Common.Helpers;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Disputes;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Workers;

/// <summary>Decision D32 (ACCEPT-01): the 24-hour acceptance window counted from T, the moment the AFTER photos were submitted.</summary>
public class AcceptanceWindowTests
{
    private const long AssignmentId = 70;
    private const long OrderId = 700;
    private const int CustomerId = 5;
    private const int WorkerId = 9;
    private static readonly DateTime T = new(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly AcceptanceRules Rules = new();

    // ---- the pure window ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_default_durations_are_30_minutes_20_hours_and_24_hours()
    {
        Assert.Equal((30, 20, 24), (Rules.RedoMinutes, Rules.ReminderHours, Rules.AutoAcceptHours));
        Assert.Equal(Rules.AutoAcceptHours, new BusinessRules().Acceptance.AutoAcceptHours);
    }

    [Theory]
    [InlineData(0, "ON_SITE")]
    [InlineData(29 * 60 + 59, "ON_SITE")]
    [InlineData(30 * 60, "REVIEW")]
    [InlineData(23 * 3600 + 59 * 60, "REVIEW")]
    public void A_waiting_job_is_on_site_for_30_minutes_then_in_review(int secondsAfterT, string phase)
    {
        var block = AcceptanceWindow.Describe(JobAssignmentStatus.AwaitingAcceptance, T, false, null, 0, T.AddSeconds(secondsAfterT), Rules)!;

        Assert.Equal(phase, block.Phase);
        Assert.Equal((T, T.AddMinutes(30), T.AddHours(24), T.AddHours(24)), (block.SubmittedAt!.Value, block.RedoUntil!.Value, block.AutoAcceptAt!.Value, block.DisputeUntil!.Value));
    }

    [Fact]
    public void An_open_dispute_shows_disputed_and_stops_the_automatic_acceptance()
    {
        var block = AcceptanceWindow.Describe(JobAssignmentStatus.AwaitingAcceptance, T, true, null, 1, T.AddMinutes(5), Rules)!;

        Assert.Equal(AcceptancePhases.Disputed, block.Phase);
        Assert.Null(block.AutoAcceptAt);
        Assert.Equal(1, block.RedoCount);
        Assert.False(AcceptanceWindow.IsAutoAcceptDue(T, true, T.AddHours(30), Rules));
    }

    [Fact]
    public void The_automatic_acceptance_is_due_at_24_hours_not_a_second_before()
    {
        Assert.False(AcceptanceWindow.IsAutoAcceptDue(T, false, T.AddHours(24).AddSeconds(-1), Rules));
        Assert.True(AcceptanceWindow.IsAutoAcceptDue(T, false, T.AddHours(24), Rules));
        Assert.False(AcceptanceWindow.IsAutoAcceptDue(null, false, T.AddHours(48), Rules));
    }

    [Fact]
    public void A_completed_job_carries_its_source_and_the_others_have_their_own_phase_or_none()
    {
        Assert.Equal("AUTO", AcceptanceWindow.Describe(JobAssignmentStatus.Completed, T, false, CompletionSource.Auto, 0, T.AddHours(25), Rules)!.CompletionSource);
        Assert.Equal("VERDICT", AcceptanceWindow.Describe(JobAssignmentStatus.Completed, T, false, CompletionSource.Verdict, 0, T, Rules)!.CompletionSource);
        Assert.Equal(AcceptancePhases.InProgress, AcceptanceWindow.Describe(JobAssignmentStatus.InProgress, null, false, null, 0, T, Rules)!.Phase);
        Assert.Null(AcceptanceWindow.Describe(JobAssignmentStatus.Assigned, null, false, null, 0, T, Rules));
        Assert.Null(AcceptanceWindow.Describe(JobAssignmentStatus.Cancelled, null, false, null, 0, T, Rules));
    }

    // ---- submit, redo, accept --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Submitting_the_after_photos_starts_the_window()
    {
        var h = new Completion(JobAssignmentStatus.InProgress, worker: true);
        h.Assignment.AcceptanceRemindedAt = T.AddHours(-30); // from an earlier window

        await h.Service.SubmitAsync(AssignmentId, null);

        Assert.Equal((T, (DateTime?)null), (h.Assignment.SubmittedAt!.Value, h.Assignment.AcceptanceRemindedAt));
    }

    [Fact]
    public async Task A_redo_inside_30_minutes_goes_back_to_work_counts_and_clears_t()
    {
        var h = new Completion(JobAssignmentStatus.AwaitingAcceptance, submittedAt: T.AddMinutes(-29));

        await h.Service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Còn bụi"));

        Assert.Equal((JobAssignmentStatus.InProgress, (byte)1, (DateTime?)null), (h.Assignment.AssignmentStatus, h.Assignment.RedoCount, h.Assignment.SubmittedAt));
    }

    [Fact]
    public async Task A_redo_after_the_worker_left_is_refused_with_redo_window_closed()
    {
        var h = new Completion(JobAssignmentStatus.AwaitingAcceptance, submittedAt: T.AddMinutes(-30));

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.RequestRedoAsync(AssignmentId, new RequestRedoRequest("Còn bụi")));

        Assert.Equal(AcceptanceErrorCodes.RedoWindowClosed, error.Code);
        Assert.Equal(JobAssignmentStatus.AwaitingAcceptance, h.Assignment.AssignmentStatus);
        Assert.Equal(0, h.Assignment.RedoCount);
    }

    [Fact]
    public async Task The_customer_accept_completes_with_source_customer_and_publishes_job_completed()
    {
        var h = new Completion(JobAssignmentStatus.AwaitingAcceptance, submittedAt: T.AddHours(-1));

        await h.Service.AcceptAsync(AssignmentId, null);

        Assert.Equal(CompletionSource.Customer, h.Repo.CompletedWith);
        Assert.IsType<JobCompleted>(Assert.Single(h.Publisher.Events));
    }

    [Fact]
    public async Task The_system_completes_only_a_waiting_job_and_never_as_the_customer()
    {
        var waiting = new Completion(JobAssignmentStatus.AwaitingAcceptance, submittedAt: T.AddHours(-25));
        Assert.True(await waiting.Service.CompleteBySystemAsync(AssignmentId, CompletionSource.Auto));
        Assert.Equal(CompletionSource.Auto, waiting.Repo.CompletedWith);
        Assert.Single(waiting.Publisher.Events);

        var cancelled = new Completion(JobAssignmentStatus.Cancelled);
        Assert.False(await cancelled.Service.CompleteBySystemAsync(AssignmentId, CompletionSource.Verdict));
        Assert.Null(cancelled.Repo.CompletedWith);

        await Assert.ThrowsAsync<ArgumentException>(() => waiting.Service.CompleteBySystemAsync(AssignmentId, CompletionSource.Customer));
    }

    // ---- the timer ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_timer_reminds_once_at_20_hours_and_auto_accepts_at_24_hours()
    {
        var timer = new Timer(T.AddHours(20));
        timer.Repo.Rows.Add(new WaitingAcceptance(AssignmentId, OrderId, CustomerId, WorkerId, T, null));

        Assert.Equal(new AcceptanceTimerResult(1, 0), await timer.Service.RunOnceAsync());
        Assert.Contains("tự nghiệm thu lúc 10:00 15/11", Assert.Single(timer.Notifications.Sent).Body); // T+24 h = 03:00 UTC = 10:00 Vietnam time
        Assert.Equal(new AcceptanceTimerResult(0, 0), await timer.Service.RunOnceAsync()); // the reminder is sent once

        timer.Clock.Set(T.AddHours(24));
        Assert.Equal(new AcceptanceTimerResult(0, 1), await timer.Service.RunOnceAsync());
        Assert.Equal([AssignmentId], timer.Completion.Completed);
        Assert.Equal(3, timer.Notifications.Sent.Count); // the reminder, then the customer and the worker
    }

    [Fact]
    public async Task The_timer_leaves_a_job_with_an_open_dispute_alone()
    {
        var timer = new Timer(T.AddHours(30));
        timer.Repo.Rows.Add(new WaitingAcceptance(AssignmentId, OrderId, CustomerId, WorkerId, T, null));
        timer.Disputes.OpenOrderIds.Add(OrderId);

        Assert.Equal(new AcceptanceTimerResult(0, 0), await timer.Service.RunOnceAsync());
        Assert.Empty(timer.Completion.Completed);
        Assert.Empty(timer.Notifications.Sent);
    }

    [Fact]
    public async Task A_job_someone_else_completed_first_is_not_counted_or_announced()
    {
        var timer = new Timer(T.AddHours(25));
        timer.Repo.Rows.Add(new WaitingAcceptance(AssignmentId, OrderId, CustomerId, WorkerId, T, T.AddHours(20)));
        timer.Completion.Answer = false; // the customer tapped at the same moment

        Assert.Equal(new AcceptanceTimerResult(0, 0), await timer.Service.RunOnceAsync());
        Assert.Empty(timer.Notifications.Sent);
    }

    // ---- the verdict ----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(FaultParty.Customer)]
    [InlineData(null)]
    [InlineData(FaultParty.Freelancer)]
    public async Task A_verdict_of_any_outcome_completes_a_waiting_job_with_source_verdict(FaultParty? fault)
    {
        var completion = new StubCompletion();
        var handler = new DisputeResolvedAcceptanceHandler(completion, NullLogger<DisputeResolvedAcceptanceHandler>.Instance);

        await handler.Handle(new DisputeResolved(3, AssignmentId, fault, 0m, "note", T), CancellationToken.None);

        Assert.Equal([(AssignmentId, CompletionSource.Verdict)], completion.Calls);
    }

    [Fact]
    public async Task A_verdict_handler_failure_never_reaches_the_verdict()
    {
        var handler = new DisputeResolvedAcceptanceHandler(new StubCompletion { Throws = true }, NullLogger<DisputeResolvedAcceptanceHandler>.Instance);
        await handler.Handle(new DisputeResolved(3, AssignmentId, null, 0m, "note", T), CancellationToken.None); // no exception
    }

    // ---- dispute window and ledger ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_dispute_window_of_a_submitted_job_ends_at_t_plus_24_hours()
    {
        var repo = new Disputes.DisputeServiceTests.MemoryDisputes();
        repo.Orders[OrderId] = new DisputeOrderInfo(OrderId, "ORD700", CustomerId,
            [new DisputeAssignmentInfo(AssignmentId, WorkerId, JobAssignmentStatus.AwaitingAcceptance, null, new DateTime(2026, 11, 14, 23, 0, 0), T)]);
        var clock = new Disputes.DisputeServiceTests.TestClock(T.AddHours(24));
        var filing = new DisputeFilingService(repo, clock, Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));
        var request = new FileDisputeRequestDto { OrderId = OrderId, Category = "QUALITY", Description = "Bẩn", EvidenceUrls = ["/u/1.jpg"] };

        Assert.Equal(DisputeConstants.Open, (await filing.FileAsync(DisputeSide.Customer, CustomerId, request)).DisputeStatus);

        repo.Orders[OrderId] = repo.Orders[OrderId] with { };
        var late = new DisputeFilingService(new Disputes.DisputeServiceTests.MemoryDisputes { Orders = { [OrderId] = repo.Orders[OrderId] } },
            new Disputes.DisputeServiceTests.TestClock(T.AddHours(24).AddTicks(1)), Microsoft.Extensions.Options.Options.Create(new DisputeOptions()));
        // The old rule would still be open here (shift end 23:00 local + 24 h); the window of the submit is over.
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => late.FileAsync(DisputeSide.Customer, CustomerId, request));
    }

    [Fact]
    public void A_freelancer_earning_is_withdrawable_from_t_plus_24_hours()
    {
        var order = new LedgerOrderSource(OrderId, CustomerId, 260_000m, 1);
        var submitted = new LedgerAssignmentSource(AssignmentId, OrderId, WorkerId, null, 1, 260_000m, 0.20m, T.AddHours(5), true, T);
        var legacy = submitted with { SubmittedAtUtc = null };

        Assert.Equal(T.AddHours(24), LedgerRules.JobEntries(order, submitted, T.AddHours(5)).Single(e => e.Type == LedgerEntryType.JobEarning).AvailableAtUtc);
        Assert.Equal(T.AddHours(29), LedgerRules.JobEntries(order, legacy, T.AddHours(5)).Single(e => e.Type == LedgerEntryType.JobEarning).AvailableAtUtc);
    }

    // ---- doubles --------------------------------------------------------------------------------------------------------------

    private static FakeClock ClockAt(DateTime utc)
    {
        var clock = new FakeClock();
        clock.Set(utc);
        return clock;
    }

    private class Completion
    {
        public JobAssignment Assignment { get; }
        public AcceptRepository Repo { get; }
        public RecordingPublisher Publisher { get; } = new();
        public JobCompletionService Service { get; }

        public Completion(JobAssignmentStatus status, DateTime? submittedAt = null, bool worker = false)
        {
            Assignment = new JobAssignment
            {
                AssignmentId = AssignmentId,
                OrderId = OrderId,
                CustomerId = CustomerId,
                WorkerId = WorkerId,
                GrossAmount = 260_000m,
                SubmittedAt = submittedAt,
            };
            foreach (var step in PathTo(status)) Assignment.TransitionTo(step);
            Repo = new AcceptRepository(Assignment);
            var user = new User(worker ? WorkerId : CustomerId, worker ? UserRole.Worker : UserRole.Customer);
            Service = new JobCompletionService(Repo, Publisher, user, ClockAt(T), Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        }

        private static JobAssignmentStatus[] PathTo(JobAssignmentStatus status) => status switch
        {
            JobAssignmentStatus.InProgress => [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress],
            JobAssignmentStatus.AwaitingAcceptance =>
                [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance],
            JobAssignmentStatus.Cancelled => [JobAssignmentStatus.Cancelled],
            _ => [],
        };
    }

    private class User(int id, UserRole role) : ICurrentUser
    {
        public int? UserId => id;
        public UserRole? Role => role;
        public bool IsAuthenticated => true;
    }

    private class RecordingPublisher : IPublisher
    {
        public List<object> Events { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default) { Events.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Events.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private class AcceptRepository(JobAssignment assignment) : IWorkerRepository
    {
        public CompletionSource? CompletedWith { get; private set; }

        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => Task.FromResult<JobAssignment?>(assignment);

        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobPhoto>>(Enumerable.Range(1, 3)
                .Select(a => new JobPhoto { AssignmentId = assignmentId, PhotoPhase = photoPhase, AngleNo = (byte)a, IsAccepted = true }).ToList());

        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) =>
            TryCompleteAssignmentAsync(assignmentId, workerId, commissionRate, payoutAmount, completedAtUtc, CompletionSource.Customer, cancellationToken);

        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CompletionSource source, CancellationToken cancellationToken = default)
        {
            if (assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance) return Task.FromResult(false);
            CompletedWith = source;
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RetireAcceptedJobPhotosAsync(long assignmentId, string photoPhase, byte angleNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Worker entity) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }

    private class Timer
    {
        public FakeClock Clock { get; }
        public MemoryTimerRepository Repo { get; } = new();
        public StubCompletion Completion { get; } = new();
        public FakeOpenDisputeQuery Disputes { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public AcceptanceTimerService Service { get; }

        public Timer(DateTime now)
        {
            Clock = ClockAt(now);
            Service = new AcceptanceTimerService(Repo, Completion, Disputes, Notifications, Clock, Microsoft.Extensions.Options.Options.Create(new BusinessRules()),
                NullLogger<AcceptanceTimerService>.Instance);
        }
    }

    private class MemoryTimerRepository : IAcceptanceTimerRepository
    {
        public List<WaitingAcceptance> Rows { get; } = [];

        public Task<IReadOnlyList<WaitingAcceptance>> GetWaitingAsync(DateTime submittedAtOrBeforeUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitingAcceptance>>(Rows.Where(r => r.SubmittedAt <= submittedAtOrBeforeUtc).Take(take).ToList());

        public Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default)
        {
            var index = Rows.FindIndex(r => r.AssignmentId == assignmentId && r.RemindedAt is null);
            if (index < 0) return Task.FromResult(false);
            Rows[index] = Rows[index] with { RemindedAt = atUtc };
            return Task.FromResult(true);
        }
    }

    private class StubCompletion : IJobCompletionService
    {
        public bool Answer { get; set; } = true;
        public bool Throws { get; set; }
        public List<long> Completed { get; } = [];
        public List<(long, CompletionSource)> Calls { get; } = [];

        public Task<bool> CompleteBySystemAsync(long assignmentId, CompletionSource source, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("database down");
            Calls.Add((assignmentId, source));
            if (Answer) Completed.Add(assignmentId);
            return Task.FromResult(Answer);
        }

        public Task<ApiResponse<AssignmentStatusResponse>> SubmitAsync(long assignmentId, string? notes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<AssignmentCompletionResponse>> AcceptAsync(long assignmentId, AcceptAssignmentRequest? request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiResponse<AssignmentStatusResponse>> RequestRedoAsync(long assignmentId, RequestRedoRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
