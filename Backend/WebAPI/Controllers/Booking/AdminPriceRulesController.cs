using CommonService.Application.Common.Models;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Booking;

/// <summary>
/// Admin price table (contract booking.md 4.1, 4.2). Policy AdminOnly. The change history is NOT served here: it is
/// <c>GET /api/admin/audit-logs?entityType=PRICE_RULE</c> (admin.md 2.4, decisions B12).
/// </summary>
[ApiController]
[Route("api/admin/price-rules")]
[Authorize(Policy = "AdminOnly")]
public class AdminPriceRulesController(IPriceRuleAdminService service, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>All price rules, ordered by service tier then area bracket.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PriceRuleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PriceRuleDto>>.Ok(await service.ListAsync(ct), "Price rules retrieved."));

    /// <summary>Changes a unit price (new orders only). One audit row is written in the same transaction.</summary>
    [HttpPut("{ruleId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PriceRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int ruleId, [FromBody] UpdatePriceRuleRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized."));
        }

        var rule = await service.UpdateUnitPriceAsync(adminId, ruleId, request.UnitPrice, request.Reason ?? string.Empty, ct);
        return Ok(ApiResponse<PriceRuleDto>.Ok(rule, "Price rule updated."));
    }
}
