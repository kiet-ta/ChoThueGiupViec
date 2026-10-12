using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CommonService.WebAPI.Controllers.Agencies;

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

    /// <summary>
    /// Imports workers from CSV file
    /// </summary>
    /// <response code="200">Returns the import result</response>
    [HttpPost("import-workers")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ImportWorkerResponseDto>>> ImportWorkers(
        [FromForm] ImportWorkersCommand command,
        CancellationToken cancellationToken)
    {
        // Get agency ID from current user context
        var agencyIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(agencyIdClaim, out int agencyId))
        {
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = "Unable to determine agency ID from token"
            });
        }

        // Override the agency ID from the command with the one from the token
        // This prevents users from specifying a different agency ID
        var updatedCommand = command with { AgencyId = agencyId };

        var response = await _mediator.Send(updatedCommand, cancellationToken);
        return Ok(response);
    }
}