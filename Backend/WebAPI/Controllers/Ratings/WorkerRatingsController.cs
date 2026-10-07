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
/// The worker rates the customer of an assignment (contract ratings.md section 2.2, BR-09). Policy WorkerOnly. The
/// rating is internal (decisions Q14): no endpoint returns it to the customer and none lists it back to the worker.
/// </summary>
[ApiController]
[Route("api/workers/me/assignments/{assignmentId:long}")]
[Authorize(Policy = "WorkerOnly")]
public class WorkerRatingsController(IRatingService ratings, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Whether the worker can still rate this assignment, and until when.</summary>
    [HttpGet("rating-window")]
    [ProducesResponseType(typeof(ApiResponse<RatingWindowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWindow(long assignmentId, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await ratings.GetWindowAsync(RaterSide.Worker, workerId, assignmentId, ct), "Rating window retrieved.");
    }

    /// <summary>Rates the customer: stars 1-5 and the criteria cooperation, workingConditions (each 1-5).</summary>
    [HttpPost("rating")]
    [ProducesResponseType(typeof(ApiResponse<RatingDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(long assignmentId, [FromBody] SubmitRatingRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return this.ToActionResult(await ratings.SubmitAsync(RaterSide.Worker, workerId, assignmentId, request, ct), "Rating saved.");
    }
}
