using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record UpdateWorkerProfileCommand(UpdateWorkerProfileRequest Request) : IRequest<ApiResponse<WorkerProfileResponse>>;

public sealed class UpdateWorkerProfileCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateWorkerProfileCommand, ApiResponse<WorkerProfileResponse>>
{
    public async Task<ApiResponse<WorkerProfileResponse>> Handle(UpdateWorkerProfileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, cancellationToken);
        if (worker == null)
        {
            throw new NotFoundException("Worker profile not found.");
        }

        if (string.IsNullOrWhiteSpace(command.Request.FullName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "fullName", ["Full name cannot be empty."] }
            });
        }

        worker.UpdateProfile(command.Request.FullName);
        workerRepository.Update(worker);
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

        return ApiResponse<WorkerProfileResponse>.Ok(response, "Profile updated successfully.");
    }
}
