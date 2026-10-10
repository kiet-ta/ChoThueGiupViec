using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payments;

/// <summary>Customer payments (contract payments.md 2.1). Policy CustomerOnly. Sandbox only, no real money (Q04).</summary>
[ApiController]
[Route("api/payments")]
[Authorize(Policy = "CustomerOnly")]
public class PaymentsController(IPaymentQrService qrService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Creates the payment QR of an order, or returns the existing PENDING one (201 created, 200 existing).</summary>
    [HttpPost("orders/{orderId:long}/qr")]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CreateOrderQr(long orderId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        try
        {
            var result = await qrService.CreateOrderQrAsync(customerId, orderId, ct);
            var body = ApiResponse<PaymentDto>.Ok(result.Payment, result.Created ? "Payment QR created." : "Payment QR already exists.");
            return result.Created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ApiResponse<object>.Fail(ex.Message));
        }
    }
}
