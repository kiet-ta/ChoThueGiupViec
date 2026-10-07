using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ratings;

/// <summary>What the Ratings module needs to know about an assignment (JOB_ASSIGNMENT is the flat shared node).</summary>
public sealed record RatingAssignmentInfo(
    long AssignmentId,
    int CustomerId,
    int WorkerId,
    JobAssignmentStatus Status,
    DateTime? CompletedAtUtc);

/// <summary>Persistence of TWO_WAY_RATING (BE-M6-01a). Reads the assignment, never changes it.</summary>
public interface IRatingRepository
{
    Task<RatingAssignmentInfo?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(long assignmentId, string raterRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the rating and returns true, or returns false (and saves nothing) when a rating of the same rater role
    /// already exists for the assignment, including when a concurrent request inserted it first (UNIQUE
    /// (assignment_id, rater_role), decision SC-5). It persists on its own so the duplicate-key error is absorbed
    /// where the database exception is visible.
    /// </summary>
    Task<bool> TryAddAsync(TwoWayRating rating, CancellationToken cancellationToken = default);
}
