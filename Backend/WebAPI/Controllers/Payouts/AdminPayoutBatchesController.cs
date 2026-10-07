using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payouts;

/// <summary>
/// The Admin's monthly payout batches (contract payouts.md 2.1, 2.2, 2.4). Policy AdminOnly; the admin id for the audit row and
/// <c>confirmed_by</c> comes from the token. No money moves in the system (decision G-1).
/// </summary>
[ApiController]
[Route("api/admin/payout-batches")]
[Authorize(Policy = "AdminOnly")]
public class AdminPayoutBatchesController(IPayoutBatchService batches, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Builds the batch of a finished month (<c>periodMonth</c> = <c>YYYY-MM</c>): 201 for a new one, 200 when the month's DRAFT batch was
    /// rebuilt from the current data (same <c>batchId</c>); 409 for a CLOSED batch, which never changes.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Build([FromBody] BuildPayoutBatchRequestDto request, CancellationToken ct)
    {
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        var result = await batches.BuildAsync(request.PeriodMonth, ct);
        return ToResult(result, result.StatusCode == 201 ? "Payout batch created." : "Payout batch rebuilt.");
    }

    /// <summary>The batches, newest month first; <c>pageSize</c> is 1-100, default 20.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? page, [FromQuery] string? pageSize, CancellationToken ct) =>
        ToResult(await batches.ListAsync(page, pageSize, ct), "Payout batches retrieved.");

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

    /// <summary>
    /// Records that the Admin made the transfers outside the system: the batch becomes CLOSED, every item TRANSFERRED and
    /// <c>PayoutBatchClosed</c> is published. 409 when already closed, empty, or the month is not over.
    /// </summary>
    [HttpPost("{batchId:int}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<PayoutBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(int batchId, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await batches.ConfirmAsync(adminId, batchId, ct), "Payout batch closed.");
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
