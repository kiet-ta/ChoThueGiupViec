using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payouts;

/// <summary>
/// The Admin's read view of the monthly payout batches (contract payouts.md 2.2). Policy AdminOnly. A month is settled and closed by
/// the system from the ledger (decision D28), so there is no build and no confirm here. No money moves in the system (decision G-1).
/// </summary>
[ApiController]
[Route("api/v1/admin/payout-batches")]
[Authorize(Policy = "AdminOnly")]
public class AdminPayoutBatchesController(IPayoutBatchService batches) : ControllerBase
{
    /// <summary>The batches, newest month first; <c>pageSize</c> is 1-100, default 20. <c>sort</c> (period, status, items, total, closedAt) and <c>dir</c> (asc, desc) order all pages.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? page, [FromQuery] string? pageSize, [FromQuery] string? sort, [FromQuery] string? dir, CancellationToken ct) =>
        ToResult(await batches.ListAsync(page, pageSize, sort, dir, ct), "Payout batches retrieved.");

    /// <summary>One batch with its items (optionally only FREELANCER or AGENCY) and a warning for each payee without a bank account.</summary>
    [HttpGet("{batchId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int batchId, [FromQuery] string? payeeType, [FromQuery] string? page, [FromQuery] string? pageSize, CancellationToken ct) =>
        ToResult(await batches.GetAsync(batchId, payeeType, page, pageSize, ct), "Payout batch retrieved.");

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
