using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers;

/// <summary>An assignment of one worker with the date and shift of its slot and the district of its order (no navigation properties, 0-JOIN rule: the repository joins).</summary>
public record WorkerAssignmentRow(
    long AssignmentId,
    long OrderId,
    string OrderCode,
    DateOnly SlotDate,
    string ShiftCode,
    TimeOnly StartTime,
    TimeOnly EndTime,
    JobAssignmentStatus Status,
    ServiceTier ServiceTier,
    string District,
    decimal PayoutAmount);

/// <summary>One assignment of a worker with the full address of its order and the paid extension waiting for the worker, if any (contract workers.md 2.3.4).</summary>
public record WorkerAssignmentDetailRow(
    long AssignmentId,
    long OrderId,
    string OrderCode,
    DateOnly SlotDate,
    string ShiftCode,
    TimeOnly StartTime,
    TimeOnly EndTime,
    JobAssignmentStatus Status,
    ServiceTier ServiceTier,
    string AddressLine,
    string District,
    string City,
    decimal Latitude,
    decimal Longitude,
    decimal PayoutAmount,
    int? PendingExtensionId,
    decimal? PendingExtraHours,
    decimal? PendingExtraAmount);

/// <summary>Read of the jobs booked for a worker (contract workers.md 2.3.3 and 2.3.4).</summary>
public interface IWorkerAssignmentRepository
{
    /// <summary>The worker's own assignments, whatever their status, whose slot date is in [<paramref name="startDate"/>, <paramref name="endDate"/>]. Nobody else's.</summary>
    Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
        int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The assignment <paramref name="assignmentId"/> with its order's address, only when it belongs to <paramref name="workerId"/>; null otherwise (an unknown
    /// assignment and another worker's one look the same). Whatever its status: the service decides which statuses may be shown.
    /// </summary>
    Task<WorkerAssignmentDetailRow?> FindDetailForWorkerAsync(
        long assignmentId, int workerId, CancellationToken cancellationToken = default);
}
