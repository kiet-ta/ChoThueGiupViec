using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>ADMIN-UAT-13 (#871): approving or rejecting an eKYC writes ADMIN_AUDIT_LOG rows in the unit of work of the worker update. No SQL Server needed.</summary>
public class EkycReviewAuditTests
{
    private const int AdminId = 7;
    private const int WorkerId = 42;
    private const string DefaultReason = "Admin duyệt hồ sơ eKYC thủ công.";

    private static WorkerEkycService Service(ReviewRepository repository, IAuditLog audit) =>
        new(repository, null!, new AdminUser(), Microsoft.Extensions.Options.Options.Create(new BusinessRules()), audit);

    /// <summary>A freelancer waiting for review: kyc_status <paramref name="kyc"/>, work_status <paramref name="work"/>.</summary>
    private static Worker Waiting(string kyc, WorkStatus work)
    {
        var worker = Worker.CreateFreelancer("0900000042", "012000000042", "Review Worker");
        worker.WorkerId = WorkerId;
        worker.SetKycResult(70m, kyc);
        if (work != worker.WorkStatus)
        {
            worker.TransitionTo(work);
        }

        return worker;
    }

    [Fact]
    public async Task Approving_a_pending_worker_writes_kyc_status_and_work_status_rows_before_the_save()
    {
        var log = new List<string>();
        var repository = new ReviewRepository(log, Waiting("MANUAL_REVIEW", WorkStatus.Pending));
        var audit = new RecordingAudit(log);

        var result = await Service(repository, audit).ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Collection(audit.Entries,
            kyc =>
            {
                Assert.Equal((AuditActorType.Admin, (int?)AdminId, "WORKER", "42", "kyc_status"), (kyc.ActorType, kyc.AdminId, kyc.EntityType, kyc.EntityId, kyc.FieldName));
                Assert.Equal(("MANUAL_REVIEW", "APPROVED", DefaultReason), (kyc.OldValue, kyc.NewValue, kyc.Reason));
            },
            work =>
            {
                Assert.Equal((AuditActorType.Admin, (int?)AdminId, "WORKER", "42", "work_status"), (work.ActorType, work.AdminId, work.EntityType, work.EntityId, work.FieldName));
                Assert.Equal(("PENDING", "IDLE", DefaultReason), (work.OldValue, work.NewValue, work.Reason));
            });
        Assert.Equal(["update", "audit", "audit", "save"], log); // both rows are written before the one save: they commit with the worker or not at all
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Approving_a_worker_that_is_already_idle_writes_only_the_kyc_status_row()
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("AUDIT_PENDING", WorkStatus.Idle)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true), CancellationToken.None);

        var row = Assert.Single(audit.Entries);
        Assert.Equal(("kyc_status", "AUDIT_PENDING", "APPROVED"), (row.FieldName, row.OldValue, row.NewValue));
    }

    [Fact]
    public async Task Approving_a_locked_worker_writes_work_status_locked_to_idle()
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Locked)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true), CancellationToken.None);

        Assert.Equal(["kyc_status", "work_status"], audit.Entries.Select(e => e.FieldName));
        Assert.Equal(("LOCKED", "IDLE"), (audit.Entries[1].OldValue, audit.Entries[1].NewValue));
    }

    [Theory]
    [InlineData("  Ảnh rõ, khớp selfie  ", "Ảnh rõ, khớp selfie")]
    [InlineData(null, DefaultReason)]
    [InlineData("   ", DefaultReason)]
    public async Task Approving_uses_the_trimmed_reason_given_or_a_default(string? given, string expected)
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Pending)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true, RejectionReason: given), CancellationToken.None);

        Assert.All(audit.Entries, e => Assert.Equal(expected, e.Reason));
    }

    [Fact]
    public async Task Approving_with_a_reason_longer_than_255_characters_falls_back_to_the_default()
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Pending)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true, RejectionReason: new string('a', 256)), CancellationToken.None);

        Assert.All(audit.Entries, e => Assert.Equal(DefaultReason, e.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOO_LONG")]
    public async Task Rejecting_needs_a_reason_of_1_to_255_characters_and_changes_nothing_otherwise(string? reason)
    {
        var log = new List<string>();
        var worker = Waiting("MANUAL_REVIEW", WorkStatus.Pending);
        var audit = new RecordingAudit(log);
        reason = reason == "TOO_LONG" ? new string('a', 256) : reason;

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Service(new ReviewRepository(log, worker), audit).ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: false, RejectionReason: reason), CancellationToken.None));

        Assert.Contains("rejectionReason", error.Errors.Keys);
        Assert.Empty(log); // not even the worker update
        Assert.Equal("MANUAL_REVIEW", worker.KycStatus);
        Assert.Equal(WorkStatus.Pending, worker.WorkStatus);
    }

    [Fact]
    public async Task Rejecting_writes_kyc_status_and_work_status_locked_rows_with_the_trimmed_reason()
    {
        var audit = new RecordingAudit([]);

        var result = await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Pending)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: false, RejectionReason: "  Ảnh mờ, sai CCCD  "), CancellationToken.None);

        Assert.Equal("Ảnh mờ, sai CCCD", result.Data!.RejectionReason);
        Assert.Collection(audit.Entries,
            kyc => Assert.Equal(("kyc_status", "MANUAL_REVIEW", "REJECTED", "Ảnh mờ, sai CCCD"), (kyc.FieldName, kyc.OldValue, kyc.NewValue, kyc.Reason)),
            work => Assert.Equal(("work_status", "PENDING", "LOCKED", "Ảnh mờ, sai CCCD"), (work.FieldName, work.OldValue, work.NewValue, work.Reason)));
        Assert.All(audit.Entries, e => Assert.Equal((AuditActorType.Admin, (int?)AdminId), (e.ActorType, e.AdminId)));
    }

    [Fact]
    public async Task Rejecting_with_exactly_255_characters_is_accepted()
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Pending)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: false, RejectionReason: new string('a', 255)), CancellationToken.None);

        Assert.All(audit.Entries, e => Assert.Equal(255, e.Reason!.Length));
    }

    [Fact]
    public async Task Rejecting_a_worker_that_is_already_locked_writes_only_the_kyc_status_row()
    {
        var audit = new RecordingAudit([]);

        await Service(new ReviewRepository([], Waiting("MANUAL_REVIEW", WorkStatus.Locked)), audit)
            .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: false, RejectionReason: "Gian lận"), CancellationToken.None);

        Assert.Equal(["kyc_status"], audit.Entries.Select(e => e.FieldName));
    }

    [Fact]
    public async Task A_failing_audit_write_stops_before_the_save()
    {
        var log = new List<string>();
        var repository = new ReviewRepository(log, Waiting("MANUAL_REVIEW", WorkStatus.Pending));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(repository, new RecordingAudit(log) { Failure = new ArgumentException("audit refused") })
                .ReviewAsync(WorkerId, new ReviewEkycRequest(Approved: true), CancellationToken.None));

        Assert.Equal(0, repository.Saves);
    }

    private sealed class RecordingAudit(List<string> log) : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Exception? Failure { get; init; }

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Entries.Add(entry);
            log.Add("audit");
            return Task.CompletedTask;
        }
    }

    private sealed class AdminUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public int? UserId => AdminId;

        public UserRole? Role => UserRole.Admin;
    }

    private sealed class ReviewRepository(List<string> log, params Worker[] workers) : IWorkerRepository
    {
        public int Saves { get; private set; }

        public Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(workers.FirstOrDefault(w => w.WorkerId == id));

        public void Update(Worker entity) => log.Add("update");

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            log.Add("save");
            return Task.CompletedTask;
        }

        public Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void UpdateSlot(BookingSlot slot) => throw new NotSupportedException();
        public Task<bool> TryCompleteAssignmentAsync(long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RetireAcceptedJobPhotosAsync(long assignmentId, string photoPhase, byte angleNo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(Worker entity) => throw new NotSupportedException();
    }
}
