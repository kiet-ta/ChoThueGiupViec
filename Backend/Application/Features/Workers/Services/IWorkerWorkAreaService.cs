using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>The work area of the signed-in freelancer (contract workers.md 2.3B, decisions D24). The worker is read from ICurrentUser.</summary>
public interface IWorkerWorkAreaService
{
    /// <summary>The registered area, or <c>IsSet</c> false when there is none.</summary>
    Task<ApiResponse<WorkerWorkAreaResponse>> GetAsync(CancellationToken ct = default);

    /// <summary>Validates and stores the base point, the radius (5, 10 or 15 km) and the districts; a bad field is a 400 with field errors.</summary>
    Task<ApiResponse<WorkerWorkAreaResponse>> SaveAsync(UpdateWorkerWorkAreaRequest? request, CancellationToken ct = default);
}
