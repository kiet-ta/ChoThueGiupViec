using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>
/// The Admin's queue of requests to change a freelancer's payout account (LEDGER-28, contract workers.md bank section and admin.md). Policy
/// AdminOnly; the admin id for the audit row and <c>reviewed_by</c> comes from the token.
/// </summary>
[ApiController]
[Route("api/v1/admin/bank-change-requests")]
[Authorize(Policy = "AdminOnly")]
public class AdminBankChangeRequestsController(IAdminBankChangeService requests, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The requests of every worker, optionally of one <c>status</c> (PENDING, APPROVED, REJECTED); <c>pageSize</c> is 1-100, default 20.
    /// The pending queue is oldest first, the other lists newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminBankChangePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(ApiResponse<AdminBankChangePageDto>.Ok(await requests.ListAsync(status, page, pageSize, ct), "Bank change requests retrieved."));

    /// <summary>
    /// Approves a pending request: the new account is written on the worker and the request becomes APPROVED in one transaction.
    /// 409 <c>BANK_CHANGE_NOT_PENDING</c> when it was already decided.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(ApiResponse<AdminBankChangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct) =>
        currentUser.UserId is not int adminId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<AdminBankChangeDto>.Ok(await requests.ApproveAsync(adminId, id, ct), "Bank change request approved."));

    /// <summary>Rejects a pending request with a reason of 1-255 characters; the worker's account does not change.</summary>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(ApiResponse<AdminBankChangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectBankChangeRequest? request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return Ok(ApiResponse<AdminBankChangeDto>.Ok(await requests.RejectAsync(adminId, id, request?.Reason, ct), "Bank change request rejected."));
    }
}
