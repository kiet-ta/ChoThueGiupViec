using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>The jobs booked for the signed-in worker (contract workers.md 2.3.3). The worker is read from ICurrentUser.</summary>
public interface IWorkerAssignmentService
{
    /// <summary>
    /// Booked jobs (ASSIGNED, CHECKED_IN, IN_PROGRESS, AWAITING_ACCEPTANCE, COMPLETED) with a slot date in the range, by date and shift start.
    /// Start defaults to today (Vietnam date) and end to start + 13 days; a range over 31 days or an end before the start is a 400.
    /// </summary>
    Task<ApiResponse<IReadOnlyList<WorkerAssignmentResponse>>> GetAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default);

    /// <summary>
    /// The detail of one of the signed-in worker's own jobs (contract workers.md 2.3.4): the full address from the moment the worker has accepted it.
    /// NotFoundException for an unknown assignment, another worker's one, an offer and a job that ended without being done (all look the same).
    /// </summary>
    Task<ApiResponse<WorkerAssignmentDetailResponse>> GetDetailAsync(long assignmentId, CancellationToken ct = default);
}
