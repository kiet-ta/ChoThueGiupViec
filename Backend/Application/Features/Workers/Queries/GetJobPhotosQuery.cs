using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetJobPhotosQuery(long AssignmentId) : IRequest<ApiResponse<IReadOnlyList<JobPhotoResponse>>>;

public sealed class GetJobPhotosQueryHandler(IWorkerRepository workerRepository)
    : IRequestHandler<GetJobPhotosQuery, ApiResponse<IReadOnlyList<JobPhotoResponse>>>
{
    public async Task<ApiResponse<IReadOnlyList<JobPhotoResponse>>> Handle(GetJobPhotosQuery query, CancellationToken cancellationToken)
    {
        var photos = await workerRepository.GetJobPhotosAsync(query.AssignmentId, cancellationToken);

        var responses = photos.Select(p => new JobPhotoResponse(
            p.PhotoId,
            p.AssignmentId,
            p.PhotoPhase,
            p.AngleNo,
            p.ImageUrl,
            p.VolScore,
            p.IsAccepted,
            p.CapturedAt
        )).ToList();

        return ApiResponse<IReadOnlyList<JobPhotoResponse>>.Ok(responses, "Retrieved job photos successfully.");
    }
}
