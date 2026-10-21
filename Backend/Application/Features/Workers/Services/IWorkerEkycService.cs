using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>eKYC submission and Admin manual review of a worker (contract workers.md §2.2). The caller is read from ICurrentUser.</summary>
public interface IWorkerEkycService
{
    /// <summary>Submits CCCD photos and selfie; auto-approves at the configured confidence (contract §2.2.1).</summary>
    Task<ApiResponse<EkycResultResponse>> SubmitAsync(SubmitEkycRequest request, CancellationToken ct = default);

    Task<ApiResponse<EkycStatusResponse>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Admin only: lists workers waiting for manual review or sample audit (contract §2.2.2).</summary>
    Task<ApiResponse<EkycQueuePagedResponse>> GetQueueAsync(int page, int pageSize, string status, CancellationToken ct = default);

    /// <summary>Admin only: manual approval or rejection (contract §2.2.3).</summary>
    Task<ApiResponse<EkycResultResponse>> ReviewAsync(int workerId, ReviewEkycRequest request, CancellationToken ct = default);
}
