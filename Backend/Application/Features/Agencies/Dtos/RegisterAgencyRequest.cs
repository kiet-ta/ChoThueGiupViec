namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Body of <c>POST /api/agencies/registration</c> (contract agencies.md §2). Every field is nullable on purpose: a
/// missing field is reported by <see cref="AgencyRegistrationValidator"/> in the shared validation envelope instead of
/// the framework's own model-state response. <c>taxCode</c> is also the identity of the business registration (ĐKKD):
/// the leader decided not to add a separate column (docx/BE-M5-01-notes.md, decision 1).
/// </summary>
public sealed record RegisterAgencyRequest(
    string? TaxCode,
    string? LegalName,
    string? LegalRepresentative,
    string? ContactPhone,
    string? ContactEmail,
    string? Password
);
