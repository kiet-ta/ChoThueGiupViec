using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>Body of the 422 answer of a rejected roster write.</summary>
public sealed record RosterRejectedDto(IReadOnlyList<RosterEntryError> Errors);

/// <summary>Weekly shift roster of the agency (contract agencies.md 5, BE-M5-05, policy PartnerOnly).</summary>
[ApiController]
[Route("api/agencies/roster")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyRosterController(ShiftRosterService service) : ControllerBase
{
    /// <summary>The roster of the week (Monday to Sunday) that contains <c>date</c> (default: this week).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RosterEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWeek([FromQuery] DateOnly? date, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RosterEntryDto>>.Ok(await service.GetWeekAsync(date, ct)));

    /// <summary>
    /// Puts workers on shifts. All-or-nothing: any invalid entry rejects the request with 422 and the errors by entry index; re-sending an
    /// existing AVAILABLE slot changes nothing.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<RosterResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RosterRejectedDto>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Put([FromBody] RosterRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse<RosterResultDto>.Ok(await service.PutAsync(request, ct)));
        }
        catch (RosterRejectedException rejected)
        {
            return UnprocessableEntity(ApiResponse<RosterRejectedDto>.Fail(rejected.Message, new RosterRejectedDto(rejected.Errors)));
        }
    }

    /// <summary>Removes an AVAILABLE slot from the roster.</summary>
    [HttpDelete("{slotId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(int slotId, CancellationToken ct)
    {
        await service.RemoveAsync(slotId, ct);
        return NoContent();
    }
}
