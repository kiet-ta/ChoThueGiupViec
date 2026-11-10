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
public class AdminLedgerController(ILedgerReconciliationService reconciliation) : ControllerBase
{
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
