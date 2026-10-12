using Backend.Application.Features.Agencies.Dtos;
using Backend.Application.Features.Agencies.Commands;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Backend.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles the registration of a new agency
/// </summary>
public sealed class RegisterAgencyCommandHandler(
    IAgencyRepository agencyRepository,
    IPasswordHasher passwordHasher,
    IConfiguration configuration)
    : IRequestHandler<RegisterAgencyCommand, ApiResponse<RegisterAgencyResponse>>
{
    public async Task<ApiResponse<RegisterAgencyResponse>> Handle(RegisterAgencyCommand command, CancellationToken cancellationToken)
    {
        // 1. Check if tax code already exists
        var taxCodeExists = await agencyRepository.TaxCodeExistsAsync(command.TaxCode, cancellationToken);
        if (taxCodeExists)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "taxCode", ["An agency with this tax code already exists."] }
            });
        }

        // 2. Create agency entity
        var agency = new PartnerAgency
        {
            BusinessRegistrationCertificate = command.BusinessRegistrationCertificate.Trim(),
            TaxCode = command.TaxCode.Trim(),
            LegalName = command.LegalName.Trim(),
            LegalRepresentative = command.LegalRepresentative.Trim(),
            ContactPhone = command.ContactPhone.Trim(),
            ContactEmail = command.ContactEmail.Trim().ToLowerInvariant(),
            EscrowDepositBalance = 0m, // Start with zero balance
            SlaScore = 100.00m, // Initial SLA score from decisions.md Q09
            AgencyStatus = "PENDING", // Not active until escrow minimum is met
            CreatedAt = DateTime.UtcNow
        };

        // 3. Hash the password using the password hasher service
        agency.PasswordHash = passwordHasher.Hash(command.Password);

        // 4. Save to database
        await agencyRepository.AddAsync(agency, cancellationToken);
        await agencyRepository.SaveChangesAsync(cancellationToken);

        // 5. Return response
        var response = new RegisterAgencyResponse
        {
            AgencyId = agency.AgencyId,
            BusinessRegistrationCertificate = agency.BusinessRegistrationCertificate,
            TaxCode = agency.TaxCode,
            LegalName = agency.LegalName,
            LegalRepresentative = agency.LegalRepresentative,
            ContactPhone = agency.ContactPhone,
            ContactEmail = agency.ContactEmail,
            EscrowDepositBalance = agency.EscrowDepositBalance,
            SlaScore = agency.SlaScore,
            AgencyStatus = agency.AgencyStatus,
            CreatedAt = agency.CreatedAt
        };

        return ApiResponse<RegisterAgencyResponse>.Ok(response, "Agency registered successfully.");
    }
}