using System.Text.Json;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payments.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payments;

/// <summary>
/// Gateway-facing payment callback (contract payments.md 2.4). Anonymous on purpose: the caller is the payment gateway, and
/// the signature check inside <see cref="IIpnService"/> is the authentication. The rejection answer carries no reason.
/// The exact MoMo format is BE-M2-06 (G-7); until then the body is a flat JSON object of the Fake gateway's keys.
/// </summary>
[ApiController]
[Route("api/payments/ipn")]
[AllowAnonymous]
public class PaymentIpnController(IIpnService ipn) : ControllerBase
{
    /// <summary>Handles a MoMo (sandbox) callback: 200 when accepted or repeated, 400 when rejected.</summary>
    [HttpPost("momo")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Momo([FromBody] JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(ApiResponse<object>.Fail("IPN rejected."));
        }

        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            payload[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => property.Value.GetRawText(),
            };
        }

        var outcome = await ipn.HandleAsync(payload, body.GetRawText(), ct);
        return outcome == IpnOutcome.Rejected
            ? BadRequest(ApiResponse<object>.Fail("IPN rejected."))
            : Ok(ApiResponse<object?>.Ok(null, "IPN processed."));
    }
}
