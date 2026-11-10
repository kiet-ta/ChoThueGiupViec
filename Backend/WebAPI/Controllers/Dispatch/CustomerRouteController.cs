using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>
/// The route from the worker to the customer's house (contract dispatch.md 3.2, policy CustomerOnly, ROUTE-WEB-01 #765). Someone
/// else's assignment answers 404; an assignment that is not on the way or a worker without a position answers 409 with
/// data.code (ROUTE_NOT_AVAILABLE, WORKER_LOCATION_UNKNOWN); a routing engine that does not answer gives 502.
/// </summary>
[ApiController]
[Route("api/v1/dispatch/assignments/{assignmentId:long}/route")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerRouteController(CustomerRouteService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CustomerRouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Get(long assignmentId, CancellationToken ct)
    {
        var route = await service.GetAsync(assignmentId, ct);
        return route is null
            ? StatusCode(StatusCodes.Status502BadGateway, ApiResponse.Fail("The route could not be calculated right now."))
            : Ok(ApiResponse<CustomerRouteDto>.Ok(route));
    }
}
