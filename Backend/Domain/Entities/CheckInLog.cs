using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table CHECK_IN_LOG. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class CheckInLog
{
    /// <summary>check_in_log.checkin_id BIGINT (PK)</summary>
    public long CheckinId { get; set; }

    /// <summary>check_in_log.assignment_id BIGINT (FK) (UNIQUE)</summary>
    public long AssignmentId { get; set; }

    /// <summary>check_in_log.device_lat DECIMAL(9,6)</summary>
    public decimal DeviceLat { get; set; }

    /// <summary>check_in_log.device_lng DECIMAL(9,6)</summary>
    public decimal DeviceLng { get; set; }

    /// <summary>check_in_log.distance_m DECIMAL(7,2)</summary>
    public decimal DistanceM { get; set; }

    /// <summary>check_in_log.gps_verified BIT</summary>
    public bool GpsVerified { get; set; }

    /// <summary>check_in_log.fallback_method VARCHAR(20) NULL</summary>
    public string? FallbackMethod { get; set; }

    /// <summary>check_in_log.fallback_photo_url NVARCHAR(500) NULL</summary>
    public string? FallbackPhotoUrl { get; set; }

    /// <summary>check_in_log.call_attempts TINYINT</summary>
    public byte CallAttempts { get; set; }

    /// <summary>check_in_log.customer_absent_at DATETIME2 NULL</summary>
    public DateTime? CustomerAbsentAt { get; set; }

    /// <summary>check_in_log.checked_in_at DATETIME2</summary>
    public DateTime CheckedInAt { get; set; }
}
