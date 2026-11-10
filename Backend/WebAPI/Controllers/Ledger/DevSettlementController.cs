using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ledger;

/// <summary>Answer of the demo settlement: the month and the status of its batch (<c>CLOSED</c>).</summary>
public record DevSettlementDto(string PeriodMonth, string BatchStatus);

/// <summary>
/// Demo helper (contract ledger.md section 9): settles a month on demand, even one that is not over yet, as if it ended now, so the whole
/// flow (earn, withdraw, settle, history) can be shown in one sitting. Anonymous like the other dev routes, reachable in Development only
/// (404 anywhere else) and hidden from OpenAPI. Not part of the API contract.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/dev/settlement")]
[AllowAnonymous]
public class DevSettlementController(IPayoutBatchService batches, IHostEnvironment environment) : ControllerBase
{
    /// <summary><c>POST run?period=yyyy-MM</c>: 200 with the batch status; 400 for a bad month; 409 when a payee could not be settled.</summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(ApiResponse<DevSettlementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Run([FromQuery] string? period, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();

        var result = await batches.SettleAsync(period, allowUnfinishedMonth: true, cancellationToken: ct);
        if (result.Success && result.Data is not null)
        {
            return Ok(ApiResponse<DevSettlementDto>.Ok(new DevSettlementDto(result.Data.PeriodMonth, result.Data.BatchStatus), "Month settled."));
        }

        object? data = result.ValidationErrors is not null ? new { errors = result.ValidationErrors } : null;
        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
