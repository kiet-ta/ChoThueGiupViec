using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>
/// The 30-minute window to replace a worker of an assigned Premium job with another worker of the agency (contract agencies.md 7,
/// BE-M5-09, decisions Q09, policy PartnerOnly). Someone else's assignment answers 404.
/// </summary>
[ApiController]
[Route("api/agencies")]
[Authorize(Policy = "PartnerOnly")]
public class AgencySwapController(AgencySwapService service) : ControllerBase
{
    /// <summary>The agency's open windows with the seconds left (the dashboard countdown).</summary>
    [HttpGet("swap-windows")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SwapWindowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SwapWindowDto>>.Ok(await service.ListOpenAsync(ct)));

    /// <summary>Reports that the assigned worker cannot work: opens the window (more than 2 h of notice) or hands over to the rescue at once.</summary>
    [HttpPost("assignments/{assignmentId:long}/unavailable")]
    [ProducesResponseType(typeof(ApiResponse<SwapWindowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unavailable(long assignmentId, [FromBody] UnavailableRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SwapWindowDto>.Ok(await service.ReportUnavailableAsync(assignmentId, request, ct)));

    /// <summary>Replaces the worker with another worker of the agency who has a free roster slot for the shift.</summary>
    [HttpPost("assignments/{assignmentId:long}/swap")]
    [ProducesResponseType(typeof(ApiResponse<SwapWindowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Swap(long assignmentId, [FromBody] SwapRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SwapWindowDto>.Ok(await service.SwapWorkerAsync(assignmentId, request, ct)));
}
