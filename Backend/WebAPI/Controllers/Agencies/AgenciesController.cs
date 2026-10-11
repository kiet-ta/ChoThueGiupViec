using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

[ApiController]
[Route("api/[controller]")]
public class AgenciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AgenciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AgencyProfileResponse>>> RegisterAgency(
        [FromBody] RegisterAgencyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterAgencyCommand(request);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("shift-roster")]
    public async Task<ActionResult<ApiResponse<ShiftRosterAgencyResponse>>> CreateShiftRosterEntry(
        [FromBody] CreateShiftRosterAgencyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateShiftRosterAgencyCommand(request);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
