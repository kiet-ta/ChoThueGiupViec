using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>Body of the unlock: why the Admin unlocks the worker (written to the audit log, decision G-5).</summary>
public sealed class WorkerUnlockRequest
{
    public string? Reason { get; init; }
}

/// <summary>
/// Admin unlock of a locked freelancer (contract admin.md section 2.6, decisions Q15). Policy AdminOnly; the Admin id for the audit row
/// comes from the token.
/// </summary>
[ApiController]
[Route("api/admin/workers/{workerId:int}/unlock")]
[Authorize(Policy = "AdminOnly")]
public class AdminWorkerUnlockController(IWorkerUnlockService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>LOCKED -> IDLE for a freelancer whose eKYC is approved (409 with the reasons otherwise).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<WorkerUnlockDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unlock(int workerId, [FromBody] WorkerUnlockRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));

        var result = await service.UnlockAsync(adminId, workerId, request?.Reason, ct);
        if (result.Success && result.Data is not null) return Ok(ApiResponse<WorkerUnlockDto>.Ok(result.Data, "Worker unlocked."));

        object? data = null;
        if (result.ValidationErrors is not null) data = new { errors = result.ValidationErrors };
        else if (result.BlockReasons is not null) data = new { blockReasons = result.BlockReasons };

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
