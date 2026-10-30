using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Domain.Enums;
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
    IWorkerAssignmentService assignments,
    IJobPhotoService photos,
    IJobCompletionService completions,
    IWorkerExtensionService extensions) : ControllerBase
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

    /// <summary>Returns the jobs booked for the signed-in worker in a date range: date, shift, status and district, never the street address (contract §2.3.3).</summary>
    [HttpGet("me/assignments")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WorkerAssignmentResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAssignments(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        var result = await assignments.GetAsync(startDate, endDate, ct);
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

    /// <summary>
    /// Legacy upload of a work photo by URL. Only accepted in the Development environment; elsewhere the answer is a 400 that asks for a
    /// file (see <see cref="UploadPhoto"/>). Not part of the OpenAPI document: it shares the route of the file upload, which is the
    /// contract (§2.4.1), and Swagger cannot describe two bodies for one method and path.
    /// </summary>
    [HttpPost("assignments/{assignmentId:long}/photos")]
    [Consumes("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<JobPhotoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadPhotoByUrl(
        long assignmentId,
        [FromBody] UploadJobPhotoRequest request,
        CancellationToken ct)
    {
        var result = await photos.UploadAsync(assignmentId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Uploads a work photo as a file (<c>multipart/form-data</c>: <c>file</c>, <c>photoPhase</c>, <c>angleNo</c>); the sharpness is measured on the
    /// real pixels with the Variance of Laplacian (contract §2.4.1). At most 5 MB, JPEG, PNG or WebP.
    /// </summary>
    [HttpPost("assignments/{assignmentId:long}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(JobPhotoFileRules.MaxBytes + 1024 * 1024)] // the file plus the form fields; the service checks the file itself
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<JobPhotoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadPhoto(
        long assignmentId,
        [FromForm] UploadJobPhotoFileForm form,
        CancellationToken ct)
    {
        if (form.File is null)
        {
            return Ok(await photos.UploadFileAsync(assignmentId, form.PhotoPhase, form.AngleNo, null, ct));
        }

        await using var content = form.File.OpenReadStream();
        var file = new UploadedPhotoFile(form.File.FileName, form.File.ContentType, content, form.File.Length);
        return Ok(await photos.UploadFileAsync(assignmentId, form.PhotoPhase, form.AngleNo, file, ct));
    }

    /// <summary>
    /// The stored file of one photo, for the assignment's worker, its customer or an Admin. Not anonymous: the photos show the inside of a
    /// customer's home, so the same role and ownership rule as the list applies (contract §2.4.3).
    /// </summary>
    [HttpGet("assignments/{assignmentId:long}/photos/{photoId:long}/file")]
    [Authorize(Roles = nameof(UserRole.Worker) + "," + nameof(UserRole.Customer) + "," + nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoFile(long assignmentId, long photoId, CancellationToken ct)
    {
        var file = await photos.GetPhotoFileAsync(assignmentId, photoId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(file.Content, file.ContentType);
    }

    /// <summary>Returns all uploaded photos for a job assignment to its worker, its customer or an Admin (contract §2.4.2).</summary>
    [HttpGet("assignments/{assignmentId:long}/photos")]
    [Authorize(Roles = nameof(UserRole.Worker) + "," + nameof(UserRole.Customer) + "," + nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobPhotoResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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

    /// <summary>The worker accepts or declines an extension the customer paid for ("Làm lần 2", contract workers.md 2.6.1).</summary>
    [HttpPost("assignments/{assignmentId:long}/extension/respond")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<ExtensionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RespondToExtension(
        long assignmentId,
        [FromBody] RespondToExtensionRequest request,
        CancellationToken ct)
    {
        var result = await extensions.RespondAsync(assignmentId, request.Accept, ct);
        return Ok(result);
    }
}


