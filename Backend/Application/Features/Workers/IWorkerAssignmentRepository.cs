using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers;

/// <summary>An assignment of one worker with the date and shift of its slot and the district of its order (no navigation properties, 0-JOIN rule: the repository joins).</summary>
public sealed record WorkerAssignmentRow(
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

/// <summary>Read of the jobs booked for a worker (contract workers.md 2.3.3).</summary>
public interface IWorkerAssignmentRepository
{
    /// <summary>The worker's own assignments, whatever their status, whose slot date is in [<paramref name="startDate"/>, <paramref name="endDate"/>]. Nobody else's.</summary>
    Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
        int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}
