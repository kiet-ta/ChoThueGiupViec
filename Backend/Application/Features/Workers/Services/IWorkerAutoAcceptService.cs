using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// The "Tự động nhận đơn" switch of the signed-in freelancer (contract workers.md 2.3C, DISPATCH-AUTO-01). The worker is read from
/// ICurrentUser. Dispatch reads the stored value when it makes an offer.
/// </summary>
public interface IWorkerAutoAcceptService
{
    /// <summary>The stored value; false for agency staff (they never get offers).</summary>
    Task<ApiResponse<WorkerAutoAcceptResponse>> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores the switch. Agency staff: 409 <c>NOT_FREELANCER</c>. Turning it on without a registered work area: 409
    /// <c>WORK_AREA_REQUIRED</c> (dispatch would never find the worker). Turning it off is always allowed.
    /// </summary>
    Task<ApiResponse<WorkerAutoAcceptResponse>> SaveAsync(UpdateWorkerAutoAcceptRequest? request, CancellationToken ct = default);
}
