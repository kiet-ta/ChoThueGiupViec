using CommonService.Application.Common.Models;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Disputes;

/// <summary>
/// The worker files and follows disputes about orders they worked on (contract disputes.md 2.1, PRD 4.3). Policy WorkerOnly;
/// the worker id comes from the token. An order or dispute the worker is not part of is a 404.
/// </summary>
[ApiController]
[Route("api/workers/me/disputes")]
[Authorize(Policy = "WorkerOnly")]
public class WorkerDisputesController(IDisputeFilingService disputes, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Files a dispute for an order: category, description and at least one piece of evidence. One dispute per order.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> File([FromBody] FileDisputeRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return this.ToActionResult(await disputes.FileAsync(DisputeSide.Worker, workerId, request, ct), "Dispute filed.");
    }

    /// <summary>The caller's disputes, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DisputeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await disputes.ListAsync(DisputeSide.Worker, workerId, ct), "Disputes retrieved.");
    }

    /// <summary>One of the caller's disputes, with the verdict once it is decided.</summary>
    [HttpGet("{disputeId:int}")]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int disputeId, CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await disputes.GetAsync(DisputeSide.Worker, workerId, disputeId, ct), "Dispute retrieved.");
    }
}
