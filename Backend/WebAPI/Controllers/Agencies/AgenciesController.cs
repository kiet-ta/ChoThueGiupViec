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
}
