namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Result of a successful registration. Never carries the password or its hash, and no access token: the agency signs in
/// through the Partner login (identity.md §2.3) with <c>contactEmail</c> + password.
/// </summary>
public sealed record RegisterAgencyResponse(
    int AgencyId,
    string TaxCode,
    string LegalName,
    string LegalRepresentative,
    string ContactPhone,
    string ContactEmail,
    string AgencyStatus,
    decimal EscrowDepositBalance,
    decimal SlaScore,
    int WorkerQuota,
    DateTime CreatedAt
);
