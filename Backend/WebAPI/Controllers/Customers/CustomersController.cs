using CommonService.Application.Common.Models;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Customers;

/// <summary>
/// Signed-in customer's own data (contract customers.md). Every endpoint requires policy CustomerOnly and
/// uses the id from the token (ICurrentUser), never from the URL or body.
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = "CustomerOnly")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerProfileService _profiles;
    private readonly ICurrentUser _currentUser;

    public CustomersController(ICustomerProfileService profiles, ICurrentUser currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    /// <summary>Returns the profile of the signed-in customer (contract §2.1).</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CustomerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        return Map(await _profiles.GetAsync(customerId, ct), "Profile retrieved.");
    }

    /// <summary>Replaces the editable profile fields fullName and email (contract §2.1).</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<CustomerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCustomerProfileDto request, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        return Map(await _profiles.UpdateAsync(customerId, request, ct), "Profile updated.");
    }

    private IActionResult Map(CustomerProfileResult result, string successMessage)
    {
        if (result.Success && result.Data != null)
        {
            return Ok(ApiResponse<CustomerProfileDto>.Ok(result.Data, successMessage));
        }

        return result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => BadRequest(ApiResponse<object>.Fail(
                result.ErrorMessage ?? "Validation failed",
                result.ValidationErrors != null ? new { errors = result.ValidationErrors } : null)),

            StatusCodes.Status404NotFound => NotFound(ApiResponse<object>.Fail(
                result.ErrorMessage ?? "Customer not found.", null)),

            _ => StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", null))
        };
    }
}
