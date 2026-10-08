using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>The Admin operations dashboard (contract admin.md 2.3). Policy AdminOnly.</summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = "AdminOnly")]
public class AdminDashboardController(IAdminDashboardService dashboard) : ControllerBase
{
    /// <summary>
    /// Orders created today and this ISO week, shifts in progress and completed today, open disputes and those near their SLA. Days and
    /// the week are those of Asia/Ho_Chi_Minh. Only these three groups: no other metric is added without the leader (decision G-7).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResponse<DashboardDto>.Ok(await dashboard.GetAsync(ct), "Dashboard retrieved."));
}
