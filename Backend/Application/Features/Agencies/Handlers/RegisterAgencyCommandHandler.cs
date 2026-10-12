using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Creates a <c>PARTNER_AGENCY</c> row from a public registration (decisions Q09, Q16; leader decisions for BE-M5-01).
/// The agency starts with a zero escrow balance, so it is <c>PENDING</c> until a deposit reaches
/// <c>Agency.MinEscrowBalance</c> (the deposit itself is BE-M5-07). <c>worker_quota</c> starts at 0 and is set by BE-M5-02.
/// </summary>
public sealed class RegisterAgencyCommandHandler(
    IAgencyRepository agencies,
    IPasswordHasher passwordHasher,
    IClock clock,
    IOptions<BusinessRules> options)
    : IRequestHandler<RegisterAgencyCommand, ApiResponse<RegisterAgencyResponse>>
{
    public async Task<ApiResponse<RegisterAgencyResponse>> Handle(
        RegisterAgencyCommand command,
        CancellationToken cancellationToken)
    {
        var rules = options.Value;
        var input = AgencyRegistrationValidator.Validate(command.Request, rules.Auth.MinPasswordLength);

        if (await agencies.TaxCodeExistsAsync(input.TaxCode, cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "An agency with this tax code is already registered.",
                AgencyErrorCodes.TaxCodeExists);
        }

        if (await agencies.EmailExistsAsync(input.ContactEmail, cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "An agency with this contact email is already registered.",
                AgencyErrorCodes.EmailExists);
        }

        const decimal initialEscrowBalance = 0m;
        var agency = new PartnerAgency
        {
            TaxCode = input.TaxCode,
            LegalName = input.LegalName,
            LegalRepresentative = input.LegalRepresentative,
            ContactPhone = input.ContactPhone,
            ContactEmail = input.ContactEmail,
            EscrowDepositBalance = initialEscrowBalance,
            SlaScore = rules.Sla.InitialScore,
            WorkerQuota = 0,
            IsVerifiedPartner = false,
            AgencyStatus = AgencyEligibility.StatusFor(initialEscrowBalance, rules.Agency.MinEscrowBalance),
            PasswordHash = passwordHasher.Hash(input.Password),
            CreatedAt = clock.UtcNow
        };

        await agencies.AddAsync(agency, cancellationToken);

        var response = new RegisterAgencyResponse(
            agency.AgencyId,
            agency.TaxCode,
            agency.LegalName,
            agency.LegalRepresentative,
            agency.ContactPhone,
            agency.ContactEmail,
            agency.AgencyStatus,
            agency.EscrowDepositBalance,
            agency.SlaScore,
            agency.WorkerQuota,
            agency.CreatedAt);

        return ApiResponse<RegisterAgencyResponse>.Ok(response, "Agency registered successfully.");
    }
}
