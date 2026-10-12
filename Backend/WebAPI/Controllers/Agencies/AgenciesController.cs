using Backend.Application.Features.Agencies.Commands;
using Backend.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using CommonService.WebAPI;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.WebAPI.Controllers.Agencies;

/// <summary>
/// Controller for agency-related operations
/// </summary>
[ApiController]
[Route("api/agencies")]
public class AgenciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AgenciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Registers a new agency
    /// </summary>
    /// <param name="command">The agency registration command</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The registration response</returns>
    [HttpPost("registration")]
    [AllowAnonymous] // Registration is typically public/self-service
    public async Task<ActionResult<ApiResponse<RegisterAgencyResponse>>> RegisterAgency(
        [FromBody] RegisterAgencyCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }
}