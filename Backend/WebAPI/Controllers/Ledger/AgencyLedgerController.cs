using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ledger;

/// <summary>
/// The agency's own transaction history (contract ledger.md section 9, policy PartnerOnly): its wallet and its escrow account. The agency
/// id comes from the token, so an agency only ever sees its own money.
/// </summary>
[ApiController]
[Route("api/v1/agencies/me")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyLedgerController(ILedgerHistoryService history, ILedgerExportService export, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The agency's statement as an <c>.xlsx</c> file (not the JSON envelope; errors keep it): its wallet and its escrow account, each with
    /// an opening and a closing balance row. The same <c>from</c>, <c>to</c> and <c>type</c> filters as the history, no paging.
    /// </summary>
    [HttpGet("ledger/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, CancellationToken ct)
    {
        if (currentUser.UserId is not int agencyId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        var file = await export.ExportAgencyAsync(agencyId, new LedgerHistoryRequest(from, to, type, null, null), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// The agency's history, newest first, each row on its account (<c>AGENCY</c> or <c>AGENCY_ESCROW</c>) with a signed amount, and the
    /// balances of both accounts before the first day and before the day after the last one. <c>from</c> and <c>to</c> are Vietnam days
    /// (<c>yyyy-MM-dd</c>), <c>type</c> an entry type; <c>pageSize</c> is 1-100, default 20.
    /// </summary>
    [HttpGet("ledger")]
    [ProducesResponseType(typeof(ApiResponse<PartyLedgerPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Ledger(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        currentUser.UserId is not int agencyId
            ? Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null))
            : Ok(ApiResponse<PartyLedgerPageDto>.Ok(
                await history.GetAgencyHistoryAsync(agencyId, new LedgerHistoryRequest(from, to, type, page, pageSize), ct), "Ledger retrieved."));
}
