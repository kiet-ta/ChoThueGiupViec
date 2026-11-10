using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ledger;

/// <summary>The Admin's view of the system ledger (contract ledger.md section 9). Policy AdminOnly.</summary>
[ApiController]
[Route("api/v1/admin/ledger")]
[Authorize(Policy = "AdminOnly")]
public class AdminLedgerController(ILedgerReconciliationService reconciliation, ILedgerHistoryService history) : ControllerBase
{
    /// <summary>
    /// The ledger, newest entry first. <c>from</c> and <c>to</c> are Vietnam days (<c>yyyy-MM-dd</c>, both included), <c>type</c> an entry type,
    /// <c>partyType</c> (with an optional <c>partyId</c>) the accounts that sent or received; <c>pageSize</c> is 1-100, default 20.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminLedgerPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, [FromQuery] string? partyType, [FromQuery] int? partyId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(ApiResponse<AdminLedgerPageDto>.Ok(
            await history.GetAdminHistoryAsync(new LedgerHistoryRequest(from, to, type, page, pageSize, partyType, partyId), ct), "Ledger retrieved."));

    /// <summary>What the platform holds and owes: revenue, clearing, the net owed to workers and to agencies, and the pending withdrawals.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<LedgerSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        Ok(ApiResponse<LedgerSummaryDto>.Ok(await history.GetSummaryAsync(ct), "Ledger summary retrieved."));

    /// <summary>Recomputes the hash chain: <c>{ ok, checkedCount, firstBrokenEntryCode }</c>.</summary>
    [HttpGet("verify")]
    [ProducesResponseType(typeof(ApiResponse<LedgerChainResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Verify(CancellationToken ct) =>
        Ok(ApiResponse<LedgerChainResult>.Ok(await history.VerifyAsync(ct), "Ledger chain verified."));

    /// <summary>
    /// Source-to-ledger discrepancies without writing anything: per entry type the missing rows and amount (<c>excessAmount</c> is what
    /// refunds hold above the source). <c>items</c> is empty when every source is in the ledger.
    /// </summary>
    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ApiResponse<LedgerReconciliationReport>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Reconciliation(CancellationToken ct) =>
        Ok(ApiResponse<LedgerReconciliationReport>.Ok(await reconciliation.ReportAsync(ct), "Ledger reconciliation retrieved."));
}
