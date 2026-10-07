using CommonService.Application.Common.Models;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Disputes;

/// <summary>
/// The customer files and follows disputes about their own orders (contract disputes.md 2.1, PRD 4.3). Policy CustomerOnly;
/// the customer id comes from the token. An order or dispute that is not the caller's is a 404.
/// </summary>
[ApiController]
[Route("api/customers/me/disputes")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerDisputesController(IDisputeFilingService disputes, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Files a dispute for an order: category, description and at least one piece of evidence. One dispute per order.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> File([FromBody] FileDisputeRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return this.ToActionResult(await disputes.FileAsync(DisputeSide.Customer, customerId, request, ct), "Dispute filed.");
    }

    /// <summary>The caller's disputes, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DisputeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await disputes.ListAsync(DisputeSide.Customer, customerId, ct), "Disputes retrieved.");
    }

    /// <summary>One of the caller's disputes, with the verdict once it is decided.</summary>
    [HttpGet("{disputeId:int}")]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int disputeId, CancellationToken ct)
    {
        if (currentUser.UserId is not int customerId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await disputes.GetAsync(DisputeSide.Customer, customerId, disputeId, ct), "Dispute retrieved.");
    }
}
