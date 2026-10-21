using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Workers;

/// <summary>
/// Worker profile management endpoints (contract workers.md §2.1 & §2.2).
/// </summary>
[ApiController]
[Route("api/workers")]
public class WorkersController(
    IWorkerProfileService profiles,
    IWorkerEkycService ekyc,
    IWorkerBookingSlotService slots,
    IJobPhotoService photos,
    IJobCompletionService completions) : ControllerBase
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
        var result = await profiles.RegisterAsync(fullRequest, ct);
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
        var result = await profiles.GetProfileAsync(ct);
        return Ok(result);
    }

    /// <summary>The account the worker's monthly payout is transferred to (decisions D19).</summary>
    [HttpGet("me/bank")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<WorkerBankResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBank(CancellationToken ct) =>
        Ok(await profiles.GetBankAsync(ct));

    /// <summary>Sets the payout account; 400 when a field is missing or malformed (decisions D19).</summary>
    [HttpPut("me/bank")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<WorkerBankResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PutBank([FromBody] UpdateWorkerBankRequest? request, CancellationToken ct) =>
        Ok(await profiles.UpdateBankAsync(request, ct));

    /// <summary>Stores where the worker is now; the dispatch search needs it (decisions D19).</summary>
    [HttpPut("me/location")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<WorkerLocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateWorkerLocationRequest? request, CancellationToken ct)
    {
        var result = await profiles.UpdateLocationAsync(request, ct);
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
        var result = await profiles.UpdateProfileAsync(request, ct);
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
        var result = await ekyc.SubmitAsync(request, ct);
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
        var result = await ekyc.GetStatusAsync(ct);
        return Ok(result);
    }

    /// <summary>Returns active blocked availability slots for specified date range (contract §2.3.1).</summary>
    [HttpGet("me/slots")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookingSlotResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSlots(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        var result = await slots.GetSlotsAsync(startDate, endDate, ct);
        return Ok(result);
    }

    /// <summary>Enables or disables an availability slot for a given date and shift (contract §2.3.2).</summary>
    [HttpPost("me/slots/toggle")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<BookingSlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ToggleSlot(
        [FromBody] ToggleBookingSlotRequest request,
        CancellationToken ct)
    {
        var result = await slots.ToggleAsync(request, ct);
        return Ok(result);
    }

    /// <summary>Uploads and verifies a work photo using Variance of Laplacian (contract §2.4.1).</summary>
    [HttpPost("assignments/{assignmentId:long}/photos")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<JobPhotoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadPhoto(
        long assignmentId,
        [FromBody] UploadJobPhotoRequest request,
        CancellationToken ct)
    {
        var result = await photos.UploadAsync(assignmentId, request, ct);
        return Ok(result);
    }

    /// <summary>Returns all uploaded photos for a job assignment (contract §2.4.2).</summary>
    [HttpGet("assignments/{assignmentId:long}/photos")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobPhotoResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPhotos(
        long assignmentId,
        CancellationToken ct)
    {
        var result = await photos.GetPhotosAsync(assignmentId, ct);
        return Ok(result);
    }

    /// <summary>Public summary view of a worker profile by ID (contract §2.1.4).</summary>
    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<WorkerPublicProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkerById(int id, CancellationToken ct)
    {
        var result = await profiles.GetByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>Worker submits completed job for customer acceptance (contract §2.5.1).</summary>
    [HttpPost("assignments/{assignmentId:long}/submit-completion")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<AssignmentStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitCompletion(
        long assignmentId,
        [FromBody] SubmitCompletionRequest? request,
        CancellationToken ct)
    {
        var result = await completions.SubmitAsync(assignmentId, request?.Notes, ct);
        return Ok(result);
    }
}


