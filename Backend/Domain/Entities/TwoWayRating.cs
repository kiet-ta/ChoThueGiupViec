using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table TWO_WAY_RATING. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class TwoWayRating
{
    /// <summary>two_way_rating.rating_id BIGINT (PK)</summary>
    public long RatingId { get; set; }

    /// <summary>two_way_rating.assignment_id BIGINT (FK)</summary>
    public long AssignmentId { get; set; }

    /// <summary>two_way_rating.worker_id INT (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>two_way_rating.rater_role VARCHAR(10)</summary>
    public string RaterRole { get; set; } = string.Empty;

    /// <summary>two_way_rating.stars TINYINT</summary>
    public byte Stars { get; set; }

    /// <summary>two_way_rating.criteria_json NVARCHAR(MAX) NULL</summary>
    public string? CriteriaJson { get; set; }

    /// <summary>two_way_rating.comment NVARCHAR(500) NULL</summary>
    public string? Comment { get; set; }

    /// <summary>two_way_rating.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
