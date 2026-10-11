using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace CommonService.Application.Features.Agencies.Commands;

public sealed record RegisterAgencyCommand(RegisterAgencyRequest Request) : IRequest<ApiResponse<AgencyProfileResponse>>;

public sealed class RegisterAgencyCommandHandler(
    IAgencyRepository agencyRepository,
    IConfiguration configuration)
    : IRequestHandler<RegisterAgencyCommand, ApiResponse<AgencyProfileResponse>>
{
    public async Task<ApiResponse<AgencyProfileResponse>> Handle(RegisterAgencyCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        // 1. Validate inputs
        if (string.IsNullOrWhiteSpace(req.BusinessRegistrationNumber))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "businessRegistrationNumber", ["Business registration number is required."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.TaxCode))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "taxCode", ["Tax code is required."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.LegalRepresentative))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "legalRepresentative", ["Legal representative is required."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.LegalName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "legalName", ["Legal name is required."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.ContactPhone))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "contactPhone", ["Contact phone is required."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.ContactEmail))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "contactEmail", ["Contact email is required."] }
            });
        }

        // 2. Check for existing agency with same tax code or business registration number
        var exists = await agencyRepository.ExistsByTaxCodeOrBusinessRegistrationNumberAsync(
            req.TaxCode,
            req.BusinessRegistrationNumber,
            cancellationToken);

        if (exists)
        {
            throw new BusinessRuleViolationException("An agency with this tax code or business registration number is already registered.");
        }

        // 3. Determine agency status based on escrow deposit balance
        var minEscrowBalance = configuration.GetValue<decimal>("Agency:MinEscrowBalance");
        var agencyStatus = req.EscrowDepositBalance >= minEscrowBalance ? "ACTIVE" : "INACTIVE";

        // 4. Create agency entity
        var agency = PartnerAgency.Create(
            req.BusinessRegistrationNumber,
            req.TaxCode,
            req.LegalName,
            req.LegalRepresentative,
            req.ContactPhone,
            req.ContactEmail,
            req.EscrowDepositBalance,
            agencyStatus
        );

        agency.CreatedAt = DateTime.UtcNow;

        await agencyRepository.AddAsync(agency, cancellationToken);
        await agencyRepository.SaveChangesAsync(cancellationToken);

        var response = new AgencyProfileResponse(
            agency.AgencyId,
            agency.BusinessRegistrationNumber,
            agency.TaxCode,
            agency.LegalRepresentative,
            agency.LegalName,
            agency.ContactPhone,
            agency.ContactEmail,
            agency.EscrowDepositBalance,
            agency.AgencyStatus,
            agency.CreatedAt
        );

        return ApiResponse<AgencyProfileResponse>.Ok(response, "Agency registered successfully.");
    }
}