using CommonService.Application.Features.Ratings.Dtos;

namespace CommonService.Application.Features.Ratings.Services;

public interface IRatingService
{
    /// <summary>The caller's view of the rating window of one assignment; 404 when it is not theirs.</summary>
    Task<RatingResult<RatingWindowDto>> GetWindowAsync(
        RaterSide side, int callerId, long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Validates, checks the window and writes one rating; 201, 400, 404 or 409 as in contract ratings.md.</summary>
    Task<RatingResult<RatingDto>> SubmitAsync(
        RaterSide side, int callerId, long assignmentId, SubmitRatingRequestDto request,
        CancellationToken cancellationToken = default);
}
