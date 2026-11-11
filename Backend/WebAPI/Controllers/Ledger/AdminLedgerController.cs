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
public class AdminLedgerController(ILedgerReconciliationService reconciliation, ILedgerHistoryService history, ILedgerExportService export) : ControllerBase
{
    /// <summary>
    /// The statement as an <c>.xlsx</c> file (the only response of this controller that is not the JSON envelope; errors keep the envelope):
    /// the same filters as the list, no paging. With one account selected (<c>partyType</c> and <c>partyId</c>, or a platform account) the
    /// file starts with its opening balance and ends with its closing balance. More than 50,000 entries is a 400: choose a shorter period.
    /// </summary>
    [HttpGet("export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, [FromQuery] string? partyType, [FromQuery] int? partyId, CancellationToken ct)
    {
        var file = await export.ExportAdminAsync(new LedgerHistoryRequest(from, to, type, null, null, partyType, partyId), ct);
        return File(file.Content, file.ContentType, file.FileName); // sets Content-Disposition: attachment
    }

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
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? dir, CancellationToken ct) =>
        Ok(ApiResponse<AdminLedgerPageDto>.Ok(
            await history.GetAdminHistoryAsync(new LedgerHistoryRequest(from, to, type, page, pageSize, partyType, partyId, sort, dir), ct), "Ledger retrieved."));

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
