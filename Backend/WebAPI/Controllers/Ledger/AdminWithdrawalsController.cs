using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ledger;

/// <summary>Request body of a rejection: the reason, 1-255 characters.</summary>
public record RejectWithdrawalRequestDto(string? Reason);

/// <summary>
/// The Admin's queue of advance requests (contract ledger.md sections 6 and 9, decision E). Policy AdminOnly; the admin id for the audit
/// row and <c>reviewed_by</c> comes from the token. No real money moves (decision G-1): an approval is a ledger entry.
/// </summary>
[ApiController]
[Route("api/v1/admin/withdrawals")]
[Authorize(Policy = "AdminOnly")]
public class AdminWithdrawalsController(IAdminWithdrawalService withdrawals, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>The requests of every worker, optionally of one <c>status</c> (PENDING, PAID, REJECTED, CANCELLED); <c>pageSize</c> is 1-100, default 20.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminWithdrawalPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(ApiResponse<AdminWithdrawalPageDto>.Ok(await withdrawals.ListAsync(status, page, pageSize, ct), "Withdrawals retrieved."));

    /// <summary>
    /// Approves a pending request: in one transaction the withdrawable amount is recomputed, the WITHDRAWAL entry is written and the
    /// request becomes PAID. 409 <c>WITHDRAWAL_INSUFFICIENT</c> when the amount is no longer withdrawable (reject it instead);
    /// 409 <c>WITHDRAWAL_NOT_PENDING</c> when it was already decided.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(ApiResponse<WithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct) =>
        currentUser.UserId is not int adminId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<WithdrawalDto>.Ok(await withdrawals.ApproveAsync(adminId, id, ct), "Withdrawal approved."));

    /// <summary>Rejects a pending request with a reason of 1-255 characters; no ledger entry is written.</summary>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(ApiResponse<WithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectWithdrawalRequestDto? request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return Ok(ApiResponse<WithdrawalDto>.Ok(await withdrawals.RejectAsync(adminId, id, request?.Reason, ct), "Withdrawal rejected."));
    }
}
