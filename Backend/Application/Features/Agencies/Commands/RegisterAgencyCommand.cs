using Backend.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace Backend.Application.Features.Agencies.Commands;

/// <summary>
/// Command to register a new agency
/// </summary>
public record RegisterAgencyCommand(
    string BusinessRegistrationCertificate,
    string TaxCode,
    string LegalName,
    string LegalRepresentative,
    string ContactPhone,
    string ContactEmail,
    string Password
) : IRequest<ApiResponse<RegisterAgencyResponse>>;