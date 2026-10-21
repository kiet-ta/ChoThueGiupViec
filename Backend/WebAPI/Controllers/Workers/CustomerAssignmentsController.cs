using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Workers;

/// <summary>
/// Customer job acceptance and rework request endpoints (contract workers.md §2.5.2 & §2.5.3).
/// </summary>
[ApiController]
[Route("api/customers/assignments")]
public class CustomerAssignmentsController(IJobCompletionService completions) : ControllerBase
{
    /// <summary>Customer confirms job completion (contract §2.5.2).</summary>
    [HttpPost("{assignmentId:long}/accept")]
    [Authorize(Policy = "CustomerOnly")]
    [ProducesResponseType(typeof(ApiResponse<AssignmentCompletionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptJob(
        long assignmentId,
        [FromBody] AcceptAssignmentRequest? request,
        CancellationToken ct)
    {
        var result = await completions.AcceptAsync(assignmentId, request, ct);
        return Ok(result);
    }

    /// <summary>Customer requests 15–30 minute touch-up/redo (contract §2.5.3).</summary>
    [HttpPost("{assignmentId:long}/request-redo")]
    [Authorize(Policy = "CustomerOnly")]
    [ProducesResponseType(typeof(ApiResponse<AssignmentStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestRedo(
        long assignmentId,
        [FromBody] RequestRedoRequest request,
        CancellationToken ct)
    {
        var result = await completions.RequestRedoAsync(assignmentId, request, ct);
        return Ok(result);
    }
}
