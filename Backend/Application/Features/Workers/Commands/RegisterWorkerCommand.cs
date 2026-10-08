using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record RegisterWorkerCommand(RegisterWorkerRequest Request) : IRequest<ApiResponse<WorkerProfileResponse>>;

public sealed class RegisterWorkerCommandHandler(
    IWorkerRepository workerRepository,
    IConfiguration configuration)
    : IRequestHandler<RegisterWorkerCommand, ApiResponse<WorkerProfileResponse>>
{
    public async Task<ApiResponse<WorkerProfileResponse>> Handle(RegisterWorkerCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        // 1. Validate registration token
        var phoneNumber = RegistrationTokenValidator.ValidateRegistrationToken(req.RegistrationToken, configuration);
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "registrationToken", ["Invalid or expired worker registration token."] }
            });
        }

        // 2. Validate inputs
        if (string.IsNullOrWhiteSpace(req.FullName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "fullName", ["Full name is required."] }
            });
        }

        var trimmedNationalId = req.NationalId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedNationalId) || trimmedNationalId.Length != 12 || !trimmedNationalId.All(char.IsDigit))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "nationalId", ["National ID (CCCD) must be exactly 12 digits."] }
            });
        }

        // 3. Check for existing worker with same phone number or national ID
        var exists = await workerRepository.ExistsByPhoneOrNationalIdAsync(phoneNumber, trimmedNationalId, cancellationToken);
        if (exists)
        {
            throw new BusinessRuleViolationException("A worker with this phone number or national ID is already registered.");
        }

        // 4. Create worker entity (Freelancer STI, WorkStatus = PENDING, KycStatus = PENDING)
        var worker = Worker.CreateFreelancer(phoneNumber, trimmedNationalId, req.FullName.Trim());
        worker.KycStatus = "PENDING";
        worker.RatingAvg = 5.00m;
        worker.CompletedJobs = 0;
        worker.CreatedAt = DateTime.UtcNow;
        worker.UpdatedAt = DateTime.UtcNow;

        await workerRepository.AddAsync(worker, cancellationToken);
        await workerRepository.SaveChangesAsync(cancellationToken);

        var response = new WorkerProfileResponse(
            worker.WorkerId,
            worker.PhoneNumber,
            worker.FullName,
            worker.NationalId,
            worker.WorkerType.ToString().ToUpperInvariant(),
            worker.AgencyId,
            worker.WorkStatus.ToString().ToUpperInvariant(),
            worker.KycStatus,
            worker.RatingAvg,
            worker.CompletedJobs,
            worker.IsSuperFreelancer,
            worker.CreatedAt
        );

        return ApiResponse<WorkerProfileResponse>.Ok(response, "Worker registered successfully.");
    }
}
