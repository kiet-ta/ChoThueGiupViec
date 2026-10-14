using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payments.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payments;

/// <summary>
/// UAT-PAY: lists the open sandbox payments and confirms one as if the Fake gateway's callback had arrived. Anonymous, like the Fake OTP
/// sender it belongs with, and reachable in Development only (404 anywhere else). Not part of the API contract: hidden from OpenAPI.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/dev/payments")]
[AllowAnonymous]
public class DevPaymentsController(DevPaymentService service, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevPaymentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(ApiResponse<IReadOnlyList<DevPaymentDto>>.Ok(await service.ListAsync(ct)));
    }

    [HttpPost("{paymentId:long}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<DevConfirmDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(long paymentId, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var outcome = await service.ConfirmAsync(paymentId, ct);
        return Ok(ApiResponse<DevConfirmDto>.Ok(new DevConfirmDto(outcome == IpnOutcome.Accepted ? "CONFIRMED" : "ALREADY_PROCESSED")));
    }
}

/// <summary>Answer of the confirm call: <c>CONFIRMED</c> or, for a repeat, <c>ALREADY_PROCESSED</c>.</summary>
public sealed record DevConfirmDto(string Result);
