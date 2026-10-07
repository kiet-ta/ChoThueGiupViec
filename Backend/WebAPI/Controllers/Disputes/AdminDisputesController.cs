using CommonService.Application.Common.Models;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Disputes;

/// <summary>
/// The Admin dispute console (contract disputes.md 2.2-2.3): queue by SLA, case file, and taking a ticket. Policy AdminOnly;
/// the admin id comes from the token. and the verdict (resolve, BE-M6-02b).
/// </summary>
[ApiController]
[Route("api/admin/disputes")]
[Authorize(Policy = "AdminOnly")]
public class AdminDisputesController(IAdminDisputeService disputes, IDisputeVerdictService verdicts, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The queue, oldest SLA first. <c>status</c> is a comma list (default OPEN,IN_REVIEW); <c>priority</c> is HIGH, MEDIUM or LOW
    /// (derived from the time left); <c>nearSla=true</c> keeps unresolved tickets due within the configured hours (overdue included);
    /// <c>pageSize</c> is 1-100, default 20.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<DisputePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? nearSla,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken ct) =>
        this.ToActionResult(await disputes.SearchAsync(status, priority, nearSla, page, pageSize, ct), "Disputes retrieved.");

    /// <summary>The case file: the ticket, its summary, the shift timeline and the photos of the order.</summary>
    [HttpGet("{disputeId:int}")]
    [ProducesResponseType(typeof(ApiResponse<DisputeCaseFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int disputeId, CancellationToken ct) =>
        this.ToActionResult(await disputes.GetAsync(disputeId, ct), "Dispute retrieved.");

    /// <summary>Takes an OPEN ticket (OPEN to IN_REVIEW); 409 when it is not open.</summary>
    [HttpPost("{disputeId:int}/take")]
    [ProducesResponseType(typeof(ApiResponse<AdminDisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Take(int disputeId, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        return this.ToActionResult(await disputes.TakeAsync(adminId, disputeId, ct), "Dispute taken.");
    }

    /// <summary>
    /// The verdict: <c>faultParty</c> FREELANCER, AGENCY or CUSTOMER decides the ticket (RESOLVED), null dismisses it (DISMISSED).
    /// The customer's refund, the agency's SLA penalty, the audit row and the ticket change commit together; a refund that fails
    /// is a 502 and nothing changes. 409 when the ticket is already decided (also when two admins decide at once).
    /// </summary>
    [HttpPost("{disputeId:int}/resolve")]
    [ProducesResponseType(typeof(ApiResponse<AdminDisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Resolve(int disputeId, [FromBody] ResolveDisputeRequestDto request, CancellationToken ct)
    {
        if (currentUser.UserId is not int adminId) return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        if (request is null) return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        return this.ToActionResult(await verdicts.ResolveAsync(adminId, disputeId, request, ct), "Dispute resolved.");
    }
}
