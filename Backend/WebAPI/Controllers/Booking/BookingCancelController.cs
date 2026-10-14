using CommonService.Application.Common.Models;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Booking;

/// <summary>Customer cancel of an order (contract booking.md 3.7, Q15). Policy CustomerOnly.</summary>
[ApiController]
[Route("api/booking/orders/{orderId:long}/cancel")]
[Authorize(Policy = "CustomerOnly")]
public class BookingCancelController(IOrderCancellationService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Cancels the order; paid orders are refunded 100 % (an assigned order within 2 hours of the shift cannot be cancelled yet).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long orderId, [FromBody] CancelOrderRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        var result = await service.CancelAsync(customerId, orderId, request, ct);
        var message = result.RefundProblem is null
            ? "Order cancelled."
            : "Order cancelled, but the refund could not be completed yet and will be handled manually.";
        return Ok(ApiResponse<OrderDto>.Ok(OrderDto.From(result.Order), message));
    }
}
