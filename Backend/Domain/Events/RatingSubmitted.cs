using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Ratings (M6) when a rating is submitted (BR-09).</summary>
public sealed record RatingSubmitted(
    long RatingId,
    long AssignmentId,
    string RaterRole,
    int Stars,
    DateTime CreatedAtUtc) : INotification;
