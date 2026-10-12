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
    /// Gets available subscription packages
    /// </summary>
    /// <response code="200">Returns the list of available subscription packages</response>
    [HttpGet("packages")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PackageDto>>>> GetPackages(
        CancellationToken cancellationToken)
    {
        var command = new GetPackagesCommand();
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Gets the current agency's subscription
    /// </summary>
    /// <response code="200">Returns the current subscription state</response>
    [HttpGet("subscriptions/me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> GetMySubscription(
        CancellationToken cancellationToken)
    {
        var command = new GetSubscriptionCommand();
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Subscribes to or renews an agency subscription package
    /// </summary>
    /// <response code="200">Returns the payment QR code for subscription</response>
    [HttpPost("subscriptions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Subscribe(
        [FromBody] SubscribeCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }
}