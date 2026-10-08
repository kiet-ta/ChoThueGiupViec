using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetEkycQueueQuery(int Page = 1, int PageSize = 20) : IRequest<ApiResponse<EkycQueuePagedResponse>>;

public sealed class GetEkycQueueQueryHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetEkycQueueQuery, ApiResponse<EkycQueuePagedResponse>>
{
    public async Task<ApiResponse<EkycQueuePagedResponse>> Handle(GetEkycQueueQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.Admin)
        {
            throw new ForbiddenAccessException("Only Admin users can view the eKYC review queue.");
        }


        var (workers, totalCount) = await workerRepository.GetEkycQueueAsync(query.Page, query.PageSize, cancellationToken);

        var queueItems = workers.Select(w => new EkycQueueItemResponse(
            w.WorkerId,
            w.FullName,
            w.PhoneNumber,
            w.NationalId,
            null,
            null,
            null,
            w.EkycConfidence,
            w.KycStatus,
            w.CompletedJobs,
            w.KycStatus == "AUDIT_PENDING" || w.CompletedJobs <= 5,
            w.UpdatedAt
        )).ToList();

        var pagedResponse = new EkycQueuePagedResponse(queueItems, totalCount, query.Page < 1 ? 1 : query.Page, query.PageSize < 1 ? 20 : query.PageSize);
        return ApiResponse<EkycQueuePagedResponse>.Ok(pagedResponse, "Retrieved eKYC queue successfully.");
    }
}
