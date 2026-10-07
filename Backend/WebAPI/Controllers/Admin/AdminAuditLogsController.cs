using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>
/// Read-only history of ADMIN_AUDIT_LOG (contract admin.md section 2.4). Policy AdminOnly. There is deliberately no
/// write action: the table is append-only and rows are only added through the IAuditLog port (decisions G-5).
/// </summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Policy = "AdminOnly")]
public class AdminAuditLogsController(IAuditLogQueryService queries) : ControllerBase
{
    /// <summary>Lists audit rows, newest first, with optional filters (AND) and paging.</summary>
    /// <remarks>
    /// <c>from</c> is inclusive and <c>to</c> exclusive (ISO-8601, UTC when no offset is given); <c>pageSize</c> is 1-100, default 20.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AuditLogPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? actorType,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken ct)
    {
        var result = await queries.SearchAsync(entityType, entityId, actorType, from, to, page, pageSize, ct);
        if (result.Success && result.Data is not null)
        {
            return Ok(ApiResponse<AuditLogPageDto>.Ok(result.Data, "Audit log retrieved."));
        }

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(
            result.ErrorMessage ?? "Request failed.",
            result.ValidationErrors is not null ? new { errors = result.ValidationErrors } : null));
    }
}
