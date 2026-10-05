using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table JOB_PHOTO. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class JobPhoto
{
    /// <summary>job_photo.photo_id BIGINT (PK)</summary>
    public long PhotoId { get; set; }

    /// <summary>job_photo.assignment_id BIGINT (FK)</summary>
    public long AssignmentId { get; set; }

    /// <summary>job_photo.photo_phase VARCHAR(10)</summary>
    public string PhotoPhase { get; set; } = string.Empty;

    /// <summary>job_photo.angle_no TINYINT</summary>
    public byte AngleNo { get; set; }

    /// <summary>job_photo.image_url NVARCHAR(500)</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>job_photo.vol_score FLOAT</summary>
    public double VolScore { get; set; }

    /// <summary>job_photo.is_accepted BIT</summary>
    public bool IsAccepted { get; set; }

    /// <summary>job_photo.captured_at DATETIME2</summary>
    public DateTime CapturedAt { get; set; }
}
