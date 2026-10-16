using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>The account the agency's payout is transferred to (MAIN-FLOW-FIX, decisions D18, policy PartnerOnly).</summary>
[ApiController]
[Route("api/agencies/me/bank")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyBankController(AgencyBankService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AgencyBankDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResponse<AgencyBankDto>.Ok(await service.GetAsync(ct)));

    /// <summary>Sets the account. 400 when a field is missing or malformed.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<AgencyBankDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put([FromBody] UpdateAgencyBankRequest? request, CancellationToken ct) =>
        Ok(ApiResponse<AgencyBankDto>.Ok(await service.SetAsync(request, ct), "Bank account saved."));
}
