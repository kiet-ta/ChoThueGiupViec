using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetEkycStatusQuery : IRequest<ApiResponse<EkycStatusResponse>>;

public sealed class GetEkycStatusQueryHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetEkycStatusQuery, ApiResponse<EkycStatusResponse>>
{
    public async Task<ApiResponse<EkycStatusResponse>> Handle(GetEkycStatusQuery request, CancellationToken cancellationToken)
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

        var response = new EkycStatusResponse(
            worker.WorkerId,
            worker.KycStatus,
            worker.EkycConfidence,
            worker.UpdatedAt,
            null
        );

        return ApiResponse<EkycStatusResponse>.Ok(response);
    }
}
