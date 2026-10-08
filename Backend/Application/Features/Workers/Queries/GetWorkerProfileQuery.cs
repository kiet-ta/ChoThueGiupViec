using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetWorkerProfileQuery : IRequest<ApiResponse<WorkerProfileResponse>>;

public sealed class GetWorkerProfileQueryHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetWorkerProfileQuery, ApiResponse<WorkerProfileResponse>>
{
    public async Task<ApiResponse<WorkerProfileResponse>> Handle(GetWorkerProfileQuery request, CancellationToken cancellationToken)
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

        return ApiResponse<WorkerProfileResponse>.Ok(response);
    }
}
