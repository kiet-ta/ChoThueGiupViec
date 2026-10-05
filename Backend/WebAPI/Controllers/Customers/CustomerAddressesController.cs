using CommonService.Application.Common.Models;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Customers;

/// <summary>
/// Address book of the signed-in customer (contract customers.md §2.2). Every endpoint requires policy CustomerOnly
/// and uses the id from the token (ICurrentUser), never from the URL or body. Another customer's address is a 404.
/// </summary>
[ApiController]
[Route("api/customers/me/addresses")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerAddressesController : ControllerBase
{
    private readonly ICustomerAddressService _addresses;
    private readonly ICurrentUser _currentUser;

    public CustomerAddressesController(ICustomerAddressService addresses, ICurrentUser currentUser)
    {
        _addresses = addresses;
        _currentUser = currentUser;
    }

    /// <summary>Lists the caller's addresses, default first then newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AddressDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        return Map(await _addresses.ListAsync(customerId, ct), "Addresses retrieved.");
    }

    /// <summary>Returns one address of the caller.</summary>
    [HttpGet("{addressId:int}")]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int addressId, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        return Map(await _addresses.GetAsync(customerId, addressId, ct), "Address retrieved.");
    }

    /// <summary>Creates an address. The first address becomes the default. totalAreaM2 is computed by the server.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] AddressRequestDto request, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        return Map(await _addresses.CreateAsync(customerId, request, ct), "Address created.");
    }

    /// <summary>Replaces an address of the caller (same body and validation as create).</summary>
    [HttpPut("{addressId:int}")]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int addressId, [FromBody] AddressRequestDto request, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        return Map(await _addresses.UpdateAsync(customerId, addressId, request, ct), "Address updated.");
    }

    /// <summary>Deletes an address of the caller. 409 while an order references it.</summary>
    [HttpDelete("{addressId:int}")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int addressId, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        var result = await _addresses.DeleteAsync(customerId, addressId, ct);
        if (result.Success)
        {
            return Ok(ApiResponse<object?>.Ok(null, "Address deleted."));
        }

        return MapFailure(result.StatusCode, result.ErrorMessage, result.ValidationErrors);
    }

    private IActionResult Map<T>(CustomerAddressResult<T> result, string successMessage)
    {
        if (result.Success && result.Data != null)
        {
            var body = ApiResponse<T>.Ok(result.Data, successMessage);
            return result.StatusCode == StatusCodes.Status201Created
                ? StatusCode(StatusCodes.Status201Created, body)
                : Ok(body);
        }

        return MapFailure(result.StatusCode, result.ErrorMessage, result.ValidationErrors);
    }

    private IActionResult MapFailure(int statusCode, string? message, IDictionary<string, string[]>? validationErrors) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => BadRequest(ApiResponse<object>.Fail(
                message ?? "Validation failed",
                validationErrors != null ? new { errors = validationErrors } : null)),

            StatusCodes.Status404NotFound => NotFound(ApiResponse<object>.Fail(message ?? "Address not found.", null)),

            StatusCodes.Status409Conflict => Conflict(ApiResponse<object>.Fail(message ?? "Conflict.", null)),

            _ => StatusCode(statusCode, ApiResponse<object>.Fail(message ?? "Request failed.", null))
        };
}
