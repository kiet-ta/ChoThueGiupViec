using CommonService.Application.Common.Models;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Customers;

/// <summary>
/// Favorite workers of the signed-in customer (contract customers.md §2.3). Every endpoint requires policy CustomerOnly
/// and uses the id from the token (ICurrentUser), never from the URL or body. Add and remove are idempotent.
/// </summary>
[ApiController]
[Route("api/customers/me/favorite-workers")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerFavoriteWorkersController : ControllerBase
{
    private readonly ICustomerFavoriteWorkerService _favorites;
    private readonly ICurrentUser _currentUser;

    public CustomerFavoriteWorkersController(ICustomerFavoriteWorkerService favorites, ICurrentUser currentUser)
    {
        _favorites = favorites;
        _currentUser = currentUser;
    }

    /// <summary>Lists the caller's favorite workers, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FavoriteWorkerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        var result = await _favorites.ListAsync(customerId, ct);
        return Ok(ApiResponse<IReadOnlyList<FavoriteWorkerDto>>.Ok(result.Data!, "Favorite workers retrieved."));
    }

    /// <summary>Adds a worker to the caller's favorites. Adding a worker already in the list returns the existing row.</summary>
    [HttpPut("{workerId:int}")]
    [ProducesResponseType(typeof(ApiResponse<FavoriteWorkerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(int workerId, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        var result = await _favorites.AddAsync(customerId, workerId, ct);
        if (result.Success && result.Data != null)
        {
            return Ok(ApiResponse<FavoriteWorkerDto>.Ok(result.Data, "Favorite worker saved."));
        }

        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", null));
    }

    /// <summary>Removes a worker from the caller's favorites. Removing a worker that is not in the list is still 200.</summary>
    [HttpDelete("{workerId:int}")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(int workerId, CancellationToken ct)
    {
        if (_currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        await _favorites.RemoveAsync(customerId, workerId, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Favorite worker removed."));
    }
}
