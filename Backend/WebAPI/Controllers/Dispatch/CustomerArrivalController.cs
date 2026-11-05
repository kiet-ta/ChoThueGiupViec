using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>
/// Customer side of the BR-04 check-in fallback (contract dispatch.md 3.2, policy CustomerOnly, CUST-ARRIVAL-01 #657): when the worker's
/// GPS is more than 100 m off, the customer confirms the arrival. Someone else's assignment answers 404; an assignment that does not
/// wait for a confirmation answers 409 with data.code = ARRIVAL_NOT_PENDING.
/// </summary>
[ApiController]
[Route("api/v1/dispatch/assignments/{assignmentId:long}/check-in")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerArrivalController(CustomerArrivalService service) : ControllerBase
{
    /// <summary>Whether the worker reported an arrival that waits for the customer's confirmation.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ArrivalStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long assignmentId, CancellationToken ct) =>
        Ok(ApiResponse<ArrivalStatusDto>.Ok(await service.GetAsync(assignmentId, ct)));

    /// <summary>The customer confirms the worker is at the door; the assignment becomes CHECKED_IN.</summary>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ApiResponse<ArrivalConfirmedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(long assignmentId, CancellationToken ct) =>
        Ok(ApiResponse<ArrivalConfirmedDto>.Ok(await service.ConfirmAsync(assignmentId, ct)));
}
