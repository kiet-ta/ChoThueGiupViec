using CommonService.Application.Features.Ratings.Dtos;

namespace CommonService.Application.Features.Ratings.Services;

public interface IRatingService
{
    /// <summary>The caller's view of the rating window of one assignment; NotFoundException (404) when it is not theirs.</summary>
    Task<RatingWindowDto> GetWindowAsync(
        RaterSide side, int callerId, long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, checks the window and writes one rating. Throws ValidationException (400), NotFoundException (404) or
    /// BusinessRuleViolationException (409) as in contract ratings.md.
    /// </summary>
    Task<RatingDto> SubmitAsync(
        RaterSide side, int callerId, long assignmentId, SubmitRatingRequestDto request,
        CancellationToken cancellationToken = default);
}
