using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetWorkerByIdQuery(int WorkerId) : IRequest<ApiResponse<WorkerPublicProfileResponse>>;

public sealed class GetWorkerByIdQueryHandler(IWorkerRepository workerRepository)
    : IRequestHandler<GetWorkerByIdQuery, ApiResponse<WorkerPublicProfileResponse>>
{
    public async Task<ApiResponse<WorkerPublicProfileResponse>> Handle(GetWorkerByIdQuery request, CancellationToken cancellationToken)
    {
        var worker = await workerRepository.GetByIdAsync(request.WorkerId, cancellationToken);
        if (worker == null)
        {
            throw new NotFoundException("Worker not found.");
        }

        var response = new WorkerPublicProfileResponse(
            worker.WorkerId,
            worker.FullName,
            null,
            worker.WorkerType.ToString().ToUpperInvariant(),
            worker.WorkStatus.ToString().ToUpperInvariant(),
            worker.KycStatus,
            worker.RatingAvg,
            worker.CompletedJobs,
            worker.IsSuperFreelancer
        );

        return ApiResponse<WorkerPublicProfileResponse>.Ok(response);
    }
}
