using Backend.Application.Features.Agencies.Dtos;

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
) : IRequest<RegisterAgencyResponse>;