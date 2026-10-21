using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Worker profile, payout account and location (contract workers.md §2.1, decisions D19). The caller is read from ICurrentUser;
/// a missing profile is a NotFoundException and an unauthenticated caller a ForbiddenAccessException.
/// </summary>
public interface IWorkerProfileService
{
    /// <summary>Registers a Freelancer with a single-purpose registration token (contract §2.1.1).</summary>
    Task<ApiResponse<WorkerProfileResponse>> RegisterAsync(RegisterWorkerRequest request, CancellationToken ct = default);

    Task<ApiResponse<WorkerProfileResponse>> GetProfileAsync(CancellationToken ct = default);

    Task<ApiResponse<WorkerProfileResponse>> UpdateProfileAsync(UpdateWorkerProfileRequest request, CancellationToken ct = default);

    /// <summary>Public summary view of any worker by id (contract §2.1.4).</summary>
    Task<ApiResponse<WorkerPublicProfileResponse>> GetByIdAsync(int workerId, CancellationToken ct = default);

    Task<ApiResponse<WorkerBankResponse>> GetBankAsync(CancellationToken ct = default);

    Task<ApiResponse<WorkerBankResponse>> UpdateBankAsync(UpdateWorkerBankRequest? request, CancellationToken ct = default);

    Task<ApiResponse<WorkerLocationResponse>> UpdateLocationAsync(UpdateWorkerLocationRequest? request, CancellationToken ct = default);
}
