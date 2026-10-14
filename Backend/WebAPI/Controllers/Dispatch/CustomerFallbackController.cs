using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>Request body of the fallback answer (contract dispatch.md 3.2).</summary>
public sealed record FallbackAnswerRequest(bool? Accept);

/// <summary>
/// Customer side of the BR-02 fallback (contract dispatch.md 3.2, policy CustomerOnly): an order with one worker and no second worker
/// waits for the customer's choice. Someone else's order answers 404; an order that is not waiting answers 409 with data.code.
/// </summary>
[ApiController]
[Route("api/dispatch/orders/{orderId:long}/fallback")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerFallbackController(ConsecutiveShiftFallbackService service) : ControllerBase
{
    /// <summary>Whether the order waits for the customer (AWAITING_CUSTOMER), for the worker (AWAITING_WORKER) or not at all.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<FallbackStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long orderId, CancellationToken ct) =>
        Ok(ApiResponse<FallbackStatusDto>.Ok(await service.GetAsync(orderId, ct)));

    /// <summary>Agrees (the worker is asked to confirm the second consecutive shift) or declines (cancel + 100 % refund).</summary>
    [HttpPost("respond")]
    [ProducesResponseType(typeof(ApiResponse<FallbackResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Respond(long orderId, [FromBody] FallbackAnswerRequest request, CancellationToken ct) =>
        Ok(ApiResponse<FallbackResponseDto>.Ok(await service.RespondAsync(
            orderId,
            request.Accept ?? throw new ArgumentException("accept là bắt buộc."),
            ct)));
}
