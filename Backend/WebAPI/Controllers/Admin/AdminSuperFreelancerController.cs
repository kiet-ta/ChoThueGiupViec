using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>Body of both endpoints: why the Admin approves or revokes (written to the audit log, decision G-5).</summary>
public sealed class SuperFreelancerReasonRequest
{
    public string? Reason { get; init; }
}

/// <summary>
/// Approve or revoke the Super-Freelancer flag of a freelancer (contract admin.md section 2.5, decisions Q12). Policy
/// AdminOnly; the Admin id for the audit row comes from the token.
/// </summary>
[ApiController]
[Route("api/admin/workers/{workerId:int}/super-freelancer")]
[Authorize(Policy = "AdminOnly")]
public class AdminSuperFreelancerController(ISuperFreelancerService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Approves when Q12 holds (409 with the failing criteria otherwise).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SuperFreelancerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int workerId, [FromBody] SuperFreelancerReasonRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await service.ApproveAsync(adminId, workerId, request?.Reason, ct), "Super-Freelancer approved.");
    }

    /// <summary>Revokes the flag manually.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<SuperFreelancerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(int workerId, [FromBody] SuperFreelancerReasonRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return ToResult(await service.RevokeAsync(adminId, workerId, request?.Reason, ct), "Super-Freelancer revoked.");
    }

    private IActionResult ToResult(SuperFreelancerResult result, string successMessage)
    {
        if (result.Success && result.Data is not null)
        {
            return Ok(ApiResponse<SuperFreelancerDto>.Ok(result.Data, successMessage));
        }

        object? data = null;
        if (result.ValidationErrors is not null) data = new { errors = result.ValidationErrors };
        else if (result.FailedCriteria is not null) data = new { failedCriteria = result.FailedCriteria };

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
