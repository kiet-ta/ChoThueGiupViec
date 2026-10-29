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
    /// <summary>
    /// Handles a MoMo (sandbox) callback: 204 No Content when accepted or repeated (what MoMo requires, within 15 seconds),
    /// 400 when rejected.
    /// </summary>
    [HttpPost("momo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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
            : NoContent();
    }

    /// <summary>
    /// Handles a VNPay (sandbox) callback: a GET whose query holds the <c>vnp_</c> fields. VNPay wants HTTP 200 with
    /// <c>RspCode</c>: 00 accepted, 02 already processed (both end its retries), 99 rejected (it retries; the answer gives no reason).
    /// </summary>
    [HttpGet("vnpay")]
    [ApiExplorerSettings(IgnoreApi = true)] // gateway-facing; not part of the client contract snapshot (.spec/contracts/openapi.json)
    public async Task<IActionResult> VnPay(CancellationToken ct)
    {
        var payload = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
        var outcome = await ipn.HandleAsync(payload, Request.QueryString.Value ?? string.Empty, ct);
        // A dictionary, not an anonymous object: the JSON naming policy would turn RspCode into rspCode, which VNPay does not read.
        return outcome switch
        {
            IpnOutcome.Accepted => Ok(VnPayAnswer("00", "Confirm Success")),
            IpnOutcome.AlreadyProcessed => Ok(VnPayAnswer("02", "Order already confirmed")),
            _ => Ok(VnPayAnswer("99", "Rejected")),
        };
    }

    private static Dictionary<string, string> VnPayAnswer(string code, string message) =>
        new() { ["RspCode"] = code, ["Message"] = message };
}
