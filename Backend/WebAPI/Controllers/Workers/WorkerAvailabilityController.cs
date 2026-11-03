using CommonService.Application.Common.Models;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Workers;

/// <summary>
/// Free shifts of a worker, for rebooking (contract booking.md 3.6). Policy CustomerOnly; an order that is not the caller's, or a worker
/// who did not complete an assignment on it, is 404.
/// </summary>
[ApiController]
[Route("api/v1/workers/{workerId:int}/availability")]
[Authorize(Policy = "CustomerOnly")]
public class WorkerAvailabilityController(IOrderQueryService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Free shifts of the worker from a completed assignment on the caller's order <c>orderId</c>; 400 without <c>orderId</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WorkerFreeShift>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RebookAvailability(int workerId, [FromQuery] long? orderId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        if (orderId is not > 0)
        {
            return BadRequest(ApiResponse<object>.Fail("orderId is required."));
        }

        return Ok(ApiResponse<IReadOnlyList<WorkerFreeShift>>.Ok(
            await service.GetRebookAvailabilityAsync(customerId, orderId.Value, workerId, ct), "Worker availability retrieved."));
    }
}
