using CommonService.Application.Features.Admin.Dtos;

namespace CommonService.Application.Features.Admin.Services;

/// <summary>GET /api/v1/admin/workers: the Admin finds a worker by name, phone, national id or number (ADMIN-UAT-14).</summary>
public interface IAdminWorkerQueryService
{
    Task<AdminWorkerPageDto> SearchAsync(
        string? q, string? workerType, string? workStatus, string? page, string? pageSize, CancellationToken cancellationToken = default);
}
