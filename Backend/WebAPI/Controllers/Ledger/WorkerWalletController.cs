using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ledger;

/// <summary>Request body of a withdrawal: a whole number of VND.</summary>
public record CreateWithdrawalRequestDto(decimal Amount);

/// <summary>
/// The freelancer's wallet and advance requests (contract ledger.md sections 6 and 9, decision E). Policy WorkerOnly; the worker id
/// comes from the token, so a worker only ever sees and changes their own money. Agency staff get 403 (the agency is paid).
/// </summary>
[ApiController]
[Route("api/v1/workers/me")]
[Authorize(Policy = "WorkerOnly")]
public class WorkerWalletController(IWithdrawalService withdrawals, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Balance, what can be withdrawn now, the amount held by a pending request and when the next earnings mature.</summary>
    [HttpGet("wallet")]
    [ProducesResponseType(typeof(ApiResponse<WalletDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Wallet(CancellationToken ct) =>
        currentUser.UserId is not int workerId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<WalletDto>.Ok(await withdrawals.GetWalletAsync(workerId, ct), "Wallet retrieved."));

    /// <summary>Asks for an advance. 409 <c>WITHDRAWAL_PENDING</c> while another request waits; 400 for an amount that is not a whole number, not positive or above what can be withdrawn.</summary>
    [HttpPost("withdrawals")]
    [ProducesResponseType(typeof(ApiResponse<WithdrawalDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateWithdrawalRequestDto? request, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        var created = await withdrawals.CreateAsync(workerId, request.Amount, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<WithdrawalDto>.Ok(created, "Withdrawal requested."));
    }

    /// <summary>The worker's own requests, newest first; <c>pageSize</c> is 1-100, default 20.</summary>
    [HttpGet("withdrawals")]
    [ProducesResponseType(typeof(ApiResponse<WithdrawalPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        currentUser.UserId is not int workerId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<WithdrawalPageDto>.Ok(await withdrawals.ListAsync(workerId, page, pageSize, ct), "Withdrawals retrieved."));

    /// <summary>Cancels the worker's own pending request. 404 for a missing or foreign one; 409 <c>WITHDRAWAL_NOT_PENDING</c> when it was already decided.</summary>
    [HttpPost("withdrawals/{id:int}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<WithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        currentUser.UserId is not int workerId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<WithdrawalDto>.Ok(await withdrawals.CancelAsync(workerId, id, ct), "Withdrawal cancelled."));
}
