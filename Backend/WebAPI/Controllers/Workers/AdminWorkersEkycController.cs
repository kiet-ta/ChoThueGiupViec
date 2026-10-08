using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Commands;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Workers;

/// <summary>
/// Admin eKYC manual review and sample audit queue endpoints (contract workers.md §2.2.2 &amp; §2.2.3).
/// </summary>
[ApiController]
[Route("api/admin/workers")]
[Authorize(Policy = "AdminOnly")]
public class AdminWorkersEkycController(ISender sender) : ControllerBase
{
    /// <summary>Lists pending manual eKYC reviews and sample audits for Admin (contract §2.2.2).</summary>
    [HttpGet("ekyc-queue")]
    [ProducesResponseType(typeof(ApiResponse<EkycQueuePagedResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEkycQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetEkycQueueQuery(page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Admin manual approval or rejection of a worker's eKYC (contract §2.2.3).</summary>
    [HttpPost("{id:int}/ekyc-review")]
    [ProducesResponseType(typeof(ApiResponse<EkycResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReviewEkyc(
        int id,
        [FromBody] ReviewEkycRequest request,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ReviewEkycCommand(id, request), ct);
        return Ok(result);
    }
}
