using CommonService.Application.Common.Models;

namespace CommonService.Application.Features.Agencies.Dtos;

public sealed record RegisterAgencyRequest(
    string BusinessRegistrationNumber,   // ĐKKD
    string TaxCode,                    // MST
    string LegalRepresentative,        // Đại diện pháp luật
    string LegalName,
    string ContactPhone,
    string ContactEmail,
    decimal EscrowDepositBalance
);