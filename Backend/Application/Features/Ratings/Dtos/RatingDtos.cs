namespace CommonService.Application.Features.Ratings.Dtos;

/// <summary>Body of the two POST .../rating endpoints (contract ratings.md section 2). Missing values become 400s.</summary>
public sealed class SubmitRatingRequestDto
{
    /// <summary>1-5. Nullable so that a missing value is reported as a validation error instead of becoming 0.</summary>
    public int? Stars { get; init; }

    /// <summary>Exactly the criteria of the rater (see <see cref="RatingCriteria"/>), each 1-5, camelCase keys.</summary>
    public Dictionary<string, int>? Criteria { get; init; }

    /// <summary>Optional, at most 500 characters (TWO_WAY_RATING.comment NVARCHAR(500)).</summary>
    public string? Comment { get; init; }
}

/// <summary>One TWO_WAY_RATING row returned to the rater who wrote it (contract ratings.md section 1).</summary>
public sealed class RatingDto
{
    public long RatingId { get; init; }
    public long AssignmentId { get; init; }

    /// <summary>CUSTOMER or WORKER.</summary>
    public string RaterRole { get; init; } = string.Empty;

    public int Stars { get; init; }

    /// <summary>camelCase keys, same as the request.</summary>
    public Dictionary<string, int> Criteria { get; init; } = [];

    public string? Comment { get; init; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Whether and until when the caller may still rate an assignment (feeds the countdown of the rating screens).</summary>
public sealed class RatingWindowDto
{
    public long AssignmentId { get; init; }
    public bool CanRate { get; init; }

    /// <summary>OPEN, NOT_COMPLETED, WINDOW_CLOSED or ALREADY_RATED. Ownership is a 404, never a reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>= completed_at; null while the assignment is not COMPLETED.</summary>
    public DateTime? OpensAt { get; init; }

    /// <summary>= completed_at + Rating.WindowHours; null while the assignment is not COMPLETED.</summary>
    public DateTime? ClosesAt { get; init; }

    /// <summary>Whole seconds left, 0 unless the window is OPEN.</summary>
    public int SecondsRemaining { get; init; }
}
