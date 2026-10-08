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
/// Worker profile management endpoints (contract workers.md §2.1 & §2.2).
/// </summary>
[ApiController]
[Route("api/workers")]
public class WorkersController(ISender sender) : ControllerBase
{
    /// <summary>Registers a new Freelancer profile using a single-purpose registration token (contract §2.1.1).</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<WorkerProfileResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromHeader(Name = "X-Registration-Token")] string? headerToken,
        [FromBody] RegisterWorkerRequest request,
        CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        var token = !string.IsNullOrWhiteSpace(request.RegistrationToken)
            ? request.RegistrationToken
            : headerToken ?? string.Empty;

        var fullRequest = request with { RegistrationToken = token };
        var result = await sender.Send(new RegisterWorkerCommand(fullRequest), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Returns profile of the authenticated worker (contract §2.1.2).</summary>
    [HttpGet("me")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<WorkerProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await sender.Send(new GetWorkerProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>Updates editable profile fields of the authenticated worker (contract §2.1.3).</summary>
    [HttpPatch("me")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<WorkerProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateWorkerProfileRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateWorkerProfileCommand(request), ct);
        return Ok(result);
    }

    /// <summary>Submits CCCD photos and selfie for eKYC verification (contract §2.2.1).</summary>
    [HttpPost("me/ekyc")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<EkycResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SubmitEkyc([FromBody] SubmitEkycRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SubmitEkycCommand(request), ct);
        return Ok(result);
    }

    /// <summary>Returns current worker's eKYC status details (contract §2.2.1).</summary>
    [HttpGet("me/ekyc/status")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<EkycStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEkycStatus(CancellationToken ct)
    {
        var result = await sender.Send(new GetEkycStatusQuery(), ct);
        return Ok(result);
    }

    /// <summary>Public summary view of a worker profile by ID (contract §2.1.4).</summary>
    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<WorkerPublicProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkerById(int id, CancellationToken ct)
    {
        var result = await sender.Send(new GetWorkerByIdQuery(id), ct);
        return Ok(result);
    }
}
