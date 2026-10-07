using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Payouts;

/// <summary>
/// The bank transfer files of a payout batch (contract payouts.md 2.3, decision Q18). Policy AdminOnly. The success response is the
/// <c>.xlsx</c> file itself, the only response of the API that is not the JSON envelope; errors keep the envelope.
/// </summary>
[ApiController]
[Route("api/admin/payout-batches/{batchId:int}/export")]
[Authorize(Policy = "AdminOnly")]
public class AdminPayoutExportController(IPayoutExportService exports) : ControllerBase
{
    /// <summary>
    /// <c>type</c> = <c>freelancer</c> (one row per freelancer), <c>agency-summary</c> (one row per agency) or <c>agency-detail</c> (one row
    /// per assignment of the agencies). Works for DRAFT and CLOSED batches; 400 for an unknown or missing type, 404 for a missing batch.
    /// </summary>
    [HttpGet]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(int batchId, [FromQuery] string? type, CancellationToken ct)
    {
        var result = await exports.ExportAsync(batchId, type, ct);
        if (result.Success && result.Data is { } file)
        {
            return File(file.Content, file.ContentType, file.FileName); // sets Content-Disposition: attachment
        }

        object? data = result.ValidationErrors is not null ? new { errors = result.ValidationErrors } : null;
        return StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", data));
    }
}
