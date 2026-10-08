using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payouts;

/// <summary>
/// The freelancer's own income screens (contract payouts.md 2.5, decision Q11). Policy WorkerOnly; the worker id comes from the token, so a
/// worker can only ever read their own money.
/// </summary>
[ApiController]
[Route("api/workers/me")]
[Authorize(Policy = "WorkerOnly")]
public class WorkerEarningsController(IWorkerEarningsService earnings, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The income of a month (<c>month</c> = <c>YYYY-MM</c>, default the current month): jobs, gross, commission, penalty, net and whether
    /// the month's payout is built (PENDING) or transferred. 400 for a bad or future month; 403 for an agency staff member (the agency is paid).
    /// </summary>
    [HttpGet("earnings")]
    [ProducesResponseType(typeof(ApiResponse<WorkerEarningsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Earnings([FromQuery] string? month, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await earnings.GetEarningsAsync(workerId, month, ct), "Earnings retrieved.");
    }

    /// <summary>The worker's own items of closed payout batches, newest month first; <c>pageSize</c> is 1-100, default 20.</summary>
    [HttpGet("payouts")]
    [ProducesResponseType(typeof(ApiResponse<WorkerPayoutHistoryPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Payouts([FromQuery] string? page, [FromQuery] string? pageSize, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await earnings.GetPayoutsAsync(workerId, page, pageSize, ct), "Payout history retrieved.");
    }

    private IActionResult ToResult<T>(PayoutResult<T> result, string successMessage)
    {
        if (result.Success && result.Data is not null)
        {
            return StatusCode(result.StatusCode, ApiResponse<T>.Ok(result.Data, successMessage));
        }

        object? data = result.ValidationErrors is not null ? new { errors = result.ValidationErrors } : null;
        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
