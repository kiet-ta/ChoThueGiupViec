using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>Partner Agency endpoints (contract agencies.md).</summary>
[ApiController]
[Route("api/agencies")]
public class AgenciesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Public self-service registration of a Partner Agency (BE-M5-01; leader decision). The agency is created
    /// <c>PENDING</c>; it becomes <c>ACTIVE</c> only when its escrow reaches the minimum (decisions Q09, BE-M5-07).
    /// 400 on invalid fields, 409 (<c>data.code</c> AGENCY_TAX_CODE_EXISTS / AGENCY_EMAIL_EXISTS) on a duplicate.
    /// </summary>
    [HttpPost("registration")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterAgencyResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterAgencyRequest? request, CancellationToken ct)
    {
        var result = await sender.Send(new RegisterAgencyCommand(request), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Gets the list of workers for the authenticated agency (AGENCY-UAT-02).
    /// </summary>
    [HttpGet("workers")]
    [Authorize(Policy = "PartnerOnly")]
    [ProducesResponseType(typeof(ApiResponse<AgencyWorkerListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWorkers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetAgencyWorkersQuery(page, pageSize, status), ct);
        return Ok(result);
    }

    /// <summary>
    /// Retires an agency worker, optionally with a replacement (AGENCY-UAT-02).
    /// </summary>
    [HttpPost("workers/{workerId:int}/retire")]
    [Authorize(Policy = "PartnerOnly")]
    [ProducesResponseType(typeof(ApiResponse<RetireWorkerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireWorker(
        int workerId,
        [FromBody] RetireWorkerRequest? request,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new RetireWorkerCommand(workerId, request ?? new RetireWorkerRequest()), ct);
        return Ok(result);
    }
}
