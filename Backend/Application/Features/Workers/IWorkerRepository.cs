using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Workers;

/// <summary>
/// Repository contract for Worker aggregate root.
/// Lives inside Application/Features/Workers per module encapsulation convention.
/// </summary>
public interface IWorkerRepository : IRepository<Worker, int>
{
    Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default);
    /// <summary>
    /// The eKYC list of the Admin audit page. <paramref name="status"/> is PENDING (waiting for review, the default), APPROVED (approved
    /// by an Admin: <c>kyc_reviewed_by</c> is set) or REJECTED; the caller has checked it.
    /// </summary>
    Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(
        int page, int pageSize, string status = EkycQueueStatus.Pending, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default);
    Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default);
    void UpdateSlot(BookingSlot slot);

    Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobPhoto>> GetJobPhotosAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobPhoto>> GetAcceptedJobPhotosByPhaseAsync(long assignmentId, string photoPhase, CancellationToken cancellationToken = default);
    Task AddJobPhotoAsync(JobPhoto photo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the assignment only if it is still AWAITING_ACCEPTANCE: one conditional update of the assignment (status, completion times,
    /// commission, payout) and, in the same transaction, <c>CompletedJobs + 1</c> and BUSY -&gt; IDLE of the worker. Returns false, with nothing
    /// changed, when another request completed or changed the assignment first, so two concurrent accepts complete it once. The tracked copies of
    /// the two rows are detached, so the next read sees the stored values.
    /// </summary>
    Task<bool> TryCompleteAssignmentAsync(
        long assignmentId, int workerId, decimal commissionRate, decimal payoutAmount, DateTime completedAtUtc, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}



