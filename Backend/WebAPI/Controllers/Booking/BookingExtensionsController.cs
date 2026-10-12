using CommonService.Application.Common.Models;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Booking;

/// <summary>"Làm lần 2" (BR-08), contract booking.md 3.8. Policy CustomerOnly.</summary>
[ApiController]
[Route("api/booking/orders/{orderId:long}/extensions")]
[Authorize(Policy = "CustomerOnly")]
public class BookingExtensionsController(IExtensionRequestService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Asks the worker on site to stay longer; the extension waits for its payment.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ExtensionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(long orderId, [FromBody] CreateExtensionRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        var extension = await service.CreateAsync(customerId, orderId, request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ExtensionDto>.Ok(extension, "Extension requested."));
    }
}
