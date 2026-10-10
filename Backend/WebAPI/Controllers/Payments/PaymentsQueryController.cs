using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payments;

/// <summary>Customer reads of payments (contract payments.md 2.3): the polling fallback when the realtime socket is down. Policy CustomerOnly.</summary>
[ApiController]
[Route("api/payments")]
[Authorize(Policy = "CustomerOnly")]
public class PaymentsQueryController(IPaymentReadService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>One payment of the caller.</summary>
    [HttpGet("{paymentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long paymentId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        return Ok(ApiResponse<PaymentDto>.Ok(await service.GetAsync(customerId, paymentId, ct), "Payment retrieved."));
    }

    /// <summary>Every payment (order and extension) of one of the caller's orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PaymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromQuery] long? orderId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        if (orderId is null or <= 0)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Validation failed", new { errors = new Dictionary<string, string[]> { ["orderId"] = ["orderId is required and must be greater than 0."] } }));
        }

        return Ok(ApiResponse<IReadOnlyList<PaymentDto>>.Ok(await service.ListForOrderAsync(customerId, orderId.Value, ct), "Payments retrieved."));
    }
}
