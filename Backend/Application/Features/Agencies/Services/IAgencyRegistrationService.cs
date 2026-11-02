using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Dtos;

namespace CommonService.Application.Features.Agencies.Services;

/// <summary>Public self-service registration of a Partner Agency (BE-M5-01).</summary>
public interface IAgencyRegistrationService
{
    /// <summary>
    /// Creates a <c>PENDING</c> agency. Throws <c>ValidationException</c> on invalid fields and
    /// <c>BusinessRuleViolationException</c> (AGENCY_TAX_CODE_EXISTS / AGENCY_EMAIL_EXISTS) on a duplicate.
    /// </summary>
    Task<ApiResponse<RegisterAgencyResponse>> RegisterAsync(RegisterAgencyRequest? request, CancellationToken cancellationToken = default);
}
