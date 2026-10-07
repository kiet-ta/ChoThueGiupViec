using System.Text.Json;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Ratings.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Ratings.Services;

/// <summary>
/// Two-way rating rules of contract ratings.md and decisions Q14 / SC-5 (BE-M6-01a): the window opens when the
/// assignment is COMPLETED and closes Rating.WindowHours later, one rating per (assignment, rater role), fixed criteria.
/// </summary>
public sealed class RatingService : IRatingService
{
    public const int MaxCommentLength = 500;
    private const int MinScore = 1;
    private const int MaxScore = 5;

    private readonly IRatingRepository _ratings;
    private readonly IClock _clock;
    private readonly IOptions<BusinessRules> _rules;
    private readonly IPublisher _publisher;
    private readonly ILogger<RatingService> _logger;

    public RatingService(
        IRatingRepository ratings,
        IClock clock,
        IOptions<BusinessRules> rules,
        IPublisher publisher,
        ILogger<RatingService>? logger = null)
    {
        _ratings = ratings;
        _clock = clock;
        _rules = rules;
        _publisher = publisher;
        _logger = logger ?? NullLogger<RatingService>.Instance;
    }

    public async Task<RatingResult<RatingWindowDto>> GetWindowAsync(
        RaterSide side, int callerId, long assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await _ratings.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment is null || !Owns(side, callerId, assignment)) return RatingResult<RatingWindowDto>.NotFound();

        var alreadyRated = await _ratings.ExistsAsync(assignmentId, RatingCriteria.RaterRole(side), cancellationToken);
        return RatingResult<RatingWindowDto>.Ok(BuildWindow(assignment, alreadyRated));
    }

    public async Task<RatingResult<RatingDto>> SubmitAsync(
        RaterSide side, int callerId, long assignmentId, SubmitRatingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(side, request, out var stars, out var scores, out var comment);
        if (errors.Count > 0) return RatingResult<RatingDto>.ValidationError(errors);

        var assignment = await _ratings.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment is null || !Owns(side, callerId, assignment)) return RatingResult<RatingDto>.NotFound();

        var role = RatingCriteria.RaterRole(side);
        var alreadyRated = await _ratings.ExistsAsync(assignmentId, role, cancellationToken);
        var window = BuildWindow(assignment, alreadyRated);
        if (!window.CanRate) return RatingResult<RatingDto>.Conflict(window.Reason);

        var createdAt = _clock.UtcNow;
        var rating = new TwoWayRating
        {
            AssignmentId = assignmentId,
            WorkerId = assignment.WorkerId, // copied from the assignment, never taken from the caller
            RaterRole = role,
            Stars = (byte)stars,
            CriteriaJson = JsonSerializer.Serialize(
                RatingCriteria.For(side).ToDictionary(c => c.Stored, c => scores[c.Api])),
            Comment = comment,
            CreatedAt = createdAt,
        };

        // A concurrent request may have written the same (assignment, rater role) since the check above.
        if (!await _ratings.TryAddAsync(rating, cancellationToken)) return RatingResult<RatingDto>.Conflict("ALREADY_RATED");

        await PublishAsync(rating, cancellationToken);

        return RatingResult<RatingDto>.Created(new RatingDto
        {
            RatingId = rating.RatingId,
            AssignmentId = assignmentId,
            RaterRole = role,
            Stars = stars,
            Criteria = RatingCriteria.For(side).ToDictionary(c => c.Api, c => scores[c.Api]),
            Comment = comment,
            CreatedAt = createdAt,
        });
    }

    private static bool Owns(RaterSide side, int callerId, RatingAssignmentInfo assignment) =>
        side == RaterSide.Customer ? assignment.CustomerId == callerId : assignment.WorkerId == callerId;

    private RatingWindowDto BuildWindow(RatingAssignmentInfo assignment, bool alreadyRated)
    {
        if (assignment.Status != JobAssignmentStatus.Completed || assignment.CompletedAtUtc is not { } completedAt)
        {
            return new RatingWindowDto { AssignmentId = assignment.AssignmentId, CanRate = false, Reason = "NOT_COMPLETED" };
        }

        var closesAt = completedAt.AddHours(_rules.Value.Rating.WindowHours);
        var now = _clock.UtcNow;

        // "now > completed_at + window" closes it: the exact closing instant is still open (contract ratings.md 2.1).
        var reason = alreadyRated ? "ALREADY_RATED" : now > closesAt ? "WINDOW_CLOSED" : "OPEN";
        return new RatingWindowDto
        {
            AssignmentId = assignment.AssignmentId,
            CanRate = reason == "OPEN",
            Reason = reason,
            OpensAt = completedAt,
            ClosesAt = closesAt,
            SecondsRemaining = reason == "OPEN" ? (int)Math.Ceiling((closesAt - now).TotalSeconds) : 0,
        };
    }

    private static Dictionary<string, string[]> Validate(
        RaterSide side, SubmitRatingRequestDto request, out int stars, out Dictionary<string, int> scores, out string? comment)
    {
        var errors = new Dictionary<string, string[]>();
        scores = [];
        stars = 0;

        if (request.Stars is not { } givenStars || givenStars < MinScore || givenStars > MaxScore)
        {
            errors["stars"] = [$"Stars must be an integer between {MinScore} and {MaxScore}."];
        }
        else
        {
            stars = givenStars;
        }

        var expected = RatingCriteria.For(side);
        var given = request.Criteria;
        if (given is null)
        {
            errors["criteria"] = [$"Criteria are required: {string.Join(", ", expected.Select(c => c.Api))}."];
        }
        else
        {
            foreach (var (api, _) in expected)
            {
                if (!given.TryGetValue(api, out var score))
                {
                    errors[$"criteria.{api}"] = ["This criterion is required."];
                }
                else if (score < MinScore || score > MaxScore)
                {
                    errors[$"criteria.{api}"] = [$"Must be an integer between {MinScore} and {MaxScore}."];
                }
                else
                {
                    scores[api] = score;
                }
            }

            foreach (var key in given.Keys.Where(k => expected.All(c => c.Api != k)))
            {
                errors[$"criteria.{key}"] = ["Unknown criterion."];
            }
        }

        comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > MaxCommentLength })
        {
            errors["comment"] = [$"Comment must be at most {MaxCommentLength} characters."];
        }

        return errors;
    }

    /// <summary>The rating is already saved: a failing consumer of the event must not turn that into an error for the rater.</summary>
    private async Task PublishAsync(TwoWayRating rating, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(
                new RatingSubmitted(rating.RatingId, rating.AssignmentId, rating.RaterRole, rating.Stars, rating.CreatedAt),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RatingSubmitted handler failed for rating {RatingId}; the rating itself is saved.", rating.RatingId);
        }
    }
}
