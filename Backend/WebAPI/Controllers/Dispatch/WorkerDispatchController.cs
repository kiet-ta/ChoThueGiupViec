using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>Request body of the decline endpoint (contract dispatch.md 3.1).</summary>
public sealed record DeclineOfferRequest(string? Reason);

/// <summary>Request body of the GPS check-in endpoint (contract dispatch.md 3.1).</summary>
public sealed record CheckInRequest(double Latitude, double Longitude);

/// <summary>Request body of the plate-photo fallback (contract dispatch.md 3.1).</summary>
public sealed record PlatePhotoRequest(string? PlatePhotoUrl);

/// <summary>
/// Worker side of Dispatch (contract dispatch.md 3.1, policy WorkerOnly): the 30-second offer, field check-in (BR-04), system calls and
/// the customer-absent report (BR-05). Someone else's assignment answers 404; a state that forbids the action answers 409 with data.code.
/// </summary>
[ApiController]
[Route("api/dispatch")]
[Authorize(Policy = "WorkerOnly")]
public class WorkerDispatchController(WorkerDispatchService service) : ControllerBase
{
    /// <summary>The worker's open offer, or data = null.</summary>
    [HttpGet("offers/current")]
    [ProducesResponseType(typeof(ApiResponse<JobOfferDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentOffer(CancellationToken ct) =>
        Ok(ApiResponse<JobOfferDto?>.Ok(await service.GetCurrentOfferAsync(ct)));

    /// <summary>Accepts the offer inside its 30-second window.</summary>
    [HttpPost("offers/{assignmentId:long}/accept")]
    [ProducesResponseType(typeof(ApiResponse<OfferAcceptedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(long assignmentId, CancellationToken ct) =>
        Ok(ApiResponse<OfferAcceptedDto>.Ok(await service.AcceptAsync(assignmentId, ct)));

    /// <summary>Declines the offer; the seat goes to the next candidate at once.</summary>
    [HttpPost("offers/{assignmentId:long}/decline")]
    [ProducesResponseType(typeof(ApiResponse<OfferDeclinedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decline(long assignmentId, [FromBody] DeclineOfferRequest? request, CancellationToken ct) =>
        Ok(ApiResponse<OfferDeclinedDto>.Ok(await service.DeclineAsync(assignmentId, request?.Reason, ct)));

    /// <summary>GPS check-in at the address (within 100 m is verified; otherwise an alternative verification is required).</summary>
    [HttpPost("assignments/{assignmentId:long}/check-in")]
    [ProducesResponseType(typeof(ApiResponse<CheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(long assignmentId, [FromBody] CheckInRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CheckInResultDto>.Ok(await service.CheckInAsync(assignmentId, request.Latitude, request.Longitude, ct)));

    /// <summary>House-plate photo when GPS is more than 100 m off.</summary>
    [HttpPost("assignments/{assignmentId:long}/check-in/plate-photo")]
    [ProducesResponseType(typeof(ApiResponse<CheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PlatePhoto(long assignmentId, [FromBody] PlatePhotoRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CheckInResultDto>.Ok(await service.SubmitPlatePhotoAsync(assignmentId, request.PlatePhotoUrl, ct)));

    /// <summary>Records one system call to the customer (counts towards the absent rule).</summary>
    [HttpPost("assignments/{assignmentId:long}/calls/log")]
    [ProducesResponseType(typeof(ApiResponse<CallLoggedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LogCall(long assignmentId, CancellationToken ct) =>
        Ok(ApiResponse<CallLoggedDto>.Ok(await service.LogCallAsync(assignmentId, ct)));

    /// <summary>Reports the customer absent (after 15 minutes and 2 system calls); an Admin reviews it.</summary>
    [HttpPost("assignments/{assignmentId:long}/absent")]
    [ProducesResponseType(typeof(ApiResponse<CustomerAbsentReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReportAbsent(long assignmentId, CancellationToken ct) =>
        Ok(ApiResponse<CustomerAbsentReportDto>.Ok(await service.ReportAbsentAsync(assignmentId, ct)));
}
