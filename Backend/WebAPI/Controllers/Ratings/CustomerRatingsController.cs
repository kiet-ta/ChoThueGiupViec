using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ratings;
using CommonService.Application.Features.Ratings.Dtos;
using CommonService.Application.Features.Ratings.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ratings;

/// <summary>
/// The customer rates the worker of an assignment (contract ratings.md section 2.1, BR-09). Policy CustomerOnly; the
/// customer id comes from the token, never from the URL or body. An assignment that is not the caller's is a 404.
/// </summary>
[ApiController]
[Route("api/customers/me/assignments/{assignmentId:long}")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerRatingsController(IRatingService ratings, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Whether the customer can still rate this assignment, and until when.</summary>
    [HttpGet("rating-window")]
    [ProducesResponseType(typeof(ApiResponse<RatingWindowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWindow(long assignmentId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await ratings.GetWindowAsync(RaterSide.Customer, customerId, assignmentId, ct), "Rating window retrieved.");
    }

    /// <summary>Rates the worker: stars 1-5 and the criteria punctuality, cleaningQuality, attitude (each 1-5).</summary>
    [HttpPost("rating")]
    [ProducesResponseType(typeof(ApiResponse<RatingDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(long assignmentId, [FromBody] SubmitRatingRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return this.ToActionResult(await ratings.SubmitAsync(RaterSide.Customer, customerId, assignmentId, request, ct), "Rating saved.");
    }
}
