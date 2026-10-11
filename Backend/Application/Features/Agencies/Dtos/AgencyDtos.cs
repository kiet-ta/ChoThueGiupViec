namespace CommonService.Application.Features.Agencies.Dtos;

public sealed record AgencyProfileResponse(
    int AgencyId,
    string BusinessRegistrationNumber,
    string TaxCode,
    string LegalRepresentative,
    string LegalName,
    string ContactPhone,
    string ContactEmail,
    decimal EscrowDepositBalance,
    string AgencyStatus,
    DateTime CreatedAt
);