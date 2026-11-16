using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>Admin lookup of workers (contract admin.md section 2.8, ADMIN-UAT-14). Policy AdminOnly; read-only.</summary>
[ApiController]
[Route("api/v1/admin/workers")]
[Authorize(Policy = "AdminOnly")]
public class AdminWorkersController(IAdminWorkerQueryService queries) : ControllerBase
{
    /// <summary>
    /// Finds workers. <c>q</c> is a whole number (that worker) or a substring of the full name, phone number or national id; empty lists
    /// every worker. <c>pageSize</c> is 1-50, default 20.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminWorkerPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? workerType,
        [FromQuery] string? workStatus,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken ct)
    {
        var result = await queries.SearchAsync(q, workerType, workStatus, page, pageSize, ct);
        return Ok(ApiResponse<AdminWorkerPageDto>.Ok(result, "Workers retrieved."));
    }
}
