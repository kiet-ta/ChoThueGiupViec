using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>Admin view of the field: force majeure incidents and their re-dispatch (contract dispatch.md 3.3, BR-10). Policy AdminOnly.</summary>
[ApiController]
[Route("api/dispatch/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminDispatchController(IAdminIncidentQuery incidents) : ControllerBase
{
    /// <summary>The incidents, newest first (at most 200).</summary>
    [HttpGet("incidents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AdminIncidentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Incidents(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AdminIncidentDto>>.Ok(await incidents.ListAsync(AdminIncidentStatus.MaxListed, ct), "Incidents retrieved."));
}
