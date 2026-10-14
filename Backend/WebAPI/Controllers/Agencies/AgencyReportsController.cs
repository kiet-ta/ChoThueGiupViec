using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>The agency's own jobs and performance report (contract agencies.md 8, BE-M5-08, decisions Q08, policy PartnerOnly).</summary>
[ApiController]
[Route("api/agencies")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyReportsController(AgencyReportService service) : ControllerBase
{
    /// <summary>The company's jobs (assignments with its agency id), newest first, optionally one status.</summary>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(ApiResponse<AgencyJobPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Orders(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse<AgencyJobPageDto>.Ok(await service.ListJobsAsync(status, page, pageSize, ct)));

    /// <summary>Per-worker performance of a period (default the last 30 days). Pro package only: 409 FEATURE_NOT_IN_PLAN on FREE.</summary>
    [HttpGet("reports/performance")]
    [ProducesResponseType(typeof(ApiResponse<PerformanceReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Performance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse<PerformanceReportDto>.Ok(await service.GetPerformanceAsync(from, to, ct)));
}
