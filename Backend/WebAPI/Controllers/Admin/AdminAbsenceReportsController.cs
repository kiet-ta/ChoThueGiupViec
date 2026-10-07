using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>
/// The Admin queue and decision on "customer absent" reports (contract admin.md 2.2, BR-05, decision Q10). Policy AdminOnly; the
/// admin id for the audit row comes from the token.
/// </summary>
[ApiController]
[Route("api/admin/absence-reports")]
[Authorize(Policy = "AdminOnly")]
public class AdminAbsenceReportsController(IAbsenceReportService reports, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The reports, oldest report first. <c>status</c> is PENDING (default), APPROVED or REJECTED; <c>pageSize</c> is 1-100, default 20.
    /// Each item says whether it <c>canApprove</c> and, if not, which <c>blockReasons</c> hold.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AbsenceReportPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? status, [FromQuery] string? page, [FromQuery] string? pageSize, CancellationToken ct) =>
        ToResult(await reports.SearchAsync(status, page, pageSize, ct), "Absence reports retrieved.");

    /// <summary>One report; 404 when the assignment has no absence report.</summary>
    [HttpGet("{assignmentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<AbsenceReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long assignmentId, CancellationToken ct) =>
        ToResult(await reports.GetAsync(assignmentId, ct), "Absence report retrieved.");

    /// <summary>
    /// Approves the absence (Q10): the worker keeps 40 % of the job value, 60 % goes back to the customer, the assignment becomes ABSENT.
    /// 409 with <c>blockReasons</c> when GPS, calls, waiting time or the report itself are missing, and when already decided; 502 when
    /// the refund is refused (nothing changes).
    /// </summary>
    [HttpPost("{assignmentId:long}/approve")]
    [ProducesResponseType(typeof(ApiResponse<AbsenceReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Approve(long assignmentId, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await reports.ApproveAsync(adminId, assignmentId, ct), "Absence approved.");
    }

    /// <summary>Rejects the report with a reason (1-255 characters); the rejection is only recorded in the audit log.</summary>
    [HttpPost("{assignmentId:long}/reject")]
    [ProducesResponseType(typeof(ApiResponse<AbsenceReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(long assignmentId, [FromBody] RejectAbsenceRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return ToResult(await reports.RejectAsync(adminId, assignmentId, request.Reason, ct), "Absence rejected.");
    }

    private IActionResult ToResult<T>(AbsenceResult<T> result, string successMessage)
    {
        if (result.Success && result.Data is not null)
        {
            return StatusCode(result.StatusCode, ApiResponse<T>.Ok(result.Data, successMessage));
        }

        object? data = null;
        if (result.ValidationErrors is not null) data = new { errors = result.ValidationErrors };
        else if (result.BlockReasons is not null) data = new { blockReasons = result.BlockReasons };

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
