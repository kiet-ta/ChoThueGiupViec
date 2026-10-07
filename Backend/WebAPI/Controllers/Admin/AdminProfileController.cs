using CommonService.Application.Common.Models;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Admin;

/// <summary>The signed-in Admin (contract admin.md section 2.1). Policy AdminOnly; the id comes from the token.</summary>
[ApiController]
[Route("api/admin/me")]
[Authorize(Policy = "AdminOnly")]
public class AdminProfileController(IAdminProfileService profiles, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>The caller's profile: id, email, name, role, active flag and creation time (never the password hash).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        var result = await profiles.GetAsync(adminId, ct);
        if (result.Success && result.Data is not null)
        {
            return Ok(ApiResponse<AdminProfileDto>.Ok(result.Data, "Admin profile retrieved."));
        }

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", null));
    }
}
