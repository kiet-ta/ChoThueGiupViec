using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>A page of the escrow ledger.</summary>
public sealed record EscrowTransactionPageDto(IReadOnlyList<EscrowTransactionDto> Items, int Page, int PageSize, int Total);

/// <summary>Escrow of the agency (contract agencies.md 6, BE-M5-07, decisions Q09, policy PartnerOnly).</summary>
[ApiController]
[Route("api/agencies/escrow")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyEscrowController(EscrowService service) : ControllerBase
{
    /// <summary>Balance, status, minimum, warnings and SLA score.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<EscrowStateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetState(CancellationToken ct) =>
        Ok(ApiResponse<EscrowStateDto>.Ok(await service.GetStateAsync(ct)));

    /// <summary>The caller's ledger rows, newest first (never another agency's).</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<EscrowTransactionPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await service.ListTransactionsAsync(page, pageSize, ct);
        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        return Ok(ApiResponse<EscrowTransactionPageDto>.Ok(new EscrowTransactionPageDto(items, Math.Max(1, page), size, total)));
    }

    /// <summary>Starts a top-up: returns the MoMo QR; the IPN credits the escrow.</summary>
    [HttpPost("deposits")]
    [ProducesResponseType(typeof(ApiResponse<DepositPurchaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Deposit([FromBody] DepositRequest request, CancellationToken ct) =>
        Ok(ApiResponse<DepositPurchaseDto>.Ok(await service.StartDepositAsync(request, ct)));

    /// <summary>Appeals a penalty within the appeal window (decisions Q09).</summary>
    [HttpPost("transactions/{escrowTxnId:long}/appeal")]
    [ProducesResponseType(typeof(ApiResponse<EscrowAppealDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Appeal(long escrowTxnId, [FromBody] AppealRequest request, CancellationToken ct) =>
        Ok(ApiResponse<EscrowAppealDto>.Ok(await service.AppealAsync(escrowTxnId, request, ct)));
}

/// <summary>Admin side of the escrow appeals (decisions Q09, G-5): a manual decision; an accepted appeal appends a REVERSAL row.</summary>
[ApiController]
[Route("api/admin/escrow/appeals")]
[Authorize(Policy = "AdminOnly")]
public class AdminEscrowAppealsController(EscrowService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EscrowAppealDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<EscrowAppealDto>>.Ok(await service.ListAppealsAsync(status, ct)));

    [HttpPost("{appealId:int}/decision")]
    [ProducesResponseType(typeof(ApiResponse<EscrowAppealDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide(int appealId, [FromBody] AppealDecisionRequest request, CancellationToken ct) =>
        Ok(ApiResponse<EscrowAppealDto>.Ok(await service.DecideAppealAsync(appealId, request, ct)));
}
